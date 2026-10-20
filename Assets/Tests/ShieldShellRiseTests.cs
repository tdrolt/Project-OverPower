using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>When the Shield triggers, every plate snaps up from the floor ring under it to its place on the shell, the lowest plates
    /// first. The numbers here are made-up shapes, not the tuned ones.</summary>
    public class ShieldShellRiseTests
    {
        private const float RingRadius = 0.6f;
        private const float RingY = 0.05f;
        private const float Stagger = 0.4f;

        [Test]
        public void PlatesStartOnTheRingAndEndAtRest()
        {
            var centres = new[] { new Vector3(0.3f, 0.35f, 0.1f), new Vector3(-0.2f, -0.4f, 0.2f), new Vector3(0f, 0.1f, -0.45f) };
            foreach (Vector3 c in centres)
            {
                Vector3 start = c + ShieldShellRise.PlateOffset(c, 0f, RingRadius, RingY, Stagger);
                Assert.AreEqual(RingY, start.y, 1e-4f, "starts at the ring's height");
                Assert.AreEqual(RingRadius, new Vector2(start.x, start.z).magnitude, 1e-4f, "starts on the ring");
                Vector2 dirStart = new Vector2(start.x, start.z).normalized, dirRest = new Vector2(c.x, c.z).normalized;
                Assert.Greater(Vector2.Dot(dirStart, dirRest), 0.999f, "starts on the ring point under its own rest place");

                Assert.Less(ShieldShellRise.PlateOffset(c, 1f, RingRadius, RingY, Stagger).magnitude, 1e-5f, "at rest when the rise ends");
                Assert.Less(ShieldShellRise.PlateOffset(c, 3f, RingRadius, RingY, Stagger).magnitude, 1e-5f, "stays at rest after the rise");
            }
        }

        [Test]
        public void ThePlateStraightAboveStartsAtTheRingCentre()
        {
            Vector3 top = new Vector3(0f, 0.5f, 0f);
            Vector3 start = top + ShieldShellRise.PlateOffset(top, 0f, RingRadius, RingY, Stagger);
            Assert.Less((start - new Vector3(0f, RingY, 0f)).magnitude, 1e-4f);
        }

        [Test]
        public void LowerPlatesArriveFirst()
        {
            Vector3 low = new Vector3(0.45f, -0.2f, 0f), high = new Vector3(0.3f, 0.4f, 0f);
            float lowArrives = ArrivalTime(low), highArrives = ArrivalTime(high);
            Assert.Less(lowArrives, highArrives, "the lowest plates lead");
            Assert.Less(highArrives, 1.0001f, "every plate is in place when the rise ends");

            float mid = 0.5f;
            float lowLeft = ShieldShellRise.PlateOffset(low, mid, RingRadius, RingY, Stagger).magnitude / ShieldShellRise.PlateOffset(low, 0f, RingRadius, RingY, Stagger).magnitude;
            float highLeft = ShieldShellRise.PlateOffset(high, mid, RingRadius, RingY, Stagger).magnitude / ShieldShellRise.PlateOffset(high, 0f, RingRadius, RingY, Stagger).magnitude;
            Assert.Less(lowLeft, highLeft, "half-way through, the low plate has less of its way left");
        }

        [Test]
        public void NoStaggerMovesEveryPlateTogether()
        {
            Vector3 low = new Vector3(0.45f, -0.2f, 0f), high = new Vector3(0.3f, 0.4f, 0f);
            float lowLeft = ShieldShellRise.PlateOffset(low, 0.5f, RingRadius, RingY, 0f).magnitude / ShieldShellRise.PlateOffset(low, 0f, RingRadius, RingY, 0f).magnitude;
            float highLeft = ShieldShellRise.PlateOffset(high, 0.5f, RingRadius, RingY, 0f).magnitude / ShieldShellRise.PlateOffset(high, 0f, RingRadius, RingY, 0f).magnitude;
            Assert.Greater(lowLeft, 0.01f, "half-way through, both plates are still moving");
            Assert.AreEqual(lowLeft, highLeft, 1e-4f);
        }

        private static float ArrivalTime(Vector3 centre)
        {
            for (int i = 0; i <= 1000; i++)
            {
                float t = i / 1000f;
                if (ShieldShellRise.PlateOffset(centre, t, RingRadius, RingY, Stagger).magnitude < 1e-5f)
                    return t;
            }
            return float.PositiveInfinity;
        }
    }
}
