using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.DeviceNetwork;

[Serializable, NetSerializable]
public enum DeviceCustomFrequencyUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class DeviceCustomFrequencyChangeMessage : BoundUserInterfaceMessage
{
    public uint Frequency { get; }

    public DeviceCustomFrequencyChangeMessage(uint frequency)
    {
        Frequency = frequency;
    }
}

[Serializable, NetSerializable]
public sealed class DeviceCustomFrequencyUserInterfaceState : BoundUserInterfaceState
{
    public uint? Frequency;

    public DeviceCustomFrequencyUserInterfaceState(uint? frequency)
    {
        Frequency = frequency;
    }
}
