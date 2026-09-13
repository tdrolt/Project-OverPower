using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers MineTargeting.SelectTargets - the "enemy of OwnerTeam AND HasLocalAuthority"
    /// filter the Task 1.8 addendum specifies for both the trigger check and the blast, with fake
    /// IDamageables standing in for real players and dummies (plain data in, no scene needed).</summary>
    public class MineTargetingTests
    {
        private const int OwnerActor = 1;
        private const int OwnerTeam = 0;

        private class FakeTarget : IDamageable
        {
            public int ActorNumber { get; set; }
            public int TeamId { get; set; }
            public bool HasLocalAuthority { get; set; } = true;
            public bool IsAlive { get; set; } = true;
            public DamageResult ApplyDamage(in DamageInfo info) => default;
        }

        [Test]
        public void AnEnemyOnAnotherTeamWithLocalAuthorityIsSelected()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1, HasLocalAuthority = true };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { enemy }, OwnerActor, OwnerTeam);

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(enemy, result[0]);
        }

        [Test]
        public void TheOwnersOwnActorIsExcludedEvenOnAnUnknownTeam()
        {
            // The placer standing on their own mine - never a valid target, whatever team shows up.
            var self = new FakeTarget { ActorNumber = OwnerActor, TeamId = OwnerTeam, HasLocalAuthority = true };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { self }, OwnerActor, OwnerTeam);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ATeammateOnTheSameTeamIsExcluded()
        {
            var teammate = new FakeTarget { ActorNumber = 3, TeamId = OwnerTeam, HasLocalAuthority = true };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { teammate }, OwnerActor, OwnerTeam);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ARemoteEnemyWithNoLocalAuthorityIsExcluded()
        {
            // A real enemy player, but this client cannot damage them (ApplyDamage would no-op on
            // their remote copy) - MineTargeting must not even offer them as a target.
            var remoteEnemy = new FakeTarget { ActorNumber = 4, TeamId = 1, HasLocalAuthority = false };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { remoteEnemy }, OwnerActor, OwnerTeam);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ADummyWithAnUnrecognisedTeamFailsOpenAsAnEnemy()
        {
            // DummyTarget.TeamId is 99 - not the owner's team, so it must count as an enemy, matching
            // FriendlyFire's own fail-open rule.
            var dummy = new FakeTarget { ActorNumber = -1, TeamId = 99, HasLocalAuthority = true };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { dummy }, OwnerActor, OwnerTeam);

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void NullCandidatesInTheListAreSkipped()
        {
            var enemy = new FakeTarget { ActorNumber = 2, TeamId = 1, HasLocalAuthority = true };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { null, enemy }, OwnerActor, OwnerTeam);

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void EmptyCandidateListSelectsNothing()
        {
            var result = MineTargeting.SelectTargets(new List<IDamageable>(), OwnerActor, OwnerTeam);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void MultipleEnemiesAreAllSelected()
        {
            var a = new FakeTarget { ActorNumber = 2, TeamId = 1, HasLocalAuthority = true };
            var b = new FakeTarget { ActorNumber = 3, TeamId = 2, HasLocalAuthority = true };

            var result = MineTargeting.SelectTargets(new List<IDamageable> { a, b }, OwnerActor, OwnerTeam);

            Assert.AreEqual(2, result.Count);
        }
    }
}
