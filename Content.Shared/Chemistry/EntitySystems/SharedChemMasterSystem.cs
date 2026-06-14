using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;

namespace Content.Shared.Chemistry.EntitySystems;

public abstract class SharedChemMasterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<ChemMasterComponent>(ChemMasterUiKey.Key, subs =>
        {
            subs.Event<ChemMasterSetModeMessage>(OnSetModeMessage);
            subs.Event<ChemMasterSetPillTypeMessage>(OnSetPillTypeMessage);
            subs.Event<ChemMasterSortMethodUpdated>(OnSortMethodUpdated);
            subs.Event<ChemMasterTransferringAmountUpdated>(OnTransferringAmountUpdated);
        });
    }

    protected virtual void OnSetModeMessage(Entity<ChemMasterComponent> ent, ref ChemMasterSetModeMessage msg)
    {
        if (!Enum.IsDefined(typeof(ChemMasterMode), msg.ChemMasterMode))
            return;
        ent.Comp.Mode = msg.ChemMasterMode;
        Dirty(ent);
    }

    protected virtual void OnSetPillTypeMessage(Entity<ChemMasterComponent> ent, ref ChemMasterSetPillTypeMessage msg)
    {
        if (msg.PillType > SharedChemMaster.PillTypes - 1)
            return;
        ent.Comp.PillType = msg.PillType;
        Dirty(ent);
    }

    protected virtual void OnSortMethodUpdated(Entity<ChemMasterComponent> ent, ref ChemMasterSortMethodUpdated msg)
    {
        ent.Comp.SortMethod = msg.SortMethod;
        Dirty(ent);
    }

    protected virtual void OnTransferringAmountUpdated(Entity<ChemMasterComponent> ent, ref ChemMasterTransferringAmountUpdated msg)
    {
        ent.Comp.TransferringAmount = msg.TransferringAmount;
        Dirty(ent);
    }
}
