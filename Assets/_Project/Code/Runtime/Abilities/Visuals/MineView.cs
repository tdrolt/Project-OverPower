using Overpower.Arena;
using Overpower.Combat;
using Overpower.Net;
using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A mine: a dark hex puck on the floor with a light on its cap blinking in the Mines row of the look table (the owner's team
    /// colour), counted from Mine.SecondsSincePlaced so every client blinks together; the colour reads on the middle, never a rim
    /// (A8). The owner's team also sees a ring at the real Trigger Radius. After Mine.InvisibleAfterSeconds MineVisibilityRule hides
    /// it from enemies and shows the owner's team a faded Ghost (A5) by Renderer.enabled, never the Visual's GameObject, which Mine
    /// itself hides when it goes off (Mine.Detonated); every client then flashes a BlastMarker at the real Explosion Radius, in the impact white.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MineView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours and the Arena Look - Assets/_Project/Shared/Config/UiTheme.asset.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this mine: the Mines ability's id.")]
        private int abilityId = 19;

        [SerializeField, Tooltip("The dark parts: the puck.")]
        private Renderer[] bodyParts;

        [SerializeField, Tooltip("Colour of the dark parts. Dark so a mine never reads as a portal.")]
        private Color bodyColor = new Color(0.1f, 0.1f, 0.12f, 1f);

        [SerializeField, Tooltip("The parts drawn in the owner's team colour: the blinking light on the cap. They hide from " +
                 "enemies with the puck.")]
        private Renderer[] teamParts;

        [SerializeField, Tooltip("Flat ring at the mine's Trigger Radius, shown only to the owner's own team.")]
        private LineRenderer triggerRing;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the trigger ring.")]
        private float triggerRingOpacity = 0.5f;

        [SerializeField, Tooltip("Points on the trigger ring.")]
        private int triggerRingSegments = 48;

        [SerializeField, Tooltip("The flash on the floor when the mine goes off - Assets/_Project/Features/Weapons/Projectiles/Blast Marker.prefab.")]
        private BlastMarker blastMarkerPrefab;

        [SerializeField, Tooltip("The mine's Visual: the container Snap Visual To Ground puts on the floor, and the one " +
                 "Mine hides when it detonates.")]
        private Transform visualRoot;

        [Header("Ghost (A5 - the owner's own team, once otherwise invisible)")]
        [SerializeField, Tooltip("The see-through material (Assets/_Project/Features/Abilities/Data/Ability Visual Glass.mat) the puck " +
                 "switches to for the owner's own team once the mine is a Ghost; the puck's own material is opaque, so its " +
                 "alpha alone shows nothing. Left empty just dims the Ghost instead of making it translucent.")]
        private Material ghostMaterial;

        [SerializeField, Range(0f, 1f), Tooltip("Alpha of the puck once this mine is a Ghost. The trigger " +
                 "ring is unaffected - it stays at its own Trigger Ring Opacity, so a teammate can still read the " +
                 "exact radius even once the puck itself has faded.")]
        private float ghostAlpha = 0.35f;

        private Mine mine;
        private Color teamColor = Color.white;
        private bool blastShown;
        private MineVisibility lastVisibility = MineVisibility.Visible;
        private bool visibilityApplied;
        private float paintedLight = -1f;
        private MaterialPropertyBlock block;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            mine = deployable as Mine;
            if (mine == null)
                return;

            teamColor = theme != null ? LookColours.ForAbility(theme, abilityId, deployable.OwnerTeam, LookFamily.Team) : Color.white;
            block ??= new MaterialPropertyBlock();
            foreach (Renderer part in bodyParts)
                VisualTint.SetMeshColor(part, block, bodyColor);
            foreach (Renderer part in teamParts)
                VisualTint.SetMeshColor(part, block, teamColor);

            if (triggerRing != null)
            {
                VisualTint.FillFlatCircle(triggerRing, mine.TriggerRadius, triggerRingSegments);
                VisualTint.SetLineColor(triggerRing, VisualTint.WithAlpha(teamColor, triggerRingOpacity));
                // Enabled live in UpdateVisibility, not here: "is this my own team" cannot be decided once at placement.
            }
        }

        private static int LocalTeam() =>
            PhotonNetwork.LocalPlayer != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int team) ? team : -1;

        private static bool IsLocalPlayersTeam(int team) => team >= 0 && LocalTeam() == team;

        private void LateUpdate()
        {
            if (mine == null || visualRoot == null)
                return;

            // Mine.Detonated (set on every client inside RPC_Detonate), not the Visual's GameObject.activeSelf: that also
            // flips false under the Lifetime Seconds expiry backstop, so watching it could show a blast that never happened.
            if (mine.Detonated)
            {
                if (!blastShown)
                {
                    blastShown = true;
                    Vector3 at = mine.transform.position;
                    BlastMarker.Spawn(blastMarkerPrefab, new Vector3(at.x, visualRoot.position.y, at.z), mine.ExplosionRadius,
                        theme != null ? theme.flashWhiteColor : Color.white);
                }
                return;
            }

            UpdateVisibility();
            PaintCap(mine.SecondsSincePlaced);
        }

        /// <summary>Colours the cap's light for this moment of its blink: the team colour and a glow.</summary>
        public void PaintCap(float secondsSincePlaced)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            float light = look != null ? MinePuckLook.CapLight(secondsSincePlaced, look.mineBlinkPeriod, look.mineBlinkOnSeconds, look.mineCapDim) : 1f;
            if (light == paintedLight)
                return;
            paintedLight = light;
            float glow = look != null ? look.mineCapGlow : 0f;
            Color lit = MinePuckLook.CapColour(teamColor, light);
            block ??= new MaterialPropertyBlock();
            foreach (Renderer part in teamParts)
            {
                if (part == null)
                    continue;
                part.GetPropertyBlock(block);
                block.SetColor(EmissionId, TowerLookRules.GlowKeepingHue(lit, glow));
                part.SetPropertyBlock(block);
                VisualTint.SetMeshColor(part, block, lit);
            }
        }

        /// <summary>Switches this mine's look between Visible/Ghost/Hidden and keeps the trigger ring's enabled state in
        /// sync with the LOCAL PLAYER'S OWN TEAM, read live every frame rather than decided once at placement: a late
        /// joiner replays this mine's placement from the room cache before their own teamID property has arrived (written
        /// once, on spawn), so a one-time decision in OnDeployablePlaced would read false for every one of their own
        /// mines forever. The ring needs the live treatment too, since it can flip true without the Visible/Ghost/Hidden
        /// answer changing (both teams get Visible before Invisible After Seconds elapses).</summary>
        private void UpdateVisibility()
        {
            if (mine.HasExpired)
                return; // The Lifetime Seconds backstop already hid every renderer: never re-enable one for the owner's team.

            if (triggerRing != null)
                triggerRing.enabled = IsLocalPlayersTeam(mine.OwnerTeam);

            MineVisibility visibility = MineVisibilityRule.For(LocalTeam(), mine.OwnerTeam, mine.SecondsSincePlaced, mine.InvisibleAfterSeconds);
            if (visibilityApplied && visibility == lastVisibility)
                return; // Only the recolour below is worth gating - Visible can become Ghost or Hidden but never the reverse.
            ShowAs(visibility);
        }

        /// <summary>Puts every part into one of the mine's looks: Visible, a faded Ghost puck, or Hidden.</summary>
        public void ShowAs(MineVisibility visibility)
        {
            lastVisibility = visibility;
            visibilityApplied = true;
            paintedLight = -1f; // the recolour below resets the cap to the plain team colour: paint its light again

            block ??= new MaterialPropertyBlock();
            foreach (Renderer part in bodyParts)
                ApplyVisibility(part, bodyColor, visibility, fades: true);
            foreach (Renderer part in teamParts)
                ApplyVisibility(part, teamColor, visibility, fades: false);
        }

        /// <summary>A Ghost fades only the puck: the cap stays on its own material, the one that can glow, so the owner's team
        /// still sees the light blink (the glass has no glow).</summary>
        private void ApplyVisibility(Renderer part, Color baseColor, MineVisibility visibility, bool fades)
        {
            if (part == null)
                return;

            part.enabled = visibility != MineVisibility.Hidden;
            if (!part.enabled)
                return; // Hidden: nothing else to set - the enemy sees nothing at all.

            if (visibility == MineVisibility.Ghost && fades)
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
