using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>When a player counts as being healed: a rise in the health every client already has, while alive before and after, never
    /// a respawn's or a fresh round's jump back to full; it lasts the tail after the last rise. And how the green plus signs rise.</summary>
    public class HealRuleTests
    {
        private const float Jump = 5f;

        [Test]
        public void ARiseWhileAliveIsAHeal()
        {
            Assert.IsTrue(HealRule.IsHealRise(50f, 50.2f, true, true, 0.1f, Jump), "zone regeneration: a small rise each frame");
            Assert.IsTrue(HealRule.IsHealRise(40f, 90f, true, true, 0f, Jump), "a health pack: one big rise");
            Assert.IsTrue(HealRule.IsHealRise(1f, 2f, true, true, 4.9f, Jump), "a quick move between two updates is still a heal");
        }

        [Test]
        public void NoRiseIsNoHeal()
        {
            Assert.IsFalse(HealRule.IsHealRise(50f, 50f, true, true, 0f, Jump), "standing still");
            Assert.IsFalse(HealRule.IsHealRise(50f, 30f, true, true, 0f, Jump), "taking damage");
            Assert.IsFalse(HealRule.IsHealRise(50f, 50.00001f, true, true, 0f, Jump), "float noise is not healing");
        }

        [Test]
        public void ARespawnOrAFreshStartIsNotAHeal()
        {
            Assert.IsFalse(HealRule.IsHealRise(0f, 100f, false, true, 0f, Jump), "coming back to life: the player was dead");
            Assert.IsFalse(HealRule.IsHealRise(0f, 100f, true, true, 0f, Jump), "the alive flag arrived before the health: a dead player's health is no living health");
            Assert.IsFalse(HealRule.IsHealRise(35f, 100f, true, true, 40f, Jump), "a new round puts a hurt player back at the spawn, full: the body jumped");
            Assert.IsFalse(HealRule.IsHealRise(50f, 60f, true, false, 0f, Jump), "nothing on a dead player");
            Assert.IsFalse(HealRule.IsHealRise(50f, 60f, false, false, 0f, Jump));
        }

        [Test]
        public void HealingLastsTheTailAfterTheLastRise()
        {
            Assert.IsTrue(HealRule.IsHealing(true, 0f, 0.9f), "rising right now");
            Assert.IsTrue(HealRule.IsHealing(true, 0.85f, 0.9f), "between two health updates");
            Assert.IsFalse(HealRule.IsHealing(true, 0.95f, 0.9f), "the healing stopped");
            Assert.IsFalse(HealRule.IsHealing(false, 0f, 0.9f), "hidden at once when the player dies");
            Assert.IsFalse(HealRule.IsHealing(true, float.PositiveInfinity, 0.9f), "never healed");
        }

        [Test]
        public void SparksRiseRoundTheBodyAndFadeAtBothEnds()
        {
            const int count = 7;
            const float rise = 1f, radius = 0.7f, start = 0.3f, metres = 2f;
            for (int i = 0; i < count; i++)
            {
                float lastY = float.NegativeInfinity;
                for (int step = 1; step < 10; step++)
                {
                    float t = step * 0.1f - (float)i / count * rise + 7f; // one rise, from just after the spark appears
                    HealRule.PlaceSpark(i, count, t, rise, radius, start, metres, out Vector3 p, out float strength);
                    Assert.AreEqual(radius, new Vector2(p.x, p.z).magnitude, 1e-4f, $"spark {i}: on the ring round the body");
                    Assert.That(p.y, Is.InRange(start - 1e-4f, start + metres + 1e-4f), $"spark {i}: between its start and its highest point");
                    Assert.Greater(p.y, lastY, $"spark {i}: always rising during one rise");
                    Assert.That(strength, Is.InRange(0f, 1f));
                    lastY = p.y;
                }
            }

            HealRule.PlaceSpark(0, count, 10f, rise, radius, start, metres, out Vector3 born, out float atBirth);
            HealRule.PlaceSpark(0, count, 10.5f, rise, radius, start, metres, out _, out float halfway);
            Assert.AreEqual(start, born.y, 1e-3f, "a spark is born at its start height");
            Assert.Less(atBirth, 0.05f, "and grows in rather than popping");
            Assert.Greater(halfway, 0.9f, "full halfway up");

            HealRule.PlaceSpark(0, count, 10.3f, rise, radius, start, metres, out Vector3 a, out _);
            HealRule.PlaceSpark(1, count, 10.3f, rise, radius, start, metres, out Vector3 b, out _);
            Assert.Greater(Vector3.Distance(a, b), 0.3f, "two sparks never sit on top of each other");
        }
    }
}
