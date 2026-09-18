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
    /// PhotonNetwork.PlayerList and BuildingManager.Current into one TeamStatus per team, letting the
    /// master write the result into Room Properties (and, master-only, neutralising Tier-3 zones on
    /// the first elimination and closing a finished room), and reacting to whatever the room says on
    /// every client (the lost panel, the "two teams left" banner and trip home, the match-result
    /// panel). Room Properties, not an RPC, for the same reason BuildingManager's territory is: a
    /// player who joins mid-match reads one value instead of replaying the match, and the state
    /// survives the master leaving.
    ///
    /// NOT a scene object: the arena is being rebuilt from primitives in a separate session, so
    /// anything placed in Game Scene.unity right now could be lost or conflict with that rebuild.
    /// BuildingManager.Awake adds this component at runtime instead, onto the same GameObject that
    /// already hosts BuildingManager/ZonePresenceTracker/MatchTelemetry - zero scene footprint, and no
    /// PhotonView is needed because this only ever reads and writes Room Properties, the same
    /// authority model BuildingManager's own territory state uses (CODING-STANDARDS.md section 5).
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
        /// <summary>Whether team is on the room's own eliminated list. Reads PhotonNetwork.CurrentRoom.
        /// CustomProperties directly rather than the cached lastAppliedEliminated (review round 2):
        /// RoomManager.PickSmallestTeam calls this from ITS OWN OnJoinedRoom, which Photon dispatches
        /// BEFORE this class's OnJoinedRoom runs ReactToRoomState - the room's own properties are
        /// already filled in by then (they arrive as part of the join itself), but the cached field
        /// is not written until this object's own callback gets its turn.</summary>
        public bool IsEliminated(int team) =>
            PhotonNetwork.InRoom && ReadEliminated(PhotonNetwork.CurrentRoom.CustomProperties).Contains(team);

        /// <summary>This team's capital zone id (GDD-fixed: 6/7/8 for teams 0/1/2 - see BuildingManager.
        /// CathedralBuildingIDs, which TerritoryMap.CapitalOf was built from). Capital adoption - a
        /// last-stand team keeping an enemy capital it just captures, GDD p.20 - was cut for this
        /// task; if it is ever built, this is the one place PlayerLifecycle and BuildingManager should
        /// keep asking, so adoption only has to change this method.</summary>
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

        public override void OnEnable()
        {
            base.OnEnable(); // Registers this as a Photon callback target - see MonoBehaviourPunCallbacks's own class comment.
            if (BuildingManager.Instance != null)
                BuildingManager.Instance.OwnershipChanged += HandleOwnershipChanged;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (BuildingManager.Instance != null)
                BuildingManager.Instance.OwnershipChanged -= HandleOwnershipChanged;
        }

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot) =>
            MasterRecompute();

        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (changedProps.ContainsKey(PlayerLifecycle.LastStandKey) || changedProps.ContainsKey(Teams.TeamKey))
                MasterRecompute();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer) => MasterRecompute();

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // Pending-write bookkeeping belonged to the old master's writes, not ours - same
            // reasoning as BuildingManager.OnMasterClientSwitched.
            lastWrittenEliminated = null;
            writesAwaitingEcho = 0;

            // The promoted master must not wait for the next capture or death to catch up - it has to
            // be ready to decide the very next elimination on its own.
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

        /// <summary>The next room is a different match; nothing from this one may leak into it - same
        /// reasoning as BuildingManager.OnLeftRoom. Found missing live (Task 2.7 review re-
        /// verification): without this, a client that leaves a finished match and joins another
        /// inside the same running process keeps this object's stale lastWrittenPhase/lastApplied*
        /// from the match it just left - RoomManager.PickSmallestTeam then reads a stale
        /// IsEliminated for a team that was never even in the new room, and a promoted master's own
        /// MasterRecompute can refuse to ever write again because it still believes Over.</summary>
        public override void OnLeftRoom()
        {
            lastWrittenEliminated = null;
            lastWrittenPhase = MatchPhase.ThreeTeams;
            lastWrittenWinner = -1;
            writesAwaitingEcho = 0;

            lastAppliedEliminated = new List<int>();
            lastAppliedPhase = MatchPhase.ThreeTeams;
            lastAppliedWinner = -1;
        }

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
            CloseFinishedRoom();
        }

        /// <summary>Nudges a recompute from outside the four wired triggers - PlayerLifecycle.
        /// RPC_HandleDeathMaster's gutted body calls this in case its RPC beats the "alive" Player
        /// Property it was sent right after across the wire. A no-op off the master (MasterRecompute's
        /// own guard), so safe to call from anywhere.</summary>
        public void RequestRecompute() => MasterRecompute();

        /// <summary>PlayerLifecycle.Start calls this once (photonView.IsMine, right after registering
        /// itself in PlayerLookup): OnJoinedRoom is a room-level callback that can run BEFORE this
        /// client's own player object is network-instantiated, especially for a late joiner, so
        /// ReactToRoomState's very first call can find no local view to react on and its winner
        /// reaction is silently skipped. That first call still updates lastAppliedWinner, so a plain
        /// retry of ReactToRoomState would see winner == lastAppliedWinner and do nothing - this
        /// applies the CURRENT winner unconditionally instead. A match that is already over does not
        /// change again, so there is nothing else worth catching up here.</summary>
        public void CatchUpLocalPlayer()
        {
            if (!PhotonNetwork.InRoom)
                return;

            int winner = ReadWinner(PhotonNetwork.CurrentRoom.CustomProperties);
            if (winner < 0)
                return;

            PhotonView localView = PhotonNetwork.LocalPlayer != null
                ? PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) : null;
            localView?.GetComponent<MatchUI>()?.ShowMatchResult(winner);
        }

        // ---------------------------------------------------------------- master: recompute + write

        private void MasterRecompute()
        {
            if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
                return;

            // A decided match is final - it must never be rebuilt from whoever happens to still be
            // connected (a later leave or join is not a new fact MatchPhaseRules needs to hear about).
            if (lastWrittenPhase == MatchPhase.Over || ReadPhase(PhotonNetwork.CurrentRoom.CustomProperties) == MatchPhase.Over)
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
            // possibly more than one team at once (GDD p.21: the instant an elimination narrows the
            // match to two teams, a further capital-less team is swept in by that same call, not next
            // tick - MatchPhaseRules.Recompute's own fixpoint, restored in the review round-2 fix).
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
            if (result.Phase == MatchPhase.Over)
                CloseFinishedRoom();

            LogTelemetry(newlyEliminated, previousPhase, result, statuses);
        }

        /// GDD p.20-21: the first elimination sends every Tier-3 zone neutral. Uses
        /// SetNeutralWithoutBountyHistory, not the ordinary SetNeutral: the GDD's bounty (p.20) is for
        /// taking a zone FROM the team that held it, and this reset takes every Tier-3 zone from
        /// nobody - the next team to capture one must not be paid for a multi-minute hold that was
        /// reset out from under its owner, not fought for. Master-only (WriteBasis's guard) and safe
        /// to loop every zone even if this runs more than once: a zone already neutral with no hold
        /// history is skipped (TerritorySnapshot.NeedsNeutralReset), and one that drained to neutral
        /// naturally but still carries history is wiped - which is the whole point of this method.
        private static void NeutraliseTierThreeZones(BuildingManager buildings)
        {
            int[] tiers = buildings.TierByZone();
            for (int zone = 0; zone < tiers.Length; zone++)
                if (tiers[zone] == 3)
                    buildings.SetNeutralWithoutBountyHistory(zone);
        }

        /// A decided match's room must not keep taking new players - RoomManager.JoinRandomRoom would
        /// otherwise place a fresh joiner straight into a frozen, finished match. Photon's own server-
        /// side room flags: no new Room Property, no RPC, and they survive a master change since they
        /// live on the room itself, not on any one client. A random joiner then gets
        /// OnJoinRandomFailed and RoomManager creates a fresh room.
        private static void CloseFinishedRoom()
        {
            Photon.Realtime.Room room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return;
            room.IsOpen = false;
            room.IsVisible = false;
        }

        private static void LogTelemetry(List<int> newlyEliminated, MatchPhase previousPhase, MatchPhaseResult result, TeamStatus[] statuses)
        {
            if (MatchTelemetry.Instance == null)
                return;

            int[] teamsRemaining = statuses.Where(s => s.InMatch && !result.Eliminated.Contains(s.TeamId))
                                            .Select(s => s.TeamId).ToArray();

            foreach (int team in newlyEliminated)
                MatchTelemetry.Instance.LogElimination(team, teamsRemaining);

            // LogPhase(2, ...) must land exactly at the three-to-two transition (PhaseTimeline.From
            // opens its "Phase 2" window on the first phase event numbered >= 2) - firing it whenever
            // either an elimination happened or the phase number itself moved (a team's last player
            // leaving the room, once at least one elimination has already happened, can drop the phase
            // with no new elimination of its own - see MatchPhaseRules.PhaseFor) covers both ways the
            // transition can actually happen.
            if (newlyEliminated.Count > 0 || result.Phase != previousPhase)
                MatchTelemetry.Instance.LogPhase((int)result.Phase, teamsRemaining);
        }

        /// <summary>2.7b step 3 [InMatch is interim, replaced in step 5 by mTeams - every team reads true until the
        /// countdown/live system exists]. HoldsOwnCapital/HoldsAnyCapitalInPlay/LastOutAtMs are final: LastOutAtMs is
        /// the latest lastStandAt Player Property (Decision 23) among the team's members currently out for the last
        /// stand, compared wrap-safe like every other server-clock stamp in this codebase - read only by
        /// MatchPhaseRules' no-draw rule.</summary>
        private static TeamStatus[] BuildTeamStatuses(BuildingManager buildings)
        {
            var statuses = new TeamStatus[TeamCount];
            TerritoryMap map = buildings.Map;
            TerritorySnapshot current = buildings.Current;

            for (int team = 0; team < TeamCount; team++)
            {
                int members = 0, outForLastStand = 0;
                int? lastOutAtMs = null;
                foreach (Player p in PhotonNetwork.PlayerList)
                {
                    if (!Teams.TryGetTeam(p, out int t) || t != team)
                        continue;
                    members++;

                    // Missing property means this player has never died with their capital lost -
                    // PlayerLifecycle only ever publishes it true on a last-stand death, false again
                    // the moment that player is back on their way into the match.
                    if (!(p.CustomProperties.TryGetValue(PlayerLifecycle.LastStandKey, out object raw) && raw is bool b && b))
                        continue;
                    outForLastStand++;

                    if (p.CustomProperties.TryGetValue(PlayerLifecycle.LastStandAtKey, out object stampRaw) && stampRaw is int stamp)
                        if (lastOutAtMs == null || unchecked(stamp - lastOutAtMs.Value) > 0)
                            lastOutAtMs = stamp;
                }

                int capital = map.CapitalOf(team);
                bool holdsOwnCapital = capital >= 0 && current.OwnerOf(capital) == team;
                bool holdsAnyCapitalInPlay = false;
                foreach (KeyValuePair<int, int> ownCapital in map.Capitals)
                    if (current.OwnerOf(ownCapital.Key) == team) { holdsAnyCapitalInPlay = true; break; }

                statuses[team] = new TeamStatus
                {
                    TeamId = team,
                    InMatch = true, // [interim, 2.7b step 3 - replaced in step 5 by mTeams]
                    Members = members,
                    MembersOutForLastStand = outForLastStand,
                    HoldsOwnCapital = holdsOwnCapital,
                    HoldsAnyCapitalInPlay = holdsAnyCapitalInPlay,
                    LastOutAtMs = lastOutAtMs,
                };
            }
            return statuses;
        }

        // ---------------------------------------------------------------- every client: react

        /// Reads the room's mPhase/mElim/mWin and applies whatever changed since this client last
        /// looked - the lost panel for a newly eliminated own team, the Tier-3 "two teams left" spawn-
        /// home + banner the first time this client itself sees the ThreeTeams -> TwoTeams edge (never
        /// for a client whose own team was eliminated in that same transition - it is already getting
        /// the lost panel and does not need sending home or told a rule "just" changed for a match it
        /// is no longer in), and the match-result panel once a winner exists. firstRead (OnJoinedRoom,
        /// including a late joiner) only ever applies the winner reaction - a joiner arriving mid-match
        /// should not be sent "home" or told the two-team rule "just" changed for a transition that
        /// already happened before they connected; MatchUI.ShowMatchResult alone already answers a
        /// late joiner reading Over and the winner, whether they were eliminated earlier or not.
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

            bool myTeamKnown = Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam);
            bool myTeamJustEliminated = myTeamKnown && !lastAppliedEliminated.Contains(myTeam) && eliminated.Contains(myTeam);

            if (!firstRead)
            {
                if (myTeamJustEliminated)
                    localView?.GetComponent<MatchUI>()?.ShowYouLost();

                if (lastAppliedPhase == MatchPhase.ThreeTeams && phase == MatchPhase.TwoTeams && !myTeamJustEliminated)
                {
                    PlayerLifecycle lifecycle = localView != null ? localView.GetComponent<PlayerLifecycle>() : null;
                    if (lifecycle != null && lifecycle.IsAlive)
                        lifecycle.ReturnToSpawnForPhaseChange();
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
