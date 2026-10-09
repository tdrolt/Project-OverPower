using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Scope's eased "how scoped am I" factor, pulled out so ScopeAbility.OwnerTick stays a thin caller.
    /// CameraTracking's multiplier stack has no notion of time (a duplicate key overwrites instantly); all smoothing lives here.
    /// </summary>
    public static class ScopeEase
    {
        /// <summary>
        /// Moves current toward target at a constant rate covering the FULL 1..1+extraZoomOutPercent/100 range in
        /// easeSeconds, so scoping in and out take the same time and the value never overshoots. easeSeconds of 0 or
        /// less snaps to target ("0 means instant", like the other tuning numbers).
        /// </summary>
        public static float Advance(float current, float target, float extraZoomOutPercent, float easeSeconds, float deltaTime)
        {
            if (easeSeconds <= 0f)
                return target;

            float fullRange = extraZoomOutPercent / 100f;
            float ratePerSecond = fullRange / easeSeconds;
            return Mathf.MoveTowards(current, target, ratePerSecond * deltaTime);
        }
    }
}
