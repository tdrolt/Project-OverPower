using Overpower.Abilities;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// Plays the flash and shock ring (StunImpactView) where the Stun Gun bolt strikes something a stun can land on. Every client simulates
    /// the bolt and reaches this hit itself, so every client that sees the spot shows the mark with no message sent; the stun itself still
    /// lands only on the victim's own client (ApplyStatusOnHit). Never decides whether the bolt keeps flying.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StunImpactOnHit : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Where the mark's colour (the STUNNED label's electric blue), sizes and timings come from: " +
                 "Assets/_Project/Shared/Config/UiTheme.asset and its Arena Look (the Stun Flash, Stun Ring and Stun Arc Rate fields).")]
        private UiTheme theme;

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context) { }

        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context, RaycastHit hit, IDamageable victim)
        {
            bool stunnable = victim != null && hit.collider != null && hit.collider.GetComponentInParent<IStatusReceiver>() != null;
            if (StunImpactLook.ShowsMark(stunnable, TeamSight.ShotShownAt(context.ShooterTeamId, hit.point)))
                StunImpactView.Play(hit.point, theme);
            return ProjectileHitResponse.Despawn;
        }

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
