using Overpower.Net;
using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a mine looks like (ability visuals step 3; Tudor: mines and portals looked the same). A small dark puck with
    /// four stubby spikes and a stud in the owner's team colour, lying on the floor: squat, dark and small, where a
    /// portal is wide, light and see-through (A8, Tudor 2026-09-17 evening: the colour reads on the MIDDLE of a mine,
    /// never a rim - PortalView's own class comment is the opposite rule). The owner's own team also sees a thin ring
    /// at the real Trigger Radius; enemies don't (they only ever saw the mine itself, and still only do). When the
    /// mine goes off, every client flashes a BlastMarker at the real Explosion Radius.
    ///
    /// Visual only. Every size comes from Mine itself, and the detonation is noticed without touching the detonation
    /// code: Mine hides its own Visual on every client the instant it goes off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MineView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For), the same " +
                 "colours as each team's shots.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The dark parts: the puck and its spikes.")]
        private Renderer[] bodyParts;

        [SerializeField, Tooltip("Colour of the dark parts. Dark so a mine never reads as a portal.")]
        private Color bodyColor = new Color(0.1f, 0.1f, 0.12f, 1f);

        [SerializeField, Tooltip("The parts drawn in the owner's team colour: the stud on top.")]
        private Renderer[] teamParts;

        [SerializeField, Tooltip("Flat ring at the mine's Trigger Radius, shown only to the owner's own team.")]
        private LineRenderer triggerRing;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the trigger ring.")]
        private float triggerRingOpacity = 0.5f;

        [SerializeField, Tooltip("Points on the trigger ring.")]
        private int triggerRingSegments = 48;

        [SerializeField, Tooltip("The flash on the floor when the mine goes off - Assets/Gameplay/Projectiles/Blast Marker.prefab.")]
        private BlastMarker blastMarkerPrefab;

        [SerializeField, Tooltip("The mine's Visual: the container Snap Visual To Ground puts on the floor, and the one " +
                 "Mine hides when it detonates.")]
        private Transform visualRoot;

        private Mine mine;
        private Color teamColor = Color.white;
        private bool blastShown;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            mine = deployable as Mine;
            if (mine == null)
                return;

            teamColor = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            var block = new MaterialPropertyBlock();
            foreach (Renderer part in bodyParts)
                VisualTint.SetMeshColor(part, block, bodyColor);
            foreach (Renderer part in teamParts)
                VisualTint.SetMeshColor(part, block, teamColor);

            if (triggerRing != null)
            {
                VisualTint.FillFlatCircle(triggerRing, mine.TriggerRadius, triggerRingSegments);
                VisualTint.SetLineColor(triggerRing, VisualTint.WithAlpha(teamColor, triggerRingOpacity));
                triggerRing.enabled = IsLocalPlayersTeam(deployable.OwnerTeam);
            }
        }

        private static bool IsLocalPlayersTeam(int team) =>
            team >= 0 && PhotonNetwork.LocalPlayer != null &&
            Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int localTeam) && localTeam == team;

        private void LateUpdate()
        {
            // Mine.RPC_Detonate hides the Visual on every client the instant it goes off. A lifetime expiry or a prune
            // destroys the mine instead, and an expired late-join copy only switches its renderers off, so neither of
            // those flashes a blast.
            if (mine == null || blastShown || visualRoot == null || visualRoot.gameObject.activeSelf)
                return;

            blastShown = true;
            Vector3 at = mine.transform.position;
            BlastMarker.Spawn(blastMarkerPrefab, new Vector3(at.x, visualRoot.position.y, at.z), mine.ExplosionRadius, teamColor);
        }
    }
}
