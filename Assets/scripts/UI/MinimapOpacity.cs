using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// How see-through the minimap is right now: the corner map is a little transparent, the big map (M) is full
    /// opacity, and moving with the big map open drops it so the player can see where they run. Pure and tested,
    /// because the point is that the map must not STROBE: two speeds (not one threshold) keep a player drifting
    /// around walking pace from crossing the line several times a second, and the alpha fades between states
    /// instead of snapping.
    /// </summary>
    public static class MinimapOpacity
    {
        /// <summary>Whether the player counts as moving, given last frame's answer. Between the two speeds the
        /// answer is whatever it already was - that gap IS the deadzone. Swapped speeds still work: the higher of
        /// the two always starts movement.</summary>
        public static bool IsMoving(bool wasMoving, float speed, float enterSpeed, float exitSpeed)
        {
            float low = Mathf.Min(enterSpeed, exitSpeed);
            float high = Mathf.Max(enterSpeed, exitSpeed);
            if (speed >= high)
                return true;
            if (speed <= low)
                return false;
            return wasMoving;
        }

        /// <summary>The opacity this state settles at. The moving drop comes off the LARGE opacity only; the corner
        /// map never dims further for movement, since it is already the quiet one.</summary>
        public static float TargetAlpha(bool largeOpen, bool moving, float cornerAlpha, float largeAlpha,
                                        float largeMovingDrop)
        {
            if (!largeOpen)
                return Mathf.Clamp01(cornerAlpha);

            return Mathf.Clamp01(moving ? largeAlpha - largeMovingDrop : largeAlpha);
        }

        /// <summary>One frame of a fade crossing the whole 0-1 range in fadeSeconds; 0 or less snaps to the target
        /// (a designer can turn the fade off).</summary>
        public static float Step(float current, float target, float deltaTime, float fadeSeconds)
        {
            if (fadeSeconds <= 0f || deltaTime <= 0f)
                return target;

            return Mathf.MoveTowards(current, target, deltaTime / fadeSeconds);
        }

        /// <summary>Frame-rate-independent exponential smoothing of a measured speed (about 63% of a step after
        /// smoothingSeconds). Raw per-frame position deltas are noisy enough to tip the threshold back and forth,
        /// the other half of why the map could flicker. 0 or less returns the raw sample.</summary>
        public static float SmoothSpeed(float current, float sample, float deltaTime, float smoothingSeconds)
        {
            if (smoothingSeconds <= 0f || deltaTime <= 0f)
                return sample;

            return Mathf.Lerp(current, sample, 1f - Mathf.Exp(-deltaTime / smoothingSeconds));
        }
    }
}
