namespace Content.Server._Misfits.SpecialStats.Components;

/// <summary>
/// Tracks medical HUD components that were granted by high Intelligence.
/// </summary>
[RegisterComponent]
public sealed partial class SpecialAppliedMedicalHudComponent : Component
{
    public bool AddedHealthBars;
    public bool AddedHealthIcons;
}
