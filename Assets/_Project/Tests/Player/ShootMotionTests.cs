using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The shoot motion follows the real shots: one motion per shot on every client (the shooter's own at once, everyone else's
    /// when the shot arrives, never twice for the shooter's echo), sped up so one motion fits between two shots of a fast gun, and a
    /// remote copy holds the pose after a shot it saw even when the streamed shoot bool lands late.</summary>
    public class ShootMotionTests
    {
        private const float Clip = 0.5f;
        private const float Cap = 10f;

        [Test]
        public void AFastGunPlaysTheMotionFastEnoughToFitBetweenTwoShots()
        {
            Assert.AreEqual(2f, ShootMotion.Speed(Clip, 0.25f, Cap), 1e-5f);
            Assert.AreEqual(5f, ShootMotion.Speed(Clip, 0.1f, Cap), 1e-5f);
        }

        [Test]
        public void ASlowGunPlaysTheMotionAtItsOwnSpeed()
        {
            Assert.AreEqual(1f, ShootMotion.Speed(Clip, 0.5f, Cap), 1e-5f);
            Assert.AreEqual(1f, ShootMotion.Speed(Clip, 1.2f, Cap), "a slow gun never slows the motion down");
        }

        [Test]
        public void TheSpeedUpStopsAtTheCap()
        {
            Assert.AreEqual(4f, ShootMotion.Speed(Clip, 0.05f, 4f), 1e-5f);
            Assert.AreEqual(4f, ShootMotion.Speed(Clip, 0f, 4f), 1e-5f, "no interval at all is as fast as allowed");
        }

        [Test]
        public void ACapBelowOneOrNoClipNeverChangesTheMotionsSpeed()
        {
            Assert.AreEqual(1f, ShootMotion.Speed(Clip, 0.1f, 0.5f), 1e-5f);
            Assert.AreEqual(1f, ShootMotion.Speed(Clip, 1f, 0.5f), 1e-5f);
            Assert.AreEqual(1f, ShootMotion.Speed(0f, 0.1f, Cap), 1e-5f);
        }

        [Test]
        public void TheShootersOwnEchoDoesNotRestartTheMotionASecondTime()
        {
            Assert.IsFalse(ShootMotion.ShowsArrivingShot(isOwnShot: true), "the shooter already restarted it when the shot left");
            Assert.IsTrue(ShootMotion.ShowsArrivingShot(isOwnShot: false));
        }

        [Test]
        public void ARemoteCopyHoldsThePoseBrieflyAfterAShotItSaw()
        {
            Assert.IsTrue(ShootMotion.HoldsPose(0f, 0.2f));
            Assert.IsTrue(ShootMotion.HoldsPose(0.19f, 0.2f));
            Assert.IsFalse(ShootMotion.HoldsPose(0.2f, 0.2f));
            Assert.IsFalse(ShootMotion.HoldsPose(float.PositiveInfinity, 0.2f), "never saw a shot");
            Assert.IsFalse(ShootMotion.HoldsPose(0f, 0f), "a zero pose holds nothing");
        }

        [Test]
        public void AShotHoldsThePoseForItsWholeMotion()
        {
            Assert.AreEqual(0.5f, ShootMotion.HoldSeconds(Clip, 1f, 0.2f), 1e-5f, "a slow gun: the whole motion at its own speed");
            Assert.AreEqual(0.4f, ShootMotion.HoldSeconds(Clip, 1.25f, 0.2f), 1e-5f, "sped up: the motion is shorter");
        }

        [Test]
        public void AShotHoldsThePoseAtLeastTheMinimum()
        {
            Assert.AreEqual(0.2f, ShootMotion.HoldSeconds(Clip, 5f, 0.2f), 1e-5f, "a motion shorter than the minimum");
            Assert.AreEqual(0.2f, ShootMotion.HoldSeconds(0f, 1f, 0.2f), 1e-5f, "no clip");
            Assert.AreEqual(0.2f, ShootMotion.HoldSeconds(Clip, 0f, 0.2f), 1e-5f, "a speed of zero never ends the motion; the minimum stands in");
        }

        // ---- wiring ----

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static MethodInfo Rule(string name) => typeof(ShootMotion).GetMethod(name);
        private static MethodInfo Method(System.Type type, string name) => type.GetMethod(name, Any);
        private static MethodInfo Getter(System.Type type, string property) => type.GetProperty(property, Any).GetGetMethod(true);

        [Test]
        public void TheShooterShowsItsShotTheMomentItLeaves()
        {
            MethodInfo raise = Method(typeof(WeaponFiring), "RaiseShotShown");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "TryFire", raise), "TryFire must show the shot it commits");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "RaiseShotShown", typeof(WeaponFiring).GetField("ShotShown", Any)),
                "RaiseShotShown must raise ShotShown");
        }

        [Test]
        public void EveryOtherClientShowsTheShotWhenItArrives()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(WeaponFiring), "RPC_FireWeapon", Rule(nameof(ShootMotion.ShowsArrivingShot))),
                "RPC_FireWeapon must ask whether an arriving shot is shown");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "RPC_FireWeapon", Method(typeof(WeaponFiring), "RaiseShotShown")));
        }

        [Test]
        public void TheMechRestartsItsShootMotionOnEveryShotShown()
        {
            Assert.IsTrue(IlWiring.HandsOver(typeof(MechAnimator), "OnEnable", Method(typeof(MechAnimator), "OnShotShown")),
                "the mech must listen for shots shown");
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "OnShotShown", Rule(nameof(ShootMotion.Speed))));
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "OnShotShown", Getter(typeof(GameplayConfig), nameof(GameplayConfig.ShootMotionMaxSpeed))));
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "OnShotShown", Getter(typeof(GameplayConfig), nameof(GameplayConfig.ShootMotionBlendSeconds))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(MechAnimator), "OnShotShown", Getter(typeof(AnimationClip), nameof(AnimationClip.length))),
                "the speed-up must come from the real clip's length");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(MechAnimator), "OnShotShown",
                typeof(Animator).GetMethod(nameof(Animator.SetFloat), new[] { typeof(int), typeof(float) })), "the speed must reach the Animator");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(MechAnimator), "OnShotShown",
                typeof(Animator).GetMethod(nameof(Animator.CrossFadeInFixedTime), new[] { typeof(int), typeof(float), typeof(int), typeof(float) })),
                "every shot must restart the motion from its first frame");
        }

        [Test]
        public void EveryShotSetsHowLongThePoseIsHeldOnEveryClient()
        {
            FieldInfo hold = typeof(MechAnimator).GetField("shotPoseSeconds", Any);
            Assert.IsNotNull(hold, "MechAnimator.shotPoseSeconds keeps the last shot's pose length");
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "OnShotShown", Rule(nameof(ShootMotion.HoldSeconds))));
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "OnShotShown", Getter(typeof(GameplayConfig), nameof(GameplayConfig.ShootPoseMinimumSeconds))));
            Assert.IsTrue(IlWiring.Stores(typeof(MechAnimator), "OnShotShown", hold), "the shot must remember its pose length");
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "ApplyParams", hold), "the shooter's own pose must last that long");
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "HoldRemoteShotPose", hold), "a remote copy's pose must last that long");
        }

        [Test]
        public void ARemoteCopyHoldsThePoseOnTheRulesAnswer()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MechAnimator), "HoldRemoteShotPose", Rule(nameof(ShootMotion.HoldsPose))));
            Assert.IsTrue(IlWiring.Uses(typeof(MechAnimator), "Update", Method(typeof(MechAnimator), "HoldRemoteShotPose")),
                "Update must run it every frame on a remote copy");
        }

        [Test]
        public void TheMechWritesItsBoolsAfterPhotonsAnimatorSyncHasWrittenItsOwn()
        {
            int Order(System.Type t) => t.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;
            Assert.Greater(Order(typeof(MechAnimator)), Order(typeof(PhotonAnimatorView)),
                "PhotonAnimatorView's Update writes the streamed shoot bool; a remote's held pose must be written after it");
        }

        [Test]
        public void ThePlayerPrefabsMechKnowsTheShootStatesClip()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Design/Resources/Multiplayer Player.prefab");
            MechAnimator script = prefab.GetComponentInChildren<MechAnimator>(true);
            Assert.IsNotNull(script);
            var clip = typeof(MechAnimator).GetField("shootClip", Any)?.GetValue(script) as AnimationClip;
            Assert.IsNotNull(clip, "the mech has no shoot clip: a fast gun's motion would not speed up");

            var controller = (AnimatorController)prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController;
            AnimatorState shoot = controller.layers[0].stateMachine.states.Select(s => s.state).First(s => s.name == MechAnimatorGraphTests.ShootState);
            Assert.AreSame(shoot.motion, clip, "the clip must be the one the Shoot state plays");
        }
    }
}
