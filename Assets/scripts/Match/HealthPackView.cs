using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Match
{
    /// <summary>
    /// What one health pack looks like: a dark hexagon pad on the floor, its corners exactly at the pickup radius so standing anywhere
    /// on it takes the pack, with a glowing green edge, and a green cross floating above it that turns and bobs. Ready glows; waiting
    /// to come back is the same green dimmed (HealthPackLook). Nothing here is solid. HealthPackManager decides ready or not.
    /// </summary>
    public sealed class HealthPackView : MonoBehaviour
    {
        public const string PadMeshName = "Health Pack Pad";
        public const string EdgeMeshName = "Health Pack Edge";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        // Just enough above the pad's top that the edge never flickers into it.
        private const float EdgeLift = 0.005f;

        private HealthPackConfig config;
        private Color green;
        private Transform cross;
        private Transform lean;
        private Renderer[] crossRenderers;
        private MeshRenderer edgeRenderer;
        private Mesh padMesh;
        private Mesh edgeMesh;
        private MaterialPropertyBlock block;
        private bool painted;

        public bool ShownReady { get; private set; }
        public Renderer[] CrossRenderers => crossRenderers;
        public Renderer EdgeRenderer => edgeRenderer;
        public Transform Cross => cross;

        /// <summary>Builds a pack standing at <paramref name="centre"/> (its ground point). <paramref name="solid"/> is the plain unlit
        /// material the pad and the cross share; the green edge uses the Arena Look's FX Alpha and is left out (with a warning) without it.</summary>
        public static HealthPackView Create(Transform parent, Vector3 centre, string name, HealthPackConfig config, UiTheme theme, Material solid)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = centre;
            HealthPackView view = root.AddComponent<HealthPackView>();
            view.config = config;
            // Without a theme the pack is still drawn (white): hiding it would hide a pickup from the players.
            view.green = theme != null ? LookColours.For(LookFamily.Heal, -1, theme) : Color.white;
            view.block = new MaterialPropertyBlock();

            float radius = config.PickupRadius, thickness = config.PadThicknessMetres;
            view.padMesh = PrismMesh.Create(6, PadMeshName);
            MeshRenderer pad = AddMesh(root.transform, "Pad", view.padMesh, solid);
            pad.transform.localPosition = new Vector3(0f, thickness / 2f, 0f);
            pad.transform.localScale = new Vector3(radius, thickness / 2f, radius);
            VisualTint.SetMeshColor(pad, view.block, config.PadColour);

            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (look != null && look.fxAlpha != null)
            {
                view.edgeMesh = AoeRingMesh.Create(new[] { Vector2.zero }, radius, config.PadEdgeWidthMetres);
                view.edgeMesh.name = EdgeMeshName;
                Color linear = VisualTint.VertexColour(view.green);
                var colours = new Color[view.edgeMesh.vertexCount];
                for (int i = 0; i < colours.Length; i++)
                    colours[i] = VisualTint.WithAlpha(linear, i < AoeRingMesh.FillStart ? 1f : config.PadFillAlpha);
                view.edgeMesh.colors = colours;
                view.edgeRenderer = AddMesh(root.transform, "Edge", view.edgeMesh, look.fxAlpha);
                view.edgeRenderer.transform.localPosition = new Vector3(0f, thickness + EdgeLift, 0f);
            }
            else
                Debug.LogWarning($"[HealthPackView] {name}: no UiTheme Arena Look with FX Alpha - the pack's pad has no green edge.");

            // The cross: two boxes in an upright plus, floating above the pad, leaned back so the near top-down camera sees the plus, and
            // turning about the vertical axis so it reads from every side.
            var crossObject = new GameObject("Cross");
            crossObject.transform.SetParent(root.transform, false);
            view.cross = crossObject.transform;
            var leanObject = new GameObject("Lean");
            leanObject.transform.SetParent(crossObject.transform, false);
            view.lean = leanObject.transform;
            float length = config.CrossSizeMetres, arm = length / 3f;
            view.crossRenderers = new Renderer[]
            {
                MakeBox(leanObject.transform, "Horizontal bar", new Vector3(length, arm, arm), solid),
                MakeBox(leanObject.transform, "Vertical bar", new Vector3(arm, length, arm), solid),
            };
            view.Animate(0f);
            view.Paint(true);
            return view;
        }

        public void SetReady(bool ready)
        {
            if (painted && ShownReady == ready)
                return;
            Paint(ready);
        }

        private void Paint(bool ready)
        {
            painted = true;
            ShownReady = ready;
            Color colour = HealthPackLook.CrossColour(ready, green, config.ReadyGlow, config.RespawningBrightness);
            foreach (Renderer r in crossRenderers)
                VisualTint.SetMeshColor(r, block, colour);
            float glow = HealthPackLook.EdgeGlow(ready, config.ReadyGlow, config.RespawningBrightness);
            if (edgeRenderer != null)
                VisualTint.SetMeshColor(edgeRenderer, block, new Color(glow, glow, glow, 1f)); // FX Alpha multiplies the green vertex colours by it
        }

        public void Animate(float time)
        {
            float bob = Mathf.Sin(time * config.BobCyclesPerSecond * Mathf.PI * 2f) * config.BobHeightMetres;
            cross.localPosition = new Vector3(0f, config.FloatHeightMetres + bob, 0f);
            cross.localRotation = Quaternion.Euler(0f, time * config.SpinDegreesPerSecond, 0f);
            lean.localRotation = Quaternion.Euler(config.CrossLeanDegrees, 0f, 0f);
        }

        private static MeshRenderer AddMesh(Transform parent, string partName, Mesh mesh, Material material)
        {
            var part = new GameObject(partName);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private static Renderer MakeBox(Transform parent, string boxName, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = boxName;
            DestroyImmediate(box.GetComponent<Collider>()); // never a solid, physics-blocking object
            box.transform.SetParent(parent, false);
            box.transform.localScale = size;
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        private void OnDestroy()
        {
            Free(ref padMesh);
            Free(ref edgeMesh);
        }

        private static void Free(ref Mesh mesh)
        {
            if (mesh == null)
                return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        /// <summary>With domain reload on, a pack's meshes alive when Play Mode stopped lost their owner: free them by name.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
#if UNITY_EDITOR
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if (leftover != null && (leftover.name == PadMeshName || leftover.name == EdgeMeshName)
                    && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
    }
}
