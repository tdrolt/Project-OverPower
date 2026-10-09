using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Works out where a blink lands. Blink never travels the space between caster and destination,
    /// so only the destination matters: clamp the requested point to a maximum range from the
    /// caster, then walk the straight line back toward the caster in fixed steps; the first valid
    /// candidate wins, and if even the caster's own spot fails the search gives up. The physics
    /// (ground, kill plane, wall) is an injected delegate so this stays plain C# and provable
    /// without a scene.
    /// </summary>
    public static class BlinkDestinationSearch
    {
        /// <summary>
        /// Checks one candidate horizontal position (Y is always 0 here; the caller resolves the real
        /// landing height from the ground). Returns true and the landing point, ground height
        /// already applied, if a player could stand there.
        /// </summary>
        public delegate bool ValidityProbe(Vector3 candidateXZ, out Vector3 landingPoint);

        public readonly struct Result
        {
            public readonly bool Found;
            public readonly Vector3 Destination;

            /// <summary>True when the destination sits closer to the caster than the raw requested
            /// point (beyond range, or blocked so the search stepped back). False only when the blink
            /// landed exactly where the cursor pointed.</summary>
            public readonly bool Adjusted;

            public Result(bool found, Vector3 destination, bool adjusted)
            {
                Found = found;
                Destination = destination;
                Adjusted = adjusted;
            }

            public static readonly Result None = new Result(false, default, false);
        }

        // Not a tuning value: below this the direction toward the request is meaningless (normalising
        // would divide by ~0), so the search probes only the caster's own spot.
        private const float MinDirectionSqrMagnitude = 0.0001f;

        // A float rounding error on an exact-range request must not read as "adjusted" in the log line.
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
                    return Result.None;

                distance = Mathf.Max(0f, distance - step);
            }
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
