using Content.Server._Misfits.SpecialStats.Components;
using Content.Server._N14.Special.Speech.Components;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Clumsy;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Applies negative SPECIAL side effects that are represented by components.
/// </summary>
public sealed class SpecialPenaltySystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    private const int LowIntelligenceThreshold = 2;
    private const int ClumsyLuckThreshold = 4;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args)
    {
        if (TryComp<SpecialComponent>(args.ChangedEntity, out var special))
            ApplyPenalties((args.ChangedEntity, special));
    }

    private void OnStatsReady(ref SpecialStatsReadyEvent args)
    {
        if (TryComp<SpecialComponent>(args.Entity, out var special))
            ApplyPenalties((args.Entity, special));
    }

    private void OnSpecialShutdown(ref SpecialShutdownEvent args)
    {
        ClearLowIntelligence(args.Entity);
        ClearLuckClumsy(args.Entity);
    }

    private void ApplyPenalties(Entity<SpecialComponent> ent)
    {
        if (_special.GetEffective(ent.Owner, SpecialStat.Intelligence, ent.Comp) < LowIntelligenceThreshold)
            ApplyLowIntelligence(ent.Owner);
        else
            ClearLowIntelligence(ent.Owner);

        if (_special.GetEffective(ent.Owner, SpecialStat.Luck, ent.Comp) < ClumsyLuckThreshold)
            ApplyLuckClumsy(ent.Owner);
        else
            ClearLuckClumsy(ent.Owner);
    }

    private void ApplyLowIntelligence(EntityUid uid)
    {
        EnsureComp<LowIntelligenceAccentComponent>(uid);
    }

    private void ClearLowIntelligence(EntityUid uid)
    {
        RemComp<LowIntelligenceAccentComponent>(uid);
    }

    private void ApplyLuckClumsy(EntityUid uid)
    {
        EnsureComp<ClumsyComponent>(uid);
        EnsureComp<SpecialAppliedClumsyComponent>(uid);
    }

    private void ClearLuckClumsy(EntityUid uid)
    {
        if (!HasComp<SpecialAppliedClumsyComponent>(uid))
            return;

        RemComp<SpecialAppliedClumsyComponent>(uid);
        RemComp<ClumsyComponent>(uid);
    }
}
