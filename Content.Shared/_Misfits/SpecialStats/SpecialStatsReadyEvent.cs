// #Misfits Add - Raised on a player entity once their S.P.E.C.I.A.L. stats are confirmed
// and ready to drive gameplay effects (stamina threshold, speed modifiers, luck, etc.).
// Raised both on first-time allocation confirmation and on subsequent round loads.

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Raised on a player entity once their S.P.E.C.I.A.L. stat values are fully loaded
/// or freshly confirmed, signalling other systems to (re-)apply stat-driven effects
/// such as stamina pool size and movement speed bonus.
/// </summary>
[ByRefEvent]
public readonly record struct SpecialStatsReadyEvent(EntityUid Entity);
