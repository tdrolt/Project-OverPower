using System.Collections.Generic;

namespace Overpower.EditorTools.Telemetry
{
    public sealed class MarkerRow
    {
        public double T;
        public int Actor;
        public string Note;
    }

    public sealed class PlayerCoverageRow
    {
        public int Actor;
        public string Nick;
        public double FirstT;
        public double LastT;
    }

    /// <summary>One row per actor seen ANYWHERE in the match (joins, sessions, `hit` attackers/victims, `death` killers/assists),
    /// whether or not that actor's own log file is in this folder. Whole-match only: "which logs are in this report" is a fact
    /// about the folder, not a time window. The header's `log-coverage` container and csv/whole_match/log_coverage.csv read this list.</summary>
    public sealed class LogCoverageRow
    {
        public int Actor;
        /// <summary>For a missing actor (no file of their own) this comes from another client's `join` line, which carries the nick;
        /// empty only when no `join` line for the actor was found (an old log, or an actor seen only via `hit`/`death`).</summary>
        public string Nick = "";
        /// <summary>Whether a `session` line (so, a whole log file) for this actor was found.</summary>
        public bool FilePresent;
        /// <summary>Null (renders as "-") rather than 0 when there is nothing to show: a missing actor's First/Last t come from their
        /// `join`/`leave` events (logged by whichever OTHER client saw them), not from a file they never wrote.</summary>
        public double? FirstT;
        public double? LastT;
        /// <summary>True for a missing actor whose `join` AND `leave` were both seen (by other clients): very likely gone before their own
        /// telemetry file opened, a softer story than "no log from them at all" with its own gentler warning wording.</summary>
        public bool JoinedAndLeftBeforeLoggingStarted;
    }

    /// <summary>One console line inside a bug card's window (20s before to 5s after the mark) - every client's, merged and sorted by
    /// time, each labelled with the player it came from (a console line has no actor field; the file IS the player - see
    /// TelemetryAggregator.BuildBugsAndConsole).</summary>
    public sealed class ConsoleLineRef
    {
        public double T;
        public int Actor;
        public string Nick = "";
        /// <summary>"log"/"warning"/"error"/"exception"/"assert" - never "dropped" (a summary line, not a real message, is left out of
        /// every bug window).</summary>
        public string Level = "";
        public string Message = "";
        /// <summary>This line's fold count (TelemetryKeys.RepeatCount); 1 for a line nothing else folded into.</summary>
        public int Count = 1;
    }

    /// <summary>One card for the "Bug reports" section: one row per `bug` line (Ctrl+B), enriched with the reporter's chat note(s) and
    /// every client's nearby console lines. Whole-match only (see ReportHeader.Bugs), like LogCoverageRow.</summary>
    public sealed class BugRow
    {
        public double T;
        public int Actor;
        public string Nick = "";
        public int Team;
        public bool Alive;
        public int Zone;
        public float X;
        public float Z;
        public int Weapon;
        public int Attachment;
        public int Mobility;
        public int Ultimate;
        /// <summary>The screenshot's FILE NAME (TelemetryKeys.ScreenshotFile), never a path. The HTML links it relative to the report, which
        /// sits in the match folder the screenshot was saved into (BugMarkerKey), so a bare name resolves there, including once the
        /// folder is zipped and opened elsewhere.</summary>
        public string ScreenshotFile = "";
        public int Phase = 1;
        /// <summary>The reporter's OWN chat text(s), 0-60s after the mark, never another player's. Empty when they said nothing.</summary>
        public List<string> ChatNotes = new List<string>();
        /// <summary>Every client's console lines from 20s before to 5s after the mark, merged and sorted by time - every level, including
        /// plain "log" (the one place plain log lines appear).</summary>
        public List<ConsoleLineRef> ConsoleWindow = new List<ConsoleLineRef>();
    }

    /// <summary>One row of the per-player "Console" section: every error/exception/warning a player's file logged, grouped by exact
    /// message, with count and first/last time. A plain "log" line is never grouped here (it only appears inside bug windows, see
    /// BugRow.ConsoleWindow); a "dropped" summary line is not a real message either. Whole-match only.</summary>
    public sealed class ConsolePlayerGroupRow
    {
        public int Actor;
        public string Nick = "";
        public string Level = "";
        public string Message = "";
        /// <summary>Summed across every separate fold bucket (TelemetryKeys.RepeatCount) that matched this (actor, level, message), not just
        /// how many `console` JSONL lines matched: one line can itself represent several real repeats.</summary>
        public int Count;
        public double FirstT;
        public double LastT;
    }

    /// <summary>Everything that isn't one of the 12 tables: match-wide facts and the quality counters (malformed/unknown lines are
    /// never a failure, just a number in the header).</summary>
    public sealed class ReportHeader
    {
        public string MatchId = "";
        public double MatchLengthSeconds;
        public int PlayersPerTeam;
        public List<string> Commits = new List<string>();
        public bool FreeLoadoutUsed;
        public bool DebugGoldUsed;
        public List<MarkerRow> Markers = new List<MarkerRow>();
        public List<PlayerCoverageRow> Coverage = new List<PlayerCoverageRow>();
        /// <summary>Always the whole match's list, even on a Phase 1/Phase 2-scoped ReportTables (see LogCoverageRow): CsvReportWriter writes it
        /// only into csv/whole_match/, and the HTML header reads it once; the values are identical across every scope.</summary>
        public List<LogCoverageRow> LogCoverage = new List<LogCoverageRow>();
        public int MalformedLineCount;
        public int UnknownEventCount;
        public int UnknownCaptureStateCount;
        public string OtherMatchId;
        public int OtherMatchFileCount;
        /// <summary>Quality counters: bad input is counted here instead of crashing or passing silently.</summary>
        public int UnreadableFileCount;
        public int NewerSchemaCount;
        /// <summary>An `ownership` line whose tier was outside 1..4 (a bad id, or a zone not yet registered): skipped rather than indexing
        /// out of range, and counted here.</summary>
        public int InvalidTierCount;

        /// <summary>True when the match had an `elimination` event but no `phase` >= 2 event (PhaseTimeline.UsedEliminationFallback), so the
        /// HTML can warn that the Phase 2 start came from the elimination (MatchDirector should be logging both).</summary>
        public bool EliminationFallbackUsed;

        /// <summary>How long the warm-up lasted before this match went live (PhaseTimeline.LiveSeconds); 0 for a legacy log. Set from the SAME
        /// match-wide PhaseTimeline on every scope's header, like EliminationFallbackUsed, not this scope's own (possibly
        /// warm-up-excluding) window.</summary>
        public double WarmupSeconds;

        /// <summary>True for a new-style log (with its own `phase` 0 warm-up anchor) whose match never went live - PhaseTimeline.HasWarmup
        /// &amp;&amp; !WentLive. The HTML shows a red warning instead of the warm-up line, and every table is empty (Phase 1/2 collapse).</summary>
        public bool NeverWentLive;

        /// <summary>The primary session's tuning snapshot, re-serialized flat; the HTML report embeds it verbatim.</summary>
        public string TuningJson;

        /// <summary>The "Bug reports" section's rows - always the whole match's list, like LogCoverage above (see BugRow).</summary>
        public List<BugRow> Bugs = new List<BugRow>();

        /// <summary>The per-player "Console" section's rows (see ConsolePlayerGroupRow).</summary>
        public List<ConsolePlayerGroupRow> ConsoleByPlayer = new List<ConsolePlayerGroupRow>();
    }

    public sealed class GoldTimelineRow
    {
        public double T;
        public int Actor;
        public string Nick;
        public int Team;
        public int Balance;
        public int EarnedSoFar;
        public int SpentSoFar;
        /// <summary>1 or 2 - which phase this sample's t falls in. Always 1 when the match never had a phase 2.</summary>
        public int Phase = 1;
    }

    public sealed class EconomyByMinuteRow
    {
        public int Minute;
        public int Team;
        /// <summary>Index 0..3 = tier 1..4. Double, not int: a `goldEarned` line's `zones` array is rounded to whole gold per zone at the
        /// SOURCE, so summing many already-rounded lines drifted from the authoritative `terr` total (a real log: Sigma-terr 254 vs
        /// Sigma-zones 244). Each line is rescaled to sum to its `terr` before accumulating (TelemetryAggregator.ScaledZones); a double
        /// keeps that correction instead of re-rounding it away.</summary>
        public double[] IncomeByTier = new double[4];
        public int Bounty;
        public int Refund;
        public int Spent;
        /// <summary>Index 0..3 = tier 1..4.</summary>
        public int[] ZonesHeldByTier = new int[4];
        public int GoldGapToRichest;
        /// <summary>Which phase this minute bucket belongs to, from the bucket's START time vs the transition. The minute INDEX stays the
        /// absolute match minute (not renumbered relative to the phase start) even on a Phase 2-scoped ReportTables - see
        /// TelemetryAggregator.BuildGoldTimelineAndEconomy on why this table isn't windowed as cleanly as the rest.</summary>
        public int Phase = 1;
        /// <summary>A `goldEarned` line can have `terr` > 0 (real territory gold, already in players.csv via its running total) with `zones`
        /// empty or summing to 0: ScaledZones has no ratio to rescale by, so the amount would vanish from every zone-keyed table.
        /// Kept here so team/tier income totals still add up to what players.csv reports.</summary>
        public double UnattributedIncome;
    }

    public sealed class ZoneIncomeRow
    {
        public int Zone;
        public int Team;
        public int Tier;
        public double SecondsHeld;
        /// <summary>Double, not int - see EconomyByMinuteRow.IncomeByTier on why the per-line rescale keeps its fraction.</summary>
        public double GoldGenerated;
    }

    public sealed class OwnershipRow
    {
        public int Zone;
        public int Tier;
        public int Team;
        public double From;
        public double To;
        public double Duration;
        /// <summary>"captured" (another team took it), "decayed" (it went neutral), "matchEnd" (still held when the log ends), or
        /// "phaseBoundary" (this row is a phase-clipped PIECE of a stint that continues past this window's end; the real reason lives
        /// on the OTHER piece).</summary>
        public string HowEnded;
        /// <summary>1 or 2 - which phase this (possibly clipped) piece of the stint belongs to. A stint crossing the phase boundary is
        /// split into two rows, one per phase, even on the whole-match ReportTables (see TelemetryAggregator.BuildOwnership).</summary>
        public int Phase = 1;
    }

    public sealed class CaptureRow
    {
        public int Zone;
        /// <summary>The zone's tier at the time of this capture attempt (from the ownership-derived zoneTier map BuildOwnership builds); 0 if
        /// no ownership line ever established it.</summary>
        public int Tier;
        public int Team;
        public double Start;
        public double End;
        /// <summary>"completed" / "neutralised" (from the matching `ownership` change - never trusted from the `capture` line's own state
        /// string, which is unreliable: "paused"/"drainPaused" is reported for BOTH a genuine pause and a completion/neutralisation) or
        /// "abandoned" (still open when the log ends, or superseded mid-match by a fresh `started`/`drainStarted` for a different team
        /// or direction) or "phaseBoundary" (same meaning as OwnershipRow.HowEnded).</summary>
        public string Outcome;
        public double Duration;
        public int Players;
        /// <summary>See OwnershipRow.Phase - same mechanism, same reason.</summary>
        public int Phase = 1;
    }

    public sealed class PurchaseRow
    {
        public double T;
        public int Actor;
        public string Nick;
        public int Team;
        /// <summary>"purchase" or "refund".</summary>
        public string Kind;
        public string Category;
        public int ItemId;
        /// <summary>Price paid (purchase) or amount refunded (refund).</summary>
        public int Amount;
        public int BalanceAfter;
        public int Zone;
        public bool Free;
        /// <summary>1 or 2, from this purchase/refund's own t.</summary>
        public int Phase = 1;
    }

    public sealed class ShopBlockedRow
    {
        public double T;
        public int Actor;
        public string Nick;
        public int ItemId;
        public int Price;
        public string Reason;
        public int Shortfall;
        public int Zone;
        /// <summary>1 or 2, from this blocked click's own t.</summary>
        public int Phase = 1;
    }

    public sealed class HitRow
    {
        public double T;
        public int Attacker;
        public int AttackerTeam;
        public int Victim;
        public int VictimTeam;
        public int Weapon;
        public int Ability;
        public string Source;
        public float Raw;
        public float Armor;
        public float HealthLost;
        public bool Lethal;
        public float? Distance;
        public float Vulnerable;
        public bool Overpower;
        /// <summary>0 (no key on the raw line - a non-marking hit) / 1 (Applied) / 2 (Cashed). Read with a default of 0, matching every
        /// hit line written before this field existed.</summary>
        public int Mark;
        /// <summary>1 or 2, from this hit's own t.</summary>
        public int Phase = 1;
    }

    public sealed class WeaponRow
    {
        public int WeaponId;
        public double TimeEquippedSeconds;
        public int Pulls;
        public int Projectiles;
        /// <summary>Discrete `Projectile`-source hits only. Accuracy is Hits/Projectiles; a splash weapon's splash rows are counted separately
        /// (<see cref="SplashHits"/>) so one rocket landing (1 direct hit + N splash hits) can't push accuracy over 100%.</summary>
        public int Hits;
        /// <summary>`Splash`-source hits - counted, but never divided into Projectiles for accuracy (a splash row isn't "a projectile that
        /// connected", it's damage a DIFFERENT connecting projectile also dealt).</summary>
        public int SplashHits;
        public double Accuracy;
        public float DamageRaw;
        public float ArmorDamage;
        public float HealthDamage;
        public double DamagePerEquippedMinute;
        public int Kills;
        public double? MeanDistance;
        public double? MedianDistance;
        /// <summary>How many hits from THIS weapon placed a fresh mark (HitRow.Mark == 1) in the window; answers "how often do players cash
        /// the mark?" alongside MarksCashed (Decision 18).</summary>
        public int MarksPlaced;
        /// <summary>How many hits from THIS weapon cashed an existing mark in (HitRow.Mark == 2) in the window.</summary>
        public int MarksCashed;
    }

    public sealed class AbilityRow
    {
        public int AbilityId;
        public int Casts;
        public float DamageRaw;
        public int Kills;
        public int StatusCount;
        public double StatusSeconds;
    }

    public sealed class PlayerRow
    {
        public int Actor;
        public string Nick;
        public int Team;
        public int Kills;
        public int Deaths;
        public int Assists;
        public float DamageDealt;
        public float DamageTaken;
        public int GoldTerritory;
        public int GoldBounty;
        public int GoldRefund;
        public int GoldDebug;
        public int GoldOther;
        public int GoldSpent;
        public double TimeAlive;
        public double TimeOwnZone;
        public double TimeEnemyZone;
        public double TimeNeutralZone;
        public float Healing;
    }

    public sealed class DeathRow
    {
        public double T;
        public int Victim;
        public string VictimNick;
        public int VictimTeam;
        public int Killer;
        public int KillerTeam;
        public int[] Assists = System.Array.Empty<int>();
        public int Weapon;
        public int Ability;
        public float X;
        public float Z;
        public int UnspentGold;
        public int LoadoutWeapon;
        public int LoadoutAttachment;
        public int LoadoutMobility;
        public int LoadoutUltimate;
        public int AbsorbLevel;
        public int RechargeLevel;
        /// <summary>1 or 2, from this death's own t.</summary>
        public int Phase = 1;
    }

    /// <summary>The whole report as plain data - one property per CSV, plus the header. CsvReportWriter and HtmlReportWriter just format
    /// these; TelemetryAggregator is the only place that computes them, so the two outputs can never disagree (design doc, Outputs).</summary>
    public sealed class ReportTables
    {
        public ReportHeader Header = new ReportHeader();
        public List<GoldTimelineRow> GoldTimeline = new List<GoldTimelineRow>();
        public List<EconomyByMinuteRow> EconomyByMinute = new List<EconomyByMinuteRow>();
        public List<ZoneIncomeRow> ZoneIncome = new List<ZoneIncomeRow>();
        public List<OwnershipRow> Ownership = new List<OwnershipRow>();
        public List<CaptureRow> Captures = new List<CaptureRow>();
        public List<PurchaseRow> Purchases = new List<PurchaseRow>();
        public List<ShopBlockedRow> ShopBlocked = new List<ShopBlockedRow>();
        public List<HitRow> Hits = new List<HitRow>();
        public List<WeaponRow> Weapons = new List<WeaponRow>();
        public List<AbilityRow> Abilities = new List<AbilityRow>();
        public List<PlayerRow> Players = new List<PlayerRow>();
        public List<DeathRow> Deaths = new List<DeathRow>();
    }

    /// <summary>The three scopes a match report is built for (see TelemetryAggregator.BuildSet and PhaseTimeline). Phase2 is null for
    /// every log that never eliminates a team; CsvReportWriter and HtmlReportWriter treat that as "no Phase 2 tab/folder content", not
    /// an error.</summary>
    public sealed class ReportSet
    {
        public ReportTables WholeMatch = new ReportTables();
        public ReportTables Phase1 = new ReportTables();
        public ReportTables Phase2;

        /// <summary>Carried straight from PhaseTimeline.LiveSeconds/WentLive/TransitionSeconds. HtmlReportWriter reads these instead of
        /// re-deriving the transition instant from Phase1's MatchLengthSeconds, which broke once Phase 1 stopped starting at 0 for a
        /// new-style log (see HtmlReportWriter.Build).</summary>
        public double LiveSeconds;
        public bool WentLive;
        public double? TransitionSeconds;
    }
}
