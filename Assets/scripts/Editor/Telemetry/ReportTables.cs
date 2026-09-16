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
        public int MalformedLineCount;
        public int UnknownEventCount;
        public int UnknownCaptureStateCount;
        public string OtherMatchId;
        public int OtherMatchFileCount;

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
    }

    public sealed class EconomyByMinuteRow
    {
        public int Minute;
        public int Team;
        /// <summary>Index 0..3 = tier 1..4.</summary>
        public int[] IncomeByTier = new int[4];
        public int Bounty;
        public int Refund;
        public int Spent;
        /// <summary>Index 0..3 = tier 1..4.</summary>
        public int[] ZonesHeldByTier = new int[4];
        public int GoldGapToRichest;
    }

    public sealed class ZoneIncomeRow
    {
        public int Zone;
        public int Team;
        public int Tier;
        public double SecondsHeld;
        public int GoldGenerated;
    }

    public sealed class OwnershipRow
    {
        public int Zone;
        public int Tier;
        public int Team;
        public double From;
        public double To;
        public double Duration;
        /// <summary>"captured" (another team took it), "decayed" (it went neutral), or "matchEnd"
        /// (still held when the log ends).</summary>
        public string HowEnded;
    }

    public sealed class CaptureRow
    {
        public int Zone;
        public int Team;
        public double Start;
        public double End;
        /// <summary>"completed" / "neutralised" (from the matching `ownership` change - never trusted
        /// from the `capture` line's own state string, which the T4 review found unreliable),
        /// "abandoned" (paused and never resumed by match end) or "interrupted" (still active when
        /// the log ends).</summary>
        public string Outcome;
        public double Duration;
        public int Players;
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
    }

    public sealed class WeaponRow
    {
        public int WeaponId;
        public double TimeEquippedSeconds;
        public int Pulls;
        public int Projectiles;
        public int Hits;
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
}
