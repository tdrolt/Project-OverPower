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

        /// <summary>2.7b: the reverse of CapitalOf - whose capital a zone is, or Neutral if it isn't one. Used by the
        /// out-of-play check (MatchStartRules.IsCapitalOutOfPlay) - since the phase-two cut (2026-09-25), an
        /// out-of-play zone's tower, ring and minimap bubble hide entirely rather than showing a "greyed out"
        /// look.</summary>
        public int CapitalTeamOf(int zoneId) => capitalOwnerByZone.TryGetValue(zoneId, out int team) ? team : Neutral;

        /// <summary>2.7b: every capital zone and the team it belongs to - the live reset's starting snapshot
        /// (TerritorySnapshot.Starting) loops this to seed every team's capital at once. A read-only view rather than
        /// the Dictionary itself (step 8 review: handing out the live dictionary let any caller rewrite which capital
        /// belongs to whom) and rather than an IEnumerable (a foreach over the interface boxes the enumerator, and
        /// MatchDirector walks this every FixedUpdate per waiting player and every frame per respawn countdown).
        /// </summary>
        public CapitalsView Capitals => new CapitalsView(capitalOwnerByZone);

        /// <summary>A foreach-able, read-only look at the capitals: GetEnumerator returns Dictionary's own struct
        /// enumerator, so walking it allocates nothing and offers no way to change the map.</summary>
        public readonly struct CapitalsView
        {
            private readonly Dictionary<int, int> byZone;
            internal CapitalsView(Dictionary<int, int> byZone) { this.byZone = byZone; }
            public Dictionary<int, int>.Enumerator GetEnumerator() => byZone.GetEnumerator();
            public int Count => byZone.Count;
        }

        /// <param name="ownerByZone">Current owner per zone; a missing zone or Neutral means nobody.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone) =>
            MayCapture(teamId, zoneId, ownerByZone, null);

        /// <summary>
        /// The same rule, where an owned neighbour only counts as a way in while it is NOT under attack (Tudor,
        /// 2026-09-16). A team whose capital is being attacked can't use the capital to take the T2 next to it, but a
        /// second safe owned neighbour still works. Your own capital stays capturable no matter what, as before.
        /// </summary>
        /// <param name="isUnderAttack">Asked for each owned neighbour; null = nothing is under attack.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone, System.Func<int, bool> isUnderAttack)
        {
            if (teamId < 0 || !adjacency.ContainsKey(zoneId))
                return false;
            if (ownerByZone.TryGetValue(zoneId, out int owner) && owner == teamId)
                return false;
            if (IsCapitalOf(zoneId, teamId))
                return true;
            foreach (int neighbour in AdjacentTo(zoneId))
                if (ownerByZone.TryGetValue(neighbour, out int neighbourOwner) && neighbourOwner == teamId
                    && (isUnderAttack == null || !isUnderAttack(neighbour)))
                    return true;
            return false;
        }

        /// <summary>
        /// 2.7b Decision 8: the cut capital of a host-started match (its third team was never in the match) is never
        /// capturable, not even by its own team - checked BEFORE the own-capital exception above, or a team standing
        /// next to it would reopen it. A separate overload, not an optional parameter, so every existing 3- and
        /// 4-argument call site stays unambiguous and unchanged.
        /// </summary>
        /// <param name="isOutOfPlay">Asked for zoneId only; null behaves exactly like the 4-argument overload.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone,
                                System.Func<int, bool> isUnderAttack, System.Func<int, bool> isOutOfPlay)
        {
            if (isOutOfPlay != null && isOutOfPlay(zoneId))
                return false;
            return MayCapture(teamId, zoneId, ownerByZone, isUnderAttack);
        }
    }
}
