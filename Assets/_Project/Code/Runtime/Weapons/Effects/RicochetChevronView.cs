using Overpower.Combat;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>When the Bounce shot's ricochet chevron shows, and which way it points.</summary>
    public static class RicochetRule
    {
        /// <summary>True when this hit added a bounce; the wall that ends the shot and a player hit add none.</summary>
        public static bool Bounced(int seenBefore, int bouncesNow) => bouncesNow > seenBefore;

        /// <summary>Level, pointing away from the wall along its normal; a floor or ceiling normal has no level direction, so unturned.</summary>
        public static Quaternion Facing(Vector3 wallNormal)
        {
            var level = new Vector3(wallNormal.x, 0f, wallNormal.z);
            return level.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(level.normalized, Vector3.up) : Quaternion.identity;
        }
    }

    /// <summary>
    /// The Bounce shot's ricochet chevron: a short white chevron pointing off the wall at every bounce, pooled by VFXManager and gated like
    /// every shot effect (a bounce in the fog shows nothing). Purely visual: it never changes whether the shot keeps flying. Must sit after
    /// BounceOffWalls on the prefab, so the bounce count it reads already includes this hit.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BounceOffWalls))]
    public sealed class RicochetChevronView : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("The chevron flash shown at each bounce - Assets/Gameplay/FX/Impacts/FX Ricochet Chevron.prefab.")]
        private GameObject chevron;

        private BounceOffWalls bounce;
        private int seen;

        private void Awake() => bounce = GetComponent<BounceOffWalls>();

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context) => seen = 0;

        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context, RaycastHit hit, IDamageable victim)
        {
            int before = seen;
            seen = bounce.Bounces;
            if (RicochetRule.Bounced(before, seen) && chevron != null && VFXManager.Instance != null
                && TeamSight.ShotShownAt(context.ShooterTeamId, hit.point))
                VFXManager.Instance.PlayVFX(chevron, hit.point, RicochetRule.Facing(hit.normal));
            return ProjectileHitResponse.Despawn; // no opinion: BounceOffWalls' KeepFlying wins
        }

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
