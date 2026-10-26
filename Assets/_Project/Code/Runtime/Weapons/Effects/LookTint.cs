using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// Paints an ability's own shot (the Stun Gun bolt) or its impact burst in the colour of its row in the look table, through a property
    /// block on each renderer so the shared material file never changes while playing. The solid core (a MeshRenderer, URP Lit with
    /// emission) gets the colour plus Stun Bolt Glow, brightened without leaving its hue; every other renderer is an unlit spark
    /// (Particles/Unlit), whose Ability Spark Glow rides in its colour; on a shot they may wear the caster's team accent instead (the bolt's
    /// crackle and its thin trail say whose; the trail's length and width are Stun Trail Seconds and Width). A projectile is tinted as it
    /// spawns; anything else (a burst) when it starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LookTint : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Where the colour (the look table's row for the ability below) and the glow come from: " +
                 "Assets/_Project/Shared/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The ability whose look-table row colours this object, by the Id on its ability asset (22 is the Stun Gun).")]
        private int abilityId;

        [SerializeField, Tooltip("On for an ability's own shot (the Stun Gun bolt): its sparks, the crackle trail behind it, take the caster's team " +
                 "colour, a thin accent that says whose bolt it is while the core keeps the ability's colour. Off for the burst where it lands: a hit " +
                 "never carries a team.")]
        private bool sparksInCastersTeam;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private bool applied;

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context) => Apply(context.ShooterTeamId);

        private void Start()
        {
            if (!applied)
                Apply(-1);
        }

        /// <summary>False, leaving the authored look, when there is no theme or the table has no row for the ability.</summary>
        public bool Apply(int team)
        {
            if (theme == null)
                return false;
            Color colour = LookColours.ForAbility(theme, abilityId, team, LookFamily.None);
            if (colour.a <= 0f)
                return false;
            float coreGlow = theme.arenaLook != null ? theme.arenaLook.stunBoltGlow : 1f;
            float sparkGlow = theme.arenaLook != null ? theme.arenaLook.abilitySparkGlow : 1f;
            Color sparkColour = sparksInCastersTeam ? LookColours.Accent(team, theme, colour) : colour;

            if (renderers == null)
                renderers = GetComponentsInChildren<Renderer>(true);
            if (block == null)
                block = new MaterialPropertyBlock();
            foreach (Renderer r in renderers)
            {
                r.GetPropertyBlock(block);
                if (r is MeshRenderer)
                {
                    block.SetColor(BaseColorId, colour);
                    Color glow = TowerLookRules.GlowKeepingHue(colour, coreGlow);
                    glow.a = 1f;
                    block.SetColor(EmissionColorId, glow);
                }
                else
                    block.SetColor(BaseColorId, new Color(sparkColour.r * sparkGlow, sparkColour.g * sparkGlow, sparkColour.b * sparkGlow, colour.a));
                r.SetPropertyBlock(block);
                if (r is TrailRenderer trail && theme.arenaLook != null)
                {
                    trail.time = theme.arenaLook.stunTrailSeconds;
                    trail.widthMultiplier = theme.arenaLook.stunTrailWidth;
                }
            }
            applied = true;
            return true;
        }

        /// <summary>No opinion on whether the shot keeps flying (any single KeepFlying wins).</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context, RaycastHit hit, IDamageable victim) =>
            ProjectileHitResponse.Despawn;

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
