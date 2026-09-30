using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Scope step 1 (Tudor, 2026-09-18). CameraTracking.AddZoomMultiplier/RemoveZoomMultiplier mirror
    /// PlayerMotor.AddSpeedMultiplier/RemoveSpeedMultiplier's own semantics exactly (PlayerMotor.cs:252-254) - a
    /// duplicate key overwrites rather than stacking, and removing an absent key is a no-op. A designer who
    /// already learned one keyed-multiplier system should be able to trust the other behaves identically without
    /// reading the code.
    /// </summary>
    public class CameraTrackingZoomStackTests
    {
        private GameObject cameraGo;
        private CameraTracking camera;

        [SetUp]
        public void CreateCamera()
        {
            cameraGo = new GameObject("TestCamera_ZoomStack");
            camera = cameraGo.AddComponent<CameraTracking>();
        }

        [TearDown]
        public void DestroyCamera()
        {
            Object.DestroyImmediate(cameraGo);
        }

        [Test]
        public void AFreshCameraHasNoActiveMultipliers()
        {
            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void AddingOneMultiplierIsReflectedInTheProduct()
        {
            camera.AddZoomMultiplier(new object(), 1.2f);

            Assert.AreEqual(1.2f, camera.ZoomMultiplierProduct, 1e-6f);
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void TwoDifferentKeysMultiplyTogether()
        {
            camera.AddZoomMultiplier(new object(), 1.2f);
            camera.AddZoomMultiplier(new object(), 1.5f);

            Assert.AreEqual(1.2f * 1.5f, camera.ZoomMultiplierProduct, 1e-6f);
            Assert.AreEqual(2, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void ADuplicateKeyOverwritesRatherThanStacking()
        {
            var key = new object();
            camera.AddZoomMultiplier(key, 1.2f);
            camera.AddZoomMultiplier(key, 1.5f); // same key again - must replace, never multiply on top

            Assert.AreEqual(1.5f, camera.ZoomMultiplierProduct, 1e-6f);
            Assert.AreEqual(1, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void RemovingAnAbsentKeyIsANoOp()
        {
            Assert.DoesNotThrow(() => camera.RemoveZoomMultiplier(new object()));

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void RemovingAKeyDropsItFromTheProduct()
        {
            var key = new object();
            camera.AddZoomMultiplier(key, 1.2f);
            camera.RemoveZoomMultiplier(key);

            Assert.AreEqual(1f, camera.ZoomMultiplierProduct);
            Assert.AreEqual(0, camera.ActiveZoomMultiplierCount);
        }

        [Test]
        public void AddingAZoomMultiplierNeverWritesIntoCurrentZoom()
        {
            // The controller's central worry (spec note 1): the scope multiplier must never be folded into
            // currentZoom itself, or the next scroll tick's clamp would re-clamp it away.
            FieldInfo currentZoomField =
                typeof(CameraTracking).GetField("currentZoom", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(currentZoomField, "CameraTracking.currentZoom");
            float before = (float)currentZoomField.GetValue(camera);

            camera.AddZoomMultiplier(new object(), 1.2f);

            float after = (float)currentZoomField.GetValue(camera);
            Assert.AreEqual(before, after);
        }
    }
}
