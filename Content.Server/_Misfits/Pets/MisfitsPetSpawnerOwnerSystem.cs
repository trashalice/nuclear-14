// #Misfits Add - Relocates pet companions spawned via ghost role takeover to the
// perk owner's current position. Without this, pets spawn wherever the owner's
// character originally appeared, which is often empty/inaccessible by mid-round.

using Content.Server.Ghost.Roles.Events;

namespace Content.Server._Misfits.Pets;

/// <summary>
/// Listens for ghost-role spawner activations and, if the spawner is a tracked
/// pet companion spawner, teleports the freshly spawned pet next to the player
/// who originally bought the perk.
/// </summary>
public sealed class MisfitsPetSpawnerOwnerSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly PetCollarSystem _petCollars = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Broadcast subscription: the event is raised on the spawned mob, but we
        // need to read the spawner's component, so we resolve via args.Spawner.
        SubscribeLocalEvent<GhostRoleSpawnerUsedEvent>(OnSpawnerUsed);
    }

    private void OnSpawnerUsed(GhostRoleSpawnerUsedEvent args)
    {
        // Only act on pet spawners that have a tracked owner
        if (!TryComp<MisfitsPetSpawnerOwnerComponent>(args.Spawner, out var ownerComp))
            return;

        var ownerEnt = ownerComp.Owner;

        // Owner gone (disconnected, gibbed, deleted) - leave pet at spawner coords.
        if (!Exists(ownerEnt) || Deleted(ownerEnt) || TerminatingOrDeleted(ownerEnt))
            return;

        if (!TryComp<TransformComponent>(ownerEnt, out var ownerXform))
            return;

        // Move the spawned pet to the owner's current coordinates and re-anchor
        // to the proper grid/map (mirrors the AttachToGridOrMap call in GhostRoleSystem).
        _transform.SetCoordinates(args.Spawned, ownerXform.Coordinates);
        _transform.AttachToGridOrMap(args.Spawned);
        _petCollars.EquipDefaultCollar(args.Spawned);
    }
}
