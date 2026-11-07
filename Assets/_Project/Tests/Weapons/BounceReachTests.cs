using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>A bounce can forfeit part of the range the shot has left (BounceOffWalls.rangeKeptAfterBounce): the leg after each bounce is shorter,
    /// the same at any frame rate. Real ProjectileMotor + BounceOffWalls against box walls in a preview scene, far from the origin like the rattle tests.</summary>
    public class BounceReachTests
    {
        private const float Speed = 58f;
        private const float Radius = 0.09f;
        private const float MaxRange = 28f;
        private const float Tolerance = 0.08f;

        private static readonly float[] Kept = { 0.75f, 0.5f };

        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private void Wall(Vector3 centre, string name)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = new Vector3(400f, 6f, 1f);
            Physics.SyncTransforms();
        }

        /// <summary>Records the centre of the projectile at every surviving hit and where it ends. Added BEFORE BounceOffWalls so it reads the
        /// position before the bounce lifts the shot off the wall.</summary>
        private class PathRecorder : MonoBehaviour, IProjectileBehaviour
        {
            public readonly List<Vector3> Hits = new List<Vector3>();
            public Vector3 Final;

            public void OnSpawned(ProjectileMotor motor, ProjectileContext shot) => Hits.Clear();

            public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext shot, RaycastHit hit, IDamageable victim)
            {
                Hits.Add(motor.transform.position);
                return ProjectileHitResponse.Despawn;
            }

            public void OnExpired(ProjectileMotor motor, ProjectileContext shot) => Final = motor.transform.position;
        }

        private PathRecorder Fly(Vector3 shooter, float[] kept, float dt)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Bullet", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = shooter;

            ProjectileMotor motor = go.AddComponent<ProjectileMotor>();
            PathRecorder recorder = go.AddComponent<PathRecorder>();
            BounceOffWalls bounce = go.AddComponent<BounceOffWalls>();
            typeof(BounceOffWalls).GetField("rangeKeptAfterBounce", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(bounce, kept);
            typeof(ProjectileMotor).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(motor, null);
            motor.Initialize(new ProjectileContext(-1, Speed, Radius, MaxRange, 4f, 1, 0, Vector3.forward, Vector3.zero));

            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < 20000 && motor.IsAlive; i++)
                motor.Step(dt, physics);

            Assert.IsFalse(motor.IsAlive, $"dt={dt:F5}: the shot never ended");
            return recorder;
        }

        private static float LegAfterOneHeadOnBounce(PathRecorder r, Vector3 shooter, out float legBefore)
        {
            Assert.AreEqual(1, r.Hits.Count, "one wall, one bounce");
            legBefore = Vector3.Distance(shooter, r.Hits[0]);
            return Vector3.Distance(r.Hits[0], r.Final);
        }

        [Test]
        public void AHeadOnBounceFliesBackOnlyTheKeptShareOfWhatWasLeft()
        {
            Wall(new Vector3(90f, 2f, 115f), "Wall");
            Vector3 shooter = new Vector3(90f, 2f, 90f);

            PathRecorder r = Fly(shooter, Kept, 1f / 60f);
            float back = LegAfterOneHeadOnBounce(r, shooter, out float before);

            Assert.AreEqual(Kept[0] * (MaxRange - before), back, Tolerance);
        }

        [Test]
        public void TheShortenedLegIsTheSameAtDifferentFrameRates()
        {
            Wall(new Vector3(90f, 2f, 115f), "Wall");
            Vector3 shooter = new Vector3(90f, 2f, 90f);

            float slow = LegAfterOneHeadOnBounce(Fly(shooter, Kept, 1f / 60f), shooter, out _);
            float fast = LegAfterOneHeadOnBounce(Fly(shooter, Kept, 1f / 470f), shooter, out _);

            Assert.AreEqual(slow, fast, 0.05f, "the shrink is applied after the step's refund, so a big step forfeits no extra range");
        }

        [Test]
        public void WithoutAListABounceDoesNotShortenTheShot()
        {
            Wall(new Vector3(90f, 2f, 115f), "Wall");
            Vector3 shooter = new Vector3(90f, 2f, 90f);

            PathRecorder r = Fly(shooter, new float[0], 1f / 60f);
            float back = LegAfterOneHeadOnBounce(r, shooter, out float before);

            Assert.AreEqual(MaxRange - before, back, Tolerance);
        }

        [Test]
        public void TheSecondBounceKeepsItsShareOfWhatTheFirstLegLeft()
        {
            // Two parallel walls close together: the shot bounces off the far one, then the near one, then flies on until its range is spent.
            Wall(new Vector3(90f, 2f, 101f), "WallA");
            Wall(new Vector3(90f, 2f, 85f), "WallB");
            Vector3 shooter = new Vector3(90f, 2f, 95f);

            PathRecorder r = Fly(shooter, Kept, 1f / 60f);

            Assert.AreEqual(2, r.Hits.Count, "two bounces");
            float leg0 = Vector3.Distance(shooter, r.Hits[0]);
            float allowedAfterFirst = Kept[0] * (MaxRange - leg0);
            float leg1 = Vector3.Distance(r.Hits[0], r.Hits[1]);
            Assert.Less(leg1, allowedAfterFirst - 1f, "the rig must let the shot reach the second wall");
            float expectedFinal = Kept[1] * (allowedAfterFirst - leg1);

            Assert.AreEqual(expectedFinal, Vector3.Distance(r.Hits[1], r.Final), Tolerance);
        }

        [Test]
        public void TheBounceHandsTheRuleToTheMotorAndTheMotorShrinksAfterTheRefund()
        {
            MethodInfo rule = typeof(BounceRules).GetMethod(nameof(BounceRules.RangeKept));
            MethodInfo schedule = typeof(ProjectileMotor).GetMethod(nameof(ProjectileMotor.ScheduleRangeKept));
            Assert.IsTrue(IlWiring.Uses(typeof(BounceOffWalls), nameof(BounceOffWalls.OnHit), rule), "a bounce asks the tested rule");
            Assert.IsTrue(IlWiring.Uses(typeof(BounceOffWalls), nameof(BounceOffWalls.OnHit), schedule), "a bounce hands the answer to the motor");

            MethodInfo step = typeof(ProjectileMotor).GetMethod(nameof(ProjectileMotor.Step));
            List<int> shrinks = IlWiring.CallOffsets(step, typeof(RangeBudget).GetMethod(nameof(RangeBudget.ShrinkRemaining)));
            List<int> refunds = IlWiring.CallOffsets(step, typeof(RangeBudget).GetMethod(nameof(RangeBudget.Refund)));
            Assert.IsNotEmpty(shrinks, "the motor shrinks the budget");
            Assert.IsNotEmpty(refunds);
            Assert.Greater(shrinks[0], refunds[0], "the shrink comes after the refund, or the reach depends on the frame rate");
        }

        [Test]
        public void TheBounceGunsPrefabListsAKeptShareForEveryBounceItAllows()
        {
            const string path = "Assets/_Project/Design/Weapons/Projectiles/Burst Bullet - Bounce.prefab";
            BounceOffWalls bounce = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BounceOffWalls>();
            Assert.IsNotNull(bounce);

            float[] kept = bounce.RangeKeptAfterBounce;
            Assert.IsNotNull(kept);
            Assert.IsNotEmpty(kept, "the Bounce gun's bounces shorten the shot");
            foreach (float value in kept)
            {
                Assert.Greater(value, 0f);
                Assert.LessOrEqual(value, 1f);
            }
        }
    }
}
