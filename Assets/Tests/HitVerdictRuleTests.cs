using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Tudor, 2026-09-18: being shot while the Invulnerability shield is up keeps you in combat. The ORDER is
    /// the point: your own and your teammates' hits are thrown away before the shield is ever asked, so they can
    /// neither spring the trap nor count as combat.</summary>
    public class HitVerdictRuleTests
    {
        private sealed class FakeShield : IArmedShield
        {
            public bool Armed;
            public int Asked;
            public bool TryConsume(float damageAmount) { Asked++; bool was = Armed; Armed = false; return was; }
        }

        [Test]
        public void YourOwnHitIsIgnoredAndNeverAsksTheShield()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.IgnoredSelf, HitVerdictRule.Classify(true, false, false, shield, 10f));
            Assert.AreEqual(0, shield.Asked);
            Assert.IsTrue(shield.Armed);
        }

        [Test]
        public void ATeammatesHitIsIgnoredAndNeverSpringsTheTrap()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.IgnoredTeammate, HitVerdictRule.Classify(false, true, false, shield, 10f));
            Assert.AreEqual(0, shield.Asked);
            Assert.IsTrue(shield.Armed);
        }

        [Test]
        public void SelfWinsOverTeammateBecauseYouAreOnYourOwnTeam()
        {
            // Teams.AreSameTeam(a, a) is true - the caller may pass both.
            Assert.AreEqual(HitVerdict.IgnoredSelf, HitVerdictRule.Classify(true, true, false, null, 10f));
        }

        [Test]
        public void AnEnemyHitDuringTheImmunityIsShieldedAndLeavesANewArmAlone()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Shielded, HitVerdictRule.Classify(false, false, true, shield, 10f));
            Assert.AreEqual(0, shield.Asked);
            Assert.IsTrue(shield.Armed);
        }

        [Test]
        public void TheFirstEnemyHitOnAnArmedShieldIsShieldedAndDisarmsIt()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Shielded, HitVerdictRule.Classify(false, false, false, shield, 10f));
            Assert.AreEqual(1, shield.Asked);
            Assert.IsFalse(shield.Armed);
        }

        [Test]
        public void AnEnemyHitWithNoArmLands()
        {
            var shield = new FakeShield();
            Assert.AreEqual(HitVerdict.Lands, HitVerdictRule.Classify(false, false, false, shield, 10f));
            Assert.AreEqual(1, shield.Asked);
        }

        [Test]
        public void NoShieldComponentStillLetsTheHitLand()
        {
            Assert.AreEqual(HitVerdict.Lands, HitVerdictRule.Classify(false, false, false, null, 10f));
        }

        [Test]
        public void ShieldedAndLandedHitsAreCombatIgnoredOnesAreNot()
        {
            Assert.IsTrue(HitVerdictRule.CountsAsCombat(HitVerdict.Shielded));
            Assert.IsTrue(HitVerdictRule.CountsAsCombat(HitVerdict.Lands));
            Assert.IsFalse(HitVerdictRule.CountsAsCombat(HitVerdict.IgnoredSelf));
            Assert.IsFalse(HitVerdictRule.CountsAsCombat(HitVerdict.IgnoredTeammate));
        }
    }
}
