using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// How see-through the minimap is right now (Tudor, 2026-09-17): the corner map is a little transparent so it
    /// takes less of the arena away, pressing M puts the big map at full opacity, and moving with the big map open
    /// drops it again so a player can still see where they are running.
    ///
    /// Pure, and therefore tested in edit mode rather than eyeballed in Play Mode, because the interesting part is
    /// not the three numbers - it is that the map must not STROBE. Two separate speeds (not one threshold) mean a
    /// player drifting around walking pace cannot cross the line several times a second, and the alpha then fades
    /// between states instead of snapping, so even a real start/stop reads as a change of state rather than a
    /// flicker.
    /// </summary>
    public static class MinimapOpacity
    {
        /// <summary>Whether the player counts as moving, given what they counted as last frame. Between the two
        /// speeds the answer is simply "whatever it already was" - that gap IS the deadzone. Passing the two
        /// speeds the wrong way round still works: the higher of the two always starts movement.</summary>
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

        /// <summary>The opacity this state should settle at. The moving drop comes off the LARGE opacity only
        /// (Tudor: "unless they are moving with the map maximized which would decrease the opacity by 30%") -
        /// the corner map never dims further for movement, since it is already the quiet one.</summary>
        public static float TargetAlpha(bool largeOpen, bool moving, float cornerAlpha, float largeAlpha,
                                        float largeMovingDrop)
        {
            if (!largeOpen)
                return Mathf.Clamp01(cornerAlpha);

            return Mathf.Clamp01(moving ? largeAlpha - largeMovingDrop : largeAlpha);
        }

        /// <summary>One frame of a fade that crosses the whole 0-1 range in fadeSeconds. A fadeSeconds of 0 (or
        /// less) snaps straight to the target, so a designer can turn the fade off entirely.</summary>
        public static float Step(float current, float target, float deltaTime, float fadeSeconds)
        {
            if (fadeSeconds <= 0f || deltaTime <= 0f)
                return target;

            return Mathf.MoveTowards(current, target, deltaTime / fadeSeconds);
        }

        /// <summary>Exponential smoothing of a measured speed, frame-rate independent: after smoothingSeconds a
        /// step change is about 63% of the way there. Raw per-frame position deltas are noisy enough on their own
        /// to tip a threshold back and forth, which is the other half of why the map used to be able to flicker.
        /// A smoothingSeconds of 0 (or less) returns the raw sample.</summary>
        public static float SmoothSpeed(float current, float sample, float deltaTime, float smoothingSeconds)
        {
            if (smoothingSeconds <= 0f || deltaTime <= 0f)
                return sample;

            return Mathf.Lerp(current, sample, 1f - Mathf.Exp(-deltaTime / smoothingSeconds));
        }
    }
}
