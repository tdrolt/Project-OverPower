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

        [Test]
        public void TheMinimapFogDefaults_DarkenTheUnseenMap_AndLiftTheSeenPart()
        {
            var config = ScriptableObject.CreateInstance<VisionConfig>();
            try
            {
                Assert.AreEqual(0.6f, config.MinimapFogDarkness, 1e-5f);
                Assert.AreEqual(0.2f, config.MinimapSeenLift, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
