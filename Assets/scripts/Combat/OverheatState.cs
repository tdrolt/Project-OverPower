using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The firing resource every weapon spends, and the one the Sprint ability spends too - that
    /// shared pool is the point: sprinting costs you the ability to shoot, without either system
    /// needing to know about the other. Plain C# for the same reason as DamageResolver and
    /// StatusEffectState: it is unit tested without touching the Unity engine, and a
    /// MonoBehaviour wrapper calls Tick from Update in a later task.
    ///
    /// The rule that makes this more than a health bar in reverse: hitting max does not just
    /// block further Add calls, it silences the primary weapon and every ability until the bar
    /// empties back to zero. With the real numbers (max 100, 25/s decay) that is up to 4 seconds
    /// of being unable to act. That was accepted deliberately over a gentler weapon-only lockout,
    /// on the condition that IsWarning exists at 80 heat so the silence reads as the player's own
    /// mistake rather than an arbitrary wall - see 00-master-plan.md.
    /// </summary>
    public sealed class OverheatState
    {
        private readonly float max;
        private readonly float decayDelay;
        private readonly float decayPerSecond;
        private readonly float warningThreshold;

        // Counts down from decayDelay every time heat is added, and only once it reaches zero
        // does Tick start removing heat. This is what makes rapid, repeated firing feel like it
        // "holds" the bar up rather than bleeding off between shots.
        private float timeSinceLastAdd;

        public float Heat { get; private set; }

        /// <summary>0..1, for the HUD bar.</summary>
        public float Normalised => Heat / max;

        /// <summary>
        /// True from the instant Heat reaches max until it decays all the way back to 0 - not
        /// merely below max. Checking Heat alone would clear the silence the moment decay ticks
        /// heat down by any amount, which is not the punishment the designer signed off on.
        /// </summary>
        public bool IsSilenced { get; private set; }

        /// <summary>
        /// Warns that overheat is close, but only before it happens. Deliberately false while
        /// IsSilenced: the warning's job is to precede the silence, not to accompany it.
        /// </summary>
        public bool IsWarning => Heat >= warningThreshold && !IsSilenced;

        public bool CanAct => !IsSilenced;

        public OverheatState(float max, float decayDelay, float decayPerSecond, float warningThreshold)
        {
            this.max = max;
            this.decayDelay = decayDelay;
            this.decayPerSecond = decayPerSecond;
            this.warningThreshold = warningThreshold;
        }

        /// <summary>Spend heat: a shot, or a second of sprinting.</summary>
        public void Add(float amount)
        {
            Heat = Mathf.Min(max, Heat + amount);
            timeSinceLastAdd = 0f;

            if (Heat >= max)
                IsSilenced = true;
        }

        /// <summary>The laser's half-cost refund when a shot connects. Never goes below zero.</summary>
        public void Refund(float amount)
        {
            Heat = Mathf.Max(0f, Heat - amount);
        }

        public void Tick(float deltaTime)
        {
            float previousElapsed = timeSinceLastAdd;
            timeSinceLastAdd += deltaTime;

            if (timeSinceLastAdd <= decayDelay)
                return; // still inside the delay window - no decay yet

            // Only the slice of this step that falls after the delay has elapsed actually
            // decays heat. Without this split, a single large Tick that straddles the delay
            // boundary would wrongly decay for its *entire* deltaTime instead of just the part
            // of it that occurs once the delay is over.
            float decayTime = timeSinceLastAdd - Mathf.Max(previousElapsed, decayDelay);
            Heat = Mathf.Max(0f, Heat - decayPerSecond * decayTime);

            if (Heat <= 0f)
                IsSilenced = false;
        }

        /// <summary>On death: zero the bar and lift any silence with it.</summary>
        public void Clear()
        {
            Heat = 0f;
            IsSilenced = false;
            timeSinceLastAdd = 0f;
        }
    }
}
