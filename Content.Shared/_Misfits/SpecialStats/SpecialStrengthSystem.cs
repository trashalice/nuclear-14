using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Applies Strength to carry and pull handling outside of direct melee damage.
/// </summary>
public sealed class SpecialStrengthSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialCarryPullSpeedModifierEvent>(OnCarryPullSpeedModifier);
    }

    private void OnCarryPullSpeedModifier(ref SpecialCarryPullSpeedModifierEvent args)
    {
        if (!TryComp<SpecialComponent>(args.User, out var special))
            return;

        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            args.User,
            SpecialStat.Strength,
            tuning.StrengthCarryPullSpeedMultiplierPerPoint,
            special);
        var multiplier = MathF.Max(0.1f, 1f + modifier);

        args.ModifySpeed(multiplier);
    }
}
