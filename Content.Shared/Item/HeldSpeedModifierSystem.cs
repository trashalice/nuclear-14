using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._Misfits.SpecialStats.Components;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Content.Shared.Movement.Systems;

namespace Content.Shared.Item;

/// <summary>
/// This handles <see cref="HeldSpeedModifierComponent"/>
/// </summary>
public sealed class HeldSpeedModifierSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<HeldSpeedModifierComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<HeldSpeedModifierComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<HeldSpeedModifierComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMovementSpeedModifiers);
    }

    private void OnGotEquippedHand(Entity<HeldSpeedModifierComponent> ent, ref GotEquippedHandEvent args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnGotUnequippedHand(Entity<HeldSpeedModifierComponent> ent, ref GotUnequippedHandEvent args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnRefreshMovementSpeedModifiers(EntityUid uid, HeldSpeedModifierComponent component, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var walkMod = component.WalkModifier;
        var sprintMod = component.SprintModifier;
        if (component.MirrorClothingModifier && TryComp<ClothingSpeedModifierComponent>(uid, out var clothingSpeedModifier))
        {
            walkMod = clothingSpeedModifier.WalkModifier;
            sprintMod = clothingSpeedModifier.SprintModifier;
        }

        if (ShouldIgnoreStrengthSlowdown(args.Holder, uid))
        {
            walkMod = MathF.Max(walkMod, 1f);
            sprintMod = MathF.Max(sprintMod, 1f);
        }

        args.Args.ModifySpeed(walkMod, sprintMod);
    }

    private bool ShouldIgnoreStrengthSlowdown(EntityUid user, EntityUid held)
    {
        if (!TryComp<StrengthIgnoreClothingSlowdownComponent>(held, out var ignore) ||
            !TryComp<SpecialComponent>(user, out var special))
        {
            return false;
        }

        return _special.GetEffective(user, SpecialStat.Strength, special) >= ignore.MinimumStrength;
    }
}
