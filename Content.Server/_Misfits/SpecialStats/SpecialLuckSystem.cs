using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.GameTicking;
using Content.Shared.Random.Helpers;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Grants a small bonus item chance when a lucky player opens marked junk storage.
/// </summary>
public sealed class SpecialLuckSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;

    private static readonly Dictionary<LuckyLootRarity, float> RarityWeights = new()
    {
        [LuckyLootRarity.Common] = 100f,
        [LuckyLootRarity.Uncommon] = 45f,
        [LuckyLootRarity.Rare] = 18f,
        [LuckyLootRarity.VeryRare] = 6f,
        [LuckyLootRarity.Legendary] = 1f,
    };

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _alreadyRolled = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LuckJunkBonusComponent, ComponentShutdown>(OnLuckCompShutdown);
        SubscribeLocalEvent<LuckJunkBonusComponent, BoundUIOpenedEvent>(OnStorageOpened);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _alreadyRolled.Clear();
    }

    private void OnLuckCompShutdown(Entity<LuckJunkBonusComponent> ent, ref ComponentShutdown args)
    {
        _alreadyRolled.Remove(ent.Owner);
    }

    private void OnStorageOpened(Entity<LuckJunkBonusComponent> ent, ref BoundUIOpenedEvent args)
    {
        var actor = args.Actor;
        if (!TryComp<SpecialComponent>(actor, out var special))
            return;

        if (!_alreadyRolled.TryGetValue(ent.Owner, out var rolledSet))
        {
            rolledSet = new HashSet<EntityUid>();
            _alreadyRolled[ent.Owner] = rolledSet;
        }

        if (!rolledSet.Add(actor))
            return;

        var rollChance = _special.GetLuckRollChance(actor, 0f, ent.Comp.ChancePerLuckPoint, special);
        if (!_random.Prob(rollChance))
            return;

        if (ent.Comp.LuckyItems.Count == 0)
            return;

        if (!TryPickLuckyItem(ent.Comp, out var chosenProto))
            return;

        if (!TryComp<StorageComponent>(ent.Owner, out var storage))
            return;

        var spawned = Spawn(chosenProto, Transform(ent.Owner).Coordinates);
        if (!_storage.Insert(ent.Owner, spawned, out _, out _, actor, storage, playSound: false))
            Del(spawned);
    }

    private bool TryPickLuckyItem(LuckJunkBonusComponent component, out EntProtoId chosenProto)
    {
        var weights = new Dictionary<EntProtoId, float>();

        foreach (var entry in component.LuckyItems)
        {
            if (!RarityWeights.TryGetValue(entry.Rarity, out var weight) || weight <= 0f)
                continue;

            weights[entry.Id] = weight;
        }

        if (weights.Count == 0)
        {
            chosenProto = default;
            return false;
        }

        chosenProto = _random.Pick(weights);
        return true;
    }
}
