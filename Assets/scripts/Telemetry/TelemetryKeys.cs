namespace Overpower.Telemetry
{
    /// <summary>Every telemetry event name and field key, as one static class of constants - so T3-T6
    /// (and the Editor aggregator that later reads these same files back) never retype a key by hand
    /// and risk a typo that silently drops a whole column from the report. TelemetryLine.Begin already
    /// owns "e" (event name) and "t" (match seconds); every other short key used inside an event's
    /// body lives here.
    ///
    /// A few names are deliberately reused as BOTH an event name and a field key inside a DIFFERENT
    /// event (Bounty, Refund, UnderAttack): the field they name means exactly the same thing as the
    /// event of that name, so one constant serves both rather than inventing a synonym. A few
    /// role-specific aliases (Attacker, Killer, SourceActor...) point at the same underlying letter,
    /// for the same reason the spec's own `hit` example does: "a" is always "the actor this line
    /// credits", whoever that is for the event in question.</summary>
    public static class TelemetryKeys
    {
        /// <summary>Session header schema version. Bump this the day an event's shape changes in a way
        /// the aggregator must branch on; T5's TelemetryLog counts anything newer than it understands
        /// instead of failing the whole report (see the spec's Error handling).</summary>
        public const int SchemaVersion = 1;

        // ---------------------------------------------------------------- Room Properties (match identity)
        // Written once by the first master into the room's Custom Properties (not part of any one
        // event line) and read by every client on join/update - see MatchTelemetry. Named here for the
        // same reason as every event/field key below: two hand-typed copies of "mId" are two chances
        // to typo one.
        public const string RoomMatchId = "mId";
        public const string RoomMatchStart = "mStart";

        // ---------------------------------------------------------------- event names
        public const string Session = "session";
        public const string Sample = "sample";
        public const string GoldEarned = "goldEarned";
        public const string Purchase = "purchase";
        public const string Refund = "refund";
        public const string ShopBlocked = "shopBlocked";
        public const string Shots = "shots";
        public const string Cast = "cast";
        public const string Hit = "hit";
        public const string Status = "status";
        public const string Death = "death";
        public const string Respawn = "respawn";
        public const string Heal = "heal";
        public const string Overheat = "overheat";
        public const string UltimateReady = "ultimateReady";
        public const string UltimateUsed = "ultimateUsed";
        public const string Ownership = "ownership";
        public const string Capture = "capture";
        public const string Bounty = "bounty";
        public const string UnderAttack = "underAttack";
        public const string Overpower = "overpower";
        public const string Join = "join";
        public const string Leave = "leave";
        public const string MasterChanged = "masterChanged";
        public const string Marker = "marker";
        // `phase` / `elimination` are Task 2.7's own events, raised into MatchTelemetry after this
        // feature ships - deliberately not added yet (see the plan's "Changes from the spec, decided
        // while planning"). Do not add them here from T2; that is 2.7's job.

        // ---------------------------------------------------------------- session (line 1)
        public const string Schema = "schema";
        public const string MatchId = "m";
        public const string Nick = "nick";
        public const string IsMaster = "master";
        public const string Commit = "commit";
        public const string UnityVersion = "uv";
        public const string Platform = "plat";
        public const string Tuning = "tuning";

        // ---------------------------------------------------------------- identity, reused across events
        /// <summary>The actor this line is about - almost always whoever raised the event; for `hit`
        /// it is the attacker, matching the spec's own worked example.</summary>
        public const string Actor = "a";
        public const string Team = "tm";
        public const string Attacker = Actor;
        public const string AttackerTeam = "at";
        public const string Killer = Actor;
        public const string KillerTeam = AttackerTeam;
        public const string SourceActor = Actor;
        public const string Victim = "v";
        public const string VictimTeam = "vt";

        // ---------------------------------------------------------------- sample
        public const string Balance = "bal";
        public const string X = "x";
        public const string Z = "z";
        public const string Alive = "alive";
        public const string Zone = "zone";
        public const string Weapon = "w";
        public const string Equipment = "eq";
        public const string Mobility = "mob";
        public const string Ultimate = "ult";
        public const string AbsorbLevel = "abl";
        public const string RechargeLevel = "rcl";
        public const string Health = "hp";
        public const string HealthLost = Health;
        public const string HealthAtTrigger = Health;
        public const string Armor = "armor";
        public const string UltimateCharge = "ultc";
        public const string OverheatLevel = "heat";
        public const string Ping = "ping";

        // ---------------------------------------------------------------- economy
        public const string Territory = "terr";
        public const string Zones = "zones";
        public const string Debug = "debug";
        public const string Other = "other";
        public const string Category = "cat";
        public const string ItemId = "item";
        public const string Price = "price";
        public const string BalanceAfter = "balAfter";
        public const string Free = "free";
        public const string Amount = "amount";
        public const string Reason = "reason";
        public const string Shortfall = "shortfall";
        public const string HoldSeconds = "holdSec";

        // ---------------------------------------------------------------- combat
        public const string Pulls = "pulls";
        public const string Projectiles = "proj";
        public const string Slot = "slot";
        public const string AbilityId = "ab";
        public const string Source = "src";
        public const string Raw = "raw";
        public const string ArmorAbsorbed = "arm";
        public const string Lethal = "lethal";
        public const string Distance = "d";
        public const string Vulnerable = "vul";
        public const string Invulnerable = "inv";
        public const string OverpowerActive = "op";
        public const string Effect = "effect";
        public const string DurationOrMagnitude = "mag";
        public const string Assists = "assists";
        public const string TimeAlive = "timeAlive";
        public const string UnspentGold = "gold";
        public const string TimeDead = "deadSec";
        public const string UnderAttackSpawn = "uaSpawn";
        public const string HealTiers = "tiers";
        /// <summary>Task T3: `death`'s embedded loadout snapshot needs its own keys, distinct from
        /// Weapon/Equipment/Mobility/Ultimate above - those mean "the killing weapon/ability" on a
        /// `death` line (matching `hit`'s own convention), so the VICTIM's own equipped loadout at
        /// the moment of death needs separate keys on that same line rather than colliding with them.
        /// `sample` has no such collision (there is no "killing weapon" concept there), so it keeps
        /// using Weapon/Equipment/Mobility/Ultimate directly for this player's own loadout.</summary>
        public const string LoadoutWeapon = "lw";
        public const string LoadoutEquipment = "leq";
        public const string LoadoutMobility = "lmob";
        public const string LoadoutUltimate = "lult";
        /// <summary>A short string state/label, reused by every event that needs one instead of a
        /// dedicated bool or a bespoke key: overheat's "silenced"/"recovered", capture's
        /// "started"/"paused"/"resumed"/"completed"/"drainStarted"/"neutralised"/"drainPaused",
        /// overpower's "triggered"/"expired"/"brokenByDistance", underAttack's "start"/"end". The exact
        /// value strings are each hook's own choice (T3/T4), not fixed here.</summary>
        public const string State = "state";
        public const string SecondsSinceReady = "sinceReady";

        // ---------------------------------------------------------------- territory
        public const string Tier = "tier";
        public const string OldOwner = "old";
        public const string NewOwner = "new";
        public const string HeldSince = "since";
        public const string Progress = "progress";
        public const string Players = "players";
        public const string ZoneDistance = "zoneDist";

        // ---------------------------------------------------------------- marker
        public const string Note = "note";
    }
}
