using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Carrying;

/// <summary>
/// Allows carrying and dragging mobs without applying the normal carry/pull slowdown.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CanFiremanCarryComponent : Component;
