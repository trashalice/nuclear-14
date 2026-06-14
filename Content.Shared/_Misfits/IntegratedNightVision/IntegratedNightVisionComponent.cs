using Robust.Shared.GameObjects;

namespace Content.Shared._Misfits.IntegratedNightVision;

[RegisterComponent]
public sealed partial class IntegratedNightVisionHelmetComponent : Component
{
    [DataField(required: true)]
    public string SetId = string.Empty;

    [ViewVariables]
    public EntityUid? Wearer;
}

[RegisterComponent]
public sealed partial class IntegratedNightVisionArmorComponent : Component
{
    [DataField(required: true)]
    public string SetId = string.Empty;
}
