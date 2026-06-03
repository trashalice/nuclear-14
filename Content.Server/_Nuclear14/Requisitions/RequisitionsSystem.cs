using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Cargo.Components;
using Content.Server.Chat.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Misfits.Currency.Components;
using Content.Shared._Nuclear14.Requisitions;
using Content.Shared._Nuclear14.Requisitions.Components;
using Content.Shared.Chasm;
using Content.Shared.Chat;
using Content.Shared.Coordinates;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using static Content.Shared._Nuclear14.Requisitions.Components.RequisitionsElevatorMode;

namespace Content.Server._Nuclear14.Requisitions;

public sealed partial class RequisitionsSystem : SharedRequisitionsSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogs = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ChasmSystem _chasm = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly EntProtoId PaperRequisitionInvoice = "N14PaperRequisitionInvoice";

    private EntityQuery<ChasmComponent> _chasmQuery;
    private EntityQuery<ChasmFallingComponent> _chasmFallingQuery;

    private readonly HashSet<Entity<MobStateComponent>> _toPit = new();

    public override void Initialize()
    {
        base.Initialize();

        _chasmQuery = GetEntityQuery<ChasmComponent>();
        _chasmFallingQuery = GetEntityQuery<ChasmFallingComponent>();

        SubscribeLocalEvent<RequisitionsComputerComponent, MapInitEvent>(OnComputerMapInit);
        SubscribeLocalEvent<RequisitionsComputerComponent, BeforeActivatableUIOpenEvent>(OnComputerBeforeActivatableUIOpen);
        SubscribeLocalEvent<RequisitionsComputerComponent, InteractUsingEvent>(OnComputerInteractUsing);

        Subs.BuiEvents<RequisitionsComputerComponent>(N14RequisitionsUIKey.Key, subs =>
        {
            subs.Event<RequisitionsBuyMsg>(OnBuy);
            subs.Event<RequisitionsBuyCartMsg>(OnBuyCart);
            subs.Event<RequisitionsPlatformMsg>(OnPlatform);
        });
    }

    private void OnComputerMapInit(Entity<RequisitionsComputerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Account = GetAccount(ent.Comp.Group, ent.Comp.AccountProto);
        Dirty(ent);
    }

    private void OnComputerBeforeActivatableUIOpen(Entity<RequisitionsComputerComponent> computer, ref BeforeActivatableUIOpenEvent args)
    {
        SetUILastInteracted(computer);
        SendUIState(computer);
    }

    private void OnComputerInteractUsing(Entity<RequisitionsComputerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.AcceptedCurrency is not { } accepted)
            return;

        if (!TryComp(args.Used, out ConsumableCurrencyComponent? currency) || currency.CurrencyType != accepted)
            return;

        if (ent.Comp.Account is not { } accountUid || !TryComp(accountUid, out RequisitionsAccountComponent? account))
            return;

        var count = TryComp(args.Used, out StackComponent? stack) ? stack.Count : 1;
        var value = currency.ValuePerUnit * count;
        if (value <= 0)
            return;

        account.Balance += value;
        Dirty(accountUid, account);
        QueueDel(args.Used);

        _audio.PlayPvs(ent.Comp.IncomingSurplus, ent);
        _popup.PopupEntity(Loc.GetString("rmc-requisitions-deposit", ("amount", value)), ent, args.User);
        SendUIStateAll();

        _adminLogs.Add(LogType.Action, $"{ToPrettyString(args.User):actor} deposited {value} into requisitions account {ent.Comp.Group}");
        args.Handled = true;
    }

    private void OnBuy(Entity<RequisitionsComputerComponent> computer, ref RequisitionsBuyMsg args)
    {
        var items = new List<RequisitionsCartItem>
        {
            new(args.Category, args.Order, 1),
        };

        TryBuyCart(computer, args.Actor, items);
    }

    private void OnBuyCart(Entity<RequisitionsComputerComponent> computer, ref RequisitionsBuyCartMsg args)
    {
        TryBuyCart(computer, args.Actor, args.Items);
    }

    private void TryBuyCart(Entity<RequisitionsComputerComponent> computer, EntityUid actor, List<RequisitionsCartItem> items)
    {
        if (items.Count == 0)
            return;

        if (GetElevator(computer) is not { } elevator)
            return;

        var remainingCapacity = GetElevatorCapacity(elevator) - elevator.Comp.Orders.Count;
        if (remainingCapacity <= 0)
            return;

        if (computer.Comp.Account is not { } accountUid ||
            !TryComp(accountUid, out RequisitionsAccountComponent? account))
        {
            return;
        }

        var orders = new List<RequisitionsEntry>();
        var totalCost = 0;
        var totalAmount = 0;
        var pendingStock = new Dictionary<string, int>();
        var history = new List<RequisitionsHistoryEntry>();
        var buyerName = Name(actor);

        foreach (var item in items)
        {
            if (item.Amount <= 0)
                return;

            if (item.Category < 0 || item.Category >= computer.Comp.Categories.Count)
            {
                Log.Error($"Player {ToPrettyString(actor)} tried to buy out of bounds requisitions order: category {item.Category}");
                return;
            }

            var category = computer.Comp.Categories[item.Category];
            if (item.Order < 0 || item.Order >= category.Entries.Count)
            {
                Log.Error($"Player {ToPrettyString(actor)} tried to buy out of bounds requisitions order: category {item.Category}, order {item.Order}");
                return;
            }

            var order = category.Entries[item.Order];

            if (order.Stock >= 0)
            {
                var crateId = order.Crate.Id;
                var alreadyBought = account.Purchased.GetValueOrDefault(crateId) + pendingStock.GetValueOrDefault(crateId);
                if (alreadyBought + item.Amount > order.Stock)
                    return;

                pendingStock[crateId] = pendingStock.GetValueOrDefault(crateId) + item.Amount;
            }

            if (order.Cost > 0 && item.Amount > (int.MaxValue - totalCost) / order.Cost)
                return;

            if (item.Amount > int.MaxValue - totalAmount)
                return;

            totalCost += order.Cost * item.Amount;
            totalAmount += item.Amount;

            if (totalAmount > remainingCapacity)
                return;

            history.Add(new RequisitionsHistoryEntry(buyerName, order.Crate.Id, item.Amount, order.Cost * item.Amount));

            for (var i = 0; i < item.Amount; i++)
            {
                orders.Add(order);
            }
        }

        if (account.Balance < totalCost)
            return;

        account.Balance -= totalCost;
        foreach (var (crateId, amount) in pendingStock)
        {
            account.Purchased[crateId] = account.Purchased.GetValueOrDefault(crateId) + amount;
        }

        history.Reverse();
        account.History.InsertRange(0, history);
        if (account.History.Count > 30)
            account.History.RemoveRange(30, account.History.Count - 30);

        elevator.Comp.Orders.AddRange(orders);
        Dirty(accountUid, account);
        Dirty(elevator);

        _audio.PlayPvs(computer.Comp.IncomingSurplus, computer);
        SendUIStateAll();
        _adminLogs.Add(LogType.Action, $"{ToPrettyString(actor):actor} bought {totalAmount} requisitions crate(s) for {totalCost}");
    }

    private void OnPlatform(Entity<RequisitionsComputerComponent> computer, ref RequisitionsPlatformMsg args)
    {
        if (GetElevator(computer) is not { } elevator)
            return;

        var comp = elevator.Comp;
        if (comp.NextMode != null || comp.Busy)
            return;

        if (comp.Mode == Lowering || comp.Mode == Raising)
            return;

        if (args.Raise && comp.Mode == Raised)
            return;

        if (!args.Raise && comp.Mode == Lowered)
            return;

        RequisitionsElevatorMode? nextMode = comp.Mode switch
        {
            Lowered => Raising,
            Raised => Lowering,
            _ => null
        };

        if (nextMode == null)
            return;

        if (nextMode == Lowering)
        {
            var mask = (int) (CollisionGroup.MobLayer | CollisionGroup.MobMask);
            foreach (var entity in _physics.GetEntitiesIntersectingBody(elevator, mask, false))
            {
                if (HasComp<MobStateComponent>(entity))
                    return;
            }
        }

        comp.ToggledAt = _timing.CurTime;
        comp.Busy = true;
        SetMode(elevator, Preparing, nextMode);
        Dirty(elevator);
    }

    private Entity<RequisitionsAccountComponent> GetAccount(string group, EntProtoId proto)
    {
        var query = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            if (account.Group == group)
                return (uid, account);
        }

        var newAccount = Spawn(proto, MapCoordinates.Nullspace);
        var newAccountComp = EnsureComp<RequisitionsAccountComponent>(newAccount);
        newAccountComp.Group = group;

        if (!newAccountComp.Started)
        {
            newAccountComp.Started = true;
            newAccountComp.Balance = newAccountComp.StartingBalance;
            newAccountComp.NextGain = _timing.CurTime + newAccountComp.GainEvery;
        }

        Dirty(newAccount, newAccountComp);
        return (newAccount, newAccountComp);
    }

    private void UpdateRailings(Entity<RequisitionsElevatorComponent> elevator, RequisitionsRailingMode mode)
    {
        var coordinates = _transform.GetMapCoordinates(elevator);
        var railings = _lookup.GetEntitiesInRange<RequisitionsRailingComponent>(coordinates, elevator.Comp.Radius + 5);
        foreach (var railing in railings)
        {
            SetRailingMode(railing, mode);
        }
    }

    private void UpdateGears(Entity<RequisitionsElevatorComponent> elevator, RequisitionsGearMode mode)
    {
        var coordinates = _transform.GetMapCoordinates(elevator);
        var railings = _lookup.GetEntitiesInRange<RequisitionsGearComponent>(coordinates, elevator.Comp.Radius + 5);
        foreach (var railing in railings)
        {
            if (railing.Comp.Mode == mode)
                continue;

            railing.Comp.Mode = mode;
            Dirty(railing);
        }
    }

    private void SendUIFeedback(Entity<RequisitionsComputerComponent> computerEnt, string flavorText)
    {
        if (!TryComp(computerEnt, out RequisitionsComputerComponent? computerComp))
            return;

        _chatSystem.TrySendInGameICMessage(computerEnt,
            flavorText,
            InGameICChatType.Speak,
            ChatTransmitRange.GhostRangeLimit,
            nameOverride: Loc.GetString("requisition-paperwork-receiver-name"));

        _audio.PlayPvs(computerComp.IncomingSurplus, computerEnt);
    }

    private void SendUIFeedback(string group, string flavorText)
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out var uid, out var computer))
        {
            if (computer.Group == group && computer.IsLastInteracted)
                SendUIFeedback((uid, computer), flavorText);
        }
    }

    private void SetUILastInteracted(Entity<RequisitionsComputerComponent> computerEnt)
    {
        if (!TryComp(computerEnt, out RequisitionsComputerComponent? selectedComputer))
            return;

        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out _, out var otherComputer))
        {
            if (otherComputer.Group == selectedComputer.Group)
                otherComputer.IsLastInteracted = false;
        }

        selectedComputer.IsLastInteracted = true;
    }

    private void TryPlayAudio(Entity<RequisitionsElevatorComponent> elevator)
    {
        var comp = elevator.Comp;
        if (comp.Audio != null)
            return;

        var time = _timing.CurTime;
        if (comp.NextMode == Lowering || comp.Mode == Lowering)
        {
            if (time < comp.ToggledAt + comp.LowerSoundDelay)
                return;

            comp.Audio = _audio.PlayPvs(comp.LoweringSound, elevator)?.Entity;
            return;
        }

        if (comp.NextMode == Raising || comp.Mode == Raising)
        {
            if (time < comp.ToggledAt + comp.RaiseSoundDelay)
                return;

            comp.Audio = _audio.PlayPvs(comp.RaisingSound, elevator)?.Entity;
        }
    }

    private void SetMode(Entity<RequisitionsElevatorComponent> elevator, RequisitionsElevatorMode mode, RequisitionsElevatorMode? nextMode)
    {
        elevator.Comp.Mode = mode;
        elevator.Comp.NextMode = nextMode;
        Dirty(elevator);

        RequisitionsGearMode? gearMode = mode switch
        {
            Lowered or Raised or Preparing => RequisitionsGearMode.Static,
            Lowering or Raising => RequisitionsGearMode.Moving,
            _ => null
        };

        if (gearMode != null)
            UpdateGears(elevator, gearMode.Value);

        RequisitionsRailingMode? railingMode = (mode, nextMode) switch
        {
            (Lowered, _) => RequisitionsRailingMode.Raised,
            (Raised, _) => RequisitionsRailingMode.Lowering,
            (_, Lowering) => RequisitionsRailingMode.Raising,
            _ => null
        };

        if (railingMode != null)
            UpdateRailings(elevator, railingMode.Value);

        SendUIStateAll();
    }

    private void SpawnOrders(Entity<RequisitionsElevatorComponent> elevator)
    {
        var comp = elevator.Comp;
        if (comp.Mode == Raised)
        {
            var coordinates = _transform.GetMoverCoordinates(elevator);
            var xOffset = comp.Radius;
            var yOffset = comp.Radius;
            int remainingDeliveries = GetElevatorCapacity(elevator);
            foreach (var order in comp.Orders)
            {
                var crate = SpawnAtPosition(order.Crate, coordinates.Offset(new Vector2(xOffset, yOffset)));
                remainingDeliveries--;

                foreach (var prototype in order.Entities)
                {
                    var entity = Spawn(prototype, MapCoordinates.Nullspace);
                    _entityStorage.Insert(entity, crate);
                }

                PrintInvoice(crate, coordinates, PaperRequisitionInvoice);

                yOffset--;
                if (yOffset < -comp.Radius)
                {
                    yOffset = comp.Radius;
                    xOffset--;
                }

                if (xOffset < -comp.Radius)
                    xOffset = comp.Radius;
            }

            comp.Orders.Clear();

            var query = EntityQueryEnumerator<RequisitionsCustomDeliveryComponent>();

            while (query.MoveNext(out var entityUid, out _))
            {
                // If elevator is full, abort and break out of the loop. Any remaining custom deliveries will be on
                // the next elevator shipment.
                if (remainingDeliveries <= 0)
                    break;

                // Remove the component so it doesn't get "delivered" again next elevator cycle.
                RemCompDeferred<RequisitionsCustomDeliveryComponent>(entityUid);

                // Teleport to the spot.
                _transform.SetCoordinates(entityUid, coordinates.Offset(new Vector2(xOffset, yOffset)));
                remainingDeliveries--; // Decrement available delivery slots count.

                // Update the next spot to teleport to.
                yOffset--;
                if (yOffset < -comp.Radius)
                {
                    yOffset = comp.Radius;
                    xOffset--;
                }

                if (xOffset < -comp.Radius)
                    xOffset = comp.Radius;
            }
        }
    }

    private bool Sell(Entity<RequisitionsElevatorComponent> elevator)
    {
        var account = GetAccount(elevator.Comp.Group, elevator.Comp.AccountProto);
        var sellEntries = GetSellEntries(elevator.Comp.Group);
        var coordinates = _transform.GetMoverCoordinates(elevator);
        var entities = _lookup.GetEntitiesIntersecting(elevator);
        var soldAny = false;
        var rewards = 0;
        var exchanged = new List<EntProtoId>();
        var delivered = new Dictionary<string, int>();
        foreach (var entity in entities)
        {
            if (entity == elevator.Comp.Audio)
                continue;

            if (HasComp<CargoSellBlacklistComponent>(entity))
                continue;

            if (HasComp<MobStateComponent>(entity))
                continue;

            if (MetaData(entity).EntityPrototype?.ID is { } proto)
            {
                var delivCount = TryComp(entity, out StackComponent? stack) ? stack.Count : 1;
                delivered[proto] = delivered.GetValueOrDefault(proto) + delivCount;
            }

            if (TryGetSellEntry(sellEntries, entity, out var sellEntry))
            {
                rewards += sellEntry.Value;
                exchanged.AddRange(sellEntry.Exchange);
                soldAny = true;
                QueueDel(entity);
                continue;
            }

            rewards += SubmitInvoices(entity);

            if (TryComp(entity, out RequisitionsCrateComponent? crate))
            {
                rewards += crate.Reward;
                soldAny = true;
            }

            QueueDel(entity);
        }

        rewards += CompleteBounties(elevator.Comp.Group, account, delivered);

        foreach (var proto in exchanged)
        {
            SpawnAtPosition(proto, coordinates);
        }

        if (rewards > 0)
            SendUIFeedback(elevator.Comp.Group, Loc.GetString("requisition-paperwork-reward-message", ("amount", rewards)));

        account.Comp.Balance += rewards;

        if (soldAny || rewards > 0)
            Dirty(account);

        return soldAny;
    }

    private int CompleteBounties(string group, Entity<RequisitionsAccountComponent> account, Dictionary<string, int> delivered)
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        List<RequisitionsBounty>? bounties = null;
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Group != group)
                continue;

            bounties = comp.Bounties;
            break;
        }

        if (bounties == null || bounties.Count == 0)
            return 0;

        var reward = 0;
        foreach (var bounty in bounties)
        {
            if (account.Comp.CompletedBounties.Contains(bounty.Id))
                continue;

            if (delivered.GetValueOrDefault(bounty.Item.Id) < bounty.Amount)
                continue;

            reward += bounty.Reward;
            account.Comp.CompletedBounties.Add(bounty.Id);
        }

        return reward;
    }

    protected override int AppraisePlatform(Entity<RequisitionsElevatorComponent> elevator, out int count)
    {
        count = 0;
        var value = 0;
        var sellEntries = GetSellEntries(elevator.Comp.Group);
        foreach (var entity in _lookup.GetEntitiesIntersecting(elevator))
        {
            if (entity == elevator.Comp.Audio ||
                HasComp<CargoSellBlacklistComponent>(entity) ||
                HasComp<MobStateComponent>(entity))
            {
                continue;
            }

            if (TryGetSellEntry(sellEntries, entity, out var sellEntry))
            {
                value += sellEntry.Value;
                count++;
                continue;
            }

            var entityValue = SubmitInvoices(entity);
            if (TryComp(entity, out RequisitionsCrateComponent? crate))
                entityValue += crate.Reward;

            if (entityValue <= 0)
                continue;

            value += entityValue;
            count++;
        }

        return value;
    }

    private List<RequisitionsSellEntry> GetSellEntries(string group)
    {
        var query = EntityQueryEnumerator<RequisitionsComputerComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Group == group)
                return comp.SellEntries;
        }

        return new List<RequisitionsSellEntry>();
    }

    private bool TryGetSellEntry(List<RequisitionsSellEntry> entries, EntityUid entity, out RequisitionsSellEntry entry)
    {
        entry = default!;
        if (entries.Count == 0)
            return false;

        var proto = MetaData(entity).EntityPrototype?.ID;
        if (proto == null)
            return false;

        foreach (var candidate in entries)
        {
            if (candidate.Item.Id == proto)
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var time = _timing.CurTime;
        var updateUI = false;
        var accounts = EntityQueryEnumerator<RequisitionsAccountComponent>();
        while (accounts.MoveNext(out var uid, out var account))
        {
            if (account.Gain <= 0)
                continue;

            if (time > account.NextGain)
            {
                account.NextGain = time + account.GainEvery;
                account.Balance += account.Gain;
                Dirty(uid, account);

                updateUI = true;
            }
        }

        var elevators = EntityQueryEnumerator<RequisitionsElevatorComponent>();
        while (elevators.MoveNext(out var uid, out var elevator))
        {
            if (ProcessElevator((uid, elevator)))
                updateUI = true;

            if (!_chasmQuery.TryComp(uid, out var chasm))
                continue;

            if (time < elevator.NextChasmCheck)
                continue;

            elevator.NextChasmCheck = time + elevator.ChasmCheckEvery;

            if (_net.IsClient)
                continue;

            if (elevator.Mode != Raised && elevator.Mode != Preparing)
            {
                _toPit.Clear();
                _lookup.GetEntitiesInRange(uid.ToCoordinates(), elevator.Radius + 0.25f, _toPit);

                foreach (var toPit in _toPit)
                {
                    if (_chasmFallingQuery.HasComp(toPit))
                        continue;

                    _chasm.StartFalling(uid, chasm, toPit);
                    _audio.PlayEntity(chasm.FallingSound, toPit, uid);
                }
            }
        }

        if (updateUI)
            SendUIStateAll();
    }

    private bool ProcessElevator(Entity<RequisitionsElevatorComponent> ent)
    {
        var time = _timing.CurTime;
        var elevator = ent.Comp;
        if (time > elevator.ToggledAt + elevator.ToggleDelay)
        {
            elevator.ToggledAt = null;
            elevator.Busy = false;
            Dirty(ent);
            SendUIStateAll();
            return false;
        }

        if (elevator.ToggledAt == null)
            return false;

        TryPlayAudio(ent);

        var delay = elevator.NextMode == Raising ? elevator.RaiseDelay : elevator.LowerDelay;
        if (elevator.Mode == Preparing &&
            elevator.NextMode != null &&
            time > elevator.ToggledAt + delay)
        {
            SetMode(ent, elevator.NextMode.Value, null);
            return false;
        }

        if (elevator.Mode != Lowering && elevator.Mode != Raising)
            return false;

        var startDelay = delay + elevator.NextMode switch
        {
            Lowering => elevator.LowerDelay,
            Raising => elevator.RaiseDelay,
            _ => TimeSpan.Zero,
        };

        var moveDelay = startDelay + elevator.Mode switch
        {
            Lowering => elevator.LowerDelay,
            Raising => elevator.RaiseDelay,
            _ => TimeSpan.Zero,
        };

        if (time > elevator.ToggledAt + moveDelay)
        {
            elevator.Audio = null;

            var mode = elevator.Mode switch
            {
                Raising => Raised,
                Lowering => Lowered,
                _ => elevator.Mode,
            };
            SetMode(ent, mode, elevator.NextMode);

            SpawnOrders(ent);

            return true;
        }

        if (elevator.Mode == Lowering &&
            time > elevator.ToggledAt + delay)
        {
            if (Sell(ent))
                return true;
        }

        return false;
    }
}
