using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Applies Perception to gun precision and firing cadence.
/// </summary>
public sealed class SpecialPerceptionSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnGunRefreshModifiers(ref GunRefreshModifiersEvent args)
    {
        var holder = Transform(args.Gun.Owner).ParentUid;

        if (!TryComp<SpecialComponent>(holder, out var special))
            return;

        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            holder,
            SpecialStat.Perception,
            -tuning.PerceptionSpreadMultiplierPerPoint,
            special);
        var keepFraction = Math.Clamp(1.0 + modifier, 0.5, 2.0);

        args.MinAngle = new Angle((double) args.MinAngle * keepFraction);
        args.MaxAngle = new Angle((double) args.MaxAngle * keepFraction);
        args.AngleIncrease = new Angle((double) args.AngleIncrease * keepFraction);
        args.CameraRecoilScalar *= (float) keepFraction;

        ApplyHeavyGunHandling(holder, ref args, special);

        if (args.FireRate <= 0f)
            return;

        args.FireRate *= 1f / GetFireDelayMultiplier(holder, special);
    }

    private void ApplyHeavyGunHandling(EntityUid holder, ref GunRefreshModifiersEvent args, SpecialComponent special)
    {
        if (!HasComp<GunRequiresWieldComponent>(args.Gun.Owner) &&
            !HasComp<WieldableComponent>(args.Gun.Owner))
            return;

        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            holder,
            SpecialStat.Perception,
            -tuning.PerceptionHeavyGunMultiplierPerPoint,
            special);
        var keepFraction = Math.Clamp(1.0 + modifier, 0.70, 1.40);

        args.MinAngle = new Angle((double) args.MinAngle * keepFraction);
        args.MaxAngle = new Angle((double) args.MaxAngle * keepFraction);
        args.AngleIncrease = new Angle((double) args.AngleIncrease * keepFraction);
        args.CameraRecoilScalar *= (float) keepFraction;
    }

    private float GetFireDelayMultiplier(EntityUid uid, SpecialComponent special)
    {
        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            uid,
            SpecialStat.Perception,
            -tuning.PerceptionFireDelayMultiplierPerPoint,
            special);

        return MathF.Max(0.1f, 1f + modifier);
    }
}
