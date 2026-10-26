using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>
    /// Which of a mine's nearby IDamageables it treats as a target, for both halves of Mine.cs
    /// (whether to trigger, and who the blast hits). Plain C#, testable with fake targets and no scene.
    ///
    /// Not self or teammate: the same FriendlyFire.IsSelfOrTeammate a shot uses. A practice dummy's
    /// unmatched team fails that check open, so a dummy is an enemy of every real team.
    ///
    /// HasLocalAuthority: the trigger runs on EVERY client (see Mine.cs), and without this a remote
    /// enemy would look like a valid target on a machine whose ApplyDamage IsMine guard would no-op,
    /// firing a needless RPC. A local dummy always passes.
    ///
    /// Never IStructure: cover's -1/-1 identity fails FriendlyFire open like an unrecognised team,
    /// which let a mine detonate against its own player's cover; IStructure is the explicit marker
    /// (see that interface for why identity alone cannot tell them apart).
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

                if (candidate is IStructure)
                    continue; // Cover and its kind are not combatants - never trips a mine, never caught in its blast.

                if (FriendlyFire.IsSelfOrTeammate(ownerActor, candidate.ActorNumber, ownerTeam, candidate.TeamId))
                    continue;

                result.Add(candidate);
            }

            return result;
        }
    }
}
