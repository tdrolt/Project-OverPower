using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// RaybeamAbility calls BeamResolver.Resolve once PER BEAM, each time with maxTargets set to
    /// Unlimited - a new usage pattern for BeamResolver, since every existing weapon caps it at 1
    /// or a small Pierce count. The addendum's "every enemy before the cut gets the debuff once per
    /// beam [C: pierces]" means a single beam must never stop at the first enemy it crosses, and a
    /// target caught by two or three beams must be asked once per beam that actually crossed it -
    /// not once per cast. These tests pin that usage down without a scene, plus the resulting
    /// Vulnerability stacking through StatusEffectState (see StatusEffectStateTests for the
    /// stacking rule in isolation; these tests pin the RAYBEAM-SPECIFIC numbers - 0.30 per beam,
    /// 0.60 cap - against it).
    /// </summary>
    public class RaybeamPierceTests
    {
        private const int CasterActor = 1;
        private const int CasterTeam = 0;
        private const int EnemyTeam = 1;
        private const float Range = 12f;

        private sealed class FakeTarget : IDamageable
        {
            public FakeTarget(int actor, int team) { ActorNumber = actor; TeamId = team; }
            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive => true;
            public int TeamId { get; }
            public int ActorNumber { get; }
            public bool HasLocalAuthority => true;
        }

        private static BeamContact Hit(IDamageable target, float distance) =>
            new BeamContact(distance, target, Vector3.forward * distance);

        [Test]
        public void OneBeamAppliesOnceEvenWhenATargetHasTwoColliders()
        {
            var target = new FakeTarget(2, EnemyTeam);
            var result = BeamResolver.Resolve(
                new List<BeamContact> { Hit(target, 4f), Hit(target, 4.1f) },
                Range, BeamResolver.Unlimited, CasterActor, CasterTeam);

            Assert.AreEqual(1, result.Struck.Count, "Two colliders of the same enemy must count as one hit.");
        }

        [Test]
        public void OneBeamPiercesEveryEnemyInItsPathNotJustTheFirst()
        {
            var near = new FakeTarget(2, EnemyTeam);
            var far = new FakeTarget(3, EnemyTeam);

            var result = BeamResolver.Resolve(
                new List<BeamContact> { Hit(near, 3f), Hit(far, 9f) },
                Range, BeamResolver.Unlimited, CasterActor, CasterTeam);

            Assert.AreEqual(2, result.Struck.Count, "Raybeam resolves with Unlimited, so it never stops at the first enemy.");
        }

        [Test]
        public void ThreeSeparateBeamsEachHittingTheSameTargetGiveThreeIndependentApplications()
        {
            // Models one target caught by all three beams: RaybeamAbility calls Resolve once per
            // beam, so this is three independent calls, never one call with three contacts.
            var target = new FakeTarget(2, EnemyTeam);
            int applications = 0;

            for (int beam = 0; beam < 3; beam++)
            {
                var result = BeamResolver.Resolve(new List<BeamContact> { Hit(target, 8f) },
                                                   Range, BeamResolver.Unlimited, CasterActor, CasterTeam);
                applications += result.Struck.Count;
            }

            Assert.AreEqual(3, applications);
        }

        [Test]
        public void AStructureInABeamsPathIsNeverCountedAsAVulnerabilityApplication()
        {
            // CoverWall is an IDamageable and IStructure but implements no IStatusReceiver -
            // RaybeamAbility's own status-application loop skips anything it cannot find a
            // receiver on. BeamResolver still reports the structure hit (it stops the beam right
            // there, per its own class comment) - this test only pins the count Resolve reports,
            // matching AStructureStopsAPiercingBeamAfterTakingTheHit in BeamResolverTests.
            var cover = new FakeTarget(2, EnemyTeam);
            var behind = new FakeTarget(3, EnemyTeam);
            var contact = new BeamContact(6f, cover, Vector3.forward * 6f, isStructure: true);

            var result = BeamResolver.Resolve(new List<BeamContact> { contact, Hit(behind, 12f) },
                                               Range, BeamResolver.Unlimited, CasterActor, CasterTeam);

            Assert.AreEqual(1, result.Struck.Count, "The beam must stop at the structure and never reach whoever is behind it.");
        }

        [Test]
        public void OneTwoAndThreeApplicationsGiveThirtySixtyAndStillSixtyPercent()
        {
            // The exact claim the raybeam's spec makes: one beam is +30%, two or three is +60%.
            const float perBeam = 0.30f;
            const float cap = 0.60f;
            var spec = new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 4f, magnitude = perBeam };

            var oneBeam = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: cap);
            oneBeam.Apply(spec);
            Assert.AreEqual(0.30f, oneBeam.Magnitude(StatusKind.Vulnerability), 0.0001f);

            var twoBeams = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: cap);
            twoBeams.Apply(spec);
            twoBeams.Apply(spec);
            Assert.AreEqual(0.60f, twoBeams.Magnitude(StatusKind.Vulnerability), 0.0001f);

            var threeBeams = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: cap);
            threeBeams.Apply(spec);
            threeBeams.Apply(spec);
            threeBeams.Apply(spec);
            Assert.AreEqual(0.60f, threeBeams.Magnitude(StatusKind.Vulnerability), 0.0001f, "The cap, not 90%.");
        }

        [Test]
        public void ASecondRaybeamRefreshesTheReportedRemainingDurationToAFullFourSeconds()
        {
            // StackToCap keeps each application as its own timed stack (see StatusEffectState's
            // class comment), and Remaining reports whichever stack has the most time left - so a
            // second raybeam landing on an already-vulnerable target does not extend the FIRST
            // stack, but it does add a fresh 4s one, and Remaining reads that as a refresh.
            var spec = new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 4f, magnitude = 0.30f };
            var state = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);

            state.Apply(spec);
            state.Tick(3f); // 1s left on the first stack
            Assert.AreEqual(1f, state.Remaining(StatusKind.Vulnerability), 0.0001f);

            state.Apply(spec); // a second raybeam lands
            Assert.AreEqual(4f, state.Remaining(StatusKind.Vulnerability), 0.0001f,
                "Remaining reports the longest-lived stack, so a fresh application reads as a refresh.");
        }
    }
}
