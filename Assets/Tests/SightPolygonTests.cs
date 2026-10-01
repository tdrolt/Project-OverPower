using System;
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    public class SightPolygonTests
    {
        const float Cone = 22f, Radius = 7f, Reveal = 0.75f;
        static readonly SightShape Shape = new SightShape(90f, Cone, Radius);

        static Func<Vector2, Vector2, float, float?> NoWalls => (o, d, max) => null;

        // A wall across the whole x axis at z = wallZ (only seen by rays heading toward +z).
        static Func<Vector2, Vector2, float, float?> WallAt(float wallZ) => (o, d, max) =>
        {
            if (d.y <= 1e-5f) return null;
            float t = (wallZ - o.y) / d.y;
            return t >= 0f && t <= max ? t : (float?)null;
        };

        static List<Vector2> Fan(Eye eye, int rays, Func<Vector2, Vector2, float, float?> cast)
        {
            var into = new List<Vector2> { new Vector2(99, 99) }; // proves the list is cleared first
            SightPolygon.Build(eye, rays, Reveal, cast, into);
            return into;
        }

        static Eye FacingUp(Vector2 at) => new Eye(at, Vector2.up, Shape);

        static int CountAt(List<Vector2> fan, Vector2 centre, float dist) =>
            fan.FindAll(p => Mathf.Abs((p - centre).magnitude - dist) < 1e-3f).Count;

        [Test]
        public void EyeIsTheFirstPoint_AndTheListIsClearedFirst()
        {
            var fan = Fan(FacingUp(new Vector2(3, 4)), 20, NoWalls);
            Assert.AreEqual(new Vector2(3, 4), fan[0]);
        }

        [Test]
        public void NoWalls_HasRayCountPointsAtTheConeLength()
        {
            Vector2 at = new Vector2(3, 4);
            var fan = Fan(FacingUp(at), 20, NoWalls);
            Assert.AreEqual(20, CountAt(fan, at, Cone));
        }

        [Test]
        public void NoWalls_EveryOutlinePointIsAtTheConeLengthOrTheCircleRadius()
        {
            Vector2 at = new Vector2(3, 4);
            var fan = Fan(FacingUp(at), 20, NoWalls);
            for (int i = 1; i < fan.Count; i++)
            {
                float d = (fan[i] - at).magnitude;
                Assert.IsTrue(Mathf.Abs(d - Cone) < 1e-3f || Mathf.Abs(d - Radius) < 1e-3f, $"point {i} at {d}");
            }
        }

        [Test]
        public void NoWalls_TheConeSpansTheConeAngleAroundTheFacing()
        {
            var fan = Fan(FacingUp(Vector2.zero), 20, NoWalls);
            float maxAngle = 0f;
            foreach (Vector2 p in fan.FindAll(p => Mathf.Abs(p.magnitude - Cone) < 1e-3f))
                maxAngle = Mathf.Max(maxAngle, Vector2.Angle(Vector2.up, p));
            Assert.AreEqual(45f, maxAngle, 0.01f);
        }

        [Test]
        public void TheCircleCoversBehindTheEye()
        {
            var fan = Fan(FacingUp(Vector2.zero), 20, NoWalls);
            Assert.IsTrue(fan.Exists(p => p.y < -Radius * 0.99f && Mathf.Abs(p.magnitude - Radius) < 1e-3f));
            Assert.IsTrue(fan.Exists(p => p.x > Radius * 0.99f - 0.5f)); // and to the side
        }

        // The rule: one ray spacing, in metres along the cone's far edge, for the cone and the circle both.
        static int ExpectedCircleRays(float coneDegrees, float coneLength, float radius, int rayCount)
        {
            float spacing = coneLength * coneDegrees * Mathf.Deg2Rad / rayCount;
            float rest = (360f - coneDegrees) * Mathf.Deg2Rad;
            return Mathf.Min(rayCount, Mathf.CeilToInt(radius * rest / spacing));
        }

        [Test]
        public void TheCircleRaysFollowTheConeSpacing_UpToTheRayCount()
        {
            const int rays = 180;
            var fan = Fan(FacingUp(Vector2.zero), rays, NoWalls);
            Assert.AreEqual(ExpectedCircleRays(90f, Cone, Radius, rays), CountAt(fan, Vector2.zero, Radius));
        }

        [Test]
        public void ANarrowScopedCone_DoesNotMultiplyTheCirclesRays()
        {
            const int rays = 20;
            var eye = new Eye(Vector2.zero, Vector2.up, new SightShape(10f, Cone, Radius));
            var fan = Fan(eye, rays, NoWalls);
            Assert.LessOrEqual(CountAt(fan, Vector2.zero, Radius), rays);
        }

        [Test]
        public void TheOutlineGoesRoundTheEyeInAngularOrder()
        {
            var fan = Fan(FacingUp(new Vector2(2, 3)), 40, NoWalls);
            Vector2 eye = fan[0];
            float total = 0f;
            for (int i = 1; i < fan.Count; i++)
            {
                Vector2 a = fan[i] - eye;
                Vector2 b = fan[i == fan.Count - 1 ? 1 : i + 1] - eye;
                float step = Mathf.DeltaAngle(Mathf.Atan2(a.y, a.x) * Mathf.Rad2Deg, Mathf.Atan2(b.y, b.x) * Mathf.Rad2Deg);
                Assert.GreaterOrEqual(step, -1e-3f, $"outline point {i} goes backwards"); // a shared end point may repeat (0)
                total += step;
            }
            Assert.AreEqual(360f, total, 0.01f);
        }

        [Test]
        public void AWallAhead_EndsTheMiddleConeRaysAtTheWallPlusRevealDepth()
        {
            var fan = Fan(FacingUp(Vector2.zero), 21, WallAt(10f));
            Vector2 middle = fan.Find(p => Mathf.Abs(p.x) < 1e-3f && p.y > Radius + 0.5f);
            Assert.AreEqual(10f + Reveal, middle.y, 1e-3f);
        }

        [Test]
        public void AWallAhead_LeavesRaysThatMissItAtFullLength()
        {
            // The wall only exists to the right of x = 5: left rays are untouched.
            Func<Vector2, Vector2, float, float?> half = (o, d, max) =>
            {
                float? t = WallAt(10f)(o, d, max);
                return t.HasValue && (o.x + d.x * t.Value) > 5f ? t : null;
            };
            var fan = Fan(FacingUp(Vector2.zero), 21, half);
            Assert.IsTrue(fan.Exists(p => Mathf.Abs(p.magnitude - Cone) < 1e-3f && p.x < -10f));
        }

        [Test]
        public void ARevealPastTheConeLength_IsCappedAtTheConeLength()
        {
            var fan = Fan(FacingUp(Vector2.zero), 21, WallAt(Cone - 0.1f));
            Vector2 middle = fan.Find(p => Mathf.Abs(p.x) < 1e-3f && p.y > Radius + 0.5f);
            Assert.AreEqual(Cone, middle.y, 1e-3f);
        }

        [Test]
        public void AWallCutsTheCircleToo()
        {
            // A wall behind the eye at z = -3: circle points straight behind end at 3 + reveal depth.
            Func<Vector2, Vector2, float, float?> behind = (o, d, max) =>
            {
                if (d.y >= -1e-5f) return null;
                float t = (-3f - o.y) / d.y;
                return t <= max ? t : (float?)null;
            };
            var fan = Fan(FacingUp(Vector2.zero), 21, behind);
            Vector2 back = fan.Find(p => Mathf.Abs(p.x) < 0.05f && p.y < -1f);
            Assert.AreEqual(-(3f + Reveal), back.y, 0.02f);
        }

        [Test]
        public void AZeroFacing_GivesOnlyCirclePoints()
        {
            var eye = new Eye(Vector2.zero, Vector2.zero, Shape);
            var fan = Fan(eye, 20, NoWalls);
            Assert.AreEqual(new Vector2(0, 0), fan[0]);
            for (int i = 1; i < fan.Count; i++)
                Assert.AreEqual(Radius, fan[i].magnitude, 1e-3f);
            Assert.Greater(fan.Count, 40);
        }

        [Test]
        public void ACircleLargerThanTheCone_TakesTheLarger()
        {
            var eye = new Eye(Vector2.zero, Vector2.up, new SightShape(90f, 5f, 8f));
            var fan = Fan(eye, 20, NoWalls);
            Assert.AreEqual(0, fan.FindAll(p => p.magnitude > 8f + 1e-3f).Count);
            Assert.IsTrue(fan.Exists(p => p.y > 7.9f));
        }

        [Test]
        public void TheRaycastGetsTheEyeAndAUnitDirection()
        {
            var seen = new List<Vector2>();
            SightPolygon.Build(FacingUp(new Vector2(1, 2)), 5, Reveal, (o, d, max) =>
            {
                Assert.AreEqual(new Vector2(1, 2), o);
                seen.Add(d);
                return null;
            }, new List<Vector2>());
            Assert.Greater(seen.Count, 5);
            foreach (Vector2 d in seen) Assert.AreEqual(1f, d.magnitude, 1e-4f);
        }
    }
}
