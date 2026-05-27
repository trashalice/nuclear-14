namespace Content.Server.DeviceNetwork.Components;

/// <summary>
/// Prevents network configurators from interacting with this device, except from allowed source prototypes.
/// </summary>
[RegisterComponent]
public sealed partial class BlockNetworkConfiguratorComponent : Component
{
    [DataField]
    public HashSet<string> AllowedSourcePrototypes = new();
}
