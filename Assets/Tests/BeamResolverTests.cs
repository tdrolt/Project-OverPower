using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    public class BeamResolverTests
    {
        // The laser's real range, and the actor/team numbers a real shooter would carry, so these
        // tests read against numbers the designer recognises from the weapon asset.
        private const float Range = 26f;
        private const int ShooterActor = 1;
        private const int ShooterTeam = 0;
        private const int EnemyTeam = 1;

        /// <summary>A stand-in for a player or dummy. The resolver only ever reads who it is and
        /// whether it is alive - it never deals damage itself - so ApplyDamage does nothing.</summary>
        private sealed class FakeTarget : IDamageable
        {
            public FakeTarget(int actorNumber, int teamId, bool isAlive = true)
            {
                ActorNumber = actorNumber;
                TeamId = teamId;
                IsAlive = isAlive;
            }

            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive { get; }
            public int TeamId { get; }
            public int ActorNumber { get; }
        }

        private static FakeTarget Enemy(int actor) => new FakeTarget(actor, EnemyTeam);

        private static BeamContact Hit(IDamageable target, float distance) =>
            new BeamContact(distance, target, Vector3.forward * distance);

        private static BeamContact Wall(float distance) =>
            new BeamContact(distance, null, Vector3.forward * distance);

        private static BeamResult Resolve(int maxTargets, params BeamContact[] contacts) =>
            BeamResolver.Resolve(new List<BeamContact>(contacts), Range, maxTargets,
                                 ShooterActor, ShooterTeam);

        [Test]
        public void ABeamThatMeetsNothingRunsItsFullRangeAndConnectsWithNothing()
        {
            var result = Resolve(BeamResolver.Unlimited);

            Assert.IsEmpty(result.Struck);
            Assert.IsFalse(result.Connected);
            Assert.IsFalse(result.StoppedOnGeometry);
            Assert.AreEqual(Range, result.Length, 0.0001f);
        }

        [Test]
        public void TargetsAreStruckNearestFirstWhateverOrderThePhysicsQueryReportedThem()
        {
            // Physics.RaycastAll makes no promise about order. A beam that stops after N targets
            // must stop after the NEAREST N, or it would skip the player in front to hit the one
            // behind.
            var near = Enemy(2);
            var middle = Enemy(3);
            var far = Enemy(4);

            var result = Resolve(BeamResolver.Unlimited, Hit(far, 20f), Hit(near, 5f), Hit(middle, 12f));

            Assert.AreEqual(3, result.Struck.Count);
            Assert.AreSame(near, result.Struck[0].Target);
            Assert.AreSame(middle, result.Struck[1].Target);
            Assert.AreSame(far, result.Struck[2].Target);
        }

        [Test]
        public void AWallStopsTheBeamAndNothingBehindItIsStruck()
        {
            var inFront = Enemy(2);
            var behind = Enemy(3);

            var result = Resolve(BeamResolver.Unlimited, Hit(behind, 15f), Wall(10f), Hit(inFront, 4f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreSame(inFront, result.Struck[0].Target);
            Assert.IsTrue(result.StoppedOnGeometry);
            Assert.AreEqual(10f, result.Length, 0.0001f,
                "The beam's visible length must end at the wall, not run on to max range.");
        }

        [Test]
        public void ATargetWithSeveralCollidersIsStruckOnlyOnce()
        {
            // A player is several colliders to Physics. Without this the laser would quietly deal
            // double damage to anything with a second hitbox - see ExplodeOnImpact's same rule.
            var twoColliders = Enemy(2);

            var result = Resolve(BeamResolver.Unlimited, Hit(twoColliders, 5f), Hit(twoColliders, 5.3f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreEqual(5f, result.Struck[0].Distance, 0.0001f,
                "The strike should be recorded where the beam first touched the target.");
        }

        [Test]
        public void ASecondColliderOfAnAlreadyStruckTargetDoesNotUseUpAPierce()
        {
            var first = Enemy(2);
            var second = Enemy(3);

            var result = Resolve(2, Hit(first, 5f), Hit(first, 5.3f), Hit(second, 9f));

            Assert.AreEqual(2, result.Struck.Count);
            Assert.AreSame(second, result.Struck[1].Target);
        }

        [Test]
        public void MaxTargetsOfOneStopsTheBeamAtTheFirstTarget()
        {
            // What a hitscan weapon with no Pierce component does.
            var first = Enemy(2);
            var second = Enemy(3);

            var result = Resolve(1, Hit(first, 5f), Hit(second, 9f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreSame(first, result.Struck[0].Target);
            Assert.AreEqual(5f, result.Length, 0.0001f);
            Assert.IsFalse(result.StoppedOnGeometry);
        }

        [Test]
        public void APositiveMaxTargetsStopsAfterExactlyThatMany()
        {
            var result = Resolve(2, Hit(Enemy(2), 3f), Hit(Enemy(3), 6f), Hit(Enemy(4), 9f));

            Assert.AreEqual(2, result.Struck.Count);
            Assert.AreEqual(6f, result.Length, 0.0001f);
        }

        [Test]
        public void UnlimitedMaxTargetsPiercesEveryTargetInRange()
        {
            var result = Resolve(BeamResolver.Unlimited,
                                 Hit(Enemy(2), 3f), Hit(Enemy(3), 6f), Hit(Enemy(4), 9f), Hit(Enemy(5), 12f));

            Assert.AreEqual(4, result.Struck.Count);
            Assert.AreEqual(Range, result.Length, 0.0001f);
        }

        [Test]
        public void AnyNegativeMaxTargetsCountsAsUnlimited()
        {
            var result = Resolve(-7, Hit(Enemy(2), 3f), Hit(Enemy(3), 6f));

            Assert.AreEqual(2, result.Struck.Count);
        }

        [Test]
        public void MaxTargetsOfZeroStillStrikesTheFirstTarget()
        {
            // A typed 0 reading as "this beam can never hit anyone" would make a weapon silently
            // useless. The safer reading of the typo is the plain non-piercing beam.
            var first = Enemy(2);

            var result = Resolve(0, Hit(first, 5f), Hit(Enemy(3), 9f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreSame(first, result.Struck[0].Target);
        }

        [Test]
        public void TheShooterIsPassedThroughAndNeverStruck()
        {
            var self = new FakeTarget(ShooterActor, ShooterTeam);
            var enemy = Enemy(2);

            var result = Resolve(1, Hit(self, 0.2f), Hit(enemy, 8f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreSame(enemy, result.Struck[0].Target);
        }

        [Test]
        public void ATeammateIsPassedThroughUndamagedAndDoesNotUseUpAPierce()
        {
            // Matches ProjectileMotor.FliesThrough: a teammate crossing your line of fire is
            // neither hit nor a shield, even for a beam that can only strike one target.
            var teammate = new FakeTarget(2, ShooterTeam);
            var enemy = Enemy(3);

            var result = Resolve(1, Hit(teammate, 4f), Hit(enemy, 8f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreSame(enemy, result.Struck[0].Target);
        }

        [Test]
        public void AnUnknownShooterTeamFailsOpenAndEveryoneElseIsAValidTarget()
        {
            // Same fail-open rule as Teams.AreSameTeam: an unknown team silently making someone
            // unhittable is far worse to debug than one stray friendly-fire hit.
            var unknownTeam = new FakeTarget(2, -1);

            var result = BeamResolver.Resolve(new List<BeamContact> { Hit(unknownTeam, 5f) },
                                              Range, 1, ShooterActor, -1);

            Assert.AreEqual(1, result.Struck.Count);
        }

        [Test]
        public void ADeadTargetIsPassedThrough()
        {
            // A corpse blocking shots was a real playtest bug, and a dead practice dummy still
            // has its collider while it waits to reset.
            var corpse = new FakeTarget(2, EnemyTeam, isAlive: false);
            var enemy = Enemy(3);

            var result = Resolve(1, Hit(corpse, 4f), Hit(enemy, 8f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.AreSame(enemy, result.Struck[0].Target);
        }

        [Test]
        public void ContactsBeyondMaxRangeAreIgnored()
        {
            var result = Resolve(BeamResolver.Unlimited, Hit(Enemy(2), 5f), Wall(Range + 1f), Hit(Enemy(3), Range + 2f));

            Assert.AreEqual(1, result.Struck.Count);
            Assert.IsFalse(result.StoppedOnGeometry);
            Assert.AreEqual(Range, result.Length, 0.0001f);
        }

        [Test]
        public void ResolvingDoesNotReorderTheCallersList()
        {
            var contacts = new List<BeamContact> { Hit(Enemy(2), 9f), Hit(Enemy(3), 2f) };

            BeamResolver.Resolve(contacts, Range, BeamResolver.Unlimited, ShooterActor, ShooterTeam);

            Assert.AreEqual(9f, contacts[0].Distance, 0.0001f);
        }
    }
}
