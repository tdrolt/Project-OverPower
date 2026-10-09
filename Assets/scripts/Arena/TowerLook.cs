using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Arena
{
    /// <summary>
    /// A round tower with as many columns as its tier, and pieces that change colour with who owns it. The prefab has
    /// four column slots (a shaft and a cap each); ApplyColumns shows the first N and hides the rest. The capital's one
    /// column is BIG, every other tier's share one normal size (columnRadius, capitalColumnRadius). The cap keeps the
    /// plan's shaft:cap proportion (Decision 6) at whichever shaft radius is showing, so one number retunes a column's
    /// whole silhouette (CODING-STANDARDS #3). TowerLookRules.ColumnRingRadius keeps a column's outer edge tangent to
    /// this prefab's collider radius, so columns never add cover over the plain round tower (Decision 6).
    ///
    /// The crown, every SHOWN cap, the Plinth and every SHOWN shaft share one Unlit material ("Tower Owner"), tinted
    /// through one reused MaterialPropertyBlock (VisualTint.SetMeshColor) - no material copy per tower - at the FULL
    /// owner colour. The Drum alone stays on the Lit "Tower Stone" material, tinted by UiTheme's Tower Body Shade, so
    /// the tower keeps a silhouette instead of one flat block of colour. Refresh gets exactly the same CaptureRingState
    /// the ring on the ground gets every frame (via OwnerPaint.From / OwnerPaintColours.For), so the tower and the ring
    /// can never disagree and a late joiner is right at once. The pieces are grey (the material's own colour) until the
    /// first Refresh; no collider here depends on the tier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TowerLook : MonoBehaviour
    {
        // Decision 6's shaft:cap proportion, applied to whichever shaft radius is showing so the cap never needs its
        // own separate number.
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

        // Trap: never mark these pieces (or Plinth/Drum) Batching Static. A statically-batched renderer draws through
        // its BATCH ROOT's combined mesh, so SetPropertyBlock on the ORIGINAL renderer (what VisualTint.SetMeshColor
        // calls) never reaches the screen, even though GetPropertyBlock/SetPropertyBlock both succeed and a test
        // reading the renderer's own block back looks green (TowerLookPrefabTests pins the flag).
        [Tooltip("Each slot's Shaft renderer, in the same order as columnSlots - the Unlit 'Tower Owner' material, " +
                 "so Refresh paints it (like Plinth below) at the full owner colour, exactly like the crown/caps " +
                 "(no shade - Tudor, 2026-09-23). Only a SHOWN slot's shaft is painted, same rule as columnCaps. " +
                 "Must not be Batching Static - see the comment above.")]
        public Renderer[] columnShafts = new Renderer[4];

        [Tooltip("The tower's base (Unlit 'Tower Owner' material, since 2026-09-23) - painted with the full owner " +
                 "colour, exactly like the crown/caps/shown shafts (no shade). Must not be Batching Static.")]
        public Renderer plinth;

        [Tooltip("The tower's body (Lit 'Tower Stone' material) - painted with the owner's colour times UiTheme's " +
                 "Tower Body Shade (the only piece this shade still applies to, since 2026-09-23), so the tower " +
                 "keeps a silhouette instead of turning into one flat block of colour. Must not be Batching Static.")]
        public Renderer drum;

        // Decision 6's starting value; the cap is drawn wider in CapToShaftRatio whichever shaft radius is showing.
        [Tooltip("Shaft radius, in metres, for every tier except the capital. The cap is drawn wider automatically, " +
                 "in a fixed proportion to this radius.")]
        public float columnRadius = 0.35f;

        // About 1.7x columnRadius, so the capital's one column reads as the grandest.
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
        /// own collider (TowerLookRules.ColumnRingRadius) - never past it, so columns never add cover. Called at
        /// edit time by ArenaPrimitiveBuilder; also at runtime through ApplyColumnsAtRuntime.</summary>
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

                // The built-in Cylinder mesh (Resources.GetBuiltinResource<Mesh>("Cylinder.fbx")) is 2 units wide
                // (radius 1) at scale 1, not the 1 unit wide the plan assumed - so scale.x/z equal the radius directly.
                Transform shaft = slot.Find("Shaft");
                if (shaft != null)
                    shaft.localScale = new Vector3(shaftRadius, shaft.localScale.y, shaftRadius);
                Transform cap = slot.Find("Cap");
                if (cap != null)
                    cap.localScale = new Vector3(capRadius, cap.localScale.y, capRadius);
            }
        }

        /// <summary>The same as ApplyColumns, at runtime - the centre drops to three columns while it plays as a Tier III
        /// (PhaseTwoCutRules.EffectiveTier) and gets its fourth back in a new room. Forces the next Refresh to repaint,
        /// since a column that was hidden when the colour was cached has none.</summary>
        public void ApplyColumnsAtRuntime(int tier)
        {
            ApplyColumns(tier);
            shownColorSet = false;
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
        /// ring on the ground gets. Writes the property block only when the painted colour actually changed.</summary>
        public void Refresh(CaptureRingState state, float timeSeconds)
        {
            if (!bound)
                return;

            OwnerPaint paint = OwnerPaint.From(state);
            Color colour = OwnerPaintColours.For(paint, theme, theme.towerNeutralColor, timeSeconds);

            // An OWNED tower glows: its flat colour is multiplied past 1 so a bright enough channel clears the scene's
            // Bloom threshold. Neutral and out-of-play towers never glow (glow stays 1, i.e. the plain flat colour), so
            // a glow always means "this is owned".
            float glow = paint.Base == OwnerPaintBase.Team ? theme.towerOwnerGlow : 1f;
            Color lit = colour * glow;
            lit.a = colour.a; // Color * float scales every channel, alpha included - put it back.

            // The cache compares the PAINTED colour (lit), not the raw owner colour - so tuning Tower Owner Glow
            // in the Inspector while Play Mode is running repaints at once instead of waiting for the next real
            // colour change (a capture, a drain, an attack pulse).
            if (shownColorSet && lit == shownColor)
                return;

            shownColor = lit;
            shownColorSet = true;

            if (crown != null)
                VisualTint.SetMeshColor(crown, block, lit);
            for (int i = 0; i < columnCaps.Length; i++)
            {
                Renderer cap = columnCaps[i];
                if (cap != null && cap.gameObject.activeInHierarchy)
                    VisualTint.SetMeshColor(cap, block, lit);
            }

            // The Plinth and every SHOWN shaft are on the Unlit "Tower Owner" material, so they get exactly the same
            // colour as the crown/caps above, glow included.
            if (plinth != null)
                VisualTint.SetMeshColor(plinth, block, lit);
            for (int i = 0; i < columnShafts.Length; i++)
            {
                Renderer shaft = columnShafts[i];
                if (shaft != null && shaft.gameObject.activeInHierarchy)
                    VisualTint.SetMeshColor(shaft, block, lit);
            }

            // The Drum alone stays on the Lit "Tower Stone" material and is tinted rather than flat-repainted, so its
            // own shading survives - the owner's RAW colour (never the glow) times UiTheme's Tower Body Shade (never
            // above 1), always dimmer than the glow every other painted piece gets above. That keeps the tower reading
            // as a silhouette rather than one flat block of colour; the Drum staying off the glow is also what keeps it
            // from blooming, on purpose - see UiTheme.towerOwnerGlow's tooltip. Alpha is put back afterwards, same
            // reason as above.
            if (drum != null)
            {
                Color bodyColour = colour * theme.towerBodyShade;
                bodyColour.a = colour.a;
                VisualTint.SetMeshColor(drum, block, bodyColour);
            }
        }
    }
}
