namespace Content.Shared._Misfits.Traits;

[RegisterComponent]
public sealed partial class GibModifierComponent : Component
{

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float GibThresholdMultiplier = 1f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float SeverThresholdMultiplier = 1f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Ungibbable = false;

}
