using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Special;

public sealed class SpecialCombatSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemComponent, GetMeleeDamageEvent>(OnGetItemMeleeDamage, after: [typeof(WieldableSystem)]);
        SubscribeLocalEvent<WieldableComponent, GetMeleeDamageEvent>(OnWieldableMeleeDamage, after: [typeof(WieldableSystem)]);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<SpecialComponent, SpecialModifyHitscanDamageEvent>(OnModifyHitscanDamage);
    }

    private void OnGetItemMeleeDamage(EntityUid uid, ItemComponent component, ref GetMeleeDamageEvent args)
    {
        if (HasComp<WieldableComponent>(uid))
            return;

        ApplyStrengthMeleeDamage(args.User, ref args);
    }

    private void OnWieldableMeleeDamage(EntityUid uid, WieldableComponent component, ref GetMeleeDamageEvent args)
    {
        ApplyStrengthMeleeDamage(args.User, ref args);
    }

    private void ApplyStrengthMeleeDamage(EntityUid user, ref GetMeleeDamageEvent args)
    {
        if (!TryComp<SpecialComponent>(user, out var special))
            return;

        var damage = args.Damage;
        ApplyStrengthMeleeModifier(user, ref damage, special);
        args.Damage = damage;
    }

    private void OnMeleeHit(EntityUid uid, MeleeWeaponComponent component, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (!TryComp<SpecialComponent>(args.User, out var special))
            return;

        var damage = args.BaseDamage;

        // Innate mob melee is unarmed. Use gentler Strength scaling than held weapons.
        if (uid == args.User)
            ApplyStrengthUnarmedModifier(args.User, ref damage, special);

        // Melee weapons use the same Luck outcome curve as ranged weapons.
        TryApplyLuckDamageOutcome(args.User, ref damage, special);

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

    private void ApplyStrengthUnarmedModifier(EntityUid user, ref DamageSpecifier damage, SpecialComponent special)
    {
        var tuning = _special.GetTuning();
        var delta = _special.GetCurvedEffectDelta(user, SpecialStat.Strength, special);

        if (delta == 0f)
            return;

        var multiplier = 1f + delta * tuning.StrengthUnarmedDamageMultiplierPerPoint;
        damage *= MathF.Max(0.1f, multiplier);
    }

    private void OnProjectileHit(Entity<ProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Shooter == null ||
            !TryComp<SpecialComponent>(args.Shooter.Value, out var special))
            return;

        // Projectile damage is mutable on the hit event, so write back only on Luck outcomes.
        var damage = args.Damage;
        if (TryApplyLuckDamageOutcome(args.Shooter.Value, ref damage, special, ent.Comp.Weapon))
            args.Damage = damage;
    }

    private void OnModifyHitscanDamage(Entity<SpecialComponent> ent, ref SpecialModifyHitscanDamageEvent args)
    {
        var damage = args.Damage;
        if (TryApplyLuckDamageOutcome(ent.Owner, ref damage, ent.Comp, args.Weapon))
            args.Damage = damage;
    }

    private bool TryApplyLuckDamageOutcome(EntityUid user, ref DamageSpecifier damage, SpecialComponent special, EntityUid? weapon = null)
    {
        var tuning = _special.GetTuning();
        var critChance = GetLuckCriticalChance(user, special, weapon);

        if (critChance > 0f && _random.Prob(critChance))
        {
            damage *= tuning.LuckCriticalDamageMultiplier;
            return true;
        }

        var unluckyChance = GetLuckUnluckyChance(user, special);
        if (unluckyChance <= 0f || !_random.Prob(unluckyChance))
            return false;

        damage *= Math.Clamp(tuning.LuckUnluckyDamageMultiplier, 0f, 1f);
        return true;
    }

    private float GetLuckCriticalChance(EntityUid user, SpecialComponent special, EntityUid? weapon)
    {
        var tuning = _special.GetTuning();

        // Every hit rolls the full configured chance; magazine-fed weapons no
        // longer divide crit chance by ammo capacity.
        var delta = _special.GetCurvedEffectDelta(user, SpecialStat.Luck, special);
        var shotChance = delta * tuning.LuckSingleShotCriticalChancePerPoint;

        if (weapon != null && HasComp<RevolverAmmoProviderComponent>(weapon.Value))
            shotChance *= 2f;

        return Math.Clamp(shotChance, 0f, 1f);
    }

    private float GetLuckUnluckyChance(EntityUid user, SpecialComponent special)
    {
        return _special.GetEffective(user, SpecialStat.Luck, special) switch
        {
            2 => 0.05f,
            3 => 0.03f,
            4 => 0.01f,
            _ => 0f,
        };
    }
}
