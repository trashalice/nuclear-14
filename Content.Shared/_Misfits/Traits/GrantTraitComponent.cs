// #Misfits Add - Generic trait-training item component.
using Content.Shared.Traits;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.Traits;

/// <summary>
///     Placed on an item that grants a trait to the user when used in hand.
/// </summary>
[RegisterComponent]
public sealed partial class GrantTraitComponent : Component
{
    [DataField(required: true)]
    public ProtoId<TraitPrototype> Trait;

    [DataField]
    public LocId? LearnMessage;

    [DataField]
    public LocId? AlreadyKnownMessage;

    [DataField]
    public bool MultiUse;

    [DataField]
    public string? SpawnedProto;

    [DataField]
    public SoundSpecifier? SoundOnUse;

    /// <summary>
    ///     Components that identify the trait as already known.
    ///     If any are already present on the user, the item is not consumed.
    /// </summary>
    [DataField]
    public ComponentRegistry AlreadyKnownComponents = new();
}
