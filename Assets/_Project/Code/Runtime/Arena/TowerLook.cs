using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Arena
{
    /// <summary>
    /// A capture tower with as many columns as its tier (four slots; ApplyColumns shows the first N, each kept inside the
    /// collider by TowerLookRules.ColumnRingRadius). Bind makes the pieces eight-sided at the prefab's own scales and puts
    /// the body and shafts on ArenaLook's Tower Body, whose stripes read the owner from the ground under them. Refresh
    /// paints the crown, the plinth and every SHOWN cap with the same CaptureRingState the ring gets. UseSpawnLook(true)
    /// gives a Dominion spawn its own look: six-sided stone body, round columns, every piece in the team colour.
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

        [Tooltip("The crown on top of the tower - it glows in the owner's colour, like the base and the shown caps.")]
        public Renderer crown;

        // Trap: never mark these pieces (or Plinth/Drum) Batching Static. A statically-batched renderer draws through
        // its BATCH ROOT's combined mesh, so SetPropertyBlock on the ORIGINAL renderer (what VisualTint.SetMeshColor
        // calls) never reaches the screen, even though GetPropertyBlock/SetPropertyBlock both succeed and a test
        // reading the renderer's own block back looks green (TowerLookPrefabTests pins the flag).
        [Tooltip("Each slot's Shaft renderer, in the same order as columnSlots. On a capture tower it wears the dark " +
                 "Tower Body look like the drum and is never painted; on a Dominion spawn it is painted in the team " +
                 "colour like the crown. Must not be Batching Static - see the comment above.")]
        public Renderer[] columnShafts = new Renderer[4];

        [Tooltip("The tower's base ring (Unlit 'Tower Owner' material) - it glows in the owner's colour, exactly like " +
                 "the crown and the shown caps. Must not be Batching Static.")]
        public Renderer plinth;

        [Tooltip("The tower's body. On a capture tower it wears ArenaLook's Tower Body (dark, with circuit stripes in " +
                 "the owner's colour) and is never painted; on a Dominion spawn it is the Lit 'Tower Stone' painted " +
                 "with the team colour times UiTheme's Tower Body Shade. Must not be Batching Static.")]
        public Renderer drum;

        // Decision 6's starting value; the cap is drawn wider in CapToShaftRatio whichever shaft radius is showing.
        [Tooltip("Shaft radius, in metres, for every tier except the capital. The cap is drawn wider automatically, " +
                 "in a fixed proportion to this radius.")]
        public float columnRadius = 0.35f;

        // About 1.7x columnRadius, so the capital's one column reads as the grandest.
        [Tooltip("Shaft radius, in metres, for the capital's single column - bigger than columnRadius, so it reads " +
                 "as the grandest. The cap keeps the same shaft:cap proportion as every other tier's columns.")]
        public float capitalColumnRadius = 0.6f;

        public const string CrownInsetName = "Crown Inset";
        private const float CrownInsetHalfHeight = 0.01f;

        private static Mesh hexMesh;
        private static Mesh octagonMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeSharedMesh()
        {
            if (hexMesh != null)
                DestroyImmediate(hexMesh);
            hexMesh = null;
            if (octagonMesh != null)
                DestroyImmediate(octagonMesh);
            octagonMesh = null;
#if UNITY_EDITOR
            // With domain reload on, the statics were already lost: the DontSave meshes of the last session are found by name.
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if ((leftover.name == HexPrismMesh.MeshName || leftover.name == OctagonPrismMesh.MeshName)
                    && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
        private bool spawnLook;
        private bool roundShapesKept;
        private int appliedTier = 1;
        private Shape drumRound, plinthRound, crownRound;
        // The prefab's own column meshes and materials, which a Dominion spawn wears.
        private Mesh roundShaft, roundCap;
        private Material stoneDrum, ownerShaft;
        private GameObject crownInset;
        private bool bodyFromGround;
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
            spawnLook = spawn;
            if (spawn)
            {
                ApplyRoundColumnsAndStone();
                ApplySpawnShapes();
            }
            else
            {
                drumRound.Restore(drum);
                plinthRound.Restore(plinth);
                crownRound.Restore(crown);
                ApplyZoneShapes();
            }
            ApplyColumns(appliedTier);
            shownColorSet = false;
        }

        private void KeepRoundShapes()
        {
            drumRound = Shape.Of(drum);
            plinthRound = Shape.Of(plinth);
            crownRound = Shape.Of(crown);
            Renderer shaft = columnShafts.Length > 0 ? columnShafts[0] : null;
            Renderer cap = columnCaps.Length > 0 ? columnCaps[0] : null;
            roundShaft = shaft != null ? shaft.GetComponent<MeshFilter>().sharedMesh : null;
            ownerShaft = shaft != null ? shaft.sharedMaterial : null;
            roundCap = cap != null ? cap.GetComponent<MeshFilter>().sharedMesh : null;
            stoneDrum = drum != null ? drum.sharedMaterial : null;
            roundShapesKept = true;
        }

        // Same scales and places as the round pieces, so the footprint and the collider fit stay exactly as they were.
        private void ApplyZoneShapes()
        {
            if (octagonMesh == null)
                octagonMesh = OctagonPrismMesh.Create();
            Material body = theme.arenaLook != null ? theme.arenaLook.towerBody : null;
            bodyFromGround = body != null;
            SetMesh(drum, octagonMesh);
            SetMesh(plinth, octagonMesh);
            SetMesh(crown, octagonMesh);
            WearBody(drum, body);
            foreach (Renderer shaft in columnShafts)
            {
                SetMesh(shaft, octagonMesh);
                WearBody(shaft, body);
            }
            foreach (Renderer cap in columnCaps)
                SetMesh(cap, octagonMesh);
            ShowCrownInset(body);
        }

        // A dark skin on the lid, so from above an owned tower reads as a glowing rim, not a solid disc of its colour.
        // Cloned from the crown so it is born in the tower's own scene (a new GameObject would land in the open scene).
        private void ShowCrownInset(Material body)
        {
            float share = theme.arenaLook != null ? theme.arenaLook.towerCrownInset : 0f;
            if (crown == null || body == null || share <= 0f)
            {
                if (crownInset != null)
                    crownInset.SetActive(false);
                return;
            }
            if (crownInset == null)
            {
                Transform found = crown.transform.parent.Find(CrownInsetName);
                crownInset = found != null ? found.gameObject : Instantiate(crown.gameObject, crown.transform.parent);
                crownInset.name = CrownInsetName;
            }
            Transform lid = crown.transform;
            float lidTop = lid.localPosition.y + lid.localScale.y;
            crownInset.transform.localPosition = new Vector3(lid.localPosition.x, lidTop + CrownInsetHalfHeight, lid.localPosition.z);
            crownInset.transform.localRotation = lid.localRotation;
            crownInset.transform.localScale = new Vector3(lid.localScale.x * share, CrownInsetHalfHeight, lid.localScale.z * share);
            crownInset.GetComponent<MeshFilter>().sharedMesh = octagonMesh;
            var top = crownInset.GetComponent<Renderer>();
            top.sharedMaterial = body;
            top.SetPropertyBlock(null);
            top.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            crownInset.SetActive(true);
        }

        private void ApplyRoundColumnsAndStone()
        {
            if (crownInset != null)
                crownInset.SetActive(false);
            if (drum != null && stoneDrum != null)
                drum.sharedMaterial = stoneDrum;
            foreach (Renderer shaft in columnShafts)
            {
                SetMesh(shaft, roundShaft);
                if (shaft != null && ownerShaft != null)
                    shaft.sharedMaterial = ownerShaft;
            }
            foreach (Renderer cap in columnCaps)
                SetMesh(cap, roundCap);
        }

        private static void SetMesh(Renderer piece, Mesh mesh)
        {
            if (piece != null && mesh != null)
                piece.GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        // The body's colour comes from the ground under it (the arena shader), so it carries no paint of its own; a
        // spawn's leftover paint is cleared too, which also keeps it in the SRP Batcher.
        private static void WearBody(Renderer piece, Material body)
        {
            if (piece == null)
                return;
            if (body != null)
                piece.sharedMaterial = body;
            piece.SetPropertyBlock(null);
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
            if (!bound)
                return;
            if (!roundShapesKept)
                KeepRoundShapes();
            if (!spawnLook)
                ApplyZoneShapes();
        }

        /// <summary>Called every frame by BuildingCapture.RefreshRingView, with exactly the same state and time the
        /// ring on the ground gets. Writes the property block only when the painted colour actually changed.</summary>
        public void Refresh(CaptureRingState state, float timeSeconds)
        {
            if (!bound)
                return;

            OwnerPaint paint = OwnerPaint.From(state);
            Color colour = OwnerPaintColours.For(paint, theme, theme.towerNeutralColor, timeSeconds);

            // Only an OWNED tower glows past 1 (the scene's Bloom threshold), so a glow always means "this is owned". A
            // spawn's look is fixed and keeps the plain multiply (Color * float scales alpha too - put it back).
            float glow = paint.Base == OwnerPaintBase.Team ? theme.towerOwnerGlow : 1f;
            Color lit = spawnLook ? colour * glow : TowerLookRules.GlowKeepingHue(colour, glow);
            lit.a = colour.a;

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
            if (plinth != null)
                VisualTint.SetMeshColor(plinth, block, lit);

            // A capture tower's body and shafts take their colour from the ground; without a Tower Body to show it they
            // are painted like a spawn's rather than left bare.
            if (!spawnLook && bodyFromGround)
                return;
            for (int i = 0; i < columnShafts.Length; i++)
            {
                Renderer shaft = columnShafts[i];
                if (shaft != null && shaft.gameObject.activeInHierarchy)
                    VisualTint.SetMeshColor(shaft, block, lit);
            }

            // The spawn's Drum (Lit "Tower Stone") takes the RAW colour times Tower Body Shade, never the glow, so the
            // spawn keeps a shaded silhouette and its body never blooms. Alpha is put back, same reason as above.
            if (drum != null)
            {
                Color bodyColour = colour * theme.towerBodyShade;
                bodyColour.a = colour.a;
                VisualTint.SetMeshColor(drum, block, bodyColour);
            }
        }
    }
}
