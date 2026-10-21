using Overpower.Abilities;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Arena
{
    /// <summary>
    /// The flat hexagon in a player's team colour on the floor under their feet, on every client's copy of every player (built by
    /// PlayerTeamAppearance as the body starts). The hexagon lives on a child this view switches by FeetHexRule; the view's own object
    /// stays active so LateUpdate keeps running, and the fog gate on it (VisibleWhenSeen, following the player like the respawn bubble)
    /// only touches Renderer.enabled. The gate collects renderers once, so the hexagon's renderer is built before it attaches. The team
    /// is the team property every client has, so a late joiner draws the same hexagons.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FeetHexView : MonoBehaviour
    {
        public const string RootName = "Feet Hex";
        public const string HexChildName = "Hex";
        public const string MeshName = "Feet Hex";

        private Transform body;
        private PhotonView player;
        private PlayerLifecycle life;
        private UiTheme theme;
        private ArenaLook look;
        private float feetBelowRoot;
        private GameObject hex;
        private MeshFilter filter;
        private VisibleWhenSeen gate;
        private Mesh mesh;
        private Color[] colours;
        private float builtRadius, builtEdgeWidth;
        private int dressedTeam = int.MinValue;
        private float dressedEdgeAlpha, dressedFillAlpha;

        public bool HexShown => hex != null && hex.activeSelf;
        public Mesh HexMesh => mesh;

        /// <summary>Builds the hidden hexagon under <paramref name="body"/>, whose feet are <paramref name="feetBelowRoot"/> below its
        /// position, or returns null (with a warning) when the theme has no Arena Look with FX Alpha.</summary>
        public static FeetHexView TryCreate(Transform body, PhotonView player, PlayerLifecycle life, UiTheme theme, float feetBelowRoot)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (body == null || look == null || look.fxAlpha == null)
            {
                Debug.LogWarning($"[FeetHexView] {(body != null ? body.name : "?")}: no UiTheme Arena Look with FX Alpha - this player has no team hexagon under their feet.");
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(body, false);
            var hex = new GameObject(HexChildName);
            hex.transform.SetParent(root.transform, false);
            hex.SetActive(false);
            MeshFilter filter = hex.AddComponent<MeshFilter>();
            MeshRenderer renderer = hex.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = look.fxAlpha;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            FeetHexView view = root.AddComponent<FeetHexView>();
            view.body = body;
            view.player = player;
            view.life = life;
            view.theme = theme;
            view.look = look;
            view.feetBelowRoot = feetBelowRoot;
            view.hex = hex;
            view.filter = filter;
            view.gate = VisibleWhenSeen.AttachToCaster(root, player);
            return view;
        }

        private void LateUpdate()
        {
            int team = player != null && Teams.TryGetTeam(player.Owner, out int t) ? t : -1;
            Step(team, life == null || life.IsAlive);
        }

        /// <summary>One frame for a player of <paramref name="team"/> (-1 = not known yet).</summary>
        public void Step(int team, bool alive)
        {
            if (FeetHexRule.Show(look.feetHexShown, team, alive))
            {
                // Null when this player was built before the local sight existed (a late join); asked again so the hexagon never shows ungated.
                if (gate == null)
                    gate = VisibleWhenSeen.AttachToCaster(gameObject, player);
                Dress(team);
                // World-aligned like the floor's hexagons, whichever way the player faces.
                hex.transform.SetPositionAndRotation(body.position + Vector3.up * (look.feetHexLift - feetBelowRoot), Quaternion.identity);
                if (!hex.activeSelf)
                    hex.SetActive(true);
            }
            else if (hex.activeSelf)
                hex.SetActive(false);
        }

        // Rebuilt or recoloured only when the team or a tunable changed, so a retune shows at once without work every frame.
        private void Dress(int team)
        {
            if (mesh == null || builtRadius != look.feetHexRadius || builtEdgeWidth != look.feetHexEdgeWidth)
                Rebuild();
            if (team == dressedTeam && dressedEdgeAlpha == look.feetHexEdgeAlpha && dressedFillAlpha == look.feetHexFillAlpha)
                return;
            // FX Alpha takes vertex colours as they are and the project renders in linear space, so hand them over linear.
            Color colour = LookColours.For(LookFamily.Team, team, theme).linear;
            Color edge = VisualTint.WithAlpha(colour, look.feetHexEdgeAlpha);
            Color fill = VisualTint.WithAlpha(colour, look.feetHexFillAlpha);
            for (int i = 0; i < colours.Length; i++)
                colours[i] = i < AoeRingMesh.FillStart ? edge : fill;
            mesh.colors = colours;
            dressedTeam = team;
            dressedEdgeAlpha = look.feetHexEdgeAlpha;
            dressedFillAlpha = look.feetHexFillAlpha;
        }

        private void Rebuild()
        {
            FreeMesh();
            // One AoE ring cell at the centre is exactly a hexagon outline (vertices before FillStart) and its fill.
            mesh = AoeRingMesh.Create(new[] { Vector2.zero }, look.feetHexRadius, look.feetHexEdgeWidth);
            mesh.name = MeshName;
            filter.sharedMesh = mesh;
            colours = new Color[mesh.vertexCount];
            builtRadius = look.feetHexRadius;
            builtEdgeWidth = look.feetHexEdgeWidth;
            dressedTeam = int.MinValue;
        }

        private void OnDestroy() => FreeMesh();

        private void FreeMesh()
        {
            if (mesh == null)
                return;
            if (filter != null && filter.sharedMesh == mesh)
                filter.sharedMesh = null;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        /// <summary>With domain reload on, a hexagon alive when Play Mode stopped lost its owner: free it by name.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
#if UNITY_EDITOR
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if (leftover != null && leftover.name == MeshName && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
    }
}
