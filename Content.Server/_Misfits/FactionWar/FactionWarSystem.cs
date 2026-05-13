// #Misfits Refactor - Server-side player-to-player war system.
// Handles GUI form submissions from clients (declare/ceasefire/warjoin) and the admin /forcewar command.
// Active war state is maintained here and broadcast to all clients on every change.
// Wars go through a 5-minute Pending phase (during which /warjoin is open) before becoming Active.
// A war is a pair of NetUserIds: Player1 (who declared) and Player2 (who was declared on).
// All joins must be manual via /warjoin; there is no auto-enlistment.
// Only original 2 players can participate in raids during the war.
// Either original player can propose/accept ceasefire to end the war.

using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared._Misfits.FactionWar;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.FactionWar;

/// <summary>
/// Manages player-to-player war declarations, ceasefires, and individual war participation.
/// Rules enforced here (all game-logic stays server-side):
///   - Any two different players can declare war on each other.
///   - Wars enter a 5-minute Pending phase before becoming Active (during which /warjoin is open).
///   - During Pending, any player may /warjoin on either side (except the original 2).
///   - Once Active, /warjoin is closed.
///   - Raids require both participants to be part of the original 2 players in the war.
///   - Only original 2 players can end the war (via ceasefire proposal).
///   - War history is logged for every event (declaration, acceptance, join, ceasefire).
/// </summary>
public sealed class FactionWarSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager    _adminManager  = default!;
    [Dependency] private readonly IChatManager     _chat          = default!;
    [Dependency] private readonly IConsoleHost     _conHost       = default!;
    [Dependency] private readonly IPlayerManager   _playerManager = default!;
    [Dependency] private readonly IGameTiming      _gameTiming    = default!;

    // ── Constants ──────────────────────────────────────────────────────────

    /// <summary>Minimum elapsed round time before war can be declared.</summary>
    /// <summary>Minimum elapsed round time before war can be declared.</summary>
    private static readonly TimeSpan WarCooldownAfterRoundStart = TimeSpan.FromMinutes(30);

    /// <summary>How long a war stays in Pending before becoming Active.</summary>
    /// <summary>How long a war stays in Pending before becoming Active.</summary>
    private static readonly TimeSpan WarPrepDuration = TimeSpan.FromMinutes(5);

    /// <summary>How long target player has to accept war before prompt times out.</summary>
    private static readonly TimeSpan WarAcceptanceTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Minimum word count for war reason/casus belli.</summary>
    private const int MinReasonWords = 5;

    /// <summary>Cooldown after a war ends before same player can declare again.</summary>
    private static readonly TimeSpan WarCooldownAfterEnd = TimeSpan.FromMinutes(10);


    // ── State ──────────────────────────────────────────────────────────────

    private readonly Dictionary<string, PlayerWarEntry> _activeWars = new();
    private TimeSpan _roundStartTime;

    /// <summary>Activation times for pending wars. Key = WarKey (canonical player pair).</summary>
    private readonly Dictionary<string, TimeSpan> _warActivationTimes = new();

    /// <summary>
    /// War acceptance prompts pending from target players.
    /// Key = WarKey, Value = (expiry time, declared by player info).
    /// </summary>
    private readonly Dictionary<string, WarAcceptancePrompt> _pendingAcceptancePrompts = new();

    /// <summary>
    /// Individual players who joined a war via /warjoin.
    /// Key = player UserId, Value = (warKey, side 1 or 2).
    /// </summary>
    private readonly Dictionary<NetUserId, (string WarKey, byte Side)> _warParticipants = new();

    /// <summary>Per-player cooldown after a war ends. Key = player UID, Value = earliest next war time.</summary>
    private readonly Dictionary<string, TimeSpan> _playerWarCooldowns = new();

    /// <summary>Ceasefire proposals awaiting the other player's consent. Key = WarKey.</summary>
    private readonly Dictionary<string, CeasefireProposal> _pendingCeasefireProposals = new();

    /// <summary>
    /// Sessions that currently have the /war panel open.
    /// Panel data is only sent to these sessions on state change, avoiding O(N) broadcasts.
    /// </summary>
    private readonly HashSet<ICommonSession> _panelOpenSessions = new();

    // ── Performance gates ──────────────────────────────────────────────────

    private float _participantResyncAccumulator;
    private const float ParticipantResyncInterval = 30f;

    private float _warUpdateAccumulator;
    private const float WarUpdateInterval = 1.0f;

    private readonly List<PlayerWarEntry> _activatedScratch = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public override void Initialize()
    {
        base.Initialize();

        // Admin-only: force-end a war between two players.
        _conHost.RegisterCommand(
            "warend",
            "Forcibly end an active war between two players.",
            "warend <player1_name_or_uid> <player2_name_or_uid>",
            WarEndCommand);

        // Admin-only: force-declare a war, bypassing cooldown and checks.
        _conHost.RegisterCommand(
            "forcewar",
            "Force-declare a war between two players (admin, bypasses cooldown).",
            "forcewar <player1_name_or_uid> <player2_name_or_uid> [reason...]",
            ForceWarCommand);

        // Receive GUI form submissions from clients.
        SubscribeNetworkEvent<FactionWarOpenPanelRequestEvent>(OnPanelRequest);
        SubscribeNetworkEvent<PlayerWarDeclareRequestEvent>(OnDeclareRequest);
        SubscribeNetworkEvent<PlayerWarCeasefireRequestEvent>(OnCeasefireRequest);

        // War acceptance/rejection.
        SubscribeNetworkEvent<PlayerWarAcceptEvent>(OnAcceptWar);
        SubscribeNetworkEvent<PlayerWarRejectEvent>(OnRejectWar);

        // Ceasefire proposal responses.
        SubscribeNetworkEvent<CeasefireAcceptedEvent>(OnAcceptCeasefireProposal);
        SubscribeNetworkEvent<CeasefireRejectedEvent>(OnRejectCeasefireProposal);

        // Warjoin panel & enlistment.
        SubscribeNetworkEvent<FactionWarJoinPanelRequestEvent>(OnWarJoinPanelRequest);
        SubscribeNetworkEvent<PlayerWarJoinRequestEvent>(OnWarJoinRequest);

        // Admin force-war GUI.
        SubscribeNetworkEvent<PlayerWarForceRequestEvent>(OnForceWarRequest);
        SubscribeNetworkEvent<PlayerWarForceCeasefireRequestEvent>(OnForceCeasefireRequest);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // ── Tick: transition Pending → Active, handle timeouts, broadcast participants ──

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _warUpdateAccumulator += frameTime;
        if (_warUpdateAccumulator < WarUpdateInterval)
            return;
        _warUpdateAccumulator -= WarUpdateInterval;

        var now = _gameTiming.CurTime;

        // ── War acceptance prompt timeouts ───────────────────────────────
        if (_pendingAcceptancePrompts.Count > 0)
        {
            List<string>? expiredKeys = null;
            foreach (var (key, prompt) in _pendingAcceptancePrompts)
            {
                if (now >= prompt.ExpiresAt)
                {
                    expiredKeys ??= new List<string>();
                    expiredKeys.Add(key);
                }
            }
            if (expiredKeys != null)
            {
                foreach (var key in expiredKeys)
                {
                    var prompt = _pendingAcceptancePrompts[key];
                    _pendingAcceptancePrompts.Remove(key);

                    // Auto-reject: remove the pending war
                    var war = _activeWars.Values.FirstOrDefault(w => w.WarKey == key);
                    if (war != null)
                    {
                        _activeWars.Remove(key);
                        _warActivationTimes.Remove(key);
                        _chat.DispatchServerAnnouncement(
                            $"WAR DECLARATION EXPIRED\n" +
                            $"The war declared by {prompt.DeclaredByCharacterName} was not accepted within 5 minutes.",
                            Color.Gray);
                    }
                }
                BroadcastWarState();
            }
        }

        // ── Ceasefire proposal timeouts ──────────────────────────────────
        if (_pendingCeasefireProposals.Count > 0)
        {
            List<string>? expiredCeaseKeys = null;
            foreach (var (key, prop) in _pendingCeasefireProposals)
            {
                if (now >= prop.ExpiresAt)
                {
                    expiredCeaseKeys ??= new List<string>();
                    expiredCeaseKeys.Add(key);
                }
            }
            if (expiredCeaseKeys != null)
            {
                foreach (var key in expiredCeaseKeys)
                {
                    var prop = _pendingCeasefireProposals[key];
                    _pendingCeasefireProposals.Remove(key);
                    _chat.DispatchServerAnnouncement(
                        $"CEASEFIRE PROPOSAL EXPIRED\n" +
                        $"The ceasefire proposed by {prop.ProposingPlayerName} expired. The war continues.",
                        Color.Gray);
                }
                SendPanelDataToAll();
            }
        }

        // ── Pending → Active transitions ──────────────────────────────────
        if (_warActivationTimes.Count > 0)
        {
            var activated = _activatedScratch;
            activated.Clear();

            foreach (var (key, activationTime) in _warActivationTimes.ToList())
            {
                if (now < activationTime)
                    continue;

                if (!_activeWars.TryGetValue(key, out var war))
                    continue;

                war.Phase = WarPhase.Active;
                _warActivationTimes.Remove(key);
                activated.Add(war);
            }

            if (activated.Count > 0)
            {
                BroadcastWarState();
                SendPanelDataToAll();

                // Ensure clients receive the participant mapping for newly-activated wars
                // so overlays will appear immediately even if no joiners have been sent.
                BroadcastParticipants();

                foreach (var war in activated)
                {
                    _chat.DispatchServerAnnouncement(
                        $"WAR HAS BEGUN\n" +
                        $"The conflict between {war.SideName1} and {war.SideName2} is now active!\n" +
                        $"(/warjoin) is now closed for this conflict.\n" +
                        $"The war will only end by ceasefire.",
                        Color.OrangeRed);
                }
            }
        }

        // Safety resync: re-broadcast participant dict every 30 s
        if (_activeWars.Count > 0)
        {
            _participantResyncAccumulator += WarUpdateInterval;
            if (_participantResyncAccumulator >= ParticipantResyncInterval)
            {
                _participantResyncAccumulator = 0f;
                BroadcastParticipants();
            }
        }
        else
        {
            _participantResyncAccumulator = 0f;
        }
    }

    // ── GUI: Panel data request ─────────────────────────────────────────

    private void OnPanelRequest(FactionWarOpenPanelRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        _panelOpenSessions.Add(player);
        SendPanelData(player);
    }

    private void SendPanelData(ICommonSession player)
    {
        var data = new PlayerWarPanelDataEvent
        {
            ActiveWars = _activeWars.Values.ToList(),
            MyCharacterName = player.AttachedEntity is { } myEntity ? Name(myEntity) : null,
            MyWars = new List<PlayerWarEntry>(),
        };

        // Populate online players for targeting
        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.UserId == player.UserId)
                continue;

            if (session.AttachedEntity is not { } entity)
                continue;

            data.OnlinePlayers.Add(new OnlinePlayerInfo
            {
                UserId = session.UserId,
                UserName = session.Name,
                CharacterName = Name(entity),
            });
        }
        data.OnlinePlayers.Sort((a, b) => string.Compare(a.CharacterName, b.CharacterName, StringComparison.Ordinal));

        // Check 30-minute cooldown.
        var elapsed = _gameTiming.CurTime - _roundStartTime;
        if (elapsed < WarCooldownAfterRoundStart)
        {
            var remaining = WarCooldownAfterRoundStart - elapsed;
            data.StatusMessage = $"War declarations are locked for the first 30 minutes. " +
                                 $"{remaining.Minutes}m {remaining.Seconds}s remaining.";
        }

        // Check per-player cooldown.
        if (data.StatusMessage == null && _playerWarCooldowns.TryGetValue(player.UserId.ToString(), out var cooldownEnd))
        {
            if (_gameTiming.CurTime < cooldownEnd)
            {
                var remaining = cooldownEnd - _gameTiming.CurTime;
                data.StatusMessage = $"Cooldown: {remaining.Minutes}m {remaining.Seconds}s remaining.";
            }
        }

        // Wars where this player is involved (as original declarer/target)
        foreach (var war in _activeWars.Values)
        {
            if (war.DeclaredByPlayer == player.UserId || war.DeclaredAgainstPlayer == player.UserId)
                data.MyWars.Add(war);
        }

        RaiseNetworkEvent(data, player);
    }

    // ── GUI: Declare War ───────────────────────────────────────────────────

    private void OnDeclareRequest(PlayerWarDeclareRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } playerEntity)
        {
            SendResult(player, false, "You must be in-game to declare war.");
            return;
        }

        if (msg.TargetPlayer == player.UserId)
        {
            SendResult(player, false, "You cannot declare war on yourself.");
            return;
        }

        var elapsed = _gameTiming.CurTime - _roundStartTime;
        if (elapsed < WarCooldownAfterRoundStart)
        {
            var remaining = WarCooldownAfterRoundStart - elapsed;
            SendResult(player, false,
                $"War declarations are locked for the first 30 minutes. {remaining.Minutes}m {remaining.Seconds}s remaining.");
            return;
        }

        var reason = msg.Reason.Trim();
        if (reason.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinReasonWords)
        {
            SendResult(player, false, $"War reason must be at least {MinReasonWords} words.");
            return;
        }

        var now = _gameTiming.CurTime;
        var myUidStr = player.UserId.ToString();
        if (_playerWarCooldowns.TryGetValue(myUidStr, out var myCooldown) && now < myCooldown)
        {
            var rem = myCooldown - now;
            SendResult(player, false,
                $"Cooldown: {rem.Minutes}m {rem.Seconds}s remaining.");
            return;
        }

        // Check if target exists and is online
        if (!TryGetSessionForPlayer(msg.TargetPlayer, out var targetSession))
        {
            SendResult(player, false, "Target player is not online.");
            return;
        }

        // Check if either player is already in a war
        foreach (var war in _activeWars.Values)
        {
            if ((war.DeclaredByPlayer == player.UserId || war.DeclaredAgainstPlayer == player.UserId) ||
                (war.DeclaredByPlayer == msg.TargetPlayer || war.DeclaredAgainstPlayer == msg.TargetPlayer))
            {
                SendResult(player, false, "One or both players are already in a war.");
                return;
            }
        }

        // Create war entry
        var declaredAgainstCharacterName = targetSession.AttachedEntity is { } tgt ? Name(tgt) : "Unknown";

        var warEntry = new PlayerWarEntry
        {
            DeclaredByPlayer = player.UserId,
            DeclaredByCharacterName = Name(playerEntity),
            DeclaredByJobName = "Unknown",
            DeclaredAgainstPlayer = msg.TargetPlayer,
            DeclaredAgainstCharacterName = declaredAgainstCharacterName,
            SideName1 = msg.SideName1.Trim(),
            SideName2 = string.IsNullOrWhiteSpace(declaredAgainstCharacterName)
                ? "Player 2's Side"
                : $"{declaredAgainstCharacterName}'s Side",
            Reason = reason,
            Phase = WarPhase.Pending,
        };

        // Add original participants with their sides
        warEntry.Participants[player.UserId] = 1;
        warEntry.Participants[msg.TargetPlayer] = 2;

        // Add history event
        warEntry.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.Declared,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = player.UserId,
            ActorUserName = player.Name,
            ActorCharacterName = warEntry.DeclaredByCharacterName,
            Details = $"Declared war on {warEntry.DeclaredAgainstCharacterName}. Reason: {reason}"
        });

        var warKey = warEntry.WarKey;
        _activeWars[warKey] = warEntry;

        // Set activation time
        _warActivationTimes[warKey] = now + WarPrepDuration;

        // Send acceptance prompt to target
        RaiseNetworkEvent(
            new WarAcceptancePromptEvent
            {
                DeclaredByPlayer = player.UserId,
                DeclaredByCharacterName = warEntry.DeclaredByCharacterName,
                SideName1 = warEntry.SideName1,
                Reason = reason,
            },
            targetSession);

        // Track acceptance prompt timeout
        _pendingAcceptancePrompts[warKey] = new WarAcceptancePrompt
        {
            ExpiresAt = now + WarAcceptanceTimeout,
            DeclaredByCharacterName = warEntry.DeclaredByCharacterName,
            DeclaredByPlayer = player.UserId,
        };

        BroadcastWarState();
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"WAR DECLARED\n" +
            $"{warEntry.DeclaredByCharacterName} has declared war on {warEntry.DeclaredAgainstCharacterName}!\n" +
            $"Reason: \"{reason}\"\n\n" +
            $"War begins in 5 minutes. {warEntry.DeclaredAgainstCharacterName} /warjoin to pick a side (MANDATORY TO BE APART OF WAR). ",
            Color.OrangeRed);

        SendResult(player, true, $"War declared. Awaiting {warEntry.DeclaredAgainstCharacterName}'s response (5 min timeout).");
    }

    // ── War acceptance/rejection ───────────────────────────────────────────

    private void OnAcceptWar(PlayerWarAcceptEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find war where this player is the target
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if (w.DeclaredAgainstPlayer == player.UserId && w.Phase == WarPhase.Pending)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "No pending war found.");
            return;
        }

        var now = _gameTiming.CurTime;

        // Accept war
        war.SideName2 = msg.SideName2.Trim();

        // Add history event
        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.Accepted,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = player.UserId,
            ActorUserName = player.Name,
            ActorCharacterName = player.AttachedEntity is { } acceptedEntity ? Name(acceptedEntity) : string.Empty,
            Details = $"Accepted war and named side: {war.SideName2}"
        });

        _pendingAcceptancePrompts.Remove(war.WarKey);
        BroadcastWarState();
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"WAR ACCEPTED\n" +
            $"{Name(player.AttachedEntity ?? EntityUid.Invalid)} has accepted the war declaration!\n" +
            $"{war.SideName1} vs {war.SideName2}\n\n" +
            $"War begins in 5 minutes. Use (/warjoin) to choose a side.",
            Color.OrangeRed);

        SendResult(player, true, "War accepted. War begins in 5 minutes.");
    }

    private void OnRejectWar(PlayerWarRejectEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find war where this player is the target
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if (w.DeclaredAgainstPlayer == player.UserId && w.Phase == WarPhase.Pending)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "No pending war found.");
            return;
        }

        _activeWars.Remove(war.WarKey);
        _warActivationTimes.Remove(war.WarKey);
        _pendingAcceptancePrompts.Remove(war.WarKey);

        BroadcastWarState();
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"WAR REJECTED\n" +
            $"{Name(player.AttachedEntity ?? EntityUid.Invalid)} rejected the war declaration.",
            Color.Gray);

        SendResult(player, true, "War declaration rejected.");
    }

    // ── GUI: Ceasefire ────────────────────────────────────────────────────

    private void OnCeasefireRequest(PlayerWarCeasefireRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find war where player is one of the original 2
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByPlayer == player.UserId || w.DeclaredAgainstPlayer == player.UserId) &&
                w.Phase == WarPhase.Active)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "You are not in an active war.");
            return;
        }

        var warKey = war.WarKey;
        var now = _gameTiming.CurTime;

        if (_pendingCeasefireProposals.ContainsKey(warKey))
        {
            SendResult(player, false, "A ceasefire is already being negotiated for this war.");
            return;
        }

        var otherPlayer = war.DeclaredByPlayer == player.UserId ? war.DeclaredAgainstPlayer : war.DeclaredByPlayer;

        var proposal = new CeasefireProposal
        {
            War = war,
            ProposingPlayer = player.UserId,
            ProposingPlayerName = Name(player.AttachedEntity ?? EntityUid.Invalid),
            ExpiresAt = now + TimeSpan.FromMinutes(5),
        };

        _pendingCeasefireProposals[warKey] = proposal;
        SendPanelDataToAll();

        if (TryGetSessionForPlayer(otherPlayer, out var otherSession))
        {
            RaiseNetworkEvent(
                new CeasefireProposalEvent
                {
                    ProposingPlayer = player.UserId,
                    ProposingPlayerName = proposal.ProposingPlayerName,
                },
                otherSession);
        }

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE PROPOSED\n" +
            $"{proposal.ProposingPlayerName} proposes a ceasefire.\n" +
            $"The other party has 5 minutes to respond.",
            Color.SkyBlue);

        SendResult(player, true, "Ceasefire proposed. Awaiting response.");
    }

    private void OnAcceptCeasefireProposal(CeasefireAcceptedEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find the war
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByPlayer == player.UserId || w.DeclaredAgainstPlayer == player.UserId) &&
                w.Phase == WarPhase.Active)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "You are not in an active war.");
            return;
        }

        var warKey = war.WarKey;

        if (!_pendingCeasefireProposals.TryGetValue(warKey, out var proposal))
        {
            SendResult(player, false, "No ceasefire proposal for this war.");
            return;
        }

        if (proposal.ProposingPlayer == player.UserId)
        {
            SendResult(player, false, "You proposed this ceasefire. Wait for the other party.");
            return;
        }

        _pendingCeasefireProposals.Remove(warKey);
        RemoveWar(war);

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE\n" +
            $"{war.SideName1} and {war.SideName2} have agreed to cease hostilities.",
            Color.SkyBlue);

        SendResult(player, true, "Ceasefire accepted. The conflict has ended.");
    }

    private void OnRejectCeasefireProposal(CeasefireRejectedEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find the war
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByPlayer == player.UserId || w.DeclaredAgainstPlayer == player.UserId) &&
                w.Phase == WarPhase.Active)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "You are not in an active war.");
            return;
        }

        var warKey = war.WarKey;

        if (!_pendingCeasefireProposals.TryGetValue(warKey, out var proposal))
        {
            SendResult(player, false, "No ceasefire proposal for this war.");
            return;
        }

        if (proposal.ProposingPlayer == player.UserId)
        {
            SendResult(player, false, "You proposed this ceasefire. Wait for the other party.");
            return;
        }

        _pendingCeasefireProposals.Remove(warKey);
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE REJECTED\n" +
            $"The ceasefire proposal was rejected. The war continues.",
            Color.OrangeRed);

        SendResult(player, true, "Ceasefire rejected. The war continues.");
    }

    // ── GUI: Warjoin panel data ────────────────────────────────────────────

    private void OnWarJoinPanelRequest(FactionWarJoinPanelRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        var data = new PlayerWarJoinPanelDataEvent();

        // List pending wars player can join
        data.AvailableWars = _activeWars.Values
            .Where(w => w.Phase == WarPhase.Pending &&
                        w.DeclaredByPlayer != player.UserId &&
                        w.DeclaredAgainstPlayer != player.UserId &&
                        !w.Participants.ContainsKey(player.UserId))
            .ToList();

        // List active wars for reference
        data.ActiveWars = _activeWars.Values
            .Where(w => w.Phase == WarPhase.Active)
            .ToList();

        // Check if player already in a war
        data.AlreadyInWar = _warParticipants.ContainsKey(player.UserId);

        if (data.AvailableWars.Count == 0 && !data.AlreadyInWar)
            data.StatusMessage = "No pending wars to join.";

        RaiseNetworkEvent(data, player);
    }

    // ── GUI: Warjoin enlistment ────────────────────────────────────────────

    private void OnWarJoinRequest(PlayerWarJoinRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame)
        {
            SendJoinResult(player, false, "You must be in-game.");
            return;
        }

        if (_warParticipants.ContainsKey(player.UserId))
        {
            SendJoinResult(player, false, "You have already joined a war.");
            return;
        }

        // Find the war
        var warKey = PlayerWarEntry.GetWarKey(msg.Player1, msg.Player2);
        if (!_activeWars.TryGetValue(warKey, out var war))
        {
            SendJoinResult(player, false, "That war is not in a joinable state.");
            return;
        }

        if (war.Phase != WarPhase.Pending)
        {
            SendJoinResult(player, false, "That war is not pending.");
            return;
        }

        if (msg.ChosenSide != 1 && msg.ChosenSide != 2)
        {
            SendJoinResult(player, false, "Invalid side selection.");
            return;
        }

        // Prevent original 2 from joining as additional participants
        if (player.UserId == msg.Player1 || player.UserId == msg.Player2)
        {
            SendJoinResult(player, false, "Original war participants cannot rejoin.");
            return;
        }

        // Add player to participants
        war.Participants[player.UserId] = msg.ChosenSide;
        _warParticipants[player.UserId] = (warKey, msg.ChosenSide);

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.PlayerJoined,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = player.UserId,
            ActorUserName = player.Name,
            ActorCharacterName = player.AttachedEntity is { } joinedEntity ? Name(joinedEntity) : string.Empty,
            Details = $"Joined on side {msg.ChosenSide}"
        });

        BroadcastParticipants();

        var sideName = msg.ChosenSide == 1 ? war.SideName1 : war.SideName2;
        SendJoinResult(player, true, $"You have joined the war on the side of {sideName}.");
    }

    // ── Admin commands ─────────────────────────────────────────────────────

    private void WarEndCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: warend <player1_uid_or_name> <player2_uid_or_name>");
            return;
        }

        var player1Id = ResolvePlayerIdentifier(args[0]);
        var player2Id = ResolvePlayerIdentifier(args[1]);

        if (player1Id == null || player2Id == null)
        {
            shell.WriteError("One or both players not found.");
            return;
        }

        var warKey = PlayerWarEntry.GetWarKey(player1Id.Value, player2Id.Value);
        if (!_activeWars.TryGetValue(warKey, out var war))
        {
            shell.WriteError("War not found.");
            return;
        }

        RemoveWar(war);
        shell.WriteLine("War ended.");
    }

    private void ForceWarCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: forcewar <player1_uid_or_name> <player2_uid_or_name> [reason...]");
            return;
        }

        var player1Id = ResolvePlayerIdentifier(args[0]);
        var player2Id = ResolvePlayerIdentifier(args[1]);

        if (player1Id == null || player2Id == null || player1Id == player2Id)
        {
            shell.WriteError("Invalid player combination.");
            return;
        }

        var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : "Admin-forced war";

        var warEntry = new PlayerWarEntry
        {
            DeclaredByPlayer = player1Id.Value,
            DeclaredByCharacterName = player1Id.Value.ToString(),
            DeclaredAgainstPlayer = player2Id.Value,
            DeclaredAgainstCharacterName = player2Id.Value.ToString(),
            SideName1 = "Side 1",
            SideName2 = "Side 2",
            Reason = reason,
            Phase = WarPhase.Pending,
        };

        warEntry.Participants[player1Id.Value] = 1;
        warEntry.Participants[player2Id.Value] = 2;

        var warKey = warEntry.WarKey;
        _activeWars[warKey] = warEntry;
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        BroadcastWarState();
        SendPanelDataToAll();

        shell.WriteLine($"War forced between {player1Id} and {player2Id}.");
    }

    private void OnForceWarRequest(PlayerWarForceRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_adminManager.IsAdmin(player))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "Admin only." }, player);
            return;
        }

        var warEntry = new PlayerWarEntry
        {
            DeclaredByPlayer = msg.Player1,
            DeclaredByCharacterName = msg.Player1.ToString(),
            DeclaredAgainstPlayer = msg.Player2,
            DeclaredAgainstCharacterName = msg.Player2.ToString(),
            SideName1 = msg.SideName1,
            SideName2 = msg.SideName2,
            Reason = msg.Reason,
            Phase = WarPhase.Pending,
        };

        warEntry.Participants[msg.Player1] = 1;
        warEntry.Participants[msg.Player2] = 2;

        var warKey = warEntry.WarKey;
        _activeWars[warKey] = warEntry;
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        BroadcastWarState();
        SendPanelDataToAll();

        RaiseNetworkEvent(new FactionWarForceResultEvent { Success = true, Message = "War forced." }, player);
    }

    private void OnForceCeasefireRequest(PlayerWarForceCeasefireRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_adminManager.IsAdmin(player))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "Admin only.", IsCeasefire = true }, player);
            return;
        }

        var warKey = PlayerWarEntry.GetWarKey(msg.Player1, msg.Player2);
        if (!_activeWars.TryGetValue(warKey, out var war))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "War not found.", IsCeasefire = true }, player);
            return;
        }

        RemoveWar(war);
        RaiseNetworkEvent(new FactionWarForceResultEvent { Success = true, Message = "War ended.", IsCeasefire = true }, player);
    }

    // ── Round lifecycle ────────────────────────────────────────────────────

    private void OnRoundRestart(RoundRestartCleanupEvent _)
    {
        _activeWars.Clear();
        _warActivationTimes.Clear();
        _warParticipants.Clear();
        _playerWarCooldowns.Clear();
        _pendingCeasefireProposals.Clear();
        _pendingAcceptancePrompts.Clear();
        _panelOpenSessions.Clear();
        _participantResyncAccumulator = 0f;
        _roundStartTime = _gameTiming.CurTime;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
        {
            _panelOpenSessions.Remove(e.Session);
            return;
        }

        if (e.NewStatus != SessionStatus.InGame)
            return;

        if (_activeWars.Count > 0)
        {
            RaiseNetworkEvent(
                new FactionWarStateUpdatedEvent { ActiveWars = _activeWars.Values.ToList() },
                e.Session);
        }

        if (_warParticipants.Count > 0)
            SendParticipantsTo(e.Session);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RemoveWar(PlayerWarEntry war)
    {
        _activeWars.Remove(war.WarKey);
        _warActivationTimes.Remove(war.WarKey);
        _pendingCeasefireProposals.Remove(war.WarKey);
        _pendingAcceptancePrompts.Remove(war.WarKey);

        // Set per-player cooldown for original 2 players
        var cooldownEnd = _gameTiming.CurTime + WarCooldownAfterEnd;
        _playerWarCooldowns[war.DeclaredByPlayer.ToString()] = cooldownEnd;
        _playerWarCooldowns[war.DeclaredAgainstPlayer.ToString()] = cooldownEnd;

        // Remove all participants
        var toRemove = _warParticipants
            .Where(kvp => kvp.Value.WarKey == war.WarKey)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var userId in toRemove)
            _warParticipants.Remove(userId);

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.Concluded,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = null,
            ActorUserName = "System",
            ActorCharacterName = "System",
            Details = "War concluded"
        });

        BroadcastWarState();
        BroadcastParticipants();
        SendPanelDataToAll();
    }

    private void BroadcastWarState()
    {
        RaiseNetworkEvent(
            new FactionWarStateUpdatedEvent { ActiveWars = _activeWars.Values.ToList() },
            Filter.Broadcast());
    }

    private void SendResult(ICommonSession session, bool success, string message)
    {
        RaiseNetworkEvent(
            new FactionWarCommandResultEvent { Success = success, Message = message },
            session);
    }

    private void SendJoinResult(ICommonSession session, bool success, string message)
    {
        RaiseNetworkEvent(
            new FactionWarJoinResultEvent { Success = success, Message = message },
            session);
    }

    private void SendPanelDataToAll()
    {
        _panelOpenSessions.RemoveWhere(s => s.Status != SessionStatus.InGame);
        foreach (var session in _panelOpenSessions)
            SendPanelData(session);
    }

    private Dictionary<NetEntity, byte> BuildParticipantDict()
    {
        var dict = new Dictionary<NetEntity, byte>();

        foreach (var (userId, (warKey, side)) in _warParticipants)
        {
            if (!_activeWars.TryGetValue(warKey, out var war) || war.Phase != WarPhase.Active)
                continue;

            if (!TryGetSessionForPlayer(userId, out var session) || session.AttachedEntity is not { } entity)
                continue;

            dict[GetNetEntity(entity)] = side;
        }

        // Include original war participants (declarer and target) for active wars so
        // the overlay always shows the two principals even if no additional players
        // have joined via /warjoin.
        foreach (var war in _activeWars.Values)
        {
            if (war.Phase != WarPhase.Active)
                continue;

            // Declarer -> side 1
            if (TryGetSessionForPlayer(war.DeclaredByPlayer, out var declSession) && declSession.AttachedEntity is { } declEntity)
            {
                var ent = GetNetEntity(declEntity);
                if (!dict.ContainsKey(ent))
                    dict[ent] = 1;
            }

            // Target -> side 2
            if (TryGetSessionForPlayer(war.DeclaredAgainstPlayer, out var targSession) && targSession.AttachedEntity is { } targEntity)
            {
                var ent = GetNetEntity(targEntity);
                if (!dict.ContainsKey(ent))
                    dict[ent] = 2;
            }
        }

        return dict;
    }

    private void BroadcastParticipants()
    {
        var dict = BuildParticipantDict();
        RaiseNetworkEvent(
            new FactionWarParticipantsUpdatedEvent { Participants = dict },
            Filter.Broadcast());
    }

    private void SendParticipantsTo(ICommonSession session)
    {
        var dict = BuildParticipantDict();
        RaiseNetworkEvent(
            new FactionWarParticipantsUpdatedEvent { Participants = dict },
            session);
    }

    public bool HasWar(string warKey)
    {
        return _activeWars.ContainsKey(warKey);
    }

    public bool TryGetActiveWarForOriginalParticipant(NetUserId userId, out PlayerWarEntry war)
    {
        foreach (var entry in _activeWars.Values)
        {
            if (entry.Phase != WarPhase.Active)
                continue;

            if (entry.DeclaredByPlayer != userId && entry.DeclaredAgainstPlayer != userId)
                continue;

            war = entry;
            return true;
        }

        war = null!;
        return false;
    }

    private bool TryGetSessionForPlayer(NetUserId userId, out ICommonSession session)
    {
        session = null!;
        foreach (var s in _playerManager.Sessions)
        {
            if (s.UserId == userId)
            {
                session = s;
                return true;
            }
        }
        return false;
    }

    private NetUserId? ResolvePlayerIdentifier(string input)
    {
        foreach (var session in _playerManager.Sessions)
        {
            if (session.UserId.ToString().Equals(input, StringComparison.OrdinalIgnoreCase))
                return session.UserId;

            if (session.Name.Equals(input, StringComparison.OrdinalIgnoreCase))
                return session.UserId;
        }

        return null;
    }

    // ── Inner types ────────────────────────────────────────────────────────

    private sealed class WarAcceptancePrompt
    {
        public TimeSpan ExpiresAt;
        public string DeclaredByCharacterName = string.Empty;
        public NetUserId DeclaredByPlayer;
    }

    private sealed class CeasefireProposal
    {
        public PlayerWarEntry War = null!;
        public NetUserId ProposingPlayer;
        public string ProposingPlayerName = string.Empty;
        public TimeSpan ExpiresAt;
    }
}
