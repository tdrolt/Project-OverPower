using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The flamethrower's cone (ability visuals step 5; Tudor: "just a cylinder instead of a cone that starts from the
    /// player"). A3 (Tudor 2026-09-17 evening) replaced the originally planned flat fan-plus-outline with a SOFT cone:
    /// a flat fan on the floor, tip under the caster's root, opening to the ability's own Cone Range and Cone Angle -
    /// exactly the shape ConeFilter.IsWithinCone tests (flat, measured from the caster's root) - warm at the tip,
    /// fading to fully transparent at the far edge and toward the side edges, with a subtle flicker, so a player can
    /// tell where the flames reach without the screen filling with a flat orange wash. The hard outline from the
    /// original plan is gone for everyone except the caster, who still sees a faint aiming ring at the true range and
    /// angle - FlamethrowerAbility.ShowVfx reads Owner.IsMine once per cast and passes it in; this class has no
    /// network state of its own.
    ///
    /// VERTEX-COLOUR TRAP (found while building this): the shared Ability Visual Glass.mat is URP/Unlit, and that
    /// shader's Attributes struct (Packages/com.unity.render-pipelines.universal/Shaders/UnlitForwardPass.hlsl) has no
    /// COLOR semantic at all - it never reads a mesh's vertex colours, so the tip-to-edge gradient below would render
    /// as a flat wash if drawn with it. This is the exact bug Laser Beam.mat hit on 2026-09-15 ("the previous plain
    /// URP/Unlit shader does not read a LineRenderer's vertex colours at all"), fixed there by switching to URP
    /// Particles/Unlit (ParticlesUnlitInput.hlsl's SampleAlbedo multiplies the material colour by the mesh's own
    /// vertex colour - verified by reading both shaders' source, not assumed). The fan below uses a DIFFERENT, NEW
    /// material - Flame Cone.mat, also Particles/Unlit - rather than editing Ability Visual Glass.mat, which the
    /// mine, portal and fence already ship with; changing its shader would have silently altered three finished steps.
    ///
    /// Visual only. The mesh is rebuilt only when Cone Range/Cone Angle change; Place runs every physics step while
    /// spraying and allocates nothing, and the flicker recolours the SAME cached arrays at a capped rate rather than
    /// building new ones every frame.
    /// </summary>
    public sealed class FlameConeVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("The filled fan: a MeshFilter whose mesh and per-vertex colour are generated here, " +
                 "drawn with Flame Cone.mat (Particles/Unlit - plain Unlit never shows a mesh's vertex colours).")]
        private MeshFilter fan;

        [SerializeField, Tooltip("The faint aiming outline at the true range and angle. Shown only on the CASTER'S " +
                 "OWN screen (A3) - everyone else sees only the soft fan, never a hard edge.")]
        private LineRenderer edge;

        [SerializeField, Range(0f, 1f), Tooltip("Alpha at the fan's tip, under the caster - the brightest the flame " +
                 "ever gets. A3's number: about 0.45, low enough that the caster and anyone standing in the flame " +
                 "stay visible.")]
        private float tipAlpha = 0.45f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the caster-only aiming outline.")]
        private float edgeOpacity = 0.9f;

        [SerializeField, Tooltip("Straight pieces the arc is made of. 24 keeps the drawn edge within 1 cm of the real " +
                 "arc at 7 m.")]
        private int arcSegments = 24;

        [SerializeField, Tooltip("Metres above the floor, so the fan doesn't flicker into the ground.")]
        private float heightAboveGround = 0.05f;

        [SerializeField, Range(0f, 1f), Tooltip("How far the flicker dips below Tip Alpha at its darkest point - 0 " +
                 "turns the flicker off, 1 lets it fade all the way to invisible. Kept small so it reads as a live " +
                 "flame, never a strobe.")]
        private float flickerDepth = 0.25f;

        [SerializeField, Tooltip("How many times a second the flicker recomputes the fan's vertex colours (A3: 'at " +
                 "most about 15'). The mesh shape never changes for this - only alpha - and the same arrays are " +
                 "reused every time, so a spray never allocates.")]
        private float flickerUpdatesPerSecond = 15f;

        private Mesh mesh;
        private Vector3[] vertices;
        private int[] triangles;
        private Color32[] colors;
        private float[] baseAlpha;
        private float builtRange = -1f;
        private float builtAngle = -1f;
        private float nextFlickerTime;
        private MaterialPropertyBlock block;

        /// <summary>Sizes the cone to the ability's real numbers and colours it. Cheap when nothing changed.
        /// <paramref name="isCasterView"/> (A3) shows the faint aiming outline only on the player who is actually
        /// spraying - everyone else's copy of this same prefab never draws it.</summary>
        public void Configure(float range, float fullAngleDegrees, Color color, bool isCasterView)
        {
            if (range != builtRange || fullAngleDegrees != builtAngle)
                Rebuild(range, fullAngleDegrees);

            if (block == null)
                block = new MaterialPropertyBlock();
            // Full alpha here on purpose: the tip-to-edge (and side) fade lives entirely in the mesh's own vertex
            // alpha below - the one channel Particles/Unlit actually multiplies per vertex - so _BaseColor only ever
            // carries the flat design tint, exactly like every other view's VisualTint.SetMeshColor call.
            VisualTint.SetMeshColor(fan != null ? fan.GetComponent<Renderer>() : null, block, VisualTint.WithAlpha(color, 1f));
            ApplyFlicker(); // immediate recolour for this cast, rather than waiting up to 1 / Flicker Updates Per Second

            if (edge != null)
            {
                edge.enabled = isCasterView;
                VisualTint.SetLineColor(edge, VisualTint.WithAlpha(color, edgeOpacity));
            }
        }

        /// <summary>Tip on the floor under <paramref name="apex"/>, opening along <paramref name="forward"/>'s flat direction.</summary>
        public void Place(Vector3 apex, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return;

            float floor = GroundSnap.TryFindGroundY(apex, out float groundY) ? groundY : apex.y;
            transform.SetPositionAndRotation(new Vector3(apex.x, floor + heightAboveGround, apex.z), Quaternion.LookRotation(forward));
        }

        private void Update()
        {
            // Gated to Flicker Updates Per Second, not every frame - several simultaneous sprays must stay cheap,
            // and Configure already applied one immediate recolour for this cast.
            if (mesh == null || Time.time < nextFlickerTime)
                return;

            ApplyFlicker();
        }

        /// <summary>A3's "subtle alpha flicker": recolours the SAME cached Color32[] (never a new array) and pushes it
        /// to the mesh - RGB pinned to white so it acts as a pure alpha mask on top of _BaseColor's tint (Configure's
        /// own comment). PerlinNoise, not Random.value, so it is deterministic and allocation-free, and its smooth
        /// curve reads as a living flame rather than a per-frame strobe.</summary>
        private void ApplyFlicker()
        {
            if (mesh == null || colors == null)
                return;

            float interval = flickerUpdatesPerSecond > 0f ? 1f / flickerUpdatesPerSecond : 0f;
            nextFlickerTime = Time.time + interval;

            float flicker = 1f - flickerDepth * Mathf.PerlinNoise(Time.time * 3f, 0.5f);
            for (int i = 0; i < colors.Length; i++)
            {
                byte a = (byte)Mathf.Clamp(baseAlpha[i] * flicker * 255f, 0f, 255f);
                colors[i] = new Color32(255, 255, 255, a);
            }
            mesh.colors32 = colors;
        }

        private void Rebuild(float range, float fullAngleDegrees)
        {
            int slices = Mathf.Max(1, arcSegments);
            int count = AbilityVisualGeometry.FanVertexCount(slices);
            if (vertices == null || vertices.Length != count)
            {
                vertices = new Vector3[count];
                triangles = new int[slices * 3];
                colors = new Color32[count];
                baseAlpha = new float[count];
            }

            for (int i = 0; i < count; i++)
            {
                vertices[i] = AbilityVisualGeometry.FanVertex(i, range, fullAngleDegrees, slices);
                // A3: warm at the tip, clear at the far edge AND toward the side edges - both are readings of the
                // SAME fan-vertex index AbilityVisualGeometry already exposes (FanVertexRangeFraction,
                // FanVertexSideFraction), not a second mesh shape or hand-rolled fade of this class's own.
                float rangeFade = 1f - AbilityVisualGeometry.FanVertexRangeFraction(i);
                float sideFade = 1f - AbilityVisualGeometry.FanVertexSideFraction(i, slices);
                baseAlpha[i] = tipAlpha * rangeFade * sideFade;
            }
            AbilityVisualGeometry.FillFanTriangles(slices, triangles);

            if (mesh == null)
            {
                mesh = new Mesh { name = "Flame Cone (generated)" };
                if (fan != null)
                    fan.sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (edge != null)
            {
                edge.useWorldSpace = false;
                edge.loop = true;
                edge.positionCount = count;
                for (int i = 0; i < count; i++)
                    edge.SetPosition(i, new Vector3(vertices[i].x, vertices[i].z, 0f)); // the flat child's local XY is the floor
            }

            builtRange = range;
            builtAngle = fullAngleDegrees;
            nextFlickerTime = 0f; // force an immediate recolour on the next ApplyFlicker after any rebuild
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
