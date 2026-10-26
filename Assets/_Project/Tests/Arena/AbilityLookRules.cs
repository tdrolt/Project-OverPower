using System.Collections.Generic;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>Audits ArenaLook's table against the ability catalogue: one row per ability, no two rows alike. Only the tests ask it,
    /// so it lives with them.</summary>
    internal static class AbilityLookRules
    {
        /// <summary>One plain sentence per problem; empty means the table is sound. The ids in <paramref name="exempt"/> (the debug pings)
        /// need no row. A No Look row (the Scope) is left out of the "two abilities look alike" check, since it draws nothing.</summary>
        public static List<string> Problems(IEnumerable<int> catalogueIds, IReadOnlyList<AbilityLook> table, ISet<int> exempt)
        {
            var problems = new List<string>();
            var catalogue = new HashSet<int>(catalogueIds);
            var rowIds = new HashSet<int>();
            var pairOwner = new Dictionary<(LookShape, LookFamily), int>();

            for (int i = 0; table != null && i < table.Count; i++)
            {
                AbilityLook row = table[i];
                if (!rowIds.Add(row.AbilityId))
                    problems.Add($"Ability {row.AbilityId} has more than one row in the look table; keep a single row.");
                else if (!catalogue.Contains(row.AbilityId))
                    problems.Add($"The look table has a row for ability {row.AbilityId}, which is not in the Ability Catalogue.");

                if (row.Shape == LookShape.NoLook)
                    continue;
                (LookShape, LookFamily) pair = (row.Shape, row.Family);
                if (pairOwner.TryGetValue(pair, out int other))
                    problems.Add($"Abilities {other} and {row.AbilityId} would look alike: both are {row.Shape} in {row.Family}.");
                else
                    pairOwner[pair] = row.AbilityId;
            }

            foreach (int id in catalogue)
                if (!rowIds.Contains(id) && (exempt == null || !exempt.Contains(id)))
                    problems.Add($"Ability {id} has no row in the look table.");
            return problems;
        }
    }
}
