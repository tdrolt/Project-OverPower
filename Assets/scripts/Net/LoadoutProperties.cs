using System.Collections.Generic;
using Overpower.Data;

namespace Overpower.Net
{
    /// <summary>
    /// The four Custom Property keys a player's loadout lives in, and the one reader every caller
    /// should go through - mirrors Teams.cs: team membership has exactly one home key and one
    /// lookup, and a loadout deserves the same treatment rather than four scripts each guessing at
    /// a string and a cast.
    ///
    /// Custom Properties travel as a plain object dictionary (Photon's own Hashtable derives from
    /// Dictionary&lt;object, object&gt;), so a value can arrive missing, or present but of the wrong
    /// type - a late joiner reading a property before it has ever been written, or a stale build on
    /// the other end writing something unexpected. ReadInt below is the only place that has to
    /// think about that; everything downstream just gets a fallback instead of an exception.
    /// </summary>
    public static class LoadoutProperties
    {
        public const string WeaponKey = "weaponId";
        public const string EquipmentKey = "equipmentId";
        public const string UltimateKey = "ultimateId";
        public const string MobilityKey = "mobilityId";

        /// <summary>Means "no ability equipped in this slot". Never 0 - id 0 on an AbilityDefinition
        /// means "not set" on the asset itself, a different kind of empty, so this needs its own
        /// value that can never collide with a real id.</summary>
        public const int Empty = -1;

        /// <summary>
        /// Reads one int-valued Custom Property, never throwing. A missing key or a value that is
        /// not an int (a stale property, or one that has not been written yet) both fall back
        /// silently rather than bringing down whatever loop is applying a loadout to nine players.
        /// </summary>
        public static int ReadInt(IDictionary<object, object> props, string key, int fallback)
        {
            if (props == null)
                return fallback;
            if (!props.TryGetValue(key, out object raw))
                return fallback;

            return raw is int value ? value : fallback;
        }

        /// <summary>
        /// The Custom Property key an ability slot's id lives under. Primary has none of its own -
        /// it is a weapon id, resolved through WeaponCatalogue and carried under WeaponKey instead
        /// of the ability catalogue - so this returns null for it rather than guessing at a key
        /// that would resolve to nothing.
        /// </summary>
        public static string KeyFor(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Equipment: return EquipmentKey;
                case AbilitySlot.Ultimate: return UltimateKey;
                case AbilitySlot.Mobility: return MobilityKey;
                default: return null;
            }
        }
    }
}
