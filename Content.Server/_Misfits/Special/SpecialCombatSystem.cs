using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Special;

public sealed class SpecialCombatSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<SpecialComponent, SpecialModifyHitscanDamageEvent>(OnModifyHitscanDamage);
    }

    private void OnMeleeHit(EntityUid uid, MeleeWeaponComponent component, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (!TryComp<SpecialComponent>(args.User, out var special))
            return;

        var damage = args.BaseDamage;
        ApplyStrengthMeleeModifier(args.User, ref damage, special);

        // Melee weapons use the same Luck crit curve as one-shot ranged weapons.
        TryApplyLuckCritical(args.User, ref damage, special, uid);

        // Preserve the original event damage and apply only the SPECIAL delta.
        args.BonusDamage += damage - args.BaseDamage;
    }

    private void ApplyStrengthMeleeModifier(EntityUid user, ref DamageSpecifier damage, SpecialComponent special)
    {
        var tuning = _special.GetTuning();
        var delta = _special.GetCurvedEffectDelta(user, SpecialStat.Strength, special);

        if (delta == 0f)
            return;

        var multiplier = 1f + delta * tuning.StrengthMeleeDamageMultiplierPerPoint;
        damage *= MathF.Max(0.1f, multiplier);
    }

    private void OnProjectileHit(Entity<ProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Shooter == null ||
            !TryComp<SpecialComponent>(args.Shooter.Value, out var special))
            return;

        // Projectile damage is mutable on the hit event, so write back only on crit.
        var damage = args.Damage;
        if (TryApplyLuckCritical(args.Shooter.Value, ref damage, special, ent.Comp.Weapon))
            args.Damage = damage;
    }

    private void OnModifyHitscanDamage(Entity<SpecialComponent> ent, ref SpecialModifyHitscanDamageEvent args)
    {
        var damage = args.Damage;
        if (TryApplyLuckCritical(ent.Owner, ref damage, ent.Comp, args.Weapon))
            args.Damage = damage;
    }

    private bool TryApplyLuckCritical(EntityUid user, ref DamageSpecifier damage, SpecialComponent special, EntityUid? weapon)
    {
        var chance = GetLuckCriticalChance(user, special, weapon);

        if (chance <= 0f || !_random.Prob(chance))
            return false;

        var tuning = _special.GetTuning();
        damage *= tuning.LuckCriticalDamageMultiplier;
        return true;
    }

    private float GetLuckCriticalChance(EntityUid user, SpecialComponent special, EntityUid? weapon)
    {
        var tuning = _special.GetTuning();

        // Hitscan callers can omit the weapon if they want the generic Luck curve.
        if (weapon == null)
            return _special.GetLuckRollChance(user, 0f, tuning.LuckCriticalChancePerPoint, special);

        // Single-shot weapons get the full configured chance. Magazine-fed weapons
        // divide that chance by capacity so expected crits per reload stay similar.
        var delta = _special.GetCurvedEffectDelta(user, SpecialStat.Luck, special);
        var ammoCapacity = GetWeaponAmmoCapacity(weapon.Value);
        var singleShotChance = tuning.LuckSingleShotCriticalChanceAtTen * delta / SharedSpecialSystem.GetCurvedEffectDelta(SpecialProfile.Maximum);
        return Math.Clamp(singleShotChance / ammoCapacity, 0f, 1f);
    }

    private int GetWeaponAmmoCapacity(EntityUid weapon)
    {
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammo, false);

        return Math.Max(1, ammo.Capacity);
    }
}
