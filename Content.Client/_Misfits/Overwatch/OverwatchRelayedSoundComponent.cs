namespace Content.Client._Misfits.Overwatch;

[RegisterComponent]
[Access(typeof(OverwatchConsoleSystem))]
public sealed partial class OverwatchRelayedSoundComponent : Component
{
    [DataField]
    public EntityUid? Relay;
}
