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
    /// original plan is gone for everyone except the caster by default, who still sees a faint aiming ring at the
    /// true range and angle - FlamethrowerAbility.ShowVfx reads Owner.IsMine once per cast and passes it in; this
    /// class has no network state of its own.
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
    /// GROUND-HUE TRAP (found 2026-09-18, after the first open-ground capture): a flat orange fill barely blends
    /// away from this arena's own tan/orange floor (measured ~RGB 172,102,60 vs. the flame's ~RGB 255,115,26 - the
    /// same family of warm hue), so raising alpha alone cannot make it read: alpha-blending two similar colours
    /// stays close to both of them regardless of the mix ratio. Same root cause as team 0's near-white being
    /// invisible against this arena's railings (steps 3-4). Fixed the same way real fire actually looks: Core Colour
    /// pushes the TIP toward a hot, pale yellow-white - a hue far enough from tan/orange that even a modest alpha
    /// blend visibly separates - fading toward the ability's own Colour (and to fully transparent) by the far edge,
    /// so A3's "doesn't fill the screen with orange" still holds.
    ///
    /// RADIAL-FADE DEFECT (review finding, 2026-09-18): the fan is built from AbilityVisualGeometry.FanVertex, whose
    /// pre-existing shape was one tip plus a SINGLE ring of arc points at the full range - so FanVertexRangeFraction
    /// was a step function (0 at the tip, 1 everywhere else), and the alpha this class computed from it,
    /// tipAlpha * rangeFade * sideFade, came out as tipAlpha at the single tip vertex and exactly 0 at every other
    /// vertex, whatever Side Fraction said. The mesh's own interpolation then drew a straight line from that one
    /// bright point to zero across the WHOLE 7 m, so almost the entire visible area sat near zero regardless of Tip
    /// Alpha - the real reason the fill read as invisible, more than the colour choice above did. Fixed by giving
    /// AbilityVisualGeometry.FanVertex an explicit Radial Rings parameter: real vertices partway along the range let
    /// Radial Hold Fraction below hold a readable alpha through the near/middle of the cone and only fall off over
    /// the last stretch, and let the side fade (which was always mathematically inert on a single ring - every
    /// non-tip vertex read the same alpha of 0 no matter its angle) finally do something visible too.
    ///
    /// Visual only. The mesh is rebuilt only when Cone Range/Cone Angle/Colour change; Place runs every physics step
    /// while spraying and allocates nothing, and the flicker recolours the SAME cached arrays at a capped rate rather
    /// than building new ones every frame.
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

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the SAME aiming outline on everyone ELSE's screen. " +
                 "Defaults to 0 because A3 (Tudor, 2026-09-17 evening) asked for casters only (\"no hard outline for " +
                 "other players\") - this is OFF on purpose, not an oversight. Raise it if the soft fill alone (Tip " +
                 "Alpha / Core Colour / Radial Hold Fraction) still doesn't read clearly enough for enemies on some " +
                 "arena floor; 0 keeps Tudor's original answer exactly as written.")]
        private float nonCasterEdgeOpacity = 0f;

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
        /// <paramref name="isCasterView"/> (A3) picks Edge Opacity vs. Non-Caster Edge Opacity for the aiming
        /// outline - everyone runs the exact same code, only the opacity they get differs.</summary>
        public void Configure(float range, float fullAngleDegrees, Color color, bool isCasterView)
        {
            if (range != builtRange || fullAngleDegrees != builtAngle || color != builtColor)
                Rebuild(range, fullAngleDegrees, color);

            if (block == null)
                block = new MaterialPropertyBlock();
            // Opaque white here on purpose: Core Colour-to-Colour and every fade now live entirely in the mesh's own
            // vertex colours below (the one channel Particles/Unlit actually multiplies per vertex) - _BaseColor
            // would otherwise re-tint the vertex-computed hue a second time and wash out Core Colour's pale tip.
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

        /// <summary>A3's "subtle alpha flicker": recolours the SAME cached Color32[] (never a new array) and pushes it
        /// to the mesh. Only alpha flickers - RGB is the fixed Core-Colour-to-Colour gradient baked in Rebuild, so the
        /// flame's hue itself never strobes, only its brightness. PerlinNoise, not Random.value, so it is
        /// deterministic and allocation-free, and its smooth curve reads as a living flame rather than a per-frame
        /// strobe.</summary>
        private void ApplyFlicker()
        {
            if (mesh == null || colors == null)
                return;

            // Clamped defensively here too, not just in OnValidate (which never runs on a built Player) - see Flicker
            // Updates Per Second's own tooltip for why 0 must never reach this division.
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

                // A3, fixed per the 2026-09-18 review: warm and full-strength through Radial Hold Fraction of the
                // range, then fading to clear by the far edge, and also fading toward the side edges - all three
                // readings of the SAME fan-vertex index AbilityVisualGeometry exposes, not hand-rolled fade maths.
                float radial = AbilityVisualGeometry.FanVertexRangeFraction(i, slices, rings); // 0 at the tip, 1 on the outer arc - now a real ramp, not a step
                float holdT = Mathf.InverseLerp(radialHoldFraction, 1f, radial); // 0 while radial <= Radial Hold Fraction, ramps to 1 by the outer arc
                float rangeFade = 1f - Mathf.Clamp01(holdT);
                float sideFade = 1f - AbilityVisualGeometry.FanVertexSideFraction(i, slices);
                baseAlpha[i] = tipAlpha * rangeFade * sideFade;

                // Ground-hue fix: the colour fades from the hot, pale Core Colour at the tip to the ability's own
                // Colour on the outer arc - radial only (not angular), matching how a real flame's base runs hottest.
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
                // The true boundary regardless of how many fill rings exist: the tip plus the OUTERMOST ring, in the
                // same tip-then-left-to-right order the fill's own outer ring already sits in - not a second
                // computation, just picking those vertices back out of the array above.
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
