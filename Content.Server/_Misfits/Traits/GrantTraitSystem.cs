// #Misfits Add - Generic system for training items that grant character traits.
using Content.Server.Traits;
using Content.Shared._Misfits.Traits;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Traits;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Traits;

public sealed class GrantTraitSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TraitSystem _traits = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GrantTraitComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(EntityUid uid, GrantTraitComponent comp, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var user = args.User;

        foreach (var entry in comp.AlreadyKnownComponents.Values)
        {
            if (!HasComp(user, entry.Component.GetType()))
                continue;

            if (comp.AlreadyKnownMessage != null)
                _popup.PopupEntity(Loc.GetString(comp.AlreadyKnownMessage.Value), uid, user, PopupType.Small);

            return;
        }

        if (!_prototype.TryIndex<TraitPrototype>(comp.Trait, out var trait))
            return;

        _traits.AddTrait(user, trait);

        if (comp.LearnMessage != null)
            _popup.PopupEntity(Loc.GetString(comp.LearnMessage.Value), uid, user, PopupType.Large);

        if (comp.SoundOnUse != null)
            _audio.PlayEntity(comp.SoundOnUse, user, user);

        args.Handled = true;

        if (comp.MultiUse)
            return;

        if (!string.IsNullOrEmpty(comp.SpawnedProto))
            Spawn(comp.SpawnedProto, Transform(uid).Coordinates);

        QueueDel(uid);
    }
}
