using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The two pure decisions behind a forced knockback. The sonic pulse is the first caller; any later
    /// ability that shoves someone and cares what they hit can reuse them. Plain C#, testable with
    /// fake targets, the same shape as FriendlyFire and MineTargeting.
    /// </summary>
    public static class KnockbackResolver
    {
        /// <summary>
        /// Which way to push a victim: straight away from origin, flattened onto the ground plane so a
        /// victim on a ledge or ramp is not shoved up or down. Falls back to fallbackDirection (also
        /// flattened) when the victim is closer than minDistance to origin, too close for "away from
        /// origin" to mean anything (a victim exactly at the caster's feet). Deterministic: every
        /// client with the same origin and victim position gets the same answer, and only the victim's
        /// own position has to be trustworthy (see SonicPulseAbility for why that always holds here).
        /// </summary>
        public static Vector3 ComputePushDirection(Vector3 origin, Vector3 victimPosition,
                                                     Vector3 fallbackDirection, float minDistance = 0.1f)
        {
            Vector3 toVictim = victimPosition - origin;
            toVictim.y = 0f;

            if (toVictim.magnitude >= minDistance)
                return toVictim.normalized;

            Vector3 fallback = fallbackDirection;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }

        /// <summary>
        /// Which way a caster throws THEMSELVES when their own ability pushes them back (the sonic
        /// pulse's self push): straight away from where they aim, flattened onto the ground plane
        /// and normalised. An aim with no ground-plane part (zero, or straight up or down) has no
        /// "backwards" to mean anything, so it returns Vector3.zero and the caller pushes nobody.
        /// </summary>
        public static Vector3 ComputeSelfPushDirection(Vector3 aimDirection)
        {
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude <= 0.0001f)
                return Vector3.zero;

            return -aimDirection.normalized;
        }

        /// <summary>
        /// Whether a cast should throw its caster back: only on the caster's own client, only with a
        /// distance above zero, and only when the aim has a ground-plane direction to go opposite to.
        /// </summary>
        public static bool ShouldSelfPush(bool isCasterClient, Vector3 aimDirection, float distance)
        {
            return isCasterClient && distance > 0f && ComputeSelfPushDirection(aimDirection) != Vector3.zero;
        }

        /// <summary>
        /// True when a blocker a victim collided with should ALSO be stunned: the player-into-player
        /// half of the collision rule. Three gates, all of which must pass:
        ///   - it actually has something to stun (hasStatusReceiver - a plain wall or a cover panel
        ///     never reaches "true" here, since the caller only calls this once it already found an
        ///     IDamageable to ask, and cover has no IStatusReceiver to find);
        ///   - it is not a structure (cover and its kind are not combatants, matching ConeFilter and
        ///     MineTargeting's identical rule);
        ///   - it is not the caster or one of their teammates (FriendlyFire's own rule) - the victim
        ///     is stunned regardless, by the caller, unconditionally; this method only ever decides
        ///     the SECOND stun, on whatever the victim hit.
        /// </summary>
        public static bool ShouldStunBlocker(IDamageable blocker, bool hasStatusReceiver,
                                              int casterActor, int casterTeam)
        {
            if (blocker == null || !hasStatusReceiver)
                return false;

            if (blocker is IStructure)
                return false;

            return !FriendlyFire.IsSelfOrTeammate(casterActor, blocker.ActorNumber, casterTeam, blocker.TeamId);
        }
    }
}
