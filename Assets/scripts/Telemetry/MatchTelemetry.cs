using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Scene singleton (lives on the BuildingManager GameObject, next to ZonePresenceTracker) that
    /// owns this client's telemetry file: the match id, the writer, the session header, periodic
    /// flushing, join/leave/masterChanged, and F1 markers. T3/T4 log through <see cref="Log"/>;
    /// T4 also adds the master-only territory listeners here (ownership/capture/bounty/underAttack) -
    /// not yet, that is out of scope for this task.
    ///
    /// Match identity (Room Properties `mId`/`mStart`) follows the exact pattern
    /// BuildingManager.WriteInitialSnapshotWhenClockIsReady already uses for the starting territory
    /// snapshot: wait for the server clock, re-check nobody else already wrote it, then write. The one
    /// difference is the write itself uses Photon's check-and-set (`expectedProperties`) on the ABSENT
    /// key, so two masters racing during a migration can't both win even inside that re-check's own
    /// race window - see TryClaimMatchIdentity's comment.
    ///
    /// Deleting Assets/scripts/Telemetry (design doc, Principle 1) removes this whole component and
    /// the game still runs: nothing outside this folder depends on it existing.
    /// </summary>
    [DisallowMultipleComponent]
    public class MatchTelemetry : MonoBehaviourPunCallbacks
    {
        public static MatchTelemetry Instance { get; private set; }

        [Header("Tuning snapshot sources")]
        [Tooltip("Turns telemetry on/off and holds the sample/flush intervals and the log folder name.")]
        [SerializeField] private TelemetryConfig config;

        [Tooltip("Territory tuning (tiers, incomes, capture times, bounty). Written verbatim into the " +
                 "session header's tuning snapshot.")]
        [SerializeField] private TerritoryConfig territoryConfig;

        [Tooltip("Match-wide tuning (health, respawn, overheat, shop, OverPower). Written verbatim " +
                 "into the session header's tuning snapshot.")]
        [SerializeField] private GameplayConfig gameplayConfig;

        [Tooltip("Armor tuning (absorb/recharge levels, upgrade costs). Written verbatim into the " +
                 "session header's tuning snapshot.")]
        [SerializeField] private ArmorConfig armorConfig;

        [Tooltip("Every weapon in the game - each one's id, name, gold cost and full stat block are " +
                 "written into the session header's tuning snapshot.")]
        [SerializeField] private WeaponCatalogue weapons;

        [Tooltip("Every ability in the game - same treatment as Weapons above.")]
        [SerializeField] private AbilityCatalogue abilities;

        // How long the master waits for the server clock before writing the match identity anyway -
        // same value and same reasoning as BuildingManager.ServerClockWaitSeconds.
        private const float ServerClockWaitSeconds = 5f;

        // Cap on how many log lines are held before the file has opened (match id + this client's own
        // actor number both known). Only ever a handful of lines in practice - join/leave/masterChanged
        // observed in the first frame or two after connecting - but a cap keeps a client that somehow
        // never resolves its identity from growing this list forever.
        private const int MaxPendingLines = 200;

        private readonly TelemetryWriter writer = new TelemetryWriter();
        private readonly List<string> pendingLines = new List<string>();

        private string matchId;
        private int matchStartMs;
        private Coroutine claimIdentityRoutine;
        private float flushTimer;

        /// <summary>Match seconds since mStart, wrap-safe and 0-guarded - see MatchClock. -1 until this
        /// client has read the room's match identity.</summary>
        public double Now => MatchClock.Seconds(PhotonNetwork.ServerTimestamp, matchStartMs);

        /// <summary>The folder this client's file lives in, or null before it has opened. Read by the
        /// F1 "Open telemetry folder" button.</summary>
        public string CurrentFolder { get; private set; }

        /// <summary>How many lines are actually on disk so far - the F1 status label reads this.</summary>
        public int LineCount => writer.LineCount;

        /// <summary>True once this client's file has been opened and the session header written.</summary>
        public bool IsRecording => writer.IsOpen;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogError("[Telemetry] a second MatchTelemetry exists - destroying it. There must be exactly one, on the BuildingManager object.", this);
                Destroy(this);
            }
        }

        private void Start()
        {
            // Normally the room is joined after this scene has started, and OnJoinedRoom does this -
            // same "might already be in the room" guard as BuildingManager.Start.
            if (PhotonNetwork.InRoom)
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                TryClaimMatchIdentity();
                TryOpenFile();
            }
        }

        private void Update()
        {
            if (config == null || !config.Enabled || !writer.IsOpen) return;

            flushTimer += Time.unscaledDeltaTime;
            if (flushTimer < config.FlushIntervalSeconds) return;

            flushTimer = 0f;
            writer.Flush();
        }

        private void OnApplicationQuit() => writer.Close();

        private void OnDestroy()
        {
            writer.Close();
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- room events

        public override void OnJoinedRoom()
        {
            ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
            TryClaimMatchIdentity();
            TryOpenFile();
            LogJoinOrLeave(TelemetryKeys.Join, PhotonNetwork.LocalPlayer);
        }

        public override void OnLeftRoom()
        {
            writer.Close();
            CurrentFolder = null;
            matchId = null;
            matchStartMs = 0;
            pendingLines.Clear();
            if (claimIdentityRoutine != null)
            {
                StopCoroutine(claimIdentityRoutine);
                claimIdentityRoutine = null;
            }
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) => LogJoinOrLeave(TelemetryKeys.Join, newPlayer);

        public override void OnPlayerLeftRoom(Player otherPlayer) => LogJoinOrLeave(TelemetryKeys.Leave, otherPlayer);

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.MasterChanged, Now);
            line.Int(TelemetryKeys.Actor, newMasterClient.ActorNumber);
            line.Int(TelemetryKeys.Team, Teams.TryGetTeam(newMasterClient, out int team) ? team : -1);
            Log(line);

            // The old master may have left before ever writing the match identity.
            TryClaimMatchIdentity();
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) => ReadMatchIdentity(propertiesThatChanged);

        private void LogJoinOrLeave(string eventName, Player player)
        {
            if (player == null) return;
            var line = new TelemetryLine();
            line.Begin(eventName, Now);
            line.Int(TelemetryKeys.Actor, player.ActorNumber);
            line.Int(TelemetryKeys.Team, Teams.TryGetTeam(player, out int team) ? team : -1);
            Log(line);
        }

        // ---------------------------------------------------------------- match identity

        private void ReadMatchIdentity(Hashtable props)
        {
            if (props == null || !string.IsNullOrEmpty(matchId)) return; // Already known - these two keys never change once written.

            if (!props.TryGetValue(TelemetryKeys.RoomMatchId, out object idRaw) || !(idRaw is string id) || string.IsNullOrEmpty(id))
                return;

            matchId = id;
            matchStartMs = props.TryGetValue(TelemetryKeys.RoomMatchStart, out object startRaw) && startRaw is int startMs ? startMs : 0;
            TryOpenFile();
        }

        /// <summary>Master only: writes mId/mStart if they are still absent from the room. Guarded
        /// twice against two masters racing during a migration:
        /// (1) a local check that the key is absent before even trying, same shape as
        ///     BuildingManager.WriteInitialSnapshotWhenClockIsReady re-checking after its own wait;
        /// (2) the write itself passes `expectedProperties = { mId: null }` (Photon's check-and-set),
        ///     so the SERVER only applies it if mId is still unset at the moment it processes the
        ///     op - closing the window between (1)'s local check and the op actually landing, which a
        ///     local check alone cannot close. If this client loses that race, SetCustomProperties
        ///     simply returns false and this client does nothing further; the winner's value arrives
        ///     through OnRoomPropertiesUpdate like any other client's.</summary>
        private void TryClaimMatchIdentity()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId)) return;
            if (claimIdentityRoutine != null) return;

            claimIdentityRoutine = StartCoroutine(ClaimMatchIdentityWhenClockIsReady());
        }

        private IEnumerator ClaimMatchIdentityWhenClockIsReady()
        {
            float waited = 0f;
            while (PhotonNetwork.ServerTimestamp == 0 && waited < ServerClockWaitSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            claimIdentityRoutine = null;

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) yield break;

            // Someone else may have written it while this client waited.
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId))
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                yield break;
            }

            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0)
                Debug.LogWarning("[Telemetry] server clock still reads 0 after waiting - writing the match start stamp anyway (events before it will read t = -1 - see the design doc's Error handling).");

            var props = new Hashtable
            {
                { TelemetryKeys.RoomMatchId, Guid.NewGuid().ToString("N") },
                { TelemetryKeys.RoomMatchStart, now },
            };
            var expectedAbsent = new Hashtable { { TelemetryKeys.RoomMatchId, null } };

            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent))
                Debug.Log("[Telemetry] match identity write was refused (another master already wrote it) - waiting for its value.");
        }

        // ---------------------------------------------------------------- file + session header

        private void TryOpenFile()
        {
            if (writer.IsOpen || writer.Disabled) return;
            if (config == null || !config.Enabled) return;
            if (string.IsNullOrEmpty(matchId) || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;

            int actor = PhotonNetwork.LocalPlayer.ActorNumber;
            if (actor <= 0) return; // Not yet assigned an actor number - guards a race right after connecting.

            string folder = ResolveMatchFolder();
            string fileName = $"{actor}_{Sanitize(PhotonNetwork.LocalPlayer.NickName)}.jsonl";
            writer.Open(Path.Combine(folder, fileName));
            if (writer.Disabled) return;

            CurrentFolder = folder;

            WriteSessionLine();
            foreach (string pending in pendingLines)
                writer.Write(pending);
            pendingLines.Clear();

            writer.Flush(); // Immediate, so the file and its header exist as soon as a client joins, not just after the first flush interval.
        }

        /// <summary>One folder per match on one PC: reuses an existing folder ending in `_{mId8}` if
        /// one already exists (a late-starting second client on the same machine), otherwise creates a
        /// freshly dated one.</summary>
        private string ResolveMatchFolder()
        {
            string root = Path.Combine(Application.persistentDataPath, string.IsNullOrEmpty(config.FolderName) ? "Telemetry" : config.FolderName);
            Directory.CreateDirectory(root);

            string suffix = "_" + matchId.Substring(0, 8);
            foreach (string existing in Directory.GetDirectories(root))
            {
                if (Path.GetFileName(existing).EndsWith(suffix, StringComparison.Ordinal))
                    return existing;
            }

            string created = Path.Combine(root, $"{DateTime.Now:yyyy-MM-dd_HHmm}{suffix}");
            Directory.CreateDirectory(created);
            return created;
        }

        private void WriteSessionLine()
        {
            Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int team);

            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Session, Now);
            line.Int(TelemetryKeys.Schema, TelemetryKeys.SchemaVersion);
            line.String(TelemetryKeys.MatchId, matchId);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer.ActorNumber);
            line.String(TelemetryKeys.Nick, PhotonNetwork.LocalPlayer.NickName ?? "");
            line.Int(TelemetryKeys.Team, team);
            line.Bool(TelemetryKeys.IsMaster, PhotonNetwork.IsMasterClient);
            line.String(TelemetryKeys.Commit, ReadCommitHash());
            line.String(TelemetryKeys.UnityVersion, Application.unityVersion);
            line.String(TelemetryKeys.Platform, Application.platform.ToString());
            line.Raw(TelemetryKeys.Tuning, TuningSnapshot.Json(territoryConfig, gameplayConfig, armorConfig, weapons, abilities));
            writer.Write(line.End());
        }

        private static string ReadCommitHash()
        {
#if UNITY_EDITOR
            return GitCommitReader.ReadShortHash(10);
#else
            TextAsset asset = Resources.Load<TextAsset>("BuildInfo");
            return asset != null ? asset.text.Trim() : "unknown";
#endif
        }

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        private static string Sanitize(string nick)
        {
            if (string.IsNullOrWhiteSpace(nick)) return "player";

            var sb = new StringBuilder(nick.Length);
            foreach (char c in nick)
                sb.Append(Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- recorder API

        /// <summary>Every T3/T4 recorder logs through this. Writes immediately (buffered inside
        /// TelemetryWriter) once the file is open; before that, queues up to MaxPendingLines lines so
        /// nothing raised in the first frame or two after connecting is lost, then flushes them right
        /// after the session header (see TryOpenFile). Does nothing at all once telemetry is disabled
        /// (the config's own switch, or an IO failure) - callers never need to check first.</summary>
        public void Log(TelemetryLine line)
        {
            if (config == null || !config.Enabled || writer.Disabled) return;

            string text = line.End();
            if (writer.IsOpen)
            {
                writer.Write(text);
                return;
            }

            if (pendingLines.Count < MaxPendingLines)
                pendingLines.Add(text);
        }

        /// <summary>F1 "Drop marker": a `marker` line with the local actor and an optional note, so the
        /// report's Markers section can show the 30s of events around whatever a designer flags live.</summary>
        public void DropMarker(string note)
        {
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Marker, Now);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1);
            line.String(TelemetryKeys.Note, note ?? "");
            Log(line);
        }
    }
}
