using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The arena outline rule (movement step 3), on a toy arena: an equilateral triangle with circumradius 10 (so its
    /// inradius is 5), whose Source third contributes one edge midpoint and the top vertex. Everything else is the
    /// 120 and 240 degree turns of that, exactly as the real arena is built.
    /// </summary>
    public class ArenaBoundsTests
    {
        // Not at the origin, so nothing can secretly rely on a centre at world zero.
        private static readonly Vector3 Centre = new Vector3(10f, 0f, 20f);
        private static readonly Vector2 Outward30 = new Vector2(0.8660254f, 0.5f);  // (x, z) at map angle 30 degrees
        private static readonly Vector2 Along30 = new Vector2(-0.5f, 0.8660254f);   // along that edge, toward 90 degrees

        // A point on the right edge's own frame: s metres along the edge from its midpoint, d metres out from the centre.
        private static Vector2 P(float s, float d) => new Vector2(Centre.x, Centre.z) + Outward30 * d + Along30 * s;

        private static Vector3 World(Vector2 xz) => new Vector3(xz.x, 0.5f, xz.y);

        private static ArenaBounds Triangle() => ArenaBounds.FromSourceOutline(
            new List<Vector2> { P(0f, 5f), new Vector2(Centre.x, Centre.z + 10f) }, Centre);

        // The same triangle with a 2 m wide, 2 m deep pocket in the right edge, like a flank pocket.
        private static ArenaBounds TriangleWithPocket() => ArenaBounds.FromSourceOutline(
            new List<Vector2> { P(0f, 5f), P(2f, 5f), P(2f, 7f), P(4f, 7f), P(4f, 5f), new Vector2(Centre.x, Centre.z + 10f) },
            Centre);

        [Test]
        public void TheCentreIsInsideByTheInradius() =>
            Assert.AreEqual(5f, Triangle().SignedDistance(World(new Vector2(Centre.x, Centre.z))), 1e-3f);

        [Test]
        public void APointJustInsideAnEdgeIsInsideWithoutRoomForAPlayer()
        {
            ArenaBounds bounds = Triangle();
            Vector3 hugging = World(P(1f, 4.5f));
            Assert.AreEqual(0.5f, bounds.SignedDistance(hugging), 1e-3f);
            Assert.IsTrue(bounds.Contains(hugging, 0f));
            Assert.IsFalse(bounds.Contains(hugging, 0.7f));
        }

        [Test]
        public void APointPastAnEdgeIsOutsideByItsDistance() =>
            Assert.AreEqual(-1f, Triangle().SignedDistance(World(P(1f, 6f))), 1e-3f);

        [Test]
        public void TheOtherThirdsAreTheSourceTurned()
        {
            // The bottom edge (map angle 270) is not in the Source outline at all: only its turned copies make it.
            ArenaBounds bounds = Triangle();
            Assert.AreEqual(1f, bounds.SignedDistance(World(new Vector2(Centre.x, Centre.z - 4f))), 1e-3f);
            Assert.AreEqual(-1f, bounds.SignedDistance(World(new Vector2(Centre.x, Centre.z - 6f))), 1e-3f);
        }

        [Test]
        public void ThePolygonIsTheSourcePointsThenTheir120And240Turns()
        {
            IReadOnlyList<Vector2> polygon = Triangle().Polygon;
            Assert.AreEqual(6, polygon.Count);
            Assert.AreEqual(Centre.x - 4.330127f, polygon[2].x, 1e-3f);  // the 30 degree midpoint turned to 150
            Assert.AreEqual(Centre.z + 2.5f, polygon[2].y, 1e-3f);
            Assert.AreEqual(Centre.x, polygon[4].x, 1e-3f);              // and turned to 270
            Assert.AreEqual(Centre.z - 5f, polygon[4].y, 1e-3f);
        }

        [Test]
        public void InsideAPocketIsInsideAndBesideItIsOutside()
        {
            ArenaBounds bounds = TriangleWithPocket();
            Assert.AreEqual(1f, bounds.SignedDistance(World(P(3f, 6f))), 1e-3f);
            Assert.AreEqual(-1f, bounds.SignedDistance(World(P(1f, 6f))), 1e-3f);
        }

        [Test]
        public void AnOutlineWithFewerThanTwoPointsIsNoBoundsAtAll()
        {
            Assert.IsNull(ArenaBounds.FromSourceOutline(new List<Vector2> { P(0f, 5f) }, Centre));
            Assert.IsNull(ArenaBounds.FromSourceOutline(null, Centre));
        }
    }
}
