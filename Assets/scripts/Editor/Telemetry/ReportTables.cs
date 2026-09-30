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

    /// <summary>Task T7: one row per actor seen ANYWHERE in the match - joins, sessions, `hit`
    /// attackers/victims, `death` killers/assists - regardless of whether that actor's own log file is
    /// actually present in this folder. Whole-match only (not scoped to a phase - "which logs are in
    /// this report" is a fact about the folder, not about a time window); the header's `log-coverage`
    /// container and csv/whole_match/log_coverage.csv both read this same list.</summary>
    public sealed class LogCoverageRow
    {
        public int Actor;
        /// <summary>Review fix (item 10): for a missing actor (no file of their own), this now
        /// comes from another client's own `join` line, which carries the nick - empty only when
        /// no one's `join` line for this actor was found either (an old log written before that
        /// field existed, or an actor only ever seen via a `hit`/`death` line, never a `join`).</summary>
        public string Nick = "";
        /// <summary>Whether a `session` line (so, a whole log file) for this actor was found.</summary>
        public bool FilePresent;
        /// <summary>Review fix (item 10): null (renders as "-") rather than 0 when there is nothing
        /// to show - a missing actor's own First/Last t come from their `join`/`leave` events (logged
        /// by whichever OTHER client saw them), not from a file they never wrote.</summary>
        public double? FirstT;
        public double? LastT;
        /// <summary>Review fix (item 10): true for a missing actor whose `join` AND `leave` were both
        /// seen (by other clients) - they were very likely gone again before their own telemetry
        /// file ever opened, which is a much softer story than "no log from them at all" and gets its
        /// own, gentler warning wording.</summary>
        public bool JoinedAndLeftBeforeLoggingStarted;
    }

    /// <summary>Playtest extras Task 2 (P4): one console line inside a bug card's own window (20s
    /// before to 5s after the mark) - every client's, merged and sorted by time, each one labelled
    /// with which player it came from (a console line has no actor field of its own; the file it
    /// came from IS the player - see TelemetryAggregator.BuildBugsAndConsole).</summary>
    public sealed class ConsoleLineRef
    {
        public double T;
        public int Actor;
        public string Nick = "";
        /// <summary>"log"/"warning"/"error"/"exception"/"assert" - never "dropped" (a summary line,
        /// never a real message, is left out of every bug window).</summary>
        public string Level = "";
        public string Message = "";
        /// <summary>This line's own fold count (TelemetryKeys.RepeatCount) - 1 for a line nothing
        /// else folded into.</summary>
        public int Count = 1;
    }

    /// <summary>Playtest extras Task 2 (P4): one card for the "Bug reports" section - one row per
    /// `bug` line (Ctrl+B), enriched with the reporter's own chat note(s) and every client's nearby
    /// console lines. Whole-match only (see ReportHeader.Bugs' own comment) - like LogCoverageRow,
    /// computed the same way regardless of which scope's own Build call filled it.</summary>
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
        public int Equipment;
        public int Mobility;
        public int Ultimate;
        /// <summary>The screenshot's own FILE NAME (TelemetryKeys.ScreenshotFile) - never a path.
        /// The HTML links it relative to the report, which sits in the same match folder the
        /// screenshot itself was saved into (BugMarkerKey), so a bare file name already resolves
        /// correctly there, including once the whole folder is zipped and opened elsewhere.</summary>
        public string ScreenshotFile = "";
        public int Phase = 1;
        /// <summary>The reporter's OWN chat text(s), 0-60s after the mark - never another player's
        /// (P4). Empty when they never said anything in that window.</summary>
        public List<string> ChatNotes = new List<string>();
        /// <summary>Every client's console lines from 20s before to 5s after the mark, merged and
        /// sorted by time (P4) - every level, including plain "log" (P4's own wording: "plain log
        /// lines only appear inside bug windows" - this is that one place).</summary>
        public List<ConsoleLineRef> ConsoleWindow = new List<ConsoleLineRef>();
    }

    /// <summary>Playtest extras Task 2 (P4): one row of the per-player "Console" section - every
    /// error/exception/warning a player's own file logged, grouped by its exact message, with how
    /// many times it happened and the first/last time. A plain "log" line is never grouped here (P4:
    /// "plain log lines only appear inside bug windows" - see BugRow.ConsoleWindow); a "dropped"
    /// summary line is not a real message either, so it is left out too. Whole-match only.</summary>
    public sealed class ConsolePlayerGroupRow
    {
        public int Actor;
        public string Nick = "";
        public string Level = "";
        public string Message = "";
        /// <summary>Summed across every separate fold bucket (TelemetryKeys.RepeatCount) that ever
        /// matched this (actor, level, message) - not just how many `console` JSONL lines matched,
        /// since one line can itself already represent several real repeats.</summary>
        public int Count;
        public double FirstT;
        public double LastT;
    }

    /// <summary>Everything that isn't one of the 12 tables: match-wide facts and the quality counters
    /// the spec's Error handling section asks for (malformed/unknown lines are never a failure, just a
    /// number in the header).</summary>
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
        /// <summary>Task T7: see LogCoverageRow's own comment - always the whole match's list, even on
        /// a Phase 1/Phase 2-scoped ReportTables (CsvReportWriter only ever writes the CSV of this into
        /// csv/whole_match/, and the HTML header only ever reads it once, from whichever scope it
        /// happens to render first - the values are identical across every scope).</summary>
        public List<LogCoverageRow> LogCoverage = new List<LogCoverageRow>();
        public int MalformedLineCount;
        public int UnknownEventCount;
        public int UnknownCaptureStateCount;
        public string OtherMatchId;
        public int OtherMatchFileCount;
        /// <summary>Opus review fixes (item 10): quality counters that used to either crash or pass
        /// silently.</summary>
        public int UnreadableFileCount;
        public int NewerSchemaCount;
        /// <summary>An `ownership` line whose own tier was outside 1..4 (a bad id, or a zone not yet
        /// registered) - skipped rather than indexing out of range, and counted here.</summary>
        public int InvalidTierCount;

        /// <summary>Review fix (item 9): true when the match had an `elimination` event but no
        /// `phase` >= 2 event at all - PhaseTimeline.UsedEliminationFallback, carried through so the
        /// HTML can warn that the Phase 2 start came from the elimination instead of a real phase
        /// change (2.7's MatchDirector should be logging both).</summary>
        public bool EliminationFallbackUsed;

        /// <summary>2.7b step 9: how long the warm-up lasted before this match went live
        /// (PhaseTimeline.LiveSeconds) - 0 for a legacy log, which never had a warm-up to measure. Set
        /// from the SAME match-wide PhaseTimeline on every scope's own header, like
        /// EliminationFallbackUsed - not this scope's own (possibly warm-up-excluding) window.</summary>
        public double WarmupSeconds;

        /// <summary>2.7b step 9: true for a new-style log (one with its own `phase` 0 warm-up anchor)
        /// whose match never actually went live - PhaseTimeline.HasWarmup &amp;&amp; !WentLive. The HTML
        /// shows a red warning instead of the ordinary warm-up line, and every table is empty (Phase 1/2
        /// collapse to nothing - see PhaseTimeline's own "never went live" windows).</summary>
        public bool NeverWentLive;

        /// <summary>The primary session's own tuning snapshot, re-serialized flat - T6's HTML report
        /// embeds this verbatim; T5 just carries it through.</summary>
        public string TuningJson;

        /// <summary>Playtest extras Task 2 (P4): the "Bug reports" section's own rows - see BugRow's
        /// own comment on why this is always the whole match's list, like LogCoverage above.</summary>
        public List<BugRow> Bugs = new List<BugRow>();

        /// <summary>Playtest extras Task 2 (P4): the per-player "Console" section's own rows - see
        /// ConsolePlayerGroupRow's own comment.</summary>
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
        /// <summary>Task T7: 1 or 2 - which phase this sample's own t falls in (see
        /// TelemetryAggregator's phase-tagging). Always 1 when the match never had a phase 2.</summary>
        public int Phase = 1;
    }

    public sealed class EconomyByMinuteRow
    {
        public int Minute;
        public int Team;
        /// <summary>Index 0..3 = tier 1..4. Double, not int (opus review fix item 4): a `goldEarned`
        /// line's own `zones` array is rounded to whole gold per zone at the SOURCE, so summing many
        /// already-rounded lines drifted noticeably from the authoritative `terr` total (a real log
        /// measured Sigma-terr 254 vs Sigma-zones 244, -4%) - each line is rescaled to sum to its own
        /// `terr` before accumulating (see TelemetryAggregator.ScaledZones), so keeping this a double
        /// preserves that correction instead of re-rounding it away immediately.</summary>
        public double[] IncomeByTier = new double[4];
        public int Bounty;
        public int Refund;
        public int Spent;
        /// <summary>Index 0..3 = tier 1..4.</summary>
        public int[] ZonesHeldByTier = new int[4];
        public int GoldGapToRichest;
        /// <summary>Task T7: which phase this minute bucket belongs to, from the bucket's own START
        /// time vs the transition - see TelemetryAggregator's phase-tagging. The minute INDEX itself
        /// stays the absolute match minute (not renumbered relative to the phase start) even on a
        /// Phase 2-scoped ReportTables - see TelemetryAggregator.BuildGoldTimelineAndEconomy's own
        /// comment on why this table is the one that isn't windowed as cleanly as the rest.</summary>
        public int Phase = 1;
        /// <summary>T5 re-review fix (item 9): a `goldEarned` line can have `terr` > 0 (real
        /// territory gold, already counted in players.csv via its running total) with `zones`
        /// empty or summing to 0 - ScaledZones has no ratio to rescale by in that case, so the whole
        /// amount used to vanish from every zone-keyed table instead of landing in some zone's tier.
        /// Kept here instead, so team/tier income totals still add up to what players.csv reports.</summary>
        public double UnattributedIncome;
    }

    public sealed class ZoneIncomeRow
    {
        public int Zone;
        public int Team;
        public int Tier;
        public double SecondsHeld;
        /// <summary>Double, not int - see EconomyByMinuteRow.IncomeByTier's own comment on why the
        /// per-line rescale needs to keep its fraction instead of re-rounding every interval.</summary>
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
        /// <summary>"captured" (another team took it), "decayed" (it went neutral), "matchEnd"
        /// (still held when the log ends), or "phaseBoundary" (Task T7: this row is a phase-clipped
        /// PIECE of a stint that actually continues past this window's own end - the real reason lives
        /// on the OTHER piece, whichever window that lands in).</summary>
        public string HowEnded;
        /// <summary>Task T7: 1 or 2 - which phase this (possibly clipped) piece of the stint belongs
        /// to. A stint that crosses the phase boundary is split into two rows, one per phase, even on
        /// the whole-match ReportTables - see TelemetryAggregator.BuildOwnership.</summary>
        public int Phase = 1;
    }

    public sealed class CaptureRow
    {
        public int Zone;
        /// <summary>T6 review fix: the zone's tier at the time of this capture attempt (from the
        /// same ownership-derived zoneTier map BuildOwnership already builds) - 0 if this zone's
        /// tier was never established by an ownership line.</summary>
        public int Tier;
        public int Team;
        public double Start;
        public double End;
        /// <summary>"completed" / "neutralised" (from the matching `ownership` change - never trusted
        /// from the `capture` line's own state string, which the T4 review found unreliable and
        /// which is stateless as of the T4 fix: "paused"/"drainPaused" is reported for BOTH a genuine
        /// pause and a completion/neutralisation, at whatever progress it actually stopped at) or
        /// "abandoned" - either still open when the log ends, or superseded mid-match by a fresh
        /// `started`/`drainStarted` for a different team or direction before this one ever closed - or
        /// "phaseBoundary" (Task T7, same meaning as OwnershipRow.HowEnded's own "phaseBoundary").</summary>
        public string Outcome;
        public double Duration;
        public int Players;
        /// <summary>Task T7: see OwnershipRow.Phase's own comment - same mechanism, same reason.</summary>
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
        /// <summary>Task T7: 1 or 2, from this purchase/refund's own t.</summary>
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
        /// <summary>Task T7: 1 or 2, from this blocked click's own t.</summary>
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
        /// <summary>Mark plan step 6: 0 (no key on the raw line - a non-marking hit) / 1 (Applied) / 2
        /// (Cashed). Read with a default of 0, matching every hit line written before this field
        /// existed.</summary>
        public int Mark;
        /// <summary>Task T7: 1 or 2, from this hit's own t.</summary>
        public int Phase = 1;
    }

    public sealed class WeaponRow
    {
        public int WeaponId;
        public double TimeEquippedSeconds;
        public int Pulls;
        public int Projectiles;
        /// <summary>Discrete `Projectile`-source hits only. Accuracy is Hits/Projectiles - a
        /// splash-damage weapon's own splash rows are counted separately (<see cref="SplashHits"/>)
        /// so one rocket landing (1 direct hit + N splash hits on nearby targets) can no longer push
        /// accuracy over 100% (opus review fix item 6).</summary>
        public int Hits;
        /// <summary>`Splash`-source hits - counted, but never divided into Projectiles for accuracy
        /// (a splash row isn't "a projectile that connected", it's damage a DIFFERENT connecting
        /// projectile happened to also deal).</summary>
        public int SplashHits;
        public double Accuracy;
        public float DamageRaw;
        public float ArmorDamage;
        public float HealthDamage;
        public double DamagePerEquippedMinute;
        public int Kills;
        public double? MeanDistance;
        public double? MedianDistance;
        /// <summary>Mark plan step 6: how many hits from THIS weapon placed a fresh mark (HitRow.Mark
        /// == 1) in the window. Answers "how often do players cash the mark?" alongside MarksCashed
        /// below (Decision 18).</summary>
        public int MarksPlaced;
        /// <summary>Mark plan step 6: how many hits from THIS weapon cashed an existing mark in
        /// (HitRow.Mark == 2) in the window.</summary>
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
        public int LoadoutEquipment;
        public int LoadoutMobility;
        public int LoadoutUltimate;
        public int AbsorbLevel;
        public int RechargeLevel;
        /// <summary>Task T7: 1 or 2, from this death's own t.</summary>
        public int Phase = 1;
    }

    /// <summary>Task T5: the whole report as plain data - one property per CSV, plus the header.
    /// CsvReportWriter (T5) and HtmlReportWriter (T6) both just format these; TelemetryAggregator is
    /// the only place that computes them, so the two outputs can never disagree (design doc, Outputs).</summary>
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

    /// <summary>Task T7: the three scopes a match report is built for - see
    /// TelemetryAggregator.BuildSet and PhaseTimeline. Phase2 is null for every log that never
    /// eliminates a team (every log before Task 2.7 ships, and any match that ends 3-team) - CsvReportWriter
    /// and HtmlReportWriter both treat that as "no Phase 2 tab/folder content", not an error.</summary>
    public sealed class ReportSet
    {
        public ReportTables WholeMatch = new ReportTables();
        public ReportTables Phase1 = new ReportTables();
        public ReportTables Phase2;

        /// <summary>2.7b step 9: carried straight from PhaseTimeline.LiveSeconds/WentLive/TransitionSeconds -
        /// HtmlReportWriter reads these instead of re-deriving the transition instant from Phase1's own
        /// MatchLengthSeconds, which broke once Phase 1 stopped starting at 0 for a new-style log (see
        /// HtmlReportWriter.Build's own comment).</summary>
        public double LiveSeconds;
        public bool WentLive;
        public double? TransitionSeconds;
    }
}
