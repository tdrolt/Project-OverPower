using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The mech's run / idle / shoot bools as levels worked out every frame: only a shot that left the gun shows the shoot pose, for
    /// as long as that shot asks, and never while the weapon may not fire. Holding the trigger on a gun that cannot fire yet shows nothing. Then
    /// the wiring: animScript asks this rule with the router's movement and the weapon's state, and never reads the held trigger or the raw keyboard.</summary>
    public class MechAnimParamsTests
    {
        private const float Pose = 0.2f;
        private const float NeverShot = float.PositiveInfinity;
        private static readonly Vector2 Still = Vector2.zero;
        private static readonly Vector2 Forward = new Vector2(0f, 1f);

        [Test]
        public void MovingRunsAndStandingStillIdles()
        {
            MechAnimParams moving = MechAnimParams.For(Forward, true, NeverShot, Pose);
            Assert.IsTrue(moving.Run);
            Assert.IsFalse(moving.Idle);

            MechAnimParams still = MechAnimParams.For(Still, true, NeverShot, Pose);
            Assert.IsFalse(still.Run);
            Assert.IsTrue(still.Idle);

            MechAnimParams noise = MechAnimParams.For(new Vector2(0.01f, 0.01f), true, NeverShot, Pose);
            Assert.IsFalse(noise.Run, "a stick resting a hair off centre is not running");
            Assert.IsTrue(noise.Idle);
        }

        [Test]
        public void RunAndIdleAreNeverBothOrNeitherTrue()
        {
            foreach (Vector2 axis in new[] { Still, Forward, new Vector2(-0.7f, 0.7f), new Vector2(0.05f, 0f) })
            {
                MechAnimParams p = MechAnimParams.For(axis, true, NeverShot, Pose);
                Assert.AreNotEqual(p.Run, p.Idle, "axis " + axis);
            }
        }

        [Test]
        public void NoShotNoShootPose()
        {
            // Holding fire while cooling down, overheated, charging or waiting out a wind-up: the gun may fire, but nothing left it.
            Assert.IsFalse(MechAnimParams.For(Still, true, NeverShot, Pose).Shoot, "never fired, standing");
            Assert.IsFalse(MechAnimParams.For(Forward, true, NeverShot, Pose).Shoot, "never fired, running");
            Assert.IsFalse(MechAnimParams.For(Still, true, 0.5f, Pose).Shoot, "the last shot's pose is over");
        }

        [Test]
        public void EveryShotHoldsTheShootPoseForItsOwnLength()
        {
            Assert.IsTrue(MechAnimParams.For(Still, true, 0f, Pose).Shoot, "the frame the shot left");
            Assert.IsTrue(MechAnimParams.For(Forward, true, 0.19f, Pose).Shoot, "just inside the pose, running");
            Assert.IsFalse(MechAnimParams.For(Still, true, 0.21f, Pose).Shoot, "just past it");
        }

        [Test]
        public void AWeaponThatMayNotFireNeverShoots()
        {
            // mayFire is false when dead, stunned, silenced by overheat, or the gun is switched off.
            Assert.IsFalse(MechAnimParams.For(Still, false, NeverShot, Pose).Shoot, "unable to fire, no shot");
            Assert.IsFalse(MechAnimParams.For(Forward, false, 0f, Pose).Shoot, "a stun lands the frame a shot left: the pose is cut");
            Assert.IsFalse(MechAnimParams.For(Still, false, 0.05f, Pose).Shoot, "a shot's pose is cut by death");
        }

        [Test]
        public void AZeroPoseLengthNeverShoots()
        {
            Assert.IsFalse(MechAnimParams.For(Still, true, 0f, 0f).Shoot);
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
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.MoveAxis))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(WeaponFiring), nameof(WeaponFiring.MayFire))));
            Assert.IsTrue(IlWiring.Uses(typeof(animScript), "ApplyParams", Getter(typeof(WeaponFiring), nameof(WeaponFiring.LastShotTime))));
        }

        [Test]
        public void HoldingFireAloneNeverReachesTheShootPose()
        {
            Assert.IsFalse(IlWiring.Uses(typeof(animScript), Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.PrimaryHeld))),
                "a held trigger raised the gun while it could not fire (cooling down, overheated, winding up)");
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
