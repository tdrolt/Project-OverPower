using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>When a mech flashes white for a hit: a hit that lands on a living enemy, never one the victim's client turns away, never
    /// damage over time; the flash is full white from the hit to the end of its length, then gone, and never shows on a dead player.</summary>
    public class HitFlashRuleTests
    {
        private static bool Flashes(DamageSource source = DamageSource.Projectile, float amount = 10f, bool alive = true, bool self = false,
                                    bool teammate = false, bool immune = false, bool shielded = false) =>
            HitFlashRule.FlashesOnHit(source, amount, alive, self, teammate, immune, shielded);

        [Test]
        public void AHitOnALivingEnemyFlashes()
        {
            Assert.IsTrue(Flashes(DamageSource.Projectile), "a bullet");
            Assert.IsTrue(Flashes(DamageSource.Splash), "a rocket or mine blast");
            Assert.IsTrue(Flashes(DamageSource.Contact), "a contact hit");
        }

        [Test]
        public void AHitTheVictimTurnsAwayDoesNotFlash()
        {
            Assert.IsFalse(Flashes(self: true), "your own blast never hurts you");
            Assert.IsFalse(Flashes(teammate: true), "friendly fire does nothing");
            Assert.IsFalse(Flashes(immune: true), "the yellow immunity stops the hit");
            Assert.IsFalse(Flashes(shielded: true), "the respawn shield stops the hit");
            Assert.IsFalse(Flashes(amount: 0f), "a shot that does no damage");
        }

        [Test]
        public void ADeadPlayerDoesNotFlash() => Assert.IsFalse(Flashes(alive: false));

        [Test]
        public void DamageOverTimeDoesNotFlash()
        {
            Assert.IsFalse(Flashes(DamageSource.Burn), "a burn tick: the flames already show it");
            Assert.IsFalse(Flashes(DamageSource.Zone), "a fire field or fence tick");
            Assert.IsFalse(Flashes(DamageSource.SuddenDeath), "the sudden-death circle");
        }

        [Test]
        public void TheFlashHoldsFullForItsLengthThenEnds()
        {
            // A dimming white over the plain white glow map is an even grey with the team's glow lines gone, so the flash never dims.
            Assert.AreEqual(1f, HitFlashRule.Strength(0f, 0.1f), 1e-5f, "full at the hit");
            Assert.AreEqual(1f, HitFlashRule.Strength(0.05f, 0.1f), 1e-5f, "still full half-way");
            Assert.AreEqual(1f, HitFlashRule.Strength(0.099f, 0.1f), 1e-5f, "and just before the end");
            Assert.AreEqual(0f, HitFlashRule.Strength(0.1f, 0.1f), "gone at the end");
            Assert.AreEqual(0f, HitFlashRule.Strength(5f, 0.1f), "and stays gone");
        }

        [Test]
        public void NoLengthNoFlash()
        {
            Assert.AreEqual(0f, HitFlashRule.Strength(0f, 0f), "a zero-length flash is switched off");
            Assert.AreEqual(0f, HitFlashRule.Strength(float.PositiveInfinity, 0.1f), "never hit");
        }

        [Test]
        public void TheFlashShowsOnlyOnALivingPlayer()
        {
            Assert.IsTrue(HitFlashRule.Shows(true, 0.5f));
            Assert.IsFalse(HitFlashRule.Shows(false, 1f), "dying mid-flash ends it");
            Assert.IsFalse(HitFlashRule.Shows(true, 0f), "over");
        }
    }
}
