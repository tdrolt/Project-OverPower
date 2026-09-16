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
    /// not yet, that is out of scope for this task.
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

        // Task T4: `capture`'s own classifier remembers, per zone, the last team/direction that was
        // actively capturing or draining it - see ClassifyCaptureTransition's own comment on why
        // ComputeCurrentProgress (Assets/scripts/Player/Building capture.cs) collapses EVERY
        // non-active state (paused, on cooldown, just completed, just neutralised) to the same
        // CaptureProgress.Idle, with no team or progress carried over, so this is the only place
        // that memory survives to tell "resumed" apart from a fresh "started".
        private int[] lastCaptureTeam;
        private bool[] lastCaptureWasDrain;

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
            lastCaptureTeam = null;
            lastCaptureWasDrain = null;
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

            var line = new TelemetryLine();
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

        private void HandleBountyPaid(int zone, int team, int amount, int heldMs)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Bounty, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, team);
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

            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.UnderAttack, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.String(TelemetryKeys.State, underAttack ? "start" : "end");
            // Reusing NewOwner as a plain "this zone's owner right now" - there is no old/new pair
            // here, only one owner value, the same reuse-a-key reasoning TelemetryKeys' own class
            // comment describes for Bounty/Refund/UnderAttack.
            line.Int(TelemetryKeys.NewOwner, owner);
            Log(line);
        }

        /// <summary>Task T4: classifies a capture-progress transition per the plan's own table, using
        /// only what BuildingCapture.ComputeCurrentProgress actually publishes today - see this
        /// method's own remarks on what that means it CANNOT tell apart.
        ///
        /// KNOWN AMBIGUITIES (documented per the task, not fixed - fixing them is the capture-ring/
        /// minimap spec's job, not telemetry's): ComputeCurrentProgress collapses every state that
        /// is not actively capturing or actively draining to the exact same CaptureProgress.Idle
        /// (Team -1, Progress 0, Rate 0) - a capture interrupted by an enemy showing up, a capture
        /// that just COMPLETED, a drain that just paused because its own way in came under attack,
        /// and a drain that just neutralised the zone all look identical on the wire the instant
        /// they happen (old = active, new = Idle). Two best-effort signals recover most of the plan's
        /// labels from that:
        /// - "completed" vs "paused": read live off BuildingManager.Current.OwnerOf(zone) - if the
        ///   zone is NOW owned by the team that was capturing, the transition completed it. This can
        ///   race the ownership snapshot's own echo (a separate SetCustomProperties call from the
        ///   capture-progress one - see PublishCaptureProgress's own class comment on why they are
        ///   deliberately two calls): on the master's OWN completion, SetCaptured already applied the
        ///   new owner to its local fields before PublishProgressIfNeeded runs the same frame, so
        ///   Current (which only updates from the room's ECHO) can still show the OLD owner for one
        ///   frame. In that one-frame window a same-frame "completed" would misclassify as "paused" -
        ///   accepted rather than chased further; a true multi-frame pause (an enemy actually
        ///   interrupting a capture) is unaffected.
        /// - "neutralised" vs "drainPaused": the same OwnerOf(zone) read - neutral now means
        ///   neutralised, still owned by the draining team's target means paused. Same one-frame race
        ///   on the master's own neutralisation.
        /// - "resumed" vs "started" (and "drainStarted" after a pause): recovered by remembering, per
        ///   zone, the last team/direction (capture vs drain) this classifier saw actively moving the
        ///   bar (lastCaptureTeam/lastCaptureWasDrain) - if the SAME team resumes the SAME direction
        ///   after an Idle gap, it reads as "resumed"/"drainStarted" continuing rather than "started"
        ///   fresh. This is telemetry's own memory, not the game's - a genuinely fresh start by the
        ///   same team that also drained it once earlier in the match would be misread as "resumed"
        ///   if nothing else changed hands in between; in practice a capture completing or the zone
        ///   going neutral between the two both correctly reset the memory (see below), which covers
        ///   every realistic case.</summary>
        private void HandleCaptureProgressChanged(int zone, CaptureProgress oldProgress, CaptureProgress newProgress)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            EnsureCaptureMemory(zone);

            string state = ClassifyCaptureTransition(zone, oldProgress, newProgress);
            if (state == null)
                return; // No case in the plan's table matches - not expected from today's code.

            bool starting = state == "started" || state == "resumed" || state == "drainStarted";
            int team = starting ? newProgress.Team : oldProgress.Team;
            float progress = starting ? newProgress.Progress01 : oldProgress.Progress01;

            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Capture, Now);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, team);
            line.String(TelemetryKeys.State, state);
            line.Float(TelemetryKeys.Progress, progress);
            line.Int(TelemetryKeys.Players, BuildingManager.Instance != null ? BuildingManager.Instance.PlayersInZone(zone) : 0);
            Log(line);

            // Remember this zone's own active team/direction for the NEXT transition's own
            // started-vs-resumed question - see the method doc above. A transition INTO Idle
            // (paused/completed/neutralised/drainPaused) deliberately does NOT touch this: the whole
            // point is surviving the Idle gap so the following active transition can compare against
            // what was active before it, not what just went idle.
            if (newProgress.RatePerSecond01 > 0f)
            {
                lastCaptureTeam[zone] = newProgress.Team;
                lastCaptureWasDrain[zone] = false;
            }
            else if (newProgress.RatePerSecond01 < 0f)
            {
                lastCaptureTeam[zone] = newProgress.Team;
                lastCaptureWasDrain[zone] = true;
            }
            else if (state == "completed" || state == "neutralised")
            {
                // A finished capture or a full neutralise ends this zone's story cleanly - the next
                // active transition here is unambiguously a fresh "started"/"drainStarted", never a
                // "resume" of what just finished.
                lastCaptureTeam[zone] = -1;
            }
        }

        private string ClassifyCaptureTransition(int zone, CaptureProgress oldP, CaptureProgress newP)
        {
            bool oldIdle = oldP.Team < 0;
            bool newIdle = newP.Team < 0;

            if (oldIdle && newP.RatePerSecond01 > 0f)
                return lastCaptureTeam[zone] == newP.Team && !lastCaptureWasDrain[zone] ? "resumed" : "started";

            if (!oldIdle && oldP.RatePerSecond01 > 0f && newIdle)
                return OwnedByTeamNow(zone, oldP.Team) ? "completed" : "paused";

            // The plan's own table has no "drain resumed" label distinct from "drainStarted" (unlike
            // capturing, which distinguishes "started" from "resumed") - every Idle -> draining
            // transition is "drainStarted", whether or not the same team drained this zone before.
            if (oldIdle && newP.RatePerSecond01 < 0f)
                return "drainStarted";

            if (!oldIdle && oldP.RatePerSecond01 < 0f && newIdle)
                return IsNeutralNow(zone) ? "neutralised" : "drainPaused";

            return null;
        }

        private static bool OwnedByTeamNow(int zone, int team) =>
            team >= 0 && BuildingManager.Instance != null && BuildingManager.Instance.Current != null
            && BuildingManager.Instance.Current.OwnerOf(zone) == team;

        private static bool IsNeutralNow(int zone) =>
            BuildingManager.Instance != null && BuildingManager.Instance.Current != null
            && BuildingManager.Instance.Current.OwnerOf(zone) == TerritoryMap.Neutral;

        private void EnsureCaptureMemory(int zone)
        {
            int size = BuildingManager.Instance != null ? BuildingManager.Instance.ZoneCount : zone + 1;
            if (lastCaptureTeam != null && lastCaptureTeam.Length >= size)
                return;

            var team = new int[size];
            for (int i = 0; i < size; i++) team[i] = -1;
            lastCaptureTeam = team;
            lastCaptureWasDrain = new bool[size];
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
        /// directory and each create their own `_{mId8}` folder. Harmless - T5's report builder already
        /// has to merge every client's `.jsonl` by `mId`, not by which folder happened to hold it, so two
        /// folders for one match still merge into one report; it would only ever show up as a slightly
        /// odd folder listing, never a wrong number.</summary>
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
