using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._Misfits.Traits;
using Content.Shared.Body.Systems;


namespace Content.Server._Misfits.Traits;


public sealed class GibModifierSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GibModifierComponent, ComponentInit>(OnComponentInit);
    }

    private void OnComponentInit(Entity<GibModifierComponent> ent, ref ComponentInit args)
    {
        // set overall gib threshold for the entity
        if (TryComp(ent, out DestructibleComponent? entityDestructible))
        {
            foreach (var threshold in entityDestructible.Thresholds)
            {
                if (threshold.Trigger is DamageTypeTrigger trigger)
                {
                    trigger.Damage = (int)  (trigger.Damage * ent.Comp.GibThresholdMultiplier); // rounds down
                }
            }
        }

        if (!ent.Comp.Ungibbable)
        {
            RemComp<UngibbableComponent>(ent);
        }

        // set gib and sever thresholds for each bodypart
        foreach (var part in _body.GetBodyChildren(ent))
        {
            if (ent.Comp.Ungibbable) // Adamantium Skeleton
            {
                part.Component.CanSever = false;
                RemComp<DestructibleComponent>(part.Id);
                continue;
            }

            if (!TryComp(part.Id, out DestructibleComponent? partDestructible))
                continue;
            foreach (var threshold in partDestructible.Thresholds)
            {
                if (threshold.Trigger is DamageTypeTrigger trigger)
                {
                    trigger.Damage = (int)  (trigger.Damage * ent.Comp.GibThresholdMultiplier); // rounds down
                }
            }

            part.Component.CanSever = true; // beheading is back on the menu
            part.Component.SeverIntegrity = (part.Component.SeverIntegrity * ent.Comp.SeverThresholdMultiplier);
        }
    }

}
