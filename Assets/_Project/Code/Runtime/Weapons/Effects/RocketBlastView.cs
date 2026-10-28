using Overpower.Abilities;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// What a rocket's blast looks like: a SplashShell (a flat shockwave ring) centred on the REAL burst point, never snapped to the ground (no GroundSnap, no StandingBody
    /// read here at all) - rockets fly at muzzle height, and a ring on the floor a couple of metres below looked like it belonged to a different
    /// explosion than the one the player just watched in the air. Sized to this rocket's own SplashRadius, in the Flash White of every impact (an impact
    /// never carries a team). One
    /// shell per blast; SplashShell itself owns the grow-and-fade and removes itself.
    /// Listens to ExplodeOnImpact.Detonated, which fires exactly once per rocket, on every client, for a hit AND an airburst alike (Detonate() is
    /// the single method both OnHit and OnExpired call), so there is no second code path here. Visual only - never keeps a shot flying.
    /// Detonated always carries the rocket's full Splash Radius (see its comment); SplashShell.Spawn's own radius &lt;= 0 guard stays as a general
    /// safety net (a designer could author a zero-radius rocket by mistake), not as a gate on "did this blast hit anything".
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ExplodeOnImpact))]
    public sealed class RocketBlastView : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("The flat shockwave ring shown at the real burst point - Assets/_Project/Design/Weapons/Projectiles/Splash Shell.prefab.")]
        private SplashShell splashShellPrefab;

        [SerializeField, Tooltip("Assets/_Project/Design/Settings/UiTheme.asset. The ring is its Flash White, as thick as its Arena Look's Blast Ring Width.")]
        private UiTheme theme;

        private ExplodeOnImpact explode;
        private int shooterTeam = -1;

        private void Awake()
        {
            explode = GetComponent<ExplodeOnImpact>();
            explode.Detonated += HandleDetonated;
        }

        private void OnDestroy()
        {
            if (explode != null)
                explode.Detonated -= HandleDetonated;
        }

        public void OnSpawned(ProjectileMotor projectileMotor, ProjectileContext context) => shooterTeam = context.ShooterTeamId;

        public ProjectileHitResponse OnHit(ProjectileMotor projectileMotor, ProjectileContext context,
                                            RaycastHit hit, IDamageable victim) => ProjectileHitResponse.Despawn;

        public void OnExpired(ProjectileMotor projectileMotor, ProjectileContext context) { }

        private void HandleDetonated(Vector3 centre, float radius)
        {
            // radius is always the rocket's own full Splash Radius (ExplodeOnImpact.Detonated's comment has the history), so nothing is gated here beyond
            // SplashShell.Spawn's own radius <= 0 check. A blast from the fog is shown only if its centre is seen or it reaches me (own team's always). (D2)
            if (!TeamSight.BlastShownAt(shooterTeam, centre, radius))
                return;
            Color color = theme != null ? theme.flashWhiteColor : Color.white;
            float width = theme != null && theme.arenaLook != null ? theme.arenaLook.blastRingWidth : 0f;
            SplashShell.Spawn(splashShellPrefab, centre, radius, color, width);
        }
    }
}
