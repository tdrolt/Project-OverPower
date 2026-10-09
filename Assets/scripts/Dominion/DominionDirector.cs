using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.Telemetry;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Dominion
{
    /// <summary>
    /// The round flow in the room. The lobby's End warm-up, the countdown and going live stay as Conquest has them (MatchDirector); once the room
    /// is live this component, in a Dominion room only, runs the match on the room's own values (DominionKeys): rounds, breaks, best of three and
    /// the match winner. Added at runtime by BuildingManager.Awake next to MatchDirector (no scene footprint, no PhotonView: it only reads and
    /// writes Room Properties). The master decides each stage edge from what the ROOM says, never from state of its own (A1):
    /// DominionRoomWrites.Next gives the one check-and-set, so a new master just carries on and two would-be masters cannot both advance a stage.
    /// A write is never repeated until the room has echoed (or a second has passed): act on the echo, not the send. Every client reacts to the
    /// stage edge it SEES: a break resets everyone's game, the next round puts everyone back at their spawn keeping their picks (A9: players may
    /// move in the break; the zones reset at the break's start and again at the round's start, so nothing done in the break counts).
    /// </summary>
    public sealed partial class DominionDirector : MonoBehaviourPunCallbacks
    {
        public static DominionDirector Instance { get; private set; }

        // Not gameplay values: how long the master waits for the echo of its own write before it may decide again (a refused check-and-set
        // never echoes), and how often it looks at the room's players.
        private const float EchoWaitSeconds = 1f;
        private const float PresenceCheckSeconds = 0.25f;

        // ---- every client: the read side, so an edge is reacted to once (like MatchDirector's lastApplied*).
        private int lastAppliedRound;
        private DominionStage lastAppliedStage = DominionStage.None;
        private int[] lastAppliedWins;                // the wins as last seen: which team's went up tells who won a round
        private int lastAppliedSuddenDeath;       // dSd as last seen: a new value is a new sudden death (or a replay)

        // ---- master: waiting for the echo of its own write / of a match-over announcement.
        private float waitForEchoUntil = -1f;
        private float announceAgainAt = -1f;
        private int zonesResetSentFor;            // master: the dEnd whose reset-done write (dRz) is on its way
        private float zonesResetSentAt = -10f;
        private float nextPresenceCheckAt;

        // Players whose connection dropped, with when this client learned it: they count as still present for the dropped grace, like the
        // Conquest knockouts do (so a quick reconnect never costs a team the match).
        private readonly Dictionary<int, float> inactiveSince = new Dictionary<int, float>();
        private readonly int[] playersPerTeam = new int[DominionKeys.TeamSlots];
        private readonly SuddenDeathRules.Tally suddenDeathTally = new SuddenDeathRules.Tally(DominionKeys.TeamSlots); // master: who lives per team and when each team's last player fell
        private readonly SuddenDeathVerdictSettle suddenDeathSettle = new SuddenDeathVerdictSettle(); // master: how long the same verdict has held

        private bool configMissingLogged;
        private bool healAreaWarned;
        private bool gameplayMissingLogged;

        /// <summary>The round now in play (or the round a break leads to), 0 before round 1. Reads the room.</summary>
        public int Round => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(DominionKeys.Round, out object r) && r is int round ? round : 0;

        /// <summary>The Dominion stage in the room right now (None before round 1).</summary>
        public DominionStage Stage => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(DominionKeys.Stage, out object s) && s is int stage
            ? (DominionStage)stage : DominionStage.None; // read straight off the key, no allocation: the shop asks every frame

        /// <summary>dSd: the server ms the sudden-death circle starts to shrink (a replay writes a new one), 0 when none is written. Reads the room.</summary>
        public int SuddenDeathStartMs => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(DominionKeys.SuddenDeathStart, out object s) && s is int ms ? ms : 0;

        private int[] suddenDeathTeams = System.Array.Empty<int>();
        private int[] suddenDeathTeamsForWins;
        private int[] suddenDeathTeamsForStored;

        /// <summary>The teams that play the current sudden death: the room's dSdT (written with sudden death's start and narrowed by every replay,
        /// A33), or for a room without it the teams level on round wins. Kept per source array, so asking every frame allocates nothing.</summary>
        public int[] SuddenDeathTeams
        {
            get
            {
                if (!PhotonNetwork.InRoom) return System.Array.Empty<int>();
                Hashtable props = PhotonNetwork.CurrentRoom.CustomProperties;
                int[] wins = props.TryGetValue(DominionKeys.Wins, out object raw) ? raw as int[] : null;
                int[] stored = props.TryGetValue(DominionKeys.SuddenDeathTeams, out object rawTeams) ? rawTeams as int[] : null;
                if (wins == null && stored == null) return System.Array.Empty<int>();
                if (!ReferenceEquals(wins, suddenDeathTeamsForWins) || !ReferenceEquals(stored, suddenDeathTeamsForStored))
                {
                    MatchDirector match = MatchDirector.Instance;
                    int[] inMatch = match != null && match.TeamsInMatch != null && match.TeamsInMatch.Length > 0 ? match.TeamsInMatch : DominionMode.TeamsOfCurrentRoom();
                    suddenDeathTeams = DominionRules.TeamsPlayingSuddenDeath(stored, wins, inMatch);
                    suddenDeathTeamsForWins = wins;
                    suddenDeathTeamsForStored = stored;
                }
                return suddenDeathTeams;
            }
        }

        /// <summary>The death stamp for a player who arrives dead in sudden death (late joiner, rejoiner, a body loading): see
        /// DominionRoomWrites.ArrivalDeathStamp. Reads the room and the player's own properties.</summary>
        public int ArrivalStampMs(Player player)
        {
            int now = PhotonNetwork.ServerTimestamp;
            if (player == null || !PhotonNetwork.InRoom) return now;
            DominionConfig config = DominionMode.Config();
            int earliest = config != null
                ? DominionRoomWrites.EarliestFallMs(SuddenDeathStartMs, config.SuddenDeathCountdownSeconds, Mathf.RoundToInt(config.SameInstantToleranceSeconds * 1000f)) : 0;
            bool dead = player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object flag) && flag is bool alive && !alive;
            bool hasStamp = player.CustomProperties.TryGetValue(PlayerLifecycle.LastStandAtKey, out object stamp) && stamp is int;
            return DominionRoomWrites.ArrivalDeathStamp(Stage, dead, hasStamp, hasStamp ? (int)stamp : 0, earliest, now);
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; } // BuildingManager.Awake adds exactly one.
            if (GetComponent<SuddenDeathZone>() == null) gameObject.AddComponent<SuddenDeathZone>(); // the circle: no scene footprint, like this component
            if (GetComponent<Overpower.UI.DominionHud>() == null) gameObject.AddComponent<Overpower.UI.DominionHud>(); // the round HUD, break card and result: drawn on every client, spectators included
        }

        // The mode's scene loads after the room was joined, so no joined-room callback comes to this component: it takes the room as it stands here,
        // or a late joiner or rejoiner would see "round 0, no stage" and get a fresh start from a stage change nobody made.
        private void Start()
        {
            if (PhotonNetwork.InRoom) ReadWithoutReacting();
        }

        private void OnDestroy()
        {
            UnhookBuildings();
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- master: decide and write

        private void Update()
        {
            if (!PhotonNetwork.InRoom || !DominionMode.IsActive()) return;
            TrackDroppedPlayers();
            if (!PhotonNetwork.IsMasterClient) return;

            MatchDirector match = MatchDirector.Instance;
            if (match == null || !match.IsLive) return;

            // The server clock is read once per Update and handed to every step: the points beat and the stage writer must agree on which side
            // of a stage's end this frame is (the buzzer payout lives exactly on that edge).
            int now = PhotonNetwork.ServerTimestamp;

            // A new master (or the old one, after a lost write) first finishes what the room already says: the zone reset the stage asked for
            // (dRz), and the match-over announcement once the room says Over. Points have their own pace and echo wait (Points file), so
            // neither this writer's wait nor theirs holds the other back.
            RunZoneReset(match);
            RunPoints(now);
            AnnounceOverIfDecided(match);

            if (Time.unscaledTime < waitForEchoUntil || Time.unscaledTime < nextPresenceCheckAt) return;
            nextPresenceCheckAt = Time.unscaledTime + PresenceCheckSeconds;

            DominionConfig config = Config();
            if (config == null) return;
            if (now == 0) return; // the server clock has not synced: never write

            Hashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
            DominionRoomState room = WithLatestPoints(DominionRoomState.Read(roomProps)); // the round is scored on what the master has written, echoed or not
            bool counted = CountPlayers();
            bool judgingSuddenDeath = counted && room.Stage == DominionStage.SuddenDeath;
            if (judgingSuddenDeath) TallySuddenDeath(Rooms.Config.DroppedGraceSeconds);
            DominionWrite write = DominionRoomWrites.Next(true, true, now, room,
                FlowNumbersOf(config, CentreInPlay(out _)), match.TeamsInMatch, counted ? playersPerTeam : null, // null: the last-team check is skipped
                judgingSuddenDeath ? suddenDeathTally : null);          // null: sudden death is not judged on a guess
            // A sudden-death verdict (a win or a replay) is written only once it has held for a network beat, so every death report has arrived
            // before the stamps are read; any other write goes at once. The decision is SuddenDeathVerdictSettle.ShouldWrite (tested).
            if (!suddenDeathSettle.ShouldWrite(write, judgingSuddenDeath, Time.unscaledTime, SuddenDeathRules.VerdictSettleSeconds)) return;

            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(write.Props, write.Expected))
            {
                Debug.LogWarning($"[DOMINION] write refused locally ({write.What})");
                return;
            }
            waitForEchoUntil = Time.unscaledTime + EchoWaitSeconds;
            suddenDeathSettle.Reset(); // the next verdict is judged from the room as it will be
            Debug.Log($"[DOMINION] master wrote: {write.What} (round {room.Round}, stage {room.Stage})");
        }

        /// <summary>The numbers the stage rules need, read from the config (seconds become server ms where the rules count in ms). A pure hand-over so a test can
        /// give every field its own value and see each one land in the right place.</summary>
        public static DominionFlowNumbers FlowNumbersOf(DominionConfig config, bool hasCentre) => new DominionFlowNumbers
        {
            RoundsToWin = config.RoundsToWin, MaxRounds = config.MaxRounds,
            RoundSeconds = config.RoundSeconds, BreakSeconds = config.BreakSeconds,
            OvertimeSeconds = config.OvertimeSeconds, OvertimeLeadPoints = config.OvertimeLeadPoints,
            HasCentre = hasCentre,
            CentreFirstMs = Mathf.RoundToInt(config.CentreFirstPayoutSeconds * 1000f),
            CentreIntervalMs = Mathf.RoundToInt(config.CentrePayoutIntervalSeconds * 1000f),
            SuddenDeathCountdownSeconds = config.SuddenDeathCountdownSeconds,
            SameInstantToleranceMs = Mathf.RoundToInt(config.SameInstantToleranceSeconds * 1000f),
        };

        // Looked up once: Update asks every quarter second.
        private RoomManager rooms;
        private RoomManager Rooms => rooms != null ? rooms : (rooms = FindFirstObjectByType<RoomManager>());

        private DominionConfig Config()
        {
            DominionConfig config = Rooms != null ? Rooms.Dominion : null;
            if (config == null && !configMissingLogged)
            {
                configMissingLogged = true;
                Debug.LogError("[DOMINION] the RoomManager has no DominionConfig - the Dominion rounds cannot run.");
            }
            return config;
        }

        /// <summary>The match is over in the room (stage Over with a winner): end it through the existing path, so the room closes and the result
        /// shows. Repeats each second until the room's own mPhase says Over, so a master that died between the two writes costs nothing.</summary>
        private void AnnounceOverIfDecided(MatchDirector match)
        {
            if (Time.unscaledTime < announceAgainAt || match.Phase == MatchPhase.Over) return;
            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            if (room.Stage != DominionStage.Over || room.Winner < 0) return;
            announceAgainAt = Time.unscaledTime + EchoWaitSeconds;
            Debug.Log($"[DOMINION] match over: team {room.Winner} wins, ending the match");
            match.AnnounceMatchOver(room.Winner);
        }

        // ---------------------------------------------------------------- who is in the room

        private void TrackDroppedPlayers()
        {
            foreach (KeyValuePair<int, Player> pair in PhotonNetwork.CurrentRoom.Players)
            {
                if (pair.Value.IsInactive)
                {
                    if (!inactiveSince.ContainsKey(pair.Key)) inactiveSince[pair.Key] = Time.unscaledTime;
                }
                else inactiveSince.Remove(pair.Key);
            }
        }

        /// <summary>Players per team id: a player counts while they are in the room, and a dropped one still counts for the dropped grace. False when the gameplay config is missing (nothing was counted).</summary>
        private bool CountPlayers()
        {
            System.Array.Clear(playersPerTeam, 0, playersPerTeam.Length);
            // No gameplay config to read the dropped grace from: log once and skip the last-team check rather than guess a number.
            GameplayConfig gameplay = Rooms != null ? Rooms.Config : null;
            if (gameplay == null)
            {
                if (!gameplayMissingLogged)
                {
                    gameplayMissingLogged = true;
                    Debug.LogError("[DOMINION] the RoomManager has no GameplayConfig - the last-team check is skipped.");
                }
                return false;
            }
            float grace = gameplay.DroppedGraceSeconds;
            foreach (KeyValuePair<int, Player> pair in PhotonNetwork.CurrentRoom.Players)
            {
                Player p = pair.Value;
                if (!Teams.TryGetPlayingTeam(p, out int team) || team < 0 || team >= playersPerTeam.Length) continue; // a spectator plays for no team
                if (!StillCounts(pair.Key, p, grace)) continue;
                playersPerTeam[team]++;
            }
            return true;
        }

        /// <summary>A dropped player still counts until the dropped grace has run out: DominionRules.CountsAsPresent (tested); this only reads the room.</summary>
        private bool StillCounts(int actor, Player p, float graceSeconds)
        {
            bool timed = inactiveSince.TryGetValue(actor, out float since);
            return DominionRules.CountsAsPresent(p.IsInactive, timed, since, Time.unscaledTime, graceSeconds);
        }

        /// <summary>Fills the sudden-death tally from the room's players: each player's replicated alive flag (PlayerLifecycle.AliveKey; a player who
        /// never died has none, which counts as alive) and, for the fallen, the server stamp they wrote with it (PlayerLifecycle.LastStandAtKey, one
        /// write with the flag). A player who dropped for good counts as gone, like CountPlayers. The counting itself is SuddenDeathRules.Tally.Add
        /// (tested); this only reads the room. Called only while judging sudden death.</summary>
        private void TallySuddenDeath(float graceSeconds)
        {
            suddenDeathTally.Clear();
            foreach (KeyValuePair<int, Player> pair in PhotonNetwork.CurrentRoom.Players)
            {
                Player p = pair.Value;
                if (!Teams.TryGetPlayingTeam(p, out int team)) continue;
                bool hasFlag = p.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object flag) && flag is bool;
                bool hasStamp = p.CustomProperties.TryGetValue(PlayerLifecycle.LastStandAtKey, out object stamp) && stamp is int;
                suddenDeathTally.Add(new SuddenDeathRules.Player
                {
                    Team = team,
                    Counts = StillCounts(pair.Key, p, graceSeconds),
                    HasAliveFlag = hasFlag, AliveFlag = hasFlag && (bool)flag,
                    HasDeathStamp = hasStamp, DeathStampMs = hasStamp ? (int)stamp : 0,
                });
            }
        }

        // ---------------------------------------------------------------- every client: react to an edge

        public override void OnJoinedRoom() => ReadWithoutReacting();

        public override void OnLeftRoom()
        {
            lastAppliedRound = 0;
            lastAppliedStage = DominionStage.None;
            lastAppliedSuddenDeath = 0;
            lastAppliedWins = null;
            waitForEchoUntil = -1f;
            announceAgainAt = -1f;
            zonesResetSentFor = 0;
            ResetPointsState();
            inactiveSince.Clear();
            healAreaWarned = false;
        }

        /// <summary>A joiner (or a client that rejoined) takes the room's stage as it stands; it is not an edge, so nothing is reset for it.</summary>
        private void ReadWithoutReacting()
        {
            if (!PhotonNetwork.InRoom) return;
            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            lastAppliedRound = room.Round;
            lastAppliedStage = room.Stage;
            lastAppliedSuddenDeath = room.SuddenDeathMs;
            lastAppliedWins = room.Wins;
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (changed == null || !PhotonNetwork.InRoom) return;
            NoteEchoForPoints(changed);
            if (!changed.ContainsKey(DominionKeys.Stage) && !changed.ContainsKey(DominionKeys.Round) && !changed.ContainsKey(DominionKeys.SuddenDeathStart)) return;
            // The echo of the master's own write (or anyone's): the wait for it is over, the next decision may be made.
            waitForEchoUntil = -1f;
            if (!DominionMode.IsActive()) return;

            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            int prevRound = lastAppliedRound;
            DominionStage prevStage = lastAppliedStage;
            int prevSuddenDeath = lastAppliedSuddenDeath;
            int[] prevWins = lastAppliedWins;
            lastAppliedRound = room.Round;
            lastAppliedStage = room.Stage;
            lastAppliedSuddenDeath = room.SuddenDeathMs;
            lastAppliedWins = room.Wins;

            DominionEdge edge = DominionRoomWrites.EdgeBetween(prevRound, prevStage, room.Round, room.Stage);
            bool suddenDeathStart = DominionRoomWrites.IsSuddenDeathStart(prevStage, prevSuddenDeath, room.Stage, room.SuddenDeathMs);
            Debug.Log($"[DOMINION] room: round {room.Round} stage {room.Stage} ends {room.EndMs} sd {room.SuddenDeathMs} sdTeams [{(room.SuddenDeathTeams != null ? string.Join(",", room.SuddenDeathTeams) : "-")}] points [{string.Join(",", room.Points)}] wins [{string.Join(",", room.Wins)}] winner {room.Winner} (edge {edge}{(suddenDeathStart ? ", sudden death start" : "")})");
            DropRoundMarkers(prevRound, prevStage, prevSuddenDeath, room, prevWins);
            if (prevStage == DominionStage.None && room.Stage == DominionStage.Break) WarnAboutMissingHealAreas();
            if (suddenDeathStart)
            {
                ResetLocalPlayerForSuddenDeath();
                return;
            }
            if (edge == DominionEdge.None) return;

            // The zones go back to neutral at the start of the break and again at the start of the round: the master does it from the room's
            // dRz (RunZoneReset), not from seeing this edge, so a master that takes over mid-way finishes it.
            ResetLocalPlayer(edge);
        }

        /// <summary>The match log's story of the match: round start and end, break, sudden death, match over. Written by the master only, from the
        /// echo it sees (the same edge every client sees), so each event is in the log once even though the report merges every client's file. The
        /// notes and the choice of which edge drops what are DominionMarkerNotes.ForEdge (tested).</summary>
        private void DropRoundMarkers(int prevRound, DominionStage prevStage, int prevSuddenDeathMs, DominionRoomState room, int[] prevWins)
        {
            MatchTelemetry telemetry = MatchTelemetry.Instance;
            if (telemetry == null) return;
            MatchDirector match = MatchDirector.Instance;
            int[] teams = match != null && match.TeamsInMatch != null && match.TeamsInMatch.Length > 0 ? match.TeamsInMatch : DominionMode.TeamsOfCurrentRoom();
            foreach (string note in DominionMarkerNotes.ForEdge(PhotonNetwork.IsMasterClient, prevRound, prevStage, prevSuddenDeathMs, room, teams, prevWins))
                telemetry.DropMarker(note);
        }

        /// <summary>Master: while the room's stage (a Round or a Break) has not had its zone reset (dRz differs from dEnd), reset the zones and
        /// write dRz. Asked of the room every frame, so any master finishes a reset the previous one never did. Not repeated until the dRz
        /// write has had a second to echo (a second reset would wipe a capture made in between). Known and accepted: a NEW master
        /// that takes over while the old master's dRz write is still in flight may redo the reset once; the zones are neutral either way and the
        /// chance of a capture in that instant is negligible.</summary>
        private void RunZoneReset(MatchDirector match)
        {
            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            if (!DominionRoomWrites.ZoneResetDue(room)) return;
            if (zonesResetSentFor == room.EndMs && Time.unscaledTime - zonesResetSentAt < EchoWaitSeconds) return;
            if (PhotonNetwork.ServerTimestamp == 0 || !TryResetTerritory(match)) return;

            DominionWrite done = DominionRoomWrites.ZonesResetDone(room);
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(done.Props, done.Expected)) return;
            zonesResetSentFor = room.EndMs;
            zonesResetSentAt = Time.unscaledTime;
            Debug.Log($"[DOMINION] master wrote: zones reset for stage ending {room.EndMs} (round {room.Round}, stage {room.Stage})");
        }

        private bool TryResetTerritory(MatchDirector match)
        {
            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null) return false;
            if (!buildings.ResetForMatchStart(match.TeamsInMatch)) return false;
            Debug.Log("[DOMINION] master reset the territory (all zones neutral, capitals home)");
            return true;
        }

        private void ResetLocalPlayer(DominionEdge edge)
        {
            if (!Teams.TryGetPlayingTeam(PhotonNetwork.LocalPlayer, out int team)) return; // a spectator has no body to reset
            PhotonView localView = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            PlayerLifecycle lifecycle = localView != null ? localView.GetComponent<PlayerLifecycle>() : null;
            if (lifecycle == null) return;

            if (edge == DominionEdge.BreakStarted)
            {
                Overpower.Vision.ZoneKnowledge.ResetKnowledge(); // the zones are neutral again: what the team knew starts over, as at go-live
                lifecycle.ResetForMatchStart(team, DominionRoomWrites.KeepsScoreboard(edge));
            }
            else if (edge == DominionEdge.RoundStarted)
            {
                Overpower.Vision.ZoneKnowledge.ResetKnowledge(); // the zones were reset again at the round's start: what the team learnt in the break goes too
                lifecycle.ResetForRoundStart(team);
            }
        }

        /// <summary>Once, when the match goes live: a 2v2 team with no SpawnHealArea in the scene heals nowhere (the lane scene places one per team).
        /// Logged, not guessed around.</summary>
        private void WarnAboutMissingHealAreas()
        {
            if (healAreaWarned) return;
            healAreaWarned = true;
            foreach (int team in DominionHealRules.TeamsMissingHealArea(DominionMode.TeamsOfCurrentRoom(), SpawnHealArea.HasAreaFor))
                Debug.LogWarning($"[DOMINION] team {team} has no SpawnHealArea in this scene - its players get no spawn healing until the scene places one.");
        }

        /// <summary>Sudden death starts, or starts over after everyone fell at once (every client, on the new dSd): a player of a tied team is alive at
        /// their spawn with full health, no shield and refreshed abilities, keeping the build; a player of any other team is dead and waits, still
        /// able to watch. The zones are left alone: points have stopped.</summary>
        private void ResetLocalPlayerForSuddenDeath()
        {
            if (!Teams.TryGetPlayingTeam(PhotonNetwork.LocalPlayer, out int team)) return; // a spectator has no body to reset
            PhotonView localView = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            PlayerLifecycle lifecycle = localView != null ? localView.GetComponent<PlayerLifecycle>() : null;
            if (lifecycle == null) return;
            bool plays = SuddenDeathRules.TeamPlays(team, SuddenDeathTeams);
            Debug.Log($"[DOMINION] sudden death starts: team {team} {(plays ? "plays - alive at its spawn" : "is not tied - dead and waiting")} (tied teams [{string.Join(",", SuddenDeathTeams)}])");
            lifecycle.ResetForSuddenDeath(team, plays);
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // Nothing of the old master's is carried: the new master reads the room in Update and carries on. Only the echo wait was this
            // client's own as a master.
            waitForEchoUntil = -1f;
            announceAgainAt = -1f;
            zonesResetSentFor = 0;
            suddenDeathSettle.Reset();
            ResetPointsState(); // the new master's first tick builds on the room's dPts / dCtr and starts its own beat
        }
    }
}
