using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// How the game expresses weapon accuracy, shared by every weapon instead of being
    /// special-cased per weapon. A rocket reads as "accurate only from a standstill" purely by
    /// being configured with a large maxAngle, a big bloomPerShot and slow recoveryPerSecond -
    /// no weapon-specific code needed. Plain C# for the same reason as the rest of Combat: it is
    /// unit tested without touching the Unity engine.
    /// </summary>
    public sealed class AimConeState
    {
        private readonly float minAngle;
        private readonly float maxAngle;
        private readonly float bloomPerShot;
        private readonly float recoveryPerSecond;
        private readonly float standingStillMultiplier;

        /// <summary>The spread before the standing-still bonus is applied.</summary>
        public float CurrentAngle { get; private set; }

        /// <summary>
        /// The spread actually used to fire, after the standing-still bonus. 1.5x tighter the
        /// instant the movement vector hits zero, so that strafing versus planting your feet is
        /// a real decision rather than something players discover by accident.
        /// </summary>
        public float EffectiveAngle { get; private set; }

        public AimConeState(float minAngle, float maxAngle, float bloomPerShot,
                            float recoveryPerSecond, float standingStillMultiplier)
        {
            this.minAngle = minAngle;
            this.maxAngle = maxAngle;
            this.bloomPerShot = bloomPerShot;
            this.recoveryPerSecond = recoveryPerSecond;
            this.standingStillMultiplier = standingStillMultiplier;

            CurrentAngle = minAngle;
            EffectiveAngle = minAngle;
        }

        public void RegisterShot()
        {
            CurrentAngle = Mathf.Min(maxAngle, CurrentAngle + bloomPerShot);
        }

        /// <summary>
        /// Recovers CurrentAngle toward minAngle regardless of movement - isMoving only decides
        /// how EffectiveAngle is derived from the (already recovered) CurrentAngle for this
        /// frame. Recovery pausing while moving would make strafing a way to bank bloom for
        /// later, which is not the intended tradeoff.
        /// </summary>
        public void Tick(float deltaTime, bool isMoving)
        {
            CurrentAngle = Mathf.Max(minAngle, CurrentAngle - recoveryPerSecond * deltaTime);
            EffectiveAngle = isMoving ? CurrentAngle : CurrentAngle / standingStillMultiplier;
        }

        /// <summary>
        /// A uniformly random offset within the current cone. Takes an injected System.Random,
        /// never UnityEngine.Random, so tests are deterministic - random spread was the
        /// designer's explicit choice over deterministic twin-ray spread.
        /// </summary>
        public float SampleOffsetDegrees(System.Random rng)
        {
            float half = EffectiveAngle / 2f;
            return (float)(rng.NextDouble() * (2.0 * half) - half);
        }

        public void Reset()
        {
            CurrentAngle = minAngle;
            EffectiveAngle = minAngle;
        }
    }
}
