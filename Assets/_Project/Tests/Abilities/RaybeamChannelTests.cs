using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 31 Oct: the Raybeam is a cast time, then a beam channelled from the gun for a few seconds. It follows the cursor, damages the
    /// first enemy it touches (walls stop it, the short barriers do not) and gives that enemy one vulnerability stack per second in it, each
    /// stack refreshing the timer, up to the cap. The caster is slowed and cannot shoot or use another ability; a stun, a knockback or
    /// death ends it. Literal numbers here are test values, not Tudor's tuning.
    /// </summary>
    public class RaybeamChannelTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [TestCase(0f, RaybeamChannelStep.Channelling)]
        [TestCase(2.9f, RaybeamChannelStep.Channelling)]
        [TestCase(3f, RaybeamChannelStep.Finished)]
        public void TheBeamRunsForTheChannelTime(float elapsed, RaybeamChannelStep expected) =>
            Assert.AreEqual(expected, RaybeamChannel.Step(elapsed, 3f, CastBlock.None, false));

        [TestCase(CastBlock.Stunned, false, RaybeamChannelStep.Broken, TestName = "a stun breaks it")]
        [TestCase(CastBlock.Dead, false, RaybeamChannelStep.Broken, TestName = "death breaks it")]
        [TestCase(CastBlock.None, true, RaybeamChannelStep.Broken, TestName = "a knockback breaks it")]
        [TestCase(CastBlock.Silenced, false, RaybeamChannelStep.Channelling, TestName = "an overheat does not")]
        public void OnlyAStunAKnockbackOrDeathBreaksTheBeam(CastBlock block, bool knockedBack, RaybeamChannelStep expected) =>
            Assert.AreEqual(expected, RaybeamChannel.Step(1f, 3f, block, knockedBack));

        [Test]
        public void TheBeamDealsItsDamagePerSecondSpreadOverTheFrames() =>
            Assert.AreEqual(0.5f, RaybeamChannel.TickDamage(5f, 0.1f), 1e-5f);

        // A stack as the beam touches, then one each further second: a whole 3 s channel reaches the third (+60%) instead of ending just short of it.
        [TestCase(0f, 0.02f, 1, TestName = "the first frame on the target: the first stack")]
        [TestCase(0.02f, 0.5f, 0, TestName = "within the first second: nothing new")]
        [TestCase(0.9f, 1.1f, 1, TestName = "a second in the beam: the second stack")]
        [TestCase(1.5f, 1.9f, 0, TestName = "between seconds: nothing new")]
        [TestCase(1.9f, 3.05f, 2, TestName = "a long frame crossing two seconds: two stacks")]
        public void AStackAsTheBeamTouchesThenOneEachSecond(float before, float after, int expected) =>
            Assert.AreEqual(expected, RaybeamChannel.StacksEarned(before, after, 1f));

        [Test]
        public void AWholeChannelOnOneTargetReachesThreeStacks()
        {
            int stacks = 0;
            float t = 0f;
            while (t < 2.95f) { stacks += RaybeamChannel.StacksEarned(t, t + 0.05f, 1f); t += 0.05f; }
            Assert.AreEqual(3, stacks, "2.95 s in the beam (a 3 s channel arriving a little late): +60%");
        }

        [Test]
        public void TheCasterIsLockedOnlyWhileChannelling()
        {
            Assert.IsTrue(RaybeamChannel.LocksCaster(true));
            Assert.IsFalse(RaybeamChannel.LocksCaster(false));
        }

        // ---- vulnerability: every new stack refreshes the timer, up to the cap

        [Test]
        public void ANewVulnerabilityStackRefreshesTheTimerOfTheOnesBefore()
        {
            var state = new StatusEffectState(0.5f, 0.6f);
            var stack = new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 4f, magnitude = 0.2f };
            state.Apply(stack);
            state.Tick(3f);
            state.Apply(stack);
            state.Tick(3f); // the first stack alone would have run out a second ago
            Assert.AreEqual(0.4f, state.Magnitude(StatusKind.Vulnerability), 1e-4f, "both stacks still on");
            Assert.AreEqual(1f, state.Remaining(StatusKind.Vulnerability), 1e-4f);
            state.Tick(1.01f);
            Assert.AreEqual(0f, state.Magnitude(StatusKind.Vulnerability), 1e-4f, "and they end together");
        }

        [Test]
        public void VulnerabilityStacksStopAtTheCap()
        {
            var state = new StatusEffectState(0.5f, 0.6f);
            var stack = new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 4f, magnitude = 0.2f };
            for (int i = 0; i < 5; i++)
                state.Apply(stack);
            Assert.AreEqual(0.6f, state.Magnitude(StatusKind.Vulnerability), 1e-4f);
        }

        // ---- the game asks the rules

        private static MethodInfo Rule(string name) => typeof(RaybeamChannel).GetMethod(name);

        [Test]
        public void TheOwnersFrameAsksTheChannelRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(RaybeamAbility), "TickChannel", Rule(nameof(RaybeamChannel.Step))));

        [Test]
        public void TheBeamDamagesAndStacksThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RaybeamAbility), "BeamTick", Rule(nameof(RaybeamChannel.TickDamage))), "damage");
            Assert.IsTrue(IlWiring.Uses(typeof(RaybeamAbility), "BeamTick", Rule(nameof(RaybeamChannel.StacksEarned))), "stacks");
        }

        private sealed class FakeTarget : IDamageable
        {
            public FakeTarget(int actor, int team) { ActorNumber = actor; TeamId = team; }
            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive => true;
            public int TeamId { get; }
            public int ActorNumber { get; }
            public bool HasLocalAuthority => true;
        }

        [Test]
        public void TheBeamStopsAtTheFirstEnemyAndPassesAnAlly()
        {
            var ally = new FakeTarget(2, 0);
            var near = new FakeTarget(3, 1);
            var far = new FakeTarget(4, 1);
            var contacts = new System.Collections.Generic.List<BeamContact>
            {
                new BeamContact(9f, far, UnityEngine.Vector3.forward * 9f),
                new BeamContact(2f, ally, UnityEngine.Vector3.forward * 2f),
                new BeamContact(5f, near, UnityEngine.Vector3.forward * 5f),
            };
            BeamResult beam = BeamResolver.Resolve(contacts, 20f, RaybeamChannel.TargetsHit, 1, 0);
            Assert.AreEqual(1, beam.Struck.Count);
            Assert.AreSame(near, beam.Struck[0].Target);
            Assert.AreEqual(5f, beam.Length, 1e-4f, "the beam ends on that enemy");
        }

        [Test]
        public void TheBeamAsksHowManyItHits() =>
            Assert.IsTrue(IlWiring.Uses(typeof(RaybeamAbility), "BeamTick", typeof(RaybeamChannel).GetProperty(nameof(RaybeamChannel.TargetsHit)).GetGetMethod()));

        [Test]
        public void TheRaybeamTellsTheRunnerItLocksTheCaster() =>
            Assert.IsTrue(IlWiring.Uses(typeof(RaybeamAbility), "get_LocksCaster", Rule(nameof(RaybeamChannel.LocksCaster))));

        [Test]
        public void TheRunnerKeepsOtherAbilitiesBackWhileOneLocksTheCaster() =>
            Assert.IsTrue(IlWiring.Uses(typeof(AbilityRunner), "LockedByAnother", typeof(AbilityModule).GetProperty(nameof(AbilityModule.LocksCaster)).GetGetMethod())
                          && IlWiring.ResultDecidesABranch(typeof(AbilityRunner), "Update", typeof(AbilityRunner).GetMethod("LockedByAnother", Any)));

        [Test]
        public void TheGunDoesNotFireWhileTheCasterIsLocked() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(WeaponFiring), "TryFire",
                typeof(AbilityRunner).GetProperty(nameof(AbilityRunner.CasterLocked)).GetGetMethod()));

        [Test]
        public void AKnockbackIsReadFromTheCastersOwnDisplacement() =>
            Assert.IsTrue(IlWiring.Uses(typeof(RaybeamAbility), "TickChannel", typeof(PlayerDisplacement).GetProperty(nameof(PlayerDisplacement.IsKnockedBack)).GetGetMethod()));

        [Test]
        public void TheNewNumbersAreOnTheRaybeamPrefab()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Abilities/Prefabs/Raybeam.prefab").Replace("\r\n", "\n");
            foreach (string name in new[] { "channelSeconds", "damagePerSecond", "vulnerabilityPerStack", "secondsPerStack", "vulnerabilitySeconds", "channelMoveMultiplier" })
            {
                FieldInfo field = typeof(RaybeamAbility).GetField(name, Any);
                Assert.NotNull(field, name);
                Assert.NotNull(System.Attribute.GetCustomAttribute(field, typeof(UnityEngine.TooltipAttribute)), name + " has no tooltip");
                StringAssert.Contains("\n  " + name + ":", yaml, name + " is not on Raybeam.prefab");
            }
        }
    }
}
