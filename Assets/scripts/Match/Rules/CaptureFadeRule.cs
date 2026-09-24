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

        /// <summary>The one enemy team actually pushing a neutral zone's claim down this tick: the single OTHER
        /// team (not claimTeam) among the zone's listed players, only when every non-claim player belongs to that
        /// same team. -1 when nobody but the claim team is here (nothing pushing, including an empty zone), or
        /// when two or more different other teams are here at once (contested among the pushers - nobody's turn
        /// to push alone).</summary>
        public static int SinglePushingTeam(int claimTeam, IReadOnlyList<int> teamsInZone)
        {
            int pushing = -1;
            for (int i = 0; i < teamsInZone.Count; i++)
            {
                int team = teamsInZone[i];
                if (team == claimTeam) continue;
                if (pushing == -1) pushing = team;
                else if (pushing != team) return -1;
            }
            return pushing;
        }

        /// <summary>Opus re-review, 2026-09-24 (replaces the same day's own first fix, EffectiveFadeRate): that
        /// version counted EVERY non-claim player as pushing, so two DIFFERENT enemy teams fighting inside a faded
        /// claim pushed it down at their combined speed, and it ignored TeamMayCaptureNow, so a team whose only
        /// way in was itself under attack still pushed a claim down it could not actually capture. This is the ONE
        /// function both the master's per-frame step (BuildingCapture.CalculateCaptureProgress) and the value it
        /// publishes (BuildingCapture.ComputeCurrentProgress -> CaptureProgressPublishRule.Decide) call for a
        /// neutral zone's current fade rate, so the two can never disagree on what a client extrapolates from -
        /// the bug the opus review found: at speed 0 clients saw a frozen Held band for the whole push-down and
        /// then a snap to 0; at the default speed 1 with two enemies clients slid at 1x while the master dropped
        /// at 2x, then snapped.
        ///
        /// Opus re-review, 2026-09-24 (second pass, "the wiring still isn't tested"): picks the pushing team
        /// itself (SinglePushingTeam, below) and asks the mayCapture delegate about THAT team, rather than taking
        /// a plain bool the caller worked out beforehand - both production call sites used to compute
        /// SinglePushingTeam and ask TeamMayCaptureNow(pushingTeam) themselves, copied at both sites, and nothing
        /// caught a call site asking about claimTeam instead, or a call site reverted to the plain fadeRate; see
        /// BuildingCapture.CurrentNeutralFadeRate, the one place both call sites now reach this function through,
        /// and CaptureFadeRuleTests' recording-delegate test for proof mayCapture is always asked about the
        /// pushing team, never claimTeam.
        ///   - Nobody in the zone → fadeRate.
        ///   - The claim team is still listed there itself (alone, or contested alongside another team) → fadeRate
        ///     - not fading at all; the caller's ordinary capture/contest logic runs instead (CapturingTeamAbsent
        ///     already gates this function out of that case in both production callers).
        ///   - Exactly ONE other team inside (SinglePushingTeam, above) and mayCapture(pushingTeam) says yes →
        ///     pushes the claim down at least as fast as that team could capture the zone itself: max(fadeRate,
        ///     N x perPlayerSpeed), N = that team's listed players, so N players push N times as fast as one,
        ///     exactly like capturing.
        ///   - Two or more different other teams inside, or the single other team present may NOT capture right
        ///     now → fadeRate; they don't push, the plain fade still applies. mayCapture is never even called in
        ///     either case (nobody to ask about).</summary>
        public static float NeutralFadeRate(float fadeRate, int claimTeam, IReadOnlyList<int> teamsInZone,
                                             float perPlayerSpeed, System.Func<int, bool> mayCapture)
        {
            if (teamsInZone.Count == 0 || Contains(teamsInZone, claimTeam))
                return fadeRate;

            int pushingTeam = SinglePushingTeam(claimTeam, teamsInZone);
            if (pushingTeam == -1 || !mayCapture(pushingTeam))
                return fadeRate;

            int n = 0;
            for (int i = 0; i < teamsInZone.Count; i++)
                if (teamsInZone[i] == pushingTeam)
                    n++;

            return System.Math.Max(fadeRate, n * perPlayerSpeed);
        }

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
