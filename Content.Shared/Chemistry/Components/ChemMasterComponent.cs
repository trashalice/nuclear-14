using Content.Shared.Chemistry;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Chemistry.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ChemMasterComponent : Component
{
    [DataField("pillType"), AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public uint PillType;

    [DataField("mode"), AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public ChemMasterMode Mode = ChemMasterMode.Transfer;

    [DataField("pillDosageLimit", required: true), AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public uint PillDosageLimit;

    [DataField("clickSound"), ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public int SortMethod;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public int TransferringAmount;
}
