using Content.Server._Misfits.Pets;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.Components;
using Content.Shared._Misfits.Special;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

// #Misfits Add - TraitSpawnEntity: spawns a separate entity at the player's position when the trait activates.
// Used for pet companion traits. If the player has charisma >= CharismaNeutralFollowerMinimum, the companion
// mob is spawned immediately as an AI NPC & auto recruited as a follower. Otherwise a ghost-role spawner
// is left so another player can take the role manually.

namespace Content.Server._Misfits.Traits;

/// <summary>
///     Spawns one or more entities at the player's location when the trait is applied.
///     High charisma players get an immediately-spawned companion
///     Low charisma players get a ghost role spawner that a ghost player can take instead.
/// </summary>
[UsedImplicitly]
public sealed partial class TraitSpawnEntity : TraitFunction
{
    /// <summary>
    ///     Prototype IDs to spawn at the player's coordinates.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Prototypes { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var xform = entityManager.GetComponent<TransformComponent>(uid);
        var coords = xform.Coordinates;

        var special = EntitySystem.Get<SharedSpecialSystem>();
        var charisma = special.GetEffective(uid, SpecialStat.Charisma);
        var canAutoSpawn = charisma >= special.GetTuning().CharismaNeutralFollowerMinimum;

        foreach (var proto in Prototypes)
        {
            var spawner = entityManager.SpawnEntity(proto, coords);

            if (!entityManager.TryGetComponent<GhostRoleMobSpawnerComponent>(spawner, out var mobSpawner)
                || mobSpawner.Prototype is not { } mobProto)
            {
                // Spawner exists but has no valid prototype
                if (entityManager.HasComponent<GhostRoleMobSpawnerComponent>(spawner))
                {
                    var ownerComp = entityManager.EnsureComponent<MisfitsPetSpawnerOwnerComponent>(spawner);
                    ownerComp.Owner = uid;
                }
                continue;
            }

            if (canAutoSpawn)
            {
                var mob = entityManager.SpawnEntity(mobProto, coords);
                entityManager.DeleteEntity(spawner);
                entityManager.AddComponent(mob, new FollowerAutoRecruitComponent { Commander = uid });
                EntitySystem.Get<PetCollarSystem>().EquipDefaultCollar(mob);
            }
            else
            {
                var ownerComp = entityManager.EnsureComponent<MisfitsPetSpawnerOwnerComponent>(spawner);
                ownerComp.Owner = uid;
            }
        }
    }
}
