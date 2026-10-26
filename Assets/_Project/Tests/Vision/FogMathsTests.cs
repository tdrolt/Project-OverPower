using NUnit.Framework;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    public class FogMathsTests
    {
        // A 100 m square whose lower corner is at world (-30, 10): x -30..70, z 10..110.
        static readonly Vector4 Rect = new Vector4(-30f, 10f, 100f, 100f);

        [Test]
        public void TheLowerCornerMapsToZero_AndTheUpperCornerToOne()
        {
            Assert.AreEqual(Vector2.zero, FogMaths.WorldToSightUv(new Vector3(-30f, 5f, 10f), Rect));
            Assert.AreEqual(Vector2.one, FogMaths.WorldToSightUv(new Vector3(70f, 5f, 110f), Rect));
        }

        [Test]
        public void TheMiddleMapsToHalf_AndHeightIsIgnored()
        {
            Vector2 uv = FogMaths.WorldToSightUv(new Vector3(20f, 123f, 60f), Rect);
            Assert.AreEqual(0.5f, uv.x, 1e-5f);
            Assert.AreEqual(0.5f, uv.y, 1e-5f);
        }

        [Test]
        public void ARectWithDifferentSides_ScalesEachAxisOnItsOwn()
        {
            Vector2 uv = FogMaths.WorldToSightUv(new Vector3(10f, 0f, 5f), new Vector4(0f, 0f, 20f, 10f));
            Assert.AreEqual(0.5f, uv.x, 1e-5f);
            Assert.AreEqual(0.5f, uv.y, 1e-5f);
        }

        [Test]
        public void PointsOnTheEdgesAreInside_AndPointsBeyondAreOutside()
        {
            Assert.IsTrue(FogMaths.IsInsideRect(new Vector3(-30f, 0f, 10f), Rect));
            Assert.IsTrue(FogMaths.IsInsideRect(new Vector3(70f, 0f, 110f), Rect));
            Assert.IsFalse(FogMaths.IsInsideRect(new Vector3(-30.01f, 0f, 60f), Rect));
            Assert.IsFalse(FogMaths.IsInsideRect(new Vector3(20f, 0f, 110.01f), Rect));
        }

        [Test]
        public void AnEmptyRectHasNothingInsideIt()
        {
            Assert.IsFalse(FogMaths.IsInsideRect(Vector3.zero, Vector4.zero));
        }
    }
}
