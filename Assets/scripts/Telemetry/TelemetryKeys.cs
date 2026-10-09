namespace Overpower.Telemetry
{
    /// <summary>Every telemetry event name and field key as constants, so the writers and the Editor aggregator that reads these
    /// files back never retype a key by hand (a typo silently drops a column from the report). TelemetryLine.Begin owns "e" (event
    /// name) and "t" (match seconds); every other short key used in an event's body lives here.
    ///
    /// A few names are deliberately both an event name and a field key inside a DIFFERENT event (Bounty, Refund, UnderAttack): the
    /// field means exactly what the event does. The role aliases (Attacker, Killer, SourceActor...) point at the same letter because
    /// "a" is always "the actor this line credits".</summary>
    public static class TelemetryKeys
    {
        /// <summary>Session header schema version. Bump it the day an event's shape changes in a way the aggregator must branch on;
        /// TelemetryLog counts anything newer than it understands instead of failing the whole report.</summary>
        public const int SchemaVersion = 1;

        // ---------------------------------------------------------------- Room Properties (match identity)
        // Written once by the first master into the room's Custom Properties and read by every client on join/update (see
        // MatchTelemetry); not part of any event line.
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
        /// <summary>Continuous damage (DamageSource.Burn: status burn and FireField's DoT) is bucketed and flushed as one `dot` line
        /// instead of one `hit` line per tick (see DotAccumulator).</summary>
        public const string Dot = "dot";
        public const string UltimateReady = "ultimateReady";
        /// <summary>For the Invulnerability ultimate (id 25) this means "committed" at the press, NOT "was protected": it arms a trap
        /// for armedSeconds, so whether the press protected anyone is decided by whether a hit lands inside the window.
        /// SecondsSinceReady still means how long a full meter was held before committing. See PlayerTelemetry.HandleCast for reading
        /// the two lines together: that pairing must count only the `status` line whose effect field reads "invulnerability", never
        /// every status(ab=25) line, or a stunSeconds > 0 retune double-counts each trigger and can push the rate negative.</summary>
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
        /// <summary>MatchTelemetry.LogPhase: MatchTelemetry itself logs `phase` 1 once as an anchor when the master claims the match
        /// identity; higher numbers come from MatchDirector (see PhaseTimeline.From).</summary>
        public const string Phase = "phase";
        /// <summary>Logged through MatchTelemetry.LogElimination, called by MatchDirector.</summary>
        public const string Elimination = "elimination";
        /// <summary>A team holding no other capital in play just took one that isn't its own (MatchPhaseRules.IsAdoption) - logged
        /// master-only from MatchDirector.HandleOwnershipChanged, live only (the warm-up sandbox never adopts anything).</summary>
        public const string Adopt = "adopt";
        /// <summary>One line per console message this client's own ConsoleTelemetry admitted (cut, folded, capped and scrubbed by
        /// ConsoleLineRule). Also the event name of the "N console lines dropped" summary lines the same rule produces, whether the
        /// drop happened before the file opened or past the per-second cap.</summary>
        public const string Console = "console";
        /// <summary>Ctrl+B - "a bug just happened", with a screenshot saved alongside this client's own log file. BugMarkerKey is the
        /// only writer.</summary>
        public const string Bug = "bug";
        /// <summary>The sender's own public chat text, logged right before PhotonChat.SubmitPublicChatOnClick publishes it - never
        /// the receive callback's copy.</summary>
        public const string Chat = "chat";

        // ---------------------------------------------------------------- session (line 1)
        public const string Schema = "schema";
        public const string MatchId = "m";
        public const string Nick = "nick";
        public const string IsMaster = "master";
        /// <summary>True on the session line of a spectator host (no team, no player row in the report).</summary>
        public const string Spectator = "spec";
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
        public const string Attachment = "eq";
        public const string Mobility = "mob";
        public const string Ultimate = "ult";
        public const string AbsorbLevel = "abl";
        public const string RechargeLevel = "rcl";
        public const string Health = "hp";
        /// <summary>Its own key, not an alias of Health: `hit`/`dot` report how much health the hit/bucket cost, which would collide
        /// with `sample`'s current health under "hp". HealthAtTrigger stays aliased to Health: `overpower`'s health at this moment is
        /// the same kind of value `sample`'s "hp" is.</summary>
        public const string HealthLost = "hpLost";
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
        /// <summary>`purchase`/`shopBlocked`'s item id: a weapon or ability's real id. Armor has no id of its own (only a path and a
        /// level), so LoadoutScreen encodes it: 100 + the absorb level reached/attempted, 200 + the recharge level
        /// (LoadoutScreen.ArmorAbsorbItemBase/ArmorRechargeItemBase), so "absorb reaches 1" (101) and "recharge reaches 1" (201) never
        /// collide.</summary>
        public const string ItemId = "item";
        public const string Price = "price";
        public const string BalanceAfter = "balAfter";
        public const string Free = "free";
        /// <summary>`refund`'s gold amount, or `bounty`'s payout. For `bounty` this is the PER-PLAYER amount (TerritoryConfig ›
        /// Capture Bounty is paid to EACH player of the capturing team); BountyRule.PayoutOnCapture passes the per-tier number straight
        /// through, unmultiplied by team size.</summary>
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
        /// <summary>`hit`'s mark outcome: 1 (MarkOutcome.Applied) placed a mark, 2 (MarkOutcome.Cashed) cashed one in. Written only
        /// when the hit touched a mark, so hit lines from non-marking weapons and self/teammate/shielded hits stay byte-identical. A
        /// cashed hit's Raw already includes the cash-in bonus (MarkLedger.ScaledAmount runs before DamageResolver); this key says WHY
        /// that hit was bigger, not a second amount.</summary>
        public const string Mark = "mark";
        public const string Distance = "d";
        public const string Vulnerable = "vul";
        /// <summary>Unused: dropped from `hit`/`dot`. PlayerHealth.ApplyDamage returns BEFORE raising Damaged when the victim is
        /// invulnerable, so a player's `hit` line could never read true, and a dummy never checks invulnerability at all. Kept in
        /// case a future place needs it.</summary>
        public const string Invulnerable = "inv";
        public const string OverpowerActive = "op";
        public const string Effect = "effect";
        /// <summary>`status` writes both duration and magnitude for every kind (Duration alongside this), so the aggregator can sum
        /// seconds uniformly instead of guessing which field a kind used.</summary>
        public const string DurationOrMagnitude = "mag";
        public const string Duration = "dur";
        /// <summary>`dot` only: a damage bucket's tick count and time span.</summary>
        public const string Ticks = "ticks";
        public const string FirstT = "firstT";
        public const string LastT = "lastT";
        public const string Assists = "assists";
        public const string TimeAlive = "timeAlive";
        public const string UnspentGold = "gold";
        public const string TimeDead = "deadSec";
        public const string UnderAttackSpawn = "uaSpawn";
        public const string HealTiers = "tiers";
        /// <summary>`death`'s embedded loadout snapshot needs its own keys: Weapon/Attachment/Mobility/Ultimate mean "the killing
        /// weapon/ability" on a `death` line (as on `hit`), so the VICTIM's equipped loadout at death must not collide with them.
        /// `sample` has no killing weapon, so it uses Weapon/Attachment/Mobility/Ultimate directly for this player's own loadout.</summary>
        public const string LoadoutWeapon = "lw";
        public const string LoadoutAttachment = "leq";
        public const string LoadoutMobility = "lmob";
        public const string LoadoutUltimate = "lult";
        /// <summary>A short string state reused by every event that needs one: overheat's "silenced"/"recovered", capture's
        /// "started"/"paused"/"resumed"/"completed"/"drainStarted"/"neutralised"/"drainPaused", overpower's "triggered"/"ended" (Reason
        /// "distance"/"death" alongside "ended"), underAttack's "start"/"end". The exact strings are each hook's own choice, not
        /// fixed here.</summary>
        public const string State = "state";
        public const string SecondsSinceReady = "sinceReady";
        /// <summary>1 on a `cast` line that is a follow-up to an earlier cast (the AoE Zone's throw), not a fresh use.</summary>
        public const string FollowUp = "followUp";

        // ---------------------------------------------------------------- territory
        public const string Tier = "tier";
        public const string OldOwner = "old";
        public const string NewOwner = "new";
        public const string HeldSince = "since";
        public const string Progress = "progress";
        public const string Players = "players";
        public const string ZoneDistance = "zoneDist";

        // ---------------------------------------------------------------- phase / elimination
        /// <summary>`phase`'s own field: 1 (the anchor MatchTelemetry writes) or 2+ (MatchDirector). PhaseTimeline.From reads it to
        /// find the first phase >= 2.</summary>
        public const string PhaseNumber = "num";
        /// <summary>Shared by `phase` and `elimination`: which team ids are still in the match after
        /// this change. `elimination` also uses the plain Team key (above) for the team that was just
        /// eliminated.</summary>
        public const string TeamsRemaining = "remain";

        // ---------------------------------------------------------------- marker
        public const string Note = "note";

        // ---------------------------------------------------------------- respawn
        /// <summary>`respawn`'s own field: true only on the ONE respawn line ResetForMatchStart can produce - a player dead the instant
        /// the match goes live is brought back by SetAlive(true), which raises the same AliveChanged(true) an ordinary respawn does.
        /// Absent on every real respawn. A field rather than a suppressed line or new event because the aggregator's alive-time-tail
        /// math needs the real respawn timestamp either way and no table counts `respawn` lines as a stat (see
        /// PlayerLifecycle.LastAliveChangeWasFreshStart, PlayerTelemetry.HandleAliveChanged).</summary>
        public const string Fresh = "fresh";

        // ---------------------------------------------------------------- console / bug / chat
        /// <summary>`console`'s own message text, already scrubbed and cut to
        /// TelemetryConfig.consoleMessageMaxChars by ConsoleLineRule.</summary>
        public const string Message = "msg";
        /// <summary>`console`'s own stack trace, scrubbed and cut - error/exception only (log/warning
        /// never carry one). Its own key rather than reusing anything above: nothing else on this list
        /// means "a call stack".</summary>
        public const string Stack = "stack";
        /// <summary>`console`'s fold count: how many times this exact (level, message) repeated within the fold window; 1 if nothing
        /// folded into it. FirstT/LastT (same meaning as `dot`'s) are only meaningful when this is greater than 1.</summary>
        public const string RepeatCount = "n";
        /// <summary>How many console lines ConsoleLineRule counted rather than wrote, on the one summary `console` line that reports
        /// it - the per-second cap's overflow, or (before the file opened) anything the pre-open queue refused. Never present on an
        /// ordinary console line.</summary>
        public const string Dropped = "dropped";
        /// <summary>`bug`'s own screenshot FILE NAME (not a path - the report links it relative to the
        /// match folder, which is this line's own folder) - see BugMarkerKey.</summary>
        public const string ScreenshotFile = "img";
        /// <summary>`chat`'s own message text, cut to 300 characters - see PhotonChat.
        /// SubmitPublicChatOnClick.</summary>
        public const string Text = "text";
    }
}
