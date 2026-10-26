using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>When a mech flashes white for a hit: any damage that lands on a living player, never a hit the victim's client turns away;
    /// damage over time starts a flash at most once per gap, so a burning mech flickers instead of staying white; the flash is full white
    /// from the hit to the end of its length, then gone, and never shows on a dead player.</summary>
    public class HitFlashRuleTests
    {
        private static bool Flashes(DamageSource source = DamageSource.Projectile, float amount = 10f, bool alive = true, bool self = false,
                                    bool teammate = false, bool immune = false, bool shielded = false) =>
            HitFlashRule.FlashesOnHit(source, amount, alive, self, teammate, immune, shielded);

        private static readonly DamageSource[] EverySource = (DamageSource[])System.Enum.GetValues(typeof(DamageSource));

        [Test]
        public void EveryKindOfDamageThatLandsFlashes()
        {
            Assert.IsTrue(Flashes(DamageSource.Projectile), "a bullet");
            Assert.IsTrue(Flashes(DamageSource.Splash), "a rocket or mine blast");
            Assert.IsTrue(Flashes(DamageSource.Contact), "a contact hit");
            Assert.IsTrue(Flashes(DamageSource.Burn), "a burn tick or the fire field");
            Assert.IsTrue(Flashes(DamageSource.Zone), "the Electric Fence");
            Assert.IsTrue(Flashes(DamageSource.SuddenDeath), "the sudden-death circle");
            foreach (DamageSource source in EverySource)
                Assert.IsTrue(Flashes(source), source + ": any damage that lands flashes");
        }

        [Test]
        public void AHitTheVictimTurnsAwayDoesNotFlash()
        {
            foreach (DamageSource source in EverySource)
            {
                Assert.IsFalse(Flashes(source, self: true), source + ": your own damage never hurts you");
                Assert.IsFalse(Flashes(source, teammate: true), source + ": friendly fire does nothing");
                Assert.IsFalse(Flashes(source, shielded: true), source + ": the respawn shield stops it");
                Assert.IsFalse(Flashes(source, amount: 0f), source + ": no damage, no flash (the Stun Gun bolt keeps its own blue)");
            }
        }

        [Test]
        public void TheImmunityStopsTheFlashExceptForTheSuddenDeathCircle()
        {
            foreach (DamageSource source in EverySource)
                if (source != DamageSource.SuddenDeath)
                    Assert.IsFalse(Flashes(source, immune: true), source + ": the yellow immunity stops it");
            Assert.IsTrue(Flashes(DamageSource.SuddenDeath, immune: true), "the circle goes through the immunity, so it lands and flashes");
        }

        [Test]
        public void ADeadPlayerDoesNotFlash()
        {
            foreach (DamageSource source in EverySource)
                Assert.IsFalse(Flashes(source, alive: false), source.ToString());
        }

        [Test]
        public void AHitAlwaysStartsTheFlashOver()
        {
            foreach (DamageSource source in new[] { DamageSource.Projectile, DamageSource.Splash, DamageSource.Contact })
            {
                Assert.IsTrue(HitFlashRule.StartsFlash(source, 0f, 0.3f), source + ": right on top of another flash");
                Assert.IsTrue(HitFlashRule.StartsFlash(source, 0.05f, 0.3f), source + ": while one still shows");
            }
        }

        [Test]
        public void DamageOverTimeStartsAFlashAtMostOncePerGap()
        {
            foreach (DamageSource source in new[] { DamageSource.Burn, DamageSource.Zone, DamageSource.SuddenDeath })
            {
                Assert.IsFalse(HitFlashRule.StartsFlash(source, 0.05f, 0.3f), source + ": a tick while a flash shows does not restart it");
                Assert.IsFalse(HitFlashRule.StartsFlash(source, 0.29f, 0.3f), source + ": nor just before the gap is up");
                Assert.IsTrue(HitFlashRule.StartsFlash(source, 0.3f, 0.3f), source + ": once the gap is up");
                Assert.IsTrue(HitFlashRule.StartsFlash(source, float.PositiveInfinity, 0.3f), source + ": never flashed before");
                Assert.IsTrue(HitFlashRule.StartsFlash(source, 0f, 0f), source + ": no gap, every tick flashes");
            }
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
