using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Works out where a blink actually lands. Blink is instantaneous - it never travels the space
    /// between the caster and the destination, so nothing along that path matters, only the
    /// destination itself. The rule (Tudor's clarification, Task 1.6b): clamp the requested point to
    /// a maximum range from the caster, then walk the straight line back toward the caster in fixed
    /// steps until a candidate spot is clear, first valid wins. If even the caster's own spot fails
    /// the search gives up rather than picking something arbitrary.
    ///
    /// The physics part (is the ground there, is it below the kill plane, is a wall in the way) is
    /// injected as a delegate so this class stays free of Physics/Collider, the same reason
    /// DisplacementPriority and OverheatState are plain C# - "does a blink reach past a thin wall"
    /// should be provable without a scene, a Rigidbody or a physics step.
    /// </summary>
    public static class BlinkDestinationSearch
    {
        /// <summary>
        /// Checks one candidate horizontal position (Y is always 0 here - the caller resolves the
        /// real landing height from the ground itself, which this class never touches). Returns true
        /// and the resolved landing point (ground height already applied, ready to teleport to) if a
        /// player could stand there.
        /// </summary>
        public delegate bool ValidityProbe(Vector3 candidateXZ, out Vector3 landingPoint);

        /// <summary>What the search found, or didn't.</summary>
        public readonly struct Result
        {
            public readonly bool Found;
            public readonly Vector3 Destination;

            /// <summary>True when the destination actually used sits closer to the caster than the
            /// raw requested point - either the request was beyond range, or the requested (or
            /// clamped) spot was blocked and the search had to step back to find one that wasn't.
            /// False only when the blink landed exactly where the cursor pointed.</summary>
            public readonly bool Adjusted;

            public Result(bool found, Vector3 destination, bool adjusted)
            {
                Found = found;
                Destination = destination;
                Adjusted = adjusted;
            }

            public static readonly Result None = new Result(false, default, false);
        }

        // Not a tuning value: below this the requested point is close enough to the caster that
        // "the direction toward it" stops meaning anything (dividing by ~0 to normalise it would
        // blow up). Below it the search simply treats the request as "blink nowhere" and probes
        // only the caster's own spot.
        private const float MinDirectionSqrMagnitude = 0.0001f;

        // How much closer the used distance must be than the requested one before it counts as
        // "adjusted" for the log line - guards against a float rounding error on an exact-range
        // request reading as adjusted when nothing was actually moved back.
        private const float AdjustedEpsilon = 0.0001f;

        public static Result Find(Vector3 origin, Vector3 requestedPoint, float range, float searchStep,
                                   ValidityProbe probe)
        {
            Vector3 originXZ = Flatten(origin);
            Vector3 requestedXZ = Flatten(requestedPoint);
            Vector3 toRequested = requestedXZ - originXZ;
            float requestedDistance = toRequested.magnitude;

            float clampedRange = Mathf.Max(0f, range);
            float step = Mathf.Max(0.01f, searchStep); // never 0 or negative - that would search forever.

            Vector3 direction = toRequested.sqrMagnitude > MinDirectionSqrMagnitude
                ? toRequested / requestedDistance
                : Vector3.zero;

            float distance = Mathf.Min(requestedDistance, clampedRange);

            while (true)
            {
                Vector3 candidateXZ = originXZ + direction * distance;
                if (probe(candidateXZ, out Vector3 landingPoint))
                {
                    bool adjusted = distance < requestedDistance - AdjustedEpsilon;
                    return new Result(true, landingPoint, adjusted);
                }

                if (distance <= 0f)
                    return Result.None; // even the caster's own spot failed - nothing to land on.

                distance = Mathf.Max(0f, distance - step);
            }
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
