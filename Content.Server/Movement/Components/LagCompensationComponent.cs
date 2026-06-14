using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Movement.Components;

[RegisterComponent]
public sealed partial class LagCompensationComponent : Component
{
    [ViewVariables]
    public readonly Queue<(GameTick Tick, EntityCoordinates Coordinates, Angle Angle)> Positions = new();
}
