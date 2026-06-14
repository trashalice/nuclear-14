using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Clothing.Pins;

/// <summary>
/// Allows this item to be attached to compatible clothing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClothingPinComponent : Component
{
    /// <summary>
    /// Pins with the same category share a per-clothing limit.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Category = "pin";

    /// <summary>
    /// Maximum number of pins in this category that can be attached to one clothing item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Limit = 1;
}
