using NUnit.Framework;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    public class VisibleWhenSeenTests
    {
        private GameObject root;
        private MeshRenderer on, off;
        private VisibleWhenSeen gate;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("shot");
            on = root.AddComponent<MeshRenderer>();
            GameObject child = new GameObject("child");
            child.transform.SetParent(root.transform);
            off = child.AddComponent<MeshRenderer>();
            off.enabled = false; // authored off
            gate = root.AddComponent<VisibleWhenSeen>();
            gate.RefreshRenderers(); // Awake does this in play; edit-mode tests do not run Awake
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void NoTeamSight_IsShown()
        {
            Assert.IsNull(TeamSight.Local);
            Assert.IsTrue(gate.Shown);
            Assert.IsTrue(on.enabled);
        }

        [Test]
        public void Hide_SwitchesOff_Restore_PutsAuthoredStateBack()
        {
            gate.SetVisible(false);
            Assert.IsFalse(gate.Shown);
            Assert.IsFalse(on.enabled);
            gate.SetVisible(true);
            Assert.IsTrue(on.enabled, "authored on comes back on");
            Assert.IsFalse(off.enabled, "authored off stays off");
        }

        [Test]
        public void RefreshWhileHidden_KeepsTheAuthoredFlags()
        {
            gate.SetVisible(false);
            gate.RefreshRenderers();
            gate.SetVisible(true);
            Assert.IsTrue(on.enabled, "a refresh while hidden must not record 'off' as authored");
            Assert.IsFalse(off.enabled);
        }

        [Test]
        public void ForceVisible_WinsOverHidden()
        {
            gate.SetVisible(false);
            gate.ForceVisible = true;
            gate.Apply();
            Assert.IsTrue(gate.Shown);
            Assert.IsTrue(on.enabled);
        }

        [Test]
        public void ComingBackIntoSight_ClearsTheTrail()
        {
            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            gate.RefreshRenderers();
            trail.AddPosition(Vector3.zero);
            trail.AddPosition(Vector3.one);
            Assert.Greater(trail.positionCount, 0);
            gate.SetVisible(false);
            gate.SetVisible(true);
            Assert.AreEqual(0, trail.positionCount);
        }
    }
}
