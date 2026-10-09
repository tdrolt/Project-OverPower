using System;
using System.Collections.Generic;
using Overpower.Match;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>What a team knows about one zone, as plain values: who owns it, the capture ring as it was drawn (phase, fill,
    /// the capturing or draining team, under attack) and when it was captured. The ring is already evaluated at the moment
    /// the caller built the live list, not the stamped rate the room properties carry: a stamped rate keeps advancing on
    /// its own clock, so a copy kept for an unseen zone would keep filling, while an evaluated ring stays exactly what the
    /// team last saw. Both the ring on the ground and the minimap are drawn from this one CaptureRingState.</summary>
    public readonly struct ZoneView
    {
        public readonly int OwnerTeam;
        public readonly CaptureRingState Ring;
        public readonly int HeldSinceMs;

        public ZoneView(int ownerTeam, CaptureRingState ring, int heldSinceMs)
        {
            OwnerTeam = ownerTeam;
            Ring = ring;
            HeldSinceMs = heldSinceMs;
        }

        public bool UnderAttack => Ring.UnderAttack;

        public static readonly ZoneView Neutral = new ZoneView(-1,
            new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral, TerritoryMap.Neutral, false), 0);
    }

    /// <summary>The plain pieces the component feeds ZoneKnowledgeStore with.</summary>
    public static class ZoneViews
    {
        /// <summary>Rebuilds <paramref name="into"/> (cleared first) with the true state of every zone in the snapshot, by zone
        /// id, the ring evaluated at <paramref name="nowMs"/>. Out-of-play zones are not special here: the displays hide those
        /// before they ever read a view.</summary>
        public static void BuildLive(TerritorySnapshot snapshot, Func<int, CaptureProgress> progressOf, Func<int, bool> underAttack,
                                     int nowMs, List<ZoneView> into)
        {
            into.Clear();
            for (int zone = 0; zone < snapshot.ZoneCount; zone++)
            {
                int owner = snapshot.OwnerOf(zone);
                CaptureRingState ring = CaptureRingState.From(progressOf(zone), owner, underAttack(zone), nowMs);
                into.Add(new ZoneView(owner, ring, snapshot.HeldSinceMs(zone)));
            }
        }

        /// <summary>Sight lines to a tower's centre always stop at the tower's own solid capsule, so a tower counts as in sight
        /// when a spot on the ground just outside that capsule is. These are those spots: TowerSightMargin beyond the capsule's
        /// world radius (its radius times the larger of the horizontal scales).</summary>
        public const float TowerSightMargin = 0.5f;
        public const int TowerSightPointCount = 8;

        public static float TowerSightRadiusFor(float capsuleRadius, Vector3 lossyScale) =>
            capsuleRadius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z)) + TowerSightMargin;

        public static Vector3 TowerSightPoint(Vector3 towerCentre, int index, float radius)
        {
            float angle = index * (2f * Mathf.PI / TowerSightPointCount);
            return new Vector3(towerCentre.x + Mathf.Cos(angle) * radius, towerCentre.y,
                               towerCentre.z + Mathf.Sin(angle) * radius);
        }

        /// <summary>A tower is seen when it is in my team's sight. With no sight object at all there is no fog, so everything
        /// is seen.</summary>
        public static bool IsSeen(bool hasSight, bool towerInSight) => !hasSight || towerInSight;
    }

    /// <summary>
    /// Zones in the fog. With the switch off, what my team knows about a zone updates only while my team sees it, owns it,
    /// or the centre scan is passing over it; otherwise it stays on what the team last knew.
    /// Starting state: the first Update copies the live state for every zone, switch or no switch - whatever is true when the
    /// local game first reads it (capitals owned and the rest neutral at a normal go-live, anything for a late joiner). No
    /// separate "start" data is kept. Index in the lists is the zone id.
    /// </summary>
    public sealed class ZoneKnowledgeStore
    {
        private ZoneView[] known = new ZoneView[0];
        private bool started;

        /// <summary>The next Update copies the live state again. Called when the match goes live (the live reset throws the
        /// warm-up captures away and fixes which capitals are in play) and when leaving a room.</summary>
        public void Reset() => started = false;

        /// <remarks>Do not call this until the room's territory snapshot has been read and the server clock has synced: the first
        /// call copies whatever it is given as the team's starting knowledge.</remarks>
        /// <param name="live">The true state of every zone this frame, by zone id, with progress already evaluated at now.</param>
        /// <param name="scannedNow">Zones the centre scan wave is passing over right now. Each frame in it the zone takes the
        /// live state, so it ends on the state at the moment the wave leaves, and then freezes again.</param>
        public void Update(bool switchOn, int myTeam, IReadOnlyList<ZoneView> live, Func<int, bool> zoneSeen, IReadOnlyCollection<int> scannedNow)
        {
            if (!started || known.Length != live.Count)
            {
                // First read (or the zone count changed): the team starts out knowing the live state.
                known = new ZoneView[live.Count];
                for (int i = 0; i < known.Length; i++)
                    known[i] = live[i];
                started = true;
                return;
            }

            for (int i = 0; i < known.Length; i++)
            {
                // A zone my team owns is live (the team sees it attacked). A zone my team knew as its own and
                // has now lost is live for that frame too: the team's income stops, so it knows. The frame after, the loss
                // is what it last knew and the zone freezes on that like any other unseen zone.
                // A zone my team is capturing or draining is live too: a teammate far from the tower still sees their own progress.
                bool mine = myTeam >= 0 && (live[i].OwnerTeam == myTeam || known[i].OwnerTeam == myTeam
                    || live[i].Ring.ArcTeam == myTeam || live[i].Ring.DrainerTeam == myTeam);
                if (switchOn || mine || zoneSeen(i) || Contains(scannedNow, i))
                    known[i] = live[i];
            }
        }

        /// <summary>What my team knows about the zone right now. Neutral for an unknown zone id.</summary>
        public ZoneView Displayed(int zoneId) => zoneId >= 0 && zoneId < known.Length ? known[zoneId] : ZoneView.Neutral;

        private static bool Contains(IReadOnlyCollection<int> set, int zone)
        {
            if (set == null)
                return false;
            foreach (int z in set)
                if (z == zone)
                    return true;
            return false;
        }
    }
}
