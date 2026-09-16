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
        public readonly int Schema;
        public readonly JObject Tuning;

        public TelemetrySession(string file, int actor, string nick, int team, bool isMaster, string matchId, string commit, int schema, JObject tuning)
        {
            File = file;
            Actor = actor;
            Nick = nick;
            Team = team;
            IsMaster = isMaster;
            MatchId = matchId;
            Commit = commit;
            Schema = schema;
            Tuning = tuning;
        }
    }

    /// <summary>Task T5 step 1: parses every *.jsonl file in a match folder with Newtonsoft. Pure
    /// file-system + JSON work - no aggregation logic lives here (see TelemetryAggregator).
    ///
    /// Merge key (the plan's own term): files are grouped by their own session's match id. A folder
    /// holding two different match ids (a stray file, or two runs accidentally sharing one folder)
    /// builds from whichever id has the most files and reports the other rather than silently
    /// merging two matches' facts into one report - see <see cref="OtherMatchId"/>. Load reads a
    /// SINGLE folder; it does not itself look across sibling folders for the rest of one match's
    /// files (see MatchTelemetry.ResolveMatchFolder's own comment on the one race that can produce
    /// two folders for one match).</summary>
    public sealed class TelemetryLog
    {
        private const string EventNameKey = "e";
        private const string TimeKey = "t";

        public string Folder { get; private set; }
        public string MatchId { get; private set; }
        public IReadOnlyList<string> IncludedFiles { get; private set; }
        public IReadOnlyList<TelemetryEvent> Events { get; private set; }
        public IReadOnlyList<TelemetrySession> Sessions { get; private set; }
        /// <summary>Malformed lines and unknown event names, counted only across the files that were
        /// actually included in this build (opus review fix) - a stray "other match" file's own junk
        /// no longer inflates the count for the match actually being reported.</summary>
        public int MalformedLineCount { get; private set; }
        public int UnknownEventCount { get; private set; }
        /// <summary>A file that could not be opened/read at all (opus review fix - previously
        /// silently skipped). Counted globally: an unreadable file has no session of its own, so it
        /// can never be attributed to one match id or another.</summary>
        public int UnreadableFileCount { get; private set; }
        /// <summary>A session whose own `schema` is newer than this build understands
        /// (TelemetryKeys.SchemaVersion) - counted, never a failure (design doc, Error handling).</summary>
        public int NewerSchemaCount { get; private set; }
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
            // Review fix (T7): these two were added to TelemetryKeys in this same task but never
            // added here, so every report - including one with no elimination at all, since
            // MatchTelemetry's own phase-1 anchor is unconditional - showed a false "unknown
            // event(s) were skipped" warning.
            TelemetryKeys.Phase, TelemetryKeys.Elimination,
        };

        public static TelemetryLog Load(string folder)
        {
            var log = new TelemetryLog { Folder = folder };

            string[] files = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

            var eventsByFile = new Dictionary<string, List<TelemetryEvent>>();
            var sessionByFile = new Dictionary<string, TelemetrySession>();
            var malformedByFile = new Dictionary<string, int>();
            var unknownByFile = new Dictionary<string, int>();
            int unreadable = 0;
            int newerSchema = 0;

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                var events = new List<TelemetryEvent>();
                eventsByFile[name] = events;
                malformedByFile[name] = 0;
                unknownByFile[name] = 0;

                string[] lines;
                try { lines = File.ReadAllLines(path); }
                catch
                {
                    unreadable++; // Opus review fix: counted, not silently dropped.
                    continue;
                }

                foreach (string rawLine in lines)
                {
                    string trimmed = rawLine.Trim();
                    if (trimmed.Length == 0) continue;

                    JObject data;
                    string eventName;
                    double t;
                    try
                    {
                        // Opus review fix: the "t" parse used to sit OUTSIDE this try, so a
                        // non-numeric "t" (a corrupt or hand-edited line) threw uncaught instead of
                        // counting as malformed like every other bad line.
                        data = JObject.Parse(trimmed);
                        eventName = data[EventNameKey]?.ToString();
                        if (string.IsNullOrEmpty(eventName))
                        {
                            malformedByFile[name]++; // Valid JSON, but not one of our lines at all.
                            continue;
                        }
                        JToken timeToken = data[TimeKey];
                        t = (timeToken != null && timeToken.Type != JTokenType.Null) ? timeToken.ToObject<double>() : -1.0;
                    }
                    catch
                    {
                        malformedByFile[name]++;
                        continue;
                    }

                    if (!KnownEventNames.Contains(eventName))
                        unknownByFile[name]++;

                    events.Add(new TelemetryEvent(name, eventName, t, data));

                    if (eventName == TelemetryKeys.Session && !sessionByFile.ContainsKey(name))
                    {
                        TelemetrySession session = ParseSession(name, data);
                        sessionByFile[name] = session;
                        if (session.Schema > TelemetryKeys.SchemaVersion)
                            newerSchema++;
                    }
                }
            }

            log.UnreadableFileCount = unreadable;
            log.NewerSchemaCount = newerSchema;

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

            // Opus review fix: only the chosen match's own files contribute to these counts now -
            // previously summed across every file read, including an "other match" one never merged in.
            log.MalformedLineCount = included.Sum(f => malformedByFile.GetValueOrDefault(f));
            log.UnknownEventCount = included.Sum(f => unknownByFile.GetValueOrDefault(f));

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
            int schema = data[TelemetryKeys.Schema]?.ToObject<int?>() ?? 0;
            JObject tuning = data[TelemetryKeys.Tuning] as JObject;
            return new TelemetrySession(file, actor, nick, team, master, matchId, commit, schema, tuning);
        }
    }
}
