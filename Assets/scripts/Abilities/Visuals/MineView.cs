using Overpower.Combat;
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
    /// A5 (Tudor 2026-09-17 evening, GDD spec): once Mine.InvisibleAfterSeconds has passed, an enemy sees nothing at
    /// all, and the owner's own team sees a faded, translucent Ghost instead - MineVisibilityRule decides which, from
    /// Mine.SecondsSincePlaced (Age, a network-agreed snapshot, so every client switches at the true placement moment
    /// regardless of its own lag - no timestamp or RPC of this view's own). Hiding toggles each part's own
    /// Renderer.enabled rather than the Visual root's GameObject, so it never collides with LateUpdate's own
    /// detonation check below (Mine.Detonated, not the Visual's active state).
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

        [Header("Ghost (A5 - the owner's own team, once otherwise invisible)")]
        [SerializeField, Tooltip("The see-through material (Assets/Gameplay/Abilities/Ability Visual Glass.mat) body " +
                 "and stud switch to for the owner's own team once the mine is a Ghost - the body/stud otherwise " +
                 "render with the opaque Solid material, whose alpha channel has no visible effect at all. Left " +
                 "empty just dims the Ghost instead of making it translucent.")]
        private Material ghostMaterial;

        [SerializeField, Range(0f, 1f), Tooltip("Alpha of the body and stud once this mine is a Ghost. The trigger " +
                 "ring is unaffected - it stays at its own Trigger Ring Opacity, so a teammate can still read the " +
                 "exact radius even once the puck itself has faded.")]
        private float ghostAlpha = 0.35f;

        private Mine mine;
        private Color teamColor = Color.white;
        private bool blastShown;
        private MineVisibility lastVisibility = MineVisibility.Visible;
        private bool visibilityApplied;

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
                // Enabled live in UpdateVisibility, not here - see that method's own comment for why "is this my own
                // team" cannot be decided once, at placement time, and left alone.
            }
        }

        private static int LocalTeam() =>
            PhotonNetwork.LocalPlayer != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int team) ? team : -1;

        private static bool IsLocalPlayersTeam(int team) => team >= 0 && LocalTeam() == team;

        private void LateUpdate()
        {
            if (mine == null || visualRoot == null)
                return;

            // Mine.Detonated - MineDetonationState's own flag, set on every client inside RPC_Detonate - not the
            // Visual's own GameObject.activeSelf (review finding: that also flips false under the Lifetime Seconds
            // expiry backstop, which is not a detonation, so watching it could show a blast that never happened).
            if (mine.Detonated)
            {
                if (!blastShown)
                {
                    blastShown = true;
                    Vector3 at = mine.transform.position;
                    BlastMarker.Spawn(blastMarkerPrefab, new Vector3(at.x, visualRoot.position.y, at.z), mine.ExplosionRadius, teamColor);
                }
                return;
            }

            UpdateVisibility();
        }

        /// <summary>A5: switches this mine's look between Visible/Ghost/Hidden, and keeps the trigger ring's own
        /// enabled state in sync with the LOCAL PLAYER'S OWN TEAM, read live every frame rather than decided once at
        /// placement. Review finding: a late joiner replays this mine's placement from the room cache before their
        /// own teamID custom property has arrived (RoomManager only writes it once, on spawn), so a one-time "is this
        /// my own team" decision made inside OnDeployablePlaced permanently reads false for every one of their own
        /// mines - even though MineVisibilityRule.For, evaluated fresh every frame below, already resolves correctly
        /// the moment the team property lands. The ring's own enabled flag needs the same live treatment, since it can
        /// flip true from a stale false without the Visible/Ghost/Hidden answer itself changing at all (both team
        /// values give the identical MineVisibility.Visible answer before Invisible After Seconds elapses).</summary>
        private void UpdateVisibility()
        {
            if (mine.HasExpired)
                return; // NetworkedDeployable's own Lifetime Seconds backstop already hid every renderer here - never re-enable one for the owner's own team.

            if (triggerRing != null)
                triggerRing.enabled = IsLocalPlayersTeam(mine.OwnerTeam);

            MineVisibility visibility = MineVisibilityRule.For(LocalTeam(), mine.OwnerTeam, mine.SecondsSincePlaced, mine.InvisibleAfterSeconds);
            if (visibilityApplied && visibility == lastVisibility)
                return; // Only the body/stud recolour below is worth gating - Visible can become Ghost or Hidden but never the reverse.

            lastVisibility = visibility;
            visibilityApplied = true;

            var block = new MaterialPropertyBlock();
            foreach (Renderer part in bodyParts)
                ApplyVisibility(part, bodyColor, visibility, block);
            foreach (Renderer part in teamParts)
                ApplyVisibility(part, teamColor, visibility, block);
        }

        private void ApplyVisibility(Renderer part, Color baseColor, MineVisibility visibility, MaterialPropertyBlock block)
        {
            if (part == null)
                return;

            part.enabled = visibility != MineVisibility.Hidden;
            if (!part.enabled)
                return; // Hidden: nothing else to set - the enemy sees nothing at all.

            if (visibility == MineVisibility.Ghost)
            {
                if (ghostMaterial != null)
                    part.sharedMaterial = ghostMaterial;
                VisualTint.SetMeshColor(part, block, VisualTint.WithAlpha(baseColor, ghostAlpha));
            }
            else
            {
                VisualTint.SetMeshColor(part, block, baseColor);
            }
        }
    }
}
