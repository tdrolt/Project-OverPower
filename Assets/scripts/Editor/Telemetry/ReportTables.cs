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
        /// <summary>Empty when this actor's own nick is unknown - which happens exactly when
        /// <see cref="FilePresent"/> is false: nobody else's log line carries another actor's nickname.</summary>
        public string Nick = "";
        /// <summary>Whether a `session` line (so, a whole log file) for this actor was found.</summary>
        public bool FilePresent;
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

        /// <summary>The primary session's own tuning snapshot, re-serialized flat - T6's HTML report
        /// embeds this verbatim; T5 just carries it through.</summary>
        public string TuningJson;
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
    }
}
