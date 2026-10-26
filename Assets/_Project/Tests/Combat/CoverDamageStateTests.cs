using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers CoverDamageState - the HP half of CoverWall's damage funnel (Task 1.8b
    /// addendum: "counts only Projectile and Splash damage toward its 100; Burn/Zone ignored").
    /// Friendly filtering is FriendlyFire's job and is already covered by FriendlyFireTests -
    /// CoverWall.ApplyDamage applies that check before this class ever sees a hit.</summary>
    public class CoverDamageStateTests
    {
        [Test]
        public void ProjectileDamageIsAbsorbed()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            float absorbed = state.ApplyDamage(40f, DamageSource.Projectile);

            Assert.AreEqual(40f, absorbed);
            Assert.AreEqual(40f, state.DamageAbsorbed);
            Assert.IsFalse(state.Destroyed);
        }

        [Test]
        public void SplashDamageIsAbsorbed()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            float absorbed = state.ApplyDamage(25f, DamageSource.Splash);

            Assert.AreEqual(25f, absorbed);
            Assert.AreEqual(25f, state.DamageAbsorbed);
        }

        [Test]
        public void BurnDamageDoesNotCount()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            float absorbed = state.ApplyDamage(50f, DamageSource.Burn);

            Assert.AreEqual(0f, absorbed);
            Assert.AreEqual(0f, state.DamageAbsorbed);
            Assert.IsFalse(state.Destroyed);
        }

        [Test]
        public void ZoneDamageDoesNotCount()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            float absorbed = state.ApplyDamage(50f, DamageSource.Zone);

            Assert.AreEqual(0f, absorbed);
            Assert.AreEqual(0f, state.DamageAbsorbed);
        }

        [Test]
        public void ContactDamageDoesNotCount()
        {
            // Not one of Tudor's two named sources either - a wall has no reason to react to a
            // contact hit, and CountsTowardHitPoints is deliberately an allow-list, not a deny-list.
            var state = new CoverDamageState(maxHitPoints: 100f);

            float absorbed = state.ApplyDamage(50f, DamageSource.Contact);

            Assert.AreEqual(0f, absorbed);
        }

        [Test]
        public void DestroyedExactlyAtMaxHitPoints()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            state.ApplyDamage(40f, DamageSource.Projectile);
            state.ApplyDamage(40f, DamageSource.Projectile);
            Assert.IsFalse(state.Destroyed);

            state.ApplyDamage(20f, DamageSource.Projectile);
            Assert.IsTrue(state.Destroyed);
        }

        [Test]
        public void NotDestroyedOneDamageBelowMax()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            state.ApplyDamage(99f, DamageSource.Projectile);

            Assert.IsFalse(state.Destroyed);
        }

        [Test]
        public void OverkillIsClampedToWhatWasActuallyRemaining()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);
            state.ApplyDamage(90f, DamageSource.Projectile);

            float absorbed = state.ApplyDamage(40f, DamageSource.Splash);

            Assert.AreEqual(10f, absorbed, "only the remaining 10 HP should be reported as absorbed");
            Assert.AreEqual(100f, state.DamageAbsorbed);
            Assert.IsTrue(state.Destroyed);
        }

        [Test]
        public void FurtherHitsAfterDestroyedAreNoOps()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);
            state.ApplyDamage(100f, DamageSource.Projectile);

            float absorbed = state.ApplyDamage(50f, DamageSource.Projectile);

            Assert.AreEqual(0f, absorbed);
            Assert.AreEqual(100f, state.DamageAbsorbed, "must not climb past MaxHitPoints on a stray extra hit");
        }

        [Test]
        public void ZeroOrNegativeAmountIsIgnored()
        {
            var state = new CoverDamageState(maxHitPoints: 100f);

            Assert.AreEqual(0f, state.ApplyDamage(0f, DamageSource.Projectile));
            Assert.AreEqual(0f, state.ApplyDamage(-5f, DamageSource.Projectile));
            Assert.AreEqual(0f, state.DamageAbsorbed);
        }
    }
}
