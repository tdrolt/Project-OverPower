using NUnit.Framework;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 7: gun sounds are heard from the player's body, not from the camera that hangs about 11 m above and
    /// behind. ListenerRig moves the scene camera's AudioListener onto a child that follows the followed player.
    /// </summary>
    public class ListenerRigTests
    {
        private GameObject camera;
        private GameObject target;

        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Camera");
            camera.AddComponent<AudioListener>();
            target = new GameObject("Target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(camera);
            Object.DestroyImmediate(target);
        }

        [Test]
        public void SplittingTheListener_LeavesExactlyOneEnabledListener_OnAChild()
        {
            Transform listener = ListenerRig.MoveListenerToChild(camera);

            Assert.IsNotNull(listener);
            Assert.AreEqual(camera.transform, listener.parent);
            int enabled = 0;
            foreach (AudioListener l in camera.GetComponentsInChildren<AudioListener>(true))
                if (l.enabled)
                    enabled++;
            Assert.AreEqual(1, enabled);
            Assert.IsFalse(camera.GetComponent<AudioListener>().enabled);
            Assert.IsTrue(listener.GetComponent<AudioListener>().enabled);
        }

        [Test]
        public void SplittingTheListener_WithNoListenerOnTheCamera_DoesNothing()
        {
            Object.DestroyImmediate(camera.GetComponent<AudioListener>());
            Assert.IsNull(ListenerRig.MoveListenerToChild(camera));
        }

        [Test]
        public void TheListenerFollowsTheTarget_NotTheCamera()
        {
            camera.transform.position = new Vector3(0f, 11f, -5f);
            Transform listener = ListenerRig.MoveListenerToChild(camera);
            target.transform.position = new Vector3(20f, 0.5f, 27f);

            ListenerRig.Follow(listener, target.transform);

            Assert.AreEqual(target.transform.position, listener.position);
        }

        [Test]
        public void WithNoTarget_TheListenerSitsOnTheCamera()
        {
            camera.transform.position = new Vector3(3f, 11f, -5f);
            Transform listener = ListenerRig.MoveListenerToChild(camera);
            listener.position = new Vector3(50f, 0f, 50f);

            ListenerRig.Follow(listener, null);

            Assert.AreEqual(camera.transform.position, listener.position);
        }
    }
}
