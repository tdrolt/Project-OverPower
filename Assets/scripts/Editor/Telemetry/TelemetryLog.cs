using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>One parsed telemetry line, tagged with which file it came from. Most events are logged
    /// by "the owner" and carry no actor field of their own (`sample`, `goldEarned`, `death`,
    /// `respawn`...) - the file is how <see cref="TelemetryAggregator"/> recovers whose fact this is,
    /// via that file's own <see cref="TelemetrySession"/>.</summary>
    public sealed class TelemetryEvent
    {
        public readonly string File;
        public readonly string Name;
        public readonly double T;
        public readonly JObject Data;

        public TelemetryEvent(string file, string name, double t, JObject data)
        {
            File = file;
            Name = name;
            T = t;
            Data = data;
        }
    }

    /// <summary>One file's line 1 (`session`), parsed just enough to group files by match id and to
    /// answer the header's own questions (players per team, free loadout, commit).</summary>
    public sealed class TelemetrySession
    {
        public readonly string File;
        public readonly int Actor;
        public readonly string Nick;
        public readonly int Team;
        public readonly bool IsMaster;
        public readonly string MatchId;
        public readonly string Commit;
        public readonly JObject Tuning;

        public TelemetrySession(string file, int actor, string nick, int team, bool isMaster, string matchId, string commit, JObject tuning)
        {
            File = file;
            Actor = actor;
            Nick = nick;
            Team = team;
            IsMaster = isMaster;
            MatchId = matchId;
            Commit = commit;
            Tuning = tuning;
        }
    }

    /// <summary>Task T5 step 1: parses every *.jsonl file in a match folder with Newtonsoft. Pure
    /// file-system + JSON work - no aggregation logic lives here (see TelemetryAggregator).
    ///
    /// Merge key (the plan's own term): files are grouped by their own session's match id. A folder
    /// holding two different match ids (a stray file, or two runs accidentally sharing one folder)
    /// builds from whichever id has the most files and reports the other rather than silently
    /// merging two matches' facts into one report - see <see cref="OtherMatchId"/>.</summary>
    public sealed class TelemetryLog
    {
        private const string EventNameKey = "e";
        private const string TimeKey = "t";

        public string Folder { get; private set; }
        public string MatchId { get; private set; }
        public IReadOnlyList<string> IncludedFiles { get; private set; }
        public IReadOnlyList<TelemetryEvent> Events { get; private set; }
        public IReadOnlyList<TelemetrySession> Sessions { get; private set; }
        public int MalformedLineCount { get; private set; }
        public int UnknownEventCount { get; private set; }
        public string OtherMatchId { get; private set; }
        public int OtherMatchFileCount { get; private set; }

        /// <summary>Every event name T1-T4 actually write. Anything else - an older/newer schema's
        /// event, or a stray line - is counted in <see cref="UnknownEventCount"/> rather than failing
        /// the whole report (design doc, Error handling).</summary>
        private static readonly HashSet<string> KnownEventNames = new HashSet<string>
        {
            TelemetryKeys.Session, TelemetryKeys.Sample, TelemetryKeys.GoldEarned, TelemetryKeys.Purchase,
            TelemetryKeys.Refund, TelemetryKeys.ShopBlocked, TelemetryKeys.Shots, TelemetryKeys.Cast,
            TelemetryKeys.Hit, TelemetryKeys.Status, TelemetryKeys.Death, TelemetryKeys.Respawn,
            TelemetryKeys.Heal, TelemetryKeys.Overheat, TelemetryKeys.Dot, TelemetryKeys.UltimateReady,
            TelemetryKeys.UltimateUsed, TelemetryKeys.Ownership, TelemetryKeys.Capture, TelemetryKeys.Bounty,
            TelemetryKeys.UnderAttack, TelemetryKeys.Overpower, TelemetryKeys.Join, TelemetryKeys.Leave,
            TelemetryKeys.MasterChanged, TelemetryKeys.Marker,
        };

        public static TelemetryLog Load(string folder)
        {
            var log = new TelemetryLog { Folder = folder };

            string[] files = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

            var eventsByFile = new Dictionary<string, List<TelemetryEvent>>();
            var sessionByFile = new Dictionary<string, TelemetrySession>();
            int malformed = 0, unknown = 0;

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                var events = new List<TelemetryEvent>();
                eventsByFile[name] = events;

                string[] lines;
                try { lines = File.ReadAllLines(path); }
                catch { continue; } // Unreadable file - contributes nothing, doesn't fail the report.

                foreach (string rawLine in lines)
                {
                    string trimmed = rawLine.Trim();
                    if (trimmed.Length == 0) continue;

                    JObject data;
                    try { data = JObject.Parse(trimmed); }
                    catch { malformed++; continue; }

                    string eventName = data[EventNameKey]?.ToString();
                    if (string.IsNullOrEmpty(eventName))
                    {
                        malformed++; // Valid JSON, but not one of our lines at all.
                        continue;
                    }

                    JToken timeToken = data[TimeKey];
                    double t = (timeToken != null && timeToken.Type != JTokenType.Null) ? timeToken.ToObject<double>() : -1.0;

                    if (!KnownEventNames.Contains(eventName))
                        unknown++;

                    events.Add(new TelemetryEvent(name, eventName, t, data));

                    if (eventName == TelemetryKeys.Session && !sessionByFile.ContainsKey(name))
                        sessionByFile[name] = ParseSession(name, data);
                }
            }

            log.MalformedLineCount = malformed;
            log.UnknownEventCount = unknown;

            // ---------------------------------------------------------------- merge key: group by match id
            var filesByMatchId = new Dictionary<string, List<string>>();
            var sessionlessFiles = new List<string>();
            foreach (string name in eventsByFile.Keys)
            {
                if (sessionByFile.TryGetValue(name, out TelemetrySession session) && !string.IsNullOrEmpty(session.MatchId))
                {
                    if (!filesByMatchId.TryGetValue(session.MatchId, out List<string> list))
                        filesByMatchId[session.MatchId] = list = new List<string>();
                    list.Add(name);
                }
                else
                {
                    sessionlessFiles.Add(name);
                }
            }

            List<string> included;
            if (filesByMatchId.Count == 0)
            {
                log.MatchId = "";
                included = eventsByFile.Keys.ToList();
            }
            else if (filesByMatchId.Count == 1)
            {
                var only = filesByMatchId.First();
                log.MatchId = only.Key;
                included = only.Value.Concat(sessionlessFiles).ToList();
            }
            else
            {
                var ordered = filesByMatchId.OrderByDescending(kv => kv.Value.Count).ToList();
                var primary = ordered[0];
                var other = ordered[1];
                log.MatchId = primary.Key;
                log.OtherMatchId = other.Key;
                log.OtherMatchFileCount = other.Value.Count;
                // Ambiguous folder: a sessionless file can't be attributed to either match with
                // confidence, so it is left out entirely rather than risked against the wrong one.
                included = primary.Value;
            }

            log.IncludedFiles = included;
            log.Sessions = included.Where(sessionByFile.ContainsKey).Select(f => sessionByFile[f]).ToList();

            var mergedEvents = new List<TelemetryEvent>();
            foreach (string name in included)
                if (eventsByFile.TryGetValue(name, out List<TelemetryEvent> list))
                    mergedEvents.AddRange(list);

            // "T == -1 first in file order": OrderBy is a stable sort, so events sharing a sort key
            // keep the relative order they were appended in above (file order, then line order).
            log.Events = mergedEvents.OrderBy(e => e.T == -1.0 ? double.NegativeInfinity : e.T).ToList();

            return log;
        }

        private static TelemetrySession ParseSession(string file, JObject data)
        {
            int actor = data[TelemetryKeys.Actor]?.ToObject<int?>() ?? -1;
            string nick = data[TelemetryKeys.Nick]?.ToString() ?? "";
            int team = data[TelemetryKeys.Team]?.ToObject<int?>() ?? -1;
            bool master = data[TelemetryKeys.IsMaster]?.ToObject<bool?>() ?? false;
            string matchId = data[TelemetryKeys.MatchId]?.ToString() ?? "";
            string commit = data[TelemetryKeys.Commit]?.ToString() ?? "";
            JObject tuning = data[TelemetryKeys.Tuning] as JObject;
            return new TelemetrySession(file, actor, nick, team, master, matchId, commit, tuning);
        }
    }
}
