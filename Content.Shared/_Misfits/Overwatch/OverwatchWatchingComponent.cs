using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Overwatch;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OverwatchWatchingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Watching;
}
