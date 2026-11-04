using System.Collections.Generic;
using Newtonsoft.Json;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Everything the page needs beyond the ReportTables: which mode and map, the players with their bot tags, the tabs and the sections
    /// each shows, the settings that matter for the mode, and the mode's own sections' numbers. ModeReportBuilder fills it; HtmlReportWriter
    /// embeds it, CsvReportWriter.WriteModeExtras writes it out.</summary>
    public sealed class ModeReport
    {
        [JsonIgnore] public ReportMode Mode;
        /// <summary>The round and sudden-death tabs' own tables, by tab key (the whole match and the phases are the ReportSet's).</summary>
        [JsonIgnore] public Dictionary<string, ReportTables> Scopes = new Dictionary<string, ReportTables>();

        public string ModeKey = "";
        public string ModeName = "";
        public bool Dominion;
        public string MapName = "";
        public int[] Teams = System.Array.Empty<int>();
        public int TeamSize;
        public string Title = "";
        public string PlayerCount = "";
        public double LiveSeconds;
        public double LengthSeconds;
        public bool ShowsOverPower;
        public bool ShowsGold;
        public List<RosterEntry> Roster = new List<RosterEntry>();
        public List<ReportTab> Tabs = new List<ReportTab>();
        /// <summary>[label, value] pairs: the settings that matter for this mode.</summary>
        public List<string[]> Settings = new List<string[]>();
        /// <summary>One warning line naming every player whose log is missing; "" when every log is here.</summary>
        public string MissingLogs = "";
        /// <summary>Zones that never play in this mode (a two-team Conquest's closed corner): left out of every zone table and chart.</summary>
        public List<int> HiddenZones = new List<int>();
        public DominionData DominionReport;
        public ConquestData ConquestReport;
    }

    public sealed class RosterEntry
    {
        public int Actor;
        public string Nick = "";
        public int Team = -1;
        public bool Bot;
        public string Build = "";
    }

    /// <summary>One tab: its key (the page's scope), its label, its window in match seconds and the sections it shows, in order.</summary>
    public sealed class ReportTab
    {
        public string Key = "";
        public string Label = "";
        public double Start;
        public double End;
        /// <summary>Times on this tab count from its own start (a round's tab: "1:20 into round 2").</summary>
        public bool TimesFromStart;
        public List<string> Sections = new List<string>();
    }

    public sealed class DominionData
    {
        public int Winner = -1;
        public Dictionary<int, int> Wins = new Dictionary<int, int>();
        public string HowEnded = "";
        public bool HasCentre;
        public List<RoundRow> Rounds = new List<RoundRow>();
        public List<PointsRace> Races = new List<PointsRace>();
        public List<ZoneHoldRow> ZoneHolds = new List<ZoneHoldRow>();
        public List<ZoneRoundRow> ZoneRounds = new List<ZoneRoundRow>();
        public List<DominionPayout> Bounties = new List<DominionPayout>();
        public List<DominionPayout> CentrePayouts = new List<DominionPayout>();
        public List<ShieldEnd> Shields = new List<ShieldEnd>();
        public List<BuildRow> Builds = new List<BuildRow>();
        public List<PlayerExtra> PlayerExtras = new List<PlayerExtra>();
        public SuddenDeathData SuddenDeath;
    }

    public sealed class RoundRow
    {
        public int Number;
        public int[] Winners = System.Array.Empty<int>();
        public string Outcome = "";
        public Dictionary<int, int> Points = new Dictionary<int, int>();
        /// <summary>The leader's points minus the next team's.</summary>
        public int Margin;
        public double? StartT;
        public double? EndT;
        public double LengthSeconds;
        public bool Overtime;
        public double OvertimeSeconds;
    }

    /// <summary>One round's points race: each team's points over the round (seconds into the round, points), the bounties and centre payouts on
    /// the way, and where each team's points came from. Logged is false for a log from before the points were logged as they changed (then the
    /// line runs straight from 0 to the round's end).</summary>
    public sealed class PointsRace
    {
        public int Round;
        public double StartT;
        public double EndT;
        public bool Logged;
        public List<PointsSeries> Series = new List<PointsSeries>();
        public List<RaceEvent> Events = new List<RaceEvent>();
        public List<PointsSource> Sources = new List<PointsSource>();
    }

    public sealed class PointsSeries
    {
        public int Team;
        public List<double[]> Points = new List<double[]>();
    }

    public sealed class RaceEvent
    {
        public double At;
        public int Team;
        /// <summary>"bounty" or "centre".</summary>
        public string Kind = "";
        public int Points;
    }

    public sealed class PointsSource
    {
        public int Team;
        public int Zones;
        public int Bounties;
        public int Centre;
        public int Total;
    }

    public sealed class ZoneHoldRow
    {
        public int Round;
        public int Zone;
        public int Team;
        public double Seconds;
    }

    public sealed class ZoneRoundRow
    {
        public int Round;
        public int Zone;
        /// <summary>How many times a team took the zone during the round.</summary>
        public int Captures;
        /// <summary>Seconds from the round's start to the zone's first capture; null when nobody took it.</summary>
        public double? FirstCaptureAfter;
        public double RoundSeconds;
    }

    public sealed class ShieldEnd
    {
        public double T;
        public int Actor;
        public int Team = -1;
        public string Reason = "";
        public int Blocked;
    }

    /// <summary>What a player carried in a round: the loadout they held for most of the round while alive.</summary>
    public sealed class BuildRow
    {
        public int Round;
        public int Actor;
        public int Team = -1;
        public int Weapon = -1;
        public int Attachment = -1;
        public int Mobility = -1;
        public int Ultimate = -1;
        public int Absorb;
        public int Recharge;
    }

    /// <summary>Per tab and player: seconds standing on a scoring zone while alive, and seconds dead.</summary>
    public sealed class PlayerExtra
    {
        public string Scope = "";
        public int Actor;
        public double ScoringZoneSeconds;
        public double DeadSeconds;
    }

    public sealed class SuddenDeathData
    {
        public double StartT;
        public double? EndT;
        public double LengthSeconds;
        public int[] Teams = System.Array.Empty<int>();
        public int Replays;
        public int Winner = -1;
        public int ForRound;
        public List<int> CircleKills = new List<int>();
        /// <summary>[victim, killer] pairs.</summary>
        public List<int[]> PlayerKills = new List<int[]>();
        /// <summary>Damage the circle did, by player.</summary>
        public Dictionary<int, int> CircleDamage = new Dictionary<int, int>();
    }

    public sealed class ConquestData
    {
        public int Winner = -1;
        public List<ScanWave> ScanWaves = new List<ScanWave>();
        public ShrinkInfo Shrink;
    }

    public sealed class ScanWave
    {
        public double StartT;
        public int Holder = -1;
        /// <summary>Enemies the holder's team spotted; -1 when no client of the holder's team logged the wave.</summary>
        public int Caught = -1;
    }

    public sealed class ShrinkInfo
    {
        public double T;
        public int Team = -1;
        public int[] Zones = System.Array.Empty<int>();
    }
}
