using NUnit.Framework;
using Overpower.Arena;
using Overpower.Weapons;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Impact and muzzle flashes are reused instead of made and destroyed per shot: a finished copy is reused first, a new one is made
    /// only under the cap, and at the cap the oldest one still playing is restarted where the new hit is.</summary>
    public class FxPoolRuleTests
    {
        [Test]
        public void ReusesBeforeInstantiating()
        {
            Assert.AreEqual(FxPoolAction.ReuseIdle, FxPoolRule.Take(1, 0, 24));
            Assert.AreEqual(FxPoolAction.ReuseIdle, FxPoolRule.Take(3, 5, 24), "a finished copy beats a new one");
            Assert.AreEqual(FxPoolAction.Instantiate, FxPoolRule.Take(0, 0, 24), "the first flash of its kind");
            Assert.AreEqual(FxPoolAction.Instantiate, FxPoolRule.Take(0, 23, 24), "still under the cap");
        }

        [Test]
        public void RecyclesOldestAtCap()
        {
            Assert.AreEqual(FxPoolAction.RecycleOldest, FxPoolRule.Take(0, 24, 24));
            Assert.AreEqual(FxPoolAction.RecycleOldest, FxPoolRule.Take(0, 30, 24), "never above the cap, even if it was lowered");
            Assert.AreEqual(FxPoolAction.Instantiate, FxPoolRule.Take(0, 0, 0), "a cap of 0 still shows the flash (one copy)");
            Assert.AreEqual(FxPoolAction.RecycleOldest, FxPoolRule.Take(0, 1, 0));
        }

        [Test]
        public void AFlashIsDrawnTowardTheCameraOnTheSameScreenSpot()
        {
            var hit = new Vector3(3f, 2f, 10f);
            var eye = new Vector3(0f, 20f, 0f);
            Vector3 drawn = FxPoolRule.Placed(hit, eye, 1f);
            Assert.AreEqual(1f, Vector3.Distance(hit, drawn), 1e-4f, "pulled Fx Camera Pull metres out of the wall it hit");
            Assert.Less(Vector3.Cross((drawn - eye).normalized, (hit - eye).normalized).magnitude, 1e-5f, "on the line to the camera, so it stays on the same spot on screen");
            Assert.AreEqual(hit, FxPoolRule.Placed(hit, eye, 0f), "no pull: drawn where it happened");
            Vector3 near = FxPoolRule.Placed(hit, hit + Vector3.up, 5f);
            Assert.LessOrEqual(Vector3.Distance(hit, near), 0.5f + 1e-4f, "never past halfway to a camera that is closer than the pull");
            Assert.AreEqual(hit, FxPoolRule.Placed(hit, hit, 1f), "a camera on the spot: no direction to pull in");
            Assert.IsTrue(IlWiring.Uses(typeof(VFXManager), "PlayVFX", typeof(FxPoolRule).GetMethod(nameof(FxPoolRule.Placed))),
                "every flash is placed by the tested rule");
        }

        [Test]
        public void ThePoolTakesItsCapAndPullFromTheArenaLook()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(VFXManager), "PlayVFX", typeof(ArenaLook).GetField(nameof(ArenaLook.fxPoolCap))),
                "the cap handed to the rule is the Arena Look's Fx Pool Cap");
            Assert.IsTrue(IlWiring.Uses(typeof(VFXManager), "PlayVFX", typeof(ArenaLook).GetField(nameof(ArenaLook.fxCameraPull))),
                "the pull handed to the rule is the Arena Look's Fx Camera Pull");
        }

        [Test]
        public void VfxManagerPoolsThroughTheRuleAndNeverDestroysPerFlash()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(VFXManager), "PlayVFX", typeof(FxPoolRule).GetMethod(nameof(FxPoolRule.Take))),
                "every flash asks the tested rule whether to reuse, make or recycle");
            var destroy = typeof(Object).GetMethod(nameof(Object.Destroy), new[] { typeof(Object), typeof(float) });
            var destroyNow = typeof(Object).GetMethod(nameof(Object.Destroy), new[] { typeof(Object) });
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(VFXManager), "Awake", destroyNow), "the check sees a Destroy where there is one");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(VFXManager), "PlayVFX", destroy), "no timed Destroy per flash");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(VFXManager), "PlayVFX", destroyNow));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(VFXManager), "Update", destroy));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(VFXManager), "Update", destroyNow), "a finished flash is switched off and kept");
        }
    }
}
