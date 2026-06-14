using Content.Shared._Misfits.Movement;
using Robust.Client.Timing;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Movement;

/// <summary>
/// Client-side lag compensation system. Reads the engine's last-confirmed real tick via
/// <see cref="IClientGameTiming.LastRealTick"/> and exposes it for client prediction code
/// (gun fire, action use) to stamp onto outgoing events.
///
/// The stamped tick is piggybacked on <c>RequestShootEvent</c> and <c>RequestPerformActionEvent</c>
/// which are already sent as predictive events — no separate periodic message is needed,
/// avoiding the "Got late MsgEntity" warning caused by tick-stamped entity events on a timer.
/// </summary>
public sealed class MisfitsLagCompensationSystem : SharedMisfitsLagCompensationSystem
{
    [Dependency] private readonly IClientGameTiming _clientTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, Queue<(GameTick Tick, EntityCoordinates Coordinates, Angle Angle)>> _positions = new();

    /// <summary>
    /// Returns the client's last confirmed engine tick. Stamp this onto any outgoing
    /// event that the server will use for lag-compensated range validation.
    /// </summary>
    public GameTick GetLastRealTick() => _clientTiming.LastRealTick;

    public override void Initialize()
    {
        base.Initialize();

        _transform.OnGlobalMoveEvent += OnGlobalMove;
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _transform.OnGlobalMoveEvent -= OnGlobalMove;
        _positions.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var bufferTicks = Math.Max(1, (int) Math.Ceiling(MaxCompensationMs / 1000f * _clientTiming.TickRate)) + 2;
        var earliestTick = _clientTiming.CurTick - (uint) bufferTicks;

        foreach (var history in _positions.Values)
        {
            while (history.TryPeek(out var pos) && pos.Tick < earliestTick)
            {
                history.Dequeue();
            }
        }
    }

    private void OnGlobalMove(ref MoveEvent args)
    {
        if (!args.NewPosition.EntityId.IsValid())
            return;

        var history = _positions.GetValueOrDefault(args.Sender);
        if (history == null)
        {
            history = new Queue<(GameTick Tick, EntityCoordinates Coordinates, Angle Angle)>();
            _positions[args.Sender] = history;
        }

        history.Enqueue((_clientTiming.CurTick, args.NewPosition, args.NewRotation));
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        _positions.Remove(args.Entity);
    }

    public override GameTick GetLastRealTick(ICommonSession? session)
    {
        return _clientTiming.LastRealTick;
    }

    public override (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        GameTick tick,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform, false))
            return (EntityCoordinates.Invalid, Angle.Zero);

        if (!_positions.TryGetValue(uid, out var history) || history.Count == 0)
            return (xform.Coordinates, xform.LocalRotation);

        var coordinates = xform.Coordinates;
        var angle = xform.LocalRotation;
        var found = false;

        foreach (var pos in history)
        {
            coordinates = pos.Coordinates;
            angle = pos.Angle;
            found = true;

            if (pos.Tick >= tick)
                break;
        }

        if (!found)
            return (xform.Coordinates, xform.LocalRotation);

        return (coordinates, angle);
    }
}
