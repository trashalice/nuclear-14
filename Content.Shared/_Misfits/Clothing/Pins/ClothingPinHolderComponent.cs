using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Clothing.Pins;

/// <summary>
/// Runtime holder for clothing pins attached to a clothing item.
/// Added lazily when a pin is attached, so clothing prototypes do not need to opt in individually.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ClothingPinHolderComponent : Component
{
    public const string DefaultContainerId = "misfits_clothing_pins";

    [DataField, AutoNetworkedField]
    public string ContainerId = DefaultContainerId;

    [DataField, AutoNetworkedField]
    public List<string> AllowedCategories = new() { "pin" };
}
