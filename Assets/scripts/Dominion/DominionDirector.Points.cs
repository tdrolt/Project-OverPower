using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Telemetry;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 3, the master's side of the points: once a second of server time every team earns for the zones it holds, a bounty is
    /// added when a zone is taken after a long hold, and in 3v3v3 the centre pays a lump at dCtr. All of the deciding is DominionPointsRules.Plan
    /// (pure); this file only gathers its inputs from the room and the territory and sends the one check-and-set it answers with.
    ///
    /// Reads the room every look, so a new master carries on from the room's own dPts / dCtr (it starts its own one-second beat, no burst).
    /// Builds each write on the last value it wrote while that write's echo is pending (DominionPointsLedger), and every write expects the
    /// stage, round and end time it was computed from, so a late one is refused once the stage has moved. Nothing accrues until the zones
    /// have been reset for this round (dRz = dEnd).
    ///
    /// The bounty is judged in BuildingManager.CaptureWritten (raised inside SetCaptured on the master): it is the only place that still sees
    /// the previous owner and the length of the hold, because the capture itself clears them from the snapshot and OwnershipChanged only
    /// carries the new one.
    /// </summary>
    public sealed partial class DominionDirector
    {
        // Not gameplay values: how long the master trusts its own last write before the room's copy (a refused check-and-set never echoes).
        private const float PointsEchoTimeoutSeconds = 1.5f;

        private readonly DominionPointsLedger pointsLedger = new DominionPointsLedger();
        private int lastTickMs; // 0 = the points beat is not running
        private readonly int[] pendingBounty = new int[DominionKeys.TeamSlots];
        private readonly List<string> pendingBountyNotes = new List<string>();
        private BuildingManager hookedBuildings;
        private int[] ownerScratch;
        private int[] tierScratch;
        private bool[] spawnScratch;
        private int centreZone = -1;

        private void ResetPointsState()
        {
            pointsLedger.Reset();
            lastTickMs = 0;
            ClearPendingBounty();
        }

        private void ClearPendingBounty()
        {
            System.Array.Clear(pendingBounty, 0, pendingBounty.Length);
            pendingBountyNotes.Clear();
        }

        private void UnhookBuildings()
        {
            if (hookedBuildings != null) hookedBuildings.CaptureWritten -= OnCaptureWritten;
            hookedBuildings = null;
        }

        /// <summary>The room with the points this master has written but not yet seen echoed, in place of the room's older copy.</summary>
        private DominionRoomState WithLatestPoints(DominionRoomState room)
        {
            if (room.Stage != DominionStage.Round) return room;
            pointsLedger.Basis(Time.unscaledTime, PointsEchoTimeoutSeconds, room.Points, room.CentreMs, out int[] points, out _);
            room.Points = points;
            return room;
        }

        /// <summary>An update from the room reached us: a new stage or round ends the ledger's story (the stage write carries its own points),
        /// otherwise it may be the echo of one of our points writes.</summary>
        private void NoteEchoForPoints(Hashtable changed)
        {
            if (changed.ContainsKey(DominionKeys.Stage) || changed.ContainsKey(DominionKeys.Round)) pointsLedger.Reset();
            else if (changed.ContainsKey(DominionKeys.Points) || changed.ContainsKey(DominionKeys.CentrePayout)) pointsLedger.Echoed();
        }

        /// <summary>True when this match has a centre that pays lumps: a three-team mode and a Tier 4 zone on the map.</summary>
        private bool CentreInPlay(out int zone)
        {
            zone = -1;
            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null || DominionMode.TeamsOfCurrentRoom().Length != 3) return false;
            if (centreZone >= 0 && buildings.BaseTierOf(centreZone) == DominionRules.CentreTier) { zone = centreZone; return true; }
            for (int z = 0; z < buildings.ZoneCount; z++)
                if (buildings.BaseTierOf(z) == DominionRules.CentreTier) { centreZone = z; zone = z; return true; }
            return false;
        }

        // ---------------------------------------------------------------- bounty

        private void OnCaptureWritten(int zone, int newOwner, int previousOwner, int previousHeldMs)
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || !DominionMode.IsActive()) return;
            if (DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties).Stage != DominionStage.Round) return;
            DominionConfig config = Config();
            if (config == null || newOwner < 0 || newOwner >= pendingBounty.Length) return;

            int points = DominionPointsRules.BountyPoints(previousHeldMs, Mathf.RoundToInt(config.BountyHoldSeconds * 1000f),
                                                          previousOwner, newOwner, config.BountyPoints);
            if (points <= 0) return;
            pendingBounty[newOwner] += points;
            pendingBountyNotes.Add(DominionMarkerNotes.Bounty(zone, newOwner, points));
            Debug.Log($"[DOMINION] bounty: zone {zone} taken by team {newOwner} from team {previousOwner} after {previousHeldMs} ms: +{points}");
        }

        // ---------------------------------------------------------------- the tick

        private void RunPoints()
        {
            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null) return;
            if (hookedBuildings != buildings)
            {
                UnhookBuildings();
                buildings.CaptureWritten += OnCaptureWritten;
                hookedBuildings = buildings;
            }

            DominionRoomState room = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            if (room.Stage != DominionStage.Round || room.ResetFor != room.EndMs)
            {
                lastTickMs = 0; // not a round, or the zones are not reset for it yet: no beat, no bounty carried over
                ClearPendingBounty();
                return;
            }

            DominionConfig config = Config();
            TerritorySnapshot snapshot = buildings.LatestForMaster;
            int now = PhotonNetwork.ServerTimestamp;
            if (config == null || snapshot == null || now == 0) return;

            int zones = snapshot.ZoneCount;
            if (ownerScratch == null || ownerScratch.Length != zones)
            {
                ownerScratch = new int[zones];
                tierScratch = new int[zones];
                spawnScratch = new bool[zones];
            }
            for (int z = 0; z < zones; z++)
            {
                ownerScratch[z] = snapshot.OwnerOf(z);
                tierScratch[z] = buildings.BaseTierOf(z);
                spawnScratch[z] = false;
            }
            foreach (KeyValuePair<int, int> capital in buildings.CathedralBuildingIDs) // a team's capital is its spawn
                if (capital.Key >= 0 && capital.Key < zones) spawnScratch[capital.Key] = true;

            MatchDirector match = MatchDirector.Instance;
            pointsLedger.Basis(Time.unscaledTime, PointsEchoTimeoutSeconds, room.Points, room.CentreMs, out int[] basePoints, out int baseCentre);
            bool hasCentre = CentreInPlay(out int centre);
            DominionTickPlan plan = DominionPointsRules.Plan(new DominionTickInput
            {
                NowMs = now, LastTickMs = lastTickMs, Room = room,
                BasePoints = basePoints, BaseCentreMs = baseCentre,
                TeamsInMatch = match != null ? match.TeamsInMatch : DominionMode.TeamsOfCurrentRoom(),
                ZoneOwner = ownerScratch, ZoneTier = tierScratch, IsSpawnZone = spawnScratch,
                PointsPerTier = config.PointsPerZonePerSecond,
                HasCentre = hasCentre, CentreOwner = hasCentre ? snapshot.OwnerOf(centre) : -1,
                CentrePoints = config.CentrePayoutPoints,
                CentreFirstMs = Mathf.RoundToInt(config.CentreFirstPayoutSeconds * 1000f),
                CentreIntervalMs = Mathf.RoundToInt(config.CentrePayoutIntervalSeconds * 1000f),
                PendingBounty = pendingBounty,
            });
            lastTickMs = plan.NewLastTickMs;
            if (plan.Write == null) return;

            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(plan.Write.Props, plan.Write.Expected))
            {
                Debug.LogWarning("[DOMINION] points write refused locally");
                return;
            }

            int[] sentPoints = plan.Write.Props.TryGetValue(DominionKeys.Points, out object p) ? (int[])p : basePoints;
            int sentCentre = plan.Write.Props.TryGetValue(DominionKeys.CentrePayout, out object c) ? (int)c : baseCentre;
            pointsLedger.Sent(sentPoints, sentCentre, Time.unscaledTime);
            Debug.Log($"[DOMINION] master wrote: points [{string.Join(",", sentPoints)}] centre {sentCentre} (round {room.Round})");

            MatchTelemetry telemetry = MatchTelemetry.Instance;
            foreach (string note in pendingBountyNotes) telemetry?.DropMarker(note);
            ClearPendingBounty();
            if (plan.CentrePaid) telemetry?.DropMarker(DominionMarkerNotes.CentrePayout(plan.CentreTeam, plan.CentrePoints));
        }
    }
}
