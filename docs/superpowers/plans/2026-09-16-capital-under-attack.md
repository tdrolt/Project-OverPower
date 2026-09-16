# Capital Under Attack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- A zone is "under attack" while a living enemy stands in it, plus 3 s.
- A team can't capture through an owned neighbour that is under attack.
- Players whose capital is under attack respawn at their capital's T2.

**Architecture:**
- **Pure rules** (`ZoneThreat`, a `TerritoryMap.MayCapture` overload) are edit-mode tested.
- **A new `ZonePresenceTracker`** (scene, next to `BuildingManager`) has the master measure every living player's zone
  from replicated positions 5×/s. It publishes team bitmasks and last-seen stamps to Room Properties only on change.
  Every client answers `IsUnderAttack(zone)` the same way.
- **`BuildingCapture`'s master tick** re-checks the capture link every frame, so a capture holds while its link is
  under attack and resumes on its own.
- **`PlayerLifecycle`** picks the T2 spawn when the capital is under attack.

No RPCs are added (RpcList unchanged).

**Tech Stack:** Unity 6000.0.70f1, Photon PUN 2 (Room Properties), NUnit edit-mode tests, `unity` CLI, two-client
harness.

**Spec:** `docs/superpowers/specs/2026-09-16-capital-under-attack-design.md` (read it; it lists the verified code facts).

---

## Rules for every task (each has cost hours on this project)

1. Branch `limit-testing` only; push after each task's commits. `unity command editor_status` must answer before editing.
2. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `editor_stop`, poll until
   `playMode: "stopped"` and `compiling: false`.
3. `recompile_status` is the only compile truth. **Tests async only:**
   `unity command run_tests -- --mode editor --async_tests true`, then poll `test_status`.
4. **Dirty scene → modal dialog → silent Editor hang.** Before tests, recompile, build or scene open, eval
   `SceneManager.GetActiveScene().isDirty`. If it's dirty and not intended, reload with
   `EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", OpenSceneMode.Single)`. If `editor_status` times
   out: stop and report.
5. CLI: `unity command <name> -- --flag value`. For long scripts: `unity command --timeout 240 run_script -- --file ...
   --entry X.Y --timeout_ms 200000`. Eval writes `UnityEngine.Object`. Scratch files go in the session scratchpad
   `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad`.
6. **Photon:**
   - Inside an RPC, "local" is the receiver.
   - Late-joiner state goes in Custom Properties.
   - Damage/status is victim-side.
   - `PhotonNetwork.ServerTimestamp` reads 0 right after connecting, so guard it.
   - A player prefab's observed components come from its saved list; `PlayerNetSync` is the only `IPunObservable`.
   - Never rename or remove an RPC.
7. Never move a player with `transform.position`; use `PlayerDisplacement.TeleportTo`, to a point outside colliders.
8. Measure, don't calculate: use game-time stamps, not CLI wall time. Look at every capture yourself (616×576).
9. Every designer-facing value lives on a config/theme asset with a plain `[Tooltip]`, in one home. Comments explain
   *why*, for a designer reader.
10. After `AssetDatabase.SaveAssets()`, check `git status` for strays. `Assets/Gameplay/Config/GameplayConfig.asset`
    must stay unchanged.
11. Commit trailer: your own `Co-Authored-By:` line. No unmeasured number in a commit message.
12. Append judgement calls to `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under
    `## Capital under attack (2026-09-16)` as short [C] lines (outside the repo; don't commit it).

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/Match/Rules/ZoneThreat.cs` + `Assets/Tests/ZoneThreatTests.cs` | Presence masks, departure stamps, under-attack rule | B1 |
| `Assets/scripts/Match/Rules/TerritoryMap.cs` (modify) + `Assets/Tests/TerritoryMapTests.cs` (modify) | Threat-aware `MayCapture` overload | B1 |
| `Assets/scripts/Data/TerritoryConfig.cs` (modify) + asset | `underAttackLingerSeconds` (3) | B1 |
| `Assets/scripts/Match/ZonePresenceTracker.cs` | Master measures + publishes presence; `IsUnderAttack`, `IsTeamPresent` | B2 |
| `Assets/scripts/Player/Building capture.cs` (modify) | Per-tick link check; defender presence (if measured broken) | B2 |
| `Assets/Scenes/Game Scene.unity` (modify) | `ZonePresenceTracker` on `managers`; three T2 spawn points + arena triplet; RoomManager array | B2, B3 |
| `Assets/scripts/RoomManager.cs`, `Assets/scripts/Player/PlayerLifecycle.cs`, `Assets/scripts/Player/MatchUI.cs`, `Assets/scripts/UI/PlayerHud.cs`, `Assets/scripts/UI/UiTheme.cs` (modify) | Respawn at T2 + feedback | B3 |

---

### Task B1: Pure rules and the linger setting

**Files:**
- Create: `Assets/scripts/Match/Rules/ZoneThreat.cs`
- Test: `Assets/Tests/ZoneThreatTests.cs`
- Modify: `Assets/scripts/Match/Rules/TerritoryMap.cs`, `Assets/Tests/TerritoryMapTests.cs`
- Modify: `Assets/scripts/Data/TerritoryConfig.cs`, `Assets/Gameplay/Config/TerritoryConfig.asset`

- [ ] **Step 1: Write the failing `ZoneThreat` tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class ZoneThreatTests
    {
        private const int Linger = 3000;
        private static int[] NeverSeen() => new int[ZoneThreat.MaxTeams];

        [Test]
        public void ANeutralZoneIsNeverUnderAttack()
        {
            Assert.IsFalse(ZoneThreat.IsUnderAttack(-1, 0b111, NeverSeen(), 0, 50000, Linger));
        }

        [Test]
        public void AnEnemyStandingInsideIsAnAttackEvenWithDefendersThere()
        {
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0b011, NeverSeen(), 0, 50000, Linger));
        }

        [Test]
        public void OnlyTheOwnersTeamInsideIsNotAnAttack()
        {
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0b001, NeverSeen(), 0, 50000, Linger));
        }

        [Test]
        public void AnEnemyWhoLeftJustUnderTheLingerAgoStillCounts()
        {
            int[] seen = { 0, 10000, 0 };
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 12900, Linger));
        }

        [Test]
        public void AnEnemyWhoLeftJustOverTheLingerAgoNoLongerCounts()
        {
            int[] seen = { 0, 10000, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 13100, Linger));
        }

        [Test]
        public void TheOwnersOwnDepartureNeverCounts()
        {
            int[] seen = { 49900, 0, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 50000, Linger));
        }

        [Test]
        public void ReadsTheRightZonesSliceOfTheSharedArray()
        {
            // Zone 1's stamps start at index 3: team 2 left zone 1 a second ago.
            int[] seen = { 0, 0, 0, 0, 0, 49000 };
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0, seen, 3, 50000, Linger));
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 50000, Linger));
        }

        [Test]
        public void WithoutAServerClockOnlyPlayersStandingInsideCount()
        {
            int[] seen = { 0, 5, 0 };
            Assert.IsFalse(ZoneThreat.IsUnderAttack(0, 0, seen, 0, 0, Linger));
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0b010, seen, 0, 0, Linger));
        }

        [Test]
        public void TheLingerWorksAcrossTheServerClockWrap()
        {
            int[] seen = { 0, int.MaxValue - 1000, 0 };
            Assert.IsTrue(ZoneThreat.IsUnderAttack(0, 0, seen, 0, int.MinValue + 1000, Linger));
        }

        [Test]
        public void PresenceMasksCombineTeamsPerZoneAndIgnorePlayersOutsideZones()
        {
            var players = new List<(int team, int zone)> { (0, 2), (1, 2), (1, 5), (2, -1), (7, 3) };
            int[] masks = ZoneThreat.PresenceMasks(10, players);
            Assert.AreEqual(0b011, masks[2]);
            Assert.AreEqual(0b010, masks[5]);
            Assert.AreEqual(0, masks[3], "a team id outside 0..2 is ignored");
            Assert.AreEqual(0, masks[0]);
        }

        [Test]
        public void DeparturesStampOnlyTheTeamsThatLeft()
        {
            int[] before = new int[10]; before[2] = 0b011;
            int[] after = new int[10]; after[2] = 0b001;
            int[] seen = new int[30]; seen[2 * 3 + 0] = 111;

            Assert.IsTrue(ZoneThreat.StampDepartures(before, after, seen, 50000));
            Assert.AreEqual(50000, seen[2 * 3 + 1]);
            Assert.AreEqual(111, seen[2 * 3 + 0], "team 0 is still there, so its stamp is untouched");
            Assert.IsFalse(ZoneThreat.StampDepartures(after, after, seen, 60000));
        }
    }
}
```

- [ ] **Step 2: Add the failing `MayCapture` overload tests** to the end of the `TerritoryMapTests` class (inside the
class):

```csharp
        [Test]
        public void AnOwnedNeighbourUnderAttackIsNotALink()
        {
            // Team 0 owns only its capital 6; 6 is under attack, so T2 zone 0 can't be captured through it.
            Assert.IsFalse(RealMap().MayCapture(0, 0, StartOwners(), zone => zone == 6));
        }

        [Test]
        public void ASecondSafeOwnedNeighbourStillAllowsTheCapture()
        {
            var owners = new Dictionary<int, int> { { 6, 0 }, { 3, 0 }, { 7, 1 }, { 8, 2 } };
            Assert.IsTrue(RealMap().MayCapture(0, 0, owners, zone => zone == 6));
        }

        [Test]
        public void YourOwnCapitalStaysCapturableWhateverIsUnderAttack()
        {
            var owners = new Dictionary<int, int> { { 6, 1 }, { 7, 1 }, { 8, 2 } };
            Assert.IsTrue(RealMap().MayCapture(0, 6, owners, zone => true));
        }

        [Test]
        public void NoUnderAttackCheckBehavesLikeTheOriginalRule()
        {
            Assert.IsTrue(RealMap().MayCapture(0, 0, StartOwners(), null));
            Assert.IsFalse(RealMap().MayCapture(0, 3, StartOwners(), null));
        }
```

- [ ] **Step 3: Recompile and confirm the failing state.** Expected: compile errors naming `ZoneThreat` and the
4-argument `MayCapture`.

- [ ] **Step 4: Implement `ZoneThreat`**

```csharp
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// Whether a zone is "under attack" (Tudor, 2026-09-16): a living player of any team other than the owner stands in
    /// it, or left it less than the linger time ago. The linger stops the state flickering while someone steps on and
    /// off the edge. A neutral zone is never under attack; there is nothing to defend.
    ///
    /// Pure, so every client reaches the same answer from the same replicated arrays: a team bitmask per zone of who
    /// stands there now, and a server-ms stamp per zone per team of when that team last left.
    /// </summary>
    public static class ZoneThreat
    {
        public const int MaxTeams = 3;

        public static int TeamBit(int team) => team >= 0 && team < MaxTeams ? 1 << team : 0;

        /// <param name="owner">The zone's owner, or -1 for neutral.</param>
        /// <param name="presentMask">Bit t set = a living player of team t is inside the zone now.</param>
        /// <param name="lastSeenMs">Server ms each team last left a zone; this zone's three entries start at
        /// <paramref name="offset"/> (zone × MaxTeams). 0 = never.</param>
        /// <param name="nowMs">PhotonNetwork.ServerTimestamp. 0 = the clock isn't synced yet, so only players standing
        /// inside count.</param>
        public static bool IsUnderAttack(int owner, int presentMask, int[] lastSeenMs, int offset, int nowMs, int lingerMs)
        {
            if (owner < 0)
                return false;

            for (int team = 0; team < MaxTeams; team++)
            {
                if (team == owner)
                    continue;
                if ((presentMask & TeamBit(team)) != 0)
                    return true;
                if (nowMs == 0 || lastSeenMs == null || offset + team >= lastSeenMs.Length)
                    continue;
                int seen = lastSeenMs[offset + team];
                // unchecked: the server clock is an int that wraps, and a wrapped subtraction is still the true gap.
                if (seen != 0 && unchecked(nowMs - seen) < lingerMs)
                    return true;
            }
            return false;
        }

        /// <summary>A team bitmask per zone from each living player's (team, zone). Zone -1 (standing in no zone) and
        /// team ids outside 0..MaxTeams-1 are ignored.</summary>
        public static int[] PresenceMasks(int zoneCount, IEnumerable<(int team, int zone)> players)
        {
            var masks = new int[zoneCount];
            foreach ((int team, int zone) in players)
                if (zone >= 0 && zone < zoneCount)
                    masks[zone] |= TeamBit(team);
            return masks;
        }

        /// <summary>Stamps lastSeenMs[zone × MaxTeams + team] with nowMs for every team present in
        /// <paramref name="before"/> and gone in <paramref name="after"/>. Returns true if it stamped anything.</summary>
        public static bool StampDepartures(int[] before, int[] after, int[] lastSeenMs, int nowMs)
        {
            bool stamped = false;
            int zones = System.Math.Min(before.Length, after.Length);
            for (int zone = 0; zone < zones; zone++)
            {
                int left = before[zone] & ~after[zone];
                for (int team = 0; team < MaxTeams && left != 0; team++)
                {
                    if ((left & TeamBit(team)) == 0) continue;
                    int index = zone * MaxTeams + team;
                    if (index < lastSeenMs.Length)
                    {
                        lastSeenMs[index] = nowMs;
                        stamped = true;
                    }
                }
            }
            return stamped;
        }
    }
}
```

- [ ] **Step 5: Add the overload to `TerritoryMap`**, directly below the existing `MayCapture`. Change the existing
method's body to call the new overload with `null`:

```csharp
        /// <param name="ownerByZone">Current owner per zone; a missing zone or Neutral means nobody.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone) =>
            MayCapture(teamId, zoneId, ownerByZone, null);

        /// <summary>
        /// The same rule, where an owned neighbour only counts as a way in while it is NOT under attack (Tudor,
        /// 2026-09-16). A team whose capital is being attacked can't use the capital to take the T2 next to it, but a
        /// second safe owned neighbour still works. Your own capital stays capturable no matter what, as before.
        /// </summary>
        /// <param name="isUnderAttack">Asked for each owned neighbour; null = nothing is under attack.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone, System.Func<int, bool> isUnderAttack)
        {
            if (teamId < 0 || !adjacency.ContainsKey(zoneId))
                return false;
            if (ownerByZone.TryGetValue(zoneId, out int owner) && owner == teamId)
                return false;
            if (IsCapitalOf(zoneId, teamId))
                return true;
            foreach (int neighbour in AdjacentTo(zoneId))
                if (ownerByZone.TryGetValue(neighbour, out int neighbourOwner) && neighbourOwner == teamId
                    && (isUnderAttack == null || !isUnderAttack(neighbour)))
                    return true;
            return false;
        }
```

- [ ] **Step 6: Add the linger setting to `TerritoryConfig`**, after `recaptureCooldownSeconds` and its property:

```csharp
        [Tooltip("Seconds a zone still counts as under attack after the last enemy steps out of it. While your capital " +
                 "is under attack you respawn at its Tier 2 zone, and no zone counts as a way in for capturing while " +
                 "it's under attack. Stops both from flickering when someone steps on and off the edge.")]
        [SerializeField, Min(0f)] private float underAttackLingerSeconds = 3f;

        public float UnderAttackLingerSeconds => underAttackLingerSeconds;
```

Recompile first so Unity serializes the new field. Then eval-load the asset, read `UnderAttackLingerSeconds` (expect
3), and `AssetDatabase.SaveAssets()` only if the asset needs the field written. Run `git diff` on the asset: the only
change must be `underAttackLingerSeconds: 3`.

- [ ] **Step 7: Recompile clean; run tests async.** Expected: all pass, the previous total + 15. Scene not dirty.

- [ ] **Step 8: Commit + push:** `feat(match): ZoneThreat and a threat-aware capture link rule (capital under attack)`.

---

### Task B2: `ZonePresenceTracker`, per-tick link check, defender measurement

**Files:**
- Create: `Assets/scripts/Match/ZonePresenceTracker.cs`
- Modify: `Assets/scripts/Player/Building capture.cs`
- Modify: `Assets/Scenes/Game Scene.unity` (add the component to `managers`, assign `territoryConfig`)

- [ ] **Step 1: Measure the defender behaviour BEFORE changing anything (two clients).**
  1. Build the Player and bring both clients into one room (`two-client-harness.md` §3–§5). Editor = team A,
     Player = team B.
  2. A captures its T2 (15 s, or eval `BuildingManager.Instance.SetCaptured(zone, team, 0, 0)` on the master for
     speed; say which).
  3. Teleport B into A's T2 (B's link: B's team must own an adjacent zone. B's own capital doesn't neighbour A's T2,
     so give B an adjacent T3 via `SetCaptured` on the master first; say which zone).
  4. Confirm the drain starts: read `CaptureProgressOf(zone)` (negative rate).
  5. Teleport A into the same zone. Read the progress rate 1 s later.

  Report: **does the drain stop when the defender is present?** Record the numbers.

- [ ] **Step 2: Create `ZonePresenceTracker`**

```csharp
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Who stands in which capture zone, by team, kept separate from capture on purpose. BuildingCapture only tracks
/// players who are allowed to capture a zone, so an enemy with no adjacent zone, or a defender standing in their own
/// zone, never appears there. "Under attack" (Tudor, 2026-09-16: any living enemy standing in the zone, plus a short
/// linger) needs every living player.
///
/// The master measures it from every player's replicated position five times a second. It writes to Room Properties
/// only when a team enters or leaves a zone, so a late joiner or a new master reads the same state, and every client
/// answers IsUnderAttack the same way (see ZoneThreat for the rule itself).
/// </summary>
public class ZonePresenceTracker : MonoBehaviourPunCallbacks
{
    public const string PresentKey = "zPres";
    public const string LastSeenKey = "zSeen";

    // Not a gameplay value: how often the master re-measures. At 0.2 s a running player crosses about 1.5 m of a
    // 20 m circle between checks - far finer than the 3 s linger this feeds.
    private const float MeasureIntervalSeconds = 0.2f;

    [SerializeField, Tooltip("The shared territory numbers. The under-attack linger time is read from here.")]
    private TerritoryConfig territoryConfig;

    public static ZonePresenceTracker Instance { get; private set; }

    /// <summary>Diagnostic only: how many presence writes this client has sent (master only).</summary>
    public int PresenceWriteCount { get; private set; }

    private int[] presentMasks;   // index = zone
    private int[] lastSeenMs;     // index = zone × ZoneThreat.MaxTeams + team
    private float nextMeasureTime;
    private readonly List<(int team, int zone)> samples = new List<(int team, int zone)>();

    private void Awake()
    {
        Instance = this;
        if (territoryConfig == null)
            Debug.LogError($"[ZonePresence] {name}: Territory Config is not assigned - the under-attack linger falls back to 3 s.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>True while a living enemy of the zone's owner stands in it, or left under the linger time ago. Always
    /// false for a neutral zone, or before this client has read any presence.</summary>
    public bool IsUnderAttack(int zone)
    {
        BuildingManager manager = BuildingManager.Instance;
        if (manager == null || manager.Current == null || presentMasks == null || zone < 0 || zone >= presentMasks.Length)
            return false;
        float lingerSeconds = territoryConfig != null ? territoryConfig.UnderAttackLingerSeconds : 3f;
        return ZoneThreat.IsUnderAttack(manager.Current.OwnerOf(zone), presentMasks[zone], lastSeenMs,
                                        zone * ZoneThreat.MaxTeams, PhotonNetwork.ServerTimestamp,
                                        Mathf.RoundToInt(lingerSeconds * 1000f));
    }

    /// <summary>True while a living player of <paramref name="team"/> stands in the zone right now (no linger).</summary>
    public bool IsTeamPresent(int zone, int team) =>
        presentMasks != null && zone >= 0 && zone < presentMasks.Length && (presentMasks[zone] & ZoneThreat.TeamBit(team)) != 0;

    private void Update()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || Time.unscaledTime < nextMeasureTime)
            return;
        nextMeasureTime = Time.unscaledTime + MeasureIntervalSeconds;

        BuildingManager manager = BuildingManager.Instance;
        int nowMs = PhotonNetwork.ServerTimestamp;
        if (manager == null || manager.ZoneCount <= 0 || nowMs == 0)
            return;
        EnsureArrays(manager.ZoneCount);

        samples.Clear();
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (!Teams.TryGetTeam(player, out int team))
                continue;
            // A dead player isn't attacking anything. A missing "alive" property means they haven't died yet.
            if (player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool alive && !alive)
                continue;
            PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
            if (view == null)
                continue;
            samples.Add((team, manager.TryGetZoneAt(view.transform.position, out int zone) ? zone : -1));
        }

        int[] next = ZoneThreat.PresenceMasks(manager.ZoneCount, samples);
        bool changed = false;
        for (int i = 0; i < next.Length; i++)
            if (next[i] != presentMasks[i]) { changed = true; break; }
        if (!changed)
            return;

        ZoneThreat.StampDepartures(presentMasks, next, lastSeenMs, nowMs);
        presentMasks = next;
        Publish();
    }

    private void Publish()
    {
        // Clones: Photon keeps a reference to what it's given, and these arrays keep changing locally.
        var props = new Hashtable
        {
            [PresentKey] = (int[])presentMasks.Clone(),
            [LastSeenKey] = (int[])lastSeenMs.Clone(),
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
        {
            Debug.LogWarning("[ZonePresence] presence could not be sent to the room.");
            return;
        }
        PresenceWriteCount++;
    }

    public override void OnJoinedRoom() => ReadFrom(PhotonNetwork.CurrentRoom.CustomProperties);

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        // The master is the only writer and its own arrays are already current. Applying its echo could briefly
        // roll back a change it made after that write.
        if (!PhotonNetwork.IsMasterClient)
            ReadFrom(propertiesThatChanged);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // A new master carries on from the last state the room holds (the stamps must survive, or an attack that just
        // ended would stop lingering the moment the master changes).
        if (newMasterClient.IsLocal)
            ReadFrom(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    private void ReadFrom(IDictionary<object, object> props)
    {
        if (props == null)
            return;
        if (props.TryGetValue(PresentKey, out object present) && present is int[] masks)
            presentMasks = (int[])masks.Clone();
        if (props.TryGetValue(LastSeenKey, out object seen) && seen is int[] stamps)
            lastSeenMs = (int[])stamps.Clone();
    }

    private void EnsureArrays(int zoneCount)
    {
        if (presentMasks == null || presentMasks.Length != zoneCount)
            presentMasks = new int[zoneCount];
        if (lastSeenMs == null || lastSeenMs.Length != zoneCount * ZoneThreat.MaxTeams)
            lastSeenMs = new int[zoneCount * ZoneThreat.MaxTeams];
    }
}
```

If a namespace or accessor differs from what the code expects (`Overpower.Data` for `TerritoryConfig`,
`Overpower.Net.Teams.TryGetTeam(Player, out int)`, `TerritorySnapshot.OwnerOf`), fix the reference and report it.

- [ ] **Step 3: Per-tick link check in `BuildingCapture`** (`Assets/scripts/Player/Building capture.cs`)

Add near the other private helpers:

```csharp
    // Cached so the per-frame capture check below doesn't allocate a new delegate for every tower every frame.
    private static readonly System.Func<int, bool> ZoneUnderAttack =
        zone => ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(zone);

    /// <summary>Master, every frame a team is actually capturing or draining this zone: may that team still capture
    /// it right now? OnTriggerEnter checks the plain adjacency rule once, on entry. This re-asks the threat-aware rule
    /// (Tudor, 2026-09-16: no capturing through an owned zone that is under attack), so a capture already in progress
    /// holds the moment its link comes under attack and carries on by itself once the link is safe again, with nobody
    /// having to step out and back in.</summary>
    private bool TeamMayCaptureNow(int team)
    {
        BuildingManager manager = BuildingManager.Instance;
        if (manager == null || manager.Map == null || manager.Current == null)
            return true; // Nothing to judge by yet; the entry check already applied the plain rule.
        return manager.Map.MayCapture(team, buildingID, manager.Current.OwnersByZone(), ZoneUnderAttack);
    }
```

In `CalculateCaptureProgress`, change the advance condition from
`if (eligiblePlayers.Any() && !enemyPlayers)` to:

```csharp
        if (eligiblePlayers.Any() && !enemyPlayers && TeamMayCaptureNow(capturingID))
```

(The existing `else` branch only stops the sound, so progress holds, exactly like capturers stepping out.)

In `ComputeCurrentProgress`, keep the bar honest. Change `if (!eligiblePlayers.Any() || enemyPresent)` to:

```csharp
        if (!eligiblePlayers.Any() || enemyPresent || !TeamMayCaptureNow(capturingID))
```

In `HandleCapturedState`, only count enemies whose team may capture now:

```csharp
        bool enemyPresent = playersInZone.Any(p => p.teamID != controllingTeam && TeamMayCaptureNow(p.teamID));
```

**Only if Step 1 measured that a defender does NOT stop a drain**, also change the next line to:

```csharp
        // BuildingCapture never adds a zone's own team to playersInZone (MayCapture refuses a zone you already own),
        // so defenders standing here were invisible and an enemy drained the zone right past them. Presence is tracked
        // for every living player, so ask it instead.
        bool teamMemberPresent = playersInZone.Any(p => p.teamID == controllingTeam)
            || (ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsTeamPresent(buildingID, controllingTeam));
```

If Step 1 showed defenders already stop a drain, leave that line unchanged and say so.

- [ ] **Step 4: Scene.** With the scene clean, add `ZonePresenceTracker` to the `managers` GameObject (the one holding
`BuildingManager`), assign `territoryConfig` (`Assets/Gameplay/Config/TerritoryConfig.asset`), save the scene, and
read it back. The component is not an `IPunObservable` and adds no `PhotonView`.

- [ ] **Step 5: Recompile clean; tests async** (all pass, same total as B1). Scene not dirty.

- [ ] **Step 6: Two-client verification** (rebuild the Player; Editor = A, Player = B; game-time stamps):
  1. Teleport B into A's capital. On A, poll `ZonePresenceTracker.Instance.IsUnderAttack(capitalOfA)` with
     `Time.time`: it becomes true within 0.5 s. Teleport B out; report the game time it turns false. **Expect ~3 s** after
     leaving (±0.3).
  2. A in A's neutral T2 (adjacent only to A's capital among A's zones), B in A's capital. After 3 s, A's T2 capture
     progress is still Idle (`CaptureProgressOf`). Teleport B out and don't move A. Report the game time the capture
     starts (**expect ~3 s**) and when it completes (**15 s** later). Also check `PresenceWriteCount` stayed small (only
     on enter/leave).
  3. If Step 3 changed the defender line, repeat Step 1's scenario: with A present, the drain stops (rate 0 / Idle).
  4. Regression: a solo T2 capture with nobody else around takes 15 s.
  5. Late joiner: while the Editor (A's team) is teleported into the Player's capital, quit and relaunch the Player
     so it rejoins the same room. The rejoined Player reads `IsUnderAttack(its own capital)` = true before any
     further movement (it can only come from the Room Properties). Report the value and the time since rejoin.

- [ ] **Step 7: Commit + push:** `feat(match): zone presence and no capturing through a zone under attack`.

---

### Task B3: Respawn at T2 while the capital is under attack

**Files:**
- Modify: `Assets/scripts/RoomManager.cs`, `Assets/scripts/Player/PlayerLifecycle.cs`, `Assets/scripts/Player/MatchUI.cs`,
  `Assets/scripts/UI/PlayerHud.cs`, `Assets/scripts/UI/UiTheme.cs` (+ its asset)
- Modify: `Assets/Scenes/Game Scene.unity` (three spawn points, `RoomManager` array, `ArenaSymmetry` triplet)

- [ ] **Step 1: `RoomManager` field**, next to `teamSpawnPoints`:

```csharp
    [Tooltip("Where each team respawns while its capital is under attack (index = team, same order as Team Spawn " +
             "Points). Each sits in that capital's Tier 2 zone, whoever owns it. Placed by the arena tool: move the Team " +
             "2 one and press Rebuild thirds on Enviorment/Arena.")]
    public Transform[] capitalUnderAttackSpawnPoints;
```

- [ ] **Step 2: Spawn points in the scene** (scratch script, scene clean first):
  1. Under `Spawn Points`, create `team (2) under attack` at `(65.05, <team (2) y>, 92.36)`. That is 5 m from T2
     tower 2 (z 87.36 after A3) toward capital 8. Give it the same rotation as `team (2)`.
  2. Create `team (1) under attack` and `team under attack` anywhere.
  3. Add a snapped triplet `[team (2) under attack, team (1) under attack, team under attack]` to
     `Enviorment/Arena`'s `ArenaSymmetry`, then `ArenaSymmetryBuilder.Rebuild(arena, false)`.
  4. Set `RoomManager.capitalUnderAttackSpawnPoints = [team (1) under attack, team under attack, team (2) under attack]`
     (index 0 = team 0 = bottom-left, like `teamSpawnPoints`).
  5. Save.

  Read back all three positions. Each must be 5.0 m from its own T2 tower, and `TryGetZoneAt` must return that T2's
  zone.

- [ ] **Step 3: `PlayerLifecycle` chooses the spawn.** Replace the spawn block in `RespawnPlayer`:

```csharp
        RoomManager roomManager = FindObjectOfType<RoomManager>();
        bool atUnderAttackSpawn = false;
        Transform spawn = roomManager != null ? ChooseSpawnPoint(roomManager, teamID, out atUnderAttackSpawn) : null;
        if (spawn != null)
            TeleportToSpawnPoint(spawn.position, spawn.rotation);
```

After `SetAlive(true)`, and before the `[VIS]` log, add the feedback and extend the log:

```csharp
        if (atUnderAttackSpawn)
            playerHud?.ShowToast(theme.capitalUnderAttackRespawnToast);
```

Change the log to include `atUnderAttackSpawn ? "T2 (capital under attack)" : "capital"`.

(`atUnderAttackSpawn` is declared before the ternary because an `out` variable declared inside one isn't definitely
assigned afterwards. If `playerHud`/`theme` references don't exist on `PlayerLifecycle`, get them the way
`PlayerLifecycle` already gets its sibling components, and report.)

Add the method:

```csharp
    /// Tudor, 2026-09-16: while your capital is under attack (an enemy standing in it, or who just left - see
    /// ZonePresenceTracker) you come back at your capital's Tier 2 zone instead, whoever owns it, rather than
    /// straight into the fight. Decided when the timer ends, not when you died, because the attack may be over by
    /// then.
    private Transform ChooseSpawnPoint(RoomManager roomManager, int teamID, out bool atUnderAttackSpawn)
    {
        atUnderAttackSpawn = false;
        Transform normal = roomManager.teamSpawnPoints != null && roomManager.teamSpawnPoints.Length > teamID
            ? roomManager.teamSpawnPoints[teamID] : null;

        BuildingManager manager = BuildingManager.Instance;
        ZonePresenceTracker presence = ZonePresenceTracker.Instance;
        if (manager == null || manager.Map == null || presence == null)
            return normal;

        int capital = manager.Map.CapitalOf(teamID);
        if (capital < 0 || !presence.IsUnderAttack(capital))
            return normal;

        Transform[] underAttack = roomManager.capitalUnderAttackSpawnPoints;
        if (underAttack == null || underAttack.Length <= teamID || underAttack[teamID] == null)
        {
            Debug.LogWarning($"[PlayerLifecycle] team {teamID}'s capital is under attack but RoomManager has no Capital Under Attack Spawn Point for it - respawning at the capital.");
            return normal;
        }

        atUnderAttackSpawn = true;
        return underAttack[teamID];
    }
```

- [ ] **Step 4: Waiting-panel line.**
  - **`MatchUI`:** add `public void SetRespawnNote(string text)`. On first use, it builds a TextMeshProUGUI label under
    `respawnPanel`, below its existing content, styled from `UiTheme` the way `PlayerHud` builds labels. It sets the
    text, and hides the label when the text is empty.
  - **`PlayerLifecycle`:** in the existing local `Update` path that runs while waiting to respawn (the method that
    checks `matchUI.IsWaitingForRespawn`), call `matchUI.SetRespawnNote(presence != null &&
    presence.IsUnderAttack(capital) ? theme.capitalUnderAttackRespawnNote : "")` every frame. Only write the text when
    the bool changes.
  - Clear the note in the respawn path.

- [ ] **Step 5: `PlayerHud.ShowToast(string)`.** Generalise the bounty toast. Rename the pieces to `toastGo` /
`toastText` / `toastHideAtTime`, add `public void ShowToast(string text)`, and make `HandleBountyReceived` call
`ShowToast($"Bounty +{amount}")`. Behaviour must be identical (same duration field, unscaled time).

- [ ] **Step 6: `UiTheme` text** (with tooltips; set the asset values and `git diff` the asset):
  - `capitalUnderAttackRespawnNote` = `"Your capital is under attack - you will respawn at your Tier 2 zone"`
  - `capitalUnderAttackRespawnToast` = `"Respawned at Tier 2: capital under attack"`

- [ ] **Step 7: Recompile clean; tests async**, including `ArenaSymmetrySceneTests` (the new triplet must validate) and
`NetworkPrefabObservablesTests`. Scene not dirty.

- [ ] **Step 8: Two clients** (rebuild the Player; A = Editor, B = Player):
  1. B stands in A's capital. Kill A: apply lethal damage on A's own client via its `PlayerHealth` with B as the
     source. Capture the respawn panel during the wait (616×576) and read the note text. After respawn, read A's
     `Rigidbody.position`: it must be within 0.5 m of A's under-attack spawn point. Capture the toast. Look at both
     captures.
  2. B leaves A's capital. After 4 s of game time, kill A again: A respawns at the capital spawn, and the note stays
     empty during the wait.
  3. With B still in A's capital, A (respawned at T2) stands still in the T2 for 5 s: no capture progress (B2 rule).
     Report `CaptureProgressOf`.

- [ ] **Step 9: Commit + push:** `feat(match): respawn at Tier 2 while your capital is under attack`.

---

## Self-review against the spec

| Spec item | Task |
|---|---|
| Under attack = living enemy in the zone + 3 s linger, neutral never | B1 (`ZoneThreat` + tests), B2 (tracker) |
| Presence separate from capture eligibility, master-measured, Room Properties on change, late joiner and new master | B2 steps 2, 6.5 |
| Linger tunable on `TerritoryConfig` | B1 step 6 |
| Per-link capture block, second safe link allows, own capital unchanged | B1 step 5 + tests |
| A capture in progress holds and resumes without re-entry; drain likewise; the bar reflects it | B2 step 3 (`CalculateCaptureProgress`, `ComputeCurrentProgress`, `HandleCapturedState`) |
| Defenders: measure first, fix only if broken | B2 steps 1, 3, 6.3 |
| Respawn at the capital's T2 when the timer ends, whoever owns it; spawn 5 m toward the capital; arena triplet | B3 steps 1–3 |
| Waiting-panel line + toast, text on `UiTheme` | B3 steps 4–6 |
| No RPCs added; observables test passes; arena scene test passes | B2 step 2 (no RPC), B3 step 7 |
| Verification scenarios 1–5 | B2 step 6, B3 step 8 |
