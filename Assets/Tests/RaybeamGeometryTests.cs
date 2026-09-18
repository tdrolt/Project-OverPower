using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Pins down the raybeam's own claim - "three beams converge at the cursor, so the skill is
    /// placing the cursor at the right distance" - as maths rather than something only verified by
    /// eye in Play mode. See RaybeamGeometry's class comment for why this is separated from the
    /// MonoBehaviour that actually fires the beams.
    /// </summary>
    public class RaybeamGeometryTests
    {
        private const float Spacing = 0.75f;
        private const float Range = 24f;      // beamRange (rework step 6: 12 -> 24, Tudor's "traverse" x2)
        private const float Radius = 0.2625f; // beamWidth 0.525 / 2 (rework step 6: 0.35 -> 0.525)

        [Test]
        public void BeamOriginsAreSpacedPerpendicularToDirectionWithTheCentreAtOrigin()
        {
            RaybeamGeometry.BeamOrigins(Vector3.zero, Vector3.forward, Spacing,
                out Vector3 left, out Vector3 centre, out Vector3 right);

            Assert.AreEqual(Vector3.zero, centre);
            Assert.AreEqual(Spacing, Vector3.Distance(left, Vector3.zero), 0.0001f);
            Assert.AreEqual(Spacing, Vector3.Distance(right, Vector3.zero), 0.0001f);
            Assert.AreEqual(Spacing * 2f, Vector3.Distance(left, right), 0.0001f,
                "Left and right must sit on opposite sides of the centre.");

            // Both offsets are perpendicular to Direction (forward), not along it.
            Assert.AreEqual(0f, Vector3.Dot((left - Vector3.zero).normalized, Vector3.forward), 0.0001f);
            Assert.AreEqual(0f, Vector3.Dot((right - Vector3.zero).normalized, Vector3.forward), 0.0001f);
        }

        [Test]
        public void BeamOriginsFollowANonForwardOriginAndDirection()
        {
            Vector3 origin = new Vector3(10f, 2f, -4f);
            RaybeamGeometry.BeamOrigins(origin, Vector3.right, Spacing,
                out Vector3 left, out Vector3 centre, out Vector3 right);

            Assert.AreEqual(origin, centre);
            // Direction is now Vector3.right, so the perpendicular offset must be along Z, not X.
            Assert.AreEqual(origin.x, left.x, 0.0001f);
            Assert.AreEqual(origin.x, right.x, 0.0001f);
            Assert.AreNotEqual(left.z, right.z);
        }

        [Test]
        public void AimFromOriginToPointFallsBackWhenPointCoincidesWithTheBeamsOwnOrigin()
        {
            Vector3 fallback = new Vector3(0f, 0f, 1f);
            Vector3 samePoint = new Vector3(1f, 0f, 1f);

            Vector3 result = RaybeamGeometry.AimFromOriginToPoint(samePoint, samePoint, fallback);

            Assert.AreEqual(fallback.normalized, result);
        }

        [Test]
        public void AtTheConvergencePointAllThreeBeamsCrossIt()
        {
            // Verify scenario 1: cursor placed on the dummy - all three beams land.
            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.forward;
            Vector3 point = new Vector3(0f, 0f, 8f);

            RaybeamGeometry.BeamOrigins(origin, direction, Spacing, out Vector3 left, out Vector3 centre, out Vector3 right);

            Vector3 leftDir = RaybeamGeometry.AimFromOriginToPoint(left, point, direction);
            Vector3 centreDir = RaybeamGeometry.AimFromOriginToPoint(centre, point, direction);
            Vector3 rightDir = RaybeamGeometry.AimFromOriginToPoint(right, point, direction);

            Assert.IsTrue(RaybeamGeometry.BeamCrosses(left, leftDir, Range, Radius, point), "Left beam must reach the convergence point.");
            Assert.IsTrue(RaybeamGeometry.BeamCrosses(centre, centreDir, Range, Radius, point), "Centre beam must reach the convergence point.");
            Assert.IsTrue(RaybeamGeometry.BeamCrosses(right, rightDir, Range, Radius, point), "Right beam must reach the convergence point.");
        }

        [Test]
        public void ThreeBeamsStillConvergeAtTheFarEndOfTheDoubledRange()
        {
            // Rework step 6: beamRange doubled 12 -> 24. At double the depth, the outer beams' own
            // inward angle away from Direction is roughly half what it was at the old range (the
            // same Spacing spread over twice the distance) - the convergence claim this whole file
            // pins is most likely to quietly stop holding right here, so it gets its own test at the
            // new range rather than trusting the old 8m-depth case to still say something meaningful.
            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.forward;
            Vector3 point = new Vector3(0f, 0f, Range);

            RaybeamGeometry.BeamOrigins(origin, direction, Spacing, out Vector3 left, out Vector3 centre, out Vector3 right);

            Vector3 leftDir = RaybeamGeometry.AimFromOriginToPoint(left, point, direction);
            Vector3 centreDir = RaybeamGeometry.AimFromOriginToPoint(centre, point, direction);
            Vector3 rightDir = RaybeamGeometry.AimFromOriginToPoint(right, point, direction);

            Assert.IsTrue(RaybeamGeometry.BeamCrosses(left, leftDir, Range, Radius, point), "Left beam must reach the convergence point at the doubled range.");
            Assert.IsTrue(RaybeamGeometry.BeamCrosses(centre, centreDir, Range, Radius, point), "Centre beam must reach the convergence point at the doubled range.");
            Assert.IsTrue(RaybeamGeometry.BeamCrosses(right, rightDir, Range, Radius, point), "Right beam must reach the convergence point at the doubled range.");
        }

        [Test]
        public void OnlyTheBeamAimedThereCrossesAPointThatSitsOnItsOwnLineButNotTheOthers()
        {
            // Verify scenario 2: cursor a few metres past the dummy, dummy offset laterally so
            // only the beam aimed through it actually crosses - the other two, aimed at the same
            // convergence Point from a different origin, pass nowhere near.
            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.forward;
            Vector3 point = new Vector3(0f, 0f, 8f); // cursor 3m past the dummy's own depth (5m)

            RaybeamGeometry.BeamOrigins(origin, direction, Spacing, out Vector3 left, out Vector3 centre, out Vector3 right);

            Vector3 rightDir = RaybeamGeometry.AimFromOriginToPoint(right, point, direction);
            Vector3 centreDir = RaybeamGeometry.AimFromOriginToPoint(centre, point, direction);
            Vector3 leftDir = RaybeamGeometry.AimFromOriginToPoint(left, point, direction);

            // A point exactly on the right beam's own segment, at 5m of the 8m depth to Point.
            float t = 5f / point.z;
            Vector3 onRightBeamOnly = Vector3.Lerp(right, point, t);

            Assert.IsTrue(RaybeamGeometry.BeamCrosses(right, rightDir, Range, Radius, onRightBeamOnly),
                "The beam aimed through this point must cross it.");
            Assert.IsFalse(RaybeamGeometry.BeamCrosses(centre, centreDir, Range, Radius, onRightBeamOnly),
                "The centre beam, aimed at the same Point from a different origin, must miss.");
            Assert.IsFalse(RaybeamGeometry.BeamCrosses(left, leftDir, Range, Radius, onRightBeamOnly),
                "The opposite outer beam must miss by an even wider margin.");
        }

        [Test]
        public void BeamCrossesReturnsFalseBeyondItsRange()
        {
            Vector3 farTarget = new Vector3(0f, 0f, 30f); // past the rework-step-6 24m range (was 20f past 12m)
            Assert.IsFalse(RaybeamGeometry.BeamCrosses(Vector3.zero, Vector3.forward, Range, Radius, farTarget));
        }

        [Test]
        public void BeamCrossesReturnsFalseBehindTheOrigin()
        {
            Vector3 behindTarget = new Vector3(0f, 0f, -5f);
            Assert.IsFalse(RaybeamGeometry.BeamCrosses(Vector3.zero, Vector3.forward, Range, Radius, behindTarget));
        }

        [Test]
        public void BeamCrossesAcceptsATargetExactlyOnTheAxisAtHalfRange()
        {
            Vector3 onAxis = new Vector3(0f, 0f, Range / 2f); // rework step 6: half of 24, not the old half of 12
            Assert.IsTrue(RaybeamGeometry.BeamCrosses(Vector3.zero, Vector3.forward, Range, Radius, onAxis));
        }
    }
}
