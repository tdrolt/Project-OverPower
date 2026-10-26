using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>
    /// Covers ConeFilter - the flamethrower's cone/angle/range test on the XZ plane (Task 1.9), and
    /// the "once per target per cast" rule its own addendum calls out by name: a spray sampled every
    /// physics step must not re-Refresh the same burn on the same target every frame, or a 1s spray
    /// deals up to 30 damage instead of the intended 25 (see StatusEffectState's Refresh rule for
    /// Burn). Fake IDamageables stand in for real players and dummies, the same shape
    /// MineTargetingTests uses.
    /// </summary>
    public class ConeFilterTests
    {
        private const int CasterActor = 1;
        private const int CasterTeam = 0;
        private const float Range = 7f;
        private const float FullAngle = 45f; // 22.5 degrees either side of forward.

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

        // ---- IsWithinCone: angle/range on XZ -------------------------------------------------------

        [Test]
        public void DirectlyAheadWithinRangeIsInsideTheCone()
        {
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 5f),
                                                   Range, FullAngle);
            Assert.IsTrue(result);
        }

        [Test]
        public void BeyondRangeIsOutsideTheConeEvenDirectlyAhead()
        {
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 8f),
                                                   Range, FullAngle);
            Assert.IsFalse(result);
        }

        [Test]
        public void ExactlyAtRangeIsInsideTheCone()
        {
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, Range),
                                                   Range, FullAngle);
            Assert.IsTrue(result);
        }

        [Test]
        public void InsideTheHalfAngleIsInsideTheCone()
        {
            // 45 full angle -> 22.5 either side. 20 degrees off-axis, within range.
            Vector3 target = Quaternion.Euler(0f, 20f, 0f) * (Vector3.forward * 5f);
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, target, Range, FullAngle);
            Assert.IsTrue(result);
        }

        [Test]
        public void BeyondTheHalfAngleIsOutsideTheCone()
        {
            // 40 degrees off-axis is well past the 22.5 degree half-angle.
            Vector3 target = Quaternion.Euler(0f, 40f, 0f) * (Vector3.forward * 5f);
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, target, Range, FullAngle);
            Assert.IsFalse(result);
        }

        [Test]
        public void AVerticalDifferenceIsIgnoredForBothAngleAndRange()
        {
            // Same XZ position as a clean in-cone hit, but 5m up in the air - the cone is judged on
            // the ground plane, not full 3D distance, matching AimConeState's own flat spread.
            Vector3 target = new Vector3(0f, 5f, 5f);
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, target, Range, FullAngle);
            Assert.IsTrue(result);
        }

        [Test]
        public void StandingExactlyAtTheApexIsInsideRegardlessOfAngle()
        {
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, Vector3.zero, Range, FullAngle);
            Assert.IsTrue(result);
        }

        [Test]
        public void NoFlatForwardComponentCannotDefineAConeAndReturnsFalse()
        {
            bool result = ConeFilter.IsWithinCone(Vector3.zero, Vector3.up, new Vector3(0f, 0f, 5f), Range, FullAngle);
            Assert.IsFalse(result);
        }

        // ---- SelectCandidates: team, structure and once-per-target ---------------------------------

        [Test]
        public void AnEnemyInsideTheConeIsSelected()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            var candidates = new List<ConeCandidate> { new ConeCandidate(enemy, new Vector3(0f, 0f, 5f)) };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(enemy, result[0].Target);
        }

        [Test]
        public void ATeammateIsExcluded()
        {
            var teammate = new FakeTarget { ActorNumber = 3, TeamId = CasterTeam };
            var candidates = new List<ConeCandidate> { new ConeCandidate(teammate, new Vector3(0f, 0f, 5f)) };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void TheCasterThemselfIsExcludedEvenStandingInTheirOwnCone()
        {
            var self = new FakeTarget { ActorNumber = CasterActor, TeamId = CasterTeam };
            var candidates = new List<ConeCandidate> { new ConeCandidate(self, new Vector3(0f, 0f, 1f)) };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void AnEnemyOutsideRangeIsExcluded()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            var candidates = new List<ConeCandidate> { new ConeCandidate(enemy, new Vector3(0f, 0f, 20f)) };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void AnEnemyOutsideTheHalfAngleIsExcluded()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            Vector3 pos = Quaternion.Euler(0f, 40f, 0f) * (Vector3.forward * 5f);
            var candidates = new List<ConeCandidate> { new ConeCandidate(enemy, pos) };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void AStructureIsExcludedDespiteFailingOpenLikeAnUnrecognisedTeam()
        {
            var cover = new FakeStructure();
            var candidates = new List<ConeCandidate> { new ConeCandidate(cover, new Vector3(0f, 0f, 5f)) };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ATargetAlreadyInTheHitSetIsExcluded()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            var candidates = new List<ConeCandidate> { new ConeCandidate(enemy, new Vector3(0f, 0f, 5f)) };
            var alreadyHit = new HashSet<IDamageable> { enemy };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, alreadyHit);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void SelectCandidatesNeverMutatesTheAlreadyHitSet()
        {
            // The caller decides which returned candidates actually get marked hit (occlusion may
            // still reject one) - this method must only read the set, never write to it.
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            var candidates = new List<ConeCandidate> { new ConeCandidate(enemy, new Vector3(0f, 0f, 5f)) };
            var alreadyHit = new HashSet<IDamageable>();

            ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range, FullAngle,
                                         CasterActor, CasterTeam, alreadyHit);

            Assert.AreEqual(0, alreadyHit.Count);
        }

        [Test]
        public void CallingTwiceWithTheSameAlreadyHitSetStopsReturningTheSameTarget()
        {
            // Simulates the caller marking a target hit after the first tick applies its status, then
            // a later FixedUpdate tick offering the identical candidate list again - the whole point
            // of "once per target per cast".
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            var candidates = new List<ConeCandidate> { new ConeCandidate(enemy, new Vector3(0f, 0f, 5f)) };
            var alreadyHit = new HashSet<IDamageable>();

            var first = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                      FullAngle, CasterActor, CasterTeam, alreadyHit);
            Assert.AreEqual(1, first.Count);

            alreadyHit.Add(enemy); // What the caller does once it actually applies the status.

            var second = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, alreadyHit);
            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void MultipleEnemiesInsideTheConeAreAllSelected()
        {
            var a = new FakeTarget { ActorNumber = 2, TeamId = 1 };
            var b = new FakeTarget { ActorNumber = 3, TeamId = 2 };
            var candidates = new List<ConeCandidate>
            {
                new ConeCandidate(a, new Vector3(0f, 0f, 4f)),
                new ConeCandidate(b, new Vector3(0f, 0f, 6f))
            };

            var result = ConeFilter.SelectCandidates(candidates, Vector3.zero, Vector3.forward, Range,
                                                       FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void EmptyCandidateListSelectsNothing()
        {
            var result = ConeFilter.SelectCandidates(new List<ConeCandidate>(), Vector3.zero, Vector3.forward,
                                                       Range, FullAngle, CasterActor, CasterTeam, new HashSet<IDamageable>());
            Assert.AreEqual(0, result.Count);
        }
    }
}
