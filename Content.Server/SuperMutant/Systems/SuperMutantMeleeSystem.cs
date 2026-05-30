using Content.Shared.Humanoid;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;

namespace Content.Server.SuperMutant.Systems;

public sealed class SuperMutantMeleeSystem : EntitySystem
{
    private const float OniMeleeDamageCeiling = 160f;

    public override void Initialize()
    {
        base.Initialize();

        // Listen for melee damage calculation (fired on the weapon). args.User is the attacker.
        SubscribeLocalEvent<GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(ref GetMeleeDamageEvent args)
    {
        if (args.User == EntityUid.Invalid)
            return;

        // Check the attacker's species via the humanoid appearance component
        if (!TryComp<HumanoidAppearanceComponent>(args.User, out var appearance))
            return;

        if (appearance.Species != "SuperMutant")
            return;

        var baseDamage = args.Damage.GetTotal().Float();
        if (baseDamage <= 0f)
            return;

        // Logarithmic curve scaled to the ceiling (maps 0..+inf -> 0..Ceiling)
        var logCurveDamage = OniMeleeDamageCeiling * MathF.Log(baseDamage + 1f) / MathF.Log(OniMeleeDamageCeiling + 1f);

        // Target damage is the log curve clamped to the ceiling. Use max against baseDamage
        // so fragile low-damage weapons still get their expected damage.
        var targetDamage = MathF.Min(MathF.Max(logCurveDamage, baseDamage), OniMeleeDamageCeiling);

        var scale = targetDamage / baseDamage;
        args.Damage *= scale;
    }
}
