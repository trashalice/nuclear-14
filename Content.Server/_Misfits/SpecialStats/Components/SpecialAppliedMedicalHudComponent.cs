namespace Content.Server._Misfits.SpecialStats.Components;

/// <summary>
/// Tracks medical HUD components that were granted by high Intelligence.
/// </summary>
[RegisterComponent]
public sealed partial class SpecialAppliedMedicalHudComponent : Component
{
    public string Action = "ActionToggleSpecialMedicalHud";
    public EntityUid? ActionEntity;
    public bool Enabled = true;
    public bool AddedHealthBars;
    public bool AddedHealthIcons;
}
