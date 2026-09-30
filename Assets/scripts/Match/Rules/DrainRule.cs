using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// What happens to an owned zone's drain each frame: an enemy standing in the zone alone drains it towards neutral.
    ///
    /// - A defender standing in the zone, or every attacker of the draining team leaving, stops the drain. The zone
    ///   then refills toward full at captureFadeSpeed (Building capture.cs's HandleCapturedState) instead of
    ///   snapping back to it, and the next drain starts from wherever that refill has gotten to, not necessarily
    ///   full (captureFadeSpeed, [C], 2026-09-24).
    /// - An attacker whose team may not capture the zone right now (its only way in is a zone of its own that is under
    ///   attack, Tudor 2026-09-16) doesn't drain. A drain already running pauses and carries on from where it was
    ///   once the way in is safe, like a blocked neutral capture (controller decision, 2026-09-16).
    ///
    /// Pure, so the master's per-frame tick in BuildingCapture stays short and every case here is tested.
    /// </summary>
    public static class DrainRule
    {
        public enum Step
        {
            /// <summary>No drain running and none starting.</summary>
            None,
            /// <summary>An enemy team starts draining, from wherever the zone's progress currently sits - full if
            /// it was never drained since capture, or wherever captureFadeSpeed's refill had gotten to otherwise
            /// (captureFadeSpeed, [C], 2026-09-24 - Building capture.cs's HandleCapturedState no longer resets
            /// captureProgress to full here).</summary>
            Start,
            /// <summary>The drain goes on, or resumes from where it paused.</summary>
            Continue,
            /// <summary>The drainers' way in is under attack: the drain holds where it is.</summary>
            Pause,
            /// <summary>A defender arrived or the attackers left: the drain is over.</summary>
            Stop,
        }

        public readonly struct Decision
        {
            public readonly Step Step;

            /// <summary>The team draining (Start, Continue) or holding the paused drain (Pause). -1 otherwise.</summary>
            public readonly int Team;

            public Decision(Step step, int team)
            {
                Step = step;
                Team = team;
            }
        }

        /// <param name="owner">The zone's owner.</param>
        /// <param name="teamsInZone">The team of every player standing in the zone whom the territory rule let in on
        /// entry (a team may appear more than once).</param>
        /// <param name="defenderPresent">A living player of the owner's team stands in the zone.</param>
        /// <param name="draining">A drain started and hasn't stopped; it may be paused.</param>
        /// <param name="drainingTeam">The team the running drain belongs to, or -1.</param>
        /// <param name="mayCaptureNow">May this team still capture the zone right now (its way in isn't under
        /// attack)?</param>
        public static Decision Decide(int owner, IReadOnlyList<int> teamsInZone, bool defenderPresent, bool draining,
                                      int drainingTeam, Func<int, bool> mayCaptureNow)
        {
            // The team already draining keeps it while it may, so a second attacker walking in doesn't take the drain
            // over. Otherwise the first enemy team that may drain: when the drainers leave or lose their way in, the
            // team actually draining is the one the bar names.
            int drainer = -1;
            bool blockedEnemy = false;
            if (draining && drainingTeam >= 0 && drainingTeam != owner && Contains(teamsInZone, drainingTeam)
                && mayCaptureNow(drainingTeam))
            {
                drainer = drainingTeam;
            }
            else
            {
                for (int i = 0; i < teamsInZone.Count; i++)
                {
                    int team = teamsInZone[i];
                    if (team == owner)
                        continue;
                    if (mayCaptureNow(team))
                    {
                        drainer = team;
                        break;
                    }
                    blockedEnemy = true;
                }
            }

            if (!defenderPresent)
            {
                if (drainer >= 0)
                    return new Decision(draining ? Step.Continue : Step.Start, drainer);
                // Only the draining team's own drain can pause. If that team has gone and only a blocked team is left,
                // the drain is over: that team's drain, once it may, starts from wherever the zone's progress has
                // refilled to by then (captureFadeSpeed, [C], 2026-09-24), not necessarily full.
                if (draining && blockedEnemy && Contains(teamsInZone, drainingTeam))
                    return new Decision(Step.Pause, drainingTeam);
            }
            return new Decision(draining ? Step.Stop : Step.None, -1);
        }

        private static bool Contains(IReadOnlyList<int> teams, int team)
        {
            for (int i = 0; i < teams.Count; i++)
                if (teams[i] == team)
                    return true;
            return false;
        }
    }
}
