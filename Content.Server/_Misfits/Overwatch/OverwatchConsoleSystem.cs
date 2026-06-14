using System;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server._Misfits.Holotape;
using Content.Shared.Access.Components;
using Content.Shared.DeltaV.NanoChat;
using Content.Shared.Damage;
using Content.Shared._Misfits.Holotape;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Content.Shared._Misfits.Overwatch;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Overwatch;

public sealed class OverwatchConsoleSystem : EntitySystem
{
    [Dependency] private readonly HolotapeSystem _holotape = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly ProtoId<OverwatchCategoryPrototype> GeneralCategoryId = "OverwatchGeneral";
    private static readonly ProtoId<OverwatchCategoryPrototype> UnassignedCategoryId = "OverwatchUnassigned";

    private const float UpdateInterval = 0.5f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
        SubscribeLocalEvent<OverwatchConsoleComponent, ComponentShutdown>(OnShutdown);
        Subs.BuiEvents<OverwatchConsoleComponent>(HolotapeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<BoundUIClosedEvent>(OnClosed);
            subs.Event<OverwatchConsoleMessage>(OnMessage);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<OverwatchConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.UiOpen && comp.WatchedNumber == null)
                continue;

            ValidateWatch((uid, comp));

            if (comp.UiOpen)
                RefreshUi((uid, comp));
        }
    }

    private void OnOpened(Entity<OverwatchConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        ent.Comp.UiOpen = true;
        ent.Comp.UiActor = args.Actor;
        RefreshUi(ent);
    }

    private void OnClosed(Entity<OverwatchConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        ent.Comp.UiOpen = false;
        ent.Comp.UiActor = null;
        StopWatching(ent);
    }

    private void OnShutdown(Entity<OverwatchConsoleComponent> ent, ref ComponentShutdown args)
    {
        StopWatching(ent);
    }

    private void OnMessage(Entity<OverwatchConsoleComponent> ent, ref OverwatchConsoleMessage args)
    {
        switch (args.Type)
        {
            case OverwatchConsoleMessageType.Watch:
                if (args.TargetNumber != null)
                    StartWatching(ent, args.Actor, args.TargetNumber.Value);
                break;
            case OverwatchConsoleMessageType.Unwatch:
                StopWatching(ent);
                break;
        }

        ValidateWatch(ent);
        RefreshUi(ent);
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        var sourceCoordinates = Transform(ev.Source).Coordinates;

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } watcher ||
                !TryComp<OverwatchWatchingComponent>(watcher, out var watching) ||
                watching.Watching is not { } watched ||
                Deleted(watched))
            {
                continue;
            }

            var watchedCoordinates = Transform(watched).Coordinates;
            if (!sourceCoordinates.TryDistance(EntityManager, watchedCoordinates, out var distance) ||
                distance > ev.VoiceRange)
            {
                continue;
            }

            ev.Recipients.TryAdd(session, new ChatSystem.ICChatRecipientData(distance, false, true));
        }
    }

    private void StartWatching(Entity<OverwatchConsoleComponent> ent, EntityUid actor, uint targetNumber)
    {
        if (!TryGetWatchTarget(ent.Comp, targetNumber, out var watchedEntity))
        {
            StopWatching(ent);
            return;
        }

        var watching = EnsureComp<OverwatchWatchingComponent>(actor);
        watching.Watching = watchedEntity;
        Dirty(actor, watching);

        if (ent.Comp.WatchingActor != actor || ent.Comp.WatchedEntity != watchedEntity)
        {
            RemoveWatchViewSubscription(ent.Comp.WatchingActor, ent.Comp.WatchedEntity);
            AddWatchViewSubscription(actor, watchedEntity);
        }

        ent.Comp.WatchingActor = actor;
        ent.Comp.WatchedEntity = watchedEntity;
        ent.Comp.WatchedNumber = targetNumber;
        UpdateLastKnown(ent.Comp, watchedEntity);
    }

    private void StopWatching(Entity<OverwatchConsoleComponent> ent)
    {
        ClearLiveWatch(ent.Comp);
        ent.Comp.WatchedNumber = null;
        ent.Comp.LastKnownName = null;
        ent.Comp.LastKnownX = null;
        ent.Comp.LastKnownY = null;
        ent.Comp.LastKnownTimestamp = null;
    }

    private void ValidateWatch(Entity<OverwatchConsoleComponent> ent)
    {
        if (ent.Comp.WatchedNumber == null)
            return;

        if (ent.Comp.WatchingActor == null ||
            Deleted(ent.Comp.WatchingActor.Value))
        {
            StopWatching(ent);
            return;
        }

        if (!TryGetWatchTarget(ent.Comp, ent.Comp.WatchedNumber.Value, out var watchedEntity))
        {
            SuspendWatching(ent.Comp);
            return;
        }

        if (ent.Comp.WatchedEntity != watchedEntity)
        {
            RemoveWatchViewSubscription(ent.Comp.WatchingActor, ent.Comp.WatchedEntity);
            AddWatchViewSubscription(ent.Comp.WatchingActor, watchedEntity);
        }

        ent.Comp.WatchedEntity = watchedEntity;
        UpdateLastKnown(ent.Comp, watchedEntity);

        var watching = EnsureComp<OverwatchWatchingComponent>(ent.Comp.WatchingActor.Value);
        watching.Watching = watchedEntity;
        Dirty(ent.Comp.WatchingActor.Value, watching);
    }

    private void RefreshUi(Entity<OverwatchConsoleComponent> ent)
    {
        if (ent.Comp.UiActor == null || Deleted(ent.Comp.UiActor.Value))
            return;

        _holotape.RefreshTerminalState(ent.Owner, ent.Comp.UiActor.Value);
    }

    public OverwatchConsoleState? BuildUiState(EntityUid uid, OverwatchConsoleComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return null;

        var personnel = GetPersonnelEntries(comp, comp.WatchedNumber);
        string? watchedName = null;

        if (comp.WatchedNumber != null)
        {
            watchedName = personnel.FirstOrDefault(entry => entry.Number == comp.WatchedNumber.Value).Name
                ?? comp.LastKnownName;
        }

        return new OverwatchConsoleState(
            Loc.GetString(comp.MonitorTitle),
            comp.WatchedNumber,
            watchedName,
            GetNetEntity(comp.WatchedEntity),
            comp.LastKnownX,
            comp.LastKnownY,
            comp.LastKnownTimestamp,
            personnel);
    }

    private List<OverwatchConsoleEntry> GetPersonnelEntries(OverwatchConsoleComponent comp, uint? watchedNumber)
    {
        var entries = new List<OverwatchConsoleEntry>();
        var query = EntityQueryEnumerator<NanoChatCardComponent, IdCardComponent>();

        while (query.MoveNext(out var uid, out var nanoChat, out var idCard))
        {
            if (nanoChat.Number == null ||
                nanoChat.PdaUid == null ||
                !MatchesTrackedPersonnel(comp, uid))
            {
                continue;
            }

            if (!TryResolvePersonnelTarget(nanoChat.PdaUid.Value, out var personnelEntity))
                continue;

            var position = Transform(personnelEntity).WorldPosition;
            var (health, state) = GetPersonnelHealth(personnelEntity);
            var category = ResolveCategory(idCard);
            entries.Add(new OverwatchConsoleEntry(
                nanoChat.Number.Value,
                idCard.FullName ?? "Unknown",
                idCard.LocalizedJobTitle,
                category.Name,
                category.SortOrder,
                health,
                state,
                position.X,
                position.Y,
                watchedNumber == nanoChat.Number.Value));
        }

        entries.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private bool TryGetWatchTarget(OverwatchConsoleComponent comp, uint targetNumber, out EntityUid target)
    {
        target = default;

        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var nanoChat))
        {
            if (nanoChat.Number != targetNumber ||
                nanoChat.PdaUid == null ||
                !MatchesTrackedPersonnel(comp, uid))
            {
                continue;
            }

            return TryResolvePersonnelTarget(nanoChat.PdaUid.Value, out target);
        }

        return false;
    }

    private bool MatchesTrackedPersonnel(OverwatchConsoleComponent comp, EntityUid trackedEntity)
    {
        if (comp.TrackedTags.Count == 0)
            return false;

        foreach (var tag in comp.TrackedTags)
        {
            if (_tag.HasTag(trackedEntity, tag))
                return true;
        }

        return false;
    }

    private (string Name, int SortOrder) ResolveCategory(IdCardComponent idCard)
    {
        if (idCard.JobPrototype != null &&
            _prototype.TryIndex(idCard.JobPrototype.Value, out JobPrototype? job) &&
            job.OverwatchCategory != null &&
            _prototype.TryIndex(job.OverwatchCategory.Value, out OverwatchCategoryPrototype? category))
        {
            return (category.LocalizedName, category.SortOrder);
        }

        var fallbackId = idCard.JobPrototype != null ? GeneralCategoryId : UnassignedCategoryId;
        if (_prototype.TryIndex(fallbackId, out OverwatchCategoryPrototype? fallback))
            return (fallback.LocalizedName, fallback.SortOrder);

        return (idCard.JobPrototype != null ? "GENERAL" : "UNASSIGNED", int.MaxValue);
    }

    private (float Health, MobState State) GetPersonnelHealth(EntityUid target)
    {
        var state = TryComp<MobStateComponent>(target, out var mobState)
            ? mobState.CurrentState
            : MobState.Alive;

        if (state == MobState.Dead)
            return (0f, state);

        if (!TryComp<DamageableComponent>(target, out var damageable))
            return (1f, state);

        if (!_mobThreshold.TryGetDeadThreshold(target, out var deadThreshold) &&
            !_mobThreshold.TryGetIncapThreshold(target, out deadThreshold))
        {
            return (1f, state);
        }

        var threshold = deadThreshold.Value.Float();
        if (threshold <= 0f)
            return (1f, state);

        var health = Math.Clamp(1f - damageable.TotalDamage.Float() / threshold, 0f, 1f);
        return (health, state);
    }

    private bool TryResolvePersonnelTarget(EntityUid pdaUid, out EntityUid target)
    {
        target = pdaUid;
        var current = pdaUid;

        while (_container.TryGetContainingContainer((current, null, null), out var container))
        {
            current = container.Owner;
            target = current;
        }

        if (target == pdaUid || Deleted(target))
            return false;

        return HasComp<MobStateComponent>(target) || HasComp<ActorComponent>(target);
    }

    private void AddWatchViewSubscription(EntityUid? actor, EntityUid? watched)
    {
        if (actor == null ||
            watched == null ||
            Deleted(actor.Value) ||
            Deleted(watched.Value) ||
            !TryComp(actor.Value, out ActorComponent? actorComp))
        {
            return;
        }

        _viewSubscriber.AddViewSubscriber(watched.Value, actorComp.PlayerSession);
    }

    private void RemoveWatchViewSubscription(EntityUid? actor, EntityUid? watched)
    {
        if (actor == null ||
            watched == null ||
            Deleted(actor.Value) ||
            Deleted(watched.Value) ||
            !TryComp(actor.Value, out ActorComponent? actorComp))
        {
            return;
        }

        _viewSubscriber.RemoveViewSubscriber(watched.Value, actorComp.PlayerSession);
    }

    private void ClearLiveWatch(OverwatchConsoleComponent comp, bool clearActor = true)
    {
        RemoveWatchViewSubscription(comp.WatchingActor, comp.WatchedEntity);

        if (comp.WatchingActor != null)
            RemComp<OverwatchWatchingComponent>(comp.WatchingActor.Value);

        if (clearActor)
            comp.WatchingActor = null;

        comp.WatchedEntity = null;
    }

    private void SuspendWatching(OverwatchConsoleComponent comp)
    {
        ClearLiveWatch(comp, false);
    }

    private void UpdateLastKnown(OverwatchConsoleComponent comp, EntityUid watchedEntity)
    {
        var position = Transform(watchedEntity).WorldPosition;
        comp.LastKnownName = MetaData(watchedEntity).EntityName;
        comp.LastKnownX = position.X;
        comp.LastKnownY = position.Y;
        comp.LastKnownTimestamp = _timing.CurTime.ToString(@"hh\:mm\:ss");
    }
}
