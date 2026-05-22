using Content.Shared.Damage;

namespace Content.Shared._Misfits.Special;

[ByRefEvent]
public readonly record struct SpecialChangedEvent(EntityUid ChangedEntity);

[ByRefEvent]
public readonly record struct SpecialShutdownEvent(EntityUid Entity);

[ByRefEvent]
public record struct SpecialModifyHitscanDamageEvent(EntityUid Weapon, DamageSpecifier Damage);
