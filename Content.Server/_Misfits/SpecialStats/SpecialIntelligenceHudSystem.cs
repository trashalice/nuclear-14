using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Actions;
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
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    private const int MedicalHudIntelligenceThreshold = 10;
    private const string BiologicalDamageContainer = "Biological";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<SpecialAppliedMedicalHudComponent, ComponentShutdown>(OnMedicalHudShutdown);
        SubscribeLocalEvent<SpecialAppliedMedicalHudComponent, SpecialMedicalHudToggleActionEvent>(OnMedicalHudToggle);
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
        _actions.AddAction(uid, ref applied.ActionEntity, applied.Action);
        _actions.SetToggled(applied.ActionEntity, applied.Enabled);

        if (!applied.Enabled)
            return;

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

        ClearMedicalHudComponents(uid, applied);
        RemComp<SpecialAppliedMedicalHudComponent>(uid);
    }

    private void ClearMedicalHudComponents(EntityUid uid, SpecialAppliedMedicalHudComponent applied)
    {
        if (applied.AddedHealthBars)
        {
            RemComp<ShowHealthBarsComponent>(uid);
            applied.AddedHealthBars = false;
        }

        if (applied.AddedHealthIcons)
        {
            RemComp<ShowHealthIconsComponent>(uid);
            applied.AddedHealthIcons = false;
        }
    }

    private void OnMedicalHudShutdown(EntityUid uid, SpecialAppliedMedicalHudComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnMedicalHudToggle(EntityUid uid, SpecialAppliedMedicalHudComponent component, SpecialMedicalHudToggleActionEvent args)
    {
        if (args.Handled)
            return;

        component.Enabled = !component.Enabled;

        if (component.Enabled)
            EnsureMedicalHud(uid);
        else
            ClearMedicalHudComponents(uid, component);

        args.Handled = true;
    }

    private static void EnsureBiologicalContainer(ICollection<string> damageContainers)
    {
        if (!damageContainers.Contains(BiologicalDamageContainer))
            damageContainers.Add(BiologicalDamageContainer);
    }
}
