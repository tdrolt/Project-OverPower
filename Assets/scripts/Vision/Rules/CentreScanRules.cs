using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// The centre scan, as plain rules (Tudor 2026-09-30): while a team holds the centre, every 30 s a sonar pulse goes out
    /// 360 degrees at once, from the moment of capture and then once per interval. It is fast, so it cannot hit the same
    /// enemy several times by itself; Blink over it to stay hidden, or across it the other way to be caught twice. The scan is
    /// gone for good once the map shrinks. Everything is a function of the server clock, so every client agrees on where the
    /// wave is without anything being sent. Times are server-clock milliseconds as ints (like the territory's tSince).
    /// </summary>
    public static class CentreScanRules
    {
        /// <summary>The scan the centre is on right now. Scan k starts at heldSince + k * interval (k = 0 at capture). Returns
        /// false while the centre has no holder (neutral, team below 0) or once the map has shrunk (the centre then plays
        /// as a Tier III and there is no scan any more). Otherwise scanStartMs is the start of the latest scan; whether its
        /// wave is still travelling is WaveRadius + IsTravelling.</summary>
        public static bool ActiveScan(int holderTeam, int heldSinceMs, int nowMs, int intervalMs, bool cutActive, out int scanStartMs)
        {
            scanStartMs = heldSinceMs;
            if (holderTeam < 0 || cutActive || intervalMs <= 0) return false;

            // Same 32-bit trick as DeployableAge: subtract unchecked so the server clock wrapping (~49.7 days) cancels out.
            // A client reading a few ms before the stamp counts as the capture moment, never as a scan from the past.
            int elapsedMs = unchecked(nowMs - heldSinceMs);
            if (elapsedMs < 0) return true;
            int k = elapsedMs / intervalMs;
            scanStartMs = unchecked(heldSinceMs + k * intervalMs);
            return true;
        }

        /// <summary>How far the front is from the centre, in metres: time since the scan began times the wave speed. Before
        /// the scan starts (a frame of the previous scan's clock) it is 0, which is also the previous radius on a scan's first frame.</summary>
        public static float WaveRadius(int scanStartMs, int nowMs, float speed)
        {
            int elapsedMs = unchecked(nowMs - scanStartMs);
            return elapsedMs > 0 ? elapsedMs / 1000f * speed : 0f;
        }

        /// <summary>The wave is over once the front is past the farthest arena point from the centre (the caller computes
        /// maxRadius from the arena bounds); nothing is left to catch.</summary>
        public static bool IsTravelling(float radius, float maxRadius) => radius <= maxRadius;

        /// <summary>The band the front swept this frame, (prevRadius, radius], contains where the player is NOW (A1). So
        /// standing still is caught once, a Blink from outside the front to inside it is missed, and a Blink from inside to
        /// ahead of the front is caught again when the front reaches it. Pass prevRadius = WaveRadius at the previous frame's
        /// time (0 on a scan's first frame).</summary>
        public static bool FrontSwept(float prevRadius, float radius, float distance) =>
            prevRadius < distance && distance <= radius;

        /// <summary>The zones whose centre the front swept this frame, among the ticked tiers (VisionConfig Scan Tier 1..4);
        /// each zone uses its own tier (the centre counts as Tier IV, which is fine because there is no scan after the cut).
        /// zonePositions and zoneTiers are indexed by zone id; a tier outside 1..4 is never refreshed. The result list is
        /// cleared and filled so the caller can reuse one.</summary>
        public static void ZonesToRefresh(float prevRadius, float radius, Vector2 centre, IReadOnlyList<Vector2> zonePositions,
                                          IReadOnlyList<int> zoneTiers, bool tier1, bool tier2, bool tier3, bool tier4, List<int> result)
        {
            result.Clear();
            int count = Mathf.Min(zonePositions.Count, zoneTiers.Count);
            for (int zone = 0; zone < count; zone++)
            {
                bool ticked;
                switch (zoneTiers[zone])
                {
                    case 1: ticked = tier1; break;
                    case 2: ticked = tier2; break;
                    case 3: ticked = tier3; break;
                    case 4: ticked = tier4; break;
                    default: ticked = false; break;
                }
                if (ticked && FrontSwept(prevRadius, radius, Vector2.Distance(centre, zonePositions[zone])))
                    result.Add(zone);
            }
        }
    }
}
