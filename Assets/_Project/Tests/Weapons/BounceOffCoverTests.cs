using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// A bouncing shot ricochets off its shooter's own Cover wall and stops on everything else that has health. The rule that picks the wall is
    /// BounceRulesTests; this file proves BounceOffWalls asks it, and that a victim that is not a Cover wall still ends the shot.
    /// </summary>
    public class BounceOffCoverTests
    {
        private const float ShotSpeed = 50f;
        private const float ShotRadius = 0.1f;
        private const float ShotRange = 40f;
        private const float ShotDamage = 5f;
        private const int ShooterActor = 3;
        private const int ShooterTeam = 0;

        /// <summary>Something with health on the other team, standing where a Cover wall would: not a CoverWall, so never the shooter's own cover.</summary>
        private class FakeBody : MonoBehaviour, IDamageable
        {
            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive => true;
            public int TeamId => 1;
            public int ActorNumber => 9;
            public bool HasLocalAuthority => true;
        }

        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject Wall(Vector3 centre, Vector3 size)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Wall", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            return go;
        }

        private ProjectileMotor BouncingShot(Vector3 position, Vector3 direction)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Bullet", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;

            ProjectileMotor motor = go.AddComponent<ProjectileMotor>();
            go.AddComponent<BounceOffWalls>();
            MethodInfo awake = typeof(ProjectileMotor).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(awake, "ProjectileMotor.Awake");
            awake.Invoke(motor, null); // fills the motor's behaviour list before Initialize calls OnSpawned on them

            motor.Initialize(new ProjectileContext(-1, ShotSpeed, ShotRadius, ShotRange, ShotDamage, ShooterActor, ShooterTeam, direction, Vector3.zero));
            return motor;
        }

        [Test]
        public void AShotStillBouncesOffBareGeometry()
        {
            Wall(new Vector3(90f, 2f, 115f), new Vector3(20f, 6f, 1f));
            ProjectileMotor motor = BouncingShot(new Vector3(90f, 2f, 100f), Vector3.forward);
            ProjectileContext shot = motor.Context;
            PhysicsScene physics = scene.GetPhysicsScene();

            for (int i = 0; i < 600 && motor.IsAlive && shot.DamageMultiplier <= 1f; i++)
                motor.Step(1f / 60f, physics);

            Assert.IsTrue(motor.IsAlive, "the shot should have flown on after the wall, not ended on it");
            Assert.Greater(shot.DamageMultiplier, 1f, "the shot never counted a bounce");
        }

        [Test]
        public void AShotThatHitsSomethingWithHealthWhichIsNotCoverStillEndsThere()
        {
            GameObject body = Wall(new Vector3(90f, 2f, 115f), new Vector3(20f, 6f, 1f));
            body.AddComponent<FakeBody>();
            ProjectileMotor motor = BouncingShot(new Vector3(90f, 2f, 100f), Vector3.forward);
            ProjectileContext shot = motor.Context;
            PhysicsScene physics = scene.GetPhysicsScene();

            for (int i = 0; i < 600 && motor.IsAlive; i++)
                motor.Step(1f / 60f, physics);

            Assert.IsFalse(motor.IsAlive, "a victim that is not the shooter's own Cover wall must end the shot");
            Assert.AreEqual(1f, shot.DamageMultiplier, 0.0001f, "it ended on the victim without counting a bounce");
        }

        [Test]
        public void TheBounceAsksTheRuleBeforeBouncingOffCover()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BounceOffWalls), nameof(BounceOffWalls.OnHit),
                typeof(BounceRules).GetMethod(nameof(BounceRules.BouncesOffCover))));
        }

        [Test]
        public void TheRuleIsGivenTheShooterTheShotCarriesAndThePlacerStoredOnTheWall()
        {
            MethodInfo shooter = typeof(ProjectileContext).GetProperty(nameof(ProjectileContext.ShooterActorNumber)).GetGetMethod();
            MethodInfo placer = typeof(CoverWall).GetProperty(nameof(CoverWall.OwnerActor)).GetGetMethod();

            Assert.IsTrue(IlWiring.Uses(typeof(BounceOffWalls), nameof(BounceOffWalls.OnHit), shooter), "the shooter comes off the shot, the same on every client");
            Assert.IsTrue(IlWiring.Uses(typeof(BounceOffWalls), nameof(BounceOffWalls.OnHit), placer), "the wall's owner comes off the wall, the same on every client");
        }
    }
}
