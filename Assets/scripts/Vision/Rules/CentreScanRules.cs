using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// The centre scan, as plain rules (Tudor 2026-09-30, rule changed 2026-10-01): the centre sends a sonar wave 360 degrees
    /// at once on a fixed clock, every interval, whether or not anyone holds it. The team that holds the centre at the moment
    /// a wave starts gets the dots and the zone refresh from that wave; neutral at the start means nobody does. The wave is
    /// fast, so it cannot hit the same enemy several times by itself; Blink over it to stay hidden, or across it the other way
    /// to be caught twice. The scan is gone for good once the map shrinks. Everything is a function of the server clock (and
    /// the room's go-live time), so every client agrees on where the wave is without anything being sent. Times are
    /// server-clock milliseconds as ints.
    /// </summary>
    public static class CentreScanRules
    {
        /// <summary>The start of the latest wave on the fixed clock, or null while there is none yet. Live (liveAtMs = the
        /// server ms the match went live): waves at liveAtMs + k * interval for k of 1 or more, so none until one interval
        /// after go-live. Warm-up (liveAtMs null: no mLiveAt yet, or the match not live): waves at whole multiples of the
        /// interval on the server clock itself, read as unsigned so every client agrees even once the 32-bit clock reads
        /// negative. Null too for an interval of 0 or less. Who the wave belongs to is a separate question (ScanBandTracker).</summary>
        public static int? ScanStart(int? liveAtMs, int nowMs, int intervalMs)
        {
            if (intervalMs <= 0)
                return null;
            if (liveAtMs.HasValue)
            {
                // Same 32-bit trick as DeployableAge: subtract unchecked so the server clock wrapping (~49.7 days) cancels out.
                int elapsedMs = unchecked(nowMs - liveAtMs.Value);
                if (elapsedMs < intervalMs)
                    return null; // before go-live, or before the first wave
                return unchecked(liveAtMs.Value + elapsedMs / intervalMs * intervalMs);
            }
            uint now = unchecked((uint)nowMs);
            return unchecked((int)(now - now % (uint)intervalMs));
        }

        /// <summary>The go-live time the schedule runs on, from the room's mLiveAt alone (0 = not announced yet): from the moment
        /// it is announced, the countdown included, waves are at liveAt + k x interval, so there is no wave during the countdown
        /// and the label counts straight to the first live wave. Null (the warm-up clock) while it is 0.</summary>
        public static int? LiveSchedule(int liveAtMs) => liveAtMs != 0 ? liveAtMs : (int?)null;

        /// <summary>The start of the next wave after nowMs (what the countdown above the tower counts to). Live: one interval
        /// after the latest wave, or after go-live while none has gone out yet. Warm-up: the next multiple of the interval.
        /// With an interval of 0 or less it is nowMs.</summary>
        public static int NextScanStart(int? liveAtMs, int nowMs, int intervalMs)
        {
            if (intervalMs <= 0)
                return nowMs;
            int? latest = ScanStart(liveAtMs, nowMs, intervalMs);
            if (latest.HasValue)
                return unchecked(latest.Value + intervalMs);
            // Live and before the first wave: that wave is one interval after go-live (liveAtMs is set whenever latest is null here).
            return unchecked(liveAtMs.Value + intervalMs);
        }

        /// <summary>The whole seconds the countdown shows for the milliseconds left until the next wave: rounded up, so it
        /// reads the full interval the moment a wave starts and 1 for the last second; never below 0.</summary>
        public static int CountdownSecondsShown(int msUntilNext)
        {
            return msUntilNext <= 0 ? 0 : (msUntilNext + 999) / 1000;
        }

        /// <summary>The wave the centre is on right now, whoever holds the centre (Tudor 2026-10-01: the zone sends a wave on
        /// a fixed clock, captured or not). False before the first wave, with an interval of 0 or less, or once the map has
        /// shrunk (the centre then plays as a Tier III and there is no scan any more). Otherwise scanStartMs is the start of
        /// the latest wave; whether it is still travelling is WaveRadius + IsTravelling. A new wave cuts off the previous one,
        /// so an interval shorter than the wave's travel time (about 3 s at 40 m/s) means the outer arena is never scanned.
        /// Call it every frame through ScanBandTracker, which also keeps the band and the wave's holder right.</summary>
        public static bool ActiveScan(int? liveAtMs, int nowMs, int intervalMs, bool cutActive, out int scanStartMs)
        {
            scanStartMs = 0;
            if (cutActive)
                return false;
            int? start = ScanStart(liveAtMs, nowMs, intervalMs);
            if (!start.HasValue)
                return false;
            scanStartMs = start.Value;
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

    /// <summary>One frame of the scan as the caller sees it. Active: a wave is on the clock and the map is not cut.
    /// Travelling: the front is still inside the arena. HolderTeam: the team the wave belongs to (the owner of the centre
    /// when the wave started), or -1 when it was neutral then (nobody learns anything from that wave). PrevRadius..Radius
    /// is the band the front swept this frame (empty, PrevRadius == Radius, on a frame that must not catch anyone).</summary>
    public readonly struct ScanFrame
    {
        public readonly bool Active;
        public readonly bool Travelling;
        public readonly int ScanStartMs;
        public readonly int HolderTeam;
        public readonly float PrevRadius;
        public readonly float Radius;

        public ScanFrame(bool active, bool travelling, int scanStartMs, int holderTeam, float prevRadius, float radius)
        {
            Active = active; Travelling = travelling; ScanStartMs = scanStartMs; HolderTeam = holderTeam; PrevRadius = prevRadius; Radius = radius;
        }
    }

    /// <summary>Keeps what a client must remember between frames to call CentreScanRules correctly (Task 10 review), and who
    /// owns each wave (Task 16).</summary>
    public sealed class ScanBandTracker
    {
        /// <summary>The time grace applies only to the truly first frame after Reset() (a join or rejoin): a client that first
        /// sees a wave up to this long after it began still catches the enemies already inside it; later than that it is
        /// someone joining mid-wave, who must not get a dot for everyone behind the front. A client that saw no wave the frame
        /// before (before the first one, cut) is not joining: a new wave starts from 0 however late the frame comes.</summary>
        public const int LateStampGraceMs = 300;

        private bool has;
        private bool sawNoScan; // the last frame stepped in this room had no wave, so the next one is new, not joined
        private int lastStart;
        private int lastHolder = -1;
        private int prevNow;
        private bool hasPrevOwner; // an owner was read on an earlier frame of this room
        private int prevOwner;     // ... and this is it (the owner before whatever this frame shows)

        /// <summary>Forget the wave seen so far (the room was left, or the scan is switched off): the next frame is a first frame.</summary>
        public void Reset() { has = false; sawNoScan = false; lastHolder = -1; hasPrevOwner = false; }

        /// <summary>Call once per frame, every frame, with the same inputs. liveAtMs is the server ms the match went live, or
        /// null in the warm-up (see CentreScanRules.ScanStart). ownerNow is who owns the centre right now (-1 neutral) and
        /// heldSinceMs the server ms that owner took it (the snapshot stamps every owner change, going neutral included).
        /// The wave belongs to whoever held the centre at its start, and every client must agree on that however its capture
        /// news arrives: on every frame of a wave, an owner who held it at the start (heldSince at or before the start)
        /// is the holder, so a capture stamped just before the start but read just after it is corrected (the band then
        /// restarts from 0 for that client within LateStampGraceMs, so the first metres of the wave are not lost, and otherwise sweeps on from the previous frame's radius); a capture stamped after the start
        /// changes nothing until the next wave (the remembered holder stays). A client whose first frame of a wave already
        /// shows a later capture, with no earlier owner read to fall back on (a joiner), gives the wave to nobody (-1) rather
        /// than the wrong team. Leave heldSinceMs null for no stamp: the owner read on a wave's first frame then stands for the whole wave (no correction). Returns Active = false (and
        /// forgets the wave) while there is no wave or the map is cut. A frame with the server clock at 0, or one that does
        /// not move the clock forward (Photon's clock can step back a few ms), gets an empty band and is not remembered, so no
        /// slice of the wave is ever covered twice. A new wave, or the first frame ever seen, starts the band from 0.</summary>
        public ScanFrame Step(int? liveAtMs, int ownerNow, int nowMs, int intervalMs, bool cutActive, float speed, float maxRadius, int? heldSinceMs = null)
        {
            if (nowMs == 0)
                return new ScanFrame(false, false, 0, -1, 0f, 0f);

            int previousOwner = prevOwner;
            bool hadPreviousOwner = hasPrevOwner;
            prevOwner = ownerNow;
            hasPrevOwner = true;

            if (!CentreScanRules.ActiveScan(liveAtMs, nowMs, intervalMs, cutActive, out int start))
            {
                has = false;
                sawNoScan = true;
                lastHolder = -1;
                return new ScanFrame(false, false, 0, -1, 0f, 0f);
            }

            float radius = CentreScanRules.WaveRadius(start, nowMs, speed);
            bool travelling = CentreScanRules.IsTravelling(radius, maxRadius);
            bool heldAtStart = !heldSinceMs.HasValue || unchecked(heldSinceMs.Value - start) <= 0;
            bool canCorrect = heldSinceMs.HasValue; // without a stamp the owner read on a wave's first frame stands for the whole wave

            float prevRadius;
            if (has && start == lastStart)
            {
                if (unchecked(nowMs - prevNow) <= 0)
                    return new ScanFrame(true, travelling, start, lastHolder, radius, radius);
                prevRadius = CentreScanRules.WaveRadius(start, prevNow, speed);
                if (canCorrect && heldAtStart && ownerNow != lastHolder)
                {
                    // A capture made before the start reached this client after it: the wave was theirs all along.
                    // The band restarts from 0 only within the late grace; later, the front has long passed whoever is behind
                    // it, so the corrected holder sweeps on from the previous frame's radius (the metres nobody swept yet, nothing twice).
                    lastHolder = ownerNow;
                    if (unchecked(nowMs - start) <= LateStampGraceMs)
                        prevRadius = 0f;
                }
            }
            else
            {
                // The first frame of this wave: whoever held the centre at the start owns it. A later capture already showing
                // means the owner of the frame before was the holder (nobody known, for a joiner: nobody gets the wave).
                lastHolder = heldAtStart ? ownerNow : hadPreviousOwner ? previousOwner : -1;
                if (has || sawNoScan || unchecked(nowMs - start) <= LateStampGraceMs)
                    prevRadius = 0f;
                else
                    prevRadius = radius;
            }

            has = true;
            sawNoScan = false;
            lastStart = start;
            prevNow = nowMs;
            return new ScanFrame(true, travelling, start, lastHolder, prevRadius, radius);
        }
    }
}
