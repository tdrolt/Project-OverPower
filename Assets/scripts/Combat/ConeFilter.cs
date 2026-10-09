using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One candidate for a cone check: an IDamageable plus the world position to test it at.
    /// IDamageable carries no Transform; the collider that found it is the only one who knows where
    /// it stands.
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
    /// Selects targets standing inside a forward cone on the ground plane: the flamethrower's spray,
    /// and any later attachment that channels a status effect into a cone in front of the caster. Two
    /// rules, both pure and testable without a scene:
    ///
    ///   - IsWithinCone: range and half-angle are measured on the flat (XZ) plane, like AimConeState's
    ///     spread, so a target on a ledge or ramp is judged by where it stands on the ground, not
    ///     penalised for a Y difference.
    ///   - SelectCandidates: not a teammate or the caster (FriendlyFire.IsSelfOrTeammate, the same
    ///     rule a shot uses), never an IStructure (cover's fail-open -1/-1 identity would read as an
    ///     enemy with no known team; MineTargeting excludes it for the same reason), and never
    ///     something already in the caller's alreadyHit set: "once per target per cast", because a
    ///     spray sampled every physics step would otherwise re-Refresh the same burn every step,
    ///     extending it well past its own duration.
    ///
    /// Deliberately NO line-of-sight occlusion: that needs a Physics.Raycast, which this class avoids
    /// so it stays testable with fake targets. The caller runs that check on what this returns.
    ///
    /// Read-only: SelectCandidates never mutates alreadyHit. The caller adds only the candidates that
    /// actually receive the status, so a target hidden behind a wall this tick can still be caught the
    /// moment it steps out.
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
