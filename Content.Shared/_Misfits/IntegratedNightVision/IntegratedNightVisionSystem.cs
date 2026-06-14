using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Overlays.Switchable;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared._Misfits.IntegratedNightVision;

public sealed class IntegratedNightVisionSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IntegratedNightVisionHelmetComponent, GotEquippedEvent>(OnHelmetEquipped);
        SubscribeLocalEvent<IntegratedNightVisionHelmetComponent, GotUnequippedEvent>(OnHelmetUnequipped);

        SubscribeLocalEvent<IntegratedNightVisionHelmetComponent, ToggleNightVisionEvent>(OnToggle,
            after: new[] { typeof(NightVisionSystem) });

        SubscribeLocalEvent<IntegratedNightVisionArmorComponent, GotEquippedEvent>(OnArmorEquipped);
        SubscribeLocalEvent<IntegratedNightVisionArmorComponent, GotUnequippedEvent>(OnArmorUnequipped);
    }

    private bool WearerHasMatchingArmor(EntityUid wearer, string setId)
    {
        return _inventory.TryGetSlotEntity(wearer, "outerClothing", out var outer)
               && TryComp<IntegratedNightVisionArmorComponent>(outer, out var armor)
               && armor.SetId == setId;
    }

    private void ForceNightVisionOff(EntityUid helmetUid, EntityUid wearer)
    {
        if (!TryComp<NightVisionComponent>(helmetUid, out var nv) || !nv.IsActive)
            return;

        nv.IsActive = false;
        Dirty(helmetUid, nv);
        var ev = new SwitchableOverlayToggledEvent(wearer, false);
        RaiseLocalEvent(helmetUid, ref ev);
    }

    private void OnHelmetEquipped(EntityUid uid, IntegratedNightVisionHelmetComponent comp, GotEquippedEvent args)
    {
        comp.Wearer = args.Equipee;
    }

    private void OnHelmetUnequipped(EntityUid uid, IntegratedNightVisionHelmetComponent comp, GotUnequippedEvent args)
    {
        ForceNightVisionOff(uid, args.Equipee);
        comp.Wearer = null;
    }

    private void OnToggle(EntityUid uid, IntegratedNightVisionHelmetComponent comp, ToggleNightVisionEvent args)
    {
        if (comp.Wearer == null || !TryComp<NightVisionComponent>(uid, out var nv))
            return;

        if (!nv.IsActive)
            return;

        if (WearerHasMatchingArmor(comp.Wearer.Value, comp.SetId))
            return;

        nv.IsActive = false;
        Dirty(uid, nv);
        var ev = new SwitchableOverlayToggledEvent(comp.Wearer.Value, false);
        RaiseLocalEvent(uid, ref ev);

        if (_net.IsServer)
            _popup.PopupEntity(
                Loc.GetString("integrated-night-vision-requires-armor"),
                comp.Wearer.Value,
                comp.Wearer.Value);
    }

    private void OnArmorEquipped(EntityUid uid, IntegratedNightVisionArmorComponent comp, GotEquippedEvent args)
    {
        if (!_inventory.TryGetSlotEntity(args.Equipee, "head", out var helmet)
            || !TryComp<IntegratedNightVisionHelmetComponent>(helmet, out var helmetComp)
            || helmetComp.SetId != comp.SetId)
            return;

        if (TryComp<IntegratedNightVisionHelmetComponent>(helmet, out var hComp))
            hComp.Wearer = args.Equipee;
    }

    private void OnArmorUnequipped(EntityUid uid, IntegratedNightVisionArmorComponent comp, GotUnequippedEvent args)
    {
        if (!_inventory.TryGetSlotEntity(args.Equipee, "head", out var helmet)
            || !TryComp<IntegratedNightVisionHelmetComponent>(helmet, out var helmetComp)
            || helmetComp.SetId != comp.SetId)
            return;

        ForceNightVisionOff(helmet.Value, args.Equipee);
    }
}
