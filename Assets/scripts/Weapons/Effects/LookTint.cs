using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// Paints an ability's own shot (the Stun Gun bolt) or its impact burst in the colour of its row in the look table, through a property
    /// block on each renderer so the shared material file never changes while playing. The solid core (a MeshRenderer, URP Lit with
    /// emission) gets the colour plus Ability Shot Glow; every other renderer is an unlit spark (Particles/Unlit), whose Ability Spark
    /// Glow rides in its colour.
    /// A projectile is tinted as it spawns; anything else (a burst) when it starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LookTint : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Where the colour (the look table's row for the ability below) and the glow come from: " +
                 "Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The ability whose look-table row colours this object, by the Id on its ability asset (22 is the Stun Gun).")]
        private int abilityId;

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
            float coreGlow = theme.arenaLook != null ? theme.arenaLook.abilityShotGlow : 1f;
            float sparkGlow = theme.arenaLook != null ? theme.arenaLook.abilitySparkGlow : 1f;

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
                    block.SetColor(EmissionColorId, new Color(colour.r * coreGlow, colour.g * coreGlow, colour.b * coreGlow, 1f));
                }
                else
                    block.SetColor(BaseColorId, new Color(colour.r * sparkGlow, colour.g * sparkGlow, colour.b * sparkGlow, colour.a));
                r.SetPropertyBlock(block);
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
