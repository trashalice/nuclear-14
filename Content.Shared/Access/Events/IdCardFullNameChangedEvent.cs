using Robust.Shared.Serialization;

namespace Content.Shared.Access.Events;

[Serializable, NetSerializable]
public sealed class IdCardFullNameChangedEvent : EntityEventArgs
{
    public string? FullName;

    public IdCardFullNameChangedEvent(string? fullName)
    {
        FullName = fullName;
    }
}
