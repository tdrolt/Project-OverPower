using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a portal looks like: a flat hexagon gate at the real size - a see-through hexagon floor, a bright hexagon frame whose
    /// corners sit on the real edge, and a thinner hexagon inside turning slowly with the server clock - in the colour of the Portal's
    /// row in the look table (the owner's team colour). Wide and light, where a mine is small and dark; the colour reads on the OUTER
    /// EDGE (A8, the opposite of MineView), the centre stays see-through and darker. Only the player who placed it also sees a floating
    /// diamond on a stem ("this one is yours"); enemies still see the gate.
    ///
    /// COOLING DOWN (D23): while the owner has no portal charge - after anyone's trip, until it recharges - the
    /// whole glow turns grey, on every player's screen. The rule is the owner's published "has a charge" Player Property
    /// (AllyPortalTraveller.ReadyKey), the same one for the owner's own client as for everyone else.
    ///
    /// Visual only: the frame is drawn from Portal.Radius, the same number TeleportAbility's channel check uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours and the Arena Look - Assets/Gameplay/Config/UiTheme.asset.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this portal: the Teleport ability's id.")]
        private int abilityId = 17;

        [SerializeField, Tooltip("The see-through hexagon floor of the gate. It is Portal's own Visual, so Portal scales it to Portal Diameter.")]
        private Renderer footprint;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the disc. Low, so whoever stands in it stays visible, and " +
                 "so the bright rim (A8: colour on the outer edge) reads as the dominant colour, not the centre.")]
        private float footprintOpacity = 0.3f;

        [SerializeField, Tooltip("The bright hexagon frame at the portal's real edge.")]
        private LineRenderer rim;

        [SerializeField, Tooltip("The thinner hexagon inside the frame that turns slowly.")]
        private LineRenderer innerHex;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the outline. High - A8: the rim is the dominant colour " +
                 "that makes a portal read as a portal, never a mine.")]
        private float rimOpacity = 0.95f;

        [SerializeField, Tooltip("Shown only on the screen of the player who placed this portal.")]
        private GameObject ownerBeacon;

        [SerializeField, Tooltip("The floating diamond inside the owner beacon.")]
        private Renderer beacon;

        [SerializeField, Tooltip("The thin stem under the diamond.")]
        private Renderer stem;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the stem.")]
        private float stemOpacity = 0.6f;

        private const int GateSides = 6;

        private Portal portal;
        private Color team;
        private bool? shownUsable;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            portal = deployable as Portal;
            if (portal == null)
                return;

            team = theme != null ? LookColours.ForAbility(theme, abilityId, deployable.OwnerTeam, LookFamily.Team) : Color.white;
            ArenaLook look = theme != null ? theme.arenaLook : null;
            VisualTint.FillFlatCircle(rim, portal.Radius, GateSides);
            if (rim != null && look != null)
                rim.startWidth = rim.endWidth = look.portalFrameWidth;
            if (innerHex != null && look != null)
            {
                VisualTint.FillFlatCircle(innerHex, portal.Radius * look.portalInnerScale, GateSides);
                innerHex.startWidth = innerHex.endWidth = look.portalInnerWidth;
            }
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
            // MUTATION no turn
        }

        /// <summary>Turns the inner hexagon about the floor's up axis to the server clock's angle; it keeps lying flat.</summary>
        public void TurnInnerHex(int serverMs)
        {
            if (innerHex == null || theme == null || theme.arenaLook == null)
                return;
            innerHex.transform.localRotation = Quaternion.Euler(90f, PortalGateLook.InnerYaw(serverMs, theme.arenaLook.portalInnerTurnSpeed), 0f);
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
            VisualTint.SetLineColor(innerHex, VisualTint.WithAlpha(colour, rimOpacity));
        }
    }
}
