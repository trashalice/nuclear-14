using Content.Server.Access.Systems;
using Content.Shared._Misfits.Clothing.Pins;
using Content.Shared._Misfits.RankTitle;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.RankTitle;

/// <summary>
/// Applies the visible rank title from a neck-worn rank pin, or from a rank pin
/// attached to equipped inner/outer clothing.
/// </summary>
public sealed class RankTitleSystem : EntitySystem
{
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RankTitleComponent, GotEquippedEvent>(OnEquip);
        SubscribeLocalEvent<RankTitleComponent, GotUnequippedEvent>(OnUnequip);
        SubscribeLocalEvent<ClothingPinHolderComponent, GotEquippedEvent>(OnPinHolderEquip);
        SubscribeLocalEvent<ClothingPinHolderComponent, GotUnequippedEvent>(OnPinHolderUnequip);
        SubscribeLocalEvent<ClothingPinHolderComponent, EntInsertedIntoContainerMessage>(OnPinHolderContainerChanged);
        SubscribeLocalEvent<ClothingPinHolderComponent, EntRemovedFromContainerMessage>(OnPinHolderContainerChanged);
        SubscribeLocalEvent<InventoryComponent, ExaminedEvent>(OnExamined);
    }

    private void OnEquip(EntityUid uid, RankTitleComponent comp, GotEquippedEvent args)
        => RefreshRankTitle(args.Equipee);

    private void OnUnequip(EntityUid uid, RankTitleComponent comp, GotUnequippedEvent args)
        => RefreshRankTitle(args.Equipee);

    private void OnPinHolderEquip(EntityUid uid, ClothingPinHolderComponent comp, GotEquippedEvent args)
        => RefreshRankTitle(args.Equipee);

    private void OnPinHolderUnequip(EntityUid uid, ClothingPinHolderComponent comp, GotUnequippedEvent args)
        => RefreshRankTitle(args.Equipee);

    private void OnPinHolderContainerChanged(EntityUid uid, ClothingPinHolderComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (TryGetWearer(uid, out var wearer))
            RefreshRankTitle(wearer);
    }

    private void OnPinHolderContainerChanged(EntityUid uid, ClothingPinHolderComponent comp, EntRemovedFromContainerMessage args)
    {
        if (TryGetWearer(uid, out var wearer))
            RefreshRankTitle(wearer);
    }

    private void RefreshRankTitle(EntityUid wearer)
    {
        if (TryGetRankPin(wearer, out var rankPin) &&
            TryComp<RankTitleComponent>(rankPin, out var rankComp))
        {
            SetRankTitle(wearer, rankComp.RankTitle);
            return;
        }

        SetRankTitle(wearer, null);
    }

    private void SetRankTitle(EntityUid wearer, string? rankTitle)
    {
        if (!_idCard.TryFindIdCard(wearer, out var idCard))
            return;

        idCard.Comp.LocalizedJobTitle = rankTitle;
    }

    private void OnExamined(EntityUid uid, InventoryComponent _, ExaminedEvent args)
    {
        if (!TryGetRankPin(uid, out var rankPin) ||
            !TryComp<RankTitleComponent>(rankPin, out var rankComp))
            return;

        var rankTitle = FormattedMessage.EscapeText(rankComp.RankTitle);
        using (args.PushGroup(nameof(RankTitleComponent)))
        {
            args.PushMarkup(Loc.GetString("rank-pin-examine",
                ("user", Identity.Entity(uid, EntityManager)),
                ("rank", rankTitle)));
        }
    }

    private bool TryGetRankPin(EntityUid wearer, out EntityUid rankPin)
    {
        rankPin = default;

        if (_inventory.TryGetSlotEntity(wearer, "neck", out var neckItem) &&
            neckItem != null &&
            HasComp<RankTitleComponent>(neckItem.Value))
        {
            rankPin = neckItem.Value;
            return true;
        }

        var slots = _inventory.GetSlotEnumerator(wearer, SlotFlags.INNERCLOTHING | SlotFlags.OUTERCLOTHING);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity == null ||
                !TryComp<ClothingPinHolderComponent>(slot.ContainedEntity.Value, out var holder) ||
                !_containers.TryGetContainer(slot.ContainedEntity.Value, holder.ContainerId, out var container))
            {
                continue;
            }

            foreach (var pin in container.ContainedEntities)
            {
                if (!HasComp<RankTitleComponent>(pin))
                    continue;

                rankPin = pin;
                return true;
            }
        }

        return false;
    }

    private bool TryGetWearer(EntityUid clothing, out EntityUid wearer)
    {
        wearer = default;

        if (!_inventory.TryGetContainingSlot(clothing, out _) ||
            !_containers.TryGetContainingContainer((clothing, (TransformComponent?) null, (MetaDataComponent?) null), out var container) ||
            !HasComp<InventoryComponent>(container.Owner))
        {
            return false;
        }

        wearer = container.Owner;
        return true;
    }
}
