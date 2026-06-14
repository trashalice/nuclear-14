using Content.Shared._Misfits.Clothing.Pins;
using Content.Shared.Clothing.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Clothing.Pins;

/// <summary>
/// Lets pin items be attached to inner or outer clothing, mirroring RMC's
/// uniform accessory interaction model without requiring every clothing proto
/// to define a holder component up front.
/// </summary>
public sealed class ClothingPinSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly SlotFlags PinSlots = SlotFlags.INNERCLOTHING | SlotFlags.OUTERCLOTHING;

    public override void Initialize()
    {
        SubscribeLocalEvent<ClothingComponent, InteractUsingEvent>(OnClothingInteractUsing);
        SubscribeLocalEvent<ClothingPinHolderComponent, GetVerbsEvent<EquipmentVerb>>(OnGetEquipmentVerbs);
        SubscribeLocalEvent<ClothingPinHolderComponent, ComponentInit>(OnHolderInit);
    }

    private void OnHolderInit(Entity<ClothingPinHolderComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
    }

    private void OnClothingInteractUsing(Entity<ClothingComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<ClothingPinComponent>(args.Used, out var pin))
            return;

        args.Handled = TryAttachPin(args.Used, pin, ent, args.User);
    }

    private void OnGetEquipmentVerbs(Entity<ClothingPinHolderComponent> ent, ref GetVerbsEvent<EquipmentVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!_containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container) ||
            container.ContainedEntities.Count == 0)
        {
            return;
        }

        foreach (var pin in container.ContainedEntities)
        {
            var user = args.User;
            args.Verbs.Add(new EquipmentVerb
            {
                Text = Loc.GetString("clothing-pin-remove-verb", ("pin", Name(pin))),
                IconEntity = GetNetEntity(pin),
                Act = () => TryRemovePin(ent, pin, user),
            });
        }
    }

    public bool TryAttachPin(EntityUid pinUid, ClothingPinComponent pin, Entity<ClothingComponent> clothingEnt, EntityUid user)
    {
        if (!TryComp<ClothingComponent>(pinUid, out _))
            return false;

        if ((clothingEnt.Comp.Slots & PinSlots) == SlotFlags.NONE)
        {
            _popup.PopupEntity(Loc.GetString("clothing-pin-attach-fail-slot"), user, user, PopupType.SmallCaution);
            return false;
        }

        var holder = EnsureComp<ClothingPinHolderComponent>(clothingEnt.Owner);
        var container = _containers.EnsureContainer<Container>(clothingEnt.Owner, holder.ContainerId);

        if (!holder.AllowedCategories.Contains(pin.Category))
        {
            _popup.PopupEntity(Loc.GetString("clothing-pin-attach-fail-category"), user, user, PopupType.SmallCaution);
            return false;
        }

        var sameCategory = 0;
        foreach (var contained in container.ContainedEntities)
        {
            if (TryComp<ClothingPinComponent>(contained, out var containedPin) &&
                containedPin.Category == pin.Category)
            {
                sameCategory++;
            }
        }

        if (sameCategory >= pin.Limit)
        {
            _popup.PopupEntity(Loc.GetString("clothing-pin-attach-fail-limit"), user, user, PopupType.SmallCaution);
            return false;
        }

        if (!_containers.Insert(pinUid, container))
            return false;

        _item.VisualsChanged(clothingEnt.Owner);
        _popup.PopupEntity(Loc.GetString("clothing-pin-attached",
            ("pin", Name(pinUid)),
            ("clothing", Name(clothingEnt.Owner))), user, user);

        return true;
    }

    private void TryRemovePin(Entity<ClothingPinHolderComponent> holder, EntityUid pin, EntityUid user)
    {
        if (!_containers.TryGetContainer(holder, holder.Comp.ContainerId, out var container) ||
            !container.Contains(pin) ||
            !_containers.Remove(pin, container))
        {
            return;
        }

        _hands.PickupOrDrop(user, pin);
        _item.VisualsChanged(holder.Owner);
    }
}
