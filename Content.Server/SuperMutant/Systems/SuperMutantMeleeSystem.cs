/*
using Content.Shared.Humanoid;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable.Components;

namespace Content.Server.SuperMutant.Systems;

public sealed class SuperMutantMeleeSystem : EntitySystem
{
    private const float MeleeDamageCeiling = 160f;

    public override void Initialize()
    {
        base.Initialize();

        // Disabled: log curve logic moved to OniSystem.OnGetMeleeDamage.
        // SubscribeLocalEvent<GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(ref GetMeleeDamageEvent args)
    {
        if (!TryComp<HumanoidAppearanceComponent>(args.User, out var appearance))
            return;

        if (appearance.Species != "SuperMutant")
            return;

        if (!TryComp<WieldableComponent>(args.Weapon, out var wieldable) ||
            !wieldable.Wielded)
            return;

        var baseDamage = args.Damage.GetTotal().Float();
        if (baseDamage <= 0f)
            return;

        var logCurveDamage = MeleeDamageCeiling * MathF.Log(baseDamage + 1f) / MathF.Log(MeleeDamageCeiling + 1f);
        var targetDamage = MathF.Min(MathF.Max(logCurveDamage, baseDamage), MeleeDamageCeiling);

        args.Damage *= targetDamage / baseDamage;
    }
}
*/
