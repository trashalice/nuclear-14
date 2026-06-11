using Content.Server.Tools;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Tools.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Nyanotrasen.Abilities.Oni;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Server.Abilities.Oni
{
    public sealed class OniSystem : SharedOniSystem
    {
        private const float MutantMeleeDamageCeiling = 160f;

        [Dependency] private readonly ToolSystem _toolSystem = default!;
        [Dependency] private readonly GunSystem _gunSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<OniComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
            SubscribeLocalEvent<OniComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
            SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
            SubscribeLocalEvent<HeldByOniComponent, TakeStaminaDamageEvent>(OnStamHit);
        }

        private void OnEntInserted(EntityUid uid, OniComponent component, EntInsertedIntoContainerMessage args)
        {
            var heldComp = EnsureComp<HeldByOniComponent>(args.Entity);
            heldComp.Holder = uid;

            if (TryComp<ToolComponent>(args.Entity, out var tool) && _toolSystem.HasQuality(args.Entity, "Prying", tool))
                _toolSystem.SetSpeedModifier((args.Entity, tool), tool.SpeedModifier * 1.66f);

            if (_gunSystem.TryGetGun(args.Entity, out _, out var gun))
            {
                gun.MinAngle *= 15f;
                gun.AngleIncrease *= 15f;
                gun.MaxAngle *= 15f;
            }
        }

        private void OnEntRemoved(EntityUid uid, OniComponent component, EntRemovedFromContainerMessage args)
        {
            if (TryComp<ToolComponent>(args.Entity, out var tool) && _toolSystem.HasQuality(args.Entity, "Prying", tool))
                _toolSystem.SetSpeedModifier((args.Entity, tool), tool.SpeedModifier / 1.66f);

            if (_gunSystem.TryGetGun(args.Entity, out _, out var gun))
            {
                gun.MinAngle /= 15f;
                gun.AngleIncrease /= 15f;
                gun.MaxAngle /= 15f;
            }

            RemComp<HeldByOniComponent>(args.Entity);
        }



        private void OnGetMeleeDamage(EntityUid uid, MeleeWeaponComponent component, ref GetMeleeDamageEvent args)
        {
            if (!TryComp<OniComponent>(args.User, out var oni))

                return;

            // Super Mutants and Nightkin: log curve for wielded weapons only, flat 2x for everything else
            bool isWielded = TryComp<WieldableComponent>(uid, out var wield) && wield.Wielded;
            bool isSuperMutant = TryComp<HumanoidAppearanceComponent>(args.User, out var appearance) &&
                (appearance.Species == "SuperMutant" || appearance.Species == "Nightkin");

            // Important: `args.Damage` is the current damage spec after other melee damage adjustments
            // have already been applied upstream at this hook (wield, two-hands, weapon bonuses, etc).
            // The curve should use the same value we scale so "log damage math" lands in the expected range.
            var baseDamage = args.Damage.GetTotal().Float();

            if (isWielded && isSuperMutant)
            {
                if (baseDamage > 0f)
                {
                    var logCurveDamage = MutantMeleeDamageCeiling * MathF.Log(baseDamage + 1f) /
                                          MathF.Log(MutantMeleeDamageCeiling + 1f);
                    var targetDamage = MathF.Min(MathF.Max(logCurveDamage, baseDamage), MutantMeleeDamageCeiling);

                    args.Damage *= targetDamage / baseDamage;
                }
            }

            // Apply Oni melee modifiers.
            // (curve math above is intended to shape wielded mutant damage; final number should be capped via log curve)
            args.Modifiers.Add(oni.MeleeModifiers);

            // Cap wielded mutant melee damage to 200 (pre-resistance; final damage pipeline may apply resistances).
            // NOTE: Oni modifier coefficients (e.g. 2.0x Blunt) are applied after this event returns
            // via DamageSpecifier.ApplyModifierSets, so we must pre-scale args.Damage to account for
            // them, otherwise the final post-modifier damage blows past the cap.
            if (isWielded && isSuperMutant)
            {
                var effectiveDamage = DamageSpecifier.ApplyModifierSet(args.Damage, oni.MeleeModifiers);
                var total = effectiveDamage.GetTotal().Float();
                if (total > 200f)
                    args.Damage *= 200f / total;
            }
        }


        private void OnStamHit(EntityUid uid, HeldByOniComponent component, TakeStaminaDamageEvent args)
        {
            if (!TryComp<OniComponent>(component.Holder, out var oni))
                return;

            args.Multiplier *= oni.StamDamageMultiplier;
        }
    }
}
