using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Electric Fence: pylons round its real Radius, jagged arcs between neighbours at each bar's height and a faint floor band as
    /// wide as Ring Thickness (where a hit really lands), in the Fence's row of the look table. The arcs take a new shape at Fence Arc
    /// Rate (ArenaLook) from the server clock and the fence's view id, so every client draws the same; only line points change.
    /// VISUAL ONLY, NO COLLIDERS: the fence damages and slows on crossing, it never blocks players or shots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FenceCageView : MonoBehaviour, IDeployableView
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Tooltip("Team colours and the Arena Look - Assets/Gameplay/Config/UiTheme.asset.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this fence: the Electric Fence ability's id.")]
        private int abilityId = 26;

        [SerializeField, Tooltip("One pylon, already on the prefab with its foot at the floor; copied round the ring. Its size is " +
                 "authored on its own transform.")]
        private Transform post;

        [SerializeField, Tooltip("The electric arcs: one jagged loop through every pylon each, at each bar's own height on the prefab.")]
        private LineRenderer[] bars;

        [SerializeField, Tooltip("A faint flat band on the floor, as wide as the fence's Ring Thickness.")]
        private LineRenderer band;

        [SerializeField, Tooltip("Largest gap between two neighbouring posts, in metres. Smaller reads more like a cage and " +
                 "costs more posts.")]
        private float maxPostSpacing = 2.5f;

        [SerializeField, Tooltip("Points on the floor band's circle.")]
        private int circleSegments = 64;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the arcs.")]
        private float barOpacity = 0.85f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the floor band.")]
        private float bandOpacity = 0.2f;

        private ArenaLook look;
        private float radius;
        private int posts;
        private int salt;
        private int shapedSeed;
        private bool shaped;
        private Vector3[] points;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            var fence = deployable as ElectricFence;
            if (fence == null || post == null)
                return;

            Color team = theme != null ? LookColours.ForAbility(theme, abilityId, deployable.OwnerTeam, LookFamily.Team) : Color.white;
            look = theme != null ? theme.arenaLook : null;
            radius = fence.Radius;
            salt = fence.photonView != null ? fence.photonView.ViewID : 0;
            var block = new MaterialPropertyBlock();

            posts = AbilityVisualGeometry.CagePostCount(fence.Radius, maxPostSpacing);
            float footY = post.localPosition.y;
            for (int i = 0; i < posts; i++)
            {
                Transform p = i == 0 ? post : Instantiate(post, post.parent);
                float yaw = i * 360f / posts;
                Vector3 onRing = AbilityVisualGeometry.CirclePoint(fence.Radius, yaw);
                p.localPosition = new Vector3(onRing.x, footY, onRing.z);
                p.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var pylon = p.GetComponent<Renderer>();
                if (pylon == null || look == null)
                    continue;
                pylon.GetPropertyBlock(block);
                block.SetColor(BaseColorId, FenceArcsRule.PylonBody(team, look.fencePylonShade));
                block.SetColor(EmissionId, FenceArcsRule.PylonGlow(team, look.fencePylonGlow));
                pylon.SetPropertyBlock(block);
            }

            if (look != null && bars != null)
            {
                points = new Vector3[FenceArcsRule.PointCount(posts, look.fenceArcPointsPerGap)];
                var glow = new MaterialPropertyBlock();
                glow.SetColor(BaseColorId, new Color(look.fenceArcGlow, look.fenceArcGlow, look.fenceArcGlow, 1f));
                foreach (LineRenderer bar in bars)
                {
                    if (bar == null)
                        continue;
                    bar.useWorldSpace = false;
                    bar.loop = true;
                    bar.positionCount = points.Length;
                    bar.startWidth = bar.endWidth = look.fenceArcWidth;
                    VisualTint.SetLineColor(bar, VisualTint.WithAlpha(team, barOpacity));
                    bar.SetPropertyBlock(glow); // the glow rides on the shared line material's colour multiplier, per renderer
                }
                shaped = false;
                Reshape(PhotonNetwork.ServerTimestamp);
            }

            if (band != null)
            {
                VisualTint.FillFlatCircle(band, fence.Radius, circleSegments);
                band.startWidth = fence.RingThickness;
                band.endWidth = fence.RingThickness;
                VisualTint.SetLineColor(band, VisualTint.WithAlpha(team, bandOpacity));
            }
        }

        private void LateUpdate()
        {
            if (points != null)
                Reshape(PhotonNetwork.ServerTimestamp);
        }

        /// <summary>Puts every arc into the shape of this server time's step; within one step of Fence Arc Rate nothing changes.</summary>
        public void Reshape(int serverMs)
        {
            if (points == null || look == null)
                return;
            int seed = StunArcsRule.Seed(serverMs, salt, look.fenceArcRate);
            if (shaped && seed == shapedSeed)
                return;
            shaped = true;
            shapedSeed = seed;
            for (int level = 0; level < bars.Length; level++)
            {
                if (bars[level] == null)
                    continue;
                for (int i = 0; i < points.Length; i++)
                    points[i] = FenceArcsRule.Point(seed, level, i, posts, look.fenceArcPointsPerGap, radius, look.fenceArcJitter);
                bars[level].SetPositions(points);
            }
        }
    }
}
