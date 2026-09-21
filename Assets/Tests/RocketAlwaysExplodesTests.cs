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
    /// Tudor, 2026-09-21: "the rocket launcher only explodes upon impacting with another player or near one. it
    /// should always explode on contact/projectile end". The cause: commit 525abde (2026-09-20, "show what
    /// actually got hit") made ExplodeOnImpact.Detonated carry 0 unless the blast actually damaged something, and
    /// RocketBlastView/SplashShell draw nothing for a radius &lt;= 0 - so a rocket that hit a wall, or reached its
    /// range end, still detonated and still dealt splash, but showed no explosion.
    ///
    /// THE PURE RULE this pins: Detonated always carries the rocket's own full Splash Radius, whatever the blast
    /// actually caught - a hit, a miss, or nothing at all. Drives a real ExplodeOnImpact (the only
    /// IProjectileBehaviour under test) directly through its public OnSpawned/OnHit/OnExpired, in an isolated
    /// edit-mode preview scene so nothing here can ever touch the real, currently-open Game Scene - the same
    /// isolation BounceOffWallsRattleTests uses. splashMask is forced to 0 (matches no layer) on every rig here,
    /// so Physics.OverlapSphereNonAlloc inside Detonate can never catch a real object even by accident: these
    /// tests are about the RADIUS ExplodeOnImpact reports, not about who its splash actually catches.
    /// </summary>
    public class RocketAlwaysExplodesTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private static void InvokeAwake(ExplodeOnImpact explode)
        {
            // Matches BounceOffWallsRattleTests' own caution: harmless whether or not Unity already ran Awake on
            // AddComponent, and it populates buildingMask before anything reads it - not that this rig's own
            // splashMask 0 ever lets a candidate reach the occlusion check anyway.
            MethodInfo awake = typeof(ExplodeOnImpact).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(awake, "ExplodeOnImpact.Awake");
            awake.Invoke(explode, null);
        }

        private static void SetSplashMaskToNothing(ExplodeOnImpact explode)
        {
            FieldInfo field = typeof(ExplodeOnImpact).GetField("splashMask", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, "ExplodeOnImpact.splashMask");
            field.SetValue(explode, (LayerMask)0);
        }

        private ExplodeOnImpact MakeRig()
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Rocket", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            ExplodeOnImpact explode = go.AddComponent<ExplodeOnImpact>();
            InvokeAwake(explode);
            SetSplashMaskToNothing(explode);
            return explode;
        }

        private static ProjectileContext MakeContext() =>
            new ProjectileContext(-1, 1f, 1f, 100f, 10f, shooterActorNumber: 1, shooterTeamId: 0,
                                   direction: Vector3.forward, targetPoint: Vector3.zero);

        /// <summary>The stand-in IDamageable for the one "direct hit" regression case below - never actually
        /// asked to apply anything, since Detonate skips the direct victim's own splash share.</summary>
        private class StubDamageable : IDamageable
        {
            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive => true;
            public int TeamId => 5; // Not the shooter's team (0) and not -1 - a plain, unrelated victim.
            public int ActorNumber => 7;
            public bool HasLocalAuthority => true;
        }

        [Test]
        public void AnAirburstThatCaughtNothingStillCarriesTheFullSplashRadius()
        {
            // OnExpired is what a rocket reaching its range end (or the cursor rocket reaching the cursor) calls -
            // no struck surface, no direct victim, and (splashMask 0) nothing the splash could ever catch either.
            // Before the 2026-09-21 fix this carried 0 (anyDamageApplied stayed false): the blast happened but drew
            // nothing, which read to a player as "the rocket only explodes on or near someone."
            ExplodeOnImpact explode = MakeRig();
            explode.OnSpawned(null, MakeContext());

            float? capturedRadius = null;
            explode.Detonated += (centre, radius) => capturedRadius = radius;

            explode.OnExpired(null, MakeContext());

            Assert.IsTrue(capturedRadius.HasValue, "OnExpired must raise Detonated");
            Assert.AreEqual(explode.SplashRadius, capturedRadius.Value, 0.0001f,
                "an airburst that caught nothing must still show the blast at the rocket's full Splash Radius");
        }

        [Test]
        public void AWallHitWithNoDirectVictimStillCarriesTheFullSplashRadius()
        {
            // OnHit's victim is null when the sweep stopped on level geometry (a wall) rather than an IDamageable -
            // the same "detonated, dealt no damage, drew nothing" bug case as the airburst above, through the
            // OTHER call site Detonate has (see ExplodeOnImpact's own class comment: OnHit and OnExpired are the
            // two paths into the one Detonate method).
            ExplodeOnImpact explode = MakeRig();
            explode.OnSpawned(null, MakeContext());

            float? capturedRadius = null;
            explode.Detonated += (centre, radius) => capturedRadius = radius;

            RaycastHit wallHit = new RaycastHit();
            wallHit.point = new Vector3(0f, 0f, 5f);
            wallHit.normal = Vector3.back;
            // wallHit.collider is left null (a synthetic RaycastHit's default) - confirmed safe to read (never
            // throws) rather than resolving to a real Collider; Detonate only branches on it being non-null to
            // decide whether to nudge its occlusion origin, which this test does not assert on.

            explode.OnHit(null, MakeContext(), wallHit, victim: null);

            Assert.IsTrue(capturedRadius.HasValue, "OnHit must raise Detonated even when it struck a wall");
            Assert.AreEqual(explode.SplashRadius, capturedRadius.Value, 0.0001f,
                "a wall hit that caught nothing in its splash must still show the blast at the rocket's full " +
                "Splash Radius");
        }

        [Test]
        public void ADirectHitStillCarriesTheFullSplashRadius()
        {
            // Regression guard: a direct hit already gave the full radius before this fix (anyDamageApplied was
            // seeded true by a non-null directVictim) - this must still hold after anyDamageApplied is gone.
            ExplodeOnImpact explode = MakeRig();
            explode.OnSpawned(null, MakeContext());

            float? capturedRadius = null;
            explode.Detonated += (centre, radius) => capturedRadius = radius;

            RaycastHit hit = new RaycastHit();
            hit.point = new Vector3(0f, 0f, 3f);
            hit.normal = Vector3.back;

            explode.OnHit(null, MakeContext(), hit, victim: new StubDamageable());

            Assert.IsTrue(capturedRadius.HasValue, "OnHit must raise Detonated on a direct hit");
            Assert.AreEqual(explode.SplashRadius, capturedRadius.Value, 0.0001f,
                "a direct hit must still show the blast at the rocket's full Splash Radius");
        }
    }
}
