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
    /// <summary>
    /// THE WALL-RATTLE FIX (2026-09-21). Tudor measured the Bounce gun (weapon 7) dealing LESS
    /// damage per trigger pull after a bounce than a direct hit does - the opposite of
    /// BounceOffWalls' own "+20% per bounce" design. The investigation (scratchpad/bounce/) found
    /// the cause in ProjectileMotor: a bullet left resting exactly on the wall it just bounced off
    /// was read as hitting that SAME wall again on the very next sweep - Unity's documented
    /// "initial overlap" signature (RaycastHit.distance 0, normal the sweep direction reversed) -
    /// so it kept reflecting about an artifact instead of flying away, burning its bounce budget on
    /// the wall it should have left. Measured worse at 65-120m from the origin (float precision),
    /// which is why every test below places its geometry out there rather than near (0,0,0) - a
    /// probe near the origin (scratchpad/bounce_overlap_probe.cs) did not reproduce it at all.
    ///
    /// Drives a REAL ProjectileMotor + BounceOffWalls (and, for the pierce test, a minimal stand-in
    /// for a projectile that pierces) against real BoxColliders in an edit-mode preview scene, the
    /// same pattern FireFieldBurnZoneTests/GroundSnapTests use - via ProjectileMotor.Step(deltaTime,
    /// physicsScene), the seam added for exactly this (see its own comment and ProjectileMotor's
    /// class comment). Awake is invoked by reflection rather than trusted to fire on AddComponent,
    /// the same caution PlayerHealthImmuneTintTests takes - it only recomputes GetComponents, so
    /// calling it explicitly is always safe even if Unity already ran it.
    /// </summary>
    public class BounceOffWallsRattleTests
    {
        // The real Bounce weapon's own numbers (07 Burst - Bounce.asset / Burst Bullet -
        // Bounce.prefab, 2026-09-21) - used everywhere a test does not need a wider range budget
        // for its own geometry (the corner and pierce tests widen it, since they are not trying to
        // match the real weapon's per-pull numbers, only the bounce-counting logic).
        private const float WeaponSpeed = 58f;
        private const float WeaponRadius = 0.09f;
        private const float WeaponMaxRange = 28f;
        private const float WeaponDamage = 4.4f;

        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        // ---- rig helpers -----------------------------------------------------------------------

        private GameObject Wall(Vector3 centre, Vector3 size, string name)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            return go;
        }

        private static void InvokeAwake(ProjectileMotor motor)
        {
            MethodInfo awake = typeof(ProjectileMotor).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(awake, "ProjectileMotor.Awake");
            awake.Invoke(motor, null);
        }

        private GameObject MakeBullet(Vector3 position, Vector3 direction, System.Action<GameObject> addBehaviours = null,
                                      float speed = WeaponSpeed, float radius = WeaponRadius,
                                      float maxRange = WeaponMaxRange, float damage = WeaponDamage)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Bullet", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;

            ProjectileMotor motor = go.AddComponent<ProjectileMotor>();
            addBehaviours?.Invoke(go);
            InvokeAwake(motor); // populates ProjectileMotor.behaviours before Initialize calls OnSpawned on them.

            ProjectileContext context = new ProjectileContext(-1, speed, radius, maxRange, damage, 1, 0,
                                                                direction, Vector3.zero);
            motor.Initialize(context);
            return go;
        }

        /// <summary>A projectile behaviour that pierces exactly ONE wall then stops on the next -
        /// IProjectileBehaviour.OnHit's own documented pattern ("motor.Ignore(hit.collider) then
        /// return KeepFlying to pierce"). The real Pierce component (Weapons/Effects/Pierce.cs) is
        /// a Hitscan/BeamResolver setting for laser weapons, not an IProjectileBehaviour - nothing
        /// in this codebase pierces a swept projectile today, so this is the smallest faithful
        /// stand-in for the pattern the interface itself documents, used to prove the rattle fix's
        /// TrySweep changes did not disturb it.</summary>
        private class PierceOnce : MonoBehaviour, IProjectileBehaviour
        {
            private bool used;

            public void OnSpawned(ProjectileMotor motor, ProjectileContext shot) => used = false;

            public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext shot, RaycastHit hit, IDamageable victim)
            {
                if (used)
                    return ProjectileHitResponse.Despawn;

                used = true;
                motor.Ignore(hit.collider);
                return ProjectileHitResponse.KeepFlying;
            }

            public void OnExpired(ProjectileMotor motor, ProjectileContext shot) { }
        }

        // ---- the red test: many oblique angles, several deltaTimes, all at large coordinates ----

        /// <summary>
        /// A flat wall at (90, *, 115) - inside the 65-120m-from-origin band the investigation
        /// measured the rattle in - hit from 20m south at angleDeg off head-on, stepped at dt. On
        /// today's (pre-fix) code this fails: the first bounce is real, but the very next Step call
        /// reads the resting sphere as hitting the same wall again (DamageMultiplier climbs a
        /// second time at essentially the same point), which is exactly the BOUNCE log signature
        /// the investigation captured (events.log / realgeo.log: repeated bounces at one point with
        /// the normal flipping sign each time). After the fix, exactly one bounce fires, the
        /// resulting direction matches Vector3.Reflect off the wall's own normal, and the bullet
        /// keeps moving away from its own bounce point instead of rattling.
        /// </summary>
        [TestCase(0f, 1f / 60f)]
        [TestCase(0f, 1f / 144f)]
        [TestCase(0f, 1f / 470f)]
        [TestCase(20f, 1f / 60f)]
        [TestCase(20f, 1f / 144f)]
        [TestCase(20f, 1f / 470f)]
        [TestCase(-20f, 1f / 60f)]
        [TestCase(35f, 1f / 60f)]
        [TestCase(35f, 1f / 144f)]
        [TestCase(35f, 1f / 470f)]
        [TestCase(-35f, 1f / 144f)]
        [TestCase(50f, 1f / 60f)]
        [TestCase(50f, 1f / 470f)]
        [TestCase(-50f, 1f / 60f)]
        [TestCase(65f, 1f / 144f)]
        public void ABouncedBulletFliesAwayFromTheWallInsteadOfRattling(float angleDeg, float dt)
        {
            RunOneWallBounceScenario(angleDeg, step => dt, $"angle={angleDeg} dt={dt:F5}");
        }

        /// <summary>The brief's fourth deltaTime: a variable, Editor-like frame time rather than a
        /// fixed one, cycling through a spread of real and near-real frame times so no single dt's
        /// own arithmetic happens to hide the bug.</summary>
        [Test]
        public void ABouncedBulletFliesAwayEvenWithAVariableEditorLikeFrameTime()
        {
            float[] variableDts = { 1f / 60f, 1f / 144f, 1f / 470f, 1f / 85f, 1f / 240f, 1f / 55f };
            RunOneWallBounceScenario(28f, step => variableDts[step % variableDts.Length], "variable dt");
        }

        private void RunOneWallBounceScenario(float angleDeg, System.Func<int, float> dtForStep, string label)
        {
            Wall(new Vector3(90f, 2f, 115f), new Vector3(400f, 6f, 1f), "Wall");

            // 8m standoff, not 20 - the widest angle tested (65 deg) needs distance/cos(angle) to
            // reach the wall within the real 28m Max Range budget (8/cos(65) = ~18.9m), leaving
            // room in the budget to also confirm it flies on afterwards.
            Vector3 shooter = new Vector3(90f, 2f, 107f);
            Vector3 incidentDir = (Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward).normalized;

            GameObject bulletGo = MakeBullet(shooter, incidentDir, go => go.AddComponent<BounceOffWalls>());
            ProjectileMotor motor = bulletGo.GetComponent<ProjectileMotor>();
            PhysicsScene physicsScene = scene.GetPhysicsScene();

            // ---- phase 1: fly to the wall and take exactly one bounce ----
            float lastMultiplier = 1f;
            Vector3 bouncePoint = default;
            Vector3 directionBeforeBounce = incidentDir;
            bool bounced = false;

            for (int i = 0; i < 2000 && motor.IsAlive; i++)
            {
                Vector3 dirBeforeStep = motor.Direction;
                motor.Step(dtForStep(i), physicsScene);
                if (!motor.IsAlive)
                    break;

                float multiplier = motor.Context.DamageMultiplier;
                if (multiplier > lastMultiplier + 0.001f)
                {
                    lastMultiplier = multiplier;
                    bouncePoint = motor.transform.position;
                    directionBeforeBounce = dirBeforeStep;
                    bounced = true;
                    break;
                }
            }

            Assert.IsTrue(bounced, $"{label}: never registered the expected bounce off the wall at all");
            Assert.IsTrue(motor.IsAlive, $"{label}: despawned instead of flying on after its one bounce");

            Vector3 expectedReflected = Vector3.Reflect(directionBeforeBounce, Vector3.back);
            Assert.Less(Vector3.Distance(motor.Direction, expectedReflected), 0.01f,
                $"{label}: post-bounce direction {motor.Direction:F3} is not the reflection of " +
                $"{directionBeforeBounce:F3} off the wall - expected {expectedReflected:F3}");

            // ---- phase 2: it must fly on and AWAY, never rattling at the same resting point ----
            float maxDistanceFromBounce = 0f;
            for (int j = 0; j < 80 && motor.IsAlive; j++)
            {
                motor.Step(dtForStep(2000 + j), physicsScene);
                if (!motor.IsAlive)
                    break;

                float multiplier = motor.Context.DamageMultiplier;
                Assert.LessOrEqual(multiplier, lastMultiplier + 0.001f,
                    $"{label}: RATTLED - damage multiplier climbed again to {multiplier:F3} at " +
                    $"{motor.transform.position:F3} (the real bounce was at {bouncePoint:F3}), meaning the " +
                    "resting sphere was read as hitting the same wall a second time");

                maxDistanceFromBounce = Mathf.Max(maxDistanceFromBounce, Vector3.Distance(motor.transform.position, bouncePoint));
            }

            Assert.Greater(maxDistanceFromBounce, WeaponRadius * 4f,
                $"{label}: never moved meaningfully away from its own bounce point at {bouncePoint:F3} - " +
                "stuck rather than flying on");
        }

        // ---- an inside corner: two real walls, two genuine bounces --------------------------

        /// <summary>
        /// Two flat walls meeting at a right angle far from the origin (a corner, not one wall hit
        /// twice) - a bullet fired diagonally into it must take exactly two REAL bounces, one off
        /// each wall, and the fix's per-collider guard (IsRestingOverlapOnLastBounce only ever
        /// exempts the wall a bounce JUST happened against) must not swallow the second wall's
        /// genuine hit just because it follows a bounce closely.
        /// </summary>
        [Test]
        public void AnInsideCornerGivesTwoGenuineBounces()
        {
            Wall(new Vector3(100f, 2f, 90f), new Vector3(1f, 8f, 100f), "CornerWallA"); // x=100 plane, spans z 40..140
            Wall(new Vector3(70f, 2f, 100f), new Vector3(100f, 8f, 1f), "CornerWallB"); // z=100 plane, spans x 20..120

            Vector3 shooter = new Vector3(70f, 2f, 70f);
            Vector3 incidentDir = new Vector3(3f, 0f, 1f).normalized; // hits Wall A (x=100) well before reaching z=100.

            GameObject bulletGo = MakeBullet(shooter, incidentDir, go => go.AddComponent<BounceOffWalls>(), maxRange: 150f);
            ProjectileMotor motor = bulletGo.GetComponent<ProjectileMotor>();
            PhysicsScene physicsScene = scene.GetPhysicsScene();

            float lastMultiplier = 1f;
            int bounces = 0;
            float dt = 1f / 60f;

            for (int i = 0; i < 3000 && motor.IsAlive && bounces < 2; i++)
            {
                motor.Step(dt, physicsScene);
                if (!motor.IsAlive)
                    break;

                float multiplier = motor.Context.DamageMultiplier;
                if (multiplier > lastMultiplier + 0.001f)
                {
                    bounces++;
                    lastMultiplier = multiplier;
                }
            }

            Assert.AreEqual(2, bounces, "expected one bounce off each wall of the corner");
            Assert.IsTrue(motor.IsAlive, "should have flown on after the second bounce, not despawned at the corner");

            // Confirm it is genuinely leaving the corner afterwards, not rattling on wall B.
            float maxAfter = 0f;
            Vector3 afterSecondBounce = motor.transform.position;
            for (int j = 0; j < 40 && motor.IsAlive; j++)
            {
                motor.Step(dt, physicsScene);
                if (!motor.IsAlive)
                    break;

                Assert.LessOrEqual(motor.Context.DamageMultiplier, lastMultiplier + 0.001f,
                    "a third 'bounce' fired after the corner's two real ones - rattling on the second wall");
                maxAfter = Mathf.Max(maxAfter, Vector3.Distance(motor.transform.position, afterSecondBounce));
            }

            Assert.Greater(maxAfter, WeaponRadius * 4f, "never moved away from the corner after its second bounce");
        }

        // ---- regression guarantees: a non-bouncing bullet, and one that starts touching a wall ----

        /// <summary>No behaviours at all - the baseline ProjectileMotor contract untouched by this
        /// fix: a wall still stops a shot outright rather than the shot tunnelling through it.</summary>
        [Test]
        public void ANonBouncingBulletStillStopsOnTheWallInsteadOfTunnelling()
        {
            Wall(new Vector3(80f, 2f, 100f), new Vector3(40f, 6f, 1f), "Wall"); // near face ~z=99.5

            Vector3 shooter = new Vector3(80f, 2f, 80f); // ~19.4m standoff to the near face.
            GameObject bulletGo = MakeBullet(shooter, Vector3.forward); // plain bullet, no behaviours.
            ProjectileMotor motor = bulletGo.GetComponent<ProjectileMotor>();
            PhysicsScene physicsScene = scene.GetPhysicsScene();

            float dt = 1f / 60f;
            float lastTravelled = 0f;
            for (int i = 0; i < 400 && motor.IsAlive; i++)
            {
                lastTravelled = motor.Range.Travelled;
                motor.Step(dt, physicsScene);
            }

            Assert.IsFalse(motor.IsAlive, "a plain bullet fired at a wall must despawn on it, not fly forever");
            Assert.Less(lastTravelled, 22f, $"travelled {lastTravelled:F2}m before stopping - well past the ~19.4m " +
                                             "standoff, which looks like tunnelling through the wall");
            Assert.Greater(lastTravelled, 15f, $"travelled only {lastTravelled:F2}m - stopped suspiciously short of the wall");
        }

        /// <summary>
        /// SafeMuzzlePosition already pulls a player's MUZZLE back off a wall it is hugging, but
        /// that guarantee is about the muzzle, not every projectile that could ever exist (a
        /// hand-placed one, an ability shot) - a shot that genuinely starts touching or inside a
        /// wall it has never bounced off must still register that hit. justBouncedOffCollider is
        /// null for a fresh bullet, so IsRestingOverlapOnLastBounce cannot exempt this collider no
        /// matter what the sweep reports for it.
        /// </summary>
        [Test]
        public void ABulletFiredWhileTouchingAWallFacingIntoItStillHitsIt()
        {
            Wall(new Vector3(80f, 2f, 100f), new Vector3(40f, 6f, 1f), "Wall"); // near face at z=99.5

            Vector3 start = new Vector3(80f, 2f, 99.5f - WeaponRadius); // exactly touching the near face.
            GameObject bulletGo = MakeBullet(start, Vector3.forward);
            ProjectileMotor motor = bulletGo.GetComponent<ProjectileMotor>();
            PhysicsScene physicsScene = scene.GetPhysicsScene();

            motor.Step(1f / 60f, physicsScene);

            Assert.IsFalse(motor.IsAlive, "a bullet starting flush against a wall, aimed straight into it, " +
                                           "must register that hit on its very first sweep, not pass through");
        }

        // ---- pierce (the general Ignore + KeepFlying pattern) still works --------------------

        [Test]
        public void APiercingBulletIgnoresTheFirstWallAndStopsOnTheSecond()
        {
            Wall(new Vector3(80f, 2f, 100f), new Vector3(40f, 6f, 1f), "NearWall");  // near face ~z=99.5, standoff ~19.4m
            Wall(new Vector3(80f, 2f, 130f), new Vector3(40f, 6f, 1f), "FarWall");   // near face ~z=129.5, standoff ~49.4m

            Vector3 shooter = new Vector3(80f, 2f, 80f);
            GameObject bulletGo = MakeBullet(shooter, Vector3.forward, go => go.AddComponent<PierceOnce>(), maxRange: 60f);
            ProjectileMotor motor = bulletGo.GetComponent<ProjectileMotor>();
            PhysicsScene physicsScene = scene.GetPhysicsScene();

            float dt = 1f / 60f;
            float lastTravelled = 0f;
            for (int i = 0; i < 600 && motor.IsAlive; i++)
            {
                lastTravelled = motor.Range.Travelled;
                motor.Step(dt, physicsScene);
            }

            Assert.IsFalse(motor.IsAlive, "must eventually despawn on the far wall");
            Assert.Greater(lastTravelled, 40f, $"stopped after only {lastTravelled:F2}m - at or before the near wall, " +
                                                "so Ignore()+KeepFlying (the pierce pattern) did not carry it through");
            Assert.Less(lastTravelled, 55f, $"travelled {lastTravelled:F2}m - past the far wall too, it should have stopped there");
        }
    }
}
