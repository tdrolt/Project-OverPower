using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 2: the round flow in the room. The lobby's End warm-up, the countdown and going live stay exactly as Conquest has them
    /// (MatchDirector); once the room is live this component, in a Dominion room only, runs the match on the room's own values (DominionKeys):
    /// round 1, its end, a break, the next round, best of three, and the match winner. Added at runtime by BuildingManager.Awake next to
    /// MatchDirector (no scene footprint, no PhotonView: it only reads and writes Room Properties, like MatchDirector).
    ///
    /// The master decides each stage edge from what the ROOM says, never from state of its own (A1): DominionRoomWrites.Next gives the one
    /// check-and-set to send, so a new master just carries on, and two clients that both think they are master cannot both advance a stage. A
    /// write is never repeated until the room has echoed (or a second has passed): the next decision is made on the echo, not the send.
    /// Every client reacts to the stage edge it SEES in the room: a break resets everyone's game, the next round puts everyone back at their
    /// spawn keeping their picks (A9: players may move in the break; the zones reset at the break's start and again at the round's start, so
    /// nothing done in the break counts).
    ///
    /// Nothing is drawn here (the round card and break UI are Task 9); points, the centre and bounties are Task 3.
    /// </summary>
    public sealed partial class DominionDirector : MonoBehaviourPunCallbacks
    {
        public static DominionDirector Instance { get; private set; }

        // Not gameplay values: how long the master waits for the echo of its own write before it may decide again (a refused check-and-set
        // never echoes), and how often it looks at the room's players.
        private const float EchoWaitSeconds = 1f;
        private const float PresenceCheckSeconds = 0.25f;
        private const float FallbackGraceSeconds = 10f;

        // ---- every client: the read side, so an edge is reacted to once (like MatchDirector's lastApplied*).
        private int lastAppliedRound;
        private DominionStage lastAppliedStage = DominionStage.None;

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

        private bool configMissingLogged;

        /// <summary>The round now in play (or the round a break leads to), 0 before round 1. Reads the room.</summary>
        public int Round => PhotonNetwork.InRoom ? DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties).Round : 0;

        /// <summary>The Dominion stage in the room right now (None before round 1).</summary>
        public DominionStage Stage => PhotonNetwork.InRoom ? DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties).Stage : DominionStage.None;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(this); // BuildingManager.Awake adds exactly one.
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

            // A new master (or the old one, after a lost write) first finishes what the room already says: the zone reset the stage asked for
            // (dRz), and the match-over announcement once the room says Over. Points have their own pace and echo wait (Points file), so
            // neither this writer's wait nor theirs holds the other back.
            RunZoneReset(match);
            RunPoints();
            AnnounceOverIfDecided(match);

            if (Time.unscaledTime < waitForEchoUntil || Time.unscaledTime < nextPresenceCheckAt) return;
            nextPresenceCheckAt = Time.unscaledTime + PresenceCheckSeconds;

            DominionConfig config = Config();
            if (config == null) return;
            if (PhotonNetwork.ServerTimestamp == 0) return; // the server clock has not synced: never write

            Hashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
            DominionRoomState room = WithLatestPoints(DominionRoomState.Read(roomProps)); // the round is scored on what the master has written, echoed or not
            CountPlayers();
            DominionWrite write = DominionRoomWrites.Next(true, true, PhotonNetwork.ServerTimestamp, room,
                new DominionFlowNumbers
                {
                    RoundsToWin = config.RoundsToWin, MaxRounds = config.MaxRounds,
                    RoundSeconds = config.RoundSeconds, BreakSeconds = config.BreakSeconds,
                    HasCentre = CentreInPlay(out _),
                    CentreFirstMs = Mathf.RoundToInt(config.CentreFirstPayoutSeconds * 1000f),
                    CentreIntervalMs = Mathf.RoundToInt(config.CentrePayoutIntervalSeconds * 1000f),
                }, match.TeamsInMatch, playersPerTeam);
            if (write == null) return;

            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(write.Props, write.Expected))
            {
                Debug.LogWarning($"[DOMINION] write refused locally ({write.What})");
                return;
            }
            waitForEchoUntil = Time.unscaledTime + EchoWaitSeconds;
            Debug.Log($"[DOMINION] master wrote: {write.What} (round {room.Round}, stage {room.Stage})");
        }

        private DominionConfig Config()
        {
            DominionConfig config = FindFirstObjectByType<RoomManager>()?.Dominion;
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

        /// <summary>Players per team id: a player counts while they are in the room, and a dropped one still counts for the dropped grace.</summary>
        private void CountPlayers()
        {
            System.Array.Clear(playersPerTeam, 0, playersPerTeam.Length);
            float grace = FindFirstObjectByType<RoomManager>()?.Config != null ? FindFirstObjectByType<RoomManager>().Config.DroppedGraceSeconds : FallbackGraceSeconds;
            foreach (KeyValuePair<int, Player> pair in PhotonNetwork.CurrentRoom.Players)
            {
                Player p = pair.Value;
                if (!Teams.TryGetPlayingTeam(p, out int team) || team < 0 || team >= playersPerTeam.Length) continue; // a spectator plays for no team
                if (p.IsInactive && inactiveSince.TryGetValue(pair.Key, out float since) && Time.unscaledTime - since >= grace) continue;
                playersPerTeam[team]++;
            }
        }

        // ---------------------------------------------------------------- every client: react to an edge

        public override void OnJoinedRoom() => ReadWithoutReacting();

        public override void OnLeftRoom()
        {
            lastAppliedRound = 0;
            lastAppliedStage = DominionStage.None;
            waitForEchoUntil = -1f;
            announceAgainAt = -1f;
            zonesResetSentFor = 0;
            ResetPointsState();
            inactiveSince.Clear();
        }

        /// <summary>A joiner (or a client that rejoined) takes the room's stage as it stands; it is not an edge, so nothing is reset for it.</summary>
        private void ReadWithoutReacting()
        {
            if (!PhotonNetwork.InRoom) return;
            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            lastAppliedRound = room.Round;
            lastAppliedStage = room.Stage;
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (changed == null || !PhotonNetwork.InRoom) return;
            NoteEchoForPoints(changed);
            if (!changed.ContainsKey(DominionKeys.Stage) && !changed.ContainsKey(DominionKeys.Round)) return;
            // The echo of the master's own write (or anyone's): the wait for it is over, the next decision may be made.
            waitForEchoUntil = -1f;
            if (!DominionMode.IsActive()) return;

            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            int prevRound = lastAppliedRound;
            DominionStage prevStage = lastAppliedStage;
            lastAppliedRound = room.Round;
            lastAppliedStage = room.Stage;

            DominionEdge edge = DominionRoomWrites.EdgeBetween(prevRound, prevStage, room.Round, room.Stage);
            Debug.Log($"[DOMINION] room: round {room.Round} stage {room.Stage} ends {room.EndMs} points [{string.Join(",", room.Points)}] wins [{string.Join(",", room.Wins)}] winner {room.Winner} (edge {edge})");
            if (edge == DominionEdge.None) return;

            // The zones go back to neutral at the start of the break and again at the start of the round: the master does it from the room's
            // dRz (RunZoneReset), not from seeing this edge, so a master that takes over mid-way finishes it.
            ResetLocalPlayer(edge);
        }

        /// <summary>Master: while the room's stage (a Round or a Break) has not had its zone reset (dRz differs from dEnd), reset the zones and
        /// write dRz. Asked of the room every frame, so any master finishes a reset the previous one never did. Not repeated until the dRz
        /// write has had a second to echo (a second reset would wipe a capture made in between).</summary>
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
                lifecycle.ResetForMatchStart(team);
            }
            else if (edge == DominionEdge.RoundStarted)
            {
                lifecycle.ResetForRoundStart(team);
            }
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // Nothing of the old master's is carried: the new master reads the room in Update and carries on. Only the echo wait was this
            // client's own as a master.
            waitForEchoUntil = -1f;
            announceAgainAt = -1f;
            zonesResetSentFor = 0;
            ResetPointsState(); // the new master's first tick builds on the room's dPts / dCtr and starts its own beat
        }
    }
}
