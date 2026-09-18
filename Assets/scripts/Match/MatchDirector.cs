using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Net;
using Overpower.Telemetry;
using Overpower.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Match
{
    /// <summary>
    /// Task 2.7: who is still in the match and which phase it is in. MatchPhaseRules.cs holds the
    /// actual rule as pure, tested C#; this class is only the Photon wiring around it - reading
    /// PhotonNetwork.PlayerList and BuildingManager.Current into one TeamStatus per team, letting
    /// the master write the result into Room Properties, and reacting to whatever the room says on
    /// every client (the lost panel, the match-result panel, the Tier-3 reset). Room Properties, not
    /// an RPC, for the same reason BuildingManager's territory is: a player who joins mid-match reads
    /// one value instead of replaying the match, and the state survives the master leaving.
    ///
    /// NOT a scene object [C, controller decision, 2026-09-18]: Tudor is rebuilding the arena from
    /// primitives in a separate session, so anything placed in Game Scene.unity right now could be
    /// lost or conflict with that rebuild. BuildingManager.Awake adds this component at runtime
    /// instead, onto the same GameObject that already hosts BuildingManager/ZonePresenceTracker/
    /// MatchTelemetry - zero scene footprint, and no PhotonView is needed because this only ever
    /// reads and writes Room Properties, the same authority model BuildingManager's own territory
    /// state uses (CODING-STANDARDS.md section 5).
    /// </summary>
    public class MatchDirector : MonoBehaviourPunCallbacks
    {
        public static MatchDirector Instance { get; private set; }

        /// Room Property key: the match's MatchPhase, as an int (MatchPhase.ThreeTeams == 1).
        public const string PhaseKey = "mPhase";
        /// Room Property key: int[] of eliminated team ids, in the order they were eliminated.
        public const string EliminatedKey = "mElim";
        /// Room Property key: the winning team id once Phase is Over, else -1.
        public const string WinnerKey = "mWin";

        // Always 0/1/2 - the same fixed team count CathedralBuildingIDs and TerritoryConfig.
        // PlayersPerTeam already assume everywhere else in this codebase.
        private const int TeamCount = 3;

        // ---- master only: the write side, mirroring BuildingManager's lastWritten/writesAwaitingEcho
        // pattern exactly (see that class's own comment on why). Room Properties come back from the
        // server a network round trip after they are set, so a second recompute inside that same
        // window (e.g. two zones neutralised in the same Apply(), each raising its own
        // OwnershipChanged) must build on the write this client already sent, not on the room's own
        // still-stale copy, or it would silently undo it.
        private List<int> lastWrittenEliminated;
        private MatchPhase lastWrittenPhase = MatchPhase.ThreeTeams;
        private int lastWrittenWinner = -1;
        private int writesAwaitingEcho;

        // ---- every client: the read side, so OnRoomPropertiesUpdate can tell a genuinely new
        // elimination or phase change from Photon re-sending a value this client already reacted to.
        private List<int> lastAppliedEliminated = new List<int>();
        private MatchPhase lastAppliedPhase = MatchPhase.ThreeTeams;
        private int lastAppliedWinner = -1;

        /// <summary>The phase this client has last read from the room. ThreeTeams before the first read.</summary>
        public MatchPhase Phase => lastAppliedPhase;
        /// <summary>The winning team once the match is over, else -1. Tracks the room, not this client's own team.</summary>
        public int Winner => lastAppliedWinner;

        /// <summary>This team's capital zone id (GDD-fixed: 6/7/8 for teams 0/1/2 - see BuildingManager.
        /// CathedralBuildingIDs, which TerritoryMap.CapitalOf was built from). Capital adoption - a
        /// last-stand team keeping an enemy capital it just captures, GDD p.20 - was cut for this task
        /// [C, 2026-09-18]; if it is ever built, this is the one place PlayerLifecycle and
        /// BuildingManager should keep asking, so adoption only has to change this method.</summary>
        public int CapitalOf(int team) =>
            BuildingManager.Instance != null && BuildingManager.Instance.Map != null
                ? BuildingManager.Instance.Map.CapitalOf(team) : TerritoryMap.Neutral;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(this); // Should not happen - BuildingManager.Awake adds exactly one of these.
        }

        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
            if (BuildingManager.Instance != null)
                BuildingManager.Instance.OwnershipChanged += HandleOwnershipChanged;
        }

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            if (BuildingManager.Instance != null)
                BuildingManager.Instance.OwnershipChanged -= HandleOwnershipChanged;
        }

        private void Start()
        {
            if (PhotonNetwork.IsMasterClient)
                StartCoroutine(InitialRecomputeWhenTerritoryIsReady());
        }

        /// BuildingManager's own initial snapshot write applies with raiseEvents:false (its "the match
        /// as it already is" comment), so OwnershipChanged never fires for it - without this wait, a
        /// match that starts with only two real teams (the two-player test) would sit at the
        /// ThreeTeams default until the first capture or death happened to trigger a recompute.
        private IEnumerator InitialRecomputeWhenTerritoryIsReady()
        {
            while (BuildingManager.Instance == null || BuildingManager.Instance.Current == null || BuildingManager.Instance.Map == null)
                yield return null;
            MasterRecompute();
        }

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot) =>
            MasterRecompute();

        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (changedProps.ContainsKey(PlayerLifecycle.AliveKey) || changedProps.ContainsKey(Teams.TeamKey))
                MasterRecompute();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer) => MasterRecompute();

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // Pending-write bookkeeping belonged to the old master's writes, not ours - same
            // reasoning as BuildingManager.OnMasterClientSwitched.
            lastWrittenEliminated = null;
            writesAwaitingEcho = 0;

            // The promoted master must not wait for the next capture or death to catch up - it has
            // to be ready to decide the very next elimination on its own (verification 3).
            if (PhotonNetwork.IsMasterClient)
                MasterRecompute();
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (propertiesThatChanged == null)
                return;

            bool touchesMatchState = propertiesThatChanged.ContainsKey(PhaseKey)
                || propertiesThatChanged.ContainsKey(EliminatedKey) || propertiesThatChanged.ContainsKey(WinnerKey);
            if (!touchesMatchState)
                return;

            if (writesAwaitingEcho > 0)
                writesAwaitingEcho--;

            ReactToRoomState(firstRead: false);
        }

        public override void OnJoinedRoom() => ReactToRoomState(firstRead: true);

        /// <summary>Called by BuildingManager.CheckTerritoryWin when one team holds every capital -
        /// the second, GDD-external win condition alongside elimination (an earlier project decision,
        /// kept as an extra win condition per this plan's own "Decisions taken" table). Master only:
        /// writes mWin/mPhase so every client's reaction is the same one path (ReactToRoomState)
        /// whichever way the match actually ended, instead of a separate RPC. Eliminated is left
        /// exactly as MatchPhaseRules last computed it - a territory win does not necessarily mean
        /// every other team also lost its capital, so this must not invent elimination facts
        /// MatchPhaseRules never decided.</summary>
        public void AnnounceTerritoryWin(int winningTeam)
        {
            if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
                return;
            if (writesAwaitingEcho == 0 && lastWrittenWinner == winningTeam)
                return; // Already the room's own answer - BuildingManager's own latch also guards this, this is defensive.

            var props = new Hashtable { { PhaseKey, (int)MatchPhase.Over }, { WinnerKey, winningTeam } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
                return;

            lastWrittenPhase = MatchPhase.Over;
            lastWrittenWinner = winningTeam;
            writesAwaitingEcho++;
        }

        /// <summary>Nudges a recompute from outside the four wired triggers - PlayerLifecycle.
        /// RPC_HandleDeathMaster's gutted body calls this in case its RPC beats the "alive" Player
        /// Property it was sent right after across the wire. A no-op off the master (MasterRecompute's
        /// own guard), so safe to call from anywhere.</summary>
        public void RequestRecompute() => MasterRecompute();

        // ---------------------------------------------------------------- master: recompute + write

        private void MasterRecompute()
        {
            if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
                return;

            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null || buildings.Current == null || buildings.Map == null)
                return;

            Hashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
            bool echoPending = writesAwaitingEcho > 0 && lastWrittenEliminated != null;
            List<int> previousEliminated = echoPending ? lastWrittenEliminated : ReadEliminated(roomProps);
            MatchPhase previousPhase = echoPending ? lastWrittenPhase : ReadPhase(roomProps);
            int previousWinner = echoPending ? lastWrittenWinner : ReadWinner(roomProps);

            TeamStatus[] statuses = BuildTeamStatuses(buildings);
            MatchPhaseResult result = MatchPhaseRules.Recompute(previousEliminated, statuses);

            // Recompute always starts from previousEliminated's own items (Recompute's first line is
            // AddRange(alreadyEliminated)), so anything after that prefix is newly found this call -
            // possibly more than one team at once (a last-stand team can be swept in by the very
            // elimination that narrows the match to two teams).
            List<int> newlyEliminated = result.Eliminated.Skip(previousEliminated.Count).ToList();
            bool changed = newlyEliminated.Count > 0 || result.Phase != previousPhase || result.Winner != previousWinner;
            if (!changed)
                return;

            var props = new Hashtable
            {
                { PhaseKey, (int)result.Phase },
                { EliminatedKey, result.Eliminated.ToArray() },
                { WinnerKey, result.Winner },
            };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
                return; // Nothing sent - leave lastWritten/writesAwaitingEcho untouched, same as BuildingManager.Write.

            lastWrittenEliminated = result.Eliminated;
            lastWrittenPhase = result.Phase;
            lastWrittenWinner = result.Winner;
            writesAwaitingEcho++;

            if (previousPhase == MatchPhase.ThreeTeams && result.Phase == MatchPhase.TwoTeams)
                NeutraliseTierThreeZones(buildings);

            LogTelemetry(newlyEliminated, previousPhase, result, statuses);
        }

        /// GDD p.20-21: the first elimination sends every Tier-3 zone neutral. SetNeutral is already
        /// a no-op on an already-neutral zone (its own guard) and master-only (WriteBasis's guard), so
        /// looping every zone here is safe even if this runs more than once.
        private static void NeutraliseTierThreeZones(BuildingManager buildings)
        {
            int[] tiers = buildings.TierByZone();
            for (int zone = 0; zone < tiers.Length; zone++)
                if (tiers[zone] == 3)
                    buildings.SetNeutral(zone);
        }

        private static void LogTelemetry(List<int> newlyEliminated, MatchPhase previousPhase, MatchPhaseResult result, TeamStatus[] statuses)
        {
            if (MatchTelemetry.Instance == null)
                return;

            int[] teamsRemaining = statuses.Where(s => s.Members > 0 && !result.Eliminated.Contains(s.TeamId))
                                            .Select(s => s.TeamId).ToArray();

            foreach (int team in newlyEliminated)
                MatchTelemetry.Instance.LogElimination(team, teamsRemaining);

            // LogPhase(2, ...) must land exactly at the three-to-two transition (PhaseTimeline.From
            // opens its "Phase 2" window on the first phase event numbered >= 2) - firing it whenever
            // either an elimination happened or the phase number itself moved (a team's last player
            // leaving the room can drop the phase with no new elimination - see BuildTeamStatuses)
            // covers both ways the transition can actually happen.
            if (newlyEliminated.Count > 0 || result.Phase != previousPhase)
                MatchTelemetry.Instance.LogPhase((int)result.Phase, teamsRemaining);
        }

        private static TeamStatus[] BuildTeamStatuses(BuildingManager buildings)
        {
            var statuses = new TeamStatus[TeamCount];
            TerritoryMap map = buildings.Map;
            TerritorySnapshot current = buildings.Current;

            for (int team = 0; team < TeamCount; team++)
            {
                int members = 0, alive = 0;
                foreach (Player p in PhotonNetwork.PlayerList)
                {
                    if (!Teams.TryGetTeam(p, out int t) || t != team)
                        continue;
                    members++;

                    // Missing "alive" property means this player has never died yet (PlayerLifecycle
                    // only ever publishes it on the first death/respawn) - the same default isAlive
                    // = true PlayerLifecycle itself starts with.
                    bool isAlive = true;
                    if (p.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool b)
                        isAlive = b;
                    if (isAlive) alive++;
                }

                int capital = map.CapitalOf(team);
                bool holdsCapital = capital >= 0 && current.OwnerOf(capital) == team;

                statuses[team] = new TeamStatus { TeamId = team, Members = members, AliveMembers = alive, HoldsItsCapital = holdsCapital };
            }
            return statuses;
        }

        // ---------------------------------------------------------------- every client: react

        /// Reads the room's mPhase/mElim/mWin and applies whatever changed since this client last
        /// looked - the lost panel for a newly eliminated own team, the Tier-3 "two teams left" spawn-
        /// home + banner the first time this client itself sees the ThreeTeams -> TwoTeams edge, and
        /// the match-result panel once a winner exists. firstRead (OnJoinedRoom, including a late
        /// joiner) only ever applies the winner reaction - a joiner arriving mid-match should not be
        /// sent "home" or told the two-team rule "just" changed for a transition that already happened
        /// before they connected; MatchUI.ShowMatchResult alone already answers requirement 2 (a late
        /// joiner reads Over and the winner) whether they were eliminated earlier or not.
        private void ReactToRoomState(bool firstRead)
        {
            if (!PhotonNetwork.InRoom)
                return;

            Hashtable props = PhotonNetwork.CurrentRoom.CustomProperties;
            MatchPhase phase = ReadPhase(props);
            List<int> eliminated = ReadEliminated(props);
            int winner = ReadWinner(props);

            PhotonView localView = PhotonNetwork.LocalPlayer != null
                ? PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) : null;

            if (!firstRead)
            {
                if (Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam)
                    && !lastAppliedEliminated.Contains(myTeam) && eliminated.Contains(myTeam))
                {
                    localView?.GetComponent<MatchUI>()?.ShowYouLost();
                }

                if (lastAppliedPhase == MatchPhase.ThreeTeams && phase == MatchPhase.TwoTeams)
                {
                    PlayerLifecycle lifecycle = localView != null ? localView.GetComponent<PlayerLifecycle>() : null;
                    if (lifecycle != null && lifecycle.IsAlive)
                        lifecycle.ReturnToSpawn();
                    localView?.GetComponent<PlayerHud>()?.ShowTwoTeamsLeftBanner();
                }
            }

            if (winner >= 0 && winner != lastAppliedWinner)
                localView?.GetComponent<MatchUI>()?.ShowMatchResult(winner);

            lastAppliedEliminated = eliminated;
            lastAppliedPhase = phase;
            lastAppliedWinner = winner;
        }

        private static MatchPhase ReadPhase(Hashtable props) =>
            props.TryGetValue(PhaseKey, out object raw) && raw is int p ? (MatchPhase)p : MatchPhase.ThreeTeams;

        private static List<int> ReadEliminated(Hashtable props) =>
            props.TryGetValue(EliminatedKey, out object raw) && raw is int[] arr ? new List<int>(arr) : new List<int>();

        private static int ReadWinner(Hashtable props) =>
            props.TryGetValue(WinnerKey, out object raw) && raw is int w ? w : -1;
    }
}
