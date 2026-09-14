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
        private readonly float movingSpreadDegrees;
        private readonly float movingBloomPerSecond;

        /// <summary>The spread before the standing-still bonus is applied.</summary>
        public float CurrentAngle { get; private set; }

        /// <summary>
        /// Whether the owner was moving as of the last Tick. Stored rather than passed to
        /// EffectiveAngle so that callers cannot read a spread that disagrees with CurrentAngle.
        /// </summary>
        private bool isMoving;

        /// <summary>
        /// The spread actually used to fire, after the standing-still bonus. 1.5x tighter the
        /// instant the movement vector hits zero, so that strafing versus planting your feet is
        /// a real decision rather than something players discover by accident. While moving, a
        /// flat moving spread is added on top of CurrentAngle instead - a second, separate cost
        /// for moving, on top of the bloom that movement lets build up via Tick.
        ///
        /// Computed on read, never cached: caching it meant RegisterShot could grow CurrentAngle
        /// while EffectiveAngle still reported the pre-shot spread until the next Tick, and Reset
        /// left it at the moving value even for a standing player. Both are the kind of silent
        /// wrongness that gets blamed on the random spread instead of on the bookkeeping.
        /// </summary>
        public float EffectiveAngle =>
            isMoving ? CurrentAngle + movingSpreadDegrees : CurrentAngle / standingStillMultiplier;

        public AimConeState(float minAngle, float maxAngle, float bloomPerShot,
                            float recoveryPerSecond, float standingStillMultiplier)
            : this(minAngle, maxAngle, bloomPerShot, recoveryPerSecond, standingStillMultiplier, 0f, 0f)
        {
        }

        /// <param name="movingSpreadDegrees">Added to the spread the instant the owner moves, removed the instant they stop.</param>
        /// <param name="movingBloomPerSecond">How fast the cone widens while the owner keeps moving. Recovery still runs,
        /// so it only grows if this is larger than recoveryPerSecond.</param>
        public AimConeState(float minAngle, float maxAngle, float bloomPerShot,
                            float recoveryPerSecond, float standingStillMultiplier,
                            float movingSpreadDegrees, float movingBloomPerSecond)
        {
            this.minAngle = minAngle;
            this.maxAngle = maxAngle;
            this.bloomPerShot = bloomPerShot;
            this.recoveryPerSecond = recoveryPerSecond;
            this.standingStillMultiplier = standingStillMultiplier;
            this.movingSpreadDegrees = movingSpreadDegrees;
            this.movingBloomPerSecond = movingBloomPerSecond;

            CurrentAngle = minAngle;
            isMoving = false;
        }

        public void RegisterShot()
        {
            CurrentAngle = Mathf.Min(maxAngle, CurrentAngle + bloomPerShot);
        }

        /// <summary>
        /// Recovers CurrentAngle toward minAngle regardless of movement - isMoving only decides
        /// how EffectiveAngle is derived from the (already recovered) CurrentAngle for this
        /// frame. Recovery pausing while moving would make strafing a way to bank bloom for
        /// later, which is not the intended tradeoff. While moving, movingBloomPerSecond widens
        /// the cone on top of that same recovery - it only grows CurrentAngle if it outpaces
        /// recoveryPerSecond, same rule as RegisterShot's bloom versus recovery.
        /// </summary>
        public void Tick(float deltaTime, bool isMoving)
        {
            float bloom = isMoving ? movingBloomPerSecond * deltaTime : 0f;
            CurrentAngle = Mathf.Clamp(CurrentAngle + bloom - recoveryPerSecond * deltaTime, minAngle, maxAngle);
            this.isMoving = isMoving;
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
        }
    }
}
