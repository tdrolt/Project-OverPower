using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Scope ability's own "how scoped am I right now" factor, eased rather than snapped - pulled out so
    /// ScopeAbility.OwnerTick can stay a thin caller, the same reasoning as CameraZoomStack for
    /// CameraTracking.LateUpdate. CameraTracking's multiplier stack itself has no notion of time at all (a
    /// duplicate key overwrites instantly, like PlayerMotor's) - all of the smoothing lives here, on the one
    /// component that actually knows about holding and releasing a key.
    /// </summary>
    public static class ScopeEase
    {
        /// <summary>
        /// Moves current toward target at a constant rate that covers the FULL 1..1+extraZoomOutPercent/100 range
        /// in exactly easeSeconds, in whichever direction current is currently travelling - so scoping in and
        /// easing back out take the same amount of time, and the value never overshoots past target
        /// (Mathf.MoveTowards clamps at it). easeSeconds of 0 or less snaps straight to target - the same "0 means
        /// instant" convention this project's other tuning numbers use (AbilityModule.cooldownSeconds among them).
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
