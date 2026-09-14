using UnityEngine;
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
    /// ALLOCATION-FREE PER SHOT (Task 11a review finding). Every client simulates every projectile - a
    /// nine-player SMG burst is a lot of bullets a second - so this must not allocate per shot. The trail
    /// is a CHILD BAKED ONTO EACH WEAPON PROJECTILE PREFAB (named Trail Child Name below, pre-configured
    /// with shared settings, emitting off), found once in Awake rather than created here; its colour
    /// comes from UiTheme.GradientFor, which builds one Gradient per team once and reuses it.
    ///
    /// VISUAL ONLY. This class never reads or writes anything ProjectileMotor uses for hit detection,
    /// damage, speed or range - it only ever touches its own TrailRenderer and a MaterialPropertyBlock on
    /// the bullet's renderer.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShotTeamVisuals : MonoBehaviour, IProjectileBehaviour
    {
        /// <summary>Name of the child GameObject each weapon projectile prefab must carry its
        /// TrailRenderer on - see the class comment on why this is baked onto the prefab rather than
        /// created at runtime.</summary>
        public const string TrailChildName = "Shot Trail";

        [SerializeField, Tooltip("Where the team colours, trail settings and tint/emission strength come " +
                 "from - Assets/Gameplay/Config/UiTheme.asset, shared with the HUD and the aim cone. " +
                 "Presentation only; nothing here is a gameplay value.")]
        private UiTheme theme;

        // Found once in Awake rather than looked up on every shot - GetComponent is not free, and a
        // weapon can put several of these in the air a second.
        private Renderer bulletRenderer;
        private MaterialPropertyBlock propertyBlock;
        private TrailRenderer trail;

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

        /// <summary>IProjectileBehaviour.OnSpawned - ProjectileMotor.Initialize calls this on every
        /// attached behaviour right after the motor itself is configured and before the projectile's first
        /// step, so the trail and the core tint are both set before anyone sees an untinted frame.</summary>
        public void OnSpawned(ProjectileMotor motor, ProjectileContext context)
        {
            if (theme == null)
            {
                if (!warnedMissingTheme)
                {
                    warnedMissingTheme = true;
                    Debug.LogWarning($"[ShotTeamVisuals] {name}: no UiTheme assigned - shots stay untinted.");
                }
                return;
            }

            ConfigureTrail(context.ShooterTeamId);
            TintCore(theme.ShotColorFor(context.ShooterTeamId));
        }

        private void ConfigureTrail(int teamId)
        {
            if (trail == null)
                return;

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

        private void TintCore(Color teamColor)
        {
            if (bulletRenderer == null)
                return;

            bulletRenderer.GetPropertyBlock(propertyBlock);

            Color baseColor = Color.Lerp(bulletRenderer.sharedMaterial.color, teamColor, theme.bulletTintStrength);
            propertyBlock.SetColor(BaseColorId, baseColor);

            // teamColor.a scales the glow too, not just the trail's fade - without this the unknown-team
            // fallback's lower alpha (meant to read as a fainter, washed-out "not a real team" colour)
            // only showed up in its trail; its emissive CORE still came out just as bright as a real
            // team's, and side-by-side captures showed it reading as an extra near-white team instead of
            // a washed-out non-team (Task 11a follow-up review, 616x576 capture).
            float glow = theme.bulletEmission * teamColor.a;
            propertyBlock.SetColor(EmissionColorId, new Color(
                teamColor.r * glow,
                teamColor.g * glow,
                teamColor.b * glow,
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
        /// behaviour and then destroys the bullet's own GameObject immediately after). The trail is BAKED
        /// onto this prefab (not created here), so it must be detached rather than left to die with its
        /// parent: reparenting it to the scene root and destroying it on its own timer is what stops its
        /// still-visible tail from popping out of existence the instant the bullet that drew it
        /// disappears.</summary>
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
