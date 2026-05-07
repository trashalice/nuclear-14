using Content.Goobstation.Common.DeviceNetwork;
using Content.Goobstation.Shared.DeviceNetwork;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;

namespace Content.Goobstation.Server.DeviceNetwork;

public sealed class DeviceCustomFrequencySystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<DeviceCustomFrequencyComponent, BeforeActivatableUIOpenEvent>(OnBeforeActivatableUIOpen);
        SubscribeLocalEvent<DeviceCustomFrequencyComponent, GetVerbsEvent<AlternativeVerb>>(AddUiVerb);

        Subs.BuiEvents<DeviceCustomFrequencyComponent>(DeviceCustomFrequencyUiKey.Key,
            subs =>
        {
            subs.Event<DeviceCustomFrequencyChangeMessage>(OnFrequencyChange);
        });
    }
    private void OnBeforeActivatableUIOpen(Entity<DeviceCustomFrequencyComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        if (!TryComp<DeviceNetworkComponent>(ent.Owner, out var device))
            return;

        var newState = new DeviceCustomFrequencyUserInterfaceState(device.ReceiveFrequency);
        _userInterface.SetUiState(ent.Owner, DeviceCustomFrequencyUiKey.Key, newState);
    }

    private void AddUiVerb(Entity<DeviceCustomFrequencyComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract) return;
        if (!TryComp<DeviceNetworkComponent>(ent.Owner, out var device))
            return;

        var uiOpen = _userInterface.IsUiOpen(ent.Owner, DeviceCustomFrequencyUiKey.Key, args.User);
        var user = args.User;
        if (!uiOpen)
        {
            AlternativeVerb verb = new()
            {
                Act = () =>
                {
                    var newState = new DeviceCustomFrequencyUserInterfaceState(device.ReceiveFrequency);
                    _userInterface.SetUiState(ent.Owner, DeviceCustomFrequencyUiKey.Key, newState);
                    _userInterface.OpenUi(ent.Owner, DeviceCustomFrequencyUiKey.Key, user);
                },
                Text = "Change Frequency"
            };
            args.Verbs.Add(verb);
        }
    }

    private void OnFrequencyChange(Entity<DeviceCustomFrequencyComponent> ent, ref DeviceCustomFrequencyChangeMessage args)
    {
        if (!ent.Comp.FrequencyChange
            || !TryComp<DeviceNetworkComponent>(ent.Owner, out var device)
            || args.Frequency > ent.Comp.MaxFrequency
            || args.Frequency < ent.Comp.MinFrequency)
            return;

        var oldFrequency = Comp<DeviceNetworkComponent>(ent.Owner).ReceiveFrequency;

        var ev = new DeviceNetworkFrequencyChangedEvent(oldFrequency, args.Frequency);
        RaiseLocalEvent(ent.Owner, ref ev);
        if (ev.Cancelled)
        {
            _popup.PopupEntity("Unable to set frequency. Try another frequency.", ent.Owner, Content.Shared.Popups.PopupType.Medium);
            return;
        }

        _deviceNetwork.SetReceiveFrequency(ent.Owner, args.Frequency, device);
        _deviceNetwork.SetTransmitFrequency(ent.Owner, args.Frequency, device);


        var newState = new DeviceCustomFrequencyUserInterfaceState(device.ReceiveFrequency);
        _userInterface.SetUiState(ent.Owner, DeviceCustomFrequencyUiKey.Key, newState);
    }
}
