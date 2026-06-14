// #Misfits Add - Client-side ByRefEvent to query the combo attack history for the combo widget overlay.
// Pattern from Goob-Station: Content.Goobstation.Common/MartialArts/ComboEvents.cs (AGPL-3.0)
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.MartialArts;

/// <summary>
/// Raised by the client overlay (ShowHandItemOverlay) to retrieve the current combo input buffer.
/// Handled by <see cref="SharedMisfitsMartialArtsSystem"/> which copies <see cref="CanPerformComboComponent.LastAttacks"/>.
/// </summary>
[ByRefEvent]
public record struct GetPerformedAttackTypesEvent(List<MisfitsComboAttackType>? AttackTypes = null);
