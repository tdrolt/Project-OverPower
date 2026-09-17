using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Overpower.Data;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T6 step 3 / Task T7: one self-contained report.html per match folder. Every
    /// number comes straight from a ReportSet's three ReportTables (T5/T7) - CsvReportWriter and this
    /// class format the exact same data, so the CSVs and the HTML page can never disagree, only
    /// present differently (design doc, Outputs). The only inputs this class reads that ReportTables
    /// doesn't carry are:
    /// - raw `sample` x/z positions (never one of the 12/13 CSVs - see TelemetryLog directly) for the
    ///   position heatmap, and
    /// - BalanceTargets and the arena PNG, both reference material with no place in a CSV either.
    ///
    /// Task T7: the page has three tabs - Phase 1, Phase 2, Whole match - each rendering its OWN
    /// ReportTables through the same render functions, parameterized by (scope, suffix): `scope`
    /// picks which of DATA.phase1/phase2/wholeMatch a table read goes through (rowsFor), `suffix`
    /// picks which tab's own copy of every element id to write into (every id in the static template
    /// below carries a suffix, filled in by TabPanel/TabPanelTemplate.Replace). A missing Phase 2 (no
    /// elimination in this match) hides that tab's content behind a single note instead of rendering
    /// three empty sections.
    ///
    /// All data is embedded as one JSON blob (Newtonsoft, StringEscapeHandling.EscapeHtml) so a
    /// player-provided string (nickname, marker note) can never break out of the embedding
    /// &lt;script&gt; tag - every '&lt;', '&gt;', '&amp;' and quote becomes a \uXXXX escape, so the
    /// literal text "&lt;/script&gt;" can never appear in the emitted HTML at all. The page's own JS
    /// then writes every player-provided string via .textContent (never innerHTML) as a second,
    /// independent layer of the same protection.</summary>
    public static class HtmlReportWriter
    {
        /// <summary>Review fix (T6 item 5): a 9-player, 25-minute match at the shipped 5s sample
        /// interval would embed ~2700 samples PER PLAYER (24300 total) as raw JSON otherwise - capped
        /// so the report stays a reasonable size regardless of match length or player count.</summary>
        private const int MaxPositionSamples = 3000;

        public static string Write(ReportSet reportSet, TelemetryLog log, BalanceTargetsData targets, ArenaReportRender.Result arena, string folder)
        {
            reportSet ??= new ReportSet();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "report.html");
            List<PositionSample> positions = ExtractPositions(log, out int keepEveryN);
            string html = Build(reportSet, positions, keepEveryN, targets, arena);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        private sealed class PositionSample
        {
            public double T;
            public int Team;
            public float X;
            public float Z;
            public bool Alive;
            /// <summary>Task T7: 1 or 2, from this sample's own t - positions are a single shared list
            /// in the payload (not tripled per scope, to keep the report's size down); the page's own
            /// rowsFor('positions', scope) filters this client-side instead.</summary>
            public int Phase = 1;
        }

        /// <summary>Positions are only ever in the raw `sample` lines (design choice: not one of the
        /// CSVs - see the spec's own CSV table list), so this reads the log directly rather than
        /// going through TelemetryAggregator, which stays untouched by this whole task.
        ///
        /// Review fix (T6 item 5): down-sampled to at most <see cref="MaxPositionSamples"/> kept
        /// points - grouped per actor (own file) first, so a long match doesn't quietly lose an
        /// entire short-lived joiner's coverage to a global "keep every Nth line read" cut; keeping
        /// every Nth sample WITHIN each player's own sequence keeps their spatial coverage roughly
        /// even instead of just truncating to their earliest N samples.</summary>
        private static List<PositionSample> ExtractPositions(TelemetryLog log, out int keepEveryN)
        {
            keepEveryN = 1;
            var result = new List<PositionSample>();
            if (log == null) return result;

            double? tPhase2 = PhaseTimeline.From(log).TransitionSeconds;

            var fileActor = new Dictionary<string, int>();
            foreach (TelemetrySession s in log.Sessions)
                if (!fileActor.ContainsKey(s.File)) fileActor[s.File] = s.Actor;

            var byActor = new Dictionary<int, List<PositionSample>>();
            int total = 0;
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;

                var xToken = e.Data[TelemetryKeys.X];
                var zToken = e.Data[TelemetryKeys.Z];
                if (xToken == null || zToken == null) continue; // recordPositions was off for this line

                int actor = fileActor.GetValueOrDefault(e.File, -1);
                if (!byActor.TryGetValue(actor, out List<PositionSample> list))
                    byActor[actor] = list = new List<PositionSample>();

                list.Add(new PositionSample
                {
                    T = e.T,
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

        private static string Build(ReportSet reportSet, List<PositionSample> positions, int positionsKeptEveryN, BalanceTargetsData targets, ArenaReportRender.Result arena)
        {
            // Task T7: Phase 1's OWN window is [0, tPhase2) starting at 0, so its own
            // MatchLengthSeconds (End - Start) equals tPhase2 exactly - the transition instant,
            // without needing the log or PhaseTimeline again here.
            double? transitionSeconds = reportSet.Phase2 != null ? reportSet.Phase1?.Header.MatchLengthSeconds : (double?)null;

            var payload = new
            {
                phase1 = ScopePayload(reportSet.Phase1),
                phase2 = reportSet.Phase2 != null ? ScopePayload(reportSet.Phase2) : null,
                wholeMatch = ScopePayload(reportSet.WholeMatch),
                transitionSeconds,
                positions,
                positionsKeptEveryN,
                targets,
                arena,
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

            sb.Append(TabPanel("whole-match", "whole-match", true));
            sb.Append(TabPanel("phase-1", "phase-1", false));
            sb.Append(TabPanel("phase-2", "phase-2", false));

            sb.Append(HtmlBody);
            return sb.ToString();
        }

        private static object ScopePayload(ReportTables tables)
        {
            tables ??= new ReportTables();
            return new
            {
                header = tables.Header,
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

        /// <summary>Task T7: one tab's full section markup, with every element id suffixed by
        /// <paramref name="scope"/> (via TabPanelTemplate's %SCOPE% token) so the same render code can
        /// target three independent copies of the DOM, one per tab.</summary>
        private static string TabPanel(string tabKey, string scope, bool visibleByDefault)
        {
            string content = TabPanelTemplate.Replace("%SCOPE%", scope);
            string style = visibleByDefault ? "" : " style='display:none'";
            return "<div id='tab-" + tabKey + "' class='tab-panel'" + style + ">\n" + content + "\n</div>\n";
        }

        /// <summary>Review fix (T6 item 6): U+2028/U+2029 (LINE/PARAGRAPH SEPARATOR) are valid inside
        /// a JSON string but were - for a long time, and still in plenty of non-browser JS engines -
        /// NOT valid inside a JS string literal at all (only fixed for literals by ES2019). A
        /// nickname or marker note containing one, embedded raw, could corrupt the surrounding
        /// `const DATA = {...};` statement. Newtonsoft's StringEscapeHandling.EscapeHtml only
        /// escapes '&lt;'/'&gt;'/'&amp;'/quotes, not these, so this is a final pass over the whole
        /// serialized JSON text - safe as a blind replace because these two characters can only ever
        /// appear INSIDE a JSON string value's content, never as JSON's own (all-ASCII) structural
        /// syntax.</summary>
        private static string EscapeLineTerminators(string json)
        {
            return json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
        }

        // Single-quoted HTML attributes and JS strings throughout, on purpose: a C# verbatim string
        // only ends at an unescaped double quote, so keeping every quote in this template single
        // avoids a large block of "" escaping.
        private const string HtmlHead = @"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>OverPower Telemetry Report</title>
<style>
:root {
  --bg: #f5f6fa;
  --fg: #1b1c22;
  --card-bg: #ffffff;
  --border: #d8d9e0;
  --muted: #666a76;
  --warn-bg: #fff2d9;
  --warn-fg: #6b4600;
  --team0: #3a6df0;
  --team1: #e0473f;
  --team2: #2fa84f;
  --team3: #9a6df0;
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
    --team0: #6d94ff;
    --team1: #ff7a72;
    --team2: #57d47a;
    --team3: #c1a4ff;
  }
}
* { box-sizing: border-box; }
html, body { max-width: 100%; overflow-x: hidden; }
body { background: var(--bg); color: var(--fg); font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px 24px 60px; }
h1 { margin-top: 0; }
h2 { border-bottom: 1px solid var(--border); padding-bottom: 6px; }
h4 { margin: 0 0 6px; font-size: 13px; color: var(--muted); }
section { margin-bottom: 44px; }
.card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px; overflow-x: auto; min-width: 0; }
.card-desc { font-size: 12px; color: var(--muted); margin: -6px 0 12px; }
table { border-collapse: collapse; width: 100%; font-size: 13px; }
th, td { border-bottom: 1px solid var(--border); padding: 5px 8px; text-align: left; white-space: nowrap; }
th { cursor: pointer; user-select: none; color: var(--muted); font-weight: 600; }
th.sorted-asc::after { content: ' \25b2'; font-size: 10px; }
th.sorted-desc::after { content: ' \25bc'; font-size: 10px; }
.warn { background: var(--warn-bg); color: var(--warn-fg); padding: 8px 14px; border-radius: 6px; margin: 6px 0; font-size: 13px; }
.grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(360px, 100%), 1fr)); gap: 16px; min-width: 0; }
.chart-wrap { position: relative; height: 260px; min-width: 0; }
.chart-wrap.tall { height: 320px; }
canvas { max-width: 100%; }
.gantt-row { display: flex; align-items: center; gap: 8px; margin: 5px 0; }
.gantt-label { width: 96px; font-size: 12px; color: var(--muted); flex-shrink: 0; }
.gantt-track { position: relative; flex: 1; height: 16px; background: var(--border); border-radius: 3px; min-width: 0; }
.gantt-bar { position: absolute; top: 0; height: 100%; border-radius: 3px; min-width: 2px; }
.note { color: var(--muted); font-size: 12px; }
#arena-wrap, [id^='arena-wrap-'] { position: relative; display: inline-block; max-width: 100%; }
canvas[id^='arena-canvas-'] { border: 1px solid var(--border); border-radius: 6px; max-width: 100%; height: auto; background: #000; }
[id^='heatmap-legend-'] { font-size: 12px; color: var(--muted); margin: 8px 0; }
.legend-dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin: 0 5px 0 12px; vertical-align: middle; border: 1.5px solid rgba(0,0,0,0.6); }
.legend-dot:first-of-type { margin-left: 6px; }
details.card summary { cursor: pointer; font-weight: 600; }
pre { white-space: pre-wrap; word-break: break-word; font-size: 12px; }
.tab-bar { display: flex; gap: 4px; margin-bottom: 20px; border-bottom: 1px solid var(--border); }
.tab-button { background: none; border: none; border-bottom: 3px solid transparent; color: var(--muted); font: inherit; font-size: 14px; padding: 8px 14px; cursor: pointer; }
.tab-button.active { color: var(--fg); border-bottom-color: var(--team0); font-weight: 600; }
.tab-panel { display: block; min-width: 0; }
.phase-duration { font-size: 13px; color: var(--muted); margin: 0 0 16px; }
</style>
</head>
<body>
<h1>OverPower Telemetry Report</h1>
<div id='chart-fallback-note' class='warn' style='display:none'></div>

<div class='tab-bar' role='tablist'>
<button class='tab-button active' data-tab='whole-match' type='button'>Whole match</button>
<button class='tab-button' data-tab='phase-1' type='button'>Phase 1: 3 teams</button>
<button class='tab-button' data-tab='phase-2' type='button'>Phase 2: 2 teams</button>
</div>
";

        /// <summary>Task T7: one tab's inner markup - every id carries a %SCOPE% token, replaced with
        /// 'whole-match' / 'phase-1' / 'phase-2' by TabPanel. Structurally identical for every scope;
        /// only the DATA each scope's render calls read (via rowsFor) differs.</summary>
        private const string TabPanelTemplate = @"
<p class='phase-duration' id='phase-duration-%SCOPE%'></p>
<div id='phase2-empty-note-%SCOPE%' class='warn' style='display:none'>No team was eliminated in this match: everything is Phase 1.</div>
<div id='tab-content-%SCOPE%'>

<section id='section-header-%SCOPE%'>
<h2>Header</h2>
<div id='header-warnings-%SCOPE%'></div>
<div class='card' id='header-summary-%SCOPE%'></div>
<details class='card'><summary>Tuning snapshot</summary><pre id='header-tuning-%SCOPE%'></pre></details>
<div class='card'><h3>Player coverage</h3><div id='header-coverage-%SCOPE%'></div></div>
<div class='card'><h3>Log coverage</h3><p class='card-desc'>Every player seen anywhere in the match, and whether their own log file is present in this folder.</p><div id='log-coverage-%SCOPE%'></div></div>
</section>

<section id='section-economy-%SCOPE%'>
<h2>Economy</h2>
<div class='grid'>
<div class='card'>
<h3>Gold per player over time</h3>
<p class='card-desc'>Each player's gold balance over time. Flat lines mean saving; drops mean a purchase; steady climbs mean territory income.</p>
<div class='chart-wrap'><canvas id='chart-gold-per-player-%SCOPE%'></canvas></div>
</div>
<div class='card'>
<h3>Team income per second by tier</h3>
<p class='card-desc'>One small chart per team: gold/s from each tier, stacked, against the GDD's own reference bands (dashed, labelled at the right edge).</p>
<div id='team-income-charts-%SCOPE%' class='grid'></div>
</div>
<div class='card'>
<h3>Gold generated per zone by team</h3>
<p class='card-desc'>Total gold each team earned from each zone it held in this scope.</p>
<div class='chart-wrap'><canvas id='chart-zone-income-%SCOPE%'></canvas></div>
</div>
<div class='card'>
<h3>Gold gap to richest team</h3>
<p class='card-desc'>How far behind the richest team each team was, over time. 0 means that team WAS the richest at that minute.</p>
<div class='chart-wrap'><canvas id='chart-gold-gap-%SCOPE%'></canvas></div>
</div>
<div class='card'>
<h3>Purchase timeline vs targets</h3>
<p class='card-desc'>Every purchase (colour = category) against the GDD's target minutes (dashed vertical lines, labelled P1/A1/Ult/P2/A2 = Primary Upgrade 1, Armor 1, Ultimate, Primary Upgrade 2, Armor 2).</p>
<div class='chart-wrap'><canvas id='chart-purchase-timeline-%SCOPE%'></canvas></div>
</div>
</div>
<div class='grid'>
<div class='card'><h3>Unspent gold (at death / at end)</h3><div id='table-unspent-gold-%SCOPE%'></div></div>
<div class='card'><h3>Blocked purchases by reason</h3><div id='table-shop-blocked-%SCOPE%'></div></div>
</div>
</section>

<section id='section-territory-%SCOPE%'>
<h2>Territory</h2>
<div class='card'>
<h3>Ownership timeline</h3>
<p class='card-desc'>Each row is one zone; a coloured bar is one team's uninterrupted holding of it. Hover a bar for its exact start/end time and how it ended.</p>
<div id='ownership-gantt-%SCOPE%'></div>
</div>
<div class='grid'>
<div class='card'>
<h3>Zones held per team over time</h3>
<p class='card-desc'>How many zones (of any tier) each team held at each minute. Rising means expanding; falling means losing ground.</p>
<div class='chart-wrap'><canvas id='chart-zones-held-%SCOPE%'></canvas></div>
</div>
<div class='card'><h3>Bounty total by team</h3><div id='table-bounty-%SCOPE%'></div></div>
</div>
<div class='card'><h3>Captures</h3><div id='table-captures-%SCOPE%'></div></div>
</section>

<section id='section-combat-%SCOPE%'>
<h2>Combat</h2>
<div class='card'>
<h3>Weapons</h3>
<p class='card-desc'>Damage per minute a weapon was actually equipped, so weapons used for very different lengths of time are still comparable.</p>
<div class='chart-wrap'><canvas id='chart-weapons-%SCOPE%'></canvas></div>
<div id='table-weapons-%SCOPE%'></div>
</div>
<div class='card'><h3>Abilities</h3><div id='table-abilities-%SCOPE%'></div></div>
<div class='card'>
<h3>Team versus team damage</h3>
<p class='card-desc' id='damage-matrix-desc-%SCOPE%'></p>
<div id='table-damage-matrix-%SCOPE%'></div>
</div>
<div class='card' id='heatmaps-%SCOPE%'>
<h3>Death and position heatmaps</h3>
<p class='card-desc'>Dots plotted on a top-down render of the arena. Deaths are solid, outlined, coloured by the victim's team; positions are small translucent samples showing where players spent time. <span id='position-sample-note-%SCOPE%'></span></p>
<div>
<label><input type='checkbox' id='toggle-deaths-%SCOPE%' checked> Deaths</label>
&nbsp;&nbsp;
<label><input type='checkbox' id='toggle-positions-%SCOPE%'> Positions</label>
</div>
<div id='heatmap-legend-%SCOPE%'></div>
<div id='arena-wrap-%SCOPE%'><canvas id='arena-canvas-%SCOPE%' width='1024' height='1024'></canvas></div>
<div id='arena-note-%SCOPE%' class='note'></div>
</div>
</section>

<section id='section-players-%SCOPE%'>
<h2>Players</h2>
<div class='card'><div id='table-players-%SCOPE%'></div></div>
</section>

<section id='section-markers-%SCOPE%'>
<h2>Markers</h2>
<div id='markers-list-%SCOPE%'></div>
</section>

</div>
";

        private const string HtmlBody = @"<script src='https://cdn.jsdelivr.net/npm/chart.js@4'></script>
<script>
(function () {
  'use strict';

  function id(base, suffix) { return document.getElementById(base + '-' + suffix); }

  function el(tag, attrs, text) {
    var e = document.createElement(tag);
    if (attrs) { for (var k in attrs) { if (attrs.hasOwnProperty(k)) e.setAttribute(k, attrs[k]); } }
    if (text !== undefined && text !== null) e.textContent = text;
    return e;
  }

  function fmt(n) {
    if (n === null || n === undefined) return '';
    if (typeof n === 'number') {
      if (!isFinite(n)) return '';
      return String(Math.round(n * 100) / 100);
    }
    return String(n);
  }

  function minutes(t) { return (t || 0) / 60; }

  // Task T7: which of DATA.phase1/phase2/wholeMatch a scope key reads from.
  function bucketFor(scope) {
    if (scope === 'phase-1') return DATA.phase1;
    if (scope === 'phase-2') return DATA.phase2;
    return DATA.wholeMatch;
  }

  // Every render function reads its table through this instead of DATA[table] directly - the one
  // seam every section is built on. `positions` is the exception: it's ONE shared list (not
  // tripled per scope, to keep the report's size down), tagged with which phase each sample fell
  // in, filtered here instead.
  function rowsFor(tableName, scope) {
    if (tableName === 'positions') {
      var all = DATA.positions || [];
      if (scope === 'whole-match') return all;
      var wantPhase = (scope === 'phase-1') ? 1 : 2;
      return all.filter(function (p) { return p.phase === wantPhase; });
    }
    var bucket = bucketFor(scope);
    return (bucket && bucket[tableName]) || [];
  }

  // Every chart builds its options through this one function - x/y titles, responsive sizing (so a
  // fixed-height .chart-wrap controls the actual pixel size instead of Chart.js guessing one), and
  // an optional legend filter for datasets carrying a `refLabel` (drawn in-chart instead - see
  // refLineLabelPlugin below).
  function chartOptions(xLabel, yLabel, config) {
    config = config || {};
    var xScale = { title: { display: true, text: xLabel } };
    if (config.xType) xScale.type = config.xType;
    if (config.stacked) xScale.stacked = true;
    var yScale = { title: { display: true, text: yLabel } };
    if (config.stacked) yScale.stacked = true;
    var options = { responsive: true, maintainAspectRatio: false, scales: { x: xScale, y: yScale } };
    if (config.parsing === false) options.parsing = false;
    if (config.hideRefLinesFromLegend) {
      options.plugins = { legend: { labels: { filter: function (item, data) { return !data.datasets[item.datasetIndex].refLabel; } } } };
    }
    return options;
  }

  // A dashed reference-line dataset (a GDD scenario income, a GDD purchase-timing target, the
  // 'Phase 2 starts' marker) carries its own short `refLabel` instead of a legend entry - a legend
  // with one entry per team/tier PLUS one per reference line was the exact 'legend swamps the plot'
  // bug found in review. This plugin draws that short label right next to the line's own last point
  // instead: at the right end for a horizontal line (same y twice), at the top end for a vertical one.
  var refLineLabelPlugin = {
    id: 'refLineLabels',
    afterDatasetsDraw: function (chart) {
      var ctx = chart.ctx;
      chart.data.datasets.forEach(function (ds, i) {
        if (!ds.refLabel) return;
        var meta = chart.getDatasetMeta(i);
        if (!meta || meta.hidden || !meta.data || meta.data.length < 2) return;
        var p0 = meta.data[0], p1 = meta.data[meta.data.length - 1];
        if (p0.x === undefined || p1.x === undefined) return;
        ctx.save();
        ctx.font = '11px system-ui, sans-serif';
        ctx.fillStyle = '#888';
        var vertical = Math.abs(p0.x - p1.x) < 1;
        if (vertical) {
          ctx.textAlign = 'center';
          ctx.fillText(ds.refLabel, p1.x, Math.min(p0.y, p1.y) - 4);
        } else {
          ctx.textAlign = 'left';
          ctx.fillText(ds.refLabel, p1.x + 4, p1.y + 3);
        }
        ctx.restore();
      });
    },
  };

  var tabButtons = document.querySelectorAll('.tab-button');
  var tabPanels = document.querySelectorAll('.tab-panel');
  for (var ti = 0; ti < tabButtons.length; ti++) {
    tabButtons[ti].addEventListener('click', function () {
      var target = this.getAttribute('data-tab');
      for (var bi = 0; bi < tabButtons.length; bi++) tabButtons[bi].classList.remove('active');
      this.classList.add('active');
      for (var pi = 0; pi < tabPanels.length; pi++) {
        tabPanels[pi].style.display = (tabPanels[pi].id === 'tab-' + target) ? 'block' : 'none';
      }
    });
  }

  // One section's bug (or an unexpected environment quirk - a chart library throwing, a
  // malformed row) must never blank out every section below it. Each top-level render section
  // runs through this, so a thrown error is logged and surfaced as a small warning instead of
  // silently truncating the rest of the report.
  function safeRun(name, fn) {
    try {
      fn();
    } catch (err) {
      if (window.console && console.error) console.error('[report] ' + name + ' section failed:', err);
      var host = document.getElementById('chart-fallback-note');
      var msg = el('div', { class: 'warn' });
      msg.textContent = 'The ' + name + ' section hit an error and may be incomplete (see the browser console for details).';
      host.parentNode.insertBefore(msg, host.nextSibling);
    }
  }

  function uniqueSorted(arr) {
    var seen = {};
    var out = [];
    for (var i = 0; i < arr.length; i++) {
      var v = arr[i];
      if (!(v in seen)) { seen[v] = true; out.push(v); }
    }
    out.sort(function (a, b) { return a - b; });
    return out;
  }

  var TEAM_COLORS = ['#3a6df0', '#e0473f', '#2fa84f', '#9a6df0'];
  function teamColor(team) {
    var i = ((team % TEAM_COLORS.length) + TEAM_COLORS.length) % TEAM_COLORS.length;
    return TEAM_COLORS[i];
  }

  // The tuning snapshot is match-wide (identical across every scope) - read once, from whichever
  // scope always exists (wholeMatch).
  var tuning = null;
  try {
    var wmHeader = DATA.wholeMatch && DATA.wholeMatch.header;
    tuning = wmHeader && wmHeader.tuningJson ? JSON.parse(wmHeader.tuningJson) : null;
  } catch (parseErr) { tuning = null; }

  function nameFromList(list, id2) {
    if (!list) return null;
    for (var i = 0; i < list.length; i++) { if (list[i].id === id2) return list[i].name; }
    return null;
  }
  // 'Rocket (2)' when the tuning snapshot has a name for this id, else a plain 'Weapon 2' fallback
  // (an older log with no matching entry, or an id outside the catalogue).
  function weaponName(wid) {
    var n = tuning && nameFromList(tuning.weapons, wid);
    return n ? (n + ' (' + wid + ')') : ('Weapon ' + wid);
  }
  function abilityName(aid) {
    var n = tuning && nameFromList(tuning.abilities, aid);
    return n ? (n + ' (' + aid + ')') : ('Ability ' + aid);
  }

  // Zone id -> tier, built once from the WHOLE MATCH's own ownership/zoneIncome (a zone's tier
  // never changes across phases, so there is no need to rebuild this per scope).
  var zoneTierCache = null;
  function zoneTier(zoneId) {
    if (zoneTierCache === null) {
      zoneTierCache = {};
      (rowsFor('ownership', 'whole-match') || []).forEach(function (o) { if (!(o.zone in zoneTierCache)) zoneTierCache[o.zone] = o.tier; });
      (rowsFor('zoneIncome', 'whole-match') || []).forEach(function (z) { if (!(z.zone in zoneTierCache)) zoneTierCache[z.zone] = z.tier; });
    }
    return zoneTierCache[zoneId];
  }
  // 'Zone 6 · T1 capital' (tier 1 is always a team's capital - TerritoryConfig's own tier-0 row
  // comment) or 'Zone 0 · T2' for any other tier, falling back to a plain 'Zone N' if no ownership
  // or income row ever named this zone's tier (never captured, or a malformed/partial log).
  function zoneLabel(zoneId) {
    if (zoneId === -1) return 'Unattributed';
    var tier = zoneTier(zoneId);
    if (tier === undefined || tier === null) return 'Zone ' + zoneId;
    return 'Zone ' + zoneId + ' · T' + tier + (tier === 1 ? ' capital' : '');
  }

  var chartsOk = (typeof Chart !== 'undefined');
  if (!chartsOk) {
    var fallbackNote = document.getElementById('chart-fallback-note');
    fallbackNote.textContent = 'Chart.js failed to load from the CDN (no internet access?) - charts are skipped, every table below still works.';
    fallbackNote.style.display = 'block';
  }

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

  function buildTable(container, columns, rows) {
    var table = el('table');
    var thead = el('thead');
    var headRow = el('tr');
    for (var c = 0; c < columns.length; c++) headRow.appendChild(el('th', null, columns[c].label));
    thead.appendChild(headRow);
    table.appendChild(thead);
    var tbody = el('tbody');
    for (var r = 0; r < rows.length; r++) {
      var row = rows[r];
      var tr = el('tr');
      for (var c2 = 0; c2 < columns.length; c2++) {
        var v = columns[c2].value(row);
        var td = el('td', { 'data-v': (v === null || v === undefined) ? '' : v });
        td.textContent = fmt(v);
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    container.appendChild(table);
    makeSortable(table);
    return table;
  }

  // ---------------------------------------------------------------- Task T7 (review fix): reference overlays
  //
  // Drawn directly on the canvas (afterDraw), never as Chart.js datasets. A dataset-based
  // vertical/horizontal line only positions correctly when it shares its sibling datasets' own
  // axis TYPE and UNIT - a review found the old phaseBoundaryDataset using minutes on a
  // seconds-axis chart (gold per player), and using {x,y} points on a CATEGORY axis at all (gold
  // gap, zones held, team income), which Chart.js reads as a bare index and draws diagonally
  // instead of vertically. A canvas overlay only needs the scale's own getPixelForValue - for a
  // category axis, that means snapping to the nearest actual label's index. A horizontal
  // reference band drawn this way also needs no minimum number of data points to appear (the old
  // dataset-based scenario bands silently vanished whenever fewer than 2 minute labels fell after
  // the transition) - it spans from the scale's own left/right pixel bounds instead.

  var overlayPlugin = {
    id: 'overlayLines',
    afterDraw: function (chart) {
      var cfg = chart.options.plugins && chart.options.plugins.overlayLines;
      if (!cfg) return;
      var ctx = chart.ctx;
      var xScale = chart.scales.x, yScale = chart.scales.y;
      if (!xScale || !yScale) return;
      var area = chart.chartArea;

      function pixelForX(spec) {
        if (spec.categoryMinutes) {
          // Round-2 review fix (item C minor): economy_by_minute buckets are FLOORED
          // (Math.floor(t / 60)), so a transition at 114s (1.9 minutes) belongs to minute 1's
          // bucket - snapping to the nearest label instead (round(1.9) = 2) put the line one
          // bucket late. Snap to the label nearest Math.floor(the raw minutes value) instead.
          var target = Math.floor(spec.value), idx = 0, best = Infinity;
          for (var i = 0; i < spec.categoryMinutes.length; i++) {
            var d = Math.abs(spec.categoryMinutes[i] - target);
            if (d < best) { best = d; idx = i; }
          }
          return xScale.getPixelForValue(idx);
        }
        return xScale.getPixelForValue(spec.value);
      }

      // Round-2 review fix (item C): clip the LINE geometry to the chart's own plot area, so a
      // band whose value the y-axis doesn't actually cover (see the suggestedMax fix at the
      // team-income chart's own construction, below) draws a clearly-truncated line at the edge
      // instead of one that floats above the plot or off the canvas entirely. Labels are drawn
      // in a second, UNCLIPPED pass (clipping would just make an edge label disappear instead of
      // fixing anything), each x-position clamped to stay just inside the area.
      var boundaryPx = null;
      var labels = [];

      ctx.save();
      ctx.beginPath();
      ctx.rect(area.left, area.top, area.right - area.left, area.bottom - area.top);
      ctx.clip();

      if (cfg.boundary) {
        boundaryPx = pixelForX(cfg.boundary);
        ctx.save();
        ctx.strokeStyle = '#c0392b';
        ctx.setLineDash([2, 2]);
        ctx.beginPath();
        ctx.moveTo(boundaryPx, area.top);
        ctx.lineTo(boundaryPx, area.bottom);
        ctx.stroke();
        ctx.restore();
        labels.push({ x: boundaryPx, y: area.top + 10, text: 'Phase 2 starts', color: '#c0392b', align: 'center' });
      }

      (cfg.bands || []).forEach(function (b) {
        var xFrom = (b.side === 'after') ? (boundaryPx != null ? boundaryPx : area.left) : area.left;
        var xTo = (b.side === 'before') ? (boundaryPx != null ? boundaryPx : area.right) : area.right;
        if (xTo <= xFrom) return;
        var y = yScale.getPixelForValue(b.value);
        ctx.save();
        ctx.strokeStyle = '#888';
        ctx.setLineDash(b.dash || [5, 4]);
        ctx.beginPath();
        ctx.moveTo(xFrom, y);
        ctx.lineTo(xTo, y);
        ctx.stroke();
        ctx.restore();
        labels.push({ x: Math.min(xTo + 4, area.right - 2), y: Math.max(area.top + 8, Math.min(y + 3, area.bottom - 2)), text: b.label, color: '#888', align: 'left' });
      });

      ctx.restore(); // undo the clip - labels below are drawn unclipped, positions already clamped inside the area.

      labels.forEach(function (l) {
        ctx.save();
        ctx.fillStyle = l.color;
        ctx.font = '11px system-ui, sans-serif';
        ctx.textAlign = l.align;
        ctx.fillText(l.text, l.x, l.y);
        ctx.restore();
      });
    },
  };

  function phase1ScenarioLines() {
    var t = DATA.targets;
    if (!t || !t.phase1ScenarioIncomePerTeam) return [];
    var s = t.phase1ScenarioIncomePerTeam;
    return [['Lo', s.losing], ['St', s.struggling], ['Av', s.average], ['Do', s.dominant]];
  }
  function phase2ScenarioLines() {
    var t = DATA.targets;
    if (!t || !t.phase2ScenarioIncomePerTeam) return [];
    var s = t.phase2ScenarioIncomePerTeam;
    return [['Lo', s.losing], ['Ev', s.even], ['Wi', s.winning]];
  }

  // Overlay boundary spec for a given chart's own x-axis unit - 'seconds'/'minutes' for a linear
  // axis, or pass categoryMinutes (that chart's own minutesList) to snap to the nearest label on a
  // category axis. Null (no boundary drawn at all) on the Phase 1/Phase 2 tabs, and on the
  // whole-match tab when the match never had a Phase 2.
  function boundaryOverlaySpec(scope, unit, categoryMinutes) {
    if (scope !== 'whole-match' || DATA.transitionSeconds == null) return null;
    if (unit === 'seconds') return { value: DATA.transitionSeconds };
    var m = minutes(DATA.transitionSeconds);
    return categoryMinutes ? { value: m, categoryMinutes: categoryMinutes } : { value: m };
  }

  // Scenario income bands as overlay bands instead of datasets - see the class comment above.
  // Phase 1/Phase 2 tabs draw their own single set across the whole chart; the whole-match tab
  // draws Phase 1's set up to the transition and Phase 2's after (labelled …1/…2 to tell them
  // apart, since both tabs' bands share short codes like 'Lo').
  function scenarioBandsForScope(scope) {
    if (scope === 'phase-1') return phase1ScenarioLines().map(function (b) { return { label: b[0], value: b[1], side: 'full', dash: [5, 4] }; });
    if (scope === 'phase-2') return phase2ScenarioLines().map(function (b) { return { label: b[0], value: b[1], side: 'full', dash: [5, 4] }; });
    var bands = phase1ScenarioLines().map(function (b) { return { label: b[0] + '1', value: b[1], side: 'before', dash: [5, 4] }; });
    if (DATA.transitionSeconds != null)
      bands = bands.concat(phase2ScenarioLines().map(function (b) { return { label: b[0] + '2', value: b[1], side: 'after', dash: [2, 3] }; }));
    return bands;
  }

  // Review fix (item 11): called for EVERY scope, even Phase 2 with no data - the tab still gets a
  // proper heading ('Phase 2 (2 teams)') above the 'no elimination' note, instead of staying a
  // blank paragraph next to it.
  function renderPhaseDuration(scope, suffix) {
    var bucket = bucketFor(scope);
    var host = id('phase-duration', suffix);
    var targets = DATA.targets;
    var label, targetSeconds;
    if (scope === 'phase-1') { label = 'Phase 1 (3 teams)'; targetSeconds = targets && targets.phase1DurationSeconds; }
    else if (scope === 'phase-2') { label = 'Phase 2 (2 teams)'; targetSeconds = targets && targets.phase2DurationSeconds; }
    else { label = 'Whole match'; targetSeconds = targets && targets.targetMatchSeconds; }

    if (!bucket) { host.textContent = label; return; }

    var length = bucket.header.matchLengthSeconds;
    var text = label + ': ' + fmt(length) + 's';
    if (targetSeconds) text += ' (GDD target ' + fmt(targetSeconds) + 's)';
    host.textContent = text;
  }

  // ---------------------------------------------------------------- header

  function renderHeader(scope, suffix) {
    var bucket = bucketFor(scope);
    var h = (bucket && bucket.header) || {};
    var warnDiv = id('header-warnings', suffix);
    function warn(text) { warnDiv.appendChild(el('div', { class: 'warn' }, text)); }
    if (h.freeLoadoutUsed) warn('Free Loadout was ON for at least part of this match - purchase and economy numbers are not representative of a real match.');
    if (h.debugGoldUsed) warn('The F1 debug gold cheat was used during this match.');
    if (h.malformedLineCount) warn(h.malformedLineCount + ' malformed log line(s) were skipped.');
    if (h.unknownEventCount) warn(h.unknownEventCount + ' unknown event(s) were skipped.');
    if (h.unknownCaptureStateCount) warn(h.unknownCaptureStateCount + ' capture line(s) had an unrecognised state.');
    if (h.invalidTierCount) warn(h.invalidTierCount + ' zone(s) had an invalid tier (0 or higher than 4) and were left out of tier-based income and zones-held totals.');
    if (h.unreadableFileCount) warn(h.unreadableFileCount + ' log file(s) could not be read.');
    if (h.newerSchemaCount) warn(h.newerSchemaCount + ' session(s) used a newer schema than this report understands.');
    if (h.otherMatchId) warn('This folder also holds ' + h.otherMatchFileCount + ' file(s) from a different match (' + h.otherMatchId + ') - not merged into this report.');
    if (h.eliminationFallbackUsed) warn('An elimination was logged without a matching phase event; Phase 2 start was taken from the elimination instead.');

    var summary = id('header-summary', suffix);
    function line(label, value) { summary.appendChild(el('div', null, label + ': ' + value)); }
    line('Match id', h.matchId || '(none)');
    line('Length', fmt(h.matchLengthSeconds) + 's (' + fmt(minutes(h.matchLengthSeconds)) + ' min)');
    line('Players per team', h.playersPerTeam);
    line('Commits', (h.commits || []).join(', ') || '(none)');

    id('header-tuning', suffix).textContent = h.tuningJson ? JSON.stringify(JSON.parse(h.tuningJson), null, 2) : '(none)';

    buildTable(id('header-coverage', suffix), [
      { label: 'Actor', value: function (r) { return r.actor; } },
      { label: 'Nick', value: function (r) { return r.nick; } },
      { label: 'First t', value: function (r) { return r.firstT; } },
      { label: 'Last t', value: function (r) { return r.lastT; } },
    ], h.coverage || []);

    // Task T7: log coverage - every actor seen anywhere (joins, sessions, hit attackers/victims,
    // death killers/assists), whether their own file is present, and an explicit warning line per
    // missing one. Whole-match fact - identical across every scope's own header.
    var coverageHost = id('log-coverage', suffix);
    var logCoverage = h.logCoverage || [];
    logCoverage.filter(function (r) { return !r.filePresent; }).forEach(function (r) {
      var label2 = r.nick ? (r.nick + ' (actor ' + r.actor + ')') : ('actor ' + r.actor);
      // Review fix (item 10): a softer message for an actor who joined and left (per OTHER
      // clients' own join/leave lines) before their own file ever opened - a much less alarming
      // story than 'no log from them at all, nothing about them is counted'.
      var message = r.joinedAndLeftBeforeLoggingStarted
        ? label2 + ' joined and left before logging started.'
        : 'No log from ' + label2 + ': their damage taken, gold and purchases aren\u2019t counted.';
      coverageHost.appendChild(el('div', { class: 'warn' }, message));
    });
    buildTable(coverageHost, [
      { label: 'Actor', value: function (r) { return r.actor; } },
      { label: 'Nick', value: function (r) { return r.nick; } },
      { label: 'File present', value: function (r) { return r.filePresent ? 'yes' : 'no'; } },
      { label: 'First t', value: function (r) { return (r.firstT === null || r.firstT === undefined) ? '-' : r.firstT; } },
      { label: 'Last t', value: function (r) { return (r.lastT === null || r.lastT === undefined) ? '-' : r.lastT; } },
    ], logCoverage);
  }

  // ---------------------------------------------------------------- economy

  function renderEconomy(scope, suffix) {
    var gt = rowsFor('goldTimeline', scope);
    var byActor = {};
    for (var i = 0; i < gt.length; i++) {
      var row = gt[i];
      if (!byActor[row.actor]) byActor[row.actor] = { nick: row.nick, team: row.team, points: [] };
      byActor[row.actor].points.push({ x: row.t, y: row.balance });
    }
    if (chartsOk) {
      var goldDatasets = [];
      var actorKeys = Object.keys(byActor);
      for (var k = 0; k < actorKeys.length; k++) {
        var a = byActor[actorKeys[k]];
        goldDatasets.push({ label: a.nick + ' (team ' + a.team + ')', data: a.points, borderColor: teamColor(a.team), backgroundColor: teamColor(a.team), fill: false, pointRadius: 1, tension: 0.1 });
      }
      // Review fix (item 3a): this chart's own x axis is SECONDS (row.t, above), not minutes -
      // the boundary overlay must use the same unit.
      var goldOpts = chartOptions('seconds', 'gold', { xType: 'linear', parsing: false });
      goldOpts.plugins = { overlayLines: { boundary: boundaryOverlaySpec(scope, 'seconds') } };
      new Chart(id('chart-gold-per-player', suffix), {
        type: 'line',
        data: { datasets: goldDatasets },
        options: goldOpts,
        plugins: [overlayPlugin],
      });
    }

    var eb = rowsFor('economyByMinute', scope);
    var teams = uniqueSorted(eb.map(function (r) { return r.team; }));
    var minutesList = uniqueSorted(eb.map(function (r) { return r.minute; }));

    var teamIncomeContainer = id('team-income-charts', suffix);
    var tierColors = ['#c9d6ff', '#8fa8ff', '#5a7cf7', '#2b52d6'];
    for (var ti2 = 0; ti2 < teams.length; ti2++) {
      var incomeTeam = teams[ti2];
      var teamCard = el('div', { class: 'card' });
      teamCard.appendChild(el('h4', null, 'Team ' + incomeTeam));
      var wrap = el('div', { class: 'chart-wrap' });
      var canvas = el('canvas');
      wrap.appendChild(canvas);
      teamCard.appendChild(wrap);
      teamIncomeContainer.appendChild(teamCard);

      if (chartsOk) {
        var teamDatasets = [];
        for (var tier = 0; tier < 4; tier++) {
          teamDatasets.push({
            label: 'Tier ' + (tier + 1),
            stack: 'tiers',
            backgroundColor: tierColors[tier],
            data: minutesList.map(function (m) {
              var matchRow = null;
              for (var ei = 0; ei < eb.length; ei++) { if (eb[ei].team === incomeTeam && eb[ei].minute === m) { matchRow = eb[ei]; break; } }
              return matchRow ? (matchRow.incomeByTier[tier] / 60) : 0;
            }),
          });
        }
        // Review fix (item 3): this chart uses a CATEGORY x-axis (labels: minutesList below) - the
        // scenario bands and the 'Phase 2 starts' line are both drawn as canvas overlays now, snapped
        // to the nearest category label, instead of {x,y} datasets a category axis can't place.
        var teamOpts = chartOptions('minute', 'gold/s', { stacked: true });
        var teamBands = scenarioBandsForScope(scope);
        // Round-2 review fix (item C regression): a band is drawn as an overlay now (not a
        // dataset), so it no longer contributes to Chart.js's own auto-scaling - with bands up to
        // 33 and a team earning ~10 gold/s, the 15/23/33 lines used to draw above the plot or off
        // the canvas. suggestedMax only ever RAISES the ceiling (Chart.js still expands further if
        // the stacked data itself is taller), so this is a floor, not a hard cap.
        var maxBandValue = 0;
        teamBands.forEach(function (b) { if (b.value > maxBandValue) maxBandValue = b.value; });
        if (maxBandValue > 0) teamOpts.scales.y.suggestedMax = maxBandValue * 1.05;
        teamOpts.plugins = {
          overlayLines: {
            boundary: boundaryOverlaySpec(scope, 'minutes', minutesList),
            bands: teamBands,
          },
        };
        new Chart(canvas, {
          type: 'bar',
          data: { labels: minutesList, datasets: teamDatasets },
          options: teamOpts,
          plugins: [overlayPlugin],
        });
      }
    }

    var zi = rowsFor('zoneIncome', scope);
    var zones = uniqueSorted(zi.map(function (r) { return r.zone; }));
    if (chartsOk) {
      var zoneDatasets = [];
      for (var zt = 0; zt < teams.length; zt++) {
        var zoneTeam = teams[zt];
        zoneDatasets.push({
          label: 'Team ' + zoneTeam, backgroundColor: teamColor(zoneTeam),
          data: zones.map(function (z) {
            var found = 0;
            for (var zi2 = 0; zi2 < zi.length; zi2++) { if (zi[zi2].zone === z && zi[zi2].team === zoneTeam) { found = zi[zi2].goldGenerated; break; } }
            return found;
          }),
        });
      }
      new Chart(id('chart-zone-income', suffix), {
        type: 'bar',
        data: { labels: zones.map(zoneLabel), datasets: zoneDatasets },
        options: chartOptions('zone', 'gold')
      });
    }

    if (chartsOk) {
      var gapDatasets = [];
      for (var gi = 0; gi < teams.length; gi++) {
        var gapTeam = teams[gi];
        gapDatasets.push({
          label: 'Team ' + gapTeam, borderColor: teamColor(gapTeam), fill: false, pointRadius: 1,
          data: minutesList.map(function (m) {
            for (var ei2 = 0; ei2 < eb.length; ei2++) { if (eb[ei2].team === gapTeam && eb[ei2].minute === m) return eb[ei2].goldGapToRichest; }
            return null;
          }),
        });
      }
      // Review fix (item 3b): category axis (labels: minutesList) - snap the boundary to the
      // nearest label instead of a {x,y} dataset point, which Chart.js read as a bare index and
      // drew diagonally.
      var gapOpts = chartOptions('minute', 'gold behind richest team');
      gapOpts.plugins = { overlayLines: { boundary: boundaryOverlaySpec(scope, 'minutes', minutesList) } };
      new Chart(id('chart-gold-gap', suffix), {
        type: 'line',
        data: { labels: minutesList, datasets: gapDatasets },
        options: gapOpts,
        plugins: [overlayPlugin],
      });
    }

    var purchases = rowsFor('purchases', scope);
    if (chartsOk) {
      var byCategory = {};
      for (var p = 0; p < purchases.length; p++) {
        var pr = purchases[p];
        if (pr.kind !== 'purchase') continue;
        var cat = pr.category || 'other';
        if (!byCategory[cat]) byCategory[cat] = [];
        byCategory[cat].push({ x: minutes(pr.t), y: pr.amount });
      }
      var catNames = Object.keys(byCategory);
      var catColors = ['#3a6df0', '#e0473f', '#2fa84f', '#9a6df0', '#e0a83f'];
      var purchaseDatasets = [];
      for (var cn = 0; cn < catNames.length; cn++) {
        purchaseDatasets.push({ label: catNames[cn], data: byCategory[catNames[cn]], backgroundColor: catColors[cn % catColors.length], pointRadius: 4 });
      }
      var maxAmount = 1;
      for (var pi = 0; pi < purchases.length; pi++) { if ((purchases[pi].amount || 0) > maxAmount) maxAmount = purchases[pi].amount; }
      maxAmount *= 1.15;
      var targetsP = DATA.targets ? DATA.targets.purchaseTargetMinutes : null;
      if (targetsP) {
        var lines = [
          ['P1', targetsP.primaryUpgrade1Minutes],
          ['A1', targetsP.armor1Minutes],
          ['Ult', targetsP.ultimateMinutes],
          ['P2', targetsP.primaryUpgrade2Minutes],
          ['A2', targetsP.armor2Minutes],
        ];
        for (var li = 0; li < lines.length; li++) {
          purchaseDatasets.push({
            type: 'line', label: lines[li][0], refLabel: lines[li][0],
            data: [{ x: lines[li][1], y: 0 }, { x: lines[li][1], y: maxAmount }],
            borderColor: '#888', borderDash: [5, 4], pointRadius: 0, fill: false,
          });
        }
      }
      // The P1/A1/Ult/P2/A2 target lines above stay dataset-based (a real, always-2-point line on
      // this chart's own genuinely linear/minutes axis, which was never the bug here) - only the
      // 'Phase 2 starts' marker moves to the overlay plugin, alongside them.
      var purchaseOpts = chartOptions('minute', 'price', { hideRefLinesFromLegend: true });
      purchaseOpts.plugins.overlayLines = { boundary: boundaryOverlaySpec(scope, 'minutes') };
      new Chart(id('chart-purchase-timeline', suffix), {
        type: 'scatter',
        data: { datasets: purchaseDatasets },
        options: purchaseOpts,
        plugins: [refLineLabelPlugin, overlayPlugin],
      });
    }

    var lastBalance = {};
    for (var g = 0; g < gt.length; g++) { lastBalance[gt[g].actor] = gt[g]; }
    var deathsUnspent = {};
    var deathsForUnspent = rowsFor('deaths', scope);
    for (var d = 0; d < deathsForUnspent.length; d++) {
      var de = deathsForUnspent[d];
      if (!deathsUnspent[de.victim]) deathsUnspent[de.victim] = [];
      deathsUnspent[de.victim].push(de.unspentGold);
    }
    var playersForUnspent = rowsFor('players', scope);
    var unspentRows = playersForUnspent.map(function (pl) {
      var lb = lastBalance[pl.actor];
      var du = deathsUnspent[pl.actor] || [];
      var sum = 0; for (var ui = 0; ui < du.length; ui++) sum += du[ui];
      var mean = du.length ? (sum / du.length) : null;
      return { nick: pl.nick, team: pl.team, endBalance: lb ? lb.balance : null, meanUnspentAtDeath: mean, deathCount: du.length };
    });
    buildTable(id('table-unspent-gold', suffix), [
      { label: 'Player', value: function (r) { return r.nick; } },
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Gold at end', value: function (r) { return r.endBalance; } },
      { label: 'Mean unspent at death', value: function (r) { return r.meanUnspentAtDeath; } },
      { label: 'Deaths', value: function (r) { return r.deathCount; } },
    ], unspentRows);

    var sb2 = rowsFor('shopBlocked', scope);
    var byReason = {};
    for (var sbi = 0; sbi < sb2.length; sbi++) {
      var reasonRow = sb2[sbi];
      var reason = reasonRow.reason || '(unknown)';
      if (!byReason[reason]) byReason[reason] = { reason: reason, count: 0, totalShortfall: 0 };
      byReason[reason].count++;
      byReason[reason].totalShortfall += (reasonRow.shortfall || 0);
    }
    buildTable(id('table-shop-blocked', suffix), [
      { label: 'Reason', value: function (r) { return r.reason; } },
      { label: 'Count', value: function (r) { return r.count; } },
      { label: 'Total shortfall', value: function (r) { return r.totalShortfall; } },
    ], Object.keys(byReason).map(function (rk) { return byReason[rk]; }));
  }

  // ---------------------------------------------------------------- territory

  function renderTerritory(scope, suffix) {
    var ownership = rowsFor('ownership', scope);
    var bucket = bucketFor(scope);
    var matchLen = (bucket && bucket.header && bucket.header.matchLengthSeconds) || 1;
    // Review fix (item 2): a Phase-2-scoped row's own from/to are ABSOLUTE match seconds (e.g.
    // 90->120), while matchLen here is that PHASE's own length (e.g. 90) - drawing at
    // st.from / matchLen put every Phase 2 bar off the right edge of its own track (900/450 =
    // 200% in the fixture). Offset both ends by this scope's own window start before dividing.
    var winStart = (scope === 'phase-2' && DATA.transitionSeconds != null) ? DATA.transitionSeconds : 0;
    var ganttDiv = id('ownership-gantt', suffix);
    var zones = uniqueSorted(ownership.map(function (r) { return r.zone; }));
    for (var zi = 0; zi < zones.length; zi++) {
      var zone = zones[zi];
      var rowDiv = el('div', { class: 'gantt-row' });
      rowDiv.appendChild(el('div', { class: 'gantt-label' }, zoneLabel(zone)));
      var track = el('div', { class: 'gantt-track' });
      var stints = ownership.filter(function (r) { return r.zone === zone; });
      for (var si = 0; si < stints.length; si++) {
        var st = stints[si];
        var leftPct = ((st.from - winStart) / matchLen) * 100;
        var widthPct = Math.max(0.3, ((st.to - st.from) / matchLen) * 100);
        var bar = el('div', {
          class: 'gantt-bar',
          style: 'left:' + leftPct + '%;width:' + widthPct + '%;background:' + teamColor(st.team) + ';',
          title: zoneLabel(st.zone) + ' team ' + st.team + ' ' + fmt(st.from) + 's to ' + fmt(st.to) + 's (' + st.howEnded + ')',
        });
        track.appendChild(bar);
      }
      rowDiv.appendChild(track);
      ganttDiv.appendChild(rowDiv);
    }

    var eb = rowsFor('economyByMinute', scope);
    var teams = uniqueSorted(eb.map(function (r) { return r.team; }));
    var minutesList = uniqueSorted(eb.map(function (r) { return r.minute; }));
    if (chartsOk) {
      var zoneHeldDatasets = [];
      for (var t = 0; t < teams.length; t++) {
        var team = teams[t];
        zoneHeldDatasets.push({
          label: 'Team ' + team, borderColor: teamColor(team), fill: false, pointRadius: 1,
          data: minutesList.map(function (m) {
            var found = null;
            for (var ei = 0; ei < eb.length; ei++) { if (eb[ei].team === team && eb[ei].minute === m) { found = eb[ei]; break; } }
            if (!found) return 0;
            var sum = 0; for (var tier = 0; tier < 4; tier++) sum += found.zonesHeldByTier[tier];
            return sum;
          }),
        });
      }
      // Review fix (item 3b): category axis (labels: minutesList) - same snap-to-nearest-label
      // overlay as the gold-gap chart above, instead of a {x,y} dataset point.
      var heldOpts = chartOptions('minute', 'zones held');
      heldOpts.plugins = { overlayLines: { boundary: boundaryOverlaySpec(scope, 'minutes', minutesList) } };
      new Chart(id('chart-zones-held', suffix), {
        type: 'line',
        data: { labels: minutesList, datasets: zoneHeldDatasets },
        options: heldOpts,
        plugins: [overlayPlugin],
      });
    }

    var bountyByTeam = {};
    for (var bi = 0; bi < eb.length; bi++) {
      var br = eb[bi];
      bountyByTeam[br.team] = (bountyByTeam[br.team] || 0) + (br.bounty || 0);
    }
    buildTable(id('table-bounty', suffix), [
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Total bounty gold', value: function (r) { return r.total; } },
    ], Object.keys(bountyByTeam).map(function (bk) { return { team: bk, total: bountyByTeam[bk] }; }));

    buildTable(id('table-captures', suffix), [
      { label: 'Zone', value: function (r) { return zoneLabel(r.zone); } },
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Start', value: function (r) { return r.start; } },
      { label: 'End', value: function (r) { return r.end; } },
      { label: 'Outcome', value: function (r) { return r.outcome; } },
      { label: 'Duration', value: function (r) { return r.duration; } },
      { label: 'Players', value: function (r) { return r.players; } },
    ], rowsFor('captures', scope));
  }

  // ---------------------------------------------------------------- combat

  function renderCombat(scope, suffix) {
    var weapons = rowsFor('weapons', scope);
    if (chartsOk && weapons.length) {
      new Chart(id('chart-weapons', suffix), {
        type: 'bar',
        data: {
          labels: weapons.map(function (w) { return weaponName(w.weaponId); }),
          datasets: [{ label: 'Damage per equipped minute', backgroundColor: '#3a6df0', data: weapons.map(function (w) { return w.damagePerEquippedMinute; }) }],
        },
        options: chartOptions('weapon', 'damage per equipped minute')
      });
    }
    buildTable(id('table-weapons', suffix), [
      { label: 'Weapon', value: function (r) { return weaponName(r.weaponId); } },
      { label: 'Time equipped (s)', value: function (r) { return r.timeEquippedSeconds; } },
      { label: 'Pulls', value: function (r) { return r.pulls; } },
      { label: 'Projectiles', value: function (r) { return r.projectiles; } },
      { label: 'Hits', value: function (r) { return r.hits; } },
      { label: 'Splash hits', value: function (r) { return r.splashHits; } },
      { label: 'Accuracy', value: function (r) { return r.accuracy; } },
      { label: 'Damage', value: function (r) { return r.damageRaw; } },
      { label: 'Armor dmg', value: function (r) { return r.armorDamage; } },
      { label: 'Health dmg', value: function (r) { return r.healthDamage; } },
      { label: 'Dmg per equipped min', value: function (r) { return r.damagePerEquippedMinute; } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Mean dist', value: function (r) { return r.meanDistance; } },
      { label: 'Median dist', value: function (r) { return r.medianDistance; } },
    ], weapons);

    buildTable(id('table-abilities', suffix), [
      { label: 'Ability', value: function (r) { return abilityName(r.abilityId); } },
      { label: 'Casts', value: function (r) { return r.casts; } },
      { label: 'Damage', value: function (r) { return r.damageRaw; } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Status count', value: function (r) { return r.statusCount; } },
      { label: 'Status seconds', value: function (r) { return r.statusSeconds; } },
    ], rowsFor('abilities', scope));

    id('damage-matrix-desc', suffix).textContent =
      'Damage dealt (raw, pre-armor; attacker team \u2192 victim team) - same measure as the players table\u2019s \u2018Dmg dealt\u2019, ' +
      'except burn/DoT tick damage isn\u2019t attributed to a team pair here, so a team\u2019s row can read lower than its players\u2019 combined total.';

    var hits = rowsFor('hits', scope).filter(function (h) { return h.source !== 'Burn'; });
    var teamsSeen = uniqueSorted(hits.map(function (h) { return h.attackerTeam; }).concat(hits.map(function (h) { return h.victimTeam; })));
    var matrix = {};
    var maxVal = 0;
    for (var hi = 0; hi < hits.length; hi++) {
      var h = hits[hi];
      var key = h.attackerTeam + ':' + h.victimTeam;
      matrix[key] = (matrix[key] || 0) + (h.raw || 0);
      if (matrix[key] > maxVal) maxVal = matrix[key];
    }
    var matrixDiv = id('table-damage-matrix', suffix);
    var matrixTable = el('table');
    var matrixHead = el('thead');
    var matrixHeadRow = el('tr');
    matrixHeadRow.appendChild(el('th', null, 'Attacker team / victim team'));
    for (var c = 0; c < teamsSeen.length; c++) matrixHeadRow.appendChild(el('th', null, 'Team ' + teamsSeen[c]));
    matrixHead.appendChild(matrixHeadRow);
    matrixTable.appendChild(matrixHead);
    var matrixBody = el('tbody');
    for (var ri = 0; ri < teamsSeen.length; ri++) {
      var tr = el('tr');
      tr.appendChild(el('td', null, 'Team ' + teamsSeen[ri]));
      for (var ci = 0; ci < teamsSeen.length; ci++) {
        var v = matrix[teamsSeen[ri] + ':' + teamsSeen[ci]] || 0;
        var alpha = maxVal > 0 ? (0.12 + 0.65 * (v / maxVal)) : 0.12;
        var td = el('td', { style: 'background: rgba(224,71,63,' + alpha + ');' });
        td.textContent = fmt(v);
        tr.appendChild(td);
      }
      matrixBody.appendChild(tr);
    }
    matrixTable.appendChild(matrixBody);
    matrixDiv.appendChild(matrixTable);

    renderHeatmaps(scope, suffix);
  }

  function renderHeatmaps(scope, suffix) {
    var arena = DATA.arena;
    var canvas = id('arena-canvas', suffix);
    var note = id('arena-note', suffix);
    var legendHost = id('heatmap-legend', suffix);

    if (DATA.positionsKeptEveryN && DATA.positionsKeptEveryN > 1) {
      id('position-sample-note', suffix).textContent =
        '(Positions down-sampled: kept every ' + DATA.positionsKeptEveryN + 'th sample per player, to limit report size.)';
    }

    if (!arena || !arena.available) {
      note.textContent = (arena && arena.note) ? arena.note : 'Arena render is unavailable.';
      canvas.style.display = 'none';
      return;
    }

    var deaths = rowsFor('deaths', scope);
    var deathTeams = uniqueSorted(deaths.map(function (d) { return d.victimTeam; }));
    if (deathTeams.length) {
      legendHost.appendChild(el('span', null, 'Deaths (victim team):'));
      deathTeams.forEach(function (t) {
        legendHost.appendChild(el('span', { class: 'legend-dot', style: 'background:' + teamColor(t) + ';' }));
        legendHost.appendChild(el('span', null, 'Team ' + t));
      });
    }

    var ctx = canvas.getContext('2d');
    var img = new Image();

    function toPixel(x, z) {
      var px = (x - arena.minX) / arena.metresPerPixel;
      var py = arena.pixelSize - (z - arena.minZ) / arena.metresPerPixel;
      return [px, py];
    }

    function draw() {
      ctx.drawImage(img, 0, 0, arena.pixelSize, arena.pixelSize);
      if (id('toggle-positions', suffix).checked) {
        ctx.fillStyle = 'rgba(58,109,240,0.35)';
        var positions = rowsFor('positions', scope);
        for (var i = 0; i < positions.length; i++) {
          var pp = toPixel(positions[i].x, positions[i].z);
          ctx.beginPath();
          ctx.arc(pp[0], pp[1], 3, 0, 2 * Math.PI);
          ctx.fill();
        }
      }
      if (id('toggle-deaths', suffix).checked) {
        for (var d = 0; d < deaths.length; d++) {
          var pd = toPixel(deaths[d].x, deaths[d].z);
          ctx.beginPath();
          ctx.arc(pd[0], pd[1], 5, 0, 2 * Math.PI);
          ctx.fillStyle = teamColor(deaths[d].victimTeam);
          ctx.fill();
          ctx.lineWidth = 1.5;
          ctx.strokeStyle = 'rgba(0,0,0,0.75)';
          ctx.stroke();
        }
      }
    }

    img.onload = function () {
      canvas.width = arena.pixelSize;
      canvas.height = arena.pixelSize;
      draw();
    };
    img.src = 'data:image/png;base64,' + arena.base64Png;

    id('toggle-positions', suffix).addEventListener('change', draw);
    id('toggle-deaths', suffix).addEventListener('change', draw);
  }

  // ---------------------------------------------------------------- players

  function renderPlayers(scope, suffix) {
    buildTable(id('table-players', suffix), [
      { label: 'Player', value: function (r) { return r.nick; } },
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Deaths', value: function (r) { return r.deaths; } },
      { label: 'Assists', value: function (r) { return r.assists; } },
      { label: 'Dmg dealt', value: function (r) { return r.damageDealt; } },
      { label: 'Dmg taken', value: function (r) { return r.damageTaken; } },
      { label: 'Gold territory', value: function (r) { return r.goldTerritory; } },
      { label: 'Gold bounty', value: function (r) { return r.goldBounty; } },
      { label: 'Gold refund', value: function (r) { return r.goldRefund; } },
      { label: 'Gold debug', value: function (r) { return r.goldDebug; } },
      { label: 'Gold other', value: function (r) { return r.goldOther; } },
      { label: 'Gold spent', value: function (r) { return r.goldSpent; } },
      { label: 'Time alive', value: function (r) { return r.timeAlive; } },
      { label: 'Time own zone', value: function (r) { return r.timeOwnZone; } },
      { label: 'Time enemy zone', value: function (r) { return r.timeEnemyZone; } },
      { label: 'Time neutral zone', value: function (r) { return r.timeNeutralZone; } },
      { label: 'Healing', value: function (r) { return r.healing; } },
    ], rowsFor('players', scope));
  }

  // ---------------------------------------------------------------- markers

  function renderMarkers(scope, suffix) {
    var bucket = bucketFor(scope);
    var markers = (bucket && bucket.header && bucket.header.markers) || [];
    var container = id('markers-list', suffix);
    if (!markers.length) {
      container.appendChild(el('div', { class: 'note' }, 'No markers were dropped in this scope.'));
      return;
    }

    var events = [];
    rowsFor('deaths', scope).forEach(function (dr) { events.push({ t: dr.t, kind: 'death', text: dr.victimNick + ' died' }); });
    rowsFor('purchases', scope).forEach(function (pr) { events.push({ t: pr.t, kind: pr.kind, text: pr.nick + ' ' + pr.kind + ' (' + pr.category + ')' }); });
    rowsFor('shopBlocked', scope).forEach(function (sr) { events.push({ t: sr.t, kind: 'shopBlocked', text: sr.nick + ' blocked: ' + sr.reason }); });
    rowsFor('captures', scope).forEach(function (cr) {
      events.push({ t: cr.start, kind: 'captureStart', text: zoneLabel(cr.zone) + ' capture by team ' + cr.team + ' started' });
      events.push({ t: cr.end, kind: 'captureEnd', text: zoneLabel(cr.zone) + ' capture ' + cr.outcome });
    });
    rowsFor('ownership', scope).forEach(function (or_) { events.push({ t: or_.from, kind: 'ownership', text: zoneLabel(or_.zone) + ' to team ' + or_.team }); });

    for (var m = 0; m < markers.length; m++) {
      var marker = markers[m];
      var card = el('div', { class: 'card' });
      var title = el('h3');
      title.textContent = 't=' + fmt(marker.t) + 's (actor ' + marker.actor + '): ';
      var noteSpan = el('span');
      noteSpan.textContent = marker.note || '';
      title.appendChild(noteSpan);
      card.appendChild(title);

      var windowed = events.filter(function (e) { return e.t >= marker.t - 30 && e.t <= marker.t + 30; })
        .sort(function (a, b) { return a.t - b.t; });
      buildTable(card, [
        { label: 't', value: function (r) { return r.t; } },
        { label: 'type', value: function (r) { return r.kind; } },
        { label: 'event', value: function (r) { return r.text; } },
      ], windowed);
      container.appendChild(card);
    }
  }

  // ---------------------------------------------------------------- Task T7: render every tab

  ['whole-match', 'phase-1', 'phase-2'].forEach(function (scope) {
    var suffix = scope;
    var bucket = bucketFor(scope);

    // Review fix (item 11): the heading renders regardless (a plain 'Phase 2 (2 teams)' label
    // when there's no data at all), so an empty Phase 2 tab still reads as 'this tab exists and
    // here's why it's empty' rather than a blank line above the note.
    renderPhaseDuration(scope, suffix);

    if (scope === 'phase-2' && !bucket) {
      // No elimination in this match: hide the Header/Tuning/coverage/section content entirely -
      // only the heading (above) and this note show, nothing empty underneath.
      id('tab-content', suffix).style.display = 'none';
      id('phase2-empty-note', suffix).style.display = 'block';
      return;
    }

    safeRun('header (' + scope + ')', function () { renderHeader(scope, suffix); });
    safeRun('economy (' + scope + ')', function () { renderEconomy(scope, suffix); });
    safeRun('territory (' + scope + ')', function () { renderTerritory(scope, suffix); });
    safeRun('combat (' + scope + ')', function () { renderCombat(scope, suffix); });
    safeRun('players (' + scope + ')', function () { renderPlayers(scope, suffix); });
    safeRun('markers (' + scope + ')', function () { renderMarkers(scope, suffix); });
  });
})();
</script>
</body>
</html>
";
    }
}
