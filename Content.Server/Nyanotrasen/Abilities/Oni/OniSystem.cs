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
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared.Contests;
using Robust.Shared.Containers;
using Content.Server._Misfits.Special; // #Misfits Add - ordering dependency

namespace Content.Server.Abilities.Oni
{
    public sealed class OniSystem : SharedOniSystem
    {
        private const float MutantMeleeDamageCeiling = 160f;

        [Dependency] private readonly ToolSystem _toolSystem = default!;
        [Dependency] private readonly GunSystem _gunSystem = default!;

        // #Misfits Add - Track weapons whose health contest we disabled for super mutants.
        private readonly HashSet<EntityUid> _healthContestDisabled = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<OniComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
            SubscribeLocalEvent<OniComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
            SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage, after: [typeof(WieldableSystem), typeof(SpecialCombatSystem)]);
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

            // #Misfits Add - Restore health contest on the weapon when dropped from a super mutant.
            if (_healthContestDisabled.Remove(args.Entity)
                && TryComp<MeleeWeaponComponent>(args.Entity, out var melee)
                && melee.ContestArgs is not null)
            {
                melee.ContestArgs.DoHealthInteraction = true;
            }

            RemComp<HeldByOniComponent>(args.Entity);
        }



        private void OnGetMeleeDamage(EntityUid uid, MeleeWeaponComponent component, ref GetMeleeDamageEvent args)
        {
            if (!TryComp<OniComponent>(args.User, out var oni))

                return;

            // Super Mutants and Nightkin: log curve for wielded weapons, hard 160 cap on everything.
            bool isWielded = TryComp<WieldableComponent>(uid, out var wield) && wield.Wielded;
            bool isSuperMutant = TryComp<HumanoidAppearanceComponent>(args.User, out var appearance) &&
                (appearance.Species == "SuperMutant" || appearance.Species == "Nightkin");

            if (isSuperMutant)
            {
                // #Misfits Tweak - Disable health contest on this weapon so the 160 cap isn't bypassed by HP-scaling.
                if (component.ContestArgs is { DoHealthInteraction: true })
                {
                    component.ContestArgs.DoHealthInteraction = false;
                    _healthContestDisabled.Add(uid);
                }

                // #Misfits Tweak - Apply Oni modifier coefficients (2.0x) directly to args.Damage so
                // the hard cap can clamp the final number. Do NOT add to args.Modifiers — those are
                // applied after this event returns and would bypass the cap.
                args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, oni.MeleeModifiers);

                // Log curve: shape wielded weapon damage so low-damage tools aren't trivial
                // and high-damage weapons are compressed toward the ceiling.
                if (isWielded)
                {
                    var baseDamage = args.Damage.GetTotal().Float();
                    if (baseDamage > 0f)
                    {
                        var logCurveDamage = MutantMeleeDamageCeiling * MathF.Log(baseDamage + 1f) /
                                              MathF.Log(MutantMeleeDamageCeiling + 1f);
                        var targetDamage = MathF.Min(MathF.Max(logCurveDamage, baseDamage), MutantMeleeDamageCeiling);
                        args.Damage *= targetDamage / baseDamage;
                    }
                }

                // #Misfits Tweak - Hard cap: super mutant damage never exceeds 160 total, period.
                var total = args.Damage.GetTotal().Float();
                if (total > MutantMeleeDamageCeiling)
                    args.Damage *= MutantMeleeDamageCeiling / total;

                return; // Super mutant path done — skip generic Oni modifier path below.
            }

            // Non-super-mutant Oni: flat modifiers via args.Modifiers (no log curve, no cap).
            args.Modifiers.Add(oni.MeleeModifiers);
        }


        private void OnStamHit(EntityUid uid, HeldByOniComponent component, TakeStaminaDamageEvent args)
        {
            if (!TryComp<OniComponent>(component.Holder, out var oni))
                return;

            args.Multiplier *= oni.StamDamageMultiplier;
        }
    }
}
