using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The flamethrower's cone: a SOFT flat fan on the floor, tip under the caster's root, opening to the ability's Cone
    /// Range and Cone Angle (exactly ConeFilter.IsWithinCone's shape), warm at the tip and fading to transparent toward the
    /// far and side edges with a subtle flicker, plus an aiming outline (Edge Opacity for the caster, Non Caster Edge
    /// Opacity for others). FlamethrowerAbility.ShowVfx passes Owner.IsMine in; no network state here.
    /// VERTEX-COLOUR TRAP: the shared Ability Visual Glass.mat is URP/Unlit, which never reads a mesh's vertex colours, so
    /// the tip-to-edge gradient would render as a flat wash. The fan uses Flame Cone.mat (Particles/Unlit, which multiplies
    /// the material colour by the vertex colour); don't edit Glass.mat, the mine, portal and fence ship with it.
    /// GROUND-HUE TRAP: a flat orange fill barely separates from this arena's tan/orange floor and alpha alone cannot fix
    /// it, so Core Colour pushes the TIP to pale yellow-white, fading to the ability's Colour and to clear by the far edge.
    /// RADIAL-FADE TRAP: with a single outer vertex ring the range fraction is a step (0 or 1), so alpha was a straight line
    /// from one bright point to zero and Radial Hold Fraction and the side fade did nothing; Radial Rings gives real vertices.
    /// Visual only. The mesh rebuilds only when Cone Range/Angle/Colour change; Place runs every physics step and allocates
    /// nothing; the flicker recolours the SAME cached arrays at a capped rate.
    /// </summary>
    public sealed class FlameConeVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("The filled fan: a MeshFilter whose mesh and per-vertex colour are generated here, " +
                 "drawn with Flame Cone.mat (Particles/Unlit - plain Unlit never shows a mesh's vertex colours).")]
        private MeshFilter fan;

        [SerializeField, Tooltip("The faint aiming outline at the true range and angle. Its opacity for the CASTER's " +
                 "own screen is Edge Opacity below; for everyone else it is Non-Caster Edge Opacity.")]
        private LineRenderer edge;

        [SerializeField, Range(0f, 1f), Tooltip("Alpha at the fan's tip, under the caster - the brightest the flame " +
                 "ever gets. A3's number: about 0.45, low enough that the caster and anyone standing in the flame " +
                 "stay visible. Core Colour and Radial Rings/Hold Fraction below do most of the work of reading " +
                 "against a warm floor now - this is a secondary knob, not the primary fix.")]
        private float tipAlpha = 0.45f;

        [SerializeField, Tooltip("Colour at the very tip, under the caster - pushed toward hot, pale yellow-white " +
                 "rather than more orange (2026-09-18: this arena's tan/orange floor, about RGB 172,102,60, sits too " +
                 "close to a plain-orange flame for alpha alone to separate from it - a real flame also reads hottest " +
                 "and palest at its base). Blends toward the ability's own Colour (vfxColor) across the fan, so the " +
                 "far half still reads as ordinary flame-orange fading to nothing, per A3. Alpha on this field is " +
                 "unused - Tip Alpha above is the only opacity control.")]
        private Color coreColor = new Color(1f, 0.95f, 0.78f, 1f);

        [SerializeField, Range(1, 12), Tooltip("Concentric rings beyond the tip the fan's radial fade is built from " +
                 "(review finding, 2026-09-18): with only 1 (the tip and a single outer arc, no vertex between them), " +
                 "Radial Hold Fraction had no vertex to hold ANY value at - the fade collapsed to one bright point " +
                 "under the caster's feet and zero everywhere else. More rings make the hold-then-fall-off below read " +
                 "as a real curve instead of a straight line to zero, and let the side fade finally have an effect.")]
        private int radialRings = 6;

        [SerializeField, Range(0f, 1f), Tooltip("Fraction of Cone Range, measured from the tip, that keeps the fan " +
                 "at full Tip Alpha before the fade even starts. The remaining stretch (this fraction to the full " +
                 "range) fades linearly to 0, so the flame holds a readable brightness through the near and middle " +
                 "of the cone and only tapers off near the true edge, rather than fading in a straight line from the " +
                 "tip (A3: 'fades toward the far edge', not from it).")]
        private float radialHoldFraction = 0.55f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the aiming outline on the CASTER's own screen.")]
        private float edgeOpacity = 0.9f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the SAME aiming outline on everyone ELSE's screen. A3 " +
                 "(Tudor, 2026-09-17 evening) originally asked for casters only (\"no hard outline for other " +
                 "players\") and this was 0. Ability visuals step 7 (Tudor: \"you can turn it on\") turned it back " +
                 "on at 0.35 - clearly under Edge Opacity's 0.9 above, so the caster's own line still reads as " +
                 "THEIR aid, but enough for an enemy to read the cone's true boundary instead of only the soft " +
                 "fill.")]
        private float nonCasterEdgeOpacity = 0.35f;

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
                 "most about 15'), clamped to at least 1 - the throttle exists specifically so several simultaneous " +
                 "sprays stay cheap, and letting this reach 0 would flip that into recolouring every single frame " +
                 "instead of turning the flicker off (review finding, 2026-09-18). Set Flicker Depth to 0 to turn the " +
                 "flicker off instead; the mesh shape never changes for this update - only alpha - and the same " +
                 "arrays are reused every time, so a spray never allocates.")]
        private float flickerUpdatesPerSecond = 15f;

        private Mesh mesh;
        private Vector3[] vertices;
        private int[] triangles;
        private Color32[] colors;
        private Color32[] baseRgb;
        private float[] baseAlpha;
        private float builtRange = -1f;
        private float builtAngle = -1f;
        private Color builtColor = Color.clear;
        private float nextFlickerTime;
        private MaterialPropertyBlock block;

        private void OnValidate()
        {
            // See Flicker Updates Per Second's own tooltip: 0 here would mean "recolour every frame", not "off".
            flickerUpdatesPerSecond = Mathf.Max(1f, flickerUpdatesPerSecond);
            radialRings = Mathf.Max(1, radialRings);
        }

        /// <summary>Sizes the cone to the ability's real numbers and colours it. Cheap when nothing changed.
        /// <paramref name="isCasterView"/> picks Edge Opacity vs. Non-Caster Edge Opacity for the aiming outline.</summary>
        public void Configure(float range, float fullAngleDegrees, Color color, bool isCasterView)
        {
            if (range != builtRange || fullAngleDegrees != builtAngle || color != builtColor)
                Rebuild(range, fullAngleDegrees, color);

            if (block == null)
                block = new MaterialPropertyBlock();
            // Opaque white on purpose: Core Colour-to-Colour and every fade live entirely in the mesh's vertex colours
            // (the channel Particles/Unlit multiplies per vertex); _BaseColor would re-tint the hue and wash out the pale tip.
            VisualTint.SetMeshColor(fan != null ? fan.GetComponent<Renderer>() : null, block, Color.white);
            ApplyFlicker(); // immediate recolour for this cast, rather than waiting up to 1 / Flicker Updates Per Second

            if (edge != null)
            {
                float outlineOpacity = isCasterView ? edgeOpacity : nonCasterEdgeOpacity;
                edge.enabled = outlineOpacity > 0f;
                VisualTint.SetLineColor(edge, VisualTint.WithAlpha(color, outlineOpacity));
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

        /// <summary>Recolours the SAME cached Color32[] (never a new array) and pushes it to the mesh. Only alpha flickers;
        /// RGB is the gradient baked in Rebuild, so the hue never strobes. PerlinNoise, not Random.value: deterministic,
        /// allocation-free, and its smooth curve reads as a living flame rather than a strobe.</summary>
        private void ApplyFlicker()
        {
            if (mesh == null || colors == null)
                return;

            // Clamped here too, not just in OnValidate (which never runs in a built Player): 0 must never reach this division.
            float interval = 1f / Mathf.Max(1f, flickerUpdatesPerSecond);
            nextFlickerTime = Time.time + interval;

            float flicker = 1f - flickerDepth * Mathf.PerlinNoise(Time.time * 3f, 0.5f);
            for (int i = 0; i < colors.Length; i++)
            {
                byte a = (byte)Mathf.Clamp(baseAlpha[i] * flicker * 255f, 0f, 255f);
                Color32 rgb = baseRgb[i];
                colors[i] = new Color32(rgb.r, rgb.g, rgb.b, a);
            }
            mesh.colors32 = colors;
        }

        private void Rebuild(float range, float fullAngleDegrees, Color color)
        {
            int slices = Mathf.Max(1, arcSegments);
            int rings = Mathf.Max(1, radialRings);
            int count = AbilityVisualGeometry.FanVertexCount(slices, rings);
            int triCount = slices * (2 * rings - 1);
            if (vertices == null || vertices.Length != count)
            {
                vertices = new Vector3[count];
                triangles = new int[triCount * 3];
                colors = new Color32[count];
                baseRgb = new Color32[count];
                baseAlpha = new float[count];
            }

            for (int i = 0; i < count; i++)
            {
                vertices[i] = AbilityVisualGeometry.FanVertex(i, range, fullAngleDegrees, slices, rings);

                // Full-strength through Radial Hold Fraction of the range, then fading to clear by the far edge, and also
                // fading toward the side edges - all readings of the SAME fan-vertex index AbilityVisualGeometry exposes.
                float radial = AbilityVisualGeometry.FanVertexRangeFraction(i, slices, rings); // 0 at the tip, 1 on the outer arc
                float holdT = Mathf.InverseLerp(radialHoldFraction, 1f, radial); // 0 while radial <= Radial Hold Fraction, ramps to 1 by the outer arc
                float rangeFade = 1f - Mathf.Clamp01(holdT);
                float sideFade = 1f - AbilityVisualGeometry.FanVertexSideFraction(i, slices);
                baseAlpha[i] = tipAlpha * rangeFade * sideFade;

                // Fades from the hot, pale Core Colour at the tip to the ability's Colour on the outer arc - radial only,
                // as a real flame's base runs hottest.
                Color rgb = Color.Lerp(coreColor, color, radial);
                baseRgb[i] = new Color32((byte)(rgb.r * 255f), (byte)(rgb.g * 255f), (byte)(rgb.b * 255f), 255);
            }
            AbilityVisualGeometry.FillFanTriangles(slices, triangles, rings);

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
                // The true boundary regardless of how many fill rings exist: the tip plus the OUTERMOST ring, picked back
                // out of the vertex array above.
                int edgeCount = slices + 2;
                int outerStart = 1 + (rings - 1) * (slices + 1);
                edge.useWorldSpace = false;
                edge.loop = true;
                edge.positionCount = edgeCount;
                edge.SetPosition(0, new Vector3(vertices[0].x, vertices[0].z, 0f));
                for (int s = 0; s <= slices; s++)
                {
                    Vector3 v = vertices[outerStart + s];
                    edge.SetPosition(1 + s, new Vector3(v.x, v.z, 0f)); // the flat child's local XY is the floor
                }
            }

            builtRange = range;
            builtAngle = fullAngleDegrees;
            builtColor = color;
            nextFlickerTime = 0f; // force an immediate recolour on the next ApplyFlicker after any rebuild
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
