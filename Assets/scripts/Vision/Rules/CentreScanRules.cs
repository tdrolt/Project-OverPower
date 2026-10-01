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
        /// wave is still travelling is WaveRadius + IsTravelling. A new scan cuts off the previous wave, so an interval
        /// shorter than the wave's travel time (about 3 s at 40 m/s) means the outer arena is never scanned. Call it every
        /// frame through ScanBandTracker, which also keeps the band right when a scan changes.</summary>
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
        /// ahead of the front is caught again when the front reaches it. Pass prevRadius = WaveRadius(start, previous frame's
        /// time) when the previous frame was on the same scan start, and 0 when the scan start differs from last frame's (a new
        /// scan, a new holder, or the first frame a late stamp is seen on) - otherwise the previous scan's far radius would be
        /// used, or the enemies already in the centre would never be caught. ScanBandTracker does exactly this.</summary>
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

    /// <summary>One frame of the scan as the caller sees it. Holding: a team holds the centre and the map is not cut.
    /// Travelling: the front is still inside the arena. PrevRadius..Radius is the band the front swept this frame (empty,
    /// PrevRadius == Radius, on a frame that must not catch anyone).</summary>
    public readonly struct ScanFrame
    {
        public readonly bool Holding;
        public readonly bool Travelling;
        public readonly int ScanStartMs;
        public readonly float PrevRadius;
        public readonly float Radius;

        public ScanFrame(bool holding, bool travelling, int scanStartMs, float prevRadius, float radius)
        {
            Holding = holding; Travelling = travelling; ScanStartMs = scanStartMs; PrevRadius = prevRadius; Radius = radius;
        }
    }

    /// <summary>Keeps what a client must remember between frames to call CentreScanRules correctly (Task 10 review).</summary>
    public sealed class ScanBandTracker
    {
        /// <summary>The time grace applies only to the truly first frame after Reset() (a join or rejoin): a client that first
        /// sees a scan up to this long after it began still catches the enemies already inside the wave; later than that it is
        /// someone joining mid-wave, who must not get a dot for everyone behind the front. A client that saw no scan the frame
        /// before (neutral, cut, no holder) is not joining: a new scan starts from 0 however late its stamp arrives.</summary>
        public const int LateStampGraceMs = 300;

        private bool has;
        private bool sawNoScan; // the last frame stepped in this room had no scan, so the next scan is new, not joined
        private int lastStart;
        private int prevNow;

        /// <summary>Forget the scan seen so far (the room was left, or the scan is switched off): the next frame is a first frame.</summary>
        public void Reset() { has = false; sawNoScan = false; }

        /// <summary>Call once per frame, every frame, with the same inputs. Returns Holding = false (and forgets the scan) while
        /// no team holds the centre or the map is cut. A frame with the server clock at 0, or one that does not move the clock
        /// forward (Photon's clock can step back a few ms), gets an empty band and is not remembered, so no slice of the wave
        /// is ever covered twice. A new scan, a new holder, or the first frame ever seen starts the band from 0.</summary>
        public ScanFrame Step(int holderTeam, int heldSinceMs, int nowMs, int intervalMs, bool cutActive, float speed, float maxRadius)
        {
            if (nowMs == 0)
                return new ScanFrame(false, false, 0, 0f, 0f);

            if (!CentreScanRules.ActiveScan(holderTeam, heldSinceMs, nowMs, intervalMs, cutActive, out int start))
            {
                has = false;
                sawNoScan = true;
                return new ScanFrame(false, false, 0, 0f, 0f);
            }

            float radius = CentreScanRules.WaveRadius(start, nowMs, speed);
            bool travelling = CentreScanRules.IsTravelling(radius, maxRadius);

            float prevRadius;
            if (has && start == lastStart)
            {
                if (unchecked(nowMs - prevNow) <= 0)
                    return new ScanFrame(true, travelling, start, radius, radius);
                prevRadius = CentreScanRules.WaveRadius(start, prevNow, speed);
            }
            else if (has || sawNoScan || unchecked(nowMs - start) <= LateStampGraceMs)
            {
                prevRadius = 0f;
            }
            else
            {
                prevRadius = radius;
            }

            has = true;
            sawNoScan = false;
            lastStart = start;
            prevNow = nowMs;
            return new ScanFrame(true, travelling, start, prevRadius, radius);
        }
    }
}
