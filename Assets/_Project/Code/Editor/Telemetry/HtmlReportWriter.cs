using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Overpower.Data;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>One self-contained report.html per match folder, told the way the match's mode is played (ModeReport): Conquest 3v3v3 with its
    /// Phase 1 / Phase 2 tabs, Conquest 3v3 without them, Dominion with a tab per round and for sudden death. Every number comes from the
    /// ReportSet, the mode report's own tables and the raw `sample` positions; CsvReportWriter formats the same data, and the stats the page
    /// leaves out (ReportVisibility) stay in the CSV files.
    ///
    /// Every tab renders through the same functions, parameterized by its key (the scope: which tables a read goes through, rowsFor) and the
    /// suffix every element id of its copy of the template carries. The tab's own section list (ReportTab.Sections) decides which sections it
    /// shows and in which order.
    ///
    /// All data is embedded as one JSON blob (Newtonsoft, StringEscapeHandling.EscapeHtml) so a player-provided string (nickname, marker note)
    /// can never break out of the embedding &lt;script&gt; tag; the page writes every such string with .textContent, never innerHTML.</summary>
    public static class HtmlReportWriter
    {
        /// <summary>A 9-player, 25-minute match at the shipped 5s sample interval would embed ~2700 samples PER PLAYER (24300 total)
        /// as raw JSON otherwise; capped so the report stays a reasonable size.</summary>
        private const int MaxPositionSamples = 3000;

        public static string Write(ReportSet reportSet, TelemetryLog log, BalanceTargetsData targets, ArenaReportRender.Result arena, string folder)
        {
            ReportMode mode = ModeReportBuilder.ModeOf(log);
            return Write(reportSet, log, targets, arena, folder, ModeReportBuilder.Build(log, mode, reportSet));
        }

        public static string Write(ReportSet reportSet, TelemetryLog log, BalanceTargetsData targets, ArenaReportRender.Result arena, string folder, ModeReport modeReport)
        {
            reportSet ??= new ReportSet();
            modeReport ??= ModeReportBuilder.Build(null, ModeReportBuilder.ModeOf(null), reportSet);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "report.html");
            List<PositionSample> positions = ExtractPositions(log, out int keepEveryN);
            string html = Build(reportSet, log, positions, keepEveryN, targets, arena, modeReport);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        private sealed class PositionSample
        {
            public double T;
            public int Actor;
            public int Team;
            public float X;
            public float Z;
            public bool Alive;
            /// <summary>1 or 2, from this sample's t. Positions are a single shared list in the payload (not one per tab, to keep the report
            /// small); the page's rowsFor('positions', scope) filters by phase or by the tab's window.</summary>
            public int Phase = 1;
        }

        /// <summary>Positions are only in the raw `sample` lines (not one of the CSVs), so this reads the log directly rather than
        /// going through TelemetryAggregator.
        ///
        /// Down-sampled to at most <see cref="MaxPositionSamples"/> kept points - grouped per actor (own file) first, so a long match
        /// doesn't lose a short-lived joiner's coverage to a global "keep every Nth line" cut; keeping every Nth sample WITHIN each
        /// player's sequence keeps spatial coverage roughly even instead of truncating to their earliest N samples.</summary>
        private static List<PositionSample> ExtractPositions(TelemetryLog log, out int keepEveryN)
        {
            keepEveryN = 1;
            var result = new List<PositionSample>();
            if (log == null) return result;

            PhaseTimeline timeline = PhaseTimeline.From(log);
            double? tPhase2 = timeline.TransitionSeconds;

            var fileActor = new Dictionary<string, int>();
            foreach (TelemetrySession s in log.Sessions)
                if (!fileActor.ContainsKey(s.File)) fileActor[s.File] = s.Actor;

            var byActor = new Dictionary<int, List<PositionSample>>();
            int total = 0;
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;
                // The report's windows start at going live: a warm-up position has no business on any tab's map.
                if (timeline.WentLive && e.T < timeline.LiveSeconds) continue;

                var xToken = e.Data[TelemetryKeys.X];
                var zToken = e.Data[TelemetryKeys.Z];
                if (xToken == null || zToken == null) continue; // recordPositions was off for this line

                int actor = fileActor.GetValueOrDefault(e.File, -1);
                if (!byActor.TryGetValue(actor, out List<PositionSample> list))
                    byActor[actor] = list = new List<PositionSample>();

                list.Add(new PositionSample
                {
                    T = e.T,
                    Actor = actor,
                    Team = e.Data[TelemetryKeys.Team]?.ToObject<int?>() ?? -1,
                    X = xToken.ToObject<float>(),
                    Z = zToken.ToObject<float>(),
                    Alive = e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true,
                    Phase = (tPhase2.HasValue && e.T >= tPhase2.Value) ? 2 : 1,
                });
                total++;
            }

            if (total > MaxPositionSamples)
                keepEveryN = (int)System.Math.Ceiling(total / (double)MaxPositionSamples);

            foreach (List<PositionSample> list in byActor.Values)
                for (int i = 0; i < list.Count; i += keepEveryN)
                    result.Add(list[i]);
            return result;
        }

        private static string Build(ReportSet reportSet, TelemetryLog log, List<PositionSample> positions, int positionsKeptEveryN,
                                    BalanceTargetsData targets, ArenaReportRender.Result arena, ModeReport modeReport)
        {
            ReportMap map = ReportModes.MapOf(modeReport.Mode);
            bool phases = ReportModes.HasPhaseTabs(modeReport.Mode); // a mode without phase tabs never reads them: left out, the page stays smaller
            var payload = new
            {
                phase1 = phases ? ScopePayload(reportSet.Phase1) : null,
                phase2 = phases && reportSet.Phase2 != null ? ScopePayload(reportSet.Phase2) : null,
                wholeMatch = ScopePayload(reportSet.WholeMatch),
                scopes = modeReport.Scopes.ToDictionary(pair => pair.Key, pair => ScopePayload(pair.Value)),
                transitionSeconds = reportSet.TransitionSeconds,
                positions,
                positionsKeptEveryN,
                targets = ReportModes.ShowsTargets(modeReport.Mode) ? targets : null,
                arena,
                // Names, never numbers: the teams, the map's own zones, weapons and abilities by name without their id.
                teamNames = Enumerable.Range(0, 3).Select(ArenaNames.TeamName).ToArray(),
                zoneNames = Enumerable.Range(0, ReportMaps.ZoneCount(map)).Select(zone => ReportMaps.ZoneName(map, zone)).ToArray(),
                weaponNames = Names(log, "weapons"),
                abilityNames = Names(log, "abilities"),
                hiddenStats = ReportVisibility.HiddenStats(),
                report = modeReport,
            };

            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };
            string json = JsonConvert.SerializeObject(payload, settings);
            json = EscapeLineTerminators(json);

            var sb = new StringBuilder(json.Length + 65536);
            sb.Append(HtmlHead);
            sb.Append("<script>\nconst DATA = ");
            sb.Append(json);
            sb.Append(";\n</script>\n");

            bool first = true;
            foreach (ReportTab tab in modeReport.Tabs)
            {
                sb.Append(TabPanel(tab.Key, first));
                first = false;
            }

            sb.Append(HtmlBody);
            return sb.ToString();
        }

        /// <summary>One tab's tables, as the page reads them: the header facts it shows, the markers it lists (not the lobby's own notes), the bug
        /// cards without their console part, and the tables.</summary>
        private static object ScopePayload(ReportTables tables)
        {
            tables ??= new ReportTables();
            ReportHeader h = tables.Header ?? new ReportHeader();
            return new
            {
                header = new
                {
                    h.MatchLengthSeconds,
                    h.WarmupSeconds,
                    h.NeverWentLive,
                    h.FreeLoadoutUsed,
                    h.DebugGoldUsed,
                    h.EliminationFallbackUsed,
                    h.OtherMatchFileCount,
                    OtherMatch = !string.IsNullOrEmpty(h.OtherMatchId),
                },
                markers = h.Markers.Where(m => !ReportVisibility.HidesMarker(m.Note)).ToList(),
                bugs = h.Bugs.Select(b => new { b.T, b.Actor, b.Nick, b.Team, b.Alive, b.Zone, b.X, b.Z, b.Weapon, b.Attachment, b.Mobility, b.Ultimate, b.ScreenshotFile, b.ChatNotes }).ToList(),
                goldTimeline = tables.GoldTimeline,
                economyByMinute = tables.EconomyByMinute,
                zoneIncome = tables.ZoneIncome,
                ownership = tables.Ownership,
                captures = tables.Captures,
                purchases = tables.Purchases,
                shopBlocked = tables.ShopBlocked,
                hits = tables.Hits,
                weapons = tables.Weapons,
                abilities = tables.Abilities,
                players = tables.Players,
                deaths = tables.Deaths,
            };
        }

        /// <summary>id -> name from the tuning snapshot's weapons or abilities (the first session that has the list).</summary>
        private static Dictionary<int, string> Names(TelemetryLog log, string list)
        {
            var names = new Dictionary<int, string>();
            if (log == null) return names;
            foreach (TelemetrySession s in log.Sessions)
            {
                if (!(s.Tuning?[list] is JArray entries) || entries.Count == 0) continue;
                foreach (JToken token in entries)
                {
                    if (!(token is JObject entry)) continue;
                    int? id = entry["id"]?.ToObject<int?>();
                    string name = entry["name"]?.ToString();
                    if (id.HasValue && !string.IsNullOrEmpty(name) && !names.ContainsKey(id.Value)) names[id.Value] = name;
                }
                break;
            }
            return names;
        }

        /// <summary>One tab's markup: the template with every element id suffixed by the tab's key.</summary>
        private static string TabPanel(string tabKey, bool visibleByDefault)
        {
            string content = TabPanelTemplate.Replace("%SCOPE%", tabKey);
            string style = visibleByDefault ? "" : " style='display:none'";
            return "<div id='tab-" + WebUtility.HtmlEncode(tabKey) + "' class='tab-panel'" + style + ">\n" + content + "\n</div>\n";
        }

        /// <summary>U+2028/U+2029 (LINE/PARAGRAPH SEPARATOR) are valid inside a JSON string but NOT inside a JS string literal before
        /// ES2019 (still true in plenty of non-browser JS engines). A nickname or marker note containing one, embedded raw, could
        /// corrupt the surrounding `const DATA = {...};` statement. EscapeHtml doesn't escape them, so this is a final pass over the
        /// serialized JSON text - a safe blind replace because these characters can only appear INSIDE a JSON string value, never as
        /// JSON's own (all-ASCII) structural syntax.</summary>
        private static string EscapeLineTerminators(string json)
        {
            return json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
        }

        // Single-quoted HTML attributes and JS strings throughout, on purpose: a C# verbatim string only ends at an unescaped double
        // quote, so keeping every quote in this template single avoids a large block of "" escaping.
        private const string HtmlHead = @"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>OverPower match report</title>
<style>
:root {
  --bg: #f5f6fa;
  --fg: #1b1c22;
  --card-bg: #ffffff;
  --border: #d8d9e0;
  --muted: #666a76;
  --warn-bg: #fff2d9;
  --warn-fg: #6b4600;
  --accent: #3a6df0;
  --bot-bg: #e4e7f2;
  --bot-fg: #3d4466;
}
@media (prefers-color-scheme: dark) {
  :root {
    --bg: #15161c;
    --fg: #e9eaf0;
    --card-bg: #1f202a;
    --border: #33343f;
    --muted: #9a9ca8;
    --warn-bg: #3a2c05;
    --warn-fg: #ffce7a;
    --accent: #6d94ff;
    --bot-bg: #2c3047;
    --bot-fg: #c3c9ea;
  }
}
* { box-sizing: border-box; }
html, body { max-width: 100%; overflow-x: hidden; }
body { background: var(--bg); color: var(--fg); font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px 24px 60px; }
h1 { margin: 0 0 4px; }
h2 { border-bottom: 1px solid var(--border); padding-bottom: 6px; }
h4 { margin: 0 0 6px; font-size: 13px; color: var(--muted); }
section { margin-bottom: 44px; }
.subtitle { color: var(--muted); margin: 0 0 12px; }
.card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px; overflow-x: auto; min-width: 0; }
.card-desc { font-size: 12px; color: var(--muted); margin: -6px 0 12px; }
table { border-collapse: collapse; width: 100%; font-size: 13px; }
th, td { border-bottom: 1px solid var(--border); padding: 5px 8px; text-align: left; white-space: nowrap; }
th { cursor: pointer; user-select: none; color: var(--muted); font-weight: 600; }
th.sorted-asc::after { content: ' \25b2'; font-size: 10px; }
th.sorted-desc::after { content: ' \25bc'; font-size: 10px; }
.warn { background: var(--warn-bg); color: var(--warn-fg); padding: 8px 14px; border-radius: 6px; margin: 6px 0; font-size: 13px; }
.grid { display: grid; grid-template-columns: 1fr; gap: 16px; min-width: 0; }
.chart-wrap { position: relative; height: 420px; min-width: 0; }
canvas { max-width: 100%; }
.gantt-row { display: flex; align-items: center; gap: 8px; margin: 5px 0; }
.gantt-label { width: 140px; font-size: 12px; color: var(--muted); flex-shrink: 0; }
.gantt-track { position: relative; flex: 1; height: 16px; background: var(--border); border-radius: 3px; min-width: 0; }
.gantt-bar { position: absolute; top: 0; height: 100%; border-radius: 3px; min-width: 2px; }
.note { color: var(--muted); font-size: 12px; }
[id^='arena-wrap-'] { position: relative; display: inline-block; max-width: 100%; }
canvas[id^='arena-canvas-'] { border: 1px solid var(--border); border-radius: 6px; max-width: 100%; height: auto; background: #000; }
[id^='heatmap-legend-'] { font-size: 12px; color: var(--muted); margin: 8px 0; }
.legend-dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin: 0 5px 0 12px; vertical-align: middle; border: 1.5px solid rgba(0,0,0,0.6); }
details.card summary { cursor: pointer; font-weight: 600; }
details.section-details > summary { cursor: pointer; list-style: none; }
details.section-details > summary::-webkit-details-marker { display: none; }
details.section-details > summary h2 { display: inline; margin: 0; border-bottom: none; padding-bottom: 0; }
details.section-details > summary::before { content: '\25b6'; display: inline-block; font-size: 11px; margin-right: 8px; transition: transform 0.1s; }
details.section-details[open] > summary::before { transform: rotate(90deg); }
.toc { font-size: 12px; color: var(--muted); margin-bottom: 18px; }
.toc a { color: var(--muted); margin-right: 10px; }
.card details > summary, .fold > summary { cursor: pointer; font-size: 12px; color: var(--muted); margin: 8px 0; }
.tab-bar { display: flex; flex-wrap: wrap; gap: 4px; margin: 16px 0 20px; border-bottom: 1px solid var(--border); }
.tab-button { background: none; border: none; border-bottom: 3px solid transparent; color: var(--muted); font: inherit; font-size: 14px; padding: 8px 14px; cursor: pointer; }
.tab-button.active { color: var(--fg); border-bottom-color: var(--accent); font-weight: 600; }
.tab-panel { display: block; min-width: 0; }
.phase-duration { font-size: 13px; color: var(--muted); margin: 0 0 16px; }
.bug-screenshot { max-width: 220px; width: 100%; height: auto; border: 1px solid var(--border); border-radius: 6px; margin: 8px 0; display: block; cursor: zoom-in; }
.bot-tag { display: inline-block; font-size: 10px; font-weight: 700; letter-spacing: 0.04em; background: var(--bot-bg); color: var(--bot-fg); border-radius: 4px; padding: 1px 5px; margin-left: 6px; vertical-align: 1px; }
.who-switch { display: inline-flex; gap: 0; border: 1px solid var(--border); border-radius: 6px; overflow: hidden; margin: 0 0 10px; }
.who-switch label { padding: 5px 12px; font-size: 13px; cursor: pointer; }
.who-switch input { display: none; }
.who-switch label.on { background: var(--accent); color: #fff; }
.result-headline { font-size: 22px; font-weight: 700; margin-bottom: 4px; }
.settings td:first-child { color: var(--muted); }
</style>
</head>
<body>
<div id='page-top'></div>
<div id='chart-fallback-note' class='warn' style='display:none'></div>
<div class='tab-bar' role='tablist' id='tab-bar'></div>
";

        /// <summary>One tab's inner markup - every id carries a %SCOPE% token, replaced with the tab's key by TabPanel. The page shows the
        /// sections the tab's list names, in that order, and hides the rest.</summary>
        private const string TabPanelTemplate = @"
<p class='phase-duration' id='phase-duration-%SCOPE%'></p>
<div id='phase2-empty-note-%SCOPE%' class='warn' style='display:none'>No team was eliminated in this match: everything is Phase 1.</div>
<div id='tab-content-%SCOPE%'>

<nav class='toc' id='toc-%SCOPE%'></nav>

<section id='section-result-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Result</h2></summary>
<div class='card' id='result-%SCOPE%'></div>
</details>
</section>

<section id='section-points-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Points race</h2></summary>
<p class='card-desc'>Each team's points over the round, with the bounties and centre payouts marked on the line, and where the points came from.</p>
<div id='points-%SCOPE%'></div>
</details>
</section>

<section id='section-zones-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Zones</h2></summary>
<p class='card-desc'>Who held each scoring zone and for how long, how often a team took it, and how soon it was first taken in the round.</p>
<div id='zones-%SCOPE%'></div>
</details>
</section>

<section id='section-fights-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Fights</h2></summary>
<div id='fights-%SCOPE%'></div>
</details>
</section>

<section id='section-builds-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Builds per round</h2></summary>
<p class='card-desc'>What each player carried for most of the round while alive, and the damage each gun did.</p>
<div id='builds-%SCOPE%'></div>
</details>
</section>

<section id='section-sudden-death-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Sudden death</h2></summary>
<div class='card' id='sudden-death-%SCOPE%'></div>
</details>
</section>

<section id='section-header-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Match</h2></summary>
<div id='header-warnings-%SCOPE%'></div>
<div class='card' id='header-summary-%SCOPE%'></div>
</details>
</section>

<section id='section-economy-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Economy</h2></summary>
<div class='grid'>
<div class='card'>
<h3>Gold per player over time</h3>
<p class='card-desc'>Each player's gold balance over time. Flat lines mean saving; drops mean a purchase; steady climbs mean territory income.</p>
<div class='chart-wrap'><canvas id='chart-gold-per-player-%SCOPE%'></canvas></div>
</div>
<div class='card'>
<h3>Team income per second by tier</h3>
<p class='card-desc' id='team-income-desc-%SCOPE%'>One small chart per team: gold per second from each tier, stacked.</p>
<div id='team-income-charts-%SCOPE%' class='grid'></div>
</div>
<div class='card'>
<h3>Gold generated per zone by team</h3>
<p class='card-desc'>Total gold each team earned from each zone it held on this tab.</p>
<div class='chart-wrap'><canvas id='chart-zone-income-%SCOPE%'></canvas></div>
</div>
<div class='card'>
<h3>Gold gap to richest team</h3>
<p class='card-desc'>How far behind the richest team each team was, over time. 0 means that team was the richest at that minute.</p>
<div class='chart-wrap'><canvas id='chart-gold-gap-%SCOPE%'></canvas></div>
</div>
<div class='card'>
<h3>Purchase timeline</h3>
<p class='card-desc' id='purchase-desc-%SCOPE%'>Every purchase, coloured by category.</p>
<div class='chart-wrap'><canvas id='chart-purchase-timeline-%SCOPE%'></canvas></div>
</div>
</div>
<div class='grid'>
<div class='card'><h3>Unspent gold (at death and at the end)</h3><div id='table-unspent-gold-%SCOPE%'></div></div>
<div class='card'><h3>Blocked purchases by reason</h3><div id='table-shop-blocked-%SCOPE%'></div></div>
</div>
</details>
</section>

<section id='section-territory-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Territory</h2></summary>
<div class='card'>
<h3>Ownership timeline</h3>
<p class='card-desc'>Each row is one zone; a coloured bar is one team's uninterrupted holding of it. Hover a bar for its start and end and how it ended.</p>
<div id='ownership-gantt-%SCOPE%'></div>
</div>
<div class='grid'>
<div class='card'>
<h3>Zones held per team over time</h3>
<p class='card-desc'>How many zones each team held at each minute.</p>
<div class='chart-wrap'><canvas id='chart-zones-held-%SCOPE%'></canvas></div>
</div>
<div class='card'><h3>Bounty gold by team</h3><div id='table-bounty-%SCOPE%'></div></div>
<div class='card'><h3>Centre scan and map shrink</h3><div id='scan-%SCOPE%'></div></div>
</div>
<div class='card'><h3>Captures</h3><div id='table-captures-%SCOPE%'></div></div>
</details>
</section>

<section id='section-combat-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Combat</h2></summary>
<div class='card'>
<h3>Weapons</h3>
<p class='card-desc'>Damage per minute a weapon was actually equipped, so weapons used for very different lengths of time are still comparable.</p>
<div class='chart-wrap'><canvas id='chart-weapons-%SCOPE%'></canvas></div>
<div id='table-weapons-%SCOPE%'></div>
</div>
<div class='card'><h3>Abilities</h3><div id='table-abilities-%SCOPE%'></div></div>
<div class='card'>
<h3>Team versus team damage</h3>
<p class='card-desc'>Damage dealt before armour, attacking team (rows) on victim team (columns). Burn damage is not split by team here.</p>
<div id='table-damage-matrix-%SCOPE%'></div>
</div>
</details>
</section>

<section id='section-map-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Map</h2></summary>
<div class='card' id='heatmaps-%SCOPE%'>
<p class='card-desc'>Deaths (solid, outlined, in the victim's team colour) and where players spent their time (small, see-through) on a top-down picture of the map. <span id='position-sample-note-%SCOPE%'></span></p>
<div>
<label><input type='checkbox' id='toggle-deaths-%SCOPE%' checked> Deaths</label>
&nbsp;&nbsp;
<label><input type='checkbox' id='toggle-positions-%SCOPE%' checked> Positions</label>
&nbsp;&nbsp;<span id='position-teams-%SCOPE%'></span>
</div>
<div id='heatmap-legend-%SCOPE%'></div>
<div id='arena-wrap-%SCOPE%'><canvas id='arena-canvas-%SCOPE%' width='1024' height='1024'></canvas></div>
<div id='arena-note-%SCOPE%' class='note'></div>
</div>
</details>
</section>

<section id='section-players-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Players</h2></summary>
<div class='card'><div id='table-players-%SCOPE%'></div></div>
</details>
</section>

<section id='section-markers-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Markers</h2></summary>
<div id='markers-list-%SCOPE%'></div>
</details>
</section>

<section id='section-bugs-%SCOPE%'>
<details class='section-details' open>
<summary><h2>Bug reports</h2></summary>
<p class='card-desc'>One card per Ctrl+B mark: who, when and where, their build, the screenshot (click for full size) and their own chat note in the minute after.</p>
<div id='bugs-list-%SCOPE%'></div>
</details>
</section>

</div>
";

        private const string HtmlBody = @"<script src='https://cdn.jsdelivr.net/npm/chart.js@4'></script>
<script>
(function () {
  'use strict';

  var R = DATA.report || {};
  var TABS = R.tabs || [];
  var LIVE = R.liveSeconds || 0;
  var HIDDEN = {};
  (DATA.hiddenStats || []).forEach(function (s) { HIDDEN[s] = true; });
  var HIDDEN_ZONES = {};
  (R.hiddenZones || []).forEach(function (z) { HIDDEN_ZONES[z] = true; });
  var ROSTER = {};
  (R.roster || []).forEach(function (p) { ROSTER[p.actor] = p; });
  var D = R.dominionReport || null;
  var who = 'all';

  function id(base, suffix) { return document.getElementById(base + '-' + suffix); }

  function el(tag, attrs, text) {
    var e = document.createElement(tag);
    if (attrs) { for (var k in attrs) { if (attrs.hasOwnProperty(k)) e.setAttribute(k, attrs[k]); } }
    if (text !== undefined && text !== null) e.textContent = text;
    return e;
  }

  // ---------------------------------------------------------------- numbers (ReportFormat writes the same in C#)

  function clockText(rel) {
    if (typeof rel !== 'number' || !isFinite(rel)) return '';
    var before = rel < 0;
    var total = before ? Math.ceil(-rel) : Math.floor(rel);
    var m = Math.floor(total / 60), s = total % 60;
    return (before ? '-' : '') + m + ':' + (s < 10 ? '0' : '') + s;
  }
  function clock(t) { return clockText((t || 0) - LIVE); }
  function len(seconds) {
    if (typeof seconds !== 'number' || !isFinite(seconds)) return '';
    var total = Math.round(Math.max(0, seconds));
    if (total < 60) return total + ' s';
    var m = Math.floor(total / 60), s = total % 60;
    return m + ':' + (s < 10 ? '0' : '') + s;
  }
  function pct(f) { return (typeof f === 'number' && isFinite(f)) ? Math.round(f * 100) + '%' : ''; }
  function whole(n) {
    if (n === null || n === undefined) return '';
    if (typeof n === 'number') return isFinite(n) ? String(Math.round(n)) : '';
    return String(n);
  }

  function tabOf(scope) { for (var i = 0; i < TABS.length; i++) if (TABS[i].key === scope) return TABS[i]; return null; }
  // A round's tab counts its times from the round's start ('1:20' is 1:20 into the round); every other tab from going live.
  function scopeClock(scope, t) {
    var tab = tabOf(scope);
    return (tab && tab.timesFromStart) ? clockText((t || 0) - tab.start) : clock(t);
  }

  // ---------------------------------------------------------------- names

  function teamName(team) {
    var names = DATA.teamNames;
    return (names && team >= 0 && team < names.length) ? names[team] : 'nobody';
  }
  function zoneLabel(zone) {
    if (zone === -1) return 'Unattributed';
    var names = DATA.zoneNames;
    return (names && zone >= 0 && zone < names.length) ? names[zone] : ('Zone ' + zone);
  }
  function weaponName(wid) {
    if (wid === -1 || wid === null || wid === undefined) return 'none';
    var n = DATA.weaponNames && DATA.weaponNames[wid];
    return n || 'unknown weapon';
  }
  function abilityName(aid) {
    if (aid === -1 || aid === null || aid === undefined) return 'none';
    var n = DATA.abilityNames && DATA.abilityNames[aid];
    return n || 'unknown ability';
  }
  function nickOf(actor, fallback) {
    var p = ROSTER[actor];
    if (p && p.nick) return p.nick;
    return fallback || 'a player';
  }
  function botTag(actor) {
    var p = ROSTER[actor];
    return (p && p.bot) ? ('BOT' + (p.build ? ' ' + p.build : '')) : '';
  }
  function nameText(actor, fallback) {
    var tag = botTag(actor);
    return nickOf(actor, fallback) + (tag ? ' [' + tag + ']' : '');
  }
  function nameNode(actor, fallback) {
    var span = el('span', null, nickOf(actor, fallback));
    var tag = botTag(actor);
    if (tag) span.appendChild(el('span', { class: 'bot-tag' }, tag));
    return span;
  }

  // The game's own team colours (UiTheme.teamShotColors), in team order; the report's lines must match the names.
  var TEAM_COLORS = ['#ff730d', '#ad52ff', '#26f2ff', '#8c8c94'];
  function teamColor(team) {
    var i = ((team % TEAM_COLORS.length) + TEAM_COLORS.length) % TEAM_COLORS.length;
    return TEAM_COLORS[i];
  }

  // ---------------------------------------------------------------- data access (the all / humans / bots switch filters every player's rows)

  var ACTOR_FIELD = { players: 'actor', deaths: 'victim', positions: 'actor', goldTimeline: 'actor', purchases: 'actor', shopBlocked: 'actor', hits: 'attacker' };
  function keepActor(actor) {
    if (who === 'all') return true;
    var bot = !!(ROSTER[actor] && ROSTER[actor].bot);
    return who === 'bots' ? bot : !bot;
  }
  function bucketFor(scope) {
    if (scope === 'whole-match') return DATA.wholeMatch;
    if (scope === 'phase-1') return DATA.phase1;
    if (scope === 'phase-2') return DATA.phase2;
    return (DATA.scopes && DATA.scopes[scope]) || null;
  }
  function rowsFor(table, scope) {
    var rows;
    if (table === 'positions') {
      rows = DATA.positions || [];
      if (scope === 'phase-1' || scope === 'phase-2') {
        var want = scope === 'phase-1' ? 1 : 2;
        rows = rows.filter(function (p) { return p.phase === want; });
      } else if (scope !== 'whole-match') {
        var tab = tabOf(scope);
        rows = tab ? rows.filter(function (p) { return p.t >= tab.start && p.t <= tab.end; }) : [];
      }
    } else {
      var bucket = bucketFor(scope);
      rows = (bucket && bucket[table]) || [];
    }
    var field = ACTOR_FIELD[table];
    if (field && who !== 'all') rows = rows.filter(function (r) { return keepActor(r[field]); });
    return rows;
  }
  function inWindow(scope, t) {
    var tab = tabOf(scope);
    return !tab || (t >= tab.start && t <= tab.end);
  }
  function roundsOf(scope) {
    if (!D) return [];
    var m = /^round-(\d+)$/.exec(scope);
    var all = (D.rounds || []).map(function (r) { return r.number; });
    return m ? all.filter(function (n) { return n === parseInt(m[1], 10); }) : all;
  }

  // ---------------------------------------------------------------- tables

  function makeSortable(table) {
    var ths = table.querySelectorAll('thead th');
    for (var i = 0; i < ths.length; i++) {
      (function (th, colIndex) {
        th.addEventListener('click', function () {
          var tbody = table.querySelector('tbody');
          var rows = Array.prototype.slice.call(tbody.querySelectorAll('tr'));
          var asc = !th.classList.contains('sorted-asc');
          for (var j = 0; j < ths.length; j++) { ths[j].classList.remove('sorted-asc', 'sorted-desc'); }
          th.classList.add(asc ? 'sorted-asc' : 'sorted-desc');
          rows.sort(function (a, b) {
            var av = a.children[colIndex].getAttribute('data-v') || '';
            var bv = b.children[colIndex].getAttribute('data-v') || '';
            var an = parseFloat(av), bn = parseFloat(bv);
            var cmp = (av !== '' && bv !== '' && !isNaN(an) && !isNaN(bn)) ? (an - bn) : av.localeCompare(bv);
            return asc ? cmp : -cmp;
          });
          for (var k = 0; k < rows.length; k++) tbody.appendChild(rows[k]);
        });
      })(ths[i], i);
    }
  }

  // A column: label, value(row) (what it sorts by), and optionally text(row) or node(row) for what it shows (else value as a whole number),
  // bold(row), and stat (a ReportVisibility name: the column is left out when the page hides that stat).
  function buildTable(container, columns, rows) {
    var cols = columns.filter(function (c) { return !c.stat || !HIDDEN[c.stat]; });
    var table = el('table');
    var thead = el('thead');
    var headRow = el('tr');
    cols.forEach(function (c) { headRow.appendChild(el('th', null, c.label)); });
    thead.appendChild(headRow);
    table.appendChild(thead);
    var tbody = el('tbody');
    rows.forEach(function (row) {
      var tr = el('tr');
      cols.forEach(function (c) {
        var v = c.value(row);
        var td = el('td', { 'data-v': (v === null || v === undefined) ? '' : v });
        if (c.node) td.appendChild(c.node(row));
        else td.textContent = c.text ? c.text(row) : whole(v);
        if (c.bold && c.bold(row)) td.style.fontWeight = '700';
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    container.appendChild(table);
    makeSortable(table);
    return table;
  }

  // A long list behind a one-line summary.
  function fold(container, summary, open) {
    var details = el('details', { class: 'fold' });
    if (open) details.setAttribute('open', '');
    details.appendChild(el('summary', null, summary));
    var body = el('div');
    details.appendChild(body);
    container.appendChild(details);
    return body;
  }

  function teamColumns(teams, valueOf, textOf, boldOf) {
    return teams.map(function (team) {
      return { label: teamName(team), value: function (r) { return valueOf(r, team); }, text: textOf ? function (r) { return textOf(r, team); } : undefined, bold: boldOf ? function (r) { return boldOf(r, team); } : undefined };
    });
  }

  // ---------------------------------------------------------------- charts

  var chartsOk = (typeof Chart !== 'undefined');
  var charts = {};
  function makeChart(suffix, canvas, config) {
    if (!chartsOk || !canvas) return null;
    var chart = new Chart(canvas, config);
    (charts[suffix] = charts[suffix] || []).push(chart);
    return chart;
  }
  if (chartsOk) {
    Chart.defaults.font.size = 13;
    Chart.defaults.plugins.legend.labels.boxWidth = 16;
  } else {
    var fallbackNote = document.getElementById('chart-fallback-note');
    fallbackNote.textContent = 'The chart library could not load (no internet?) - charts are left out, every table still works.';
    fallbackNote.style.display = 'block';
  }

  // xKind: 'clock' (match seconds, shown from going live), 'round' (seconds into a round), 'minute' (category labels already formatted).
  function chartOptions(xLabel, yLabel, xKind) {
    var xScale = { title: { display: true, text: xLabel } };
    if (xKind === 'clock') { xScale.type = 'linear'; xScale.ticks = { callback: function (v) { return clock(v); } }; }
    if (xKind === 'round') { xScale.type = 'linear'; xScale.ticks = { callback: function (v) { return clockText(v); } }; }
    return { responsive: true, maintainAspectRatio: false, scales: { x: xScale, y: { title: { display: true, text: yLabel }, beginAtZero: true } } };
  }

  function chartCard(container, title) {
    var card = el('div', { class: 'card' });
    if (title) card.appendChild(el('h3', null, title));
    var wrap = el('div', { class: 'chart-wrap' });
    var canvas = el('canvas');
    wrap.appendChild(canvas);
    card.appendChild(wrap);
    container.appendChild(card);
    return { card: card, canvas: canvas };
  }

  // One section's error must never blank out every section below it.
  function safeRun(name, fn) {
    try { fn(); }
    catch (err) {
      if (window.console && console.error) console.error('[report] ' + name + ' failed:', err);
      var host = document.getElementById('chart-fallback-note');
      host.parentNode.insertBefore(el('div', { class: 'warn' }, 'The ' + name + ' section hit an error and may be incomplete (see the browser console).'), host.nextSibling);
    }
  }

  function uniqueSorted(arr) {
    var seen = {}, out = [];
    arr.forEach(function (v) { if (!(v in seen)) { seen[v] = true; out.push(v); } });
    out.sort(function (a, b) { return a - b; });
    return out;
  }

  // ---------------------------------------------------------------- the top of the page

  function renderTop() {
    document.title = R.title || 'OverPower match report';
    var top = document.getElementById('page-top');
    top.appendChild(el('h1', null, R.title || 'OverPower match report'));
    var facts = [];
    if (R.playerCount) facts.push(R.playerCount);
    if (R.lengthSeconds) facts.push(len(R.lengthSeconds) + ' from going live');
    if (R.mapName) facts.push('played on ' + R.mapName);
    top.appendChild(el('p', { class: 'subtitle' }, facts.join(' \u00b7 ')));

    var hasBots = (R.roster || []).some(function (p) { return p.bot; });
    var hasHumans = (R.roster || []).some(function (p) { return !p.bot; });
    if (hasBots && hasHumans) {
      var sw = el('div', { class: 'who-switch', role: 'radiogroup' });
      [['all', 'Everyone'], ['humans', 'Humans'], ['bots', 'Bots']].forEach(function (option) {
        var label = el('label', option[0] === who ? { class: 'on' } : null);
        var input = el('input', { type: 'radio', name: 'who', value: option[0] });
        if (option[0] === who) input.checked = true;
        label.appendChild(input);
        label.appendChild(document.createTextNode(option[1]));
        input.addEventListener('change', function () {
          who = option[0];
          var labels = sw.querySelectorAll('label');
          for (var i = 0; i < labels.length; i++) labels[i].classList.remove('on');
          label.classList.add('on');
          renderAll();
        });
        sw.appendChild(label);
      });
      top.appendChild(sw);
    }

    function warn(text) { top.appendChild(el('div', { class: 'warn' }, text)); }
    if (R.missingLogs) warn(R.missingLogs);
    var h = (DATA.wholeMatch && DATA.wholeMatch.header) || {};
    if (h.neverWentLive) warn('This match never went live: the whole log is warm-up, nothing in it counts toward a real match.');
    if (R.showsGold && h.freeLoadoutUsed) warn('Free Loadout was on for at least part of this match: purchases and gold are not those of a real match.');
    if (R.showsGold && h.debugGoldUsed) warn('The F1 debug gold was used during this match.');
    if (h.otherMatch) warn('This folder also holds ' + h.otherMatchFileCount + ' file(s) from a different match; they are not in this report.');

    if (R.settings && R.settings.length) {
      var body = fold(top, 'Settings that matter for this mode', false);
      var table = el('table', { class: 'settings' });
      var tbody = el('tbody');
      R.settings.forEach(function (row) {
        var tr = el('tr');
        tr.appendChild(el('td', null, row[0]));
        tr.appendChild(el('td', null, row[1]));
        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      body.appendChild(table);
    }

    var bar = document.getElementById('tab-bar');
    if (TABS.length < 2) { bar.style.display = 'none'; return; }
    TABS.forEach(function (tab, i) {
      var button = el('button', { class: 'tab-button' + (i === 0 ? ' active' : ''), type: 'button' }, tab.label);
      button.addEventListener('click', function () {
        var buttons = bar.querySelectorAll('.tab-button');
        for (var b = 0; b < buttons.length; b++) buttons[b].classList.remove('active');
        button.classList.add('active');
        TABS.forEach(function (other) {
          var panel = document.getElementById('tab-' + other.key);
          if (panel) panel.style.display = other.key === tab.key ? 'block' : 'none';
        });
      });
      bar.appendChild(button);
    });
  }

  // ---------------------------------------------------------------- one tab

  var SECTION_TITLES = {
    'result': 'Result', 'points': 'Points race', 'zones': 'Zones', 'fights': 'Fights', 'builds': 'Builds per round', 'sudden-death': 'Sudden death',
    'header': 'Match', 'economy': 'Economy', 'territory': 'Territory', 'combat': 'Combat', 'map': 'Map', 'players': 'Players', 'markers': 'Markers', 'bugs': 'Bug reports',
  };

  function arrangeSections(tab) {
    var content = id('tab-content', tab.key);
    var toc = id('toc', tab.key);
    var all = content.querySelectorAll('section');
    for (var i = 0; i < all.length; i++) all[i].style.display = 'none';
    (tab.sections || []).forEach(function (name) {
      var section = document.getElementById('section-' + name + '-' + tab.key);
      if (!section) return;
      section.style.display = '';
      content.appendChild(section);
      toc.appendChild(el('a', { href: '#section-' + name + '-' + tab.key }, SECTION_TITLES[name] || name));
    });
  }

  function renderTabHeading(tab) {
    var host = id('phase-duration', tab.key);
    var targets = DATA.targets;
    var target = null;
    if (targets) target = tab.key === 'phase-1' ? targets.phase1DurationSeconds : tab.key === 'phase-2' ? targets.phase2DurationSeconds : targets.targetMatchSeconds;
    var text = tab.label + ': ' + len(Math.max(0, tab.end - tab.start));
    if (target) text += ' (GDD target ' + len(target) + ')';
    if (tab.timesFromStart) text += ' \u00b7 times on this tab count from its start';
    host.textContent = text;
  }

  function renderTab(tab) {
    renderTabHeading(tab);
    if (tab.key === 'phase-2' && !DATA.phase2) {
      id('phase-duration', tab.key).textContent = tab.label;
      id('tab-content', tab.key).style.display = 'none';
      id('phase2-empty-note', tab.key).style.display = 'block';
      return;
    }
    arrangeSections(tab);
    (tab.sections || []).forEach(function (name) {
      var render = RENDERERS[name];
      if (render) safeRun((SECTION_TITLES[name] || name) + ' (' + tab.label + ')', function () { render(tab.key, tab.key, tab); });
    });
  }

  // ---------------------------------------------------------------- Conquest: match, economy, territory, combat

  function renderMatchHeader(scope, suffix) {
    var bucket = bucketFor(scope);
    var h = (bucket && bucket.header) || {};
    var summary = id('header-summary', suffix);
    function line(label, value) { summary.appendChild(el('div', null, label + ': ' + value)); }
    line('Length', len(h.matchLengthSeconds));
    if (scope === 'whole-match' && h.warmupSeconds > 0) line('Warm-up', len(h.warmupSeconds) + ' before the match went live (left out of every number)');
    if (R.conquestReport && R.conquestReport.winner >= 0) line('Winner', teamName(R.conquestReport.winner));
    if (h.eliminationFallbackUsed)
      id('header-warnings', suffix).appendChild(el('div', { class: 'warn' }, 'A knockout was logged without its phase line: Phase 2 starts at the knockout.'));
  }

  function renderEconomy(scope, suffix) {
    var gt = rowsFor('goldTimeline', scope);
    var byActor = {};
    gt.forEach(function (row) {
      if (!byActor[row.actor]) byActor[row.actor] = { actor: row.actor, nick: row.nick, team: row.team, points: [] };
      byActor[row.actor].points.push({ x: row.t, y: row.balance });
    });
    var DASHES = [[], [10, 5], [2, 4], [14, 4, 2, 4]];
    var seenPerTeam = {};
    var goldDatasets = Object.keys(byActor).map(function (key) {
      var a = byActor[key];
      var nth = seenPerTeam[a.team] = (seenPerTeam[a.team] === undefined ? 0 : seenPerTeam[a.team] + 1);
      return { label: nameText(a.actor, a.nick), data: a.points, borderColor: teamColor(a.team), backgroundColor: teamColor(a.team), borderDash: DASHES[nth % DASHES.length], borderWidth: 2, fill: false, pointRadius: 1, tension: 0.1 };
    });
    var goldOpts = chartOptions('match time', 'gold', 'clock');
    goldOpts.parsing = false;
    makeChart(suffix, id('chart-gold-per-player', suffix), { type: 'line', data: { datasets: goldDatasets }, options: goldOpts });

    var eb = rowsFor('economyByMinute', scope);
    var teams = uniqueSorted(eb.map(function (r) { return r.team; }));
    var minutesList = uniqueSorted(eb.map(function (r) { return r.minute; }));
    var minuteLabels = minutesList.map(function (m) { return clock(m * 60); });
    function ebRow(team, minute) { for (var i = 0; i < eb.length; i++) if (eb[i].team === team && eb[i].minute === minute) return eb[i]; return null; }

    var bands = [];
    var targets = DATA.targets;
    if (targets) {
      var s1 = targets.phase1ScenarioIncomePerTeam, s2 = targets.phase2ScenarioIncomePerTeam;
      if (scope !== 'phase-2' && s1) bands = bands.concat([['losing', s1.losing], ['struggling', s1.struggling], ['average', s1.average], ['dominant', s1.dominant]]);
      if (scope === 'phase-2' && s2) bands = bands.concat([['losing', s2.losing], ['even', s2.even], ['winning', s2.winning]]);
      id('team-income-desc', suffix).textContent = 'One small chart per team: gold per second from each tier, stacked, against the GDD scenario incomes (dashed).';
    }
    var tierColors = ['#c9d6ff', '#8fa8ff', '#5a7cf7', '#2b52d6'];
    var incomeHost = id('team-income-charts', suffix);
    teams.forEach(function (team) {
      var holder = chartCard(incomeHost, teamName(team));
      var datasets = [0, 1, 2, 3].map(function (tier) {
        return { type: 'bar', label: 'Tier ' + (tier + 1), stack: 'tiers', backgroundColor: tierColors[tier],
                 data: minutesList.map(function (m) { var row = ebRow(team, m); return row ? Math.round(row.incomeByTier[tier] / 60) : 0; }) };
      });
      // Each band is a stack of its own, so the stacked axis never piles one GDD line on another or on the bars.
      bands.forEach(function (b, i) {
        datasets.push({ type: 'line', label: 'GDD ' + b[0], stack: 'band' + i, data: minutesList.map(function () { return b[1]; }), borderColor: '#888', borderDash: [5, 4], pointRadius: 0, fill: false });
      });
      var opts = chartOptions('match time', 'gold per second', 'minute');
      opts.scales.x.stacked = true;
      opts.scales.y.stacked = true;
      makeChart(suffix, holder.canvas, { data: { labels: minuteLabels, datasets: datasets }, options: opts });
    });

    var zi = rowsFor('zoneIncome', scope).filter(function (r) { return !HIDDEN_ZONES[r.zone]; });
    var zones = uniqueSorted(zi.map(function (r) { return r.zone; }));
    var zoneDatasets = teams.map(function (team) {
      return { label: teamName(team), backgroundColor: teamColor(team),
               data: zones.map(function (z) { var sum = 0; zi.forEach(function (r) { if (r.zone === z && r.team === team) sum += r.goldGenerated; }); return Math.round(sum); }) };
    });
    makeChart(suffix, id('chart-zone-income', suffix), { type: 'bar', data: { labels: zones.map(zoneLabel), datasets: zoneDatasets }, options: chartOptions('zone', 'gold') });

    var gapDatasets = teams.map(function (team) {
      return { label: teamName(team), borderColor: teamColor(team), backgroundColor: teamColor(team), fill: false, pointRadius: 1,
               data: minutesList.map(function (m) { var row = ebRow(team, m); return row ? row.goldGapToRichest : null; }) };
    });
    makeChart(suffix, id('chart-gold-gap', suffix), { type: 'line', data: { labels: minuteLabels, datasets: gapDatasets }, options: chartOptions('match time', 'gold behind the richest team', 'minute') });

    var purchases = rowsFor('purchases', scope).filter(function (p) { return p.kind === 'purchase'; });
    var byCategory = {};
    purchases.forEach(function (p) {
      var cat = p.category || 'other';
      (byCategory[cat] = byCategory[cat] || []).push({ x: p.t, y: p.amount });
    });
    var catColors = ['#3a6df0', '#e0473f', '#2fa84f', '#9a6df0', '#e0a83f'];
    var purchaseDatasets = Object.keys(byCategory).map(function (cat, i) {
      return { label: cat, data: byCategory[cat], backgroundColor: catColors[i % catColors.length], pointRadius: 4 };
    });
    var timing = targets ? targets.purchaseTargetMinutes : null;
    if (timing) {
      var maxAmount = 1;
      purchases.forEach(function (p) { if (p.amount > maxAmount) maxAmount = p.amount; });
      [['GDD primary upgrade 1', timing.primaryUpgrade1Minutes], ['GDD armour 1', timing.armor1Minutes], ['GDD ultimate', timing.ultimateMinutes],
       ['GDD primary upgrade 2', timing.primaryUpgrade2Minutes], ['GDD armour 2', timing.armor2Minutes]].forEach(function (line) {
        var x = LIVE + line[1] * 60;
        purchaseDatasets.push({ type: 'line', label: line[0], data: [{ x: x, y: 0 }, { x: x, y: maxAmount * 1.1 }], borderColor: '#888', borderDash: [5, 4], pointRadius: 0, fill: false });
      });
      id('purchase-desc', suffix).textContent = 'Every purchase, coloured by category, against the GDD target times (dashed).';
    }
    makeChart(suffix, id('chart-purchase-timeline', suffix), { type: 'scatter', data: { datasets: purchaseDatasets }, options: chartOptions('match time', 'price', 'clock') });

    var lastBalance = {};
    gt.forEach(function (row) { lastBalance[row.actor] = row.balance; });
    var unspentAtDeath = {};
    rowsFor('deaths', scope).forEach(function (d) { (unspentAtDeath[d.victim] = unspentAtDeath[d.victim] || []).push(d.unspentGold); });
    var unspentRows = rowsFor('players', scope).map(function (p) {
      var list = unspentAtDeath[p.actor] || [];
      var sum = 0; list.forEach(function (g) { sum += g; });
      return { actor: p.actor, nick: p.nick, team: p.team, end: lastBalance[p.actor], mean: list.length ? sum / list.length : null, deaths: list.length };
    });
    buildTable(id('table-unspent-gold', suffix), [
      { label: 'Player', value: function (r) { return nickOf(r.actor, r.nick); }, node: function (r) { return nameNode(r.actor, r.nick); } },
      { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
      { label: 'Gold at the end', value: function (r) { return r.end; } },
      { label: 'Average unspent at death', value: function (r) { return r.mean; } },
      { label: 'Deaths', value: function (r) { return r.deaths; } },
    ], unspentRows);

    var byReason = {};
    rowsFor('shopBlocked', scope).forEach(function (b) {
      var reason = b.reason || 'unknown';
      if (!byReason[reason]) byReason[reason] = { reason: reason, count: 0, shortfall: 0 };
      byReason[reason].count++;
      byReason[reason].shortfall += (b.shortfall || 0);
    });
    buildTable(id('table-shop-blocked', suffix), [
      { label: 'Reason', value: function (r) { return r.reason; }, text: function (r) { return r.reason; } },
      { label: 'Times', value: function (r) { return r.count; } },
      { label: 'Gold short in all', value: function (r) { return r.shortfall; } },
    ], Object.keys(byReason).map(function (k) { return byReason[k]; }));
  }

  function renderTerritory(scope, suffix) {
    var tab = tabOf(scope);
    var winStart = tab ? tab.start : 0;
    var winLength = tab ? Math.max(1, tab.end - tab.start) : 1;
    var ownership = rowsFor('ownership', scope).filter(function (r) { return !HIDDEN_ZONES[r.zone]; });
    var gantt = id('ownership-gantt', suffix);
    uniqueSorted(ownership.map(function (r) { return r.zone; })).forEach(function (zone) {
      var row = el('div', { class: 'gantt-row' });
      row.appendChild(el('div', { class: 'gantt-label' }, zoneLabel(zone)));
      var track = el('div', { class: 'gantt-track' });
      ownership.filter(function (r) { return r.zone === zone; }).forEach(function (st) {
        track.appendChild(el('div', {
          class: 'gantt-bar',
          style: 'left:' + ((st.from - winStart) / winLength * 100) + '%;width:' + Math.max(0.3, (st.to - st.from) / winLength * 100) + '%;background:' + teamColor(st.team) + ';',
          title: zoneLabel(st.zone) + ': ' + teamName(st.team) + ' from ' + clock(st.from) + ' to ' + clock(st.to) + ' (' + st.howEnded + ')',
        }));
      });
      row.appendChild(track);
      gantt.appendChild(row);
    });

    var eb = rowsFor('economyByMinute', scope);
    var teams = uniqueSorted(eb.map(function (r) { return r.team; }));
    var minutesList = uniqueSorted(eb.map(function (r) { return r.minute; }));
    var heldDatasets = teams.map(function (team) {
      return { label: teamName(team), borderColor: teamColor(team), backgroundColor: teamColor(team), fill: false, pointRadius: 1,
               data: minutesList.map(function (m) {
                 var sum = 0;
                 eb.forEach(function (r) { if (r.team === team && r.minute === m) for (var t = 0; t < 4; t++) sum += r.zonesHeldByTier[t]; });
                 return sum;
               }) };
    });
    makeChart(suffix, id('chart-zones-held', suffix), { type: 'line', data: { labels: minutesList.map(function (m) { return clock(m * 60); }), datasets: heldDatasets }, options: chartOptions('match time', 'zones held', 'minute') });

    var bountyByTeam = {};
    eb.forEach(function (r) { bountyByTeam[r.team] = (bountyByTeam[r.team] || 0) + (r.bounty || 0); });
    buildTable(id('table-bounty', suffix), [
      { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
      { label: 'Bounty gold', value: function (r) { return r.total; } },
    ], Object.keys(bountyByTeam).map(function (k) { return { team: parseInt(k, 10), total: bountyByTeam[k] }; }));

    var scanHost = id('scan', suffix);
    var C = R.conquestReport || {};
    var waves = (C.scanWaves || []).filter(function (w) { return inWindow(scope, w.startT); });
    if (waves.length) {
      var byHolder = {};
      waves.forEach(function (w) {
        var k = w.holder;
        if (!byHolder[k]) byHolder[k] = { holder: k, waves: 0, caught: 0 };
        byHolder[k].waves++;
        if (w.caught > 0) byHolder[k].caught += w.caught;
      });
      scanHost.appendChild(el('div', null, waves.length + ' scan wave(s) on this tab.'));
      buildTable(scanHost, [
        { label: 'Held by', value: function (r) { return r.holder; }, text: function (r) { return r.holder >= 0 ? teamName(r.holder) : 'nobody (centre neutral)'; } },
        { label: 'Waves', value: function (r) { return r.waves; } },
        { label: 'Enemies spotted', value: function (r) { return r.caught; } },
      ], Object.keys(byHolder).map(function (k) { return byHolder[k]; }));
    } else {
      scanHost.appendChild(el('div', { class: 'note' }, 'No scan wave was logged on this tab.'));
    }
    if (C.shrink && C.shrink.team >= 0) {
      var closed = (C.shrink.zones || []).map(zoneLabel).join(', ');
      var when = C.shrink.t <= LIVE + 1 ? 'from the start' : 'at ' + clock(C.shrink.t);
      scanHost.appendChild(el('div', null, teamName(C.shrink.team) + '\u2019s corner closed ' + when + (closed ? ' (' + closed + ')' : '') + '.'));
    }

    var captureRows = rowsFor('captures', scope).filter(function (r) { return !HIDDEN_ZONES[r.zone]; });
    var perTeam = {};
    captureRows.forEach(function (r) { perTeam[r.team] = (perTeam[r.team] || 0) + 1; });
    var summary = captureRows.length + ' capture attempt(s)' + (Object.keys(perTeam).length ? ' (' + Object.keys(perTeam).map(function (t) { return teamName(parseInt(t, 10)) + ' ' + perTeam[t]; }).join(', ') + ')' : '');
    var body = fold(id('table-captures', suffix), summary + ' - show', false);
    buildTable(body, [
      { label: 'Zone', value: function (r) { return zoneLabel(r.zone); }, text: function (r) { return zoneLabel(r.zone); } },
      { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
      { label: 'Start', value: function (r) { return r.start; }, text: function (r) { return clock(r.start); } },
      { label: 'End', value: function (r) { return r.end; }, text: function (r) { return clock(r.end); } },
      { label: 'Outcome', value: function (r) { return r.outcome; }, text: function (r) { return r.outcome; } },
      { label: 'Length', value: function (r) { return r.duration; }, text: function (r) { return len(r.duration); } },
      { label: 'Players', value: function (r) { return r.players; } },
    ], captureRows);
  }

  function renderCombat(scope, suffix) {
    var weapons = rowsFor('weapons', scope).filter(function (w) { return w.timeEquippedSeconds > 0 || w.damageRaw > 0 || w.kills > 0; });
    makeChart(suffix, id('chart-weapons', suffix), {
      type: 'bar',
      data: { labels: weapons.map(function (w) { return weaponName(w.weaponId); }),
              datasets: [{ label: 'Damage per minute equipped', backgroundColor: '#3a6df0', data: weapons.map(function (w) { return Math.round(w.damagePerEquippedMinute); }) }] },
      options: chartOptions('weapon', 'damage per minute equipped'),
    });
    buildTable(id('table-weapons', suffix), [
      { label: 'Weapon', value: function (r) { return weaponName(r.weaponId); }, text: function (r) { return weaponName(r.weaponId); } },
      { label: 'Time equipped', value: function (r) { return r.timeEquippedSeconds; }, text: function (r) { return len(r.timeEquippedSeconds); } },
      { label: 'Trigger pulls', stat: 'pulls', value: function (r) { return r.pulls; } },
      { label: 'Projectiles', stat: 'projectiles', value: function (r) { return r.projectiles; } },
      { label: 'Hits', value: function (r) { return r.hits; } },
      { label: 'Splash hits', stat: 'splashHits', value: function (r) { return r.splashHits; } },
      { label: 'Accuracy', value: function (r) { return r.accuracy; }, text: function (r) { return pct(r.accuracy); } },
      { label: 'Marks placed', stat: 'marksPlaced', value: function (r) { return r.marksPlaced; } },
      { label: 'Marks cashed', value: function (r) { return r.marksCashed; } },
      { label: 'Damage', value: function (r) { return r.damageRaw; } },
      { label: 'Armour damage', stat: 'armorDamage', value: function (r) { return r.armorDamage; } },
      { label: 'Health damage', stat: 'healthDamage', value: function (r) { return r.healthDamage; } },
      { label: 'Damage per minute equipped', value: function (r) { return r.damagePerEquippedMinute; } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Mean distance (m)', stat: 'meanDistance', value: function (r) { return r.meanDistance; } },
      { label: 'Median distance (m)', stat: 'medianDistance', value: function (r) { return r.medianDistance; } },
    ], weapons);

    buildTable(id('table-abilities', suffix), [
      { label: 'Ability', value: function (r) { return abilityName(r.abilityId); }, text: function (r) { return abilityName(r.abilityId); } },
      { label: 'Uses', value: function (r) { return r.casts; } },
      { label: 'Damage', value: function (r) { return r.damageRaw; } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Effects applied', stat: 'statusCount', value: function (r) { return r.statusCount; } },
      { label: 'Effect seconds', stat: 'statusSeconds', value: function (r) { return r.statusSeconds; } },
    ], rowsFor('abilities', scope));

    var hits = rowsFor('hits', scope).filter(function (h) { return h.source !== 'Burn' && h.attackerTeam >= 0; });
    var teams = uniqueSorted(hits.map(function (h) { return h.attackerTeam; }).concat(hits.map(function (h) { return h.victimTeam; })).filter(function (t) { return t >= 0; }));
    var matrix = {}, maxVal = 0;
    hits.forEach(function (h) {
      var key = h.attackerTeam + ':' + h.victimTeam;
      matrix[key] = (matrix[key] || 0) + (h.raw || 0);
      if (matrix[key] > maxVal) maxVal = matrix[key];
    });
    var table = el('table'), thead = el('thead'), head = el('tr');
    head.appendChild(el('th', null, 'Attacking team'));
    teams.forEach(function (t) { head.appendChild(el('th', null, 'on ' + teamName(t))); });
    thead.appendChild(head);
    table.appendChild(thead);
    var tbody = el('tbody');
    teams.forEach(function (a) {
      var tr = el('tr');
      tr.appendChild(el('td', null, teamName(a)));
      teams.forEach(function (v) {
        var value = matrix[a + ':' + v] || 0;
        var alpha = maxVal > 0 ? (0.12 + 0.65 * (value / maxVal)) : 0.12;
        tr.appendChild(el('td', { style: 'background: rgba(224,71,63,' + alpha + ');' }, whole(value)));
      });
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    id('table-damage-matrix', suffix).appendChild(table);
  }

  // ---------------------------------------------------------------- the map

  function renderMap(scope, suffix) {
    var arena = DATA.arena;
    var canvas = id('arena-canvas', suffix);
    var note = id('arena-note', suffix);
    if (DATA.positionsKeptEveryN && DATA.positionsKeptEveryN > 1)
      id('position-sample-note', suffix).textContent = '(Every ' + DATA.positionsKeptEveryN + 'th position per player is drawn, to keep the report small.)';
    if (!arena || !arena.available) {
      note.textContent = (arena && arena.note) ? arena.note : 'There is no picture of this map.';
      canvas.style.display = 'none';
      return;
    }

    var deaths = rowsFor('deaths', scope);
    var legend = id('heatmap-legend', suffix);
    var deathTeams = uniqueSorted(deaths.map(function (d) { return d.victimTeam; }));
    if (deathTeams.length) {
      legend.appendChild(el('span', null, 'Deaths by the victim\u2019s team:'));
      deathTeams.forEach(function (t) {
        legend.appendChild(el('span', { class: 'legend-dot', style: 'background:' + teamColor(t) + ';' }));
        legend.appendChild(el('span', null, teamName(t)));
      });
    }

    function rgba(team, alpha) {
      var hex = teamColor(team).replace('#', '');
      return 'rgba(' + parseInt(hex.substr(0, 2), 16) + ',' + parseInt(hex.substr(2, 2), 16) + ',' + parseInt(hex.substr(4, 2), 16) + ',' + alpha + ')';
    }
    var positionTeams = uniqueSorted(rowsFor('positions', scope).map(function (p) { return p.team; }));
    var teamBoxes = {};
    var teamHost = id('position-teams', suffix);
    positionTeams.forEach(function (t) {
      var box = el('input', { type: 'checkbox' });
      box.checked = true;
      teamBoxes[t] = box;
      var label = el('label');
      label.appendChild(box);
      label.appendChild(el('span', { class: 'legend-dot', style: 'background:' + teamColor(t) + ';' }));
      label.appendChild(document.createTextNode(teamName(t) + '  '));
      teamHost.appendChild(label);
      box.addEventListener('change', function () { draw(); });
    });

    var W = arena.pixelsAcross, H = arena.pixelsUp;
    var ctx = canvas.getContext('2d');
    var img = new Image();
    function toPixel(x, z) {
      return [(x - arena.frameMinX) / arena.frameWidthMetres * W, H - (z - arena.frameMinZ) / arena.frameDepthMetres * H];
    }
    function draw() {
      ctx.fillStyle = '#000';
      ctx.fillRect(0, 0, W, H);
      var iw = img.naturalWidth, ih = img.naturalHeight;
      var sx = (arena.frameMinX - arena.imageMinX) / arena.imageWidthMetres * iw;
      var sw = arena.frameWidthMetres / arena.imageWidthMetres * iw;
      var sy = (arena.imageMinZ + arena.imageDepthMetres - (arena.frameMinZ + arena.frameDepthMetres)) / arena.imageDepthMetres * ih;
      var sh = arena.frameDepthMetres / arena.imageDepthMetres * ih;
      ctx.drawImage(img, sx, sy, sw, sh, 0, 0, W, H);
      if (id('toggle-positions', suffix).checked) {
        rowsFor('positions', scope).forEach(function (p) {
          var box = teamBoxes[p.team];
          if (box && !box.checked) return;
          var pp = toPixel(p.x, p.z);
          ctx.fillStyle = rgba(p.team, 0.45);
          ctx.beginPath();
          ctx.arc(pp[0], pp[1], 3, 0, 2 * Math.PI);
          ctx.fill();
        });
      }
      if (id('toggle-deaths', suffix).checked) {
        deaths.forEach(function (d) {
          var pd = toPixel(d.x, d.z);
          ctx.beginPath();
          ctx.arc(pd[0], pd[1], 5, 0, 2 * Math.PI);
          ctx.fillStyle = teamColor(d.victimTeam);
          ctx.fill();
          ctx.lineWidth = 1.5;
          ctx.strokeStyle = 'rgba(0,0,0,0.75)';
          ctx.stroke();
        });
      }
    }
    img.onload = function () { canvas.width = W; canvas.height = H; draw(); };
    img.src = 'data:image/png;base64,' + arena.base64Png;
    id('toggle-positions', suffix).addEventListener('change', draw);
    id('toggle-deaths', suffix).addEventListener('change', draw);
  }

  // ---------------------------------------------------------------- players

  function extraOf(scope, actor) {
    if (!D) return null;
    var list = D.playerExtras || [];
    for (var i = 0; i < list.length; i++) if (list[i].scope === scope && list[i].actor === actor) return list[i];
    return null;
  }

  function renderPlayers(scope, suffix) {
    var columns = [
      { label: 'Player', value: function (r) { return nickOf(r.actor, r.nick); }, node: function (r) { return nameNode(r.actor, r.nick); } },
      { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Deaths', value: function (r) { return r.deaths; } },
      { label: 'Assists', value: function (r) { return r.assists; } },
      { label: 'Damage dealt', value: function (r) { return r.damageDealt; } },
      { label: 'Damage taken', value: function (r) { return r.damageTaken; } },
    ];
    if (R.dominion) {
      columns.push(
        { label: 'Time on a scoring zone', value: function (r) { var x = extraOf(scope, r.actor); return x ? x.scoringZoneSeconds : null; }, text: function (r) { var x = extraOf(scope, r.actor); return x ? len(x.scoringZoneSeconds) : ''; } },
        { label: 'Time dead', value: function (r) { var x = extraOf(scope, r.actor); return x ? x.deadSeconds : null; }, text: function (r) { var x = extraOf(scope, r.actor); return x ? len(x.deadSeconds) : ''; } },
        { label: 'Healing', value: function (r) { return r.healing; } });
    } else {
      columns.push(
        { label: 'Gold from zones', value: function (r) { return r.goldTerritory; } },
        { label: 'Gold from bounties', value: function (r) { return r.goldBounty; } },
        { label: 'Gold refunded', stat: 'goldRefund', value: function (r) { return r.goldRefund; } },
        { label: 'Debug gold', stat: 'goldDebug', value: function (r) { return r.goldDebug; } },
        { label: 'Other gold', stat: 'goldOther', value: function (r) { return r.goldOther; } },
        { label: 'Gold spent', value: function (r) { return r.goldSpent; } },
        { label: 'Time alive', value: function (r) { return r.timeAlive; }, text: function (r) { return len(r.timeAlive); } },
        { label: 'Time in own zones', value: function (r) { return r.timeOwnZone; }, text: function (r) { return len(r.timeOwnZone); } },
        { label: 'Time in enemy zones', value: function (r) { return r.timeEnemyZone; }, text: function (r) { return len(r.timeEnemyZone); } },
        { label: 'Time outside zones', value: function (r) { return r.timeNeutralZone; }, text: function (r) { return len(r.timeNeutralZone); } },
        { label: 'Healing', value: function (r) { return r.healing; } });
    }
    buildTable(id('table-players', suffix), columns, rowsFor('players', scope));
  }

  // ---------------------------------------------------------------- markers and bug reports

  function nearbyEvents(scope) {
    var events = [];
    rowsFor('deaths', scope).forEach(function (d) { events.push({ t: d.t, text: nameText(d.victim, d.victimNick) + ' died' + (d.killer >= 0 ? ' to ' + nameText(d.killer) : '') }); });
    if (R.showsGold) rowsFor('purchases', scope).forEach(function (p) { events.push({ t: p.t, text: nameText(p.actor, p.nick) + ' ' + p.kind + ' (' + p.category + ')' }); });
    rowsFor('ownership', scope).filter(function (o) { return !HIDDEN_ZONES[o.zone]; }).forEach(function (o) { events.push({ t: o.from, text: zoneLabel(o.zone) + ' taken by ' + teamName(o.team) }); });
    return events;
  }

  function eventsTable(container, scope, events) {
    buildTable(container, [
      { label: 'Time', value: function (r) { return r.t; }, text: function (r) { return scopeClock(scope, r.t); } },
      { label: 'What happened', value: function (r) { return r.text; }, text: function (r) { return r.text; } },
    ], events);
  }

  function renderMarkers(scope, suffix) {
    var bucket = bucketFor(scope);
    var markers = (bucket && bucket.markers) || [];
    var container = id('markers-list', suffix);
    if (!markers.length) { container.appendChild(el('div', { class: 'note' }, 'No markers on this tab.')); return; }
    var events = nearbyEvents(scope);
    markers.forEach(function (m) {
      var card = el('div', { class: 'card' });
      card.appendChild(el('h3', null, scopeClock(scope, m.t) + ' \u2013 ' + nickOf(m.actor, 'the host') + ': ' + (m.note || '(no note)')));
      var nearby = events.filter(function (e) { return e.t >= m.t - 30 && e.t <= m.t + 30; }).sort(function (a, b) { return a.t - b.t; });
      eventsTable(fold(card, 'What happened in the 30 s around it: ' + nearby.length + ' event(s) - show', false), scope, nearby);
      container.appendChild(card);
    });
  }

  function renderBugReports(scope, suffix) {
    var bucket = bucketFor(scope);
    var bugs = (bucket && bucket.bugs) || [];
    var container = id('bugs-list', suffix);
    if (!bugs.length) { container.appendChild(el('div', { class: 'note' }, 'No bugs were marked (Ctrl+B) in this match.')); return; }
    var events = nearbyEvents(scope);
    bugs.forEach(function (bug) {
      var card = el('div', { class: 'card' });
      var title = el('h3', null, clock(bug.t) + ' \u2013 ');
      title.appendChild(nameNode(bug.actor, bug.nick));
      title.appendChild(document.createTextNode(' (' + teamName(bug.team) + ')'));
      card.appendChild(title);
      card.appendChild(el('div', null, 'Where: ' + zoneLabel(bug.zone) + ', ' + (bug.alive ? 'alive' : 'dead')));
      card.appendChild(el('div', null, 'Build: ' + weaponName(bug.weapon) + ', ' + abilityName(bug.attachment) + ', ' + abilityName(bug.mobility) + ', ' + abilityName(bug.ultimate)));
      if (bug.screenshotFile) {
        var link = el('a', { href: bug.screenshotFile, target: '_blank', rel: 'noopener' });
        link.appendChild(el('img', { class: 'bug-screenshot', src: bug.screenshotFile, alt: 'Bug screenshot at ' + clock(bug.t) + ' (click for full size)' }));
        card.appendChild(link);
      }
      card.appendChild(el('div', { class: 'note' }, (bug.chatNotes && bug.chatNotes.length) ? ('Their note: ' + bug.chatNotes.join(' / ')) : 'No chat note in the minute after the mark.'));
      var nearby = events.filter(function (e) { return e.t >= bug.t - 30 && e.t <= bug.t + 30; }).sort(function (a, b) { return a.t - b.t; });
      eventsTable(fold(card, 'What happened in the 30 s around it: ' + nearby.length + ' event(s) - show', false), scope, nearby);
      container.appendChild(card);
    });
  }

  // ---------------------------------------------------------------- Dominion

  function needDominion(host) {
    if (D) return true;
    host.appendChild(el('div', { class: 'note' }, 'The round notes of this match could not be read.'));
    return false;
  }

  function winnerText(r) {
    var names = (r.winners || []).map(teamName);
    switch (r.outcome) {
      case 'won': return names[0] || '';
      case 'tied': return 'tied: nobody';
      case 'shared': return names.join(' & ') + ' (shared)';
      case 'cut short': return 'cut short: the others left';
      case 'sudden death': return 'tied: sudden death for the round';
      case 'won in sudden death': return (names[0] || '') + ' (in sudden death)';
      default: return r.outcome || 'not finished';
    }
  }

  function renderResult(scope, suffix) {
    var host = id('result', suffix);
    if (!needDominion(host)) return;
    var teams = R.teams || [];
    var score = teams.map(function (t) { return D.wins[t] || 0; }).sort(function (a, b) { return b - a; }).join('-');
    host.appendChild(el('div', { class: 'result-headline' }, D.winner >= 0 ? teamName(D.winner) + ' wins ' + score : 'No winner recorded'));
    var facts = ['Match length ' + len(R.lengthSeconds)];
    if (D.howEnded) facts.push('decided by ' + D.howEnded);
    host.appendChild(el('div', { class: 'note' }, facts.join(' \u00b7 ')));
    var columns = [
      { label: 'Round', value: function (r) { return r.number; } },
      { label: 'Winner', value: function (r) { return winnerText(r); }, text: function (r) { return winnerText(r); }, bold: function (r) { return (r.winners || []).length > 0; } },
    ].concat(teamColumns(teams,
      function (r, t) { return r.points[t]; },
      function (r, t) { return r.points[t] === undefined ? '' : whole(r.points[t]); },
      function (r, t) { return (r.winners || []).indexOf(t) >= 0; })).concat([
      { label: 'Margin', value: function (r) { return r.margin; } },
      { label: 'Length', value: function (r) { return r.lengthSeconds; }, text: function (r) { return len(r.lengthSeconds); } },
      { label: 'Overtime', value: function (r) { return r.overtimeSeconds; }, text: function (r) {
          if (!r.overtime) return 'no';
          var how = r.outcome === 'won' ? 'a lead won it' : r.outcome === 'shared' ? 'no lead: shared' : r.outcome === 'sudden death' ? 'no lead: sudden death' : r.outcome;
          return len(r.overtimeSeconds) + ', ' + how;
        } },
    ]);
    buildTable(host, columns, D.rounds || []);
  }

  function renderPoints(scope, suffix) {
    var host = id('points', suffix);
    if (!needDominion(host)) return;
    var numbers = roundsOf(scope);
    (D.races || []).filter(function (race) { return numbers.indexOf(race.round) >= 0; }).forEach(function (race) {
      var holder = chartCard(host, scope === 'whole-match' ? 'Round ' + race.round : null);
      var datasets = race.series.map(function (s) {
        return { label: teamName(s.team), data: s.points.map(function (p) { return { x: p[0], y: p[1] }; }), borderColor: teamColor(s.team), backgroundColor: teamColor(s.team),
                 stepped: true, fill: false, pointRadius: 0, borderWidth: 2 };
      });
      var marks = {};
      (race.events || []).forEach(function (ev) {
        var key = ev.kind + ':' + ev.team;
        if (!marks[key]) marks[key] = { kind: ev.kind, team: ev.team, points: [] };
        var series = null;
        race.series.forEach(function (s) { if (s.team === ev.team) series = s; });
        var y = 0;
        if (series) series.points.forEach(function (p) { if (p[0] <= ev.at) y = p[1]; });
        marks[key].points.push({ x: ev.at, y: y, label: ev.kind + ' +' + ev.points });
      });
      Object.keys(marks).forEach(function (k) {
        var m = marks[k];
        datasets.push({ type: 'scatter', label: teamName(m.team) + (m.kind === 'bounty' ? ' bounty' : ' centre payout'), data: m.points,
                        pointStyle: m.kind === 'bounty' ? 'triangle' : 'star', pointRadius: 8, backgroundColor: teamColor(m.team), borderColor: '#000' });
      });
      var opts = chartOptions('time into the round', 'points', 'round');
      opts.parsing = false;
      opts.plugins = { tooltip: { callbacks: { label: function (c) { return c.dataset.label + ': ' + (c.raw && c.raw.label ? c.raw.label : whole(c.parsed.y)) + ' at ' + clockText(c.parsed.x); } } } };
      makeChart(suffix, holder.canvas, { type: 'line', data: { datasets: datasets }, options: opts });
      if (!race.logged) holder.card.appendChild(el('div', { class: 'note' }, 'This log only has the round\u2019s end totals: each line runs straight from 0 to them.'));

      var columns = [
        { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
        { label: 'From zones held', value: function (r) { return r.zones; } },
        { label: 'From bounties', value: function (r) { return r.bounties; } },
      ];
      if (D.hasCentre) columns.push({ label: 'From the centre', value: function (r) { return r.centre; } });
      columns.push({ label: 'Total', value: function (r) { return r.total; } });
      holder.card.appendChild(el('h4', null, 'Where the points came from'));
      buildTable(holder.card, columns, race.sources || []);
    });
  }

  function renderZones(scope, suffix) {
    var host = id('zones', suffix);
    if (!needDominion(host)) return;
    var teams = R.teams || [];
    roundsOf(scope).forEach(function (n) {
      var rows = (D.zoneRounds || []).filter(function (z) { return z.round === n; });
      if (!rows.length) return;
      var card = el('div', { class: 'card' });
      card.appendChild(el('h3', null, 'Round ' + n));
      function held(zone, team) {
        var s = 0;
        (D.zoneHolds || []).forEach(function (h) { if (h.round === n && h.zone === zone && h.team === team) s += h.seconds; });
        return s;
      }
      var columns = [{ label: 'Zone', value: function (r) { return zoneLabel(r.zone); }, text: function (r) { return zoneLabel(r.zone); } }]
        .concat(teamColumns(teams,
          function (r, t) { return held(r.zone, t); },
          function (r, t) { var s = held(r.zone, t); return s > 0 ? len(s) + ' (' + pct(r.roundSeconds > 0 ? s / r.roundSeconds : 0) + ')' : '\u2013'; },
          function (r, t) { var best = -1, bestTeam = -1; teams.forEach(function (o) { var v = held(r.zone, o); if (v > best) { best = v; bestTeam = o; } }); return best > 0 && bestTeam === t; }))
        .concat([
          { label: 'Times taken', value: function (r) { return r.captures; } },
          { label: 'First taken after', value: function (r) { return r.firstCaptureAfter; }, text: function (r) { return r.firstCaptureAfter === null || r.firstCaptureAfter === undefined ? 'never' : len(r.firstCaptureAfter); } },
        ]);
      buildTable(card, columns, rows);

      var race = null;
      (D.races || []).forEach(function (rc) { if (rc.round === n) race = rc; });
      var start = race ? race.startT : 0, end = race ? race.endT : 0;
      var bounties = (D.bounties || []).filter(function (b) { return b.t >= start && b.t <= end; });
      var perTeam = {};
      bounties.forEach(function (b) { if (!perTeam[b.team]) perTeam[b.team] = { n: 0, p: 0 }; perTeam[b.team].n++; perTeam[b.team].p += b.points; });
      card.appendChild(el('div', null, 'Bounties taken: ' + (bounties.length ? Object.keys(perTeam).map(function (t) { return teamName(parseInt(t, 10)) + ' ' + perTeam[t].n + ' (' + perTeam[t].p + ' points)'; }).join(', ') : 'none')));
      if (D.hasCentre) {
        var payouts = (D.centrePayouts || []).filter(function (c) { return c.t >= start && c.t <= end; });
        var paid = {}, nobody = 0;
        payouts.forEach(function (c) { if (c.team < 0) nobody++; else paid[c.team] = (paid[c.team] || 0) + 1; });
        var parts = Object.keys(paid).map(function (t) { return teamName(parseInt(t, 10)) + ' ' + paid[t]; });
        if (nobody) parts.push(nobody + ' paid nobody');
        card.appendChild(el('div', null, 'Centre payouts: ' + (parts.length ? parts.join(', ') : 'none')));
      }
      host.appendChild(card);
    });
  }

  var SHIELD_REASONS = [['ran out', 'Ran out'], ['attacked', 'Broke by attacking'], ['left spawn', 'Broke by leaving the spawn'], ['died', 'Died with it up'], ['fresh start', 'A round or break started']];

  function renderFights(scope, suffix, tab) {
    var host = id('fights', suffix);
    var teams = R.teams || [];
    var deaths = rowsFor('deaths', scope);
    var perTeam = teams.map(function (t) {
      return { team: t,
               kills: deaths.filter(function (d) { return d.killer >= 0 && d.killerTeam === t && d.victimTeam !== t; }).length,
               deaths: deaths.filter(function (d) { return d.victimTeam === t; }).length };
    });
    var card = el('div', { class: 'card' });
    card.appendChild(el('h3', null, 'Kills and deaths'));
    buildTable(card, [
      { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Deaths', value: function (r) { return r.deaths; } },
    ], perTeam);
    host.appendChild(card);

    if (!D) return;
    var shields = (D.shields || []).filter(function (s) { return inWindow(scope, s.t) && keepActor(s.actor); });
    var shieldCard = el('div', { class: 'card' });
    shieldCard.appendChild(el('h3', null, 'How each respawn shield ended'));
    if (!shields.length) {
      shieldCard.appendChild(el('div', { class: 'note' }, 'No respawn shield was logged on this tab.'));
    } else {
      var rows = SHIELD_REASONS.map(function (r) { return { label: r[1], count: shields.filter(function (s) { return s.reason === r[0]; }).length }; })
        .filter(function (r) { return r.count > 0; });
      buildTable(shieldCard, [
        { label: 'Ended', value: function (r) { return r.label; }, text: function (r) { return r.label; } },
        { label: 'Shields', value: function (r) { return r.count; } },
      ], rows);
      var blocked = 0;
      shields.forEach(function (s) { blocked += s.blocked || 0; });
      shieldCard.appendChild(el('div', null, 'Hits and effects the shields stopped: ' + blocked));
    }
    host.appendChild(shieldCard);
  }

  function buildText(b) {
    var armour = [];
    if (b.absorb > 0) armour.push('absorb ' + b.absorb);
    if (b.recharge > 0) armour.push('recharge ' + b.recharge);
    return armour.length ? armour.join(', ') : 'none';
  }

  function renderBuilds(scope, suffix) {
    var host = id('builds', suffix);
    if (!needDominion(host)) return;
    var numbers = roundsOf(scope);
    numbers.forEach(function (n) {
      var rows = (D.builds || []).filter(function (b) { return b.round === n && keepActor(b.actor); });
      if (!rows.length) return;
      var card = el('div', { class: 'card' });
      if (scope === 'whole-match') card.appendChild(el('h3', null, 'Round ' + n));
      buildTable(card, [
        { label: 'Player', value: function (r) { return nickOf(r.actor); }, node: function (r) { return nameNode(r.actor); } },
        { label: 'Team', value: function (r) { return teamName(r.team); }, text: function (r) { return teamName(r.team); } },
        { label: 'Gun', value: function (r) { return weaponName(r.weapon); }, text: function (r) { return weaponName(r.weapon); } },
        { label: 'Attachment', value: function (r) { return abilityName(r.attachment); }, text: function (r) { return abilityName(r.attachment); } },
        { label: 'Movement', value: function (r) { return abilityName(r.mobility); }, text: function (r) { return abilityName(r.mobility); } },
        { label: 'Ultimate', value: function (r) { return abilityName(r.ultimate); }, text: function (r) { return abilityName(r.ultimate); } },
        { label: 'Armour', value: function (r) { return buildText(r); }, text: function (r) { return buildText(r); } },
      ], rows);
      host.appendChild(card);
    });

    var damage = {};
    numbers.forEach(function (n) {
      var bucket = DATA.scopes && DATA.scopes['round-' + n];
      ((bucket && bucket.weapons) || []).forEach(function (w) {
        if (!(w.damageRaw > 0)) return;
        if (!damage[w.weaponId]) damage[w.weaponId] = { weapon: w.weaponId, rounds: {} };
        damage[w.weaponId].rounds[n] = w.damageRaw;
      });
    });
    var gunRows = Object.keys(damage).map(function (k) { return damage[k]; });
    if (gunRows.length) {
      var card = el('div', { class: 'card' });
      card.appendChild(el('h3', null, 'Damage per gun'));
      buildTable(card, [{ label: 'Gun', value: function (r) { return weaponName(r.weapon); }, text: function (r) { return weaponName(r.weapon); } }]
        .concat(numbers.map(function (n) { return { label: 'Round ' + n, value: function (r) { return r.rounds[n] || 0; } }; })), gunRows);
      host.appendChild(card);
    }
  }

  function renderSuddenDeath(scope, suffix) {
    var host = id('sudden-death', suffix);
    if (!needDominion(host)) return;
    var sd = D.suddenDeath;
    if (!sd) { host.appendChild(el('div', { class: 'note' }, 'No sudden death was played.')); return; }
    function line(label, value) { host.appendChild(el('div', null, label + ': ' + value)); }
    line('Played for', sd.forRound > 0 ? 'round ' + sd.forRound : 'the match');
    line('Teams', (sd.teams || []).map(teamName).join(', '));
    line('Length', len(sd.lengthSeconds));
    if (sd.replays) line('Started over', sd.replays + ' time(s), everyone fell at once');
    line('The circle killed', (sd.circleKills || []).length ? sd.circleKills.map(function (a) { return nameText(a); }).join(', ') : 'nobody');
    line('Died to players', (sd.playerKills || []).length ? sd.playerKills.map(function (k) { return nameText(k[0]) + ' by ' + nameText(k[1]); }).join(', ') : 'nobody');
    line('Last team standing', sd.winner >= 0 ? teamName(sd.winner) : 'not recorded');
    var damageRows = Object.keys(sd.circleDamage || {}).map(function (a) { return { actor: parseInt(a, 10), damage: sd.circleDamage[a] }; })
      .filter(function (r) { return keepActor(r.actor); });
    if (damageRows.length) {
      host.appendChild(el('h4', null, 'Damage from the circle'));
      buildTable(host, [
        { label: 'Player', value: function (r) { return nickOf(r.actor); }, node: function (r) { return nameNode(r.actor); } },
        { label: 'Circle damage', value: function (r) { return r.damage; } },
      ], damageRows);
    }
  }

  // ---------------------------------------------------------------- render every tab

  var RENDERERS = {
    'header': renderMatchHeader, 'economy': renderEconomy, 'territory': renderTerritory, 'combat': renderCombat, 'map': renderMap,
    'players': renderPlayers, 'markers': renderMarkers, 'bugs': renderBugReports,
    'result': renderResult, 'points': renderPoints, 'zones': renderZones, 'fights': renderFights, 'builds': renderBuilds, 'sudden-death': renderSuddenDeath,
  };

  var pristine = {};
  TABS.forEach(function (tab) { var panel = document.getElementById('tab-' + tab.key); if (panel) pristine[tab.key] = panel.innerHTML; });

  function renderAll() {
    TABS.forEach(function (tab) {
      (charts[tab.key] || []).forEach(function (c) { c.destroy(); });
      charts[tab.key] = [];
      var panel = document.getElementById('tab-' + tab.key);
      if (!panel) return;
      panel.innerHTML = pristine[tab.key];
      renderTab(tab);
    });
  }

  renderTop();
  renderAll();
})();
</script>
</body>
</html>
";
    }
}
