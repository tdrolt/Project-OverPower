using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T5 step 5: writes the 12 CSVs from a ReportTables. Formatting only - every number
    /// already came out of TelemetryAggregator, so CsvReportWriter and (later) HtmlReportWriter can
    /// never disagree about what a value is, only about how it's presented.
    ///
    /// RFC-4180: invariant culture, CRLF line endings, a field quoted only when it contains a comma,
    /// a quote or a newline, with inner quotes doubled.
    ///
    /// Opus review item 13: written UTF-8 WITH a BOM so Excel auto-detects the encoding and the
    /// comma as a field separator on double-click; a Dutch-locale Windows install still defaults
    /// Excel's own list separator to semicolon, so a Dutch reader may still need Data > From Text/CSV
    /// and choose "Comma" explicitly rather than double-clicking the file directly - the BOM only
    /// fixes character encoding, not that regional setting.</summary>
    public static class CsvReportWriter
    {
        /// <summary>The original, pre-T7 entry point - unchanged: exactly the 12 CSVs, straight into
        /// folder/csv/. Kept for direct single-scope use (and CsvReportWriterTests, which counts
        /// exactly 12 files) - the T7 three-folder layout is the separate Write(ReportSet, ...)
        /// overload below, which calls this same per-table writing through WriteTables.</summary>
        public static void Write(ReportTables tables, string folder)
        {
            WriteTables(tables, Path.Combine(folder, "csv"));
        }

        /// <summary>Task T7: one folder per scope - csv/whole_match/, csv/phase1_3teams/ and, if the
        /// match ever had one, csv/phase2_2teams/ - each with the same 12 files as the single-scope
        /// overload above (no second copy of the writing logic - see WriteTables), plus a 13th file
        /// in whole_match only: log_coverage.csv (which players' logs are actually in this report -
        /// a whole-match fact, not scoped to one phase - see LogCoverageRow's own comment).</summary>
        public static void Write(ReportSet reportSet, string folder)
        {
            if (reportSet == null) return;

            string csvRoot = Path.Combine(folder, "csv");

            string wholeMatchFolder = Path.Combine(csvRoot, "whole_match");
            WriteTables(reportSet.WholeMatch, wholeMatchFolder);
            WriteLogCoverage(reportSet.WholeMatch?.Header?.LogCoverage, wholeMatchFolder);

            WriteTables(reportSet.Phase1, Path.Combine(csvRoot, "phase1_3teams"));

            if (reportSet.Phase2 != null)
                WriteTables(reportSet.Phase2, Path.Combine(csvRoot, "phase2_2teams"));
        }

        private static void WriteTables(ReportTables tables, string csvFolder)
        {
            Directory.CreateDirectory(csvFolder);

            WriteGoldTimeline(tables, csvFolder);
            WriteEconomyByMinute(tables, csvFolder);
            WriteZoneIncome(tables, csvFolder);
            WriteOwnership(tables, csvFolder);
            WriteCaptures(tables, csvFolder);
            WritePurchases(tables, csvFolder);
            WriteShopBlocked(tables, csvFolder);
            WriteHits(tables, csvFolder);
            WriteWeapons(tables, csvFolder);
            WriteAbilities(tables, csvFolder);
            WritePlayers(tables, csvFolder);
            WriteDeaths(tables, csvFolder);
        }

        /// <summary>Task T7, the 13th file - whole_match only (see Write(ReportSet, ...)'s own
        /// comment). One row per actor seen anywhere in the match; "present" is whether their own log
        /// file was found.</summary>
        private static void WriteLogCoverage(System.Collections.Generic.List<LogCoverageRow> rows, string csvFolder)
        {
            rows ??= new System.Collections.Generic.List<LogCoverageRow>();
            Directory.CreateDirectory(csvFolder);
            WriteCsv(
                Path.Combine(csvFolder, "log_coverage.csv"),
                new[] { "actor", "name", "filePresent", "firstT", "lastT" },
                // Review fix (item 10): a missing actor with no join/leave info at all writes "-"
                // for firstT/lastT rather than a misleading "0".
                rows.Select(r => new[]
                {
                    N(r.Actor), r.Nick, N(r.FilePresent),
                    r.FirstT.HasValue ? N(r.FirstT.Value) : "-",
                    r.LastT.HasValue ? N(r.LastT.Value) : "-",
                }));
        }

        // Review fix (item 5): the plan's own "the whole-match tables also get a Phase column on
        // time-based rows" was implemented on ReportTables/the HTML's embedded JSON but never
        // reached the CSVs - added to the same 8 row types that carry a Phase field. Written for
        // every scope (not just whole_match), since WriteTables is the one shared writing path for
        // all three folders (no second copy) - in a phase-scoped folder every row simply reads the
        // same constant phase, which still lets a spreadsheet pivot/filter uniformly across all
        // three CSVs by this one column instead of needing scope-specific logic.
        private static void WriteGoldTimeline(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "gold_timeline.csv"),
            new[] { "t", "actor", "name", "team", "balance", "earnedSoFar", "spentSoFar", "phase" },
            t.GoldTimeline.Select(r => new[]
            {
                N(r.T), N(r.Actor), r.Nick, N(r.Team), N(r.Balance), N(r.EarnedSoFar), N(r.SpentSoFar), N(r.Phase),
            }));

        private static void WriteEconomyByMinute(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "economy_by_minute.csv"),
            new[] { "minute", "team", "incomeTier1", "incomeTier2", "incomeTier3", "incomeTier4", "incomeUnattributed",
                    "bounty", "refunds", "spent", "zonesTier1", "zonesTier2", "zonesTier3", "zonesTier4", "goldGapToRichest", "phase" },
            t.EconomyByMinute.Select(r => new[]
            {
                N(r.Minute), N(r.Team),
                N(r.IncomeByTier[0]), N(r.IncomeByTier[1]), N(r.IncomeByTier[2]), N(r.IncomeByTier[3]), N(r.UnattributedIncome),
                N(r.Bounty), N(r.Refund), N(r.Spent),
                N(r.ZonesHeldByTier[0]), N(r.ZonesHeldByTier[1]), N(r.ZonesHeldByTier[2]), N(r.ZonesHeldByTier[3]),
                N(r.GoldGapToRichest), N(r.Phase),
            }));

        private static void WriteZoneIncome(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "zone_income.csv"),
            new[] { "zone", "team", "tier", "secondsHeld", "goldGenerated" },
            t.ZoneIncome.Select(r => new[] { N(r.Zone), N(r.Team), N(r.Tier), N(r.SecondsHeld), N(r.GoldGenerated) }));

        private static void WriteOwnership(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "ownership.csv"),
            new[] { "zone", "tier", "team", "from", "to", "duration", "howEnded", "phase" },
            t.Ownership.Select(r => new[] { N(r.Zone), N(r.Tier), N(r.Team), N(r.From), N(r.To), N(r.Duration), r.HowEnded, N(r.Phase) }));

        private static void WriteCaptures(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "captures.csv"),
            new[] { "zone", "tier", "team", "start", "end", "outcome", "duration", "players", "phase" },
            t.Captures.Select(r => new[] { N(r.Zone), N(r.Tier), N(r.Team), N(r.Start), N(r.End), r.Outcome, N(r.Duration), N(r.Players), N(r.Phase) }));

        private static void WritePurchases(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "purchases.csv"),
            new[] { "t", "actor", "name", "team", "kind", "category", "item", "price", "balanceAfter", "zone", "free", "phase" },
            t.Purchases.Select(r => new[]
            {
                N(r.T), N(r.Actor), r.Nick, N(r.Team), r.Kind, r.Category, N(r.ItemId), N(r.Amount), N(r.BalanceAfter), N(r.Zone), N(r.Free), N(r.Phase),
            }));

        private static void WriteShopBlocked(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "shop_blocked.csv"),
            new[] { "t", "actor", "name", "item", "price", "reason", "shortfall", "zone", "phase" },
            t.ShopBlocked.Select(r => new[] { N(r.T), N(r.Actor), r.Nick, N(r.ItemId), N(r.Price), r.Reason, N(r.Shortfall), N(r.Zone), N(r.Phase) }));

        private static void WriteHits(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "hits.csv"),
            // mark (mark plan step 6): appended at the END, found by header name like every other
            // column here - 0 for a non-marking hit (or any hit line written before this step existed).
            new[] { "t", "attacker", "attackerTeam", "victim", "victimTeam", "weapon", "ability", "source",
                    "raw", "armor", "healthLost", "lethal", "distance", "vulnerable", "overpower", "phase", "mark" },
            t.Hits.Select(r => new[]
            {
                N(r.T), N(r.Attacker), N(r.AttackerTeam), N(r.Victim), N(r.VictimTeam), N(r.Weapon), N(r.Ability), r.Source,
                N(r.Raw), N(r.Armor), N(r.HealthLost), N(r.Lethal), r.Distance.HasValue ? N(r.Distance.Value) : "",
                N(r.Vulnerable), N(r.Overpower), N(r.Phase), N(r.Mark),
            }));

        private static void WriteWeapons(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "weapons.csv"),
            // splashHits (opus review item 6): counted separately from hits/accuracy, which are
            // Projectile-source only - a rocket's own splash falloff no longer inflates accuracy past 100%.
            // marksPlaced/marksCashed (mark plan step 6): appended at the END, found by header name.
            new[] { "weapon", "timeEquippedSeconds", "pulls", "projectiles", "hits", "splashHits", "accuracy",
                    "damageRaw", "armorDamage", "healthDamage", "damagePerEquippedMinute", "kills", "meanDistance", "medianDistance",
                    "marksPlaced", "marksCashed" },
            t.Weapons.Select(r => new[]
            {
                N(r.WeaponId), N(r.TimeEquippedSeconds), N(r.Pulls), N(r.Projectiles), N(r.Hits), N(r.SplashHits), N(r.Accuracy),
                N(r.DamageRaw), N(r.ArmorDamage), N(r.HealthDamage), N(r.DamagePerEquippedMinute), N(r.Kills),
                r.MeanDistance.HasValue ? N(r.MeanDistance.Value) : "",
                r.MedianDistance.HasValue ? N(r.MedianDistance.Value) : "",
                N(r.MarksPlaced), N(r.MarksCashed),
            }));

        private static void WriteAbilities(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "abilities.csv"),
            new[] { "ability", "casts", "damageRaw", "kills", "statusCount", "statusSeconds" },
            t.Abilities.Select(r => new[] { N(r.AbilityId), N(r.Casts), N(r.DamageRaw), N(r.Kills), N(r.StatusCount), N(r.StatusSeconds) }));

        private static void WritePlayers(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "players.csv"),
            new[] { "actor", "name", "team", "kills", "deaths", "assists", "damageDealt", "damageTaken",
                    "goldTerritory", "goldBounty", "goldRefund", "goldDebug", "goldOther", "goldSpent",
                    "timeAlive", "timeOwnZone", "timeEnemyZone", "timeNeutralZone", "healing" },
            t.Players.Select(r => new[]
            {
                N(r.Actor), r.Nick, N(r.Team), N(r.Kills), N(r.Deaths), N(r.Assists), N(r.DamageDealt), N(r.DamageTaken),
                N(r.GoldTerritory), N(r.GoldBounty), N(r.GoldRefund), N(r.GoldDebug), N(r.GoldOther), N(r.GoldSpent),
                N(r.TimeAlive), N(r.TimeOwnZone), N(r.TimeEnemyZone), N(r.TimeNeutralZone), N(r.Healing),
            }));

        private static void WriteDeaths(ReportTables t, string folder) => WriteCsv(
            Path.Combine(folder, "deaths.csv"),
            new[] { "t", "victim", "victimName", "victimTeam", "killer", "killerTeam", "assists", "weapon", "ability",
                    "x", "z", "unspentGold", "loadoutWeapon", "loadoutEquipment", "loadoutMobility", "loadoutUltimate",
                    "absorbLevel", "rechargeLevel", "phase" },
            t.Deaths.Select(r => new[]
            {
                N(r.T), N(r.Victim), r.VictimNick, N(r.VictimTeam), N(r.Killer), N(r.KillerTeam),
                string.Join(";", r.Assists), N(r.Weapon), N(r.Ability), N(r.X), N(r.Z), N(r.UnspentGold),
                N(r.LoadoutWeapon), N(r.LoadoutEquipment), N(r.LoadoutMobility), N(r.LoadoutUltimate),
                N(r.AbsorbLevel), N(r.RechargeLevel), N(r.Phase),
            }));

        // ---------------------------------------------------------------- CSV mechanics

        private static void WriteCsv(string path, string[] header, IEnumerable<string[]> rows)
        {
            var sb = new StringBuilder();
            AppendRow(sb, header);
            foreach (string[] row in rows)
                AppendRow(sb, row);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void AppendRow(StringBuilder sb, string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(fields[i]));
            }
            sb.Append("\r\n");
        }

        private static string Escape(string field)
        {
            field ??= "";
            bool needsQuote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuote) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string N(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string N(bool value) => value ? "true" : "false";
    }
}
