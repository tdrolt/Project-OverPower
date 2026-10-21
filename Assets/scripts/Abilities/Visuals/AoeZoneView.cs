using System.Collections.Generic;
using Overpower.Arena;
using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The AoE Zone: a hard edge line on its real Radius and a band of hexagon cells just inside it, in the AoE Zone's row of the look table.
    /// The cells flicker at Aoe Flicker Rate (ArenaLook) from the server clock and the caster, so every client lights the same cells; only
    /// vertex colours change. Both parts are on the prefab, so the zone's fog gate (collected in its Awake) hides them with it.
    /// The zone sits on its caster's root, half a metre up, so the ring is put back on the floor whenever the zone moves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AoeZoneView : MonoBehaviour, IDeployableView
    {
        private const int EdgeSegments = 72;

        [SerializeField, Tooltip("Where the colour (the look table's row for the ability below) and every ring number come from: " +
                 "Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this zone: the AoE Zone ability's id.")]
        private int abilityId = 27;

        [SerializeField, Tooltip("The holder of the cells and the edge, kept on the floor under the zone.")]
        private Transform ring;

        [SerializeField, Tooltip("The hexagon cells: a flat mesh built when the zone is placed.")]
        private MeshFilter cells;

        [SerializeField, Tooltip("The hard line round the zone's real edge, lying flat.")]
        private LineRenderer edge;

        private readonly List<Vector2> centres = new List<Vector2>();
        private ArenaLook look;
        private Mesh mesh;
        private Color[] colours;
        private Color danger;
        private Color cellDanger; // the same red for the cells' vertex colours
        private int salt;
        private int shownSeed;
        private bool shown;
        private Vector3 groundedAt;
        private bool grounded;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            var zone = deployable as AoeZone;
            if (zone == null || theme == null || theme.arenaLook == null)
                return;
            look = theme.arenaLook;
            danger = LookColours.ForAbility(theme, abilityId, zone.OwnerTeam, LookFamily.Danger);
            cellDanger = VisualTint.VertexColour(danger);
            salt = zone.OwnerActor;
            float radius = zone.Radius;

            if (edge != null)
            {
                VisualTint.FillFlatCircle(edge, radius, EdgeSegments);
                edge.startWidth = edge.endWidth = look.aoeEdgeWidth;
                VisualTint.SetLineColor(edge, VisualTint.WithAlpha(danger, look.aoeEdgeAlpha));
            }

            if (cells != null)
            {
                AoeRingLook.CellCentres(radius, look.aoeRingBand, look.aoeCellSize, centres);
                FreeMesh();
                mesh = AoeRingMesh.Create(centres, AoeRingLook.Circumradius(look.aoeCellSize, look.aoeCellGap), look.aoeCellLineWidth);
                cells.sharedMesh = mesh;
                colours = new Color[mesh.vertexCount];
                shown = false;
                Flicker(PhotonNetwork.ServerTimestamp);
            }
        }

        private void LateUpdate()
        {
            if (mesh == null)
                return;
            Flicker(PhotonNetwork.ServerTimestamp);

            Vector3 at = transform.position;
            if (ring == null || (grounded && (at - groundedAt).sqrMagnitude < 0.0001f))
                return;
            grounded = true;
            groundedAt = at;
            GroundSnap.TryFindGroundY(at, out float groundY);
            ring.position = new Vector3(ring.position.x, groundY + look.aoeLift, ring.position.z);
        }

        /// <summary>Lights this server time's cells; within one step of Aoe Flicker Rate nothing changes.</summary>
        public void Flicker(int serverMs)
        {
            if (mesh == null || look == null)
                return;
            int seed = StunArcsRule.Seed(serverMs, salt, look.aoeFlickerRate);
            if (shown && seed == shownSeed)
                return;
            shown = true;
            shownSeed = seed;
            for (int i = 0; i < centres.Count; i++)
            {
                bool lit = AoeRingLook.Lit(seed, i, look.aoeLitShare);
                Color line = AoeRingLook.Line(cellDanger, lit, look.aoeLitLineAlpha, look.aoeDimLineAlpha);
                Color fill = AoeRingLook.Fill(cellDanger, lit, look.aoeLitFillAlpha);
                int b = i * AoeRingMesh.VerticesPerCell;
                for (int v = 0; v < AoeRingMesh.FillStart; v++)
                    colours[b + v] = line;
                for (int v = AoeRingMesh.FillStart; v < AoeRingMesh.VerticesPerCell; v++)
                    colours[b + v] = fill;
            }
            mesh.colors = colours;
        }

        private void OnDestroy() => FreeMesh();

        private void FreeMesh()
        {
            if (mesh == null)
                return;
            if (cells != null && cells.sharedMesh == mesh)
                cells.sharedMesh = null;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        /// <summary>With domain reload on, a ring alive when Play Mode stopped lost its owner: free it by name.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
#if UNITY_EDITOR
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if (leftover != null && leftover.name == AoeRingMesh.Name && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
    }
}
