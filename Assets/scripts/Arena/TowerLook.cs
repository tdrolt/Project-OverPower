using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Arena
{
    /// <summary>
    /// Tudor, 2026-09-18: "round towers that each have as many columns as their tiers and each should have a piece
    /// or multiple that would change color depending on who owns it." The prefab has four column slots (a shaft and
    /// a cap each); ApplyColumns shows the first N and hides the rest, at edit time, from ArenaPrimitiveBuilder -
    /// nothing here builds anything at runtime.
    ///
    /// Tudor, 2026-09-19 (the plan's Q2): the capital's one column is BIG; every other tier's columns share one
    /// normal size. columnRadius is the normal column's shaft radius; capitalColumnRadius is the capital's. The cap
    /// keeps the plan's own shaft:cap proportion (0.45 / 0.35, Decision 6) at whichever shaft radius is showing, so
    /// there is one number to retune a column's whole silhouette, not two that could drift apart (CODING-STANDARDS
    /// #3). TowerLookRules.ColumnRingRadius keeps a column's outer edge tangent to this prefab's own collider radius
    /// - never past it - so columns never add cover over the plain round tower (Decision 6), at either size.
    ///
    /// The crown and every SHOWN cap share one Unlit material ("Tower Owner"), tinted through one reused
    /// MaterialPropertyBlock (VisualTint.SetMeshColor) - no material copy per tower. Refresh gets exactly the same
    /// CaptureRingState the ring on the ground already gets every frame (via OwnerPaint.From /
    /// OwnerPaintColours.For), so the tower and the ring can never disagree and a late joiner is right at once. The
    /// pieces are grey (the material's own colour) until the first Refresh; no collider here depends on the tier.
    ///
    /// Tudor, 2026-09-21: "currently its only the top" - the Plinth, Drum and every SHOWN shaft (the Lit "Tower
    /// Stone" material) are painted too, through the very same Refresh call and the very same one colour, times
    /// UiTheme's Tower Body Shade so the stone keeps its own lighting instead of going flat like the crown/caps.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TowerLook : MonoBehaviour
    {
        // The plan's own Decision 6 shaft:cap proportion (0.35 m shaft / 0.45 m cap), applied to whichever shaft
        // radius is showing so the cap never needs its own separate number.
        private const float ShaftRadiusAtDesign = 0.35f;
        private const float CapRadiusAtDesign = 0.45f;
        private const float CapToShaftRatio = CapRadiusAtDesign / ShaftRadiusAtDesign;

        [Tooltip("The four column slots, evenly spaced round the tower; ApplyColumns shows the first N and hides " +
                 "the rest. Each slot's own Shaft and Cap child are scaled to the showing radius and placed on the " +
                 "matching ring - see columnRadius/capitalColumnRadius below.")]
        public Transform[] columnSlots = new Transform[4];

        [Tooltip("Each slot's Cap renderer, in the same order as columnSlots - the piece Refresh paints along with " +
                 "the crown.")]
        public Renderer[] columnCaps = new Renderer[4];

        [Tooltip("The crown on top of the tower - painted with the owner's colour, like the shown caps.")]
        public Renderer crown;

        // Trap, found by capturing and looking (2026-09-21): these pieces (and Plinth/Drum below) used to be
        // marked Batching Static, same as any stone that "never repaints" - which was true before this change.
        // A statically-batched renderer draws through its BATCH ROOT's combined mesh, so SetPropertyBlock on the
        // ORIGINAL renderer (what VisualTint.SetMeshColor calls) never reaches the screen, even though
        // GetPropertyBlock/SetPropertyBlock both succeed and a test reading the renderer's own block back looks
        // green. The prefab's flag is off now for these pieces (TowerLookPrefabTests pins it) - never turn it
        // back on for anything Refresh paints.
        [Tooltip("Each slot's Shaft renderer, in the same order as columnSlots - the Lit 'Tower Stone' material, " +
                 "so Refresh paints it (like Plinth/Drum below) with the owner's colour times UiTheme's Tower " +
                 "Body Shade rather than the flat colour the Crown/caps get. Only a SHOWN slot's shaft is painted, " +
                 "same rule as columnCaps. Must not be Batching Static - see the comment above.")]
        public Renderer[] columnShafts = new Renderer[4];

        [Tooltip("The tower's base (Lit 'Tower Stone' material) - painted with the owner's colour times UiTheme's " +
                 "Tower Body Shade, same as Drum and the shown shafts. Must not be Batching Static.")]
        public Renderer plinth;

        [Tooltip("The tower's body (Lit 'Tower Stone' material) - painted with the owner's colour times UiTheme's " +
                 "Tower Body Shade, same as Plinth and the shown shafts. Must not be Batching Static.")]
        public Renderer drum;

        // The plan's starting value (Decision 6, 2026-09-18): 0.35 m shaft / 0.45 m cap. The cap above is drawn
        // wider automatically, in that same proportion, whichever shaft radius is showing.
        [Tooltip("Shaft radius, in metres, for every tier except the capital. The cap is drawn wider automatically, " +
                 "in a fixed proportion to this radius.")]
        public float columnRadius = 0.35f;

        // Tudor, 2026-09-19: "the capital gets 1 big column" - about 1.7x columnRadius, so it reads as the grandest.
        [Tooltip("Shaft radius, in metres, for the capital's single column - bigger than columnRadius, so it reads " +
                 "as the grandest. The cap keeps the same shaft:cap proportion as every other tier's columns.")]
        public float capitalColumnRadius = 0.6f;

        private MaterialPropertyBlock block;
        private UiTheme theme;
        private bool bound;
        private Color shownColor;
        private bool shownColorSet;

        /// <summary>Diagnostic: the colour Refresh last painted (or would paint before Bind).</summary>
        public Color ShownColor => shownColor;

        /// <summary>How many of the four slots are currently shown.</summary>
        public int ShownColumns
        {
            get
            {
                int count = 0;
                for (int i = 0; i < columnSlots.Length; i++)
                    if (columnSlots[i] != null && columnSlots[i].gameObject.activeSelf)
                        count++;
                return count;
            }
        }

        /// <summary>Shows this tier's columns (TowerLookRules.ColumnsForTier), each scaled to this tier's radius
        /// (TowerLookRules.ColumnRadius) and placed on the ring that keeps its outer edge tangent to this prefab's
        /// own collider (TowerLookRules.ColumnRingRadius) - never past it, so columns never add cover. Edit-time
        /// only, called by ArenaPrimitiveBuilder (arena step 3); nothing here runs at runtime.</summary>
        public void ApplyColumns(int tier)
        {
            int count = TowerLookRules.ColumnsForTier(tier);
            float shaftRadius = TowerLookRules.ColumnRadius(tier, columnRadius, capitalColumnRadius);
            float capRadius = shaftRadius * CapToShaftRatio;
            float ringRadius = TowerLookRules.ColumnRingRadius(shaftRadius, ColliderRadius());

            for (int i = 0; i < columnSlots.Length; i++)
            {
                Transform slot = columnSlots[i];
                if (slot == null)
                    continue;

                bool shown = i < count;
                slot.gameObject.SetActive(shown);
                if (!shown)
                    continue;

                float yaw = TowerLookRules.ColumnYawDegrees(i, count);
                slot.localPosition = AbilityVisualGeometry.CirclePoint(ringRadius, yaw);
                slot.localRotation = Quaternion.Euler(0f, yaw, 0f);

                // Resources.GetBuiltinResource<Mesh>("Cylinder.fbx") is 2 units wide (radius 1) at scale 1 - measured
                // when the prefab was authored (MakeTowerLook), not the 1-unit-wide the plan assumed - so scale.x/z
                // equal the radius directly.
                Transform shaft = slot.Find("Shaft");
                if (shaft != null)
                    shaft.localScale = new Vector3(shaftRadius, shaft.localScale.y, shaftRadius);
                Transform cap = slot.Find("Cap");
                if (cap != null)
                    cap.localScale = new Vector3(capRadius, cap.localScale.y, capRadius);
            }
        }

        private float ColliderRadius()
        {
            var capsule = GetComponent<CapsuleCollider>();
            return capsule != null ? capsule.radius : 2.6f;
        }

        public void Bind(UiTheme uiTheme)
        {
            theme = uiTheme;
            block ??= new MaterialPropertyBlock();
            bound = theme != null;
        }

        /// <summary>Called every frame by BuildingCapture.RefreshRingView, with exactly the same state and time the
        /// ring on the ground gets. Writes the property block only when the colour actually changed.</summary>
        public void Refresh(CaptureRingState state, float timeSeconds)
        {
            if (!bound)
                return;

            Color colour = OwnerPaintColours.For(OwnerPaint.From(state), theme, theme.towerNeutralColor, timeSeconds);
            if (shownColorSet && colour == shownColor)
                return;

            shownColor = colour;
            shownColorSet = true;

            if (crown != null)
                VisualTint.SetMeshColor(crown, block, colour);
            for (int i = 0; i < columnCaps.Length; i++)
            {
                Renderer cap = columnCaps[i];
                if (cap != null && cap.gameObject.activeInHierarchy)
                    VisualTint.SetMeshColor(cap, block, colour);
            }

            // The stone body (Plinth, Drum, every SHOWN shaft) stays on the Lit "Tower Stone" material and is
            // tinted rather than flat-repainted, so its own shading survives - the owner's colour times UiTheme's
            // Tower Body Shade (default 0.8), always dimmer than the Crown/caps' flat colour above. Alpha is put
            // back afterwards because Color * float scales every channel, alpha included.
            Color bodyColour = colour * theme.towerBodyShade;
            bodyColour.a = colour.a;

            if (plinth != null)
                VisualTint.SetMeshColor(plinth, block, bodyColour);
            if (drum != null)
                VisualTint.SetMeshColor(drum, block, bodyColour);
            for (int i = 0; i < columnShafts.Length; i++)
            {
                Renderer shaft = columnShafts[i];
                if (shaft != null && shaft.gameObject.activeInHierarchy)
                    VisualTint.SetMeshColor(shaft, block, bodyColour);
            }
        }
    }
}
