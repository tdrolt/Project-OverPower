using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>
    /// Which of a mine's nearby IDamageables it should actually treat as a target - used for both
    /// halves of Mine.cs (deciding whether to trigger at all, and who the blast hits once it does).
    ///
    /// TWO RULES, BOTH ALREADY BUILT ELSEWHERE. Not a teammate or the owner themselves - the exact
    /// same FriendlyFire.IsSelfOrTeammate check a shot uses, so a mine cannot be tripped by its own
    /// placer or their team any more than a bullet can hit them. And HasLocalAuthority - IDamageable's
    /// own "is this the machine that actually owns this target" flag - because a mine's trigger runs
    /// on EVERY client (see Mine.cs class comment): without this a remote enemy player would look
    /// like a valid target on a machine that cannot damage them anyway (ApplyDamage's own IsMine
    /// guard would silently no-op), which would needlessly fire an RPC. A local practice dummy
    /// always passes - DummyTarget.HasLocalAuthority is always true, and its unmatched team id (99)
    /// already fails FriendlyFire's team check open, so a dummy is an enemy of every real team.
    ///
    /// Plain C#, no UnityEngine dependency beyond IDamageable's own types, so this is testable with
    /// fake targets and no scene - the same shape as FriendlyFire and DeployablePruning.
    /// </summary>
    public static class MineTargeting
    {
        public static List<IDamageable> SelectTargets(IReadOnlyList<IDamageable> candidates, int ownerActor, int ownerTeam)
        {
            var result = new List<IDamageable>();

            foreach (IDamageable candidate in candidates)
            {
                if (candidate == null || !candidate.HasLocalAuthority)
                    continue;

                if (FriendlyFire.IsSelfOrTeammate(ownerActor, candidate.ActorNumber, ownerTeam, candidate.TeamId))
                    continue;

                result.Add(candidate);
            }

            return result;
        }
    }
}
