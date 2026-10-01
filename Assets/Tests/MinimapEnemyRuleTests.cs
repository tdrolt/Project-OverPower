using NUnit.Framework;
using Overpower.Vision;

namespace Overpower.Tests
{
    public class MinimapEnemyRuleTests
    {
        [Test]
        public void ASeenLivingEnemy_WithTheFogOn_GetsADot()
        {
            Assert.IsTrue(MinimapEnemyRule.ShowDot(friendly: false, alive: true, seen: true, fogOn: true));
        }

        [Test]
        public void AnEnemyNobodySees_GetsNoDot()
        {
            Assert.IsFalse(MinimapEnemyRule.ShowDot(false, true, false, true));
        }

        [Test]
        public void ADeadEnemy_GetsNoDot_EvenIfSeen()
        {
            Assert.IsFalse(MinimapEnemyRule.ShowDot(false, false, true, true));
        }

        [Test]
        public void AFriendly_GetsNoRedDot()
        {
            Assert.IsFalse(MinimapEnemyRule.ShowDot(true, true, true, true));
        }

        [Test]
        public void WithTheFogOff_NoEnemyDotIsDrawn_EvenForASeenEnemy()
        {
            Assert.IsFalse(MinimapEnemyRule.ShowDot(false, true, true, false));
        }

        [Test]
        public void TheFogLayerIsClearWhereSeen_AndFullDarknessWhereNot()
        {
            Assert.AreEqual(0f, FogMaths.UnseenAlpha(1f, 0.6f), 1e-5f);
            Assert.AreEqual(0.6f, FogMaths.UnseenAlpha(0f, 0.6f), 1e-5f);
            Assert.AreEqual(0.3f, FogMaths.UnseenAlpha(0.5f, 0.6f), 1e-5f);
        }
    }
}
