using UnityEngine;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Vision;

namespace Overpower.Weapons
{
    /// <summary>
    /// Tints a weapon shot's core toward its shooter's team colour and gives it a short fading trail in that colour, so a player caught in the open
    /// can tell who is shooting at them without reading a name tag.
    /// WEAPON SHOTS ONLY, on a weapon's own projectile prefab: lasers spawn no projectile (Hitscan resolves instantly) and ability projectiles
    /// (Zip Gun Bullet, Stun Gun Bullet) keep their own look.
    /// NOTHING HERE IS NETWORKED: ProjectileContext.ShooterTeamId is already identical on every client. VISUAL ONLY: never reads or writes anything
    /// ProjectileMotor uses for hit detection, damage, speed or range.
    /// ALLOCATION-FREE PER SHOT (every client simulates every projectile, so a nine-player SMG burst is a lot of bullets a second): the trail is a
    /// CHILD BAKED ONTO EACH WEAPON PROJECTILE PREFAB (Trail Child Name, pre-configured with shared settings, emitting off), found once in Awake;
    /// its colour comes from UiTheme.GradientFor, which builds one Gradient per team once and reuses it.
    /// The core is white-hot (Bullet Tint Strength keeps only a hint of the team); the team colour lives in the trail and in the shot light
    /// ShotLightPool may put under it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShotTeamVisuals : MonoBehaviour, IProjectileBehaviour
    {
        /// <summary>Name of the child GameObject each weapon projectile prefab must carry its
        /// TrailRenderer on - see the class comment on why this is baked onto the prefab rather than
        /// created at runtime.</summary>
        public const string TrailChildName = "Shot Trail";

        [SerializeField, Tooltip("Where the team colours, trail settings and tint/emission strength come " +
                 "from - Assets/_Project/Shared/Config/UiTheme.asset, shared with the HUD and the aim cone. " +
                 "Presentation only; nothing here is a gameplay value. Overwrites the baked 'Shot Trail' " +
                 "child's own time/width/material every spawn (see ConfigureTrail) - set those on UiTheme, " +
                 "not on the prefab child.")]
        private UiTheme theme;

        // Found once in Awake rather than looked up on every shot - GetComponent is not free, and a
        // weapon can put several of these in the air a second.
        private Renderer bulletRenderer;
        private MaterialPropertyBlock propertyBlock;
        private TrailRenderer trail;
        private VisibleWhenSeen gate;
        private Color lightColour = Color.white;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Logged once per play session, not once per shot - a prefab missing its theme or its baked
        // trail child fires this on EVERY spawn otherwise, which for a misconfigured SMG is a warning a
        // frame for as long as the trigger is held.
        private static bool warnedMissingTheme;
        private static bool warnedMissingTrail;

        private void Awake()
        {
            // Restricted to this GameObject, not GetComponentInChildren - every current weapon projectile
            // prefab carries its MeshFilter/MeshRenderer on the root, and the baked trail child below is
            // itself a Renderer (TrailRenderer), which GetComponentInChildren could just as easily return
            // first.
            bulletRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();

            Transform trailXf = transform.Find(TrailChildName);
            trail = trailXf != null ? trailXf.GetComponent<TrailRenderer>() : null;
            if (trail == null && !warnedMissingTrail)
            {
                warnedMissingTrail = true;
                Debug.LogError($"[ShotTeamVisuals] {name}: no '{TrailChildName}' child with a TrailRenderer " +
                                "- bake one onto this prefab (Task 11a). Shots will show no trail.");
            }
        }

        /// <summary>The shooter's team colour, for the light under this shot.</summary>
        public Color LightColour => lightColour;

        /// <summary>The sight gate WeaponFiring attaches right after the spawn; null until then, or when there is no sight at all.</summary>
        public VisibleWhenSeen Gate
        {
            get
            {
                if (gate == null)
                    gate = GetComponent<VisibleWhenSeen>();
                return gate;
            }
        }

        /// <summary>Runs before the projectile's first step, so the trail and the core tint are set before anyone sees an untinted frame.</summary>
        public void OnSpawned(ProjectileMotor motor, ProjectileContext context)
        {
            ShotLightPool.Register(this);
            if (theme == null)
            {
                if (!warnedMissingTheme)
                {
                    warnedMissingTheme = true;
                    Debug.LogWarning($"[ShotTeamVisuals] {name}: no UiTheme assigned - shots stay untinted.");
                }
                return;
            }

            lightColour = theme.ShotColorFor(context.ShooterTeamId);
            ConfigureTrail(context.ShooterTeamId);
            TintCore(lightColour);
        }

        private void ConfigureTrail(int teamId)
        {
            if (trail == null)
                return;

            // Overwrites whatever time/width the baked "Shot Trail" child carries: Trail Time/Trail Start Width/Trail End Width on UiTheme are the one
            // real home for these numbers across every weapon projectile prefab; the prefab's own baked values are only a design-time Scene-view preview.
            trail.time = theme.trailTime;
            trail.startWidth = theme.trailStartWidth;
            trail.endWidth = theme.trailEndWidth;
            // sharedMaterial, never .material - one asset serves every team's trail (and every bullet in
            // flight at once); .material would silently instantiate a new one per bullet.
            trail.sharedMaterial = theme.trailMaterial;

            // A pooled/reused GameObject must not carry a previous flight's trail points into a new one.
            trail.Clear();
            // The cached, shared-per-team Gradient - never `new Gradient` here, see the class comment.
            trail.colorGradient = theme.GradientFor(teamId);
            trail.emitting = true;
        }

        /// <summary>The core's glow: Flash White pulled toward the team colour by the tint strength, so a low strength reads white-hot. The team
        /// colour's alpha scales it, so the unknown-team fallback's fainter colour dims the core as well as the trail.</summary>
        public static Color EmissionFor(Color teamColor, Color white, float tintStrength, float emission)
        {
            Color hue = Color.Lerp(white, teamColor, tintStrength);
            float glow = emission * teamColor.a;
            return new Color(hue.r * glow, hue.g * glow, hue.b * glow, 1f);
        }

        private void TintCore(Color teamColor)
        {
            if (bulletRenderer == null)
                return;

            bulletRenderer.GetPropertyBlock(propertyBlock);

            Color baseColor = Color.Lerp(theme.flashWhiteColor, teamColor, theme.bulletTintStrength);
            propertyBlock.SetColor(BaseColorId, baseColor);

            propertyBlock.SetColor(EmissionColorId, EmissionFor(teamColor, theme.flashWhiteColor, theme.bulletTintStrength, theme.bulletEmission));

            bulletRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>This component has no opinion on whether the shot keeps flying; that is ExplodeOnImpact/BounceOffWalls/Pierce's call. Despawn is the
        /// neutral answer, and since any single KeepFlying wins (IProjectileBehaviour), this can never cut a bounce or a pierce short.</summary>
        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context,
                                            RaycastHit hit, IDamageable victim)
        {
            return ProjectileHitResponse.Despawn;
        }

        /// <summary>The bullet is gone, however it went (impact, range or the lifetime backstop all funnel through ProjectileMotor.Despawn, which
        /// destroys the bullet's GameObject right after this). The trail is BAKED onto this prefab, so it must be detached rather than left to die with
        /// its parent: reparented to the scene root and destroyed on its own timer, its still-visible tail does not pop out of existence the instant
        /// the bullet disappears.</summary>
        public void OnExpired(ProjectileMotor motor, ProjectileContext context)
        {
            ShotLightPool.Unregister(this);
            if (trail == null)
                return;

            GameObject trailObject = trail.gameObject;
            trailObject.transform.SetParent(null, true); // Keep world position - no jump on detach.
            trail.emitting = false;
            Destroy(trailObject, theme != null ? theme.trailTime : 0.25f);
            trail = null; // OnExpired only ever runs once per flight; guards a stray double call.
        }

        private void OnDestroy() => ShotLightPool.Unregister(this);
    }
}
