using Content.Shared._Misfits.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Spawners;

namespace Content.Server._Misfits.Storage;

public sealed class PauseTimedDespawnInEntityStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PauseTimedDespawnInEntityStorageComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PauseTimedDespawnInEntityStorageComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<PauseTimedDespawnInEntityStorageComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnStartup(EntityUid uid, PauseTimedDespawnInEntityStorageComponent pauseComp, ComponentStartup _)
    {
        if (_container.IsEntityInContainer(uid))
            PauseDespawn(uid, pauseComp);
    }

    private void OnInserted(EntityUid uid, PauseTimedDespawnInEntityStorageComponent pauseComp, EntGotInsertedIntoContainerMessage _)
    {
        PauseDespawn(uid, pauseComp);
    }

    private void PauseDespawn(EntityUid uid, PauseTimedDespawnInEntityStorageComponent pauseComp)
    {
        if (!TryComp<TimedDespawnComponent>(uid, out var timedDespawn))
            return;
        // Keep compost from disappearing while a crate is intentionally storing it.
        pauseComp.PausedLifetime = timedDespawn.Lifetime;
        RemComp<TimedDespawnComponent>(uid);
    }

    private void OnRemoved(EntityUid uid, PauseTimedDespawnInEntityStorageComponent pauseComp, EntGotRemovedFromContainerMessage _)
    {
        if (MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating ||
            pauseComp.PausedLifetime is not { } pausedLifetime)
        {
            return;
        }

        var timedDespawn = EnsureComp<TimedDespawnComponent>(uid);
        timedDespawn.Lifetime = pausedLifetime;
        pauseComp.PausedLifetime = null;
    }
}
