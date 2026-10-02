using System.Collections.Generic;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Net
{
    /// <summary>
    /// Task 9e-2: everything on the LOCAL player's Custom Properties that belongs to the match just left rather than to the player
    /// across matches - the one list RoomManager.ResetMatchProperties writes when a match is given up (a deliberate leave, Leave or
    /// OK on the rejoin panel). A value of null REMOVES the key: gold and the loadout are then read as "never set", exactly like a
    /// first-time joiner (GoldWallet.Start falls through to StartingGold, PlayerLoadout.Start publishes the starter kit).
    /// "teamID" is in the list: a stale team from the room just left must not open the next lobby's match log with the old team (MatchTelemetry.TryOpenFile) or make a spectator look like a player. Not in the list: the nickname.
    /// </summary>
    public static class MatchPropertyReset
    {
        public static Dictionary<string, object> Build() => new Dictionary<string, object>
        {
            { PlayerLifecycle.AliveKey, true },
            { PlayerLifecycle.LastStandKey, false },
            { PlayerLifecycle.LastStandAtKey, null },
            { GoldWallet.GoldKey, null },
            { LoadoutProperties.ArmorAbsorbLevelKey, 0 },
            { LoadoutProperties.ArmorRechargeLevelKey, 0 },
            { LoadoutProperties.WeaponKey, null },
            { LoadoutProperties.AttachmentKey, null },
            { LoadoutProperties.UltimateKey, null },
            { LoadoutProperties.MobilityKey, null },
            { ScoreboardRules.Key, null },
            { HealthPackManager.RequestKey, null },
            { AllyPortalTraveller.UseKey, null },
            { AllyPortalTraveller.ReadyKey, null },
            { StatusLabelProperty.Key, null },
            { AoeZoneRecast.PropertyKey, null },
            { ScopeSightProperty.Key, null },
            { Teams.SpectatorKey, null },
            { Teams.TeamKey, null },
        };
    }
}
