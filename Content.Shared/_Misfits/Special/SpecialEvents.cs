using Content.Shared.Damage;

namespace Content.Shared._Misfits.Special;

/// <summary>
/// Raised whenever a character's effective SPECIAL values may have changed.
/// </summary>
[ByRefEvent]
public readonly record struct SpecialChangedEvent(EntityUid ChangedEntity);

/// <summary>
/// Raised before cached stat side effects should be removed from a deleted component.
/// </summary>
[ByRefEvent]
public readonly record struct SpecialShutdownEvent(EntityUid Entity);

/// <summary>
/// Lets ranged weapon systems pass hitscan damage through Luck critical handling.
/// </summary>
[ByRefEvent]
public record struct SpecialModifyHitscanDamageEvent(EntityUid Weapon, DamageSpecifier Damage);
