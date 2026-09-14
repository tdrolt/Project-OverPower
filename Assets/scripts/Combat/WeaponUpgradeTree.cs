using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>How one weapon node looks from the player's current weapon.</summary>
    public enum UpgradeNodeState
    {
        /// <summary>The weapon the player is holding.</summary>
        Equipped,
        /// <summary>A direct upgrade from the equipped weapon - clickable now.</summary>
        Selectable,
        /// <summary>A weapon the player already came through on the way to the equipped one.</summary>
        Owned,
        /// <summary>Not reachable from here without resetting.</summary>
        Locked,
    }

    /// <summary>
    /// The rules of the weapon upgrade tree, kept apart from any UI so the loadout screen now and the shop later ask
    /// the same questions. Built from weapon ids and their parent ids (WeaponDefinition.Parent), so the tree grows
    /// automatically when a designer adds a weapon asset with a parent. Pure C# so it is unit tested.
    /// </summary>
    public sealed class WeaponUpgradeTree
    {
        private readonly Dictionary<int, int> parentOf = new Dictionary<int, int>();
        private readonly Dictionary<int, List<int>> childrenOf = new Dictionary<int, List<int>>();
        private readonly HashSet<int> inCycle = new HashSet<int>();
        private readonly List<string> problems = new List<string>();
        private static readonly IReadOnlyList<int> None = new List<int>();

        /// <summary>The starting weapon everyone begins with; -1 if the data has no root.</summary>
        public int RootId { get; } = -1;

        /// <summary>Data mistakes a designer should fix (cycles, several roots, missing parents). Empty when the tree is sound.</summary>
        public IReadOnlyList<string> Problems => problems;

        public WeaponUpgradeTree(IEnumerable<(int id, int parentId)> nodes)
        {
            // Last one wins on a repeated id, same as a plain Dictionary write would - but a
            // second WeaponDefinition claiming an id already in the catalogue is a data mistake
            // (mirrors WeaponCatalogue.Validate's own duplicate-id check), so it goes in Problems
            // rather than being silently swallowed.
            var seenIds = new HashSet<int>();
            foreach (var (id, parentId) in nodes)
            {
                if (!seenIds.Add(id))
                    problems.Add($"Weapon {id} appears more than once in the upgrade tree data - the later entry silently wins. Give each weapon a unique Id.");
                parentOf[id] = parentId;
            }

            var roots = new List<int>();
            foreach (var pair in parentOf)
            {
                if (pair.Value < 0) { roots.Add(pair.Key); continue; }
                if (!parentOf.ContainsKey(pair.Value))
                {
                    problems.Add($"Weapon {pair.Key} has parent {pair.Value}, which is not in the catalogue.");
                    continue;
                }
                if (!childrenOf.TryGetValue(pair.Value, out var list))
                    childrenOf[pair.Value] = list = new List<int>();
                list.Add(pair.Key);
            }
            foreach (var list in childrenOf.Values) list.Sort();

            roots.Sort();
            if (roots.Count == 0) problems.Add("No weapon without a parent - there is no starting weapon.");
            if (roots.Count > 1) problems.Add($"{roots.Count} weapons have no parent; using the lowest id ({roots[0]}) as the start.");
            if (roots.Count > 0) RootId = roots[0];

            // A parent chain longer than the number of weapons can only be a loop (A -> B -> A),
            // or a chain that runs INTO one further up (e.g. 4 -> 2 -> 3 -> 2): either way the walk
            // never reaches a root (parentId -1) within one pass over every weapon, so it is
            // flagged and locked rather than walked forever.
            foreach (int id in parentOf.Keys)
            {
                int steps = 0, current = id;
                while (current >= 0 && parentOf.TryGetValue(current, out int parent) && steps <= parentOf.Count)
                {
                    current = parent;
                    steps++;
                }
                if (steps > parentOf.Count)
                {
                    inCycle.Add(id);
                    problems.Add($"Weapon {id} is part of a parent loop - its Parent chain never reaches the starting weapon.");
                }
            }
        }

        public IReadOnlyList<int> ChildrenOf(int id) =>
            childrenOf.TryGetValue(id, out var list) ? list : None;

        public bool IsLeaf(int id) => parentOf.ContainsKey(id) && ChildrenOf(id).Count == 0;

        /// <summary>True only for a direct child of the given weapon - upgrades go one step at a time.</summary>
        public bool CanUpgrade(int fromId, int toId) =>
            !inCycle.Contains(toId) && parentOf.TryGetValue(toId, out int parent) && parent == fromId && parent >= 0;

        public UpgradeNodeState StateOf(int nodeId, int equippedId)
        {
            if (!parentOf.ContainsKey(nodeId) || inCycle.Contains(nodeId) || inCycle.Contains(equippedId))
                return UpgradeNodeState.Locked;
            if (nodeId == equippedId) return UpgradeNodeState.Equipped;
            if (CanUpgrade(equippedId, nodeId)) return UpgradeNodeState.Selectable;
            if (IsAncestorOf(nodeId, equippedId)) return UpgradeNodeState.Owned;
            return UpgradeNodeState.Locked;
        }

        private bool IsAncestorOf(int ancestorId, int id)
        {
            int current = id, steps = 0;
            while (parentOf.TryGetValue(current, out int parent) && parent >= 0 && steps++ <= parentOf.Count)
            {
                if (parent == ancestorId) return true;
                current = parent;
            }
            return false;
        }
    }
}
