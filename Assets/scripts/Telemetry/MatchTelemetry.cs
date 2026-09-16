using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Scene singleton (lives on the BuildingManager GameObject, next to ZonePresenceTracker) that
    /// owns this client's telemetry file: the match id, the writer, the session header, periodic
    /// flushing, join/leave/masterChanged, and F1 markers. T3/T4 log through <see cref="Log"/>;
    /// T4 also adds the master-only territory listeners here (ownership/capture/bounty/underAttack) -
    /// see the "master-only territory events" region below.
    ///
    /// Match identity (Room Properties `mId`/`mStart`) follows the exact pattern
    /// BuildingManager.WriteInitialSnapshotWhenClockIsReady already uses for the starting territory
    /// snapshot: wait for the server clock, re-check nobody else already wrote it, then write. The one
    /// difference is the write itself uses Photon's check-and-set (`expectedProperties`) on the ABSENT
    /// key, so two masters racing during a migration can't both win even inside that re-check's own
    /// race window - see TryClaimMatchIdentity's comment. A plain, unchecked write is the fallback if
    /// mId never echoes back within a few seconds of that write (see ClaimMatchIdentityWhenClockIsReady).
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

        // How long the master waits, after sending the check-and-set match identity write, for mId to
        // actually show up in the room's Custom Properties before falling back to a plain write - see
        // ClaimMatchIdentityWhenClockIsReady's own comment on why this fallback exists at all.
        private const float MatchIdentityEchoWaitSeconds = 5f;

        // Cap on how many log lines are held before the file has opened (match id + this client's own
        // actor number both known). Only ever a handful of lines in practice - join/leave/masterChanged
        // observed in the first frame or two after connecting - but a cap keeps a client that somehow
        // never resolves its identity from growing this list forever.
        private const int MaxPendingLines = 200;

        // Not readonly: OnLeftRoom replaces this with a fresh instance for the next match, so one
        // match's IO error (which permanently sets TelemetryWriter.Disabled) doesn't silently disable
        // telemetry for every match this client plays afterwards in the same session.
        private TelemetryWriter writer = new TelemetryWriter();
        private readonly List<string> pendingLines = new List<string>();

        private string matchId;
        private int matchStartMs;
        private Coroutine claimIdentityRoutine;
        private float flushTimer;

        // Reused for every event this component ever logs (opus review fix) - Begin/.../End is
        // safe to call again immediately once End() has returned the finished string (see
        // TelemetryLine's own class comment), and nothing here is reentrant, so one instance is
        // enough. PlayerTelemetry already follows this pattern; this class used to allocate a new
        // TelemetryLine per event instead.
        private readonly TelemetryLine line = new TelemetryLine();

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

        /// <summary>T3 review (item 10): raised right before the writer closes in every path that can
        /// end it - quitting, leaving the room, or this object being destroyed - so a recorder with
        /// its own buffered totals (PlayerTelemetry's `shots`/`dot` accumulators) gets one last chance
        /// to flush through Log before it would otherwise be silently dropped. Fixes a real loss:
        /// OnApplicationQuit used to close the writer with no such hook, and PlayerTelemetry's own
        /// OnDestroy (which flushes its own accumulators) is not guaranteed to run first - Unity does
        /// not order OnDestroy between two different GameObjects during teardown.</summary>
        public event System.Action BeforeClose;

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
            // Task T4: subscribed here, not Awake - this component lives on the SAME GameObject as
            // BuildingManager (T2 step 7's own wiring), and Unity guarantees every object's Awake
            // runs before any object's Start, so BuildingManager.Instance/ZonePresenceTracker.Instance
            // are already set by the time this runs, whichever component's Awake happened to run
            // first. Every one of these is a plain C# event, not a Photon callback, so subscribing
            // does not depend on PhotonNetwork.InRoom the way the match-identity calls below do.
            if (BuildingManager.Instance != null)
            {
                BuildingManager.Instance.OwnershipChanged += HandleOwnershipChanged;
                BuildingManager.Instance.CaptureProgressChanged += HandleCaptureProgressChanged;
                BuildingManager.Instance.BountyPaid += HandleBountyPaid;
            }
            else
            {
                Debug.LogError("[Telemetry] no BuildingManager in the scene - ownership/capture/bounty events will never be logged.");
            }

            if (ZonePresenceTracker.Instance != null)
                ZonePresenceTracker.Instance.UnderAttackChanged += HandleUnderAttackChanged;
            else
                Debug.LogError("[Telemetry] no ZonePresenceTracker in the scene - underAttack events will never be logged.");

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

        private void OnApplicationQuit()
        {
            BeforeClose?.Invoke();
            writer.Close();
        }

        private void OnDestroy()
        {
            if (BuildingManager.Instance != null)
            {
                BuildingManager.Instance.OwnershipChanged -= HandleOwnershipChanged;
                BuildingManager.Instance.CaptureProgressChanged -= HandleCaptureProgressChanged;
                BuildingManager.Instance.BountyPaid -= HandleBountyPaid;
            }
            if (ZonePresenceTracker.Instance != null)
                ZonePresenceTracker.Instance.UnderAttackChanged -= HandleUnderAttackChanged;

            BeforeClose?.Invoke();
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
            BeforeClose?.Invoke();
            writer.Close();
            writer = new TelemetryWriter(); // Fresh writer for whatever match comes next - see the field's own comment.
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
            line.Begin(eventName, Now);
            line.Int(TelemetryKeys.Actor, player.ActorNumber);
            line.Int(TelemetryKeys.Team, Teams.TryGetTeam(player, out int team) ? team : -1);
            Log(line);
        }

        // ---------------------------------------------------------------- master-only territory events (Task T4)
        //
        // Every handler below is subscribed on every client (Start, above) but only ever LOGS while
        // PhotonNetwork.IsMasterClient is true AT THE MOMENT the event fires - not gated at
        // subscribe time - so a client that becomes master mid-match starts logging immediately
        // without needing to resubscribe, and one that stops being master stops just as cleanly.
        // BuildingManager/ZonePresenceTracker themselves already only ever raise the master-facing
        // half of these (OwnershipChanged is raised on every client for its own state sync, but
        // CaptureProgressChanged/BountyPaid/UnderAttackChanged only carry meaning worth recording
        // once, from the master) - this guard is what turns "every client's copy of this event" into
        // "logged exactly once, by whichever client currently holds mastership".

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            line.Begin(TelemetryKeys.Ownership, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Tier, BuildingManager.Instance != null ? BuildingManager.Instance.TierOf(zone) : 0);
            line.Int(TelemetryKeys.OldOwner, oldOwner);
            line.Int(TelemetryKeys.NewOwner, newOwner);
            // The aggregator's dedupe key (T5): two masters logging the same applied change around a
            // master switch both stamp the SAME held-since, from the one snapshot they both applied.
            line.Int(TelemetryKeys.HeldSince, snapshot.HeldSinceMs(zone));
            Log(line);
        }

        /// <summary>opus review fix: BuildingManager.BountyPaid now also carries the PAYING team
        /// (whoever held the zone too long before losing it) alongside the team that was paid, and
        /// is only raised once BuildingManager's own write actually reaches Photon (Write returning
        /// false - not connected, no room - means nothing was written, so nothing to report).</summary>
        private void HandleBountyPaid(int zone, int paidTeam, int payingTeam, int amount, int heldMs)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            line.Begin(TelemetryKeys.Bounty, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, paidTeam);
            // Reusing OldOwner for "the team that paid" - the team that used to hold the zone and is
            // now losing the bounty to whoever just captured it, the same "who owned it before" idea
            // OldOwner already carries on `ownership`.
            line.Int(TelemetryKeys.OldOwner, payingTeam);
            line.Int(TelemetryKeys.Amount, amount);
            line.Float(TelemetryKeys.HoldSeconds, heldMs / 1000f);
            Log(line);
        }

        private void HandleUnderAttackChanged(int zone, bool underAttack)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            int owner = BuildingManager.Instance != null && BuildingManager.Instance.Current != null
                ? BuildingManager.Instance.Current.OwnerOf(zone)
                : -1;

            line.Begin(TelemetryKeys.UnderAttack, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.String(TelemetryKeys.State, underAttack ? "start" : "end");
            // Reusing NewOwner as a plain "this zone's owner right now" - there is no old/new pair
            // here, only one owner value, the same reuse-a-key reasoning TelemetryKeys' own class
            // comment describes for Bounty/Refund/UnderAttack.
            line.Int(TelemetryKeys.NewOwner, owner);
            Log(line);
        }

        /// <summary>opus review fix: classification itself moved to the stateless, pure
        /// <see cref="CaptureTransitionClassifier"/> (own file, its own edit-mode tests) - this is
        /// just the wiring: ask it what happened, and if anything did, log it with the zone's live
        /// player count (BuildingManager doesn't know that from a CaptureProgress alone).</summary>
        private void HandleCaptureProgressChanged(int zone, CaptureProgress oldProgress, CaptureProgress newProgress)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            string state = CaptureTransitionClassifier.Classify(oldProgress, newProgress,
                PhotonNetwork.ServerTimestamp, out int team, out float progress);
            if (state == null)
                return;

            line.Begin(TelemetryKeys.Capture, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, team);
            line.String(TelemetryKeys.State, state);
            line.Float(TelemetryKeys.Progress, progress);
            line.Int(TelemetryKeys.Players, BuildingManager.Instance != null ? BuildingManager.Instance.PlayersInZone(zone) : 0);
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
        /// against two masters racing during a migration:
        /// (1) a local check that the key is absent before even trying, same shape as
        ///     BuildingManager.WriteInitialSnapshotWhenClockIsReady re-checking after its own wait;
        /// (2) the write itself passes `expectedProperties = { mId: null }` (Photon's check-and-set),
        ///     so the SERVER only applies it if mId is still unset at the moment it processes the
        ///     op - closing the window between (1)'s local check and the op actually landing, which a
        ///     local check alone cannot close.
        ///
        /// `Room.SetCustomProperties`'s bool return is whether the operation could be SENT (are we
        /// connected, is there a room...), NOT whether the server's compare-and-swap accepted it -
        /// `LoadBalancingClient.OpSetPropertiesOfRoom` returns that same "could it be sent" bool and
        /// never surfaces the CAS outcome to the caller. So this does not branch on that return value at
        /// all; instead ClaimMatchIdentityWhenClockIsReady waits for the real answer - mId actually
        /// showing up in the room's Custom Properties, via the ordinary OnRoomPropertiesUpdate path,
        /// whether it was this client's write that won or another master's.</summary>
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

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            {
                claimIdentityRoutine = null;
                yield break;
            }

            // Someone else may have written it while this client waited for the clock.
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId))
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                claimIdentityRoutine = null;
                yield break;
            }

            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0)
                Debug.LogWarning("[Telemetry] server clock still reads 0 after waiting - writing the match start stamp anyway (events before it will read t = -1 - see the design doc's Error handling).");

            string claimedMatchId = Guid.NewGuid().ToString("N");
            var props = new Hashtable
            {
                { TelemetryKeys.RoomMatchId, claimedMatchId },
                { TelemetryKeys.RoomMatchStart, now },
            };
            var expectedAbsent = new Hashtable { { TelemetryKeys.RoomMatchId, null } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent);

            // Wait for mId to actually show up - see TryClaimMatchIdentity's own comment on why the call
            // above's return value is not the signal to wait for.
            float waitedForEcho = 0f;
            while (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId) && waitedForEcho < MatchIdentityEchoWaitSeconds)
            {
                waitedForEcho += Time.unscaledDeltaTime;
                yield return null;
            }

            claimIdentityRoutine = null;

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) yield break;

            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(TelemetryKeys.RoomMatchId))
            {
                ReadMatchIdentity(PhotonNetwork.CurrentRoom.CustomProperties);
                yield break;
            }

            // Safety net, not the primary path: a live single-master run confirmed the CAS write above
            // shows up almost immediately, but Photon's client source gives no documented guarantee that
            // an expected value of null matches a key that is ABSENT server-side (only that it matches an
            // existing null-valued one - see TryClaimMatchIdentity's own comment on the return value not
            // being proof either way). If mId still has not appeared after waiting, something silently
            // dropped or rejected the write for a reason other than "another master's write won" - fall
            // back to one plain, unchecked write rather than leaving the match with no identity at all
            // (no session file would ever open on any client without mId).
            Debug.LogWarning("[Telemetry] match identity never echoed back after the check-and-set write - falling back to a plain write.");
            var fallbackProps = new Hashtable
            {
                { TelemetryKeys.RoomMatchId, claimedMatchId },
                { TelemetryKeys.RoomMatchStart, PhotonNetwork.ServerTimestamp },
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(fallbackProps);
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
        /// freshly dated one.
        ///
        /// Small same-instant race, accepted rather than fixed: two local clients opening their file for
        /// the first time in the very same frame can both fail to see the other's not-yet-created
        /// directory and each create their own `_{mId8}` folder. Not silently harmless - T5's
        /// TelemetryLog.Load reads ONE folder (it merges every `.jsonl` FILE it finds there by `mId`,
        /// it does not itself go looking across sibling folders), so two folders from this race need
        /// their files copied into one before Build Report is pointed at them - the same manual step
        /// a multi-PC playtest already requires (design doc, "Files").</summary>
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
            line.Begin(TelemetryKeys.Marker, Now);
            line.Int(TelemetryKeys.Actor, PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1);
            line.String(TelemetryKeys.Note, note ?? "");
            Log(line);
        }
    }
}
