using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The mech's shoot motion follows the real shots: every shot restarts it from its first frame on every client, a fast gun
    /// plays it faster so one motion fits between two shots, and each shot holds the pose until its motion is done. A remote copy holds it
    /// from the shot it saw, because the streamed shoot bool can land after the shot itself.</summary>
    public static class ShootMotion
    {
        /// <summary>The Shoot state's speed: the clip's length over the gun's fire interval, never slower than authored and never above
        /// the cap.</summary>
        public static float Speed(float clipSeconds, float fireIntervalSeconds, float maxSpeed)
        {
            float cap = Mathf.Max(1f, maxSpeed);
            if (clipSeconds <= 0f)
                return 1f;
            if (fireIntervalSeconds <= 0f)
                return cap;
            return Mathf.Clamp(clipSeconds / fireIntervalSeconds, 1f, cap);
        }

        /// <summary>The shooter restarts the motion when its shot leaves, so the server's echo of that same shot must not restart it again.</summary>
        public static bool ShowsArrivingShot(bool isOwnShot) => !isOwnShot;

        /// <summary>How long a shot holds the shoot pose: its whole motion at the speed it plays, never under the minimum that lets a tap
        /// reach the other screens. Cut shorter, the bool turns off mid-motion and the mech blends back out before the recoil is done.</summary>
        public static float HoldSeconds(float clipSeconds, float speed, float minimumSeconds)
        {
            float motion = clipSeconds > 0f && speed > 0f ? clipSeconds / speed : 0f;
            return Mathf.Max(minimumSeconds, motion);
        }

        public static bool HoldsPose(float secondsSinceShotSeen, float poseSeconds) => secondsSinceShotSeen < poseSeconds;
    }
}
