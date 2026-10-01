using NUnit.Framework;
using UnityEngine;
using Overpower.Data;
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
    }
}
