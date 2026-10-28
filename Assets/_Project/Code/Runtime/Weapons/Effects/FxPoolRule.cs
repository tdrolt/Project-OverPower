using UnityEngine;

namespace Overpower.Weapons
{
    public enum FxPoolAction { ReuseIdle, Instantiate, RecycleOldest }

    /// <summary>How VfxManager gets a copy of an impact or muzzle flash: a finished copy first, a new one while fewer than the cap are
    /// playing, else the oldest one still playing is restarted at the new spot. A cap below 1 still allows one copy. Also where a flash is drawn.</summary>
    public static class FxPoolRule
    {
        public static FxPoolAction Take(int idle, int live, int cap)
        {
            if (idle > 0)
                return FxPoolAction.ReuseIdle;
            return live < System.Math.Max(1, cap) ? FxPoolAction.Instantiate : FxPoolAction.RecycleOldest;
        }

        /// <summary>Where a flash is drawn: <paramref name="pull"/> metres from where it happened toward the camera, never past halfway. On the line
        /// to the camera it stays on the same spot on screen, and a camera-facing flash on a wall is not half hidden inside the wall.</summary>
        public static Vector3 Placed(Vector3 at, Vector3 eye, float pull)
        {
            Vector3 toEye = eye - at;
            float distance = toEye.magnitude;
            if (pull <= 0f || distance <= 1e-4f)
                return at;
            return at + toEye * (Mathf.Min(pull, distance * 0.5f) / distance);
        }
    }
}
