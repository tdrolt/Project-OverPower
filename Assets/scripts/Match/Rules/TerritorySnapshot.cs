using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// The whole territory state of a match as plain arrays indexed by zone id - exactly what is
    /// stored in the room's Custom Properties. Immutable: every change returns a new snapshot, so the
    /// master can compare "before" and "after" and every client can tell what changed.
    ///
    /// Why Room Properties instead of the old buffered RPC: a late joiner reads one value instead of
    /// replaying every capture of the match, and hold timers are stamped with the server clock, so a
    /// new master after a disconnect reads the same timers the old one wrote.
    /// </summary>
    public sealed class TerritorySnapshot
    {
        public const string OwnersKey = "tOwn";
        public const string HeldSinceKey = "tSince";
        public const string LastOwnerKey = "tLast";
        public const string LastHeldKey = "tLastMs";
        public const string BountyKey = "tBounty";

        private readonly int[] owners;
        private readonly int[] heldSinceMs;
        private readonly int[] lastOwner;
        private readonly int[] lastHeldMs;
        private readonly int[] bountyPaid;

        public int ZoneCount => owners.Length;

        public TerritorySnapshot(int zoneCount)
        {
            owners = Filled(zoneCount, TerritoryMap.Neutral);
            heldSinceMs = new int[zoneCount];
            lastOwner = Filled(zoneCount, TerritoryMap.Neutral);
            lastHeldMs = new int[zoneCount];
            bountyPaid = new int[zoneCount];
        }

        private TerritorySnapshot(int[] owners, int[] heldSinceMs, int[] lastOwner, int[] lastHeldMs, int[] bountyPaid)
        {
            this.owners = owners;
            this.heldSinceMs = heldSinceMs;
            this.lastOwner = lastOwner;
            this.lastHeldMs = lastHeldMs;
            this.bountyPaid = bountyPaid;
        }

        public int OwnerOf(int zone) => InRange(zone) ? owners[zone] : TerritoryMap.Neutral;
        public int HeldSinceMs(int zone) => InRange(zone) ? heldSinceMs[zone] : 0;
        /// <summary>Who owned the zone before it last went neutral.</summary>
        public int LastOwnerOf(int zone) => InRange(zone) ? lastOwner[zone] : TerritoryMap.Neutral;
        /// <summary>How long that previous owner held it without losing it.</summary>
        public int LastHeldMs(int zone) => InRange(zone) ? lastHeldMs[zone] : 0;
        /// <summary>Gold each player of the current owner was paid for the capture that made them owner.</summary>
        public int BountyPaidOnLastCapture(int zone) => InRange(zone) ? bountyPaid[zone] : 0;

        public TerritorySnapshot WithCapture(int zone, int team, int nowMs, int bountyPaid)
        {
            TerritorySnapshot next = Copy();
            if (!InRange(zone)) return next;
            next.owners[zone] = team;
            next.heldSinceMs[zone] = nowMs;
            next.bountyPaid[zone] = bountyPaid;
            // The previous hold has been settled by this capture; forget it so it can't pay twice.
            next.lastOwner[zone] = TerritoryMap.Neutral;
            next.lastHeldMs[zone] = 0;
            return next;
        }

        public TerritorySnapshot WithNeutral(int zone, int nowMs)
        {
            TerritorySnapshot next = Copy();
            if (!InRange(zone) || owners[zone] == TerritoryMap.Neutral) return next;
            next.lastOwner[zone] = owners[zone];
            next.lastHeldMs[zone] = unchecked(nowMs - heldSinceMs[zone]);
            next.owners[zone] = TerritoryMap.Neutral;
            next.heldSinceMs[zone] = nowMs;
            next.bountyPaid[zone] = 0;
            return next;
        }

        /// <summary>Same effect as WithNeutral, but WIPES the zone's bounty-eligible history (lastOwner/
        /// lastHeldMs) instead of recording it (Task 2.7 review). The GDD's bounty is for taking a zone
        /// FROM the team that held it (p.20); the Tier-3 reset at the three-to-two team transition
        /// takes a zone from nobody, so the next team to capture it must not be paid for a hold that
        /// was reset out from under its owner, not fought for.</summary>
        public TerritorySnapshot WithNeutralReset(int zone, int nowMs)
        {
            TerritorySnapshot next = Copy();
            if (!InRange(zone)) return next;
            next.owners[zone] = TerritoryMap.Neutral;
            next.heldSinceMs[zone] = nowMs;
            next.lastOwner[zone] = TerritoryMap.Neutral;
            next.lastHeldMs[zone] = 0;
            next.bountyPaid[zone] = 0;
            return next;
        }

        public List<int> ZonesWhoseOwnerChangedSince(TerritorySnapshot previous)
        {
            var changed = new List<int>();
            for (int zone = 0; zone < owners.Length; zone++)
                if (previous == null || previous.OwnerOf(zone) != owners[zone])
                    changed.Add(zone);
            return changed;
        }

        public IReadOnlyDictionary<int, int> OwnersByZone()
        {
            var map = new Dictionary<int, int>(owners.Length);
            for (int zone = 0; zone < owners.Length; zone++) map[zone] = owners[zone];
            return map;
        }

        /// <summary>Writes into a Photon Hashtable (or any dictionary). Arrays are int[] because
        /// Photon serialises those natively. Photon's Hashtable derives from
        /// Dictionary&lt;object, object&gt;, so it can be passed here directly - checked in the Editor
        /// (Task 2.1b) - which keeps this class free of Photon and testable without a network.</summary>
        public void WriteTo(IDictionary<object, object> props)
        {
            props[OwnersKey] = (int[])owners.Clone();
            props[HeldSinceKey] = (int[])heldSinceMs.Clone();
            props[LastOwnerKey] = (int[])lastOwner.Clone();
            props[LastHeldKey] = (int[])lastHeldMs.Clone();
            props[BountyKey] = (int[])bountyPaid.Clone();
        }

        public static bool TryRead(IDictionary<object, object> props, int zoneCount, out TerritorySnapshot snapshot)
        {
            snapshot = null;
            if (props == null || !props.TryGetValue(OwnersKey, out object ownersRaw) || !(ownersRaw is int[] ownerArray))
                return false;
            snapshot = new TerritorySnapshot(
                Padded(ownerArray, zoneCount, TerritoryMap.Neutral),
                Padded(Read(props, HeldSinceKey), zoneCount, 0),
                Padded(Read(props, LastOwnerKey), zoneCount, TerritoryMap.Neutral),
                Padded(Read(props, LastHeldKey), zoneCount, 0),
                Padded(Read(props, BountyKey), zoneCount, 0));
            return true;
        }

        private static int[] Read(IDictionary<object, object> props, string key) =>
            props.TryGetValue(key, out object raw) ? raw as int[] : null;

        private static int[] Padded(int[] source, int length, int fill)
        {
            int[] result = Filled(length, fill);
            if (source != null)
                for (int i = 0; i < length && i < source.Length; i++) result[i] = source[i];
            return result;
        }

        private static int[] Filled(int length, int value)
        {
            var array = new int[length];
            for (int i = 0; i < length; i++) array[i] = value;
            return array;
        }

        private bool InRange(int zone) => zone >= 0 && zone < owners.Length;

        private TerritorySnapshot Copy() => new TerritorySnapshot(
            (int[])owners.Clone(), (int[])heldSinceMs.Clone(), (int[])lastOwner.Clone(),
            (int[])lastHeldMs.Clone(), (int[])bountyPaid.Clone());
    }
}
