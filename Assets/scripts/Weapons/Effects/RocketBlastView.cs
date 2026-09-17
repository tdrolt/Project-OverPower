using Overpower.Abilities;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// What a rocket's blast looks like (ability visuals step 6; Tudor: "the rocket explosion is not on the ground
    /// which looks weird"). The plan's first answer was a floor ring; Tudor's actual answer once that was tried was
    /// the opposite (A6, 2026-09-17 evening) - keep the blast at its REAL height, rockets fly at muzzle height and a
    /// ring on the floor a couple of metres below looked like it belonged to a different explosion than the one the
    /// player just watched happen in the air.
    ///
    /// So this drops a SplashShell - built in step 2 for exactly this - centred on the real burst point, never
    /// snapped to the ground (no GroundSnap, no StandingBody read here at all), sized to this rocket's own
    /// SplashRadius, in the shooter's team colour. One shell per blast; SplashShell itself owns the grow-and-fade and
    /// removes itself.
    ///
    /// Listens to ExplodeOnImpact.Detonated, which fires exactly once per rocket, on every client, for a hit AND an
    /// airburst alike - Detonate() is the single method both OnHit and OnExpired call (see that class's own
    /// comments). Before Detonated existed, an airburst (a rocket reaching its range end, or the cursor rocket
    /// reaching the cursor) showed nothing at all; wiring up to that one event is what makes the airburst half of A6
    /// real, not a second code path here. Visual only - an IProjectileBehaviour that never keeps a shot flying.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ExplodeOnImpact))]
    public sealed class RocketBlastView : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("The see-through shell shown at the real burst point - Assets/Gameplay/Projectiles/Splash Shell.prefab.")]
        private SplashShell splashShellPrefab;

        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset. The shell is the shooter's colour.")]
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

        private void HandleDetonated(Vector3 centre)
        {
            Color color = theme != null ? theme.ShotColorFor(shooterTeam) : Color.white;
            SplashShell.Spawn(splashShellPrefab, centre, explode.SplashRadius, color);
        }
    }
}
