using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Scope step 2 (Tudor, 2026-09-18). ScopeAbility does its own easing and calls
    /// CameraTracking.AddZoomMultiplier/RemoveZoomMultiplier itself (CameraTracking's stack has no easing of its
    /// own - CameraZoomStack, scope step 1). Every interrupt path must snap back AT ONCE rather than ease out, or
    /// a stale multiplier could survive a death/stun/silence/unequip - see PlayerMotor's own class comment for the
    /// speed-multiplier version of exactly this bug (2.10's respawn speed bug).
    ///
    /// Drives OwnerTick directly rather than through AbilityRunner/PlayerInputRouter - the same "held" the runner
    /// would compute (Equipment key down AND canAct), just supplied straight to the module the way the Play Mode
    /// measurement script also will (see progress.md's Scope step 2 entry for which path it used there).
    /// </summary>
    public class ScopeAbilityTests
    {
        private GameObject cameraGo;
        private CameraTracking camera;
        private GameObject scopeGo;
        private ScopeAbility scope;

        [SetUp]
        public void CreateRig()
        {
            cameraGo = new GameObject("TestCamera_Scope");
            camera = cameraGo.AddComponent<CameraTracking>();
            // Edit-mode components never run Awake (confirmed by eval against this exact Unity version) - only
            // Play Mode's player loop calls it, and CameraTracking is not [ExecuteAlways]. Set the static Instance
            // the same Awake would, so ScopeAbility - which only ever reads CameraTracking.Instance, the same as
            // it would find the real local player's camera in Play Mode - has something to talk to here.
            // OnDestroy DOES run on DestroyImmediate (also confirmed), so TearDown's destroy alone clears it again.
            SetStaticInstance(camera);

            scopeGo = new GameObject("TestScope");
            scope = scopeGo.AddComponent<ScopeAbility>();
        }

        private static void SetStaticInstance(CameraTracking value)
        {
            PropertyInfo property = typeof(CameraTracking).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(property, "CameraTracking.Instance");
            property.SetValue(null, value);
        }

        [TearDown]
        public void DestroyRig()
        {
            Object.DestroyImmediate(scopeGo);
            Object.DestroyImmediate(cameraGo);
        }

        [Test]
        public void TryBuildCastAlwaysRefusesSoNothingIsEverSpentOrSent()
        {
            bool accepted = scope.TryBuildCast(default, out CastPayload payload);

            Assert.IsFalse(accepted);
        }

        [Test]
        public void HoldingScopeAddsTheMultiplier()
        {
            scope.OwnerTick(1f, held: true, canAct: true); // a whole second - past any sane ease time

            Assert.Greater(camera.ZoomMultiplierProduct, 1f);
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void NotHeldNeverAddsAnything()
        {
            scope.OwnerTick(1f, held: false, canAct: true);

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void ReleasingErasesBackToOneAndRemovesTheKeyExactlyAtOne()
        {
            scope.OwnerTick(1f, held: true, canAct: true);
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount, "held for a full second must have added it");

            scope.OwnerTick(1f, held: false, canAct: true); // release for a whole second too

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void NotBeingAbleToActErasesTheMultiplierEvenIfStillHeld()
        {
            scope.OwnerTick(1f, held: true, canAct: true); // fully scoped
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount);

            scope.OwnerTick(1f, held: true, canAct: false); // silenced mid-hold: held stays true, canAct does not

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void InterruptSnapsBackAtOnceEvenMidEase()
        {
            scope.OwnerTick(0.01f, held: true, canAct: true); // one small step - still easing, not yet at target
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount);

            scope.Interrupt(InterruptReason.Silenced);

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void OnRespawnedSnapsBackAtOnce()
        {
            scope.OwnerTick(0.01f, held: true, canAct: true);
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount);

            scope.OnRespawned();

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void MissingCameraNeverThrows()
        {
            // The module can exist before the camera does, and in edit-mode tests there may be no camera at all -
            // ApplyOrRemove must null-check CameraTracking.Instance every time.
            Object.DestroyImmediate(cameraGo);
            cameraGo = null;

            Assert.DoesNotThrow(() => scope.OwnerTick(1f, held: true, canAct: true));
            Assert.DoesNotThrow(() => scope.Interrupt(InterruptReason.Died));
        }
    }
}
