using System;
using System.Collections.Generic;
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

        /// <summary>A blast is drawn when it is my team's, when its centre is seen, or when it hurts someone on my team
        /// (the blast reaches one of my team's eyes), so a blast from the fog that hits us is never invisible.</summary>
        public static bool BlastShown(bool shooterIsFriendly, bool centreSeen, bool reachesMyTeam) =>
            shooterIsFriendly || centreSeen || reachesMyTeam;

        /// <summary>True when any of the positions is within <paramref name="radius"/> of the blast centre (the rim counts).</summary>
        public static bool ReachesAny(Vector3 centre, float radius, IReadOnlyList<Vector3> positions)
        {
            for (int i = 0; i < positions.Count; i++)
                if (Vector3.Distance(centre, positions[i]) <= radius)
                    return true;
            return false;
        }

        /// <summary>The height the wall test aims at. A point on the ground is lifted by the Eye Height (as high as the eyes
        /// looking); a shot is already in the air, so it is judged on the eye's own plane (the one the sight picture uses),
        /// not lifted again over a Cover Wall.</summary>
        public static float TargetHeight(bool atEyeHeight, float eyeY, float pointY, float eyeHeight) =>
            atEyeHeight ? eyeY : pointY + eyeHeight;

        /// <summary>True when the disc (a zone, a fire field) is seen: its centre, or any of <paramref name="rimPoints"/>
        /// points spaced evenly round the rim at the centre's height. Stops at the first seen sample. A radius that is
        /// not positive is just the centre.</summary>
        public static bool DiscSeen(Vector3 centre, float radius, int rimPoints, Func<Vector3, bool> seen)
        {
            if (seen(centre))
                return true;
            if (radius <= 0f || rimPoints <= 0)
                return false;
            for (int i = 0; i < rimPoints; i++)
            {
                float angle = i * (2f * Mathf.PI / rimPoints);
                if (seen(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius))
                    return true;
            }
            return false;
        }

        /// <summary>Like ReachesAny, but each player is a vertical line from their feet up <paramref name="bodyHeight"/>:
        /// the blast counts when it reaches the nearest point of that line, so a burst in the air reaches a chest.</summary>
        public static bool ReachesAnyBody(Vector3 centre, float radius, IReadOnlyList<Vector3> feet, float bodyHeight)
        {
            for (int i = 0; i < feet.Count; i++)
            {
                float y = Mathf.Clamp(centre.y, feet[i].y, feet[i].y + Mathf.Max(0f, bodyHeight));
                if (Vector3.Distance(centre, new Vector3(feet[i].x, y, feet[i].z)) <= radius)
                    return true;
            }
            return false;
        }

        /// <summary>True when the point lies inside a flat cone: within <paramref name="range"/> of the apex on the ground
        /// and within half of <paramref name="fullAngleDegrees"/> of the forward direction (height ignored, the way the
        /// Flamethrower itself measures). The apex itself counts.</summary>
        public static bool ConeReaches(Vector3 apex, Vector3 forward, float range, float fullAngleDegrees, Vector3 point)
        {
            Vector2 toPoint = new Vector2(point.x - apex.x, point.z - apex.z);
            float distance = toPoint.magnitude;
            if (distance > range)
                return false;
            if (distance < 0.0001f)
                return true;
            Vector2 flatForward = new Vector2(forward.x, forward.z);
            if (flatForward.sqrMagnitude < 0.0001f)
                return false;
            return Vector2.Angle(flatForward, toPoint) <= fullAngleDegrees * 0.5f;
        }
    }
}
