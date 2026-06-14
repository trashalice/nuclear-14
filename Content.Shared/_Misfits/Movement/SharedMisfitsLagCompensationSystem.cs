using Content.Shared._Misfits.CCVar;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Movement;

public abstract class SharedMisfitsLagCompensationSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<ActorComponent> _actorQuery;
    private int _substeps;
    private float _substepTime;

    public float MarginTiles { get; private set; }
    public int MaxCompensationMs { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        _actorQuery = GetEntityQuery<ActorComponent>();

        Subs.CVar(_config, PerformanceCVars.LagCompensationMarginTiles, v => MarginTiles = v, true);
        Subs.CVar(_config, PerformanceCVars.LagCompensationMs, v => MaxCompensationMs = v, true);
        Subs.CVar(_config, CVars.NetTickrate, UpdateSubsteps, true);
        Subs.CVar(_config, CVars.TargetMinimumTickrate, UpdateSubsteps, true);
    }

    private void UpdateSubsteps(int _)
    {
        var targetMinTickrate = (float) _config.GetCVar(CVars.TargetMinimumTickrate);
        var serverTickrate = (float) _config.GetCVar(CVars.NetTickrate);
        _substeps = Math.Max(1, (int) Math.Ceiling(targetMinTickrate / serverTickrate));
        _substepTime = 1.0f / serverTickrate / _substeps;
    }

    public virtual (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        ICommonSession? session,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform))
            return (EntityCoordinates.Invalid, Angle.Zero);

        return (xform.Coordinates, xform.LocalRotation);
    }

    public virtual Angle GetAngle(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        var (_, angle) = GetCoordinatesAngle(uid, session, xform);
        return angle;
    }

    public virtual EntityCoordinates GetCoordinates(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        var (coordinates, _) = GetCoordinatesAngle(uid, session, xform);
        return coordinates;
    }

    public virtual (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        GameTick tick,
        TransformComponent? xform = null)
    {
        return GetCoordinatesAngle(uid, (ICommonSession?) null, xform);
    }

    public virtual Angle GetAngle(EntityUid uid, GameTick tick, TransformComponent? xform = null)
    {
        var (_, angle) = GetCoordinatesAngle(uid, tick, xform);
        return angle;
    }

    public virtual EntityCoordinates GetCoordinates(EntityUid uid, GameTick tick, TransformComponent? xform = null)
    {
        var (coordinates, _) = GetCoordinatesAngle(uid, tick, xform);
        return coordinates;
    }

    public EntityCoordinates GetCoordinates(EntityUid uid, EntityUid? session, TransformComponent? xform = null)
    {
        if (!_actorQuery.TryComp(session, out var actor))
            return GetCoordinates(uid, (ICommonSession?) null, xform);

        return GetCoordinates(uid, actor.PlayerSession, xform);
    }

    public virtual GameTick GetLastRealTick(ICommonSession? session)
    {
        return _timing.CurTick;
    }

    public virtual GameTick GetLastRealTick(EntityUid ent)
    {
        if (!_actorQuery.TryComp(ent, out var actor))
            return _timing.CurTick;

        return GetLastRealTick(actor.PlayerSession);
    }

    public bool IsWithinMargin(Entity<TransformComponent?> origin,
        Entity<TransformComponent?> target,
        ICommonSession? session,
        float range)
    {
        var targetCoords = GetCoordinates(target.Owner, session, target.Comp);
        var targetCurrentCoords = target.Comp?.Coordinates ?? Transform(target).Coordinates;
        var originCoords = origin.Comp?.Coordinates ?? Transform(origin).Coordinates;
        if (_net.IsServer &&
            ShouldApplyRangeMargin(session) &&
            !_transform.InRange(targetCoords, targetCurrentCoords, 0.01f))
        {
            range += MarginTiles;
        }

        return _transform.InRange(originCoords, targetCoords, range);
    }

    public bool ShouldApplyRangeMargin(ICommonSession? session)
    {
        if (!_net.IsServer || session == null)
            return false;

        var storedTick = GetLastRealTick(session);
        var tickDelta = (int) (_timing.CurTick.Value - storedTick.Value);
        var msLag = tickDelta * (1000f / _timing.TickRate);
        return msLag > 0 && msLag <= MaxCompensationMs;
    }

    public int? GetCurrentSubstep()
    {
        if (_substepTime <= 0f || _physics.EffectiveCurTime is not { } physicsTime)
            return null;

        var diff = physicsTime - _timing.CurTime;
        return (int) Math.Round(diff.TotalSeconds / _substepTime);
    }

    public int GetSubsteps()
    {
        return _substeps;
    }

    public int GetClientSubstep()
    {
        return GetCurrentSubstep() ?? 0;
    }
}
