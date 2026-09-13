using NUnit.Framework;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>
    /// Covers KnockbackResolver - the two pure decisions the sonic pulse (Task 1.10a) needs and
    /// nothing about physics or networking should ever change: which way a victim flies, and who
    /// gets stunned once the flight stops. Fake IDamageables stand in for real players and dummies,
    /// the same shape ConeFilterTests and MineTargetingTests use.
    /// </summary>
    public class KnockbackResolverTests
    {
        private const int CasterActor = 1;
        private const int CasterTeam = 0;

        private class FakeTarget : IDamageable
        {
            public int ActorNumber { get; set; }
            public int TeamId { get; set; }
            public bool HasLocalAuthority { get; set; } = true;
            public bool IsAlive { get; set; } = true;
            public DamageResult ApplyDamage(in DamageInfo info) => default;
        }

        private class FakeStructure : IDamageable, IStructure
        {
            public int ActorNumber { get; set; } = -1;
            public int TeamId { get; set; } = -1;
            public bool HasLocalAuthority { get; set; } = true;
            public bool IsAlive { get; set; } = true;
            public DamageResult ApplyDamage(in DamageInfo info) => default;
        }

        // ---- ComputePushDirection ------------------------------------------------------------------

        [Test]
        public void PushesDirectlyAwayFromTheOriginTowardTheVictim()
        {
            Vector3 origin = Vector3.zero;
            Vector3 victim = new Vector3(0f, 0f, 3f);

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, Vector3.right);

            Assert.AreEqual(Vector3.forward, result);
        }

        [Test]
        public void PushDirectionIsFlattenedIgnoringAnyHeightDifference()
        {
            // The victim stands 2m higher (a ramp, a ledge) but is otherwise directly ahead - the
            // push must stay on the ground plane, not tilt upward.
            Vector3 origin = Vector3.zero;
            Vector3 victim = new Vector3(0f, 2f, 3f);

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, Vector3.right);

            Assert.AreEqual(Vector3.forward, result);
        }

        [Test]
        public void PushDirectionIsAlwaysNormalised()
        {
            Vector3 origin = Vector3.zero;
            Vector3 victim = new Vector3(10f, 0f, 0f);

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, Vector3.forward);

            Assert.AreEqual(1f, result.magnitude, 0.0001f);
        }

        [Test]
        public void AVictimStandingAtTheApexFallsBackToTheAimDirection()
        {
            // Same position as the caster - flatten(victim - origin) is the zero vector, which has
            // no direction of its own, so the aim direction the caster pressed with is used instead.
            Vector3 origin = new Vector3(5f, 0f, 5f);
            Vector3 victim = origin;

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, Vector3.right);

            Assert.AreEqual(Vector3.right, result);
        }

        [Test]
        public void AVictimWithinTheMinimumDistanceFallsBackToTheAimDirection()
        {
            // 0.05m away is closer than the 0.1m minimum the addendum specifies - too close to
            // trust for a direction, so the aim direction wins instead of an almost-random vector.
            Vector3 origin = Vector3.zero;
            Vector3 victim = new Vector3(0.05f, 0f, 0f);

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, Vector3.forward);

            Assert.AreEqual(Vector3.forward, result);
        }

        [Test]
        public void AVictimExactlyAtTheMinimumDistanceUsesItsOwnDirection()
        {
            Vector3 origin = Vector3.zero;
            Vector3 victim = new Vector3(0.1f, 0f, 0f);

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, Vector3.forward);

            Assert.AreEqual(Vector3.right, result);
        }

        [Test]
        public void TheFallbackDirectionIsAlsoFlattenedAndNormalised()
        {
            Vector3 origin = Vector3.zero;
            Vector3 victim = origin; // Forces the fallback path.

            Vector3 result = KnockbackResolver.ComputePushDirection(origin, victim, new Vector3(0f, 5f, 2f));

            Assert.AreEqual(Vector3.forward, result);
        }

        // ---- ShouldStunBlocker ----------------------------------------------------------------------

        [Test]
        public void AnEnemyBlockerWithAStatusReceiverIsStunned()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            bool result = KnockbackResolver.ShouldStunBlocker(enemy, hasStatusReceiver: true, CasterActor, CasterTeam);
            Assert.IsTrue(result);
        }

        [Test]
        public void ATeammateBlockerIsNeverStunned()
        {
            var teammate = new FakeTarget { ActorNumber = 3, TeamId = CasterTeam };
            bool result = KnockbackResolver.ShouldStunBlocker(teammate, hasStatusReceiver: true, CasterActor, CasterTeam);
            Assert.IsFalse(result);
        }

        [Test]
        public void TheCasterThemselfAsABlockerIsNeverStunned()
        {
            var self = new FakeTarget { ActorNumber = CasterActor, TeamId = CasterTeam };
            bool result = KnockbackResolver.ShouldStunBlocker(self, hasStatusReceiver: true, CasterActor, CasterTeam);
            Assert.IsFalse(result);
        }

        [Test]
        public void ABlockerWithNoStatusReceiverIsNeverStunned()
        {
            // A plain wall with an IDamageable-less collider never reaches this method at all (the
            // caller returns before it does), but a blocker that IS an IDamageable with nothing to
            // apply a status to (should not normally happen) must still not be stunned.
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            bool result = KnockbackResolver.ShouldStunBlocker(enemy, hasStatusReceiver: false, CasterActor, CasterTeam);
            Assert.IsFalse(result);
        }

        [Test]
        public void AStructureBlockerIsNeverStunnedEvenWithAStatusReceiverFlag()
        {
            var cover = new FakeStructure();
            bool result = KnockbackResolver.ShouldStunBlocker(cover, hasStatusReceiver: true, CasterActor, CasterTeam);
            Assert.IsFalse(result);
        }

        [Test]
        public void ANullBlockerIsNeverStunned()
        {
            bool result = KnockbackResolver.ShouldStunBlocker(null, hasStatusReceiver: true, CasterActor, CasterTeam);
            Assert.IsFalse(result);
        }

        [Test]
        public void AnUnknownCasterTeamFailsOpenSoTheBlockerIsStillStunned()
        {
            // Matches FriendlyFire.IsSelfOrTeammate: an unrecognised (-1) caster team must not
            // silently protect every blocker from ever being stunned.
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            bool result = KnockbackResolver.ShouldStunBlocker(enemy, hasStatusReceiver: true, CasterActor, -1);
            Assert.IsTrue(result);
        }
    }
}
