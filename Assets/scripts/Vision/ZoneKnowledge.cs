using System;
using System.Collections.Generic;
using Overpower.Data;
using Overpower.Match;
using UnityEngine;
using Photon.Pun;

namespace Overpower.Vision
{
    /// <summary>
    /// What this client's team knows about every zone (Tudor 2026-09-30, Vision Task 9b). Runs on the BuildingManager's
    /// GameObject (added at runtime, like MatchDirector): the zone displays - the ring on the ground, the tower's owner
    /// colour, the carpet and the minimap - exist from the scene start, before and after any one player object, and a
    /// respawn must not make the team forget or relearn; and it lives wherever the territory Room Properties are read.
    ///
    /// Each frame it builds the live ZoneViews from the territory snapshot and the capture progress that BuildingManager
    /// already decoded (no second parser), hands them to ZoneKnowledgeStore with "is the tower in my team's sight", and
    /// answers Displayed. With VisionConfig › Zone Owners Visible Without Sight on (the default), TryGetDisplayed says
    /// "not filtering" and every display keeps drawing the live state exactly as before. Only the displays read this:
    /// gold, income, capture rules, respawns, the scoreboard and the result screen always use the live state.
    /// Runs before the displays (BuildingCapture.Update, MinimapView.LateUpdate).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ZoneKnowledge : MonoBehaviour
    {
        public static ZoneKnowledge Instance { get; private set; }

        private readonly ZoneKnowledgeStore store = new ZoneKnowledgeStore();
        private readonly List<ZoneView> live = new List<ZoneView>();
        private int[] lastShownOwner = new int[0];
        private bool filtering;
        private int version;

        // The centre scan (Task 11) will hand in the zones its wave is passing over; empty until then.
        private IReadOnlyCollection<int> scannedNow = Array.Empty<int>();

        private Func<int, CaptureProgress> progressOf;
        private Func<int, bool> underAttack;
        private Func<int, bool> towerSeen;
        private TeamSight sight;
        private BuildingManager buildings;

        /// <summary>The zones the centre scan wave is passing over right now (Task 11 sets this each frame).</summary>
        public IReadOnlyCollection<int> ScannedNow
        {
            get => scannedNow;
            set => scannedNow = value ?? Array.Empty<int>();
        }

        /// <summary>Changes whenever a display that is only repainted on change should repaint: the filter turned on or
        /// off, or a zone's displayed owner changed while filtering.</summary>
        public int Version => version;

        /// <summary>True while the displays must draw the known state instead of the live one: the switch is off and the
        /// knowledge has been started.</summary>
        public static bool IsFiltering => Instance != null && Instance.filtering;

        /// <summary>The known state of a zone, or false when the displays should draw the live state (the switch is on,
        /// or the knowledge has not started yet).</summary>
        public static bool TryGetDisplayed(int zone, out ZoneView view)
        {
            ZoneKnowledge k = Instance;
            if (k == null || !k.filtering)
            {
                view = default;
                return false;
            }
            view = k.store.Displayed(zone);
            return true;
        }

        /// <summary>The match went live (the territory was reset) or the room was left: the next frame starts the team's
        /// knowledge from the live state again.</summary>
        public static void ResetKnowledge() => Instance?.store.Reset();

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            progressOf = id => buildings.CaptureProgressOf(id);
            underAttack = id => ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(id);
            towerSeen = id => buildings.TryGetZoneCentre(id, out Vector3 centre)
                && ZoneViews.IsSeen(sight != null, sight != null && TowerInSight(centre));
        }

        // The tower is solid, so a line to its centre stops on its own surface: it is in sight when a spot just outside it is.
        private bool TowerInSight(Vector3 centre)
        {
            for (int i = 0; i < ZoneViews.TowerSightPointCount; i++)
                if (sight.CanSee(ZoneViews.TowerSightPoint(centre, i)))
                    return true;
            return false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            buildings = BuildingManager.Instance;
            // Not before the room's territory is read and the server clock has synced (the first Update copies live).
            if (buildings == null || buildings.Current == null || !PhotonNetwork.InRoom || PhotonNetwork.ServerTimestamp == 0)
            {
                SetFiltering(false);
                return;
            }

            sight = TeamSight.Local;
            VisionConfig config = sight != null ? sight.Config : null;
            bool switchOn = config == null || config.ZoneOwnersVisibleWithoutSight;
            int myTeam = sight != null ? sight.FriendlyTeamId : -1;

            ZoneViews.BuildLive(buildings.Current, progressOf, underAttack, PhotonNetwork.ServerTimestamp, live);
            store.Update(switchOn, myTeam, live, towerSeen, scannedNow);

            SetFiltering(!switchOn);
            if (!filtering)
                return;

            if (lastShownOwner.Length != live.Count)
            {
                lastShownOwner = new int[live.Count];
                for (int i = 0; i < lastShownOwner.Length; i++)
                    lastShownOwner[i] = int.MinValue;
            }
            for (int i = 0; i < lastShownOwner.Length; i++)
            {
                int owner = store.Displayed(i).OwnerTeam;
                if (owner != lastShownOwner[i])
                {
                    lastShownOwner[i] = owner;
                    version++;
                }
            }
        }

        private void SetFiltering(bool value)
        {
            if (filtering == value)
                return;
            filtering = value;
            version++;
            for (int i = 0; i < lastShownOwner.Length; i++)
                lastShownOwner[i] = int.MinValue;
        }
    }
}
