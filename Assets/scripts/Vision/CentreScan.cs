using System;
using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// The centre scan in the game (Tudor 2026-10-01, Vision Tasks 11 and 16). A red wave rolls out from the centre on a
    /// fixed clock, every Scan Interval Seconds, whoever holds the centre (the first one an interval after the match goes
    /// live; in the warm-up on whole multiples of the interval on the server clock); everyone sees it on the ground and
    /// everyone sees the countdown to the next one above the tower. The team that holds the centre at the moment a wave
    /// STARTS gets a red dot where each enemy was when the front passed over them, and a refresh of the zones the front
    /// passes, for that whole wave (a capture mid-wave changes nothing until the next wave). A neutral centre at the start
    /// means nobody learns anything from that wave. Once the map has shrunk there is no wave and no countdown.
    ///
    /// WHERE IT LIVES: on the BuildingManager's GameObject (added at runtime, next to ZoneKnowledge and MatchDirector), not on
    /// the player prefab: the wave and the dots must survive a respawn and exist for a spectator, and the centre's owner is
    /// read from the territory this object already hosts. Nothing is sent: the wave is a function of the server clock and
    /// the room's go-live time mLiveAt (CentreScanRules), so every client agrees on where it is. Each client remembers the
    /// owner it saw when the wave started (ScanBandTracker); a client that joins mid-wave takes the owner it reads then.
    ///
    /// CATCHES ARE JUDGED ON THE HOLDER'S GAME against the enemy's remote copy (the interpolated position this client draws):
    /// a Blink of 3 m or more snaps there, so the enemy cannot be caught part-way; a shorter Blink glides for about 0.2 s and
    /// may be caught on the way. Two holders can therefore see slightly different dots (lag). Not fixed.
    ///
    /// Runs before ZoneKnowledge (order -110 against its -100) so the zones the front passed are known to it the same frame.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public sealed class CentreScan : MonoBehaviour
    {
        public static CentreScan Instance { get; private set; }

        /// <summary>Highest tier in the game: the centre. The centre zone is found as the one tower of this tier.</summary>
        private const int CentreTier = 4;

        private readonly ScanBandTracker tracker = new ScanBandTracker();
        private readonly ScanDotPool dotPool = new ScanDotPool();
        private readonly List<int> refreshed = new List<int>();
        private readonly List<Vector2> zonePositions = new List<Vector2>();
        private readonly List<int> zoneTiers = new List<int>();

        private int dotsTeam = -1; // the team the dots in the pool belong to (a spectator can switch the watched team)
        private int centreZone = -1;
        private Vector3 centrePosition;
        private float centreTopY;
        private float maxRadius;
        private VisionConfig config;

        /// <summary>This frame's scan: Active (a wave is on the clock and the map is not cut), Travelling (the front is still
        /// inside the arena), the team the wave belongs to, and the band it swept.</summary>
        public ScanFrame Frame { get; private set; }

        /// <summary>The team this wave belongs to (the owner of the centre when it started), or -1 (no wave, or neutral then).</summary>
        public int HolderTeam { get; private set; } = -1;

        /// <summary>Whole seconds until the next wave, for the label above the tower; -1 when there is no countdown (no
        /// centre read yet, not in a room, fog off, or the map has shrunk). It keeps counting while a wave travels.</summary>
        public int CountdownSeconds { get; private set; } = -1;

        /// <summary>Where the countdown label floats: the centre tower's top plus the configured height.</summary>
        public Vector3 CountdownPosition => new Vector3(centrePosition.x, centreTopY + (config != null ? config.ScanCountdownHeight : 0f), centrePosition.z);

        /// <summary>The centre tower's position (the wave's centre).</summary>
        public Vector3 CentrePosition => centrePosition;

        /// <summary>The farthest the wave goes, from the arena outline.</summary>
        public float MaxRadius => maxRadius;

        /// <summary>The vision numbers the scan reads (null before the owner's player exists).</summary>
        public VisionConfig Config => config;

        /// <summary>The wave is on the ground now: a wave is running and its front is inside the arena. Everyone sees it.</summary>
        public bool WaveVisible => Frame.Active && Frame.Travelling;

        /// <summary>The current wave belongs to my team (the watched team while spectating): the maps show the ring and the dots.</summary>
        public bool SeenByMyTeam { get; private set; }

        /// <summary>The red dots of the enemies the front passed over, newest last.</summary>
        public ScanDotPool Dots => dotPool;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            if (GetComponent<CentreScanWaveView>() == null)
                gameObject.AddComponent<CentreScanWaveView>();
            if (GetComponent<CentreScanCountdownView>() == null)
                gameObject.AddComponent<CentreScanCountdownView>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            BuildingManager buildings = BuildingManager.Instance;
            TeamSight sight = TeamSight.Local;
            config = sight != null ? sight.Config : null;

            // Not in a room, or the territory is not read: nothing is judged, and nothing is remembered as "last frame".
            if (buildings == null || buildings.Current == null || !PhotonNetwork.InRoom || sight == null || config == null
                || !config.FogEnabled)
            {
                Stop();
                return;
            }
            // The server clock reads 0 for a moment after connecting (trap 18): skip the frame, remember nothing from it.
            int nowMs = PhotonNetwork.ServerTimestamp;
            if (nowMs == 0)
                return;

            if (!FindCentre(buildings))
            {
                Stop();
                return;
            }

            TerritorySnapshot territory = buildings.Current;
            int owner = territory.OwnerOf(centreZone);
            MatchDirector director = MatchDirector.Instance;
            bool cut = director != null && director.CutTeam >= 0;
            // Live and with a go-live time: waves an interval apart from go-live. Otherwise (warm-up) on server-clock multiples.
            int? liveAtMs = director != null && director.IsLive && director.LiveAtMs != 0 ? director.LiveAtMs : (int?)null;
            int intervalMs = Mathf.RoundToInt(config.ScanIntervalSeconds * 1000f);
            Frame = tracker.Step(liveAtMs, owner, nowMs, intervalMs, cut, config.ScanWaveSpeed, maxRadius);
            HolderTeam = Frame.Active ? Frame.HolderTeam : -1;
            CountdownSeconds = cut || intervalMs <= 0
                ? -1
                : CentreScanRules.CountdownSecondsShown(unchecked(CentreScanRules.NextScanStart(liveAtMs, nowMs, intervalMs) - nowMs));

            int friendly = sight.FriendlyTeamId;
            SeenByMyTeam = Frame.Active && CentreScanDisplayRules.SeesScan(friendly, Frame.HolderTeam);
            if (dotPool.Dots.Count > 0 && dotsTeam != friendly)
                dotPool.Clear();
            if (SeenByMyTeam)
                dotsTeam = friendly;
            // The dots of an earlier wave finish their time and fade as normal; only the wave, the ring and the zone work stop.
            dotPool.Prune(Time.time, config.ScanDotSeconds);
            if (!SeenByMyTeam)
            {
                SetScanned(refreshed, false);
                return;
            }

            // Once the front is past the arena's farthest point there is nobody left to catch and no zone left to refresh.
            if (Frame.Radius > Frame.PrevRadius && Frame.PrevRadius <= maxRadius)
            {
                CatchEnemies(friendly, nowMs);
                RefreshZones(buildings, territory);
            }
            else
            {
                SetScanned(refreshed, false);
            }
        }

        // Every living enemy the front swept this frame, wherever they are now (A1), leaves a dot at that spot.
        private void CatchEnemies(int friendlyTeam, int nowMs)
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return;
            Vector2 centre = new Vector2(centrePosition.x, centrePosition.z);
            foreach (KeyValuePair<int, Photon.Realtime.Player> pair in room.Players)
            {
                Photon.Realtime.Player player = pair.Value;
                if (player.IsLocal || !Teams.TryGetTeam(player, out int team) || team < 0 || team == friendlyTeam)
                    continue;
                bool? flag = player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool isAlive ? isAlive : (bool?)null;
                if (!PresenceRules.CountsAsAlive(player.IsInactive, flag))
                    continue;
                PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                if (view == null)
                    continue;
                Vector3 position = view.transform.position;
                float distance = Vector2.Distance(centre, new Vector2(position.x, position.z));
                if (CentreScanRules.FrontSwept(Frame.PrevRadius, Frame.Radius, distance))
                    dotPool.Add(position, Time.time, nowMs, player.ActorNumber);
            }
        }

        // The ticked tiers' zones the front passed this frame take the live state in ZoneKnowledge (only visible with the zone
        // switch off).
        private void RefreshZones(BuildingManager buildings, TerritorySnapshot territory)
        {
            zonePositions.Clear();
            zoneTiers.Clear();
            for (int zone = 0; zone < territory.ZoneCount; zone++)
            {
                bool known = buildings.TryGetZoneCentre(zone, out Vector3 position);
                zonePositions.Add(new Vector2(position.x, position.z));
                zoneTiers.Add(known ? buildings.BaseTierOf(zone) : 0);
            }
            CentreScanRules.ZonesToRefresh(Frame.PrevRadius, Frame.Radius, new Vector2(centrePosition.x, centrePosition.z),
                zonePositions, zoneTiers, config.ScanTier1, config.ScanTier2, config.ScanTier3, config.ScanTier4, refreshed);
            SetScanned(refreshed, true);
        }

        private void SetScanned(IReadOnlyCollection<int> zones, bool any)
        {
            ZoneKnowledge knowledge = ZoneKnowledge.Instance;
            if (knowledge != null)
                knowledge.ScannedNow = any ? zones : Array.Empty<int>();
        }

        // The centre is the one tower of the highest tier; the wave must travel to the arena's farthest outline point from it.
        private bool FindCentre(BuildingManager buildings)
        {
            if (centreZone >= 0 && buildings.TryGetZoneCentre(centreZone, out centrePosition) && maxRadius > 0f)
                return true;

            centreZone = -1;
            TerritorySnapshot territory = buildings.Current;
            for (int zone = 0; zone < territory.ZoneCount; zone++)
            {
                if (buildings.BaseTierOf(zone) == CentreTier && buildings.TryGetZoneCentre(zone, out centrePosition))
                {
                    centreZone = zone;
                    break;
                }
            }
            if (centreZone < 0)
                return false;
            centreTopY = buildings.TryGetZoneTowerTopY(centreZone, out float topY) ? topY : centrePosition.y;

            ArenaBounds bounds = ArenaSymmetry.Active != null ? ArenaSymmetry.Active.FullBounds : null;
            maxRadius = bounds != null
                ? CentreScanDisplayRules.MaxRadius(bounds.Polygon, new Vector2(centrePosition.x, centrePosition.z))
                : 0f;
            return maxRadius > 0f;
        }

        // Not in a room, or the scan is off: the wave, countdown, dots and zone refresh all stop, and the next wave starts clean.
        private void Stop()
        {
            tracker.Reset();
            Frame = default;
            HolderTeam = -1;
            SeenByMyTeam = false;
            CountdownSeconds = -1;
            dotPool.Clear();
            SetScanned(refreshed, false);
        }
    }
}
