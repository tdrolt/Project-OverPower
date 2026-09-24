using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// captureFadeSpeed (Tudor, 2026-09-24): unfinished capture progress slides back instead of snapping when
    /// nobody is capturing/draining it. Pure C# so Building capture.cs's CalculateCaptureProgress (neutral zone)
    /// and HandleCapturedState (owned zone) can wire the same two ticks together and stay short - see their own
    /// comments for exactly where each piece plugs in.
    ///
    /// Two independent pieces: CapturingTeamAbsent decides WHETHER a neutral zone's claim should fade this tick
    /// (nobody of the claiming team currently listed - whether the zone is empty or only another team stands
    /// there; contested is excluded on purpose, since the claiming team is still listed there too); Step/Refill are
    /// the actual per-tick maths, shared by both the neutral (fades toward 0) and owned (refills toward full)
    /// cases - same rate, same units as ProgressPerPlayerPerSecond (one-player-seconds per real second), so
    /// captureFadeSpeed 1 fades/refills exactly as fast as one player would have built/drained it.
    /// </summary>
    public static class CaptureFadeRule
    {
        /// <summary>True when a neutral zone's current claim should fade this tick: it has a real claiming team
        /// (capturingId >= 0) and none of that team's players are currently listed in the zone - whether the zone
        /// is entirely empty or only another team stands there. False for an unclaimed zone (-1, nothing to fade)
        /// and false while the claiming team is still listed (alone, or contested by another team too) - both left
        /// to CaptureClaimRule/the ordinary build-or-hold path instead.</summary>
        public static bool CapturingTeamAbsent(int capturingId, IReadOnlyList<int> teamsInZone) =>
            capturingId >= 0 && !Contains(teamsInZone, capturingId);

        /// <summary>Review fix, 2026-09-24: at captureFadeSpeed 0, a neutral zone whose claiming team left stayed
        /// locked to that team forever once ANOTHER team stood there alone - CalculateCaptureProgress's own fade
        /// check kept it at the same progress every tick (Step with rate 0), so the other team could never claim
        /// it without the plain fade ever moving. Decided: when another team stands in the zone ALONE (nobody of
        /// them the caller only calls this with otherTeamPlayersInZone > 0 to begin with), it pushes the old claim
        /// down at least as fast as it could capture the zone itself - never slower than
        /// otherTeamPlayersInZone x progressPerPlayerPerSecond, and never slower than the configured fadeRatePerSecond
        /// either, so N players still push N times as fast as one, exactly like capturing. The plain fade (nobody
        /// in the zone at all - otherTeamPlayersInZone 0) is untouched: it keeps fadeRatePerSecond exactly, so
        /// fade speed 0 still means "holds while nobody's there".</summary>
        public static float EffectiveFadeRate(float fadeRatePerSecond, int otherTeamPlayersInZone, float progressPerPlayerPerSecond) =>
            otherTeamPlayersInZone > 0
                ? System.Math.Max(fadeRatePerSecond, otherTeamPlayersInZone * progressPerPlayerPerSecond)
                : fadeRatePerSecond;

        /// <summary>One tick of a neutral zone's claim fading toward 0, never past it.</summary>
        public static float Step(float captureProgress, float fadeRatePerSecond, float deltaTime) =>
            System.Math.Max(0f, captureProgress - fadeRatePerSecond * deltaTime);

        /// <summary>One tick of an owned zone's hold refilling toward full (captureSeconds), never past it.</summary>
        public static float Refill(float captureProgress, float captureSeconds, float fadeRatePerSecond, float deltaTime) =>
            System.Math.Min(captureSeconds, captureProgress + fadeRatePerSecond * deltaTime);

        private static bool Contains(IReadOnlyList<int> teams, int team)
        {
            for (int i = 0; i < teams.Count; i++)
                if (teams[i] == team)
                    return true;
            return false;
        }
    }
}
