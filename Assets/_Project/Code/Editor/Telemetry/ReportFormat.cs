using System;
using System.Globalization;
using Overpower.Data;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>How the report writes a number. Never decimals of a second; damage, healing, points and gold are whole numbers; shares are
    /// percentages. The page's own script (HtmlReportWriter: clockText, len, pct, whole) writes the same, so a number reads alike in a C#-built
    /// line and a table the page builds.</summary>
    public static class ReportFormat
    {
        /// <summary>Match time: m:ss from <paramref name="zeroSeconds"/> (the moment the match went live, or a round's start). Before it, a
        /// minus sign ("-0:05" five seconds before going live). Whole seconds, counted down (1:59.8 reads 1:59).</summary>
        public static string Clock(double t, double zeroSeconds)
        {
            double relative = t - zeroSeconds;
            if (double.IsNaN(relative) || double.IsInfinity(relative)) return "";
            bool before = relative < 0;
            long total = before ? (long)Math.Ceiling(-relative) : (long)Math.Floor(relative);
            return (before ? "-" : "") + MinutesSeconds(total);
        }

        /// <summary>A length: "45 s" under a minute, m:ss from a minute on ("1:20"). Rounded to the nearest second; a negative length reads 0 s.</summary>
        public static string Length(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "";
            long total = (long)Math.Round(Math.Max(0, seconds), MidpointRounding.AwayFromZero);
            return total < 60 ? total.ToString(CultureInfo.InvariantCulture) + " s" : MinutesSeconds(total);
        }

        /// <summary>A share as a whole percentage: 0.375 reads "38%".</summary>
        public static string Percent(double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction)) return "";
            return Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>A whole number, rounded half away from zero: 12.5 reads "13".</summary>
        public static string Whole(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>The name of a bot's build (BotConfig: 0 Shotgun, 1 Burst, 2 Laser); "" for any other number.</summary>
        public static string BotBuildName(int build)
        {
            switch (build)
            {
                case BotConfig.Shotgun: return "Shotgun";
                case BotConfig.Burst: return "Burst";
                case BotConfig.Laser: return "Laser";
                default: return "";
            }
        }

        /// <summary>The header's count: "4 players: 0 humans, 4 bots" ("1 player: 1 human, 0 bots" for one).</summary>
        public static string PlayerCount(int humans, int bots)
        {
            int players = Math.Max(0, humans) + Math.Max(0, bots);
            return Count(players, "player", "players") + ": " + Count(humans, "human", "humans") + ", " + Count(bots, "bot", "bots");
        }

        private static string Count(int n, string one, string many) =>
            n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);

        private static string MinutesSeconds(long totalSeconds)
        {
            long minutes = totalSeconds / 60, seconds = totalSeconds % 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
