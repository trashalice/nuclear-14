using Content.Server.Administration;
using Content.Shared.Access.Components;
using Content.Shared.Access.Events;
using Content.Shared.Access.Systems;
using Content.Shared._Misfits.Pets;
using Content.Shared.Administration;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Pets;

public sealed class PetCollarSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly SharedIdCardSystem _idCards = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PetCollarHolderComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<PetCollarHolderComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<PetCollarHolderComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<IdCardComponent, IdCardFullNameChangedEvent>(OnCardNameChanged);
    }

    public void EquipDefaultCollar(EntityUid pet)
    {
        if (!TryComp<PetCollarHolderComponent>(pet, out _))
            return;

        if (_inventory.TryGetSlotEntity(pet, "id", out var existing) && existing.HasValue && Exists(existing.Value))
            return;

        var collar = Spawn("N14BellCollar", Transform(pet).Coordinates);

        if (!_inventory.TryEquip(pet, collar, "id", silent: true, force: true))
        {
            QueueDel(collar);
            return;
        }

        RefreshPetName(pet);
    }

    private void OnGetVerbs(EntityUid uid, PetCollarHolderComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (!_inventory.TryGetSlotEntity(uid, "id", out var collarUid) || !TryComp<IdCardComponent>(collarUid, out _))
            return;

        args.Verbs.Add(new Verb
        {
            Text = "Rename pet",
            Category = VerbCategory.Interaction,
            Act = () =>
            {
                _quickDialog.OpenDialog(actor.PlayerSession, "Rename pet", "Pet name", (string name) =>
                {
                    if (!Exists(uid) || !Exists(collarUid))
                        return;

                    if (!_inventory.TryGetSlotEntity(uid, "id", out var currentCollar) || currentCollar != collarUid)
                        return;

                    if (!_idCards.TryChangeFullName(collarUid.Value, name, player: args.User))
                        return;

                    RefreshPetName(uid);
                });
            }
        });
    }

    private void OnItemInserted(EntityUid uid, PetCollarHolderComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != "id")
            return;

        RefreshPetName(uid);
    }

    private void OnItemRemoved(EntityUid uid, PetCollarHolderComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != "id")
            return;

        RefreshPetName(uid);
    }

    private void OnCardNameChanged(EntityUid uid, IdCardComponent idCard, IdCardFullNameChangedEvent ev)
    {
        if (!TryComp<TransformComponent>(uid, out var xform))
            return;

        var pet = xform.ParentUid;
        if (!TryComp<PetCollarHolderComponent>(pet, out _))
            return;

        RefreshPetName(pet);
    }

    private void RefreshPetName(EntityUid pet)
    {
        if (!TryComp<PetCollarHolderComponent>(pet, out _))
            return;

        if (_inventory.TryGetSlotEntity(pet, "id", out var collarUid) &&
            TryComp<IdCardComponent>(collarUid, out var collar) &&
            !string.IsNullOrWhiteSpace(collar.FullName))
        {
            _meta.SetEntityName(pet, collar.FullName);
            return;
        }

        if (TryComp<MetaDataComponent>(pet, out var meta) && meta.EntityPrototype != null)
            _meta.SetEntityName(pet, meta.EntityPrototype.Name);
    }
}
