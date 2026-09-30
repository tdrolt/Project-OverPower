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

        // ---- Mark plan step 3: MarkWindowSeconds/MarkedDamageMultiplier follow AbilityId's own
        // precedent - appended last with a default, so every existing call site keeps compiling. ----

        [Test]
        public void ADamageInfoDoesNotMarkUnlessToldTo()
        {
            var info = new DamageInfo(10f, 1, 0, 5, DamageSource.Projectile, false, Vector3.zero);

            Assert.AreEqual(0f, info.MarkWindowSeconds);
            Assert.AreEqual(1f, info.MarkedDamageMultiplier);
        }

        [Test]
        public void WithAmountChangesOnlyTheAmount()
        {
            var info = new DamageInfo(10f, 1, 0, 5, DamageSource.Projectile, true, new Vector3(1, 2, 3), abilityId: 7,
                                       markWindowSeconds: 2f, markedDamageMultiplier: 1.5f);

            DamageInfo scaled = info.WithAmount(15f);

            Assert.AreEqual(15f, scaled.Amount);
            Assert.AreEqual(info.SourceActorNumber, scaled.SourceActorNumber);
            Assert.AreEqual(info.SourceTeamId, scaled.SourceTeamId);
            Assert.AreEqual(info.WeaponId, scaled.WeaponId);
            Assert.AreEqual(info.Source, scaled.Source);
            Assert.AreEqual(info.IgnoresArmor, scaled.IgnoresArmor);
            Assert.AreEqual(info.HitPoint, scaled.HitPoint);
            Assert.AreEqual(info.AbilityId, scaled.AbilityId);
            Assert.AreEqual(info.MarkWindowSeconds, scaled.MarkWindowSeconds);
            Assert.AreEqual(info.MarkedDamageMultiplier, scaled.MarkedDamageMultiplier);
        }

        [Test]
        public void AResultWithAMarkKeepsEveryDamageFigure()
        {
            var result = new DamageResult(3, 18, false, false);

            DamageResult marked = result.WithMark(MarkOutcome.Cashed);

            Assert.AreEqual(3f, marked.ArmorAbsorbed);
            Assert.AreEqual(18f, marked.HealthLost);
            Assert.IsFalse(marked.ArmorBroke);
            Assert.IsFalse(marked.Lethal);
            Assert.AreEqual(21f, marked.Total);
            Assert.AreEqual(MarkOutcome.Cashed, marked.Mark);
            Assert.AreEqual(MarkOutcome.None, result.Mark, "the default, before WithMark");
        }
    }
}
