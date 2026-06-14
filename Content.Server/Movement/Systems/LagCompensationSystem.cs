using Content.Server.Movement.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Movement.Systems;

/// <summary>
/// Stores a buffer of previous positions of the relevant entity.
/// Can be used to check the entity's position at a recent point in time.
/// </summary>
public sealed class LagCompensationSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    // I figured 500 ping is max, so 1.5 is 750.
    // Max ping I've had is 350ms from aus to spain.
    public TimeSpan BufferTime { get; set; } = TimeSpan.FromMilliseconds(750);

    public override void Initialize()
    {
        base.Initialize();
        Log.Level = LogLevel.Info;
        SubscribeLocalEvent<LagCompensationComponent, MoveEvent>(OnLagMove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var bufferTicks = Math.Max(1, (int) Math.Ceiling(BufferTime.TotalSeconds * _timing.TickRate));
        var earliestTick = _timing.CurTick - (uint) bufferTicks;

        // Cull any old ones from active updates
        // Probably fine to include ignored.
        var query = AllEntityQuery<LagCompensationComponent>();

        while (query.MoveNext(out var comp))
        {
            while (comp.Positions.TryPeek(out var pos))
            {
                if (pos.Tick < earliestTick)
                {
                    comp.Positions.Dequeue();
                    continue;
                }

                break;
            }
        }
    }

    private void OnLagMove(EntityUid uid, LagCompensationComponent component, ref MoveEvent args)
    {
        if (!args.NewPosition.EntityId.IsValid())
            return; // probably being sent to nullspace for deletion.

        component.Positions.Enqueue((_timing.CurTick, args.NewPosition, args.NewRotation));
    }

    public (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid, ICommonSession? pSession,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform))
            return (EntityCoordinates.Invalid, Angle.Zero);

        if (pSession == null)
            return (xform.Coordinates, xform.LocalRotation);

        var ping = pSession.Ping;
        var deltaTicks = Math.Max(1, (int) Math.Ceiling(ping * 1.5 / 1000f * _timing.TickRate));
        var targetTick = _timing.CurTick - (uint) deltaTicks;
        return GetCoordinatesAngle(uid, targetTick, xform);
    }

    public (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid, GameTick targetTick,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform))
            return (EntityCoordinates.Invalid, Angle.Zero);

        if (!TryComp<LagCompensationComponent>(uid, out var lag) || lag.Positions.Count == 0)
            return (xform.Coordinates, xform.LocalRotation);

        var angle = xform.LocalRotation;
        var coordinates = xform.Coordinates;
        var found = false;

        foreach (var pos in lag.Positions)
        {
            coordinates = pos.Coordinates;
            angle = pos.Angle;
            found = true;

            if (pos.Tick >= targetTick)
                break;
        }

        if (!found)
        {
            Log.Debug($"No long comp coords found, using {xform.Coordinates}");
            coordinates = xform.Coordinates;
            angle = xform.LocalRotation;
        }
        else
        {
            Log.Debug($"Actual coords is {xform.Coordinates} and got {coordinates}");
        }

        return (coordinates, angle);
    }

    public Angle GetAngle(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        var (_, angle) = GetCoordinatesAngle(uid, session, xform);
        return angle;
    }

    public EntityCoordinates GetCoordinates(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        var (coordinates, _) = GetCoordinatesAngle(uid, session, xform);
        return coordinates;
    }
}
