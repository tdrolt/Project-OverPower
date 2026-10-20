using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Shield's shell view: it owns its runtime mesh and frees it with itself (a scene switch or a domain reload must not
    /// leak one "Shield Shell" per cast), runs the rise and then stops, shows the dark backing only once the plates are closed, and wears
    /// the gold and the seam shade through property blocks so the shared plates material is never written.</summary>
    public class ShieldShellViewTests
    {
        private static int LiveShells() => Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && m.name == ShieldShellMesh.Name);

        private static void DestroyLikeTheGame(GameObject go)
        {
            // Edit mode never sends OnDestroy to a plain MonoBehaviour, so the test sends it the way the game's Destroy does.
            foreach (ShieldShellView view in go.GetComponents<ShieldShellView>())
                typeof(ShieldShellView).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            Object.DestroyImmediate(go);
        }

        private static Material NewPlates() => new Material(Shader.Find("Universal Render Pipeline/Lit"));

        [Test]
        public void DestroyFreesTheMesh()
        {
            int before = LiveShells();
            var go = new GameObject("shell test");
            Material plates = NewPlates();
            try
            {
                ShieldShellView.Build(go, plates, 0.1f, 0.15f, 0.3f, 0.55f, 0.07f);
                Assert.AreEqual(before + 1, LiveShells(), "the view builds one mesh of its own");
            }
            finally
            {
                DestroyLikeTheGame(go);
                Object.DestroyImmediate(plates);
            }
            Assert.AreEqual(before, LiveShells(), "the mesh leaked past its view");
        }

        [Test]
        public void TheRiseEndsWithEveryPlateAtRestAndThenStops()
        {
            var go = new GameObject("shell test");
            Material plates = NewPlates();
            try
            {
                ShieldShellView view = ShieldShellView.Build(go, plates, 0.1f, 0.15f, 0.3f, 0.55f, 0.07f);
                Mesh rest = ShieldShellMesh.Create(0.1f, out _);
                try
                {
                    Mesh shown = go.GetComponent<MeshFilter>().sharedMesh;
                    Assert.Greater(MaxGap(shown, rest), 0.05f, "the first frame shows the plates down on the ring");
                    Assert.IsTrue(view.Rising);
                    view.Advance(0.1f);
                    view.Advance(0.1f);
                    Assert.Less(MaxGap(shown, rest), 1e-5f, "every plate is in place once the rise time is over");
                    Assert.IsFalse(view.Rising, "the view stops moving the mesh");
                }
                finally { Object.DestroyImmediate(rest); }
            }
            finally
            {
                DestroyLikeTheGame(go);
                Object.DestroyImmediate(plates);
            }
        }

        [Test]
        public void TintPutsGoldOnThePlatesAndTheShadeOnTheBackingWithoutTouchingTheMaterial()
        {
            var go = new GameObject("shell test");
            Material plates = NewPlates();
            Color before = plates.GetColor("_BaseColor");
            Color gold = new Color(0.9f, 0.7f, 0.1f, 1f);
            Color shade = new Color(0.05f, 0.04f, 0.03f, 1f);
            try
            {
                ShieldShellView view = ShieldShellView.Build(go, plates, 0.1f, 0.15f, 0.3f, 0.55f, 0.07f);
                view.Tint(gold, 0.5f, shade);
                var block = new MaterialPropertyBlock();
                Renderer renderer = go.GetComponent<MeshRenderer>();
                renderer.GetPropertyBlock(block, 0);
                AssertClose(gold, block.GetColor("_BaseColor"));
                AssertClose(gold * 0.5f, block.GetColor("_EmissionColor"));
                renderer.GetPropertyBlock(block, 1);
                AssertClose(shade, block.GetColor("_BaseColor"));
                AssertClose(Color.black, block.GetColor("_EmissionColor"));
                Assert.AreEqual(2, renderer.sharedMaterials.Length, "one slot for the plates, one for the backing");
                Assert.IsTrue(renderer.sharedMaterials.All(m => m == plates), "the shared plates material is used, never copied");
                Assert.AreEqual(before, plates.GetColor("_BaseColor"), "the shared material is never written");
                Assert.IsNull(go.GetComponent<Collider>(), "the shell is never solid");
            }
            finally
            {
                DestroyLikeTheGame(go);
                Object.DestroyImmediate(plates);
            }
        }

        [Test]
        public void TheBackingShowsOnlyOnceThePlatesAreClosed()
        {
            var go = new GameObject("shell test");
            Material plates = NewPlates();
            try
            {
                ShieldShellView view = ShieldShellView.Build(go, plates, 0.1f, 0.15f, 0.3f, 0.55f, 0.07f);
                Mesh shown = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(2, shown.subMeshCount);
                view.Advance(0.1f);
                Assert.IsTrue(view.Rising);
                Assert.AreEqual(0f, BackingArea(shown), 1e-6f, "a dark ball inside the rising plates would hide the snap");
                view.Advance(0.1f);
                Assert.IsFalse(view.Rising);
                Assert.Greater(BackingArea(shown), 0.5f, "the closed shell's seams read dark");
            }
            finally
            {
                DestroyLikeTheGame(go);
                Object.DestroyImmediate(plates);
            }
        }

        [Test]
        public void TheReloadSweepFreesOnlyRuntimeShells()
        {
            MethodInfo sweep = typeof(ShieldShellView).GetMethod("FreeLeftovers", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(sweep);
            Mesh leftover = ShieldShellMesh.Create(0.1f, out _); // its view is gone, as after a domain reload
            var saved = new Mesh { name = ShieldShellMesh.Name }; // not DontSave: someone else's, never swept
            try
            {
                sweep.Invoke(null, null);
                Assert.IsTrue(leftover == null, "the leftover runtime shell is freed by name");
                Assert.IsTrue(saved != null, "a mesh that is not a runtime shell stays");
            }
            finally { Object.DestroyImmediate(saved); }
        }

        [Test]
        public void TheFogGateHidesTheWholeShell()
        {
            var go = new GameObject("shell test");
            Material plates = NewPlates();
            try
            {
                ShieldShellView.Build(go, plates, 0.1f, 0.15f, 0.3f, 0.55f, 0.07f);
                VisibleWhenSeen gate = go.AddComponent<VisibleWhenSeen>();
                gate.RefreshRenderers(); // Awake does this in play; edit-mode tests do not run Awake
                gate.SetVisible(false);
                Renderer[] all = go.GetComponentsInChildren<Renderer>(true);
                Assert.IsNotEmpty(all);
                Assert.IsTrue(all.All(r => !r.enabled), "a Shield on a player in the fog draws nothing");
            }
            finally
            {
                DestroyLikeTheGame(go);
                Object.DestroyImmediate(plates);
            }
        }

        private static float BackingArea(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.GetTriangles(1);
            float area = 0f;
            for (int i = 0; i < t.Length; i += 3)
                area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            return area;
        }

        // The block stores linear values, so a colour read back can differ from the one set in the last bits.
        private static void AssertClose(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f);
            Assert.AreEqual(expected.g, actual.g, 1e-4f);
            Assert.AreEqual(expected.b, actual.b, 1e-4f);
        }

        private static float MaxGap(Mesh a, Mesh b)
        {
            Vector3[] va = a.vertices, vb = b.vertices;
            float gap = 0f;
            for (int i = 0; i < va.Length; i++)
                gap = Mathf.Max(gap, (va[i] - vb[i]).magnitude);
            return gap;
        }
    }
}
