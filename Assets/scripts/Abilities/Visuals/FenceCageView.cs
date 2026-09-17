using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Electric Fence as a cage (ability visuals step 4; Tudor: "walls instead of zones that have horizontal bars").
    /// Thin posts stand round the fence's real Radius with see-through horizontal bar circles between them, plus a faint
    /// band on the floor exactly as wide as Ring Thickness - where a hit really lands (an enemy whose centre is in the
    /// band, or crosses the ring). Owner's team colour.
    ///
    /// VISUAL ONLY, NO COLLIDERS. The fence damages and slows on crossing; it doesn't block. Posts or bars with colliders
    /// would stop players and shots, which is a gameplay change for Tudor to decide.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FenceCageView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For).")]
        private UiTheme theme;

        [SerializeField, Tooltip("One post, already on the prefab; copied round the ring. Its height and thickness are " +
                 "authored on its own transform (its centre height is half its height).")]
        private Transform post;

        [SerializeField, Tooltip("The horizontal bars: one circle each, at each bar's own height on the prefab.")]
        private LineRenderer[] bars;

        [SerializeField, Tooltip("A faint flat band on the floor, as wide as the fence's Ring Thickness.")]
        private LineRenderer band;

        [SerializeField, Tooltip("Largest gap between two neighbouring posts, in metres. Smaller reads more like a cage and " +
                 "costs more posts.")]
        private float maxPostSpacing = 2.5f;

        [SerializeField, Tooltip("Points per bar and band circle.")]
        private int circleSegments = 64;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the posts. See-through, so players inside stay visible.")]
        private float postOpacity = 0.85f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the bars.")]
        private float barOpacity = 0.85f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the floor band.")]
        private float bandOpacity = 0.2f;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            var fence = deployable as ElectricFence;
            if (fence == null || post == null)
                return;

            Color team = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            var block = new MaterialPropertyBlock();

            int count = AbilityVisualGeometry.CagePostCount(fence.Radius, maxPostSpacing);
            float postCentreY = post.localPosition.y;
            for (int i = 0; i < count; i++)
            {
                Transform p = i == 0 ? post : Instantiate(post, post.parent);
                float yaw = i * 360f / count;
                Vector3 onRing = AbilityVisualGeometry.CirclePoint(fence.Radius, yaw);
                p.localPosition = new Vector3(onRing.x, postCentreY, onRing.z);
                p.localRotation = Quaternion.Euler(0f, yaw, 0f);
                VisualTint.SetMeshColor(p.GetComponent<Renderer>(), block, VisualTint.WithAlpha(team, postOpacity));
            }

            foreach (LineRenderer bar in bars)
            {
                VisualTint.FillStandingCircle(bar, fence.Radius, circleSegments);
                VisualTint.SetLineColor(bar, VisualTint.WithAlpha(team, barOpacity));
            }

            if (band != null)
            {
                VisualTint.FillFlatCircle(band, fence.Radius, circleSegments);
                band.startWidth = fence.RingThickness;
                band.endWidth = fence.RingThickness;
                VisualTint.SetLineColor(band, VisualTint.WithAlpha(team, bandOpacity));
            }
        }
    }
}
