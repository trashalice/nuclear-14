namespace Content.Goobstation.Common.DeviceNetwork;

[ByRefEvent]
public record struct DeviceNetworkFrequencyChangedEvent(uint? OldFrequency, uint? NewFrequency)
{
    public bool Cancelled;
}
