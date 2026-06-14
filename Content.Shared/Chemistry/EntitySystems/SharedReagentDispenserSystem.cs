using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;

namespace Content.Shared.Chemistry.EntitySystems;

public abstract class SharedReagentDispenserSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<ReagentDispenserComponent>(ReagentDispenserUiKey.Key, subs =>
        {
            subs.Event<ReagentDispenserSetDispenseAmountMessage>(OnSetDispenseAmountMessage);
        });
    }

    protected virtual void OnSetDispenseAmountMessage(Entity<ReagentDispenserComponent> ent, ref ReagentDispenserSetDispenseAmountMessage msg)
    {
        ent.Comp.DispenseAmount = msg.ReagentDispenserDispenseAmount;
        Dirty(ent);
    }
}
