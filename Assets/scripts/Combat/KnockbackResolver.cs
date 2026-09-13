using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The two pure decisions behind a forced knockback - the sonic pulse (Task 1.10a) is the first
    /// caller, and any later ability that shoves someone and cares what they hit can reuse this
    /// rather than re-deriving it. Plain C#, no scene dependency beyond maths types, so both rules
    /// are testable with fake targets - the same shape as FriendlyFire and MineTargeting.
    /// </summary>
    public static class KnockbackResolver
    {
        /// <summary>
        /// Which way to push a victim: straight away from origin, flattened onto the ground plane so
        /// a victim standing on a ledge or ramp is not shoved up or down. Falls back to
        /// fallbackDirection (also flattened) when the victim is closer than minDistance to origin -
        /// too close for "away from origin" to mean anything - which covers the addendum's own
        /// worked case of a victim standing exactly at the caster's feet. Deterministic: every
        /// client that runs this with the same origin and victim position gets the same answer, and
        /// the only position that has to be trustworthy is the victim's own (see SonicPulseAbility's
        /// class comment for why that is always true here).
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
        /// True when a blocker a victim collided with should ALSO be stunned - the player-into-
        /// player half of the collision rule (Task 1.10 addendum's "Holes" section: the brief's
        /// version, which wins over the plan's "wall or enemy" wording). Three gates, all of which
        /// must pass:
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
