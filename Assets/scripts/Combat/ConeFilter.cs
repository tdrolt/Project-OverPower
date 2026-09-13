using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One candidate for a cone check: an IDamageable plus the world position to test it at. A plain
    /// pair rather than reusing IDamageable alone, because IDamageable carries no Transform - the
    /// caller (the collider that found it) is the only one who knows where it actually stands.
    /// </summary>
    public readonly struct ConeCandidate
    {
        public readonly IDamageable Target;
        public readonly Vector3 Position;

        public ConeCandidate(IDamageable target, Vector3 position)
        {
            Target = target;
            Position = position;
        }
    }

    /// <summary>
    /// Selects targets standing inside a forward cone on the ground plane - the flamethrower's spray
    /// (Task 1.9) today, and any later equipment that channels a status effect into a cone in front of
    /// the caster. Two rules, both pure and both testable without a scene:
    ///
    ///   - IsWithinCone: range and half-angle are measured on the flat (XZ) plane, exactly like
    ///     AimConeState's own spread - a target standing above or below the caster (a ledge, a ramp)
    ///     is judged by where it stands on the ground, not penalised for a Y difference that has
    ///     nothing to do with "is this player in front of me".
    ///   - SelectCandidates: not a teammate or the caster themself (FriendlyFire.IsSelfOrTeammate, the
    ///     same rule a shot uses), never an IStructure (cover's own fails-open -1/-1 identity would
    ///     otherwise read as "an enemy with no known team" - MineTargeting excludes it for the
    ///     identical reason), and never something already in the caller's own alreadyHit set - the
    ///     "once per target per cast" rule the Task 1.9 addendum calls out by name: without it, a
    ///     spray sampled every physics step would re-Refresh the same burn every ~0.02s for the whole
    ///     spray window, extending it well past its own duration (up to 30 damage instead of 25 in
    ///     the addendum's own worked example).
    ///
    /// Deliberately does NOT do line-of-sight occlusion (a wall between the caster and a candidate
    /// standing in the cone): that needs a Physics.Raycast, which this class avoids on purpose so it
    /// stays testable with fake targets and no scene, the same reasoning as FriendlyFire and
    /// MineTargeting. The caller runs that check itself on whatever this method returns.
    ///
    /// Read-only. SelectCandidates never mutates alreadyHit - the caller decides which of the
    /// candidates it returns actually receive the status (an occluded one might not) and adds only
    /// those, so a target hidden behind a wall this tick can still be caught the moment it steps out,
    /// rather than being silently written off for the rest of the cast.
    /// </summary>
    public static class ConeFilter
    {
        /// <summary>
        /// True when targetPosition lies within range of origin and within fullAngleDegrees/2 either
        /// side of forward, all measured on the XZ plane. A forward vector with no flat component
        /// (looking straight up or down, which never happens for this game's top-down aim but is
        /// guarded anyway) cannot define a cone and always returns false.
        /// </summary>
        public static bool IsWithinCone(Vector3 origin, Vector3 forward, Vector3 targetPosition,
                                         float range, float fullAngleDegrees)
        {
            Vector3 toTarget = targetPosition - origin;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance > range)
                return false;

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude <= 0.0001f)
                return false;

            if (distance <= 0.0001f)
                return true; // Standing exactly at the apex - inside every cone, whatever its angle.

            float angle = Vector3.Angle(flatForward, toTarget);
            return angle <= fullAngleDegrees * 0.5f;
        }

        /// <summary>
        /// Every candidate inside the cone, an enemy of casterTeam, not an IStructure and not already
        /// in alreadyHit - see the class comment for why occlusion is not checked here.
        /// </summary>
        public static List<ConeCandidate> SelectCandidates(IReadOnlyList<ConeCandidate> candidates,
            Vector3 origin, Vector3 forward, float range, float fullAngleDegrees,
            int casterActor, int casterTeam, ICollection<IDamageable> alreadyHit)
        {
            var result = new List<ConeCandidate>();

            foreach (ConeCandidate candidate in candidates)
            {
                if (candidate.Target == null || alreadyHit.Contains(candidate.Target))
                    continue;

                if (candidate.Target is IStructure)
                    continue; // Cover and its kind are not combatants - see MineTargeting's identical rule.

                if (FriendlyFire.IsSelfOrTeammate(casterActor, candidate.Target.ActorNumber,
                                                  casterTeam, candidate.Target.TeamId))
                    continue;

                if (!IsWithinCone(origin, forward, candidate.Position, range, fullAngleDegrees))
                    continue;

                result.Add(candidate);
            }

            return result;
        }
    }
}
