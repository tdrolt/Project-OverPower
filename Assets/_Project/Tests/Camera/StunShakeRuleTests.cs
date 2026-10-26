using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A stun gives the stunned player's own camera a short jolt that settles to still, on nobody else's screen, and only while the
    /// designer leaves it on.</summary>
    public class StunShakeRuleTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void OnlyAStunOnMyOwnPlayerShakesMyCamera()
        {
            Assert.IsTrue(StunShakeRule.ShakesOnApply(StatusKind.Stun, true, true, 0.1f));
            Assert.IsFalse(StunShakeRule.ShakesOnApply(StatusKind.Stun, false, true, 0.1f), "someone else's stun never moves my camera");
            Assert.IsFalse(StunShakeRule.ShakesOnApply(StatusKind.Stun, true, false, 0.1f), "switched off in the Arena Look");
            Assert.IsFalse(StunShakeRule.ShakesOnApply(StatusKind.Stun, true, true, 0f), "no strength, no shake");
            foreach (StatusKind other in new[] { StatusKind.Burn, StatusKind.Slow, StatusKind.Vulnerability, StatusKind.Invulnerability })
                Assert.IsFalse(StunShakeRule.ShakesOnApply(other, true, true, 0.1f), other + " does not shake the camera");
        }

        [Test]
        public void TheJoltIsStrongestAtTheStartAndSettlesToStill()
        {
            const float seconds = 0.4f, strength = 0.2f;
            Assert.AreEqual(strength, StunShakeRule.Strength(0f, seconds, strength), 1e-5f, "full strength the moment the stun lands");
            float before = StunShakeRule.Strength(0f, seconds, strength);
            for (float t = 0.02f; t < seconds; t += 0.02f)
            {
                float now = StunShakeRule.Strength(t, seconds, strength);
                Assert.Less(now, before, $"weaker at {t:0.00} s than a moment before");
                Assert.Greater(now, 0f, "still shaking before the end");
                before = now;
            }
            Assert.AreEqual(0f, StunShakeRule.Strength(seconds, seconds, strength), "still once the shake is over");
            Assert.AreEqual(0f, StunShakeRule.Strength(seconds + 1f, seconds, strength));
            Assert.AreEqual(0f, StunShakeRule.Strength(-0.01f, seconds, strength), "nothing before it starts");
        }

        [Test]
        public void TheCameraMovesByTheJoltsStrengthAndKeepsJumping()
        {
            const float seconds = 0.4f, strength = 0.2f, frequency = 20f;
            Vector2 last = StunShakeRule.Offset(0f, seconds, strength, frequency);
            int moves = 0;
            for (float t = 0f; t < seconds; t += 1f / frequency)
            {
                Vector2 o = StunShakeRule.Offset(t, seconds, strength, frequency);
                Assert.AreEqual(StunShakeRule.Strength(t, seconds, strength), o.magnitude, 1e-4f, $"at {t:0.00} s the camera is off by the jolt's strength");
                if (Vector2.Angle(o, last) > 30f)
                    moves++;
                last = o;
            }
            Assert.GreaterOrEqual(moves, 5, "each step jumps the other way, so it reads as a shake, not a slide");
            Assert.AreEqual(Vector2.zero, StunShakeRule.Offset(seconds, seconds, strength, frequency), "back in place when it ends");
            Assert.AreEqual(StunShakeRule.Offset(0.13f, seconds, strength, frequency), StunShakeRule.Offset(0.13f, seconds, strength, frequency), "the same moment, the same spot");
        }

        [Test]
        public void TheStunnedPlayersOwnClientShakesItsCameraOnTheRulesAnswer()
        {
            MethodInfo shakes = typeof(StunShakeRule).GetMethod(nameof(StunShakeRule.ShakesOnApply));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerStatusEffects), nameof(PlayerStatusEffects.Apply), shakes),
                "applying a status asks the rule and acts on its answer");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerStatusEffects), nameof(PlayerStatusEffects.Apply), typeof(CameraTracking).GetMethod(nameof(CameraTracking.Shake))),
                "and shakes the local camera");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerStatusEffects), nameof(PlayerStatusEffects.Apply),
                typeof(Overpower.Arena.ArenaLook).GetField(nameof(Overpower.Arena.ArenaLook.stunShakeOn))), "the switch comes from the Arena Look");

            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "LateUpdate", typeof(StunShakeRule).GetMethod(nameof(StunShakeRule.Offset))),
                "the camera's own placement adds the rule's offset every frame");
            Assert.IsTrue(IlWiring.Stores(typeof(CameraTracking), nameof(CameraTracking.Shake), typeof(CameraTracking).GetField("shakeStartedAt", Any)),
                "a shake remembers when it started");
        }
    }
}
