using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Content.Shared.Mobs;
using Robust.Shared.Network;

namespace Content.Shared._Misfits.Overwatch;

[Serializable, NetSerializable]
public enum OverwatchConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable, DataRecord]
public partial struct OverwatchConsoleEntry
{
    public uint Number;
    public string Name;
    public string? JobTitle;
    public string Category;
    public int CategorySortOrder;
    public float Health;
    public MobState State;
    public float X;
    public float Y;
    public bool IsCurrentTarget;

    public OverwatchConsoleEntry(
        uint number,
        string name,
        string? jobTitle,
        string category,
        int categorySortOrder,
        float health,
        MobState state,
        float x,
        float y,
        bool isCurrentTarget)
    {
        Number = number;
        Name = name;
        JobTitle = jobTitle;
        Category = category;
        CategorySortOrder = categorySortOrder;
        Health = health;
        State = state;
        X = x;
        Y = y;
        IsCurrentTarget = isCurrentTarget;
    }
}

[Serializable, NetSerializable]
public sealed class OverwatchConsoleState : BoundUserInterfaceState
{
    public readonly string MonitorTitle;
    public readonly uint? WatchedNumber;
    public readonly string? WatchedName;
    public readonly NetEntity? WatchedEntity;
    public readonly float? LastKnownX;
    public readonly float? LastKnownY;
    public readonly string? LastKnownTimestamp;
    public readonly List<OverwatchConsoleEntry> Personnel;

    public OverwatchConsoleState(
        string monitorTitle,
        uint? watchedNumber,
        string? watchedName,
        NetEntity? watchedEntity,
        float? lastKnownX,
        float? lastKnownY,
        string? lastKnownTimestamp,
        List<OverwatchConsoleEntry> personnel)
    {
        MonitorTitle = monitorTitle;
        WatchedNumber = watchedNumber;
        WatchedName = watchedName;
        WatchedEntity = watchedEntity;
        LastKnownX = lastKnownX;
        LastKnownY = lastKnownY;
        LastKnownTimestamp = lastKnownTimestamp;
        Personnel = personnel;
    }
}

[Serializable, NetSerializable]
public sealed class OverwatchConsoleMessage : BoundUserInterfaceMessage
{
    public readonly OverwatchConsoleMessageType Type;
    public readonly uint? TargetNumber;

    public OverwatchConsoleMessage(OverwatchConsoleMessageType type, uint? targetNumber = null)
    {
        Type = type;
        TargetNumber = targetNumber;
    }
}

[Serializable, NetSerializable]
public enum OverwatchConsoleMessageType : byte
{
    Watch,
    Unwatch,
}
