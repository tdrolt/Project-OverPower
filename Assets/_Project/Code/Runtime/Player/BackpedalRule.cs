using UnityEngine;

namespace Overpower.Combat
{
    public enum MoveHeading { Forward, Strafe, BackDiagonal, Back }

    /// <summary>
    /// Walking away from where you aim is slower: straight back costs the most, a back diagonal less, a strafe nothing. Shooting
    /// while stepping away from the aim (back or back diagonal, never a strafe) costs a further share. The slices are measured
    /// from straight back and from straight sideways, so with the default 45 degrees each of the eight key directions lands in one.
    /// </summary>
    public static class BackpedalRule
    {
        public struct Settings
        {
            public float backSlow;
            public float backDiagonalSlow;
            public float shootingAwaySlow;
            public float backSliceDegrees;
            public float strafeSliceDegrees;
        }

        /// <summary>Flat directions only; no movement or no aim reads as forward (no slow).</summary>
        public static MoveHeading HeadingOf(Vector3 move, Vector3 aim, float backSliceDegrees, float strafeSliceDegrees)
        {
            move.y = 0f;
            aim.y = 0f;
            if (move.sqrMagnitude < 1e-6f || aim.sqrMagnitude < 1e-6f)
                return MoveHeading.Forward;

            float fromAim = Vector3.Angle(aim, move);
            if (fromAim >= 180f - backSliceDegrees * 0.5f)
                return MoveHeading.Back;
            if (Mathf.Abs(fromAim - 90f) <= strafeSliceDegrees * 0.5f)
                return MoveHeading.Strafe;
            return fromAim > 90f ? MoveHeading.BackDiagonal : MoveHeading.Forward;
        }

        /// <summary>The share of walking speed kept, 0..1.</summary>
        public static float SpeedMultiplier(MoveHeading heading, bool shooting, Settings s)
        {
            float slow;
            switch (heading)
            {
                case MoveHeading.Back: slow = s.backSlow; break;
                case MoveHeading.BackDiagonal: slow = s.backDiagonalSlow; break;
                default: return 1f;
            }
            if (shooting)
                slow += s.shootingAwaySlow;
            return Mathf.Clamp01(1f - slow);
        }

        /// <summary>The gun counts as shooting from a shot until one shot's gap (plus the grace) has passed.</summary>
        public static bool IsShooting(float secondsSinceShot, float shotIntervalSeconds, float graceSeconds) =>
            secondsSinceShot <= shotIntervalSeconds + graceSeconds;
    }
}
