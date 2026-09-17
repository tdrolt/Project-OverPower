using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a portal looks like (ability visuals step 3). A see-through disc at the real diameter with a bright rim, in
    /// the owner's team colour: wide and light, where a mine is small and dark (A8, Tudor 2026-09-17 evening: the
    /// colour reads on the OUTER EDGE of a portal - MineView's own class comment is the opposite rule; the rim is the
    /// dominant colour, the centre stays see-through and darker). Only the player who placed it also sees a floating
    /// diamond on a stem - "this one is yours to use" - because nobody else, teammates included, can use it (Portal's
    /// class comment). Enemies still see the disc and rim, as they always saw the portal.
    ///
    /// Visual only: the rim is drawn from Portal.Radius, the same number TeleportAbility's channel check uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For).")]
        private UiTheme theme;

        [SerializeField, Tooltip("The see-through disc. It is Portal's own Visual, so Portal scales it to Portal Diameter.")]
        private Renderer footprint;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the disc. Low, so whoever stands in it stays visible, and " +
                 "so the bright rim (A8: colour on the outer edge) reads as the dominant colour, not the centre.")]
        private float footprintOpacity = 0.3f;

        [SerializeField, Tooltip("The bright outline at the portal's real edge.")]
        private LineRenderer rim;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the outline. High - A8: the rim is the dominant colour " +
                 "that makes a portal read as a portal, never a mine.")]
        private float rimOpacity = 0.95f;

        [SerializeField, Tooltip("Points on the outline circle.")]
        private int rimSegments = 48;

        [SerializeField, Tooltip("Shown only on the screen of the player who placed this portal.")]
        private GameObject ownerBeacon;

        [SerializeField, Tooltip("The floating diamond inside the owner beacon.")]
        private Renderer beacon;

        [SerializeField, Tooltip("The thin stem under the diamond.")]
        private Renderer stem;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the stem.")]
        private float stemOpacity = 0.6f;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            var portal = deployable as Portal;
            if (portal == null)
                return;

            Color team = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            var block = new MaterialPropertyBlock();
            VisualTint.SetMeshColor(footprint, block, VisualTint.WithAlpha(team, footprintOpacity));
            VisualTint.SetMeshColor(beacon, block, VisualTint.WithAlpha(team, 1f));
            VisualTint.SetMeshColor(stem, block, VisualTint.WithAlpha(team, stemOpacity));
            VisualTint.FillFlatCircle(rim, portal.Radius, rimSegments);
            VisualTint.SetLineColor(rim, VisualTint.WithAlpha(team, rimOpacity));

            if (ownerBeacon != null)
                ownerBeacon.SetActive(PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == deployable.OwnerActor);
        }
    }
}
