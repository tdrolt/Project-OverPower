using Overpower.Arena;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The triggered Shield's look: the gold hex-plate shell (ShieldShellMesh) on the shared plates material in both slots, the
    /// gold and the seams' dark shade put on through one property block per slot, the plates rising from the armed ring and then standing
    /// still. It owns its mesh and frees it with itself; the Shield's own timed Destroy ends it. Visual only: no collider.</summary>
    [DisallowMultipleComponent]
    public sealed class ShieldShellView : MonoBehaviour
    {
        private const int PlatesSlot = 0, BackingSlot = 1;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        private Mesh mesh;
        private PlateRange[] plates;
        private Vector3[] rest;
        private Vector3[] shown;
        private float riseSeconds, stagger, ringRadius, ringY;
        private float elapsed;
        private bool rising;
        private MaterialPropertyBlock block;

        /// <summary>Builds the shell on <paramref name="host"/> (a mesh filter and renderer are added; the host must be fully built before a
        /// fog gate collects its renderers). The ring numbers are in the shell's own space: metres divided by the Shield's diameter.</summary>
        public static ShieldShellView Build(GameObject host, Material plates, float seamFraction, float riseSeconds, float stagger, float ringRadius, float ringY)
        {
            ShieldShellView view = host.AddComponent<ShieldShellView>();
            view.mesh = ShieldShellMesh.Create(seamFraction, out view.plates);
            view.mesh.MarkDynamic();
            view.rest = view.mesh.vertices;
            view.shown = new Vector3[view.rest.Length]; // the backing's vertices stay at zero (nothing drawn) until the plates are closed
            view.riseSeconds = Mathf.Max(0f, riseSeconds);
            view.stagger = stagger;
            view.ringRadius = ringRadius;
            view.ringY = ringY;
            view.rising = true;

            host.AddComponent<MeshFilter>().sharedMesh = view.mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { plates, plates };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.Advance(0f);
            return view;
        }

        public bool Rising => rising;

        /// <summary>Puts the colours on this shell only: the plates take the gold, and the gold times <paramref name="glow"/> as their own
        /// light; the backing takes <paramref name="seamShade"/>, matte and without glow, so the seams read dark.</summary>
        public void Tint(Color gold, float glow, Color seamShade)
        {
            var renderer = GetComponent<MeshRenderer>();
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, PlatesSlot);
            block.SetColor(BaseColorId, gold);
            block.SetColor(EmissionColorId, gold * glow);
            renderer.SetPropertyBlock(block, PlatesSlot);

            renderer.GetPropertyBlock(block, BackingSlot);
            block.SetColor(BaseColorId, seamShade);
            block.SetColor(EmissionColorId, Color.black);
            block.SetFloat(SmoothnessId, 0f);
            block.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(block, BackingSlot);
        }

        private void Update() => Advance(Time.deltaTime);

        public void Advance(float seconds)
        {
            if (!rising || mesh == null)
                return;
            elapsed += seconds;
            float t01 = riseSeconds > 0f ? elapsed / riseSeconds : 1f;
            foreach (PlateRange plate in plates)
            {
                Vector3 offset = ShieldShellRise.PlateOffset(plate.Centre, t01, ringRadius, ringY, stagger);
                for (int i = plate.FirstVertex; i < plate.FirstVertex + plate.VertexCount; i++)
                    shown[i] = rest[i] + offset;
            }
            if (t01 >= 1f)
            {
                rising = false;
                mesh.vertices = rest; // every plate is at rest now, and the backing appears behind the closed plates
            }
            else
                mesh.vertices = shown;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh == null)
                return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        /// <summary>With domain reload on, a shell alive when Play Mode stopped lost its owner: free it by name.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
#if UNITY_EDITOR
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if (leftover != null && leftover.name == ShieldShellMesh.Name && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
    }
}
