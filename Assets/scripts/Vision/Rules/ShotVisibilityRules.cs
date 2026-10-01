using System;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// Whether a shot's visuals are drawn on this client (vision D2: a hidden shooter's shot appears when it crosses the
    /// fog's edge). Plain values and a "can my team see this point" callback, so the rules are provable in an edit-mode
    /// test; TeamSight supplies the real callback. Only what is DRAWN is decided here - hits, damage and the shot logic
    /// every client runs are never touched.
    /// </summary>
    public static class ShotVisibilityRules
    {
        /// <summary>My team's shots (and the watched team's while spectating) are always drawn; an enemy's only while
        /// the point is seen.</summary>
        public static bool ShowOwnTeamOrSeen(bool shooterIsFriendly, bool pointSeen) => shooterIsFriendly || pointSeen;

        /// <summary>Whether a shooter on <paramref name="shooterTeam"/> counts as friendly to the viewer whose friendly
        /// team is <paramref name="friendlyTeam"/>. A shooter whose team is not known yet (negative) counts as an enemy,
        /// and so does everyone when the viewer has no friendly team.</summary>
        public static bool IsFriendlyTeam(int shooterTeam, int friendlyTeam) => shooterTeam >= 0 && shooterTeam == friendlyTeam;

        /// <summary>True when any sample along a to b is seen. Samples every <paramref name="spacing"/> metres from a,
        /// and b is always sampled last; a zero-length line is one sample. A spacing that is not positive samples just
        /// the two ends.</summary>
        public static bool LineSeen(Vector3 a, Vector3 b, float spacing, Func<Vector3, bool> seen)
        {
            float length = Vector3.Distance(a, b);
            if (length <= 0f)
                return seen(a);
            if (seen(a))
                return true;
            if (spacing > 0f)
            {
                Vector3 step = (b - a) / length;
                for (float d = spacing; d < length; d += spacing)
                    if (seen(a + step * d))
                        return true;
            }
            return seen(b);
        }

        /// <summary>A blast is drawn when it is my team's, when its centre is seen, or when it reaches me (so a blast
        /// from the fog that hits me is never invisible).</summary>
        public static bool BlastShown(bool shooterIsFriendly, bool centreSeen, float distanceToLocal, float radius) =>
            shooterIsFriendly || centreSeen || distanceToLocal <= radius;
    }
}
