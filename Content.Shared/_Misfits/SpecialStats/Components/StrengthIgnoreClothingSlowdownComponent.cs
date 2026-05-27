using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.SpecialStats.Components;

/// <summary>
/// Allows sufficiently strong characters to ignore this item's clothing speed penalties.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StrengthIgnoreClothingSlowdownComponent : Component
{
    [DataField]
    public int MinimumStrength = 7;
}
