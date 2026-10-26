using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// captureFadeSpeed: unfinished capture progress slides back instead of snapping when nobody is
    /// capturing/draining it. Pure C# so Building capture.cs's CalculateCaptureProgress (neutral zone)
    /// and HandleCapturedState (owned zone) can wire the same two ticks together and stay short.
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
        /// <summary>How many players drain an owned zone: the players of the one draining team (the team DrainRule
        /// named, the one the bar shows) standing in it. Another enemy team in the zone at the same time does not add to it, in line
        /// with DrainRule, which lets only one team drain at a time.</summary>
        public static int DrainerCount(int drainingTeam, IReadOnlyList<int> teamsInZone)
        {
            int n = 0;
            for (int i = 0; i < teamsInZone.Count; i++)
                if (teamsInZone[i] == drainingTeam) n++;
            return n;
        }

        /// <summary>How much of an owned zone's progress (in one-player seconds, same units as captureProgress) its drain removes per
        /// real second: one drainer takes captureSeconds / decaySeconds, more take that times the capture-speed list's factor for
        /// that many players (1 / 1.5 / 1.75 by default). BuildingCapture.UpdateDecay (the step) and CaptureProgressPublishRule (the published rate) both call this, each with the one drainer count BuildingCapture.ApplyDrain stores per frame.</summary>
        public static float DrainProgressPerSecond(float captureSeconds, float decaySeconds, int drainers, IReadOnlyList<float> captureSpeedByPlayers) =>
            captureSeconds / System.Math.Max(0.01f, decaySeconds) * CaptureSpeedRule.For(System.Math.Max(1, drainers), captureSpeedByPlayers);

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

        /// <summary>The ONE function both the master's per-frame step (BuildingCapture.CalculateCaptureProgress) and the
        /// value it publishes (BuildingCapture.ComputeCurrentProgress -> CaptureProgressPublishRule.Decide) call, through
        /// BuildingCapture.CurrentNeutralFadeRate, for a neutral zone's current fade rate, so the two can never disagree
        /// on what a client extrapolates from (at speed 0 clients would see a frozen Held band for the whole push-down and
        /// then a snap to 0; with two enemy teams they would slide at 1x while the master dropped at 2x). It picks the
        /// pushing team itself (SinglePushingTeam) and asks the mayCapture delegate about THAT team, never claimTeam, so
        /// two different enemy teams fighting in the zone, or a team whose only way in is under attack, push nothing.
        ///   - Nobody in the zone → fadeRate.
        ///   - The claim team is still listed there itself (alone, or contested alongside another team) → fadeRate
        ///     - not fading at all; the caller's ordinary capture/contest logic runs instead (CapturingTeamAbsent
        ///     already gates this function out of that case in both production callers).
        ///   - Exactly ONE other team inside (SinglePushingTeam, above) and mayCapture(pushingTeam) says yes →
        ///     pushes the claim down at least as fast as that team could capture the zone itself: max(fadeRate,
        ///     speed(N) x perPlayerSpeed), N = that team's listed players, speed from CaptureSpeedRule (1 / 1.5 / 1.75 for 1 / 2 / 3
        ///     players by default), the same speed N players would capture at.
        ///   - Two or more different other teams inside, or the single other team present may NOT capture right
        ///     now → fadeRate; they don't push, the plain fade still applies. mayCapture is never even called in
        ///     either case (nobody to ask about).</summary>
        public static float NeutralFadeRate(float fadeRate, int claimTeam, IReadOnlyList<int> teamsInZone,
                                             float perPlayerSpeed, System.Func<int, bool> mayCapture,
                                             IReadOnlyList<float> captureSpeedByPlayers)
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

            return System.Math.Max(fadeRate, CaptureSpeedRule.For(n, captureSpeedByPlayers) * perPlayerSpeed);
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
