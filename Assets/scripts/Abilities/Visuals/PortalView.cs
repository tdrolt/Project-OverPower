using Overpower.Combat;
using Overpower.UI;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a portal looks like: a see-through disc at the real diameter with a bright rim, in the owner's team colour -
    /// wide and light, where a mine is small and dark; the colour reads on the OUTER EDGE (A8, the opposite of MineView),
    /// the centre stays see-through and darker. Only the player who placed it also sees a floating diamond on a stem
    /// ("this one is yours"); enemies still see the disc and rim.
    ///
    /// COOLING DOWN (D23): while the owner has no portal charge - after anyone's trip, until it recharges - the
    /// whole glow turns grey, on every player's screen. The rule is the owner's published "has a charge" Player Property
    /// (AllyPortalTraveller.ReadyKey), the same one for the owner's own client as for everyone else.
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

        private Portal portal;
        private Color team;
        private bool? shownUsable;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            portal = deployable as Portal;
            if (portal == null)
                return;

            team = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            VisualTint.FillFlatCircle(rim, portal.Radius, rimSegments);
            Paint(true);

            if (ownerBeacon != null)
                ownerBeacon.SetActive(PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == deployable.OwnerActor);
        }

        private void Update()
        {
            if (portal == null)
                return;
            bool usable = PortalUseRules.ShowsUsable(TryReadOwnerCharge(out bool ownerHasCharge), ownerHasCharge);
            if (shownUsable != usable)
                Paint(usable);
        }

        private bool TryReadOwnerCharge(out bool ownerHasCharge)
        {
            ownerHasCharge = false;
            Room room = PhotonNetwork.CurrentRoom;
            Player owner = room != null ? room.GetPlayer(portal.OwnerActor) : null;
            if (owner == null || !owner.CustomProperties.TryGetValue(AllyPortalTraveller.ReadyKey, out object raw) || !(raw is bool ready))
                return false;
            ownerHasCharge = ready;
            return true;
        }

        /// <summary>Draws every part in the team colour, or in the theme's cooling-down grey; opacities stay as tuned.</summary>
        private void Paint(bool usable)
        {
            shownUsable = usable;
            Color colour = usable || theme == null ? team : theme.portalCooldownColor;
            var block = new MaterialPropertyBlock();
            VisualTint.SetMeshColor(footprint, block, VisualTint.WithAlpha(colour, footprintOpacity));
            VisualTint.SetMeshColor(beacon, block, VisualTint.WithAlpha(colour, 1f));
            VisualTint.SetMeshColor(stem, block, VisualTint.WithAlpha(colour, stemOpacity));
            VisualTint.SetLineColor(rim, VisualTint.WithAlpha(colour, rimOpacity));
        }
    }
}
