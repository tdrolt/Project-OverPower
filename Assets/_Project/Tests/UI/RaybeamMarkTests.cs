using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.TestRange;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Raybeam's own mark: red spikes round the spot where the Mark gun's pink diamond sits, over an enemy the local player's
    /// beam left vulnerable. The spikes keep clear of the diamond whatever the sizes, so on one enemy both marks show whole.</summary>
    public class RaybeamMarkTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private static readonly int[] Counts = { 3, 6, 8, 12 };
        private static readonly float[] Widths = { 0.2f, 0.5f, 1f };
        private static readonly float[] DiamondSizes = { 10f, 22f, 30f };
        private static readonly float[] Gaps = { 0f, 2f, 6f };

        [Test]
        public void OnlyAVulnerabilityThatLandsOnAnUnshieldedEnemyShowsTheMark()
        {
            Assert.IsTrue(RaybeamMarkRule.ShowsOnEnemy(StatusKind.Vulnerability, true, false), "my beam left an enemy vulnerable");
            Assert.IsFalse(RaybeamMarkRule.ShowsOnEnemy(StatusKind.Vulnerability, true, true), "their respawn shield stopped it");
            Assert.IsFalse(RaybeamMarkRule.ShowsOnEnemy(StatusKind.Vulnerability, false, false), "not mine, not an enemy, or already dead");
            foreach (StatusKind other in new[] { StatusKind.Burn, StatusKind.Slow, StatusKind.Stun, StatusKind.Invulnerability })
                Assert.IsFalse(RaybeamMarkRule.ShowsOnEnemy(other, true, false), other + " is not a vulnerability");
        }

        [Test]
        public void TheSpikesKeepClearOfTheDiamondSoBothMarksShowWhole()
        {
            foreach (float size in DiamondSizes)
            foreach (float gap in Gaps)
            foreach (int count in Counts)
            foreach (float width in Widths)
            {
                float corner = size * Mathf.Sqrt(2f) * 0.5f; // the diamond is a square turned 45 degrees: its points are this far out
                float inner = RaybeamMarkRule.InnerRadius(size, gap, count, width);
                for (int i = 0; i < count; i++)
                {
                    RaybeamMarkRule.Spike(i, count, inner, 12f, width, out Vector2 left, out Vector2 tip, out Vector2 right);
                    string at = $"size {size} gap {gap} count {count} width {width} spike {i}";
                    Assert.GreaterOrEqual(DistanceToSegment(left, right), corner + gap - 1e-3f, "the spike's base stays the gap clear of the diamond: " + at);
                    Assert.GreaterOrEqual(DistanceToSegment(left, tip), corner + gap - 1e-3f, at);
                    Assert.GreaterOrEqual(DistanceToSegment(right, tip), corner + gap - 1e-3f, at);
                    Assert.AreEqual(inner + 12f, tip.magnitude, 1e-3f, "the tip sits the spike's length past the ring: " + at);
                }
            }
        }

        [Test]
        public void TheSpikesFanOutEvenlyClockwiseFromStraightUp()
        {
            const int count = 8;
            const float inner = 20f, length = 12f, width = 0.5f;
            RaybeamMarkRule.Spike(0, count, inner, length, width, out Vector2 l0, out Vector2 t0, out Vector2 r0);
            Assert.AreEqual(0f, t0.x, 1e-3f, "the first spike points straight up");
            Assert.AreEqual(inner + length, t0.y, 1e-3f);

            float step = 360f / count;
            for (int i = 0; i < count; i++)
            {
                RaybeamMarkRule.Spike(i, count, inner, length, width, out Vector2 left, out Vector2 tip, out Vector2 right);
                RaybeamMarkRule.Spike((i + 1) % count, count, inner, length, width, out _, out Vector2 next, out _);
                Assert.AreEqual(-step, Vector2.SignedAngle(tip, next), 1e-2f, "each next spike is one even step clockwise");
                Assert.AreEqual(step * width, Vector2.Angle(left, right), 1e-2f, "the base fills its width's share of the spike's slice");
                Assert.AreEqual(inner, left.magnitude, 1e-3f, "the base sits on the ring");
                Assert.AreEqual(inner, right.magnitude, 1e-3f);
            }
        }

        [Test]
        public void SpikesNeverOverlapEachOtherWhateverTheWidth()
        {
            RaybeamMarkRule.Spike(0, 6, 20f, 10f, 3f, out Vector2 left, out _, out Vector2 right);
            Assert.AreEqual(60f, Vector2.Angle(left, right), 1e-2f, "a width past 1 is held at 1: bases meet, never cross");
        }

        [Test]
        public void TheSpikeRingCanDrawOnACanvas()
        {
            var go = new GameObject("spike ring", typeof(RectTransform));
            try
            {
                go.AddComponent<SpikeRingGraphic>();
                Assert.IsNotNull(go.GetComponent<CanvasRenderer>(), "a graphic draws through a CanvasRenderer; without one nothing shows");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- the game calls the rule ----

        [Test]
        public void TheAttackersCopyOfAPlayerOrADummyRaisesTheMarkOnTheRulesAnswer()
        {
            MethodInfo shows = typeof(RaybeamMarkRule).GetMethod(nameof(RaybeamMarkRule.ShowsOnEnemy));
            MethodInfo raise = typeof(CombatEvents).GetMethod(nameof(CombatEvents.RaiseVulnerabilitySeen));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerStatusEffects), nameof(PlayerStatusEffects.Apply), shows), "a player's copy asks the rule");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerStatusEffects), nameof(PlayerStatusEffects.Apply), raise), "and raises the mark");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DummyTarget), nameof(DummyTarget.ApplyStatus), shows), "a test dummy asks the rule");
            Assert.IsTrue(IlWiring.Uses(typeof(DummyTarget), nameof(DummyTarget.ApplyStatus), raise), "and raises the mark");
        }

        [Test]
        public void TheHudDrawsRedSpikesSizedFromTheDiamondAndTheViewListens()
        {
            System.Type hud = typeof(PlayerHud);
            MethodInfo build = hud.GetMethod("BuildRaybeamMark", Any);
            Assert.IsNotNull(build, "the HUD has a builder for the spikes");
            Assert.IsTrue(IlWiring.Uses(hud, "BuildHitFeedbackCanvas", build), "the hit canvas builds them");
            Assert.IsTrue(IlWiring.Uses(hud, "BuildRaybeamMark", typeof(UiTheme).GetField(nameof(UiTheme.dangerColor))), "the Raybeam's red");
            Assert.IsTrue(IlWiring.Uses(hud, "BuildRaybeamMark", typeof(UiTheme).GetField(nameof(UiTheme.markIndicatorSize))), "the hole is sized from the diamond");
            Assert.IsTrue(IlWiring.Uses(hud, "BuildRaybeamMark", typeof(RaybeamMarkRule).GetMethod(nameof(RaybeamMarkRule.InnerRadius))), "through the rule");

            System.Type ring = typeof(UiTheme).Assembly.GetType("Overpower.UI.SpikeRingGraphic");
            Assert.IsNotNull(ring, "the spikes are their own graphic");
            Assert.IsTrue(IlWiring.Uses(ring, "OnPopulateMesh", typeof(RaybeamMarkRule).GetMethod(nameof(RaybeamMarkRule.Spike))), "drawn from the rule's spikes");

            Assert.IsTrue(IlWiring.Uses(typeof(MarkIndicatorView), "OnEnable", typeof(CombatEvents).GetEvent(nameof(CombatEvents.LocalVulnerabilitySeen)).GetAddMethod()),
                "the mark view listens for the Raybeam's mark");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MarkIndicatorView), "LateUpdate", typeof(MarkIndicatorRule).GetMethod(nameof(MarkIndicatorRule.Drops))),
                "a mark is dropped on the rule's answer");
        }

        private static float DistanceToSegment(Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t).magnitude;
        }
    }
}
