using Content.Server._Misfits.SpecialStats.Components;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Overlays;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Grants a medical HUD effect to characters with maximum Intelligence.
/// </summary>
public sealed class SpecialIntelligenceHudSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    private const int MedicalHudIntelligenceThreshold = 10;
    private const string BiologicalDamageContainer = "Biological";

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
            ApplyMedicalHud((args.ChangedEntity, special));
    }

    private void OnStatsReady(ref SpecialStatsReadyEvent args)
    {
        if (TryComp<SpecialComponent>(args.Entity, out var special))
            ApplyMedicalHud((args.Entity, special));
    }

    private void OnSpecialShutdown(ref SpecialShutdownEvent args)
    {
        ClearMedicalHud(args.Entity);
    }

    private void ApplyMedicalHud(Entity<SpecialComponent> ent)
    {
        if (_special.GetEffective(ent.Owner, SpecialStat.Intelligence, ent.Comp) >= MedicalHudIntelligenceThreshold)
            EnsureMedicalHud(ent.Owner);
        else
            ClearMedicalHud(ent.Owner);
    }

    private void EnsureMedicalHud(EntityUid uid)
    {
        var applied = EnsureComp<SpecialAppliedMedicalHudComponent>(uid);

        if (!HasComp<ShowHealthBarsComponent>(uid))
        {
            var bars = EnsureComp<ShowHealthBarsComponent>(uid);
            EnsureBiologicalContainer(bars.DamageContainers);
            Dirty(uid, bars);
            applied.AddedHealthBars = true;
        }

        if (!HasComp<ShowHealthIconsComponent>(uid))
        {
            var icons = EnsureComp<ShowHealthIconsComponent>(uid);
            EnsureBiologicalContainer(icons.DamageContainers);
            Dirty(uid, icons);
            applied.AddedHealthIcons = true;
        }
    }

    private void ClearMedicalHud(EntityUid uid)
    {
        if (!TryComp<SpecialAppliedMedicalHudComponent>(uid, out var applied))
            return;

        if (applied.AddedHealthBars)
            RemComp<ShowHealthBarsComponent>(uid);

        if (applied.AddedHealthIcons)
            RemComp<ShowHealthIconsComponent>(uid);

        RemComp<SpecialAppliedMedicalHudComponent>(uid);
    }

    private static void EnsureBiologicalContainer(ICollection<string> damageContainers)
    {
        if (!damageContainers.Contains(BiologicalDamageContainer))
            damageContainers.Add(BiologicalDamageContainer);
    }
}
