using Content.Server.Movement.Systems;
using Content.Shared._Misfits.CCVar;
using Content.Shared._Misfits.Movement;
using Content.Shared.Actions;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Movement;

/// <summary>
/// Server-side lag compensation system. Piggybacks on the <c>LastRealTick</c> field
/// already stamped on <see cref="RequestShootEvent"/> and <see cref="RequestPerformActionEvent"/>
/// by the client, storing it per-session. Range-validation calls can then apply a small
/// tolerance margin when a player's action was sent from a behind-tick snapshot.
///
/// No separate heartbeat message is used — the tick is always read from the most recent
/// latency-sensitive event the player raised. This avoids the "Got late MsgEntity" spam
/// that arises when sending tick-stamped entity events on a periodic timer.
/// </summary>
public sealed class ServerMisfitsLagCompensationSystem : SharedMisfitsLagCompensationSystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly LagCompensationSystem _lagCompensation = default!;

    // Per-session last-real-tick extracted from the client's stamped events.
    private readonly Dictionary<NetUserId, GameTick> _lastRealTicks = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config,
            PerformanceCVars.LagCompensationMs,
            v => _lagCompensation.BufferTime = TimeSpan.FromMilliseconds(v),
            true);

        // Read LastRealTick directly from the events the client already sends for game actions.
        // This avoids needing a separate periodic heartbeat message (which caused "Got late MsgEntity" spam).
        SubscribeNetworkEvent<RequestShootEvent>(OnReceiveShootEvent);
        SubscribeNetworkEvent<RequestPerformActionEvent>(OnReceiveActionEvent);

        // Clean up stored ticks when a player disconnects to prevent a slow memory leak.
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        // Only clean up on true disconnect, not just detachment from a body.
        if (ev.Player.Status != Robust.Shared.Enums.SessionStatus.Disconnected)
            return;

        _lastRealTicks.Remove(ev.Player.UserId);
    }

    private void OnReceiveShootEvent(RequestShootEvent msg, EntitySessionEventArgs args)
    {
        if (msg.LastRealTick is not { } tick)
            return;

        // Store tick - 1: the last fully-received world state the client acted on.
        SetLastRealTick(args.SenderSession.UserId, tick - 1);
    }

    private void OnReceiveActionEvent(RequestPerformActionEvent msg, EntitySessionEventArgs args)
    {
        if (msg.LastRealTick is not { } tick)
            return;

        SetLastRealTick(args.SenderSession.UserId, tick - 1);
    }

    public void SetLastRealTick(NetUserId session, GameTick tick)
    {
        _lastRealTicks[session] = tick;
    }

    public override GameTick GetLastRealTick(ICommonSession? session)
    {
        if (session == null)
            return base.GetLastRealTick(session);

        return _lastRealTicks.GetValueOrDefault(session.UserId, base.GetLastRealTick(session));
    }

    /// <summary>
    /// Checks whether <paramref name="target"/> is within <paramref name="range"/> tiles of
    /// <paramref name="origin"/>, optionally adding <see cref="MarginTiles"/> if the session's
    /// last confirmed tick is behind the server by at most <see cref="MaxCompensationMs"/> ms.
    ///
    /// Use this instead of bare <c>TransformSystem.InRange</c> on any server-side handler that
    /// validates player-originated ranged interactions.
    /// </summary>
    public bool IsWithinRange(EntityUid origin, EntityUid target, ICommonSession session, float range)
    {
        return IsWithinMargin((origin, Transform(origin)), (target, Transform(target)), session, range);
    }

    public override (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        ICommonSession? session,
        TransformComponent? xform = null)
    {
        if (session == null)
            return _lagCompensation.GetCoordinatesAngle(uid, session, xform);

        return _lagCompensation.GetCoordinatesAngle(uid, GetLastRealTick(session), xform);
    }

    public override Angle GetAngle(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        return GetCoordinatesAngle(uid, session, xform).Angle;
    }

    public override EntityCoordinates GetCoordinates(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        return GetCoordinatesAngle(uid, session, xform).Coordinates;
    }

    public override (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        GameTick tick,
        TransformComponent? xform = null)
    {
        return _lagCompensation.GetCoordinatesAngle(uid, tick, xform);
    }

    public override Angle GetAngle(EntityUid uid, GameTick tick, TransformComponent? xform = null)
    {
        return _lagCompensation.GetCoordinatesAngle(uid, tick, xform).Angle;
    }

    public override EntityCoordinates GetCoordinates(EntityUid uid, GameTick tick, TransformComponent? xform = null)
    {
        return _lagCompensation.GetCoordinatesAngle(uid, tick, xform).Coordinates;
    }
}
