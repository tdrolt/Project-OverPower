using System.Collections.Generic;
using Overpower.Data;

namespace Overpower.Net
{
    /// <summary>
    /// The four Custom Property keys a player's loadout lives in, and the one reader every caller
    /// should go through (mirrors Teams.cs). Custom Properties travel as a plain object dictionary,
    /// so a value can arrive missing or of the wrong type (a late joiner, a stale build); ReadInt is
    /// the only place that has to think about that, everything downstream gets a fallback.
    /// </summary>
    public static class LoadoutProperties
    {
        public const string WeaponKey = "weaponId";
        public const string AttachmentKey = "attachmentId";
        public const string UltimateKey = "ultimateId";
        public const string MobilityKey = "mobilityId";

        /// <summary>Armor upgrade levels: two independent counters, not one tier, so two keys. A
        /// missing key means level 0, which is why PlayerLoadout reads these with a fallback rather
        /// than LoadoutProperties.Empty.</summary>
        public const string ArmorAbsorbLevelKey = "armorAbsorbLvl";
        public const string ArmorRechargeLevelKey = "armorRechargeLvl";

        /// <summary>Means "no ability equipped in this slot". Never 0: id 0 on an AbilityDefinition
        /// means "not set" on the asset, a different kind of empty, so this needs a value that can
        /// never collide with a real id.</summary>
        public const int Empty = -1;

        /// <summary>
        /// Reads one int-valued Custom Property, never throwing. A missing key or a non-int value
        /// falls back silently rather than bringing down the loop applying a loadout to nine players.
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
        /// The Custom Property key an ability slot's id lives under. Primary has none: it is a weapon
        /// id carried under WeaponKey, so this returns null for it rather than a key that resolves
        /// to nothing.
        /// </summary>
        public static string KeyFor(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Attachment: return AttachmentKey;
                case AbilitySlot.Ultimate: return UltimateKey;
                case AbilitySlot.Mobility: return MobilityKey;
                default: return null;
            }
        }
    }
}
