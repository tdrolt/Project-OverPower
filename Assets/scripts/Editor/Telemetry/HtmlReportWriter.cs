using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Overpower.Data;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T6 step 3: one self-contained report.html per match folder. Every number comes
    /// straight from a ReportTables (T5) - CsvReportWriter and this class format the exact same data,
    /// so the CSVs and the HTML page can never disagree, only present differently (design doc,
    /// Outputs). The only inputs this class reads that ReportTables doesn't carry are:
    /// - raw `sample` x/z positions (never one of the 12 CSVs - see TelemetryLog directly) for the
    ///   position heatmap, and
    /// - BalanceTargets and the arena PNG, both reference material with no place in a CSV either.
    ///
    /// All data is embedded as one JSON blob (Newtonsoft, StringEscapeHandling.EscapeHtml) so a
    /// player-provided string (nickname, marker note) can never break out of the embedding
    /// &lt;script&gt; tag - every '&lt;', '&gt;', '&amp;' and quote becomes a \uXXXX escape, so the
    /// literal text "&lt;/script&gt;" can never appear in the emitted HTML at all. The page's own JS
    /// then writes every player-provided string via .textContent (never innerHTML) as a second,
    /// independent layer of the same protection.</summary>
    public static class HtmlReportWriter
    {
        public static string Write(ReportTables tables, TelemetryLog log, BalanceTargetsData targets, ArenaReportRender.Result arena, string folder)
        {
            tables ??= new ReportTables();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "report.html");
            string html = Build(tables, ExtractPositions(log), targets, arena);
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
        }

        /// <summary>Positions are only ever in the raw `sample` lines (design choice: not one of the
        /// 12 CSVs - see the spec's own CSV table list), so this reads the log directly rather than
        /// going through TelemetryAggregator, which stays untouched by this whole task.</summary>
        private static List<PositionSample> ExtractPositions(TelemetryLog log)
        {
            var result = new List<PositionSample>();
            if (log == null) return result;

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;

                var xToken = e.Data[TelemetryKeys.X];
                var zToken = e.Data[TelemetryKeys.Z];
                if (xToken == null || zToken == null) continue; // recordPositions was off for this line

                result.Add(new PositionSample
                {
                    T = e.T,
                    Team = e.Data[TelemetryKeys.Team]?.ToObject<int?>() ?? -1,
                    X = xToken.ToObject<float>(),
                    Z = zToken.ToObject<float>(),
                    Alive = e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true,
                });
            }
            return result;
        }

        private static string Build(ReportTables tables, List<PositionSample> positions, BalanceTargetsData targets, ArenaReportRender.Result arena)
        {
            var payload = new
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
                positions = positions,
                targets = targets,
                arena = arena,
            };

            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };
            string json = JsonConvert.SerializeObject(payload, settings);

            var sb = new StringBuilder(json.Length + 65536);
            sb.Append(HtmlHead);
            sb.Append("<script>\nconst DATA = ");
            sb.Append(json);
            sb.Append(";\n</script>\n");
            sb.Append(HtmlBody);
            return sb.ToString();
        }

        // Single-quoted HTML attributes and JS strings throughout, on purpose: a C# verbatim string
        // only ends at an unescaped double quote, so keeping every quote in this template single
        // avoids a large block of "" escaping.
        private const string HtmlHead = @"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
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
body { background: var(--bg); color: var(--fg); font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px 24px 60px; }
h1 { margin-top: 0; }
h2 { border-bottom: 1px solid var(--border); padding-bottom: 6px; }
section { margin-bottom: 44px; }
.card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px; overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: 13px; }
th, td { border-bottom: 1px solid var(--border); padding: 5px 8px; text-align: left; white-space: nowrap; }
th { cursor: pointer; user-select: none; color: var(--muted); font-weight: 600; }
th.sorted-asc::after { content: ' \25b2'; font-size: 10px; }
th.sorted-desc::after { content: ' \25bc'; font-size: 10px; }
.warn { background: var(--warn-bg); color: var(--warn-fg); padding: 8px 14px; border-radius: 6px; margin: 6px 0; font-size: 13px; }
.grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(360px, 1fr)); gap: 16px; }
canvas { max-width: 100%; }
.gantt-row { display: flex; align-items: center; gap: 8px; margin: 5px 0; }
.gantt-label { width: 70px; font-size: 12px; color: var(--muted); flex-shrink: 0; }
.gantt-track { position: relative; flex: 1; height: 16px; background: var(--border); border-radius: 3px; }
.gantt-bar { position: absolute; top: 0; height: 100%; border-radius: 3px; min-width: 2px; }
.note { color: var(--muted); font-size: 12px; }
#arena-wrap { position: relative; display: inline-block; max-width: 100%; }
#arena-canvas { border: 1px solid var(--border); border-radius: 6px; max-width: 100%; height: auto; background: #000; }
details.card summary { cursor: pointer; font-weight: 600; }
pre { white-space: pre-wrap; word-break: break-word; font-size: 12px; }
.tab-bar { display: flex; gap: 4px; margin-bottom: 20px; border-bottom: 1px solid var(--border); }
.tab-button { background: none; border: none; border-bottom: 3px solid transparent; color: var(--muted); font: inherit; font-size: 14px; padding: 8px 14px; cursor: pointer; }
.tab-button.active { color: var(--fg); border-bottom-color: var(--team0); font-weight: 600; }
.tab-panel { display: block; }
</style>
</head>
<body>
<h1>OverPower Telemetry Report</h1>
<div id='chart-fallback-note' class='warn' style='display:none'></div>

<div class='tab-bar' role='tablist'>
<button class='tab-button' data-tab='phase-1' type='button'>Phase 1: 3 teams</button>
<button class='tab-button' data-tab='phase-2' type='button'>Phase 2: 2 teams</button>
<button class='tab-button active' data-tab='whole-match' type='button'>Whole match</button>
</div>

<div id='tab-phase-1' class='tab-panel' style='display:none'>
<p class='note'>No phase data in this log (phases arrive with Task 2.7).</p>
</div>

<div id='tab-phase-2' class='tab-panel' style='display:none'>
<p class='note'>No phase data in this log (phases arrive with Task 2.7).</p>
</div>

<div id='tab-whole-match' class='tab-panel'>

<section id='section-header'>
<h2>Header</h2>
<div id='header-warnings'></div>
<div class='card' id='header-summary'></div>
<details class='card'><summary>Tuning snapshot</summary><pre id='header-tuning'></pre></details>
<div class='card'><h3>Player coverage</h3><div id='header-coverage'></div></div>
<div class='card'><h3>Log coverage</h3><div id='log-coverage'></div></div>
</section>

<section id='section-economy'>
<h2>Economy</h2>
<div class='grid'>
<div class='card'><h3>Gold per player over time</h3><canvas id='chart-gold-per-player'></canvas></div>
<div class='card'><h3>Team income per second by tier</h3><canvas id='chart-team-income'></canvas></div>
<div class='card'><h3>Gold generated per zone by team</h3><canvas id='chart-zone-income'></canvas></div>
<div class='card'><h3>Gold gap to richest team</h3><canvas id='chart-gold-gap'></canvas></div>
<div class='card'><h3>Purchase timeline vs targets</h3><canvas id='chart-purchase-timeline'></canvas></div>
</div>
<div class='grid'>
<div class='card'><h3>Unspent gold (at death / at end)</h3><div id='table-unspent-gold'></div></div>
<div class='card'><h3>Blocked purchases by reason</h3><div id='table-shop-blocked'></div></div>
</div>
</section>

<section id='section-territory'>
<h2>Territory</h2>
<div class='card'><h3>Ownership timeline</h3><div id='ownership-gantt'></div></div>
<div class='grid'>
<div class='card'><h3>Zones held per team over time</h3><canvas id='chart-zones-held'></canvas></div>
<div class='card'><h3>Bounty total by team</h3><div id='table-bounty'></div></div>
</div>
<div class='card'><h3>Captures</h3><div id='table-captures'></div></div>
</section>

<section id='section-combat'>
<h2>Combat</h2>
<div class='card'><h3>Weapons</h3><canvas id='chart-weapons'></canvas><div id='table-weapons'></div></div>
<div class='card'><h3>Abilities</h3><div id='table-abilities'></div></div>
<div class='card'><h3>Team versus team damage</h3><div id='table-damage-matrix'></div></div>
<div class='card' id='heatmaps'>
<h3>Death and position heatmaps</h3>
<div>
<label><input type='checkbox' id='toggle-deaths' checked> Deaths</label>
&nbsp;&nbsp;
<label><input type='checkbox' id='toggle-positions'> Positions</label>
</div>
<div id='arena-wrap'><canvas id='arena-canvas' width='1024' height='1024'></canvas></div>
<div id='arena-note' class='note'></div>
</div>
</section>

<section id='section-players'>
<h2>Players</h2>
<div class='card'><div id='table-players'></div></div>
</section>

<section id='section-markers'>
<h2>Markers</h2>
<div id='markers-list'></div>
</section>

</div>

<script src='https://cdn.jsdelivr.net/npm/chart.js@4'></script>
";

        private const string HtmlBody = @"<script>
(function () {
  'use strict';

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

  // Telemetry step 7 (not implemented yet - see assumptions-for-tudor.md) will add a per-row
  // `phase` value ('3 teams' / '2 teams') once a team is eliminated. currentPhase and rowsFor are
  // the one seam that work plugs into: every render function already reads its table through
  // rowsFor(name) instead of DATA[name] directly, so filtering by currentPhase later is a
  // one-line change here, not a rewrite of every render function.
  var currentPhase = 'all';
  function rowsFor(tableName, scope) { return DATA[tableName] || []; }

  // Every time-based chart (x = seconds or minutes) builds its options through this one function,
  // so a phase-boundary marker line (telemetry step 7) can be injected in ONE place later instead
  // of hunting through five separate chart configs.
  function timeChartOptions(xLabel, yLabel, config) {
    config = config || {};
    var xScale = { title: { display: true, text: xLabel } };
    if (config.xType) xScale.type = config.xType;
    if (config.stacked) xScale.stacked = true;
    var yScale = { title: { display: true, text: yLabel } };
    if (config.stacked) yScale.stacked = true;
    var options = { scales: { x: xScale, y: yScale } };
    if (config.parsing === false) options.parsing = false;
    return options;
  }

  // Telemetry step 7 (not implemented yet - see assumptions-for-tudor.md) splits this report into
  // a 3-team phase and a 2-team phase. Today's log has no phase data at all, so only the 'Whole
  // match' tab is ever populated; Phase 1/Phase 2 just show a placeholder. The tab-switch itself
  // is generic (any panel id works), ready for step 7 to add real content to the other two panels
  // without touching this function.
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

  var tuning = null;
  try { tuning = (DATA.header && DATA.header.tuningJson) ? JSON.parse(DATA.header.tuningJson) : null; } catch (parseErr) { tuning = null; }

  function nameFromList(list, id) {
    if (!list) return null;
    for (var i = 0; i < list.length; i++) { if (list[i].id === id) return list[i].name; }
    return null;
  }
  function weaponName(id) { return (tuning && nameFromList(tuning.weapons, id)) || ('Weapon ' + id); }
  function abilityName(id) { return (tuning && nameFromList(tuning.abilities, id)) || ('Ability ' + id); }

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

  safeRun('header', function () {
    var h = DATA.header || {};
    var warnDiv = document.getElementById('header-warnings');
    function warn(text) { warnDiv.appendChild(el('div', { class: 'warn' }, text)); }
    if (h.freeLoadoutUsed) warn('Free Loadout was ON for at least part of this match - purchase and economy numbers are not representative of a real match.');
    if (h.debugGoldUsed) warn('The F1 debug gold cheat was used during this match.');
    if (h.malformedLineCount) warn(h.malformedLineCount + ' malformed log line(s) were skipped.');
    if (h.unknownEventCount) warn(h.unknownEventCount + ' unknown event(s) were skipped.');
    if (h.unknownCaptureStateCount) warn(h.unknownCaptureStateCount + ' capture line(s) had an unrecognised state.');
    if (h.invalidTierCount) warn(h.invalidTierCount + ' ownership line(s) had an invalid tier and were skipped.');
    if (h.unreadableFileCount) warn(h.unreadableFileCount + ' log file(s) could not be read.');
    if (h.newerSchemaCount) warn(h.newerSchemaCount + ' session(s) used a newer schema than this report understands.');
    if (h.otherMatchId) warn('This folder also holds ' + h.otherMatchFileCount + ' file(s) from a different match (' + h.otherMatchId + ') - not merged into this report.');

    var summary = document.getElementById('header-summary');
    function line(label, value) { summary.appendChild(el('div', null, label + ': ' + value)); }
    line('Match id', h.matchId || '(none)');
    line('Length', fmt(h.matchLengthSeconds) + 's (' + fmt(minutes(h.matchLengthSeconds)) + ' min)');
    line('Players per team', h.playersPerTeam);
    line('Commits', (h.commits || []).join(', ') || '(none)');

    document.getElementById('header-tuning').textContent = h.tuningJson ? JSON.stringify(JSON.parse(h.tuningJson), null, 2) : '(none)';

    buildTable(document.getElementById('header-coverage'), [
      { label: 'Actor', value: function (r) { return r.actor; } },
      { label: 'Nick', value: function (r) { return r.nick; } },
      { label: 'First t', value: function (r) { return r.firstT; } },
      { label: 'Last t', value: function (r) { return r.lastT; } },
    ], h.coverage || []);
  });

  safeRun('economy', function () {
    var gt = rowsFor('goldTimeline', 'whole-match') || [];
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
      new Chart(document.getElementById('chart-gold-per-player'), {
        type: 'line',
        data: { datasets: goldDatasets },
        options: timeChartOptions('seconds', 'gold', { xType: 'linear', parsing: false })
      });
    }

    var eb = rowsFor('economyByMinute', 'whole-match') || [];
    var teams = uniqueSorted(eb.map(function (r) { return r.team; }));
    var minutesList = uniqueSorted(eb.map(function (r) { return r.minute; }));

    if (chartsOk) {
      var tierColors = ['#c9d6ff', '#8fa8ff', '#5a7cf7', '#2b52d6'];
      var incomeDatasets = [];
      for (var ti = 0; ti < teams.length; ti++) {
        var incomeTeam = teams[ti];
        for (var tier = 0; tier < 4; tier++) {
          incomeDatasets.push({
            label: 'Team ' + incomeTeam + ' tier ' + (tier + 1),
            stack: 'team' + incomeTeam,
            backgroundColor: tierColors[tier],
            data: minutesList.map(function (m) {
              var matchRow = null;
              for (var ei = 0; ei < eb.length; ei++) { if (eb[ei].team === incomeTeam && eb[ei].minute === m) { matchRow = eb[ei]; break; } }
              return matchRow ? (matchRow.incomeByTier[tier] / 60) : 0;
            }),
          });
        }
      }
      if (DATA.targets) {
        var scen = DATA.targets.scenarioIncomePerTeam;
        var scenarios = [['Losing', scen.losing], ['Struggling', scen.struggling], ['Average', scen.average], ['Dominant', scen.dominant]];
        for (var s = 0; s < scenarios.length; s++) {
          var scenarioValue = scenarios[s][1];
          incomeDatasets.push({
            type: 'line', label: scenarios[s][0] + ' (GDD)',
            data: minutesList.map(function () { return scenarioValue; }),
            borderColor: '#888', borderDash: [5, 4], pointRadius: 0, fill: false,
          });
        }
      }
      new Chart(document.getElementById('chart-team-income'), {
        type: 'bar',
        data: { labels: minutesList, datasets: incomeDatasets },
        options: timeChartOptions('minute', 'gold per second', { stacked: true })
      });
    }

    var zi = rowsFor('zoneIncome', 'whole-match') || [];
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
      new Chart(document.getElementById('chart-zone-income'), {
        type: 'bar',
        data: { labels: zones.map(function (z) { return 'Zone ' + z; }), datasets: zoneDatasets },
        options: { scales: { y: { title: { display: true, text: 'gold' } } } }
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
      new Chart(document.getElementById('chart-gold-gap'), {
        type: 'line',
        data: { labels: minutesList, datasets: gapDatasets },
        options: timeChartOptions('minute', 'gold behind richest team')
      });
    }

    var purchases = rowsFor('purchases', 'whole-match') || [];
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
          ['Primary Upgrade 1', targetsP.primaryUpgrade1Minutes],
          ['Armor 1', targetsP.armor1Minutes],
          ['Ultimate', targetsP.ultimateMinutes],
          ['Primary Upgrade 2', targetsP.primaryUpgrade2Minutes],
          ['Armor 2', targetsP.armor2Minutes],
        ];
        for (var li = 0; li < lines.length; li++) {
          purchaseDatasets.push({ type: 'line', label: lines[li][0] + ' target', data: [{ x: lines[li][1], y: 0 }, { x: lines[li][1], y: maxAmount }], borderColor: '#888', borderDash: [5, 4], pointRadius: 0, fill: false });
        }
      }
      new Chart(document.getElementById('chart-purchase-timeline'), {
        type: 'scatter',
        data: { datasets: purchaseDatasets },
        options: timeChartOptions('minute', 'price')
      });
    }

    var lastBalance = {};
    for (var g = 0; g < gt.length; g++) { lastBalance[gt[g].actor] = gt[g]; }
    var deathsUnspent = {};
    var deathsForUnspent = rowsFor('deaths', 'whole-match') || [];
    for (var d = 0; d < deathsForUnspent.length; d++) {
      var de = deathsForUnspent[d];
      if (!deathsUnspent[de.victim]) deathsUnspent[de.victim] = [];
      deathsUnspent[de.victim].push(de.unspentGold);
    }
    var playersForUnspent = rowsFor('players', 'whole-match') || [];
    var unspentRows = playersForUnspent.map(function (pl) {
      var lb = lastBalance[pl.actor];
      var du = deathsUnspent[pl.actor] || [];
      var sum = 0; for (var ui = 0; ui < du.length; ui++) sum += du[ui];
      var mean = du.length ? (sum / du.length) : null;
      return { nick: pl.nick, team: pl.team, endBalance: lb ? lb.balance : null, meanUnspentAtDeath: mean, deathCount: du.length };
    });
    buildTable(document.getElementById('table-unspent-gold'), [
      { label: 'Player', value: function (r) { return r.nick; } },
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Gold at end', value: function (r) { return r.endBalance; } },
      { label: 'Mean unspent at death', value: function (r) { return r.meanUnspentAtDeath; } },
      { label: 'Deaths', value: function (r) { return r.deathCount; } },
    ], unspentRows);

    var sb2 = rowsFor('shopBlocked', 'whole-match') || [];
    var byReason = {};
    for (var sbi = 0; sbi < sb2.length; sbi++) {
      var reasonRow = sb2[sbi];
      var reason = reasonRow.reason || '(unknown)';
      if (!byReason[reason]) byReason[reason] = { reason: reason, count: 0, totalShortfall: 0 };
      byReason[reason].count++;
      byReason[reason].totalShortfall += (reasonRow.shortfall || 0);
    }
    buildTable(document.getElementById('table-shop-blocked'), [
      { label: 'Reason', value: function (r) { return r.reason; } },
      { label: 'Count', value: function (r) { return r.count; } },
      { label: 'Total shortfall', value: function (r) { return r.totalShortfall; } },
    ], Object.keys(byReason).map(function (rk) { return byReason[rk]; }));
  });

  safeRun('territory', function () {
    var ownership = rowsFor('ownership', 'whole-match') || [];
    var matchLen = (DATA.header && DATA.header.matchLengthSeconds) || 1;
    var ganttDiv = document.getElementById('ownership-gantt');
    var zones = uniqueSorted(ownership.map(function (r) { return r.zone; }));
    for (var zi = 0; zi < zones.length; zi++) {
      var zone = zones[zi];
      var rowDiv = el('div', { class: 'gantt-row' });
      rowDiv.appendChild(el('div', { class: 'gantt-label' }, 'Zone ' + zone));
      var track = el('div', { class: 'gantt-track' });
      var stints = ownership.filter(function (r) { return r.zone === zone; });
      for (var si = 0; si < stints.length; si++) {
        var st = stints[si];
        var leftPct = (st.from / matchLen) * 100;
        var widthPct = Math.max(0.3, ((st.to - st.from) / matchLen) * 100);
        var bar = el('div', {
          class: 'gantt-bar',
          style: 'left:' + leftPct + '%;width:' + widthPct + '%;background:' + teamColor(st.team) + ';',
          title: 'Zone ' + st.zone + ' tier ' + st.tier + ' team ' + st.team + ' ' + fmt(st.from) + 's to ' + fmt(st.to) + 's (' + st.howEnded + ')',
        });
        track.appendChild(bar);
      }
      rowDiv.appendChild(track);
      ganttDiv.appendChild(rowDiv);
    }

    var eb = rowsFor('economyByMinute', 'whole-match') || [];
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
      new Chart(document.getElementById('chart-zones-held'), {
        type: 'line',
        data: { labels: minutesList, datasets: zoneHeldDatasets },
        options: timeChartOptions('minute', 'zones held')
      });
    }

    var bountyByTeam = {};
    for (var bi = 0; bi < eb.length; bi++) {
      var br = eb[bi];
      bountyByTeam[br.team] = (bountyByTeam[br.team] || 0) + (br.bounty || 0);
    }
    buildTable(document.getElementById('table-bounty'), [
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Total bounty gold', value: function (r) { return r.total; } },
    ], Object.keys(bountyByTeam).map(function (bk) { return { team: bk, total: bountyByTeam[bk] }; }));

    buildTable(document.getElementById('table-captures'), [
      { label: 'Zone', value: function (r) { return r.zone; } },
      { label: 'Team', value: function (r) { return r.team; } },
      { label: 'Start', value: function (r) { return r.start; } },
      { label: 'End', value: function (r) { return r.end; } },
      { label: 'Outcome', value: function (r) { return r.outcome; } },
      { label: 'Duration', value: function (r) { return r.duration; } },
      { label: 'Players', value: function (r) { return r.players; } },
    ], rowsFor('captures', 'whole-match') || []);
  });

  safeRun('combat', function () {
    var weapons = rowsFor('weapons', 'whole-match') || [];
    if (chartsOk && weapons.length) {
      new Chart(document.getElementById('chart-weapons'), {
        type: 'bar',
        data: {
          labels: weapons.map(function (w) { return weaponName(w.weaponId); }),
          datasets: [{ label: 'Damage per equipped minute', backgroundColor: '#3a6df0', data: weapons.map(function (w) { return w.damagePerEquippedMinute; }) }],
        },
        options: { scales: { y: { title: { display: true, text: 'damage per equipped minute' } } } }
      });
    }
    buildTable(document.getElementById('table-weapons'), [
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

    buildTable(document.getElementById('table-abilities'), [
      { label: 'Ability', value: function (r) { return abilityName(r.abilityId); } },
      { label: 'Casts', value: function (r) { return r.casts; } },
      { label: 'Damage', value: function (r) { return r.damageRaw; } },
      { label: 'Kills', value: function (r) { return r.kills; } },
      { label: 'Status count', value: function (r) { return r.statusCount; } },
      { label: 'Status seconds', value: function (r) { return r.statusSeconds; } },
    ], rowsFor('abilities', 'whole-match') || []);

    var hits = (rowsFor('hits', 'whole-match') || []).filter(function (h) { return h.source !== 'Burn'; });
    var teamsSeen = uniqueSorted(hits.map(function (h) { return h.attackerTeam; }).concat(hits.map(function (h) { return h.victimTeam; })));
    var matrix = {};
    var maxVal = 0;
    for (var hi = 0; hi < hits.length; hi++) {
      var h = hits[hi];
      var key = h.attackerTeam + ':' + h.victimTeam;
      matrix[key] = (matrix[key] || 0) + (h.healthLost || 0);
      if (matrix[key] > maxVal) maxVal = matrix[key];
    }
    var matrixDiv = document.getElementById('table-damage-matrix');
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

    renderHeatmaps();
  });

  function renderHeatmaps() {
    var arena = DATA.arena;
    var canvas = document.getElementById('arena-canvas');
    var note = document.getElementById('arena-note');
    if (!arena || !arena.available) {
      note.textContent = (arena && arena.note) ? arena.note : 'Arena render is unavailable.';
      canvas.style.display = 'none';
      return;
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
      if (document.getElementById('toggle-positions').checked) {
        ctx.fillStyle = 'rgba(58,109,240,0.35)';
        var positions = rowsFor('positions', 'whole-match') || [];
        for (var i = 0; i < positions.length; i++) {
          var pp = toPixel(positions[i].x, positions[i].z);
          ctx.beginPath();
          ctx.arc(pp[0], pp[1], 3, 0, 2 * Math.PI);
          ctx.fill();
        }
      }
      if (document.getElementById('toggle-deaths').checked) {
        ctx.fillStyle = 'rgba(224,71,63,0.9)';
        var deaths = rowsFor('deaths', 'whole-match') || [];
        for (var d = 0; d < deaths.length; d++) {
          var pd = toPixel(deaths[d].x, deaths[d].z);
          ctx.beginPath();
          ctx.arc(pd[0], pd[1], 4, 0, 2 * Math.PI);
          ctx.fill();
        }
      }
    }

    img.onload = function () {
      canvas.width = arena.pixelSize;
      canvas.height = arena.pixelSize;
      draw();
    };
    img.src = 'data:image/png;base64,' + arena.base64Png;

    document.getElementById('toggle-positions').addEventListener('change', draw);
    document.getElementById('toggle-deaths').addEventListener('change', draw);
  }

  safeRun('players', function () {
    buildTable(document.getElementById('table-players'), [
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
    ], rowsFor('players', 'whole-match') || []);
  });

  safeRun('markers', function () {
    var markers = (DATA.header && DATA.header.markers) || [];
    var container = document.getElementById('markers-list');
    if (!markers.length) {
      container.appendChild(el('div', { class: 'note' }, 'No markers were dropped in this match.'));
      return;
    }

    var events = [];
    (rowsFor('deaths', 'whole-match') || []).forEach(function (dr) { events.push({ t: dr.t, kind: 'death', text: dr.victimNick + ' died' }); });
    (rowsFor('purchases', 'whole-match') || []).forEach(function (pr) { events.push({ t: pr.t, kind: pr.kind, text: pr.nick + ' ' + pr.kind + ' (' + pr.category + ')' }); });
    (rowsFor('shopBlocked', 'whole-match') || []).forEach(function (sr) { events.push({ t: sr.t, kind: 'shopBlocked', text: sr.nick + ' blocked: ' + sr.reason }); });
    (rowsFor('captures', 'whole-match') || []).forEach(function (cr) {
      events.push({ t: cr.start, kind: 'captureStart', text: 'Zone ' + cr.zone + ' capture by team ' + cr.team + ' started' });
      events.push({ t: cr.end, kind: 'captureEnd', text: 'Zone ' + cr.zone + ' capture ' + cr.outcome });
    });
    (rowsFor('ownership', 'whole-match') || []).forEach(function (or_) { events.push({ t: or_.from, kind: 'ownership', text: 'Zone ' + or_.zone + ' to team ' + or_.team }); });

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
  });
})();
</script>
</body>
</html>
";
    }
}
