using System.Collections.Generic;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Overwatch;

[RegisterComponent, Access(typeof(OverwatchConsoleSystem))]
public sealed partial class OverwatchConsoleComponent : Component
{
    [DataField]
    public List<ProtoId<TagPrototype>> TrackedTags = new();

    [DataField]
    public string MonitorTitle = "overwatch-monitor-title";

    [ViewVariables]
    public bool UiOpen;

    [ViewVariables]
    public EntityUid? UiActor;

    [ViewVariables]
    public EntityUid? WatchingActor;

    [ViewVariables]
    public EntityUid? WatchedEntity;

    [ViewVariables]
    public uint? WatchedNumber;

    [ViewVariables]
    public string? LastKnownName;

    [ViewVariables]
    public float? LastKnownX;

    [ViewVariables]
    public float? LastKnownY;

    [ViewVariables]
    public string? LastKnownTimestamp;
}
