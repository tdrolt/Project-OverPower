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
    ///
    /// UseSpawnLook(true) turns the same pieces into a Dominion spawn: a six-sided plinth, body and lid and UiTheme's
    /// Spawn Tower columns (up to six, so slots are cloned on demand). Every piece is still painted by Refresh, and the
    /// collider is never touched.
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
        [Tooltip("Each slot's Shaft renderer, in the same order as columnSlots - the Unlit 'Tower Owner' material, so " +
                 "Refresh paints it (like Plinth below) at the full owner colour, exactly like the crown/caps (no " +
                 "shade). Only a SHOWN slot's shaft is painted, same rule as columnCaps. Must not be Batching Static " +
                 "- see the comment above.")]
        public Renderer[] columnShafts = new Renderer[4];

        [Tooltip("The tower's base (Unlit 'Tower Owner' material) - painted with the full owner colour, exactly like " +
                 "the crown/caps/shown shafts (no shade). Must not be Batching Static.")]
        public Renderer plinth;

        [Tooltip("The tower's body (Lit 'Tower Stone' material) - painted with the owner's colour times UiTheme's " +
                 "Tower Body Shade (the only piece this shade still applies to), so the tower keeps a silhouette " +
                 "instead of turning into one flat block of colour. Must not be Batching Static.")]
        public Renderer drum;

        // Decision 6's starting value; the cap is drawn wider in CapToShaftRatio whichever shaft radius is showing.
        [Tooltip("Shaft radius, in metres, for every tier except the capital. The cap is drawn wider automatically, " +
                 "in a fixed proportion to this radius.")]
        public float columnRadius = 0.35f;

        // About 1.7x columnRadius, so the capital's one column reads as the grandest.
        [Tooltip("Shaft radius, in metres, for the capital's single column - bigger than columnRadius, so it reads " +
                 "as the grandest. The cap keeps the same shaft:cap proportion as every other tier's columns.")]
        public float capitalColumnRadius = 0.6f;

        private static Mesh hexMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeSharedMesh()
        {
            if (hexMesh != null)
                DestroyImmediate(hexMesh);
            hexMesh = null;
#if UNITY_EDITOR
            // With domain reload on, the static was already lost: the DontSave mesh of the last session is found by name.
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if (leftover.name == HexPrismMesh.MeshName)
                    DestroyImmediate(leftover);
#endif
        }
        private bool spawnLook;
        private bool roundShapesKept;
        private int appliedTier = 1;
        private Shape drumRound, plinthRound, crownRound;
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
            appliedTier = tier;
            if (spawnLook)
            {
                ApplySpawnColumns();
                return;
            }

            int count = TowerLookRules.ColumnsForTier(tier);
            float shaftRadius = TowerLookRules.ColumnRadius(tier, columnRadius, capitalColumnRadius);
            float ringRadius = TowerLookRules.ColumnRingRadius(shaftRadius, ColliderRadius());
            PlaceColumns(count, ringRadius, shaftRadius);
        }

        private void PlaceColumns(int count, float ringRadius, float shaftRadius)
        {
            float capRadius = shaftRadius * CapToShaftRatio;
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

        /// <summary>Dominion: the capital as a spawn, or back to the round tower. Needs Bind first; asking for the look it already has does nothing.
        /// Re-applies the columns, since a spawn's are not the tier's.</summary>
        public void UseSpawnLook(bool spawn)
        {
            if (spawn == spawnLook || !bound)
                return;
            if (!roundShapesKept)
            {
                drumRound = Shape.Of(drum);
                plinthRound = Shape.Of(plinth);
                crownRound = Shape.Of(crown);
                roundShapesKept = true;
            }
            spawnLook = spawn;
            if (spawn)
                ApplySpawnShapes();
            else
            {
                drumRound.Restore(drum);
                plinthRound.Restore(plinth);
                crownRound.Restore(crown);
            }
            ApplyColumns(appliedTier);
            shownColorSet = false;
        }

        // Radii are held to the collider here, not trusted from the asset: the footprint is what players hit and stand against.
        private void ApplySpawnShapes()
        {
            if (hexMesh == null)
                hexMesh = HexPrismMesh.Create();
            float collider = ColliderRadius();
            SetHex(drum, SpawnTowerRules.WithinCollider(theme.spawnTowerBodyRadius, collider), drumRound.scale.y, drumRound.position);
            SetHex(plinth, SpawnTowerRules.WithinCollider(theme.spawnTowerPlinthRadius, collider), plinthRound.scale.y, plinthRound.position);
            float lidHalf = theme.spawnTowerLidHeight / 2f;
            float bodyTop = drumRound.position.y + drumRound.scale.y;
            SetHex(crown, SpawnTowerRules.WithinCollider(theme.spawnTowerLidRadius, collider), lidHalf, new Vector3(crownRound.position.x, bodyTop + lidHalf, crownRound.position.z));
        }

        private static void SetHex(Renderer piece, float radius, float halfHeight, Vector3 position)
        {
            if (piece == null)
                return;
            piece.GetComponent<MeshFilter>().sharedMesh = hexMesh;
            piece.transform.localScale = new Vector3(radius, halfHeight, radius);
            piece.transform.localPosition = position;
        }

        private void ApplySpawnColumns()
        {
            int count = SpawnTowerRules.ColumnCount(theme.spawnTowerColumnCount);
            EnsureSlots(count);
            float collider = ColliderRadius();
            float shaftRadius = SpawnTowerRules.WithinCollider(theme.spawnTowerColumnRadius, collider);
            float ringRadius = SpawnTowerRules.ColumnRingRadius(
                SpawnTowerRules.WithinCollider(theme.spawnTowerBodyRadius, collider), shaftRadius * CapToShaftRatio, collider);
            PlaceColumns(count, ringRadius, shaftRadius);
        }

        // The prefab has four slots; a spawn can want six. Extra ones are copies of the first, kept (hidden) if the round look returns.
        private void EnsureSlots(int count)
        {
            Transform source = columnSlots.Length > 0 ? columnSlots[0] : null;
            if (source == null)
                return;
            while (columnSlots.Length < count)
            {
                Transform clone = Instantiate(source, source.parent);
                clone.name = "Column Slot " + columnSlots.Length;
                int last = columnSlots.Length;
                System.Array.Resize(ref columnSlots, last + 1);
                System.Array.Resize(ref columnCaps, last + 1);
                System.Array.Resize(ref columnShafts, last + 1);
                columnSlots[last] = clone;
                columnCaps[last] = clone.Find("Cap").GetComponent<Renderer>();
                columnShafts[last] = clone.Find("Shaft").GetComponent<Renderer>();
            }
        }

        private struct Shape
        {
            public Mesh mesh;
            public Vector3 position, scale;

            public static Shape Of(Renderer piece) => piece == null
                ? default
                : new Shape { mesh = piece.GetComponent<MeshFilter>().sharedMesh, position = piece.transform.localPosition, scale = piece.transform.localScale };

            public void Restore(Renderer piece)
            {
                if (piece == null)
                    return;
                piece.GetComponent<MeshFilter>().sharedMesh = mesh;
                piece.transform.localPosition = position;
                piece.transform.localScale = scale;
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
