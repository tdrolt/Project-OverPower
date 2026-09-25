using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Net;
using Overpower.Telemetry;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Match
{
    /// <summary>
    /// 2.7b step 5: the countdown-and-going-live half of MatchDirector, split into its own partial file so the
    /// elimination/phase half (MatchDirector.cs) stays readable. MatchStartRules.cs holds the actual rule as pure,
    /// tested C#; everything here is the Photon wiring around it - two Room Properties written by the master with
    /// check-and-set (Decision 1: mTeams when the countdown starts, mPhase when it goes live), read directly from
    /// the room by every client (like IsEliminated - RoomManager.OnJoinedRoom can run before this class's own
    /// callback), and a per-frame/half-second poll (Decision 16) rather than relying only on callbacks, so a
    /// missed one, an unsynced clock or an unread snapshot can never strand a full room in the warm-up.
    /// </summary>
    public partial class MatchDirector
    {
        /// Room Property key: int[], the teams fixed into the match, ascending (Decision 1/4). Present from the
        /// countdown start for the rest of the room's life (a decided match closes the room rather than clearing
        /// it - Decision 1) - absent again only if the countdown is cancelled.
        public const string TeamsInMatchKey = "mTeams";
        /// Room Property key: int, the server ms the match goes live (Decision 1). Present from the countdown
        /// start on, and STAYS present after going live too (GoLive's own write never touches this key, so it is
        /// simply never removed) - the master writes it once, alongside TeamsInMatchKey, and never again.
        public const string LiveAtKey = "mLiveAt";

        // Not gameplay values. The master re-checks every 0.5 s in the warm-up (a callback alone can be missed -
        // clock not synced, snapshot not read yet - and would strand a full room there) and every frame while
        // counting down (to go live on the frame its clock reaches mLiveAt). After sending a countdown or cancel
        // write it waits for the room to echo it - or 1 s, in case a check-and-set was refused - so one decision
        // is never sent twice.
        private const float StartCheckIntervalSeconds = 0.5f;
        private const float EchoWaitSeconds = 1f;
        private float nextStartCheck;
        private float waitForEchoUntil = -1f; // cleared by OnRoomPropertiesUpdate when mTeams/mLiveAt/mPhase arrive
        private bool liveWritten;             // master: the live write was sent

        /// <summary>Warmup/CountingDown/Live, from the two Room Property facts (Decision 1-2).</summary>
        public StartState State => MatchStartRules.StartStateFor(TeamsFixed, IsLive);

        /// <summary>mPhase is present - the moment the match went live (Decision 1). Reads the room directly,
        /// like IsEliminated: the countdown is still warm-up (Decision 3), so nothing gates on this client's own
        /// clock or on liveWritten, only on the room's own echoed value.</summary>
        public bool IsLive => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PhaseKey);

        /// <summary>mTeams is present and mPhase is not - the countdown is running.</summary>
        public bool IsCountingDown => TeamsFixed && !IsLive;

        /// <summary>mTeams is present - the teams in the match are fixed (Decision 4), whether counting down or
        /// already live.</summary>
        public bool TeamsFixed => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TeamsInMatchKey);

        /// <summary>The server ms the match goes live, or 0 before the countdown starts.</summary>
        public int LiveAtMs =>
            PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(LiveAtKey, out object raw) && raw is int v
                ? v : 0;

        /// <summary>"Match starts in N" (Decision 22) on this client's own synced clock. Review fix: before this
        /// client's own clock has synced, PhotonNetwork.ServerTimestamp reads 0 for its first frames (BuildingManager's
        /// own "FAIL #15" comment already records this) - liveAtMs - 0 would read as a nonsense huge number, so the
        /// pure rule takes the configured countdown length as a fallback for exactly that case, sourced here from the
        /// local player's own GameplayConfig (LocalPlayerConfig, the same getter StartCountdown already uses - it
        /// needs no server clock, just the serialized reference on this player's own prefab).</summary>
        public int CountdownSecondsShown =>
            MatchStartRules.CountdownSecondsShown(PhotonNetwork.ServerTimestamp, LiveAtMs,
                LocalPlayerConfig()?.MatchStartCountdownSeconds ?? 0f);

        /// <summary>The teams fixed into the match (Decision 4), ascending - empty before the countdown starts.</summary>
        public int[] TeamsInMatch =>
            PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(TeamsInMatchKey, out object raw) && raw is int[] arr
                ? arr : System.Array.Empty<int>();

        /// <summary>Whether team was fixed into the match at the countdown's start.</summary>
        public bool IsInMatch(int team) => System.Array.IndexOf(TeamsInMatch, team) >= 0;

        /// <summary>Map shrink T3 (D1/D13): the team whose corner is closed right now, or PhaseTwoCutRules.NoCut.
        /// Reads the room directly (like IsLive), so it is right for a late joiner on its very first frame and
        /// after a master switch.</summary>
        public int CutTeam => PhotonNetwork.InRoom
            ? PhaseTwoCutRules.CutTeam(IsLive, TeamsInMatch, ReadCutTeam(PhotonNetwork.CurrentRoom.CustomProperties))
            : PhaseTwoCutRules.NoCut;

        /// <summary>A zone behind the phase-two wall, or the cut capital of a host start. The MatchStartRules.
        /// IsCapitalOutOfPlay call stays (Decision 8: out of play from LIVE - the countdown is still warm-up,
        /// Decision 3) as a belt-and-braces check, but it is no longer the only thing catching a host start's
        /// left-out capital: once live, CutTeam already derives that same team as the cut (PhaseTwoCutRules.CutTeam
        /// falls back to the team missing from TeamsInMatch), so PhaseTwoCutRules.IsZoneCut below already covers its
        /// capital too - final review, 2026-09-25.</summary>
        public bool IsOutOfPlay(int zone)
        {
            BuildingManager buildings = BuildingManager.Instance;
            TerritoryMap map = buildings != null ? buildings.Map : null;
            int capitalTeam = map != null ? map.CapitalTeamOf(zone) : TerritoryMap.Neutral;
            if (MatchStartRules.IsCapitalOutOfPlay(IsLive, capitalTeam, TeamsInMatch))
                return true;
            return map != null && PhaseTwoCutRules.IsZoneCut(map, zone, CutTeam, BaseTierOf);
        }

        // Cached once: IsOutOfPlay runs every frame for every tower and minimap bubble, and a fresh method-group delegate
        // per call would allocate each time.
        private System.Func<int, int> baseTierOf;
        private System.Func<int, int> BaseTierOf => baseTierOf ??= zone =>
            BuildingManager.Instance != null ? BuildingManager.Instance.BaseTierOf(zone) : 0;

        /// <summary>Decision 4/17 (R3): before the teams are fixed, any team; from the countdown on, only a team
        /// in the match and not knocked out. RoomManager reads this for both PickSmallestTeam and
        /// EnsureLocalTeamInMatch.</summary>
        public bool MayJoinTeam(int team) => MatchStartRules.MayJoin(TeamsFixed, IsInMatch(team), IsEliminated(team));

        /// <summary>How many of the three teams currently have a player - the warm-up line's own question,
        /// polled every frame (MatchStartPanel, step 8), so counted through PhotonNetwork.CurrentRoom.Players
        /// (a Dictionary; its enumerator is a struct, unlike PhotonNetwork.PlayerList's freshly sorted array -
        /// same reasoning as MinimapView.UpdatePlayers) and CountMembers' own reused array, so this allocates
        /// nothing per frame.</summary>
        public int TeamsWithPlayersNow => MatchStartRules.CountTeamsWithPlayers(CountMembers());

        /// <summary>Tudor: with only two teams, the host gets a Start button - polled every frame the warm-up
        /// panel is open (step 8), same allocation reasoning as TeamsWithPlayersNow (nothing per frame).</summary>
        public bool HostMayStartNow =>
            PhotonNetwork.IsMasterClient && MatchStartRules.HostMayStart(TeamsFixed, CountMembers(), PlayersWithoutATeam());

        /// <summary>Covers the countdown starting, being cancelled, and the match going live - MatchStartPanel
        /// (step 8) subscribes instead of polling every frame for a change that happens rarely. Map shrink T3: the
        /// phase-two cut changing (mCut) raises it too - the minimap already listens, to redraw its overlay.</summary>
        public event System.Action LiveStateChanged;

        private void Update()
        {
            // Map shrink F3: a promotion's cut-zone catch-up, one frame after OnMasterClientSwitched - before the early
            // returns below, which skip everything once the match is live.
            if (finishCutNeutralisePending)
            {
                finishCutNeutralisePending = false;
                FinishCutNeutralise();
            }

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || liveWritten || IsLive
                || Time.unscaledTime < waitForEchoUntil)
                return;

            if (IsCountingDown)
            {
                if (MatchStartRules.CountdownShouldCancel(TeamsInMatch, CountMembers()))
                    CancelCountdown();
                else if (MatchStartRules.HasReached(PhotonNetwork.ServerTimestamp, LiveAtMs))
                    GoLive(TeamsInMatch);
                return;
            }

            if (Time.unscaledTime < nextStartCheck)
                return;
            nextStartCheck = Time.unscaledTime + StartCheckIntervalSeconds;
            int[] members = CountMembers();
            if (MatchStartRules.StartsCountdownAutomatically(TeamsFixed, members))
                StartCountdown(MatchStartRules.TeamsWithPlayers(members));
        }

        /// <summary>The host's Start button calls this directly - the host IS the master, so this is a local call
        /// and no RPC is needed. Starts the countdown; refused (and the button hides next frame) if mastership
        /// moved or the rule no longer holds.</summary>
        public void HostStartMatch()
        {
            if (!HostMayStartNow)
            {
                Debug.LogWarning("[MATCH] Start refused: not the master, already started, or not exactly two teams.");
                return;
            }
            StartCountdown(MatchStartRules.TeamsWithPlayers(CountMembers()));
        }

        private void StartCountdown(int[] teams)
        {
            GameplayConfig config = LocalPlayerConfig();
            if (config == null || PhotonNetwork.ServerTimestamp == 0 || teams.Length < 2)
                return; // player not spawned / clock not synced yet - the next poll tries again

            int liveAt = MatchStartRules.CountdownEndsAt(PhotonNetwork.ServerTimestamp, config.MatchStartCountdownSeconds);
            var props = new Hashtable { { TeamsInMatchKey, teams }, { LiveAtKey, liveAt } };
            // Check-and-set on the ABSENT key (as MatchTelemetry's identity claim): two masters racing across a
            // switch can't both start a countdown, and the teams can't be fixed twice.
            var expectedAbsent = new Hashtable { { TeamsInMatchKey, null } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent))
                return;

            waitForEchoUntil = Time.unscaledTime + EchoWaitSeconds;
            // A host-started match holds six, not nine (Decision 7).
            PhotonNetwork.CurrentRoom.MaxPlayers = (byte)(teams.Length * RoomManager.TeamSize);
            // 2.7b step 9: the countdown starting is a marker, not a phase change - going live is still what
            // logs the real `phase` line (below, in GoLive). The existing F1 marker shape, so the report's
            // Markers section shows it with no new event type.
            MatchTelemetry.Instance?.DropMarker("countdown start");
            Debug.Log($"[MATCH] countdown: teams [{string.Join(",", teams)}], live at {liveAt}");
        }

        /// <summary>Tudor, 2026-09-18: a team in the countdown emptied - back to the warm-up. Check-and-set
        /// expecting mPhase still absent: a countdown that already went live is never cancelled (R13).</summary>
        private void CancelCountdown()
        {
            var props = new Hashtable { { TeamsInMatchKey, null }, { LiveAtKey, null } };
            var expected = new Hashtable { { PhaseKey, null } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expected))
                return;
            waitForEchoUntil = Time.unscaledTime + EchoWaitSeconds;
            PhotonNetwork.CurrentRoom.MaxPlayers = (byte)(MatchStartRules.TeamCount * RoomManager.TeamSize);
            MatchTelemetry.Instance?.DropMarker("countdown cancelled"); // 2.7b step 9 - see StartCountdown's own comment.
            Debug.Log("[MATCH] countdown cancelled: a team in it emptied - back to the warm-up");
        }

        /// <summary>Decision 5: territory FIRST, match keys SECOND, in one frame - the frame this master's clock
        /// reached mLiveAt (or, for a single-client check, a direct reflection call that skips the countdown).
        /// Photon delivers one client's writes in order, so every client applies the reset before it sees the
        /// match go live - which is what its own fresh start (MatchDirector.ReactToRoomState's live edge) and
        /// every knockout check (MasterRecompute, gated on the echoed mPhase) rely on. Why the master's write,
        /// and not each client's own clock, is the moment: see Decision 5's own comment on the plan.</summary>
        private void GoLive(int[] teams)
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || liveWritten || IsLive)
                return;

            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null || !buildings.ResetForMatchStart(teams))
                return; // snapshot not read / clock not synced yet - the next frame tries again

            MatchPhase phase = teams.Length >= MatchStartRules.TeamCount ? MatchPhase.ThreeTeams : MatchPhase.TwoTeams;
            var props = new Hashtable
            {
                { TeamsInMatchKey, teams }, // the same value the countdown fixed - also lets GoLive be called
                                            // directly by reflection in a single-client check that skips it.
                { PhaseKey, (int)phase },
                { EliminatedKey, new int[0] },
                { WinnerKey, -1 },
            };
            // Check-and-set on the ABSENT mPhase: the match goes live once only, whichever master gets there.
            var expectedAbsent = new Hashtable { { PhaseKey, null } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent))
                return;

            liveWritten = true;
            lastWrittenEliminated = new System.Collections.Generic.List<int>();
            lastWrittenPhase = phase;
            lastWrittenWinner = -1;
            // R2: check-and-set means the live write can only ever succeed once (above), but near a master
            // switch two clients can both believe they are master and both send it. The loser's own client-side
            // check passes (its cached room properties still show mPhase absent) and it optimistically counts
            // writesAwaitingEcho here exactly like the winner does - the server then refuses ITS write once the
            // winner's has already landed. writesAwaitingEcho stays at 1 on the losing client until the WINNING
            // master's own write echoes back to it too (a genuine room property change, delivered to every
            // client including this one) and OnRoomPropertiesUpdate decrements it there. It heals itself; no
            // action needed.
            writesAwaitingEcho++;
            // 2.7b step 9: the telemetry live line, right after the write - PhaseTimeline.From reads the first
            // `phase` >= 1 event as the live moment (the countdown itself is still warm-up - Decision 3), so
            // this must land here, in the master's own live write, not in StartCountdown. `phase` 1 (three
            // teams) or 2 (a host start) - never logged twice, since GoLive itself only ever runs once
            // (liveWritten/IsLive guard at the top).
            MatchTelemetry.Instance?.LogPhase((int)phase, teams);
            Debug.Log($"[MATCH] live: teams [{string.Join(",", teams)}], {phase}");
        }

        // Reused across every CountMembers() call - Update's own poll, HostStartMatch, StartCountdown, and
        // (step 8) TeamsWithPlayersNow/HostMayStartNow, both polled every frame the warm-up panel is open. Not
        // static any more (it wraps an instance field now), but MatchDirector is a singleton (Instance), so this
        // still costs nothing per frame instead of a fresh int[3] every single call.
        private readonly int[] countMembersScratch = new int[MatchStartRules.TeamCount];

        /// <summary>How many players are on each of the three teams right now - counted through
        /// PhotonNetwork.CurrentRoom.Players (a Dictionary, struct enumerator) rather than PhotonNetwork.
        /// PlayerList, which sorts into a fresh array every call. Shared by every caller in this file:
        /// Update's own poll, HostStartMatch and StartCountdown.</summary>
        private int[] CountMembers()
        {
            System.Array.Clear(countMembersScratch, 0, countMembersScratch.Length);
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return countMembersScratch;
            foreach (System.Collections.Generic.KeyValuePair<int, Player> pair in room.Players)
                if (Teams.TryGetTeam(pair.Value, out int t) && t >= 0 && t < countMembersScratch.Length)
                    countMembersScratch[t]++;
            return countMembersScratch;
        }

        /// <summary>How many players in the room have no team Custom Property yet (Decision 17, R3) - the host
        /// cannot start while this is above 0, because one of them might be the third team.</summary>
        private static int PlayersWithoutATeam()
        {
            int count = 0;
            Room room = PhotonNetwork.CurrentRoom;
            if (room != null)
                foreach (System.Collections.Generic.KeyValuePair<int, Player> pair in room.Players)
                    if (!Teams.TryGetTeam(pair.Value, out _))
                        count++;
            return count;
        }

        /// <summary>The countdown length (Decision 22, R15) is only ever read through the master's own player -
        /// MatchDirector has no serialized fields (BuildingManager.Awake adds it at runtime), so this is the one
        /// place that asks. Null while the master's own player has not spawned yet; StartCountdown's caller
        /// tries again on the next poll.</summary>
        private static GameplayConfig LocalPlayerConfig()
        {
            PhotonView localView = PhotonNetwork.LocalPlayer != null
                ? PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) : null;
            PlayerLifecycle lifecycle = localView != null ? localView.GetComponent<PlayerLifecycle>() : null;
            return lifecycle != null ? lifecycle.Config : null;
        }
    }
}
