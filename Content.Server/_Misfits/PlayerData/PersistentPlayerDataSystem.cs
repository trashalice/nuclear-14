// #Misfits Add - Server system managing persistent player SPECIAL stats, kill/death/round counters,
// and character history log. Data persists across rounds via the database.
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared._Misfits.PlayerData;
using Content.Shared._Misfits.PlayerData.Components;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats; // #Misfits Add - SPECIAL effects event
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems; // #Misfits Add - refresh movement speed after SPECIAL load
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;

namespace Content.Server._Misfits.PlayerData;

/// <summary>
/// Loads/saves player data (SPECIAL, statistics, history) from the database.
/// Tracks mob kills via <see cref="MobStateChangedEvent"/> origin attribution.
/// Tracks player deaths via the same event on entities that own the component.
/// </summary>
public sealed class PersistentPlayerDataSystem : EntitySystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    // #Misfits Add - Used to re-evaluate movement speed after SPECIAL stats are loaded.
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    // #Misfits Add - Sawmill for data system logging
    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();

        _log = Logger.GetSawmill("persistent_player_data");

        // Component-scoped events (fire only when entity has the component)
        SubscribeLocalEvent<PersistentPlayerDataComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PersistentPlayerDataComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PersistentPlayerDataComponent, MobStateChangedEvent>(OnPlayerMobStateChanged);

        // Global event — track kills when any mob dies with a known origin
        SubscribeLocalEvent<MobStateChangedEvent>(OnAnyMobStateChanged);

        // Ensure the component is added when a player spawns (required for loading to trigger)
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        // #Misfits Add - Handle SPECIAL allocation confirmation from client
        SubscribeNetworkEvent<ConfirmSpecialAllocationEvent>(OnConfirmSpecialAllocation);

        // One-time migration from legacy JSON to database
        MigrateJsonToDatabase();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a text entry to the character's history log (capped at 50 entries) and saves.
    /// </summary>
    public void AddHistoryEntry(EntityUid playerUid, string entry)
    {
        if (!TryComp<PersistentPlayerDataComponent>(playerUid, out var comp))
            return;

        AppendHistory(comp, entry);
        Dirty(playerUid, comp);
        SavePlayer(comp);
    }

    // ── Spawn / Attach ─────────────────────────────────────────────────────────

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Ensure the persistent data component exists on the player entity
        var comp = EnsureComp<PersistentPlayerDataComponent>(args.Mob);
        var special = EnsureComp<SpecialComponent>(args.Mob);
        _special.TrySetBaseValues(args.Mob, args.Profile.Special, special);
        SyncPersistentSpecialFromComponent(args.Mob, comp, special);

        // Load immediately if the player is already attached (they will be for normal spawns)
        if (args.Player.AttachedEntity == args.Mob)
            LoadPlayerAsync(args.Mob, comp, args.Player);
    }

    private void OnPlayerAttached(Entity<PersistentPlayerDataComponent> ent, ref PlayerAttachedEvent args)
    {
        // Handles reconnects and late-attachment; LoadPlayerAsync is idempotent
        LoadPlayerAsync(ent, ent.Comp, args.Player);
    }

    // ── Shutdown / Save ────────────────────────────────────────────────────────

    private void OnShutdown(Entity<PersistentPlayerDataComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Loaded)
            SavePlayer(ent.Comp);
    }

    // ── Death / Kill tracking ──────────────────────────────────────────────────

    /// <summary>
    /// Fires on the player entity's own state change → track player deaths.
    /// </summary>
    private void OnPlayerMobStateChanged(Entity<PersistentPlayerDataComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (ent.Comp.DiedThisRound)
            return; // already counted this round

        ent.Comp.DiedThisRound = true;
        ent.Comp.Deaths++;

        AppendHistory(ent.Comp, $"Died in the Wasteland (round {ent.Comp.RoundsPlayed}).");
        Dirty(ent, ent.Comp);
        SavePlayer(ent.Comp);
    }

    /// <summary>
    /// Fires on ALL mob state changes. If a non-player mob dies and Origin has player data,
    /// credit a mob kill to that player.
    /// </summary>
    private void OnAnyMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (args.Origin == null)
            return;

        // Dying entity must NOT be a player (must not have PersistentPlayerDataComponent)
        if (HasComp<PersistentPlayerDataComponent>(args.Target))
            return;

        // Killer must be a player
        if (!TryComp<PersistentPlayerDataComponent>(args.Origin.Value, out var killerComp))
            return;

        killerComp.MobKills++;
        Dirty(args.Origin.Value, killerComp);
        SavePlayer(killerComp);
    }

    // ── SPECIAL confirmation ───────────────────────────────────────────────────

    // #Misfits Add - Validates and applies the player's SPECIAL allocation then locks it.
    private void OnConfirmSpecialAllocation(ConfirmSpecialAllocationEvent msg, EntitySessionEventArgs args)
    {
        var entity = args.SenderSession.AttachedEntity;
        if (entity == null)
            return;

        if (!TryComp<PersistentPlayerDataComponent>(entity.Value, out var comp))
            return;

        if (comp.StatsConfirmed)
            return; // already locked — ignore replay

        // Validate: each stat 1-10 and total budget <= 40 (seven 5s plus five extra points).
        var vals = new[] { msg.Strength, msg.Perception, msg.Endurance, msg.Charisma, msg.Intelligence, msg.Agility, msg.Luck };
        if (vals.Any(v => v < SpecialProfile.Minimum || v > SpecialProfile.Maximum) || vals.Sum() > SpecialProfile.MaxTotal)
            return;

        var profile = new SpecialProfile
        {
            Strength = msg.Strength,
            Perception = msg.Perception,
            Endurance = msg.Endurance,
            Charisma = msg.Charisma,
            Intelligence = msg.Intelligence,
            Agility = msg.Agility,
            Luck = msg.Luck,
        };

        if (!profile.IsValid)
            return;

        var special = EnsureComp<SpecialComponent>(entity.Value);
        _special.TrySetBaseValues(entity.Value, profile, special);

        comp.Strength     = msg.Strength;
        comp.Perception   = msg.Perception;
        comp.Endurance    = msg.Endurance;
        comp.Charisma     = msg.Charisma;
        comp.Intelligence = msg.Intelligence;
        comp.Agility      = msg.Agility;
        comp.Luck         = msg.Luck;
        comp.StatsConfirmed = true;

        AppendHistory(comp, $"Allocated SPECIAL: S{msg.Strength} P{msg.Perception} E{msg.Endurance} C{msg.Charisma} I{msg.Intelligence} A{msg.Agility} L{msg.Luck}.");
        Dirty(entity.Value, comp);
        SavePlayer(comp);

        // #Misfits Add - Re-apply stat-driven gameplay effects with the newly confirmed values.
        var ev = new SpecialStatsReadyEvent(entity.Value);
        RaiseLocalEvent(entity.Value, ref ev, true);
        _movement.RefreshMovementSpeedModifiers(entity.Value);
    }

    // ── Data load/save helpers ─────────────────────────────────────────────────

    private async void LoadPlayerAsync(EntityUid uid, PersistentPlayerDataComponent comp, ICommonSession session)
    {
        if (comp.Loaded)
            return;

        // Mind may not be ready at startup — only proceed when it is
        if (!_mind.TryGetMind(uid, out _, out var mind))
            return;

        var characterName = mind.CharacterName;
        if (string.IsNullOrEmpty(characterName))
            return;

        comp.UserId = session.UserId.ToString();
        comp.CharacterName = characterName;

        var playerId = session.UserId.UserId;

        try
        {
            var saved = await _db.GetCharacterPlayerDataAsync(playerId, characterName);

            if (saved != null)
            {
                comp.MobKills = saved.MobKills;
                comp.Deaths = saved.Deaths;
                comp.RoundsPlayed = saved.RoundsPlayed;
                comp.HistoryLog = DeserializeHistoryLog(saved.HistoryLog);

                // Character setup is authoritative for SPECIAL. Keep the persistent
                // row as a mirror so old database values cannot override profile edits.
                SyncPersistentSpecialFromComponent(uid, comp);
            }
            else
            {
                // First-time character — welcome entry
                AppendHistory(comp, "Arrived in the Wasteland for the first time.");
                SyncPersistentSpecialFromComponent(uid, comp);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load player data for {characterName}: {ex}");
        }

        // Increment round counter exactly once per spawn
        if (!comp.RoundCountedThisRound)
        {
            comp.RoundsPlayed++;
            comp.RoundCountedThisRound = true;

            if (comp.RoundsPlayed > 1)
                AppendHistory(comp, $"Entered the Wasteland for round {comp.RoundsPlayed}.");
        }

        comp.Loaded = true;
        Dirty(uid, comp);
        SavePlayer(comp);

        // #Misfits Add - Trigger stat-driven gameplay effects (stamina pool, movement speed).
        var ev = new SpecialStatsReadyEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void SyncPersistentSpecialFromComponent(EntityUid uid, PersistentPlayerDataComponent comp, SpecialComponent? special = null)
    {
        if (!Resolve(uid, ref special, false))
        {
            comp.Strength = SpecialProfile.DefaultValue;
            comp.Perception = SpecialProfile.DefaultValue;
            comp.Endurance = SpecialProfile.DefaultValue;
            comp.Charisma = SpecialProfile.DefaultValue;
            comp.Intelligence = SpecialProfile.DefaultValue;
            comp.Agility = SpecialProfile.DefaultValue;
            comp.Luck = SpecialProfile.DefaultValue;
        }
        else
        {
            comp.Strength = _special.GetBase(uid, SpecialStat.Strength, special);
            comp.Perception = _special.GetBase(uid, SpecialStat.Perception, special);
            comp.Endurance = _special.GetBase(uid, SpecialStat.Endurance, special);
            comp.Charisma = _special.GetBase(uid, SpecialStat.Charisma, special);
            comp.Intelligence = _special.GetBase(uid, SpecialStat.Intelligence, special);
            comp.Agility = _special.GetBase(uid, SpecialStat.Agility, special);
            comp.Luck = _special.GetBase(uid, SpecialStat.Luck, special);
        }

        comp.StatsConfirmed = true;
        Dirty(uid, comp);
    }

    private void SavePlayer(PersistentPlayerDataComponent comp)
    {
        if (string.IsNullOrEmpty(comp.UserId) || string.IsNullOrEmpty(comp.CharacterName))
            return;

        if (!Guid.TryParse(comp.UserId, out var playerId))
            return;

        var data = new Content.Server.Database.CharacterPlayerData
        {
            PlayerId = playerId,
            CharacterName = comp.CharacterName,
            Strength = comp.Strength,
            Perception = comp.Perception,
            Endurance = comp.Endurance,
            Charisma = comp.Charisma,
            Agility = comp.Agility,
            Intelligence = comp.Intelligence,
            Luck = comp.Luck,
            MobKills = comp.MobKills,
            Deaths = comp.Deaths,
            RoundsPlayed = comp.RoundsPlayed,
            StatsConfirmed = comp.StatsConfirmed,
            HistoryLog = SerializeHistoryLog(comp.HistoryLog),
        };

        _db.UpsertCharacterPlayerDataAsync(data);
    }

    private static void AppendHistory(PersistentPlayerDataComponent comp, string entry)
    {
        comp.HistoryLog.Add(entry);
        // Cap to 50 entries — remove oldest when exceeded
        if (comp.HistoryLog.Count > 50)
            comp.HistoryLog.RemoveAt(0);
    }

    private static string SerializeHistoryLog(List<string> log) =>
        JsonSerializer.Serialize(log);

    private static List<string> DeserializeHistoryLog(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    // ── One-time JSON → database migration ─────────────────────────────────────

    private async void MigrateJsonToDatabase()
    {
        var userDataPath = _resourceManager.UserData.RootDir ?? ".";
        var jsonPath = Path.Combine(userDataPath, "player_data.json");

        if (!File.Exists(jsonPath))
            return;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var data = JsonSerializer.Deserialize<Dictionary<string, LegacyCharacterPlayerData>>(json);

            if (data == null || data.Count == 0)
            {
                File.Move(jsonPath, jsonPath + ".migrated");
                return;
            }

            _log.Info($"Migrating {data.Count} player data records from JSON to database...");

            foreach (var (_, record) in data)
            {
                if (string.IsNullOrEmpty(record.UserId) || !Guid.TryParse(record.UserId, out var playerId))
                    continue;

                var dbData = new Content.Server.Database.CharacterPlayerData
                {
                    PlayerId = playerId,
                    CharacterName = record.CharacterName,
                    Strength = record.Strength,
                    Perception = record.Perception,
                    Endurance = record.Endurance,
                    Charisma = record.Charisma,
                    Intelligence = record.Intelligence,
                    Agility = record.Agility,
                    Luck = record.Luck,
                    MobKills = record.MobKills,
                    Deaths = record.Deaths,
                    RoundsPlayed = record.RoundsPlayed,
                    StatsConfirmed = record.StatsConfirmed,
                    HistoryLog = SerializeHistoryLog(record.HistoryLog),
                };

                await _db.UpsertCharacterPlayerDataAsync(dbData);
            }

            File.Move(jsonPath, jsonPath + ".migrated");
            _log.Info("Player data JSON migration complete.");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to migrate player_data.json to database: {ex}");
        }
    }
}

/// <summary>
/// Legacy JSON data model for one-time migration from player_data.json.
/// </summary>
internal sealed class LegacyCharacterPlayerData
{
    public string UserId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public int Strength { get; set; } = SpecialProfile.DefaultValue;
    public int Perception { get; set; } = SpecialProfile.DefaultValue;
    public int Endurance { get; set; } = SpecialProfile.DefaultValue;
    public int Charisma { get; set; } = SpecialProfile.DefaultValue;
    public int Agility { get; set; } = SpecialProfile.DefaultValue;
    public int Intelligence { get; set; } = SpecialProfile.DefaultValue;
    public int Luck { get; set; } = SpecialProfile.DefaultValue;
    public int MobKills { get; set; }
    public int Deaths { get; set; }
    public int RoundsPlayed { get; set; }
    public bool StatsConfirmed { get; set; } = false;
    public List<string> HistoryLog { get; set; } = new();
}
