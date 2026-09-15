using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// Which zone a team may start capturing. Pure C# so the rule is tested without a scene, and so
    /// the capture zones, the shop and the phase rules all ask the same question.
    ///
    /// The GDD rule (p.19): you may only capture a territory next to one you control. Two
    /// exceptions keep a match from dead-locking: you can never "capture" what you already own, and
    /// your own capital is always capturable, so a team that loses everything can still fight back.
    /// </summary>
    public sealed class TerritoryMap
    {
        public const int Neutral = -1;

        private readonly Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> capitalOwnerByZone = new Dictionary<int, int>();
        private static readonly IReadOnlyList<int> None = new List<int>();

        /// <param name="zones">Each zone id with the zones next to it. A link listed on one side only
        /// counts for both: a designer who adds 9 -> 3 should not also have to remember 3 -> 9.</param>
        /// <param name="capitals">Capital zone id and the team whose capital it is.</param>
        public TerritoryMap(IEnumerable<(int zoneId, IEnumerable<int> adjacent)> zones,
                            IEnumerable<(int zoneId, int teamId)> capitals)
        {
            foreach (var (zoneId, adjacent) in zones)
            {
                List<int> list = ListFor(zoneId);
                if (adjacent == null) continue;
                foreach (int other in adjacent)
                {
                    if (other == zoneId) continue;
                    if (!list.Contains(other)) list.Add(other);
                    List<int> back = ListFor(other);
                    if (!back.Contains(zoneId)) back.Add(zoneId);
                }
            }
            foreach (List<int> list in adjacency.Values) list.Sort();

            foreach (var (zoneId, teamId) in capitals)
                capitalOwnerByZone[zoneId] = teamId;
        }

        private List<int> ListFor(int zoneId)
        {
            if (!adjacency.TryGetValue(zoneId, out List<int> list))
                adjacency[zoneId] = list = new List<int>();
            return list;
        }

        public IReadOnlyList<int> AdjacentTo(int zoneId) =>
            adjacency.TryGetValue(zoneId, out List<int> list) ? list : None;

        public bool IsCapitalOf(int zoneId, int teamId) =>
            capitalOwnerByZone.TryGetValue(zoneId, out int owner) && owner == teamId;

        /// <summary>The team's starting capital zone, or Neutral.</summary>
        public int CapitalOf(int teamId)
        {
            foreach (KeyValuePair<int, int> pair in capitalOwnerByZone)
                if (pair.Value == teamId) return pair.Key;
            return Neutral;
        }

        /// <param name="ownerByZone">Current owner per zone; a missing zone or Neutral means nobody.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone)
        {
            if (teamId < 0 || !adjacency.ContainsKey(zoneId))
                return false;
            if (ownerByZone.TryGetValue(zoneId, out int owner) && owner == teamId)
                return false;
            if (IsCapitalOf(zoneId, teamId))
                return true;
            foreach (int neighbour in AdjacentTo(zoneId))
                if (ownerByZone.TryGetValue(neighbour, out int neighbourOwner) && neighbourOwner == teamId)
                    return true;
            return false;
        }
    }
}
