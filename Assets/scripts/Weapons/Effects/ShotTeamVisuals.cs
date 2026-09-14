using UnityEngine;
using UnityEngine.Rendering;
using Overpower.Combat;
using Overpower.UI;

namespace Overpower.Weapons
{
    /// <summary>
    /// Task 11a (playtest polish, designer request): tints a weapon shot's core toward its shooter's team
    /// colour and gives it a short fading trail in that same colour, so a player caught in the open can
    /// tell who is shooting at them without reading a name tag.
    ///
    /// WEAPON SHOTS ONLY. Lasers spawn no projectile at all (Hitscan.Fire resolves instantly - see
    /// WeaponFiring.Spawn) and get their own wind-up + beam treatment in Task 11b instead. Ability
    /// projectiles (Zip Gun Bullet, Stun Gun Bullet) keep their existing look on the designer's
    /// instruction, so this component only ever sits on a WEAPON'S own projectile prefab - never on an
    /// ability's.
    ///
    /// NOTHING HERE IS NETWORKED. ProjectileContext.ShooterTeamId already comes from
    /// Teams.TryGetTeam(info.Sender) inside WeaponFiring.RPC_FireWeapon, so it is identical on every
    /// client before this component ever sees it - see ProjectileContext's own class comment on why a
    /// locally-Instantiated projectile needs no RPC of its own.
    ///
    /// VISUAL ONLY. This class never reads or writes anything ProjectileMotor uses for hit detection,
    /// damage, speed or range - it only ever touches its own TrailRenderer and a MaterialPropertyBlock on
    /// the bullet's renderer.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShotTeamVisuals : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Where the team colours, trail settings and tint/emission strength come " +
                 "from - Assets/Gameplay/Config/UiTheme.asset, shared with the HUD and the aim cone. " +
                 "Presentation only; nothing here is a gameplay value.")]
        private UiTheme theme;

        // Found once in Awake rather than looked up on every shot - GetComponentInChildren is not free,
        // and a weapon can put several of these in the air a second.
        private Renderer bulletRenderer;
        private MaterialPropertyBlock propertyBlock;

        // The trail lives on its OWN child object, not this one, so OnExpired below can detach it and let
        // it fade out on its own timer instead of vanishing the instant ProjectileMotor.Despawn destroys
        // this bullet's GameObject - see the class comment on Task 11a's "trail must not pop" requirement.
        private TrailRenderer trail;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            // Read BEFORE the trail child is created below - TrailRenderer is itself a Renderer subclass,
            // and GetComponentInChildren would just as happily return the wrong one if the trail already
            // existed on the hierarchy at the time of this call.
            bulletRenderer = GetComponentInChildren<Renderer>();
            propertyBlock = new MaterialPropertyBlock();

            GameObject trailObject = new GameObject("Shot Trail");
            trailObject.transform.SetParent(transform, false);
            trail = trailObject.AddComponent<TrailRenderer>();
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false; // Turned on in OnSpawned, once the team colour is actually known.
        }

        /// <summary>IProjectileBehaviour.OnSpawned - ProjectileMotor.Initialize calls this on every
        /// attached behaviour right after the motor itself is configured and before the projectile's first
        /// step, so the trail and the core tint are both set before anyone sees an untinted frame.</summary>
        public void OnSpawned(ProjectileMotor motor, ProjectileContext context)
        {
            if (theme == null)
            {
                Debug.LogWarning($"[ShotTeamVisuals] {name}: no UiTheme assigned - shot stays untinted.");
                return;
            }

            Color teamColor = theme.ShotColorFor(context.ShooterTeamId);
            ConfigureTrail(teamColor);
            TintCore(teamColor);
        }

        private void ConfigureTrail(Color teamColor)
        {
            trail.time = theme.trailTime;
            trail.startWidth = theme.trailStartWidth;
            trail.endWidth = theme.trailEndWidth;
            // sharedMaterial, never .material - one asset serves every team's trail (and every bullet in
            // flight at once); .material would silently instantiate a new one per bullet.
            trail.sharedMaterial = theme.trailMaterial;

            // A pooled/reused GameObject must not carry a previous flight's trail points into a new one.
            trail.Clear();
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(teamColor, 0f), new GradientColorKey(teamColor, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(teamColor.a, 0f), new GradientAlphaKey(0f, 1f) },
            };
            trail.emitting = true;
        }

        private void TintCore(Color teamColor)
        {
            if (bulletRenderer == null)
                return;

            bulletRenderer.GetPropertyBlock(propertyBlock);

            Color baseColor = Color.Lerp(bulletRenderer.sharedMaterial.color, teamColor, theme.bulletTintStrength);
            propertyBlock.SetColor(BaseColorId, baseColor);
            propertyBlock.SetColor(EmissionColorId, new Color(
                teamColor.r * theme.bulletEmission,
                teamColor.g * theme.bulletEmission,
                teamColor.b * theme.bulletEmission,
                1f));

            bulletRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>IProjectileBehaviour.OnHit - this component has no opinion on whether the shot keeps
        /// flying; that is ExplodeOnImpact/BounceOffWalls/Pierce's call. Despawn is the neutral answer - the
        /// same one a projectile with no behaviours at all gives - since "any single KeepFlying wins" over
        /// every behaviour attached (see IProjectileBehaviour's own comment), so this can never cut a
        /// bounce or a pierce short.</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context,
                                            RaycastHit hit, IDamageable victim)
        {
            return ProjectileHitResponse.Despawn;
        }

        /// <summary>IProjectileBehaviour.OnExpired - the bullet is gone, however it went (impact, range or
        /// the lifetime backstop all funnel through ProjectileMotor.Despawn, which calls this on every
        /// behaviour and then destroys the bullet's own GameObject immediately after). Detaching the trail
        /// onto its own lifetime is what stops its still-visible tail from popping out of existence the
        /// instant the bullet that drew it disappears.</summary>
        public void OnExpired(ProjectileMotor motor, ProjectileContext context)
        {
            if (trail == null)
                return;

            GameObject trailObject = trail.gameObject;
            trailObject.transform.SetParent(null, true); // Keep world position - no jump on detach.
            trail.emitting = false;
            Destroy(trailObject, theme != null ? theme.trailTime : 0.25f);
            trail = null; // OnExpired only ever runs once per flight; guards a stray double call.
        }
    }
}
