using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>
    /// Who one tick of the Heal Zone heals: the caster and the caster's living teammates inside, never an enemy, a practice dummy or cover.
    /// Every client ticks the zone, but only a player's own client may change their health, so a copy without local authority is skipped
    /// (MineTargeting's rule, turned round to friends). An unknown caster team heals only the caster: FriendlyFire fails open to "everyone is
    /// an enemy", which for a heal means nobody else. Only players have an actor number (a dummy's team is whatever its Inspector says).
    /// </summary>
    public static class HealZoneTargeting
    {
        public static bool Heals(int casterActor, int casterTeam, int actor, int team, bool alive, bool localAuthority, bool structure) =>
            actor > 0 && alive && localAuthority && !structure && FriendlyFire.IsSelfOrTeammate(casterActor, actor, casterTeam, team);

        /// <summary>Fills <paramref name="into"/> (cleared first) with the candidates one tick heals, in their order.</summary>
        public static void SelectHealed(IReadOnlyList<IDamageable> candidates, int casterActor, int casterTeam, List<IDamageable> into)
        {
            into.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                IDamageable candidate = candidates[i];
                if (candidate != null && Heals(casterActor, casterTeam, candidate.ActorNumber, candidate.TeamId, candidate.IsAlive,
                        candidate.HasLocalAuthority, candidate is IStructure))
                    into.Add(candidate);
            }
        }
    }
}
