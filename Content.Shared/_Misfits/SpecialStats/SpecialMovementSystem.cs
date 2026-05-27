using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Applies Agility as a small movement speed modifier.
/// </summary>
public sealed class SpecialMovementSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<SpecialComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            ent.Owner,
            SpecialStat.Agility,
            tuning.AgilityMovementSpeedMultiplierPerPoint,
            ent.Comp);
        var multiplier = MathF.Max(0.1f, 1f + modifier);

        args.ModifySpeed(multiplier, multiplier);
    }
}
