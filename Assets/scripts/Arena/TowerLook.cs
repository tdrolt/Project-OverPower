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

        [Tooltip("Shaft radius, in metres, for every tier except the capital (the plan's 0.35 m). The cap is drawn " +
                 "wider automatically, in the same proportion as the plan's 0.35 m shaft / 0.45 m cap.")]
        public float columnRadius = 0.35f;

        [Tooltip("Shaft radius, in metres, for the capital's single column (Tudor, 2026-09-19: \"the capital gets " +
                 "1 big column\") - about 1.7x columnRadius. The cap keeps the same shaft:cap proportion as every " +
                 "other tier's columns.")]
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
        }
    }
}
