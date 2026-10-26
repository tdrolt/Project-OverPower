using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The mech's run / idle / shoot bools as levels worked out every frame: a held trigger shoots only while the weapon may fire,
    /// a charge weapon never shoots while charging and every shot holds the pose a minimum time. Then the wiring: animScript asks this rule
    /// with the router's input and the weapon's state, and never reads the raw keyboard or mouse again.</summary>
    public class MechAnimParamsTests
    {
        private const float Pose = 0.2f;
        private const float NeverShot = float.PositiveInfinity;
        private static readonly Vector2 Still = Vector2.zero;
        private static readonly Vector2 Forward = new Vector2(0f, 1f);

        [Test]
        public void MovingRunsAndStandingStillIdles()
        {
            MechAnimParams moving = MechAnimParams.For(false, Forward, true, false, NeverShot, Pose);
            Assert.IsTrue(moving.Run);
            Assert.IsFalse(moving.Idle);

            MechAnimParams still = MechAnimParams.For(false, Still, true, false, NeverShot, Pose);
            Assert.IsFalse(still.Run);
            Assert.IsTrue(still.Idle);

            MechAnimParams noise = MechAnimParams.For(false, new Vector2(0.01f, 0.01f), true, false, NeverShot, Pose);
            Assert.IsFalse(noise.Run, "a stick resting a hair off centre is not running");
            Assert.IsTrue(noise.Idle);
        }

        [Test]
        public void RunAndIdleAreNeverBothOrNeitherTrue()
        {
            foreach (Vector2 axis in new[] { Still, Forward, new Vector2(-0.7f, 0.7f), new Vector2(0.05f, 0f) })
            {
                MechAnimParams p = MechAnimParams.For(false, axis, true, false, NeverShot, Pose);
                Assert.AreNotEqual(p.Run, p.Idle, "axis " + axis);
            }
        }

        [Test]
        public void HoldingTheTriggerShootsForAsLongAsItIsHeld()
        {
            Assert.IsTrue(MechAnimParams.For(true, Still, true, false, NeverShot, Pose).Shoot, "held, standing");
            Assert.IsTrue(MechAnimParams.For(true, Forward, true, false, NeverShot, Pose).Shoot, "held, running");
            Assert.IsTrue(MechAnimParams.For(true, Still, true, false, 5f, Pose).Shoot, "held long after the last shot (between automatic shots)");
        }

        [Test]
        public void LettingGoStopsShootingOnceThePoseHasPlayed()
        {
            Assert.IsFalse(MechAnimParams.For(false, Still, true, false, NeverShot, Pose).Shoot, "never fired");
            Assert.IsFalse(MechAnimParams.For(false, Forward, true, false, 0.5f, Pose).Shoot, "released half a second after the last shot");
        }

        [Test]
        public void AQuickTapStillHoldsTheShootPoseTheMinimumTime()
        {
            Assert.IsTrue(MechAnimParams.For(false, Still, true, false, 0f, Pose).Shoot, "the frame the tap's shot left");
            Assert.IsTrue(MechAnimParams.For(false, Still, true, false, 0.19f, Pose).Shoot, "just inside the minimum");
            Assert.IsFalse(MechAnimParams.For(false, Still, true, false, 0.21f, Pose).Shoot, "just past it");
        }

        [Test]
        public void AChargeWeaponDoesNotShootWhileCharging()
        {
            Assert.IsFalse(MechAnimParams.For(true, Still, true, true, NeverShot, Pose).Shoot, "holding to charge, nothing fired yet");
            Assert.IsFalse(MechAnimParams.For(true, Forward, true, true, 3f, Pose).Shoot, "charging again long after the last shot");
        }

        [Test]
        public void AChargeShotPulsesTheShootPoseOnRelease()
        {
            Assert.IsTrue(MechAnimParams.For(false, Still, true, true, 0f, Pose).Shoot, "the release fired the charged shot");
            Assert.IsTrue(MechAnimParams.For(false, Forward, true, true, 0.1f, Pose).Shoot, "still inside the pulse, running");
            Assert.IsFalse(MechAnimParams.For(false, Still, true, true, 0.3f, Pose).Shoot, "the pulse is over");
            Assert.IsTrue(MechAnimParams.For(true, Still, true, true, 0.05f, Pose).Shoot,
                "pressed again right after the shot: the pulse still plays out");
        }

        [Test]
        public void AWeaponThatMayNotFireNeverShoots()
        {
            // mayFire is false when dead, stunned, silenced by overheat, or the gun is switched off.
            Assert.IsFalse(MechAnimParams.For(true, Still, false, false, NeverShot, Pose).Shoot, "held while unable to fire");
            Assert.IsFalse(MechAnimParams.For(true, Forward, false, false, 0f, Pose).Shoot, "a stun lands the frame a shot left: the pose is cut");
            Assert.IsFalse(MechAnimParams.For(false, Still, false, true, 0.05f, Pose).Shoot, "a charge shot's pulse is cut by death");
        }

        [Test]
        public void AZeroPoseLengthMeansOnlyTheHeldTriggerShoots()
        {
            Assert.IsFalse(MechAnimParams.For(false, Still, true, false, 0f, 0f).Shoot);
            Assert.IsFalse(MechAnimParams.For(false, Still, true, true, 0f, 0f).Shoot);
            Assert.IsTrue(MechAnimParams.For(true, Still, true, false, 0f, 0f).Shoot);
        }

        // ---- wiring ----

        private static readonly MethodInfo Rule = typeof(MechAnimParams).GetMethod(nameof(MechAnimParams.For));
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo Getter(System.Type type, string property) => type.GetProperty(property, Any).GetGetMethod(true);

        [Test]
        public void AnimScriptAsksTheRuleEveryFrame()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Rule), "animScript.ApplyParams must ask MechAnimParams.For");
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "Update", typeof(animScript).GetMethod("ApplyParams", Any)),
                "Update must run ApplyParams every frame");
        }

        [Test]
        public void AnimScriptFeedsTheRuleTheRoutersInputAndTheWeaponsState()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.PrimaryHeld))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.MoveAxis))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(WeaponFiring), nameof(WeaponFiring.MayFire))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(WeaponFiring), nameof(WeaponFiring.LastShotTime))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(GameplayConfig), nameof(GameplayConfig.ShootPoseMinimumSeconds))));
            // Without the weapon's own charge flag the Charge gun would hold the shoot pose all the time its trigger is held to charge.
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(WeaponFiring), nameof(WeaponFiring.Weapon))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(WeaponDefinition), nameof(WeaponDefinition.CanCharge))),
                "a charge weapon must not show the shoot pose while charging");
        }

        [Test]
        public void AnimScriptHandsAllThreeAnswersToTheAnimator()
        {
            foreach (string field in new[] { nameof(MechAnimParams.Run), nameof(MechAnimParams.Idle), nameof(MechAnimParams.Shoot) })
                Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", typeof(MechAnimParams).GetField(field)), field + " is never read");
        }

        [Test]
        public void AnimScriptNoLongerReadsTheRawKeyboardOrMouse()
        {
            MethodBase getKey = typeof(Input).GetMethod(nameof(Input.GetKey), new[] { typeof(KeyCode) });
            MethodBase mouseDown = typeof(Input).GetMethod(nameof(Input.GetMouseButtonDown), new[] { typeof(int) });
            MethodBase mouseUp = typeof(Input).GetMethod(nameof(Input.GetMouseButtonUp), new[] { typeof(int) });
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(animScript), getKey), "W/A/S/D read raw: the mech runs while typing in chat");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(animScript), mouseDown), "a press edge can be missed and stick the shoot pose");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(animScript), mouseUp), "a release edge can be missed and stick the shoot pose");
        }

        [Test]
        public void TheWeaponRecordsWhenAShotLeftAndAsksTheCastGateWhetherItMayFire()
        {
            MethodInfo setter = typeof(WeaponFiring).GetProperty(nameof(WeaponFiring.LastShotTime), Any).GetSetMethod(true);
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "TryFire", setter), "TryFire must stamp LastShotTime when a shot is committed");
            MethodInfo forActor = typeof(Overpower.Combat.CastGate).GetMethod(nameof(Overpower.Combat.CastGate.ForActor));
            MethodInfo actorBlock = typeof(WeaponFiring).GetMethod("ActorBlock", Any);
            Assert.IsNotNull(actorBlock, "WeaponFiring.ActorBlock is the one place the weapon asks the cast gate");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "ActorBlock", forActor));
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "TryFire", actorBlock), "TryFire refuses on the same answer");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "get_" + nameof(WeaponFiring.MayFire), actorBlock),
                "MayFire must ask the same gate TryFire does");
        }

        [Test]
        public void ThePlayerPrefabsMechHasTheGameplayConfig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Multiplayer Player.prefab");
            Assert.IsNotNull(prefab);
            animScript script = prefab.GetComponentInChildren<animScript>(true);
            Assert.IsNotNull(script);
            object config = typeof(animScript).GetField("gameplayConfig", Any)?.GetValue(script);
            Assert.IsNotNull(config as GameplayConfig, "the mech has no Gameplay Config: the shoot pose minimum would be lost");
        }
    }
}
