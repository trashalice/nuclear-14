// #Misfits Change - Wasteland Map server system
using System;
using System.Collections.Generic;
using Content.Server.Access.Components;
using Content.Server.Chat.Managers; // #Misfits Add - faction death alert chat dispatch
using Content.Server._Misfits.Group; // #Misfits Add - group blip injection
using Content.Server._Misfits.TribalHunt;
using Content.Shared.Access.Components;
using Content.Shared.Humanoid; // #Misfits Add - Followers casualty filter for humanoid player bodies only
using Content.Shared.Mind; // #Misfits Add - MindComponent (OriginalOwnerUserId player check)
using Content.Shared.Mind.Components; // #Misfits Add - MindContainerComponent
using Content.Shared.Mobs; // #Misfits Add - MobState, MobStateChangedEvent
using Content.Shared.Mobs.Components; // #Misfits Add - MobStateComponent
using Content.Shared.Mobs.Systems; // #Misfits Add - MobStateSystem
using Content.Shared.Tag;
using Content.Shared._Misfits.WastelandMap;
using Content.Shared._Misfits.TribalHunt;
using Content.Shared.NPC.Components; // NpcFactionMemberComponent
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player; // #Misfits Add - ActorComponent for faction filter iteration

namespace Content.Server._Misfits.WastelandMap;

/// <summary>
/// Sends the WastelandMap state (including world bounds) to the client BUI
/// when the UI is opened. Box2 is not NetSerializable, so we unpack it into
/// 4 floats inside the BUI state.
/// </summary>
public sealed class WastelandMapSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly GroupSystem _groupSystem = default!; // #Misfits Add - group member map blips
    // #Misfits Add - Followers dead body tracking & death alerts
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    private const int MaxSharedAnnotations = 128;
    private const int MaxStrokePoints = 512; // 256 UV points × 2 floats each
    // #Misfits Fix: Slowed from 0.5 s — the map is informational, not real-time.
    // GetIdCardBlips does a global PresetIdCard world-scan every update; 2.5 s is imperceptible to players.
    private const float UpdateInterval = 2.5f;
    private float _updateAccumulator;
    private readonly Dictionary<(MapId MapId, WastelandMapTacticalFeedKind Feed), List<WastelandMapAnnotation>> _sharedFeedAnnotations = new();

    // #Misfits Add - Scratch buffer for Followers death-alert session dispatch.
    private readonly List<ICommonSession> _followerSessionScratch = new();

    // #Misfits Add - Scratch buffers + tick-local cache for BuildState.
    // At 150 pop with many open wasteland maps, the 2.5s sweep was the single hottest user-
    // facing UI allocator. These buffers are reused per Update sweep; the _nonActorCache
    // holds faction/tribal blips keyed by (mapId, feed) so multiple map entities with the
    // same feed only pay for one world-scan per sweep.
    private readonly List<WastelandMapTrackedBlip> _blipScratch = new();
    private readonly List<WastelandMapTrackedBlip> _groupScratch = new();
    private readonly Dictionary<(MapId MapId, WastelandMapTacticalFeedKind Feed), WastelandMapTrackedBlip[]> _nonActorCache = new();
    private bool _inUpdateSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WastelandMapComponent, AfterActivatableUIOpenEvent>(OnAfterOpen);
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapAddAnnotationMessage>(OnAddAnnotationMessage);
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapRemoveAnnotationMessage>(OnRemoveAnnotationMessage);
        SubscribeLocalEvent<WastelandMapComponent, WastelandMapClearAnnotationsMessage>(OnClearAnnotationsMessage);
        // #Misfits Add - notify Followers players when a player humanoid dies
        SubscribeLocalEvent<MindContainerComponent, MobStateChangedEvent>(OnMindedEntityMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;

        _updateAccumulator = 0f;

        // #Misfits Add - Open a sweep window so BuildState can cache the non-actor blip
        // portion per (mapId, feed) across multiple map entities on this tick.
        _nonActorCache.Clear();
        _inUpdateSweep = true;
        try
        {
            var query = EntityQueryEnumerator<WastelandMapComponent, UserInterfaceComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var map, out var ui, out var xform))
            {
                // #Misfits Fix: Skip the expensive BUI rebuild when nobody has this map open.
                // GetActors() is O(1) with the early-out; the rebuild + GetIdCardBlips world-scan is O(all id cards).
                var viewerMap = xform.MapID;
                EntityUid? firstActor = null;
                foreach (var actor in _uiSystem.GetActors((uid, ui), WastelandMapUiKey.Key))
                {
                    viewerMap = Transform(actor).MapID;
                    firstActor = actor; // #Misfits Add - pass actor so group blips are relative to who holds the map
                    break;
                }
                if (firstActor == null)
                    continue;

                _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key, BuildState(map, viewerMap, actor: firstActor));
            }
        }
        finally
        {
            _inUpdateSweep = false;
            _nonActorCache.Clear();
        }
    }

    private void OnAfterOpen(EntityUid uid, WastelandMapComponent comp, AfterActivatableUIOpenEvent args)
    {
        var userMap = Transform(args.User).MapID;
        // #Misfits Add - pass the user so group member blips are seeded correctly on open
        _uiSystem.SetUiState(uid, WastelandMapUiKey.Key, BuildState(comp, userMap, actor: args.User));
    }

    private void OnAddAnnotationMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapAddAnnotationMessage args)
    {
        if (!TryAddAnnotation(args.Actor, comp, Transform(args.Actor).MapID, args.Annotation))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    private void OnRemoveAnnotationMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapRemoveAnnotationMessage args)
    {
        if (!TryRemoveAnnotation(args.Actor, comp, Transform(args.Actor).MapID, args.Index))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    private void OnClearAnnotationsMessage(EntityUid uid, WastelandMapComponent comp, WastelandMapClearAnnotationsMessage args)
    {
        if (!TryClearAnnotations(args.Actor, comp, Transform(args.Actor).MapID))
            return;

        UpdateMapUi(uid, comp, Transform(args.Actor).MapID);
    }

    // #Misfits Add - optional actor param so group-member blips can be injected per-viewer
    public WastelandMapBoundUserInterfaceState BuildState(WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind? feedOverride = null, EntityUid? actor = null)
    {
        var feed = feedOverride ?? GetEffectiveFeed(comp);
        var trackedBlips = GetTrackedBlips(feed, mapId, comp.WorldBounds, actor);
        var sharedAnnotations = GetSharedAnnotations(comp, mapId, feed).ToArray();

        return new WastelandMapBoundUserInterfaceState(
            comp.MapTitle,
            comp.MapTexturePath.ToString(),
            comp.CompactHud,
            comp.WorldBounds.Left,
            comp.WorldBounds.Bottom,
            comp.WorldBounds.Right,
            comp.WorldBounds.Top,
            trackedBlips,
            sharedAnnotations);
    }

    public WastelandMapTacticalFeedKind GetEffectiveFeed(WastelandMapComponent comp)
    {
        if (comp.TacticalFeed != WastelandMapTacticalFeedKind.None)
            return comp.TacticalFeed;

        return comp.TrackBrotherhoodHolotags
            ? WastelandMapTacticalFeedKind.Brotherhood
            : WastelandMapTacticalFeedKind.None;
    }

    public bool TryAddAnnotation(EntityUid actor, WastelandMapComponent comp, MapId mapId, WastelandMapAnnotation annotation, WastelandMapTacticalFeedKind? feedOverride = null)
    {
        var sanitized = SanitizeAnnotation(annotation);
        if (sanitized == null)
            return false;

        var annotations = GetSharedAnnotations(comp, mapId, feedOverride ?? GetEffectiveFeed(comp));
        annotations.Add(sanitized.Value);
        if (annotations.Count > MaxSharedAnnotations)
            annotations.RemoveAt(0);

        return true;
    }

    public bool TryRemoveAnnotation(EntityUid actor, WastelandMapComponent comp, MapId mapId, int index, WastelandMapTacticalFeedKind? feedOverride = null)
    {
        var annotations = GetSharedAnnotations(comp, mapId, feedOverride ?? GetEffectiveFeed(comp));
        if (index < 0 || index >= annotations.Count)
            return false;

        annotations.RemoveAt(index);
        return true;
    }

    public bool TryClearAnnotations(EntityUid actor, WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind? feedOverride = null)
    {
        var annotations = GetSharedAnnotations(comp, mapId, feedOverride ?? GetEffectiveFeed(comp));
        if (annotations.Count == 0)
            return false;

        annotations.Clear();
        return true;
    }

    private void UpdateMapUi(EntityUid uid, WastelandMapComponent comp, MapId? mapId = null)
    {
        if (!TryComp<UserInterfaceComponent>(uid, out var ui))
            return;

        _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key, BuildState(comp, mapId ?? Transform(uid).MapID));
    }

    private static WastelandMapAnnotation? SanitizeAnnotation(WastelandMapAnnotation annotation)
    {
        if (annotation.Type is not (WastelandMapAnnotationType.Marker
            or WastelandMapAnnotationType.Box
            or WastelandMapAnnotationType.Draw))
            return null;

        var label = annotation.Label.Trim();
        if (label.Length > 64)
            label = label[..64].TrimEnd();

        // Draw type: sanitize stroke points
        if (annotation.Type == WastelandMapAnnotationType.Draw)
        {
            var pts = annotation.StrokePoints;
            if (pts == null || pts.Length < 4)
                return null;
            var count = Math.Min(pts.Length & ~1, MaxStrokePoints); // ensure even, cap to max
            var sanitizedPts = new float[count];
            for (var i = 0; i < count; i++)
                sanitizedPts[i] = Math.Clamp(pts[i], 0f, 1f);
            if (string.IsNullOrWhiteSpace(label))
                label = "Drawing";
            return new WastelandMapAnnotation(WastelandMapAnnotationType.Draw, 0f, 0f, 0f, 0f, label, annotation.PackedColor, Math.Clamp(annotation.StrokeWidth, 1f, 12f), sanitizedPts);
        }

        // Marker / Box
        var startX = Math.Clamp(annotation.StartX, 0f, 1f);
        var startY = Math.Clamp(annotation.StartY, 0f, 1f);
        var endX = Math.Clamp(annotation.EndX, 0f, 1f);
        var endY = Math.Clamp(annotation.EndY, 0f, 1f);

        if (string.IsNullOrWhiteSpace(label))
            label = annotation.Type == WastelandMapAnnotationType.Marker ? "Marker" : "Box";

        return new WastelandMapAnnotation(annotation.Type, startX, startY, endX, endY, label, annotation.PackedColor, Math.Clamp(annotation.StrokeWidth, 1f, 12f), null);
    }

    private List<WastelandMapAnnotation> GetSharedAnnotations(WastelandMapComponent comp, MapId mapId, WastelandMapTacticalFeedKind feed)
    {
        if (feed == WastelandMapTacticalFeedKind.None)
            return comp.SharedAnnotations;

        var key = (mapId, feed);
        if (_sharedFeedAnnotations.TryGetValue(key, out var annotations))
            return annotations;

        annotations = new List<WastelandMapAnnotation>(comp.SharedAnnotations);
        _sharedFeedAnnotations[key] = annotations;
        return annotations;
    }

    // #Misfits Add - actor param enables group-member blip injection
    private WastelandMapTrackedBlip[] GetTrackedBlips(WastelandMapTacticalFeedKind feed, MapId mapId, Box2 bounds, EntityUid? actor = null)
    {
        // #Misfits Tweak - Cache the non-actor (faction + tribal) portion per (mapId, feed)
        // for the lifetime of a single Update sweep, so multiple open maps sharing a feed
        // pay for one world-scan instead of N. Outside the sweep this falls back to a
        // direct rebuild (e.g. OnAfterOpen, annotation messages).
        WastelandMapTrackedBlip[] nonActorBlips;
        var cacheKey = (mapId, feed);
        if (_inUpdateSweep && _nonActorCache.TryGetValue(cacheKey, out var cached))
        {
            nonActorBlips = cached;
        }
        else
        {
            _blipScratch.Clear();
            AppendFactionBlips(_blipScratch, feed, mapId, bounds);
            AppendTribalHuntTargetBlips(_blipScratch, mapId, bounds);
            nonActorBlips = _blipScratch.ToArray();
            if (_inUpdateSweep)
                _nonActorCache[cacheKey] = nonActorBlips;
        }

        // Group blips are per-actor and therefore never cached across viewers.
        if (actor.HasValue)
        {
            _groupScratch.Clear();
            AppendGroupMemberBlips(_groupScratch, actor.Value, mapId, bounds);
            if (_groupScratch.Count == 0)
                return nonActorBlips;

            var combined = new WastelandMapTrackedBlip[nonActorBlips.Length + _groupScratch.Count];
            nonActorBlips.CopyTo(combined, 0);
            for (var i = 0; i < _groupScratch.Count; i++)
                combined[nonActorBlips.Length + i] = _groupScratch[i];
            return combined;
        }

        return nonActorBlips;
    }

    // #Misfits Add - Append the faction blip set for this feed into the supplied buffer.
    private void AppendFactionBlips(List<WastelandMapTrackedBlip> buffer, WastelandMapTacticalFeedKind feed, MapId mapId, Box2 bounds)
    {
        switch (feed)
        {
            case WastelandMapTacticalFeedKind.Brotherhood:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardBrotherhood");
                break;
            case WastelandMapTacticalFeedKind.Vault:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardVault");
                break;
            case WastelandMapTacticalFeedKind.NCR:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardNCR");
                break;
            case WastelandMapTacticalFeedKind.Enclave:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardEnclave");
                break;
            case WastelandMapTacticalFeedKind.Legion:
                AppendIdCardBlips(buffer, mapId, bounds, "IdCardLegion");
                break;
            // #Misfits Add - Followers feed shows dead player humanoids
            case WastelandMapTacticalFeedKind.Followers:
                AppendDeadBodyBlips(buffer, mapId, bounds);
                break;
        }
    }

    /// <summary>Appends a blip for each group member on the same map as the actor, excluding the actor themselves.</summary>
    private void AppendGroupMemberBlips(List<WastelandMapTrackedBlip> buffer, EntityUid actor, MapId mapId, Box2 bounds)
    {
        var members = _groupSystem.GetGroupMemberEntities(actor);
        if (members == null || members.Count == 0)
            return;

        foreach (var member in members)
        {
            if (member == actor)
                continue; // don't show the holder as a blip

            var mapCoords = _transform.GetMapCoordinates(member);
            if (mapCoords.MapId != mapId)
                continue;

            var pos = mapCoords.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = Name(member);
            buffer.Add(new WastelandMapTrackedBlip(pos.X, pos.Y, label, WastelandMapTrackedBlipKind.PipBoyGroupMember));
        }
    }

    private void AppendTribalHuntTargetBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var query = EntityQueryEnumerator<LegendaryCreatureComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var legendary, out var xform))
        {
            if (!legendary.RevealLocation)
                continue;

            var mapCoordinates = _transform.GetMapCoordinates(uid, xform);
            if (mapCoordinates.MapId != mapId)
                continue;

            var pos = mapCoordinates.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = string.IsNullOrWhiteSpace(legendary.CreatureName)
                ? "Legendary Target"
                : $"Legendary {legendary.CreatureName}";

            buffer.Add(new WastelandMapTrackedBlip(
                pos.X,
                pos.Y,
                label,
                WastelandMapTrackedBlipKind.TribalHuntTarget));
        }

        var minorQuery = EntityQueryEnumerator<MinorHuntCreatureComponent, TransformComponent>();

        while (minorQuery.MoveNext(out var uid, out var minor, out var xform))
        {
            if (!minor.RevealLocation)
                continue;

            var mapCoordinates = _transform.GetMapCoordinates(uid, xform);
            if (mapCoordinates.MapId != mapId)
                continue;

            var pos = mapCoordinates.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = string.IsNullOrWhiteSpace(minor.CreatureName)
                ? "Minor Hunt Target"
                : $"Minor {minor.CreatureName}";

            buffer.Add(new WastelandMapTrackedBlip(
                pos.X,
                pos.Y,
                label,
                WastelandMapTrackedBlipKind.TribalHuntTarget));
        }
    }

    // #Misfits Add - Blips for dead player humanoids; used by the Followers tac-map feed.
    private void AppendDeadBodyBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds)
    {
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var mobState, out var xform))
        {
            if (!_mobState.IsDead(uid, mobState))
                continue;

            if (!IsFollowersTrackableCasualty(uid, mindContainer))
                continue;

            var mapCoords = _transform.GetMapCoordinates(uid, xform);
            if (mapCoords.MapId != mapId)
                continue;

            var pos = mapCoords.Position;
            if (!bounds.Contains(pos))
                continue;

            buffer.Add(new WastelandMapTrackedBlip(pos.X, pos.Y, Name(uid), WastelandMapTrackedBlipKind.DeadBody));
        }
    }

    private bool IsFollowersTrackableCasualty(EntityUid uid, MindContainerComponent mindContainer)
    {
        // Some non-humanoid entities can temporarily have a player mind, e.g. controlled
        // creatures or ghost roles. Followers rescue alerts are only for humanoid characters.
        if (!HasComp<HumanoidAppearanceComponent>(uid))
            return false;

        if (mindContainer.OriginalMind == null)
            return false;

        return TryComp<MindComponent>(mindContainer.OriginalMind.Value, out var mindComp)
            && mindComp.OriginalOwnerUserId != null;
    }

    // #Misfits Add - Notify Followers on player death and immediately refresh maps on revival.
    private void OnMindedEntityMobStateChanged(EntityUid uid, MindContainerComponent comp, MobStateChangedEvent args)
    {
        // Only care about transitions to or from Dead.
        var wasDead = args.OldMobState == MobState.Dead;
        var isDead  = args.NewMobState == MobState.Dead;
        if (!wasDead && !isDead)
            return;

        // Ignore NPCs and controlled creatures; only act on real humanoid player characters.
        if (!IsFollowersTrackableCasualty(uid, comp))
            return;

        if (isDead)
        {
            // Player just died — notify all online Followers.
            _followerSessionScratch.Clear();
            var factionQuery = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
            while (factionQuery.MoveNext(out _, out var factionComp, out var actor))
            {
                foreach (var f in factionComp.Factions)
                {
                    if (f.Id == "Followers")
                    {
                        _followerSessionScratch.Add(actor.PlayerSession);
                        break;
                    }
                }
            }

            if (_followerSessionScratch.Count > 0)
            {
                var msg = Loc.GetString("followers-death-alert", ("name", Name(uid)));
                foreach (var session in _followerSessionScratch)
                    _chatManager.DispatchServerMessage(session, msg);
            }
        }
        else
        {
            // Player was revived — immediately remove the blip from all active Followers maps.
            RefreshFollowersMaps();
        }
    }

    // #Misfits Add - Push an immediate state update to every open Followers tac-map.
    // Called on revival so the dead-body blip disappears without waiting for the 2.5s sweep.
    private void RefreshFollowersMaps()
    {
        var query = EntityQueryEnumerator<WastelandMapComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var map, out var ui, out var xform))
        {
            if (GetEffectiveFeed(map) != WastelandMapTacticalFeedKind.Followers)
                continue;

            // Only refresh if at least one player has this map open.
            var hasViewer = false;
            foreach (var _ in _uiSystem.GetActors((uid, ui), WastelandMapUiKey.Key))
            {
                hasViewer = true;
                break;
            }
            if (!hasViewer)
                continue;

            _uiSystem.SetUiState((uid, ui), WastelandMapUiKey.Key,
                BuildState(map, xform.MapID));
        }
    }

    private void AppendIdCardBlips(List<WastelandMapTrackedBlip> buffer, MapId mapId, Box2 bounds, string requiredTag)
    {
        var query = EntityQueryEnumerator<PresetIdCardComponent, IdCardComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var presetId, out var idCard, out var xform))
        {
            if (!_tag.HasTag(uid, requiredTag))
                continue;

            var meta = MetaData(uid);

            var mapCoordinates = _transform.GetMapCoordinates(uid, xform);
            if (mapCoordinates.MapId != mapId)
                continue;

            var pos = mapCoordinates.Position;
            if (!bounds.Contains(pos))
                continue;

            var label = GetHolotagLabel(idCard, presetId);
            var kind = GetHolotagKind(idCard, presetId, meta);
            buffer.Add(new WastelandMapTrackedBlip(pos.X, pos.Y, label, kind));
        }
    }

    private static string GetHolotagLabel(IdCardComponent idCard, PresetIdCardComponent presetId)
    {
        var fullName = idCard.FullName?.Trim();
        var rank = idCard.LocalizedJobTitle?.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
            return "Unknown Holotag";

        if (string.IsNullOrWhiteSpace(rank))
            rank = presetId.JobName?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(rank))
            return fullName;

        return $"{fullName} ({rank})";
    }

    private static WastelandMapTrackedBlipKind GetHolotagKind(IdCardComponent idCard, PresetIdCardComponent presetId, MetaDataComponent meta)
    {
        var rank = idCard.LocalizedJobTitle?.Trim();
        if (string.IsNullOrWhiteSpace(rank))
            rank = presetId.JobName?.ToString()?.Trim();

        var protoId = meta.EntityPrototype?.ID ?? string.Empty;
        var source = string.IsNullOrWhiteSpace(rank) ? protoId : rank;

        if (source.Contains("elder", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("commander", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.Elder;
        }

        if (source.Contains("paladin", StringComparison.OrdinalIgnoreCase))
            return WastelandMapTrackedBlipKind.Paladin;

        if (source.Contains("knight", StringComparison.OrdinalIgnoreCase))
            return WastelandMapTrackedBlipKind.Knight;

        if (source.Contains("scribe", StringComparison.OrdinalIgnoreCase))
            return WastelandMapTrackedBlipKind.Scribe;

        if (source.Contains("squire", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("initiate", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.Squire;
        }

        // #Misfits Add - Legion rank detection for the Centurion tactical computer
        if (source.Contains("legate", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("centurion", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.LegionCenturion;
        }

        if (source.Contains("decanus", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("dean", StringComparison.OrdinalIgnoreCase)) // CaesarLegionDean = Decanus in-game
        {
            return WastelandMapTrackedBlipKind.LegionDecanus;
        }

        if (source.Contains("legionnaire", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("vexillarius", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("houndmaster", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("frumentarii", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("optio", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("explorer", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.LegionWarrior;
        }

        if (source.Contains("auxilia", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("recruit", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("slave", StringComparison.OrdinalIgnoreCase))
        {
            return WastelandMapTrackedBlipKind.LegionRecruit;
        }
        // End Misfits Add

        return WastelandMapTrackedBlipKind.Unknown;
    }
}
