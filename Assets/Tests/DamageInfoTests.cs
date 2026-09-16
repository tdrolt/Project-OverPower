using NUnit.Framework;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Task T3: DamageInfo.AbilityId is appended last with a default of -1, so every
    /// existing call site (the 9 `new DamageInfo(...)` sites across Mine/AoeZone/ElectricFence/
    /// PlayerStatusEffects/DummyTarget/ExplodeOnImpact/Hitscan/FireField/ProjectileMotor) keeps
    /// compiling unchanged. These two tests are the whole contract: the default, and that a real
    /// value passed through survives unchanged.</summary>
    public class DamageInfoTests
    {
        [Test]
        public void AbilityIdDefaultsToMinusOneWhenOmitted()
        {
            var info = new DamageInfo(10f, 1, 0, 5, DamageSource.Projectile, false, Vector3.zero);

            Assert.AreEqual(-1, info.AbilityId);
        }

        [Test]
        public void AbilityIdIsKeptWhenPassed()
        {
            var info = new DamageInfo(10f, 1, 0, -1, DamageSource.Splash, false, Vector3.zero, abilityId: 7);

            Assert.AreEqual(7, info.AbilityId);
        }
    }
}
