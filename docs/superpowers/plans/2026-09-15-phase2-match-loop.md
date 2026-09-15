# Phase 2 — Match Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the combat prototype into a playable match: tiered territory with a centre zone, visible capture progress, per-player gold from territory, a priced shop on the existing P screen, health regen in safe tiers, capture bounties, the OverPower comeback buff, and GDD phase transitions — verified with a real second client where it matters.

**Architecture:** Every rule is a pure class in `Assets/scripts/Match/Rules/` (namespace `Overpower.Match`, edit-mode tested). Match **state** lives in Photon Custom Properties, never buffered RPCs: territory in Room Properties written by the master (ownership, hold stamps, bounty, capture progress), gold in each player's own Player Properties written by that player. MonoBehaviours only read state, call the rules, and present. All numbers live on `TerritoryConfig`, `GameplayConfig`, `ArmorConfig`, `WeaponDefinition`/`AbilityDefinition.GoldCost` or `UiTheme`.

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, uGUI + TextMeshPro, NUnit edit-mode tests, `unity` CLI (Editor server) + the Runtime server in a development Player for the second client.

**Sources (read before a task, don't re-derive):**
- GDD facts, page-cited: `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\gdd-match-loop-facts.md`
- Current code facts, verified 2026-09-15: `...\Limit Test\phase2-code-survey.md`
- Earlier design review (partly stale — the survey wins): `...\Limit Test\addenda-phase2.md`
- Original task list: `docs/superpowers/plans/2026-09-12-limit-test.md` § "Phase 2 — Match loop"
- Decisions to report to Tudor: `...\Limit Test\assumptions-for-tudor.md` (**append every [C] decision you make**)

Markers: [T] Tudor · [G] GDD · [C] Claude.

---

## Decisions taken for this plan (Tudor away; all listed in assumptions-for-tudor.md)

| Topic | Decision | Why |
|---|---|---|
| Gold authority | **Each player's own client accrues and publishes its gold** (Player Property `gold`) from the replicated territory state. Not master-ticked (deviates from the 2026-09-12 plan) [C] | One writer per value, no spend/income race, no new RPC, survives master migration for free. No anti-cheat requirement in a prototype. |
| Income split | Per-player income = team income ÷ `playersPerTeam` (3) [G p.37-38 sheet] | The GDD sheet models team income /3 per player. |
| Starting gold | 0 [C] (GDD silent) | Starting kit is free [G p.17-18]. |
| Territory state | Room Properties, master is the only writer; the `AllBuffered` ownership RPC is retired (method kept for RpcList) [C, addenda] | Late joiners read one property; hold timers survive master migration. |
| Tower tiers | Capitals 6/7/8 = T1; 0/1/2 = T2; 3/4/5 = T3; new 9 = T4 adjacent to 3/4/5 [C, addenda, §4a default] | Derived from the scene's adjacency. |
| Capture time | `captureSeconds` per tier is the time for ONE player; N players are N× faster (existing behaviour) [G times, C speed-up] | GDD silent on multi-player speed. |
| Shop location | Any territory your team owns, out of combat ≥ 5s [G p.18-19; C resolves the p.29 "capital" conflict] | The purchasing rule is the specific rule. |
| Prices | Path upgrade 1200, leaf 1600, armor 1400/1800, ultimate 1550 [G]; mobility/equipment change 800 [C]; changing ultimate costs its price again [C]; weapon/armor reset refunds 50% of what that category cost [G rate] | |
| Starting kit | Base weapon, armor level 0, the prefab's starting mobility + equipment free; **ultimate slot empty until bought** [G] | |
| Free loadout toggle | `GameplayConfig.freeLoadout` (asset value **true**, so Tudor's pending weapon test via P stays free); false = full economy [C] | Tudor hasn't done his free-test pass yet. |
| Health regen | T1 10 HP/s, T2 4 HP/s, only in your own zone and out of combat (the armor clock) [G tiers, C rates + gate] | GDD gives no rate. |
| Bounty | Full bounty to **each** player of the capturing team; the hold that counts is the previous owner's uninterrupted hold ≥ 300s ending in neutralisation [G values, C split] | GDD's per-player sheet lists bounty flat. |
| OverPower | "3 highest parameters" = damage, fire rate, range, each +10% [C, 2026-09-12 plan]; "near" = within 15m of the edge of a zone your team owns [C] | |
| Phases | Elimination and phase are recomputed by the master from replicated state and stored in Room Properties; the three static tallies in `PlayerLifecycle` are retired [C, addenda] | They don't survive master migration or a second match. |
| Phase 2 transition | Tier-3 zones go neutral and survivors return to their capitals [G]; **map geometry reduction cut** [T plan instruction]; capital adoption during a last stand built if time allows [G], cuttable | |
| Territory win (hold all 3 capitals) | Kept as an extra win condition [earlier project decision] | Not in GDD; listed. |
| Tier 4 vision perk | Not built [T: vision out of scope] | No fog of war exists. |
| Two-client verification | A development Player driven through the Runtime server (Task 2.0) | Many Phase 2 claims can't be proven from one client. |

---

## Rules for every task (from HANDOFF §6 — each one has cost time)

1. Branch `limit-testing` only; never touch or push `main`. Push after each task's commits.
2. `unity command editor_status` must answer before editing (only it proves the Editor is alive).
3. **Never `run_tests`, `recompile`, or edit a `.cs` file while in Play Mode.** Stop, confirm `playMode: "stopped"`. A timeout → stop and report.
4. CLI: `unity command <name> -- --flag value` (note `--`). C# via `eval_file -- --file <path>` or `run_script -- --file <path> --entry X.Y --timeout_ms 120000` (30s default budget). Scratch files in the session scratchpad, never under `Assets/`. Write `UnityEngine.Object`.
5. `recompile_status` is the only authoritative compile check.
6. Prefab edits: `LoadPrefabContents` → edit → `SaveAsPrefabAsset` → `UnloadPrefabContents`, read back. Scene edits: open scene, `SerializedObject`, `EditorSceneManager.SaveScene`, never while play-mode harness objects exist. `SaveAssets()` flushes every dirty asset — check `git status` for strays. Towers 0/3/6 are disconnected GameObjects; 1/2/4/5/7/8 are prefab instances — edit the scene objects, not the prefab.
7. **Photon:** RpcList is index-based — never rename an RPC; a new RPC is appended by `PhotonEditor.UpdateRpcList()` + `AssetDatabase.SaveAssets()` and the asset committed (this plan needs none). Inside an RPC "local" is the receiver. `PhotonNetwork.Instantiate` returns only the caller's copy. Damage and status are victim-side. Late-joiner state goes in Custom Properties. `PlayerNetSync` is the only `IPunObservable`.
8. Measure, don't calculate: armor + health; game timestamps; captures looked at, at the Editor's real Game view (616×576); count from the object's own state, not global event buses.
9. Every gameplay/visual value: `[SerializeField]` (or public field on a config SO) + plain-language `[Tooltip]`, one home. Comments explain *why* for a designer reader.
10. `EditorApplication.isPaused` disconnects Photon; `Time.timeScale = 0` despawns projectiles.
11. Commit messages end with the implementer's own `Co-Authored-By` line. No number in a commit message unless measured.
12. Append every judgement call to `assumptions-for-tudor.md` under "Phase 2 — match loop".

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/Match/Rules/TerritoryMap.cs` + `Assets/Tests/TerritoryMapTests.cs` | Who may capture what (adjacency, capitals) | 2.1a |
| `Assets/scripts/Data/TerritoryConfig.cs` + `Assets/Gameplay/Config/TerritoryConfig.asset` | Per-tier numbers, hold time, split, starting gold, decay/cooldown | 2.1a |
| `Assets/scripts/Match/Rules/TerritorySnapshot.cs` + tests | Room-property territory state and its transitions; bounty rule | 2.1b, 2.5 |
| `Assets/scripts/BuildingManager.cs`, `Assets/scripts/Player/Building capture.cs` (modify) | Master writes snapshot; everyone applies it; tiers drive capture | 2.1b |
| `Assets/Scenes/Game Scene.unity` (modify) | Tier per tower; tower 9 | 2.1a, 2.1c |
| `Assets/scripts/Match/Rules/CaptureProgress.cs` + tests, `Assets/scripts/Match/CaptureProgressView.cs` | Replicated, extrapolated capture progress + world bar | 2.1d |
| `Assets/scripts/Match/Rules/GoldMath.cs` + tests, `Assets/scripts/Match/GoldWallet.cs` | Income maths; the player's wallet | 2.2 |
| `Assets/scripts/Match/Rules/HealthRegenRule.cs` + tests; `PlayerHealth.cs` (modify) | Tier regen | 2.3 |
| `Assets/scripts/Match/Rules/ShopRules.cs` + tests; `LoadoutScreen.cs` (modify) | Purchase gate, prices, refunds | 2.5 |
| `Assets/scripts/Match/Rules/OverPowerState.cs` + tests; `Assets/scripts/Match/OverPowerBuff.cs`; `WeaponFiring.cs` (modify) | Comeback buff | 2.6 |
| `Assets/scripts/Match/Rules/MatchPhaseRules.cs` + tests; `Assets/scripts/Match/MatchDirector.cs`; `PlayerLifecycle.cs` (modify) | Elimination, phases | 2.7 |
| `Resources/loops/Limit Test/two-client-harness.md` | How to build, launch and drive the second client | 2.0 |

Order (dependencies): 2.0 → 2.1a → 2.1b → 2.1c → 2.1d → 2.2 → 2.3 (regen) → 2.4 (bounty) → 2.5 (shop) → 2.6 (OverPower, cuttable) → 2.7 (phases, cuttable last) → 2.8 wrap-up → Final: `findings.md`.
**Task numbers differ from the 2026-09-12 plan** (there: 2.2 gold, 2.3 shop, 2.4 OverPower, 2.5 bounty, 2.6 phases); this order follows the dependencies.

---

### Task 2.0: Two-client harness

**Files:** Create `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\two-client-harness.md`. Possibly modify Project Settings (Pipeline Runtime enable), `.gitignore` (build output folder). No gameplay code.

The Pipeline package runs a **Runtime** command server (ports 7900–7949) inside a *development* Player (Mono) when Project Settings › Pipeline › Runtime is enabled. `unity command --runtime <player exe name> eval "<code>"` then drives that Player the way `eval` drives the Editor.

- [ ] **Step 1: Check prerequisites and report them.** Read `ProjectSettings/` for the Pipeline runtime setting (find the asset the "Pipeline" Project Settings page writes), the scripting backend (`PlayerSettings` — must be Mono for Standalone), Build Settings scenes (which scene loads first and how a player reaches `Game Scene` and joins a room — read the first scene's scripts), and `.gitignore` (is `Builds/` ignored? if not, add `Builds/`).
- [ ] **Step 2: Enable the Runtime server** for development builds (the Project Settings value from Step 1) and commit that settings change alone.
- [ ] **Step 3: Build** a Windows Standalone development build to `Builds/Client2/OverPower.exe` via `unity command build` (read `unity command --query build --detail full` for flags: development=true, target StandaloneWindows64). Poll `build_status`. Report duration and size.
- [ ] **Step 4: Launch** it windowed: `Builds/Client2/OverPower.exe -screen-fullscreen 0 -screen-width 800 -screen-height 600` (PowerShell `Start-Process`). Confirm the Runtime server answers: `unity command --runtime OverPower runtime_status` (or `unity command --runtime OverPower eval -- "return UnityEngine.Application.productName;"` — find the exact runtime flag form with `unity command --help`).
- [ ] **Step 5: Put both clients in one room.** Editor: `editor_play`, join via `RoomManager.JoinGame()` when connected and not in a room. Player: the same through runtime eval (if the first scene is a menu, drive it to `Game Scene` the way a human would — call the menu button's handler). Poll until both report `PhotonNetwork.InRoom` and `CurrentRoom.Name` equal and `PlayerCount == 2`. Report both actor numbers and teams.
- [ ] **Step 6: Prove a cross-client read and write:** on the Player, `PlayerLoadout.SetWeapon(2)` for its own player; on the Editor, read the remote player's `weaponId` Custom Property → 2. On the Editor, fire the Editor player's baseline weapon at the Player's avatar position (aim via reflection as earlier harnesses); on the Player, read its own `PlayerHealth` armor+health drop (damage is victim-side, so it lands on the Player's own client).
- [ ] **Step 7: Write `two-client-harness.md`** — exact commands for build, launch, room join on both, reading/writing on each side, shutting the Player down (`Stop-Process`), and gotchas found. State plainly what could not be made to work.
- [ ] **Step 8: Shut down** the Player, stop play mode, `git status` clean (build output ignored), commit + push.

If the Runtime server cannot be enabled or eval doesn't run in a Player (IL2CPP, stripping), stop after Step 4, document why, and every later "two-client" step falls back to single-client simulation with the gap stated in the report and in findings.

---

### Task 2.1a: Territory rules, tiers and config (pure + data)

**Files:**
- Create: `Assets/scripts/Match/Rules/TerritoryMap.cs`, `Assets/Tests/TerritoryMapTests.cs`
- Create: `Assets/scripts/Data/TerritoryConfig.cs`, `Assets/Gameplay/Config/TerritoryConfig.asset`
- Modify: `Assets/scripts/Player/Building capture.cs` (a `tier` field; config drives capture numbers), `Assets/Scenes/Game Scene.unity` (tier per tower, config reference)

The adjacency gate already exists inline in `BuildingCapture.OnTriggerEnter` (survey §1). This task moves the *rule* into a tested pure class and makes tiers data; behaviour for the nine current towers must not change except capture time.

- [ ] **Step 1: Write the failing tests** (`Assets/Tests/TerritoryMapTests.cs`):

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TerritoryMapTests
    {
        // The scene's real adjacency (phase2-code-survey.md) plus the planned centre 9 -> {3,4,5}.
        private static TerritoryMap RealMap() => new TerritoryMap(
            new List<(int, IEnumerable<int>)>
            {
                (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                (9, new[] { 3, 4, 5 }),
            },
            new List<(int, int)> { (6, 0), (7, 1), (8, 2) });

        private static Dictionary<int, int> StartOwners() =>
            new Dictionary<int, int> { { 6, 0 }, { 7, 1 }, { 8, 2 } };

        [Test]
        public void AZoneNextToOneYouOwnIsCapturable()
        {
            Assert.IsTrue(RealMap().MayCapture(0, 0, StartOwners()));
        }

        [Test]
        public void AZoneWithNothingOfYoursNextToItIsNot()
        {
            Assert.IsFalse(RealMap().MayCapture(0, 3, StartOwners()));
        }

        [Test]
        public void AZoneYouAlreadyOwnIsNotCapturable()
        {
            Assert.IsFalse(RealMap().MayCapture(0, 6, StartOwners()));
        }

        [Test]
        public void YourOwnCapitalIsAlwaysCapturableEvenWithNothingAdjacent()
        {
            var owners = new Dictionary<int, int> { { 6, 1 }, { 7, 1 }, { 8, 2 } };
            Assert.IsTrue(RealMap().MayCapture(0, 6, owners));
        }

        [Test]
        public void AnEnemyCapitalNeedsAnAdjacentZone()
        {
            Assert.IsFalse(RealMap().MayCapture(1, 6, StartOwners()));
            var owners = StartOwners();
            owners[0] = 1;
            Assert.IsTrue(RealMap().MayCapture(1, 6, owners));
        }

        [Test]
        public void TheCentreNeedsAFlankingZone()
        {
            var owners = StartOwners();
            owners[0] = 0;
            Assert.IsFalse(RealMap().MayCapture(0, 9, owners));
            owners[3] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners));
        }

        [Test]
        public void AdjacencyWorksInBothDirectionsEvenIfListedOnce()
        {
            var map = new TerritoryMap(
                new List<(int, IEnumerable<int>)> { (3, new int[0]), (9, new[] { 3 }) },
                new List<(int, int)>());
            CollectionAssert.Contains(map.AdjacentTo(3), 9);
        }

        [Test]
        public void AnUnknownZoneOrNoTeamIsNeverCapturable()
        {
            Assert.IsFalse(RealMap().MayCapture(0, 42, StartOwners()));
            Assert.IsFalse(RealMap().MayCapture(-1, 0, StartOwners()));
        }

        [Test]
        public void CapitalOfFindsTheTeamsCapitalZone()
        {
            Assert.AreEqual(7, RealMap().CapitalOf(1));
            Assert.AreEqual(TerritoryMap.Neutral, RealMap().CapitalOf(5));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `unity command recompile`, poll `recompile_status`: expected error `TerritoryMap` not found.

- [ ] **Step 3: Implement** `Assets/scripts/Match/Rules/TerritoryMap.cs`:

```csharp
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// Which zone a team may start capturing. Pure C# so the rule is tested without a scene, and so
    /// the capture zones, the shop and the phase rules all ask the same question.
    ///
    /// The GDD rule (p.19): you may only capture a territory next to one you control. Two
    /// exceptions keep a match from dead-locking: you can never "capture" what you already own, and
    /// your own capital is always capturable, so a team that loses everything can still fight back.
    /// </summary>
    public sealed class TerritoryMap
    {
        public const int Neutral = -1;

        private readonly Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> capitalOwnerByZone = new Dictionary<int, int>();
        private static readonly IReadOnlyList<int> None = new List<int>();

        /// <param name="zones">Each zone id with the zones next to it. A link listed on one side only
        /// counts for both: a designer who adds 9 -> 3 should not also have to remember 3 -> 9.</param>
        /// <param name="capitals">Capital zone id and the team whose capital it is.</param>
        public TerritoryMap(IEnumerable<(int zoneId, IEnumerable<int> adjacent)> zones,
                            IEnumerable<(int zoneId, int teamId)> capitals)
        {
            foreach (var (zoneId, adjacent) in zones)
            {
                List<int> list = ListFor(zoneId);
                if (adjacent == null) continue;
                foreach (int other in adjacent)
                {
                    if (other == zoneId) continue;
                    if (!list.Contains(other)) list.Add(other);
                    List<int> back = ListFor(other);
                    if (!back.Contains(zoneId)) back.Add(zoneId);
                }
            }
            foreach (List<int> list in adjacency.Values) list.Sort();

            foreach (var (zoneId, teamId) in capitals)
                capitalOwnerByZone[zoneId] = teamId;
        }

        private List<int> ListFor(int zoneId)
        {
            if (!adjacency.TryGetValue(zoneId, out List<int> list))
                adjacency[zoneId] = list = new List<int>();
            return list;
        }

        public IReadOnlyList<int> AdjacentTo(int zoneId) =>
            adjacency.TryGetValue(zoneId, out List<int> list) ? list : None;

        public bool IsCapitalOf(int zoneId, int teamId) =>
            capitalOwnerByZone.TryGetValue(zoneId, out int owner) && owner == teamId;

        /// <summary>The team's starting capital zone, or Neutral.</summary>
        public int CapitalOf(int teamId)
        {
            foreach (KeyValuePair<int, int> pair in capitalOwnerByZone)
                if (pair.Value == teamId) return pair.Key;
            return Neutral;
        }

        /// <param name="ownerByZone">Current owner per zone; a missing zone or Neutral means nobody.</param>
        public bool MayCapture(int teamId, int zoneId, IReadOnlyDictionary<int, int> ownerByZone)
        {
            if (teamId < 0 || !adjacency.ContainsKey(zoneId))
                return false;
            if (ownerByZone.TryGetValue(zoneId, out int owner) && owner == teamId)
                return false;
            if (IsCapitalOf(zoneId, teamId))
                return true;
            foreach (int neighbour in AdjacentTo(zoneId))
                if (ownerByZone.TryGetValue(neighbour, out int neighbourOwner) && neighbourOwner == teamId)
                    return true;
            return false;
        }
    }
}
```

`Dictionary<int,int>` is passed where `IReadOnlyDictionary<int,int>` is expected — that compiles (Dictionary implements it).

- [ ] **Step 4: Run tests** — all pass (370 + 9).

- [ ] **Step 5: `TerritoryConfig`** — `Assets/scripts/Data/TerritoryConfig.cs`, matching the namespace/style of `GameplayConfig.cs` (read it first):

```csharp
using System;
using UnityEngine;

// namespace: same as GameplayConfig

[CreateAssetMenu(menuName = "Overpower/Territory Config", fileName = "TerritoryConfig")]
public sealed class TerritoryConfig : ScriptableObject
{
    [Serializable]
    public struct TierSettings
    {
        [Tooltip("Seconds for ONE player standing alone to capture a neutral zone of this tier. " +
                 "Two players take half as long, three a third.")]
        public float captureSeconds;

        [Tooltip("Gold per second this zone earns the team that owns it. Each player on that team receives " +
                 "this divided by Players Per Team.")]
        public int teamGoldPerSecond;

        [Tooltip("Gold paid to EACH player of a team that captures this zone after another team held it " +
                 "uninterrupted for Bounty Hold Seconds. 0 = no bounty on this tier.")]
        public int captureBounty;

        [Tooltip("Health per second you regain while standing in a zone of this tier that your team owns, " +
                 "once you have been out of combat long enough. 0 = no regen on this tier.")]
        public float healthRegenPerSecond;
    }

    [Tooltip("One row per tier. Element 0 = Tier 1 (Capital), 1 = Tier 2, 2 = Tier 3, 3 = Tier 4 (Centre).")]
    [SerializeField] private TierSettings[] tiers =
    {
        new TierSettings { captureSeconds = 20f, teamGoldPerSecond = 0,  captureBounty = 0,    healthRegenPerSecond = 10f },
        new TierSettings { captureSeconds = 15f, teamGoldPerSecond = 5,  captureBounty = 0,    healthRegenPerSecond = 4f },
        new TierSettings { captureSeconds = 10f, teamGoldPerSecond = 10, captureBounty = 900,  healthRegenPerSecond = 0f },
        new TierSettings { captureSeconds = 15f, teamGoldPerSecond = 8,  captureBounty = 1200, healthRegenPerSecond = 0f },
    };

    [Tooltip("How many players a team's territory income is shared between. The GDD balances income per team " +
             "and divides by 3; keep it at 3 even in a smaller test so the economy feels the same.")]
    [SerializeField, Min(1)] private int playersPerTeam = 3;

    [Tooltip("Seconds a team must hold a Tier 3 or Tier 4 zone without losing it before capturing it pays the bounty.")]
    [SerializeField, Min(0f)] private float bountyHoldSeconds = 300f;

    [Tooltip("Gold every player starts the match with. The starting kit is already free.")]
    [SerializeField, Min(0)] private int startingGold = 0;

    [Tooltip("Seconds an undefended enemy takes to drain a captured zone back to neutral.")]
    [SerializeField, Min(0.01f)] private float decaySeconds = 5f;

    [Tooltip("Seconds a zone that just went neutral cannot be captured by anyone.")]
    [SerializeField, Min(0f)] private float recaptureCooldownSeconds = 5f;

    public int PlayersPerTeam => playersPerTeam;
    public float BountyHoldSeconds => bountyHoldSeconds;
    public int StartingGold => startingGold;
    public float DecaySeconds => decaySeconds;
    public float RecaptureCooldownSeconds => recaptureCooldownSeconds;
    public int TierCount => tiers != null ? tiers.Length : 0;

    /// <summary>Settings for tier 1..4. An out-of-range tier logs once and reads as Tier 1 so a
    /// mistyped tower stays playable rather than throwing every frame.</summary>
    public TierSettings ForTier(int tier)
    {
        if (tiers == null || tiers.Length == 0)
            return default;
        int index = tier - 1;
        if (index < 0 || index >= tiers.Length)
        {
            Debug.LogError($"{name}: tier {tier} does not exist (1..{tiers.Length}) - using Tier 1.", this);
            index = 0;
        }
        return tiers[index];
    }
}
```

Create the asset via eval (`CreateInstance`, `AssetDatabase.CreateAsset(..., "Assets/Gameplay/Config/TerritoryConfig.asset")`, `SaveAssets`).

- [ ] **Step 6: Tiers on towers.** In `BuildingCapture` add (keep the file's public-field style):

```csharp
    [Header("Territory")]
    [Tooltip("1 = Capital, 2 = Transition, 3 = Flanking, 4 = Centre. Decides capture time, income, bounty and " +
             "regen from the Territory Config.")]
    [Range(1, 4)] public int tier = 2;

    [Tooltip("Shared per-tier numbers. Every tower should point at the same asset.")]
    public TerritoryConfig territoryConfig;
```

Remove the per-tower `captureThreshold`, `baseCaptureRate`, `decaySeconds`, `recaptureCooldownSeconds` fields (their values move to the config; one home). In code, capture progress becomes **seconds of one-player capture**: `captureThreshold` → `territoryConfig.ForTier(tier).captureSeconds`; `baseCaptureRate` → `1` per player per second (comment why); `decaySeconds`/`recaptureCooldownSeconds` → config. Log an error in `Start` if `territoryConfig` is null. Keep `captureRadius` per tower (geometry).
- [ ] **Step 7: Scene.** Open `Game Scene`; for every `BuildingCapture` set `tier` (6/7/8 → 1, 0/1/2 → 2, 3/4/5 → 3) and `territoryConfig`; save. `git diff` the scene: only those fields + the removed ones change on the 9 towers.
- [ ] **Step 8: Verify** (play mode, joined room): solo-capture tower 0 (a Tier 2 next to your capital) by moving the local player into its radius (teleport via `PlayerDisplacement`/rigidbody as earlier harnesses) and time neutral → captured from the master's own log timestamps: expect ~15.0s (report measured). Tower 3 without owning 0 or 1: never registers (unchanged gate). Stop play mode, tests pass.
- [ ] **Step 9: Commit + push** (pure + tests; config; capture fields + scene). Append the tier mapping and "one-player seconds" decision to assumptions.

---

### Task 2.1b: Territory state in Room Properties

**Files:**
- Create: `Assets/scripts/Match/Rules/TerritorySnapshot.cs`, `Assets/Tests/TerritorySnapshotTests.cs`
- Modify: `Assets/scripts/BuildingManager.cs`, `Assets/scripts/Player/Building capture.cs`

Today ownership replicates by `RPC_UpdateTowerDictionary` (**AllBuffered**, one buffered call per change). This task makes the master the single writer of a Room Property snapshot that every client applies, and makes `BuildingCapture.OnTriggerEnter` use `TerritoryMap`.

- [ ] **Step 1: Failing tests** (`Assets/Tests/TerritorySnapshotTests.cs`):

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TerritorySnapshotTests
    {
        [Test]
        public void ANewSnapshotIsAllNeutral()
        {
            var s = new TerritorySnapshot(10);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(i));
                Assert.AreEqual(0, s.BountyPaidOnLastCapture(i));
            }
        }

        [Test]
        public void CaptureSetsOwnerAndHoldStart()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, team: 1, nowMs: 5000, bountyPaid: 0);
            Assert.AreEqual(1, s.OwnerOf(3));
            Assert.AreEqual(5000, s.HeldSinceMs(3));
        }

        [Test]
        public void TransitionsDoNotMutateTheOriginal()
        {
            var a = new TerritorySnapshot(10);
            var b = a.WithCapture(3, 1, 5000, 0);
            Assert.AreEqual(TerritoryMap.Neutral, a.OwnerOf(3));
            Assert.AreEqual(1, b.OwnerOf(3));
        }

        [Test]
        public void NeutralisingRemembersWhoHeldItAndForHowLong()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).WithNeutral(3, nowMs: 305000);
            Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(3));
            Assert.AreEqual(1, s.LastOwnerOf(3));
            Assert.AreEqual(300000, s.LastHeldMs(3));
        }

        [Test]
        public void HoldTimeSurvivesServerTimestampWrapAround()
        {
            // PhotonNetwork.ServerTimestamp is an int that wraps; unchecked subtraction still gives the gap.
            var s = new TerritorySnapshot(10).WithCapture(3, 1, int.MaxValue - 1000, 0)
                                             .WithNeutral(3, unchecked(int.MaxValue + 2000));
            Assert.AreEqual(3000, s.LastHeldMs(3)); // (MaxValue + 2000) - (MaxValue - 1000), wrapped
        }

        [Test]
        public void PropertiesRoundTrip()
        {
            var s = new TerritorySnapshot(10).WithCapture(6, 0, 100, 0).WithCapture(3, 2, 200, 900);
            var props = new Dictionary<object, object>();
            s.WriteTo(props);
            Assert.IsTrue(TerritorySnapshot.TryRead(props, 10, out TerritorySnapshot back));
            Assert.AreEqual(0, back.OwnerOf(6));
            Assert.AreEqual(2, back.OwnerOf(3));
            Assert.AreEqual(200, back.HeldSinceMs(3));
            Assert.AreEqual(900, back.BountyPaidOnLastCapture(3));
        }

        [Test]
        public void ReadingPropertiesWithoutTerritoryFails()
        {
            Assert.IsFalse(TerritorySnapshot.TryRead(new Dictionary<object, object>(), 10, out _));
        }

        [Test]
        public void ReadingAShorterArrayPadsWithNeutral()
        {
            // A room created before tower 9 existed must still load: missing zones are neutral.
            var s = new TerritorySnapshot(9).WithCapture(8, 2, 1, 0);
            var props = new Dictionary<object, object>();
            s.WriteTo(props);
            Assert.IsTrue(TerritorySnapshot.TryRead(props, 10, out TerritorySnapshot back));
            Assert.AreEqual(2, back.OwnerOf(8));
            Assert.AreEqual(TerritoryMap.Neutral, back.OwnerOf(9));
        }

        [Test]
        public void ChangedZonesListsOnlyOwnershipChanges()
        {
            var a = new TerritorySnapshot(10).WithCapture(6, 0, 1, 0);
            var b = a.WithCapture(0, 0, 2, 0).WithNeutral(6, 3);
            CollectionAssert.AreEquivalent(new[] { 0, 6 }, b.ZonesWhoseOwnerChangedSince(a));
        }

        [Test]
        public void OwnersAsDictionaryFeedsTheMap()
        {
            var s = new TerritorySnapshot(10).WithCapture(6, 0, 1, 0);
            IReadOnlyDictionary<int, int> owners = s.OwnersByZone();
            Assert.AreEqual(0, owners[6]);
            Assert.AreEqual(TerritoryMap.Neutral, owners[0]);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Implement** `Assets/scripts/Match/Rules/TerritorySnapshot.cs`:

```csharp
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// The whole territory state of a match as plain arrays indexed by zone id - exactly what is
    /// stored in the room's Custom Properties. Immutable: every change returns a new snapshot, so the
    /// master can compare "before" and "after" and every client can tell what changed.
    ///
    /// Why Room Properties instead of the old buffered RPC: a late joiner reads one value instead of
    /// replaying every capture of the match, and hold timers are stamped with the server clock, so a
    /// new master after a disconnect reads the same timers the old one wrote.
    /// </summary>
    public sealed class TerritorySnapshot
    {
        public const string OwnersKey = "tOwn";
        public const string HeldSinceKey = "tSince";
        public const string LastOwnerKey = "tLast";
        public const string LastHeldKey = "tLastMs";
        public const string BountyKey = "tBounty";

        private readonly int[] owners;
        private readonly int[] heldSinceMs;
        private readonly int[] lastOwner;
        private readonly int[] lastHeldMs;
        private readonly int[] bountyPaid;

        public int ZoneCount => owners.Length;

        public TerritorySnapshot(int zoneCount)
        {
            owners = Filled(zoneCount, TerritoryMap.Neutral);
            heldSinceMs = new int[zoneCount];
            lastOwner = Filled(zoneCount, TerritoryMap.Neutral);
            lastHeldMs = new int[zoneCount];
            bountyPaid = new int[zoneCount];
        }

        private TerritorySnapshot(int[] owners, int[] heldSinceMs, int[] lastOwner, int[] lastHeldMs, int[] bountyPaid)
        {
            this.owners = owners;
            this.heldSinceMs = heldSinceMs;
            this.lastOwner = lastOwner;
            this.lastHeldMs = lastHeldMs;
            this.bountyPaid = bountyPaid;
        }

        public int OwnerOf(int zone) => InRange(zone) ? owners[zone] : TerritoryMap.Neutral;
        public int HeldSinceMs(int zone) => InRange(zone) ? heldSinceMs[zone] : 0;
        /// <summary>Who owned the zone before it last went neutral.</summary>
        public int LastOwnerOf(int zone) => InRange(zone) ? lastOwner[zone] : TerritoryMap.Neutral;
        /// <summary>How long that previous owner held it without losing it.</summary>
        public int LastHeldMs(int zone) => InRange(zone) ? lastHeldMs[zone] : 0;
        /// <summary>Gold each player of the current owner was paid for the capture that made them owner.</summary>
        public int BountyPaidOnLastCapture(int zone) => InRange(zone) ? bountyPaid[zone] : 0;

        public TerritorySnapshot WithCapture(int zone, int team, int nowMs, int bountyPaid)
        {
            TerritorySnapshot next = Copy();
            if (!InRange(zone)) return next;
            next.owners[zone] = team;
            next.heldSinceMs[zone] = nowMs;
            next.bountyPaid[zone] = bountyPaid;
            // The previous hold has been settled by this capture; forget it so it can't pay twice.
            next.lastOwner[zone] = TerritoryMap.Neutral;
            next.lastHeldMs[zone] = 0;
            return next;
        }

        public TerritorySnapshot WithNeutral(int zone, int nowMs)
        {
            TerritorySnapshot next = Copy();
            if (!InRange(zone) || owners[zone] == TerritoryMap.Neutral) return next;
            next.lastOwner[zone] = owners[zone];
            next.lastHeldMs[zone] = unchecked(nowMs - heldSinceMs[zone]);
            next.owners[zone] = TerritoryMap.Neutral;
            next.heldSinceMs[zone] = nowMs;
            next.bountyPaid[zone] = 0;
            return next;
        }

        public List<int> ZonesWhoseOwnerChangedSince(TerritorySnapshot previous)
        {
            var changed = new List<int>();
            for (int zone = 0; zone < owners.Length; zone++)
                if (previous == null || previous.OwnerOf(zone) != owners[zone])
                    changed.Add(zone);
            return changed;
        }

        public IReadOnlyDictionary<int, int> OwnersByZone()
        {
            var map = new Dictionary<int, int>(owners.Length);
            for (int zone = 0; zone < owners.Length; zone++) map[zone] = owners[zone];
            return map;
        }

        /// <summary>Writes into a Photon Hashtable (or any dictionary). Arrays are int[] because
        /// Photon serialises those natively.</summary>
        public void WriteTo(IDictionary<object, object> props)
        {
            props[OwnersKey] = (int[])owners.Clone();
            props[HeldSinceKey] = (int[])heldSinceMs.Clone();
            props[LastOwnerKey] = (int[])lastOwner.Clone();
            props[LastHeldKey] = (int[])lastHeldMs.Clone();
            props[BountyKey] = (int[])bountyPaid.Clone();
        }

        public static bool TryRead(IDictionary<object, object> props, int zoneCount, out TerritorySnapshot snapshot)
        {
            snapshot = null;
            if (props == null || !props.TryGetValue(OwnersKey, out object ownersRaw) || !(ownersRaw is int[] ownerArray))
                return false;
            snapshot = new TerritorySnapshot(
                Padded(ownerArray, zoneCount, TerritoryMap.Neutral),
                Padded(Read(props, HeldSinceKey), zoneCount, 0),
                Padded(Read(props, LastOwnerKey), zoneCount, TerritoryMap.Neutral),
                Padded(Read(props, LastHeldKey), zoneCount, 0),
                Padded(Read(props, BountyKey), zoneCount, 0));
            return true;
        }

        private static int[] Read(IDictionary<object, object> props, string key) =>
            props.TryGetValue(key, out object raw) ? raw as int[] : null;

        private static int[] Padded(int[] source, int length, int fill)
        {
            int[] result = Filled(length, fill);
            if (source != null)
                for (int i = 0; i < length && i < source.Length; i++) result[i] = source[i];
            return result;
        }

        private static int[] Filled(int length, int value)
        {
            var array = new int[length];
            for (int i = 0; i < length; i++) array[i] = value;
            return array;
        }

        private bool InRange(int zone) => zone >= 0 && zone < owners.Length;

        private TerritorySnapshot Copy() => new TerritorySnapshot(
            (int[])owners.Clone(), (int[])heldSinceMs.Clone(), (int[])lastOwner.Clone(),
            (int[])lastHeldMs.Clone(), (int[])bountyPaid.Clone());
    }
}
```

Note for the implementer: Photon's `ExitGames.Client.Photon.Hashtable` implements `IDictionary` (non-generic) — check. If it doesn't implement `IDictionary<object, object>`, add thin overloads in `BuildingManager` that copy between the Hashtable and a `Dictionary<object, object>`; keep this class free of Photon so it stays testable.

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: Wire `BuildingManager`** (read the whole file first):
  - Fields: `private TerritorySnapshot current;` a `TerritoryMap map` built in `Awake` from `TowerDictionary` (`Adjacents`) + `CathedralBuildingIDs`; `public int ZoneCount` = max tower id + 1; `public TerritoryMap Map => map;` `public TerritorySnapshot Current => current;`
  - `public event System.Action<int, int, int, TerritorySnapshot> OwnershipChanged;` (zone, oldOwner, newOwner, snapshot) — raised on every client when an applied update changes a zone's owner, **not** for the first snapshot a client reads on joining (a late joiner must not be paid old bounties).
  - Implements `IInRoomCallbacks` (`MonoBehaviourPunCallbacks` or `PhotonNetwork.AddCallbackTarget`): `OnRoomPropertiesUpdate` → `TryRead` → `Apply(snapshot, raiseEvents: true)`; `OnJoinedRoom`/`Start` when already in a room → if the room has a snapshot `Apply(..., raiseEvents: false)`; if not and `IsMasterClient` → write the initial snapshot: capitals owned by their teams with `HeldSinceMs = PhotonNetwork.ServerTimestamp`.
  - `Apply`: for each zone update `TowerDictionary` (`isCaptured = owner >= 0`, `controllingTeam = owner >= 0 ? owner : previous team` — keep the existing "captured flag + last team" semantics that `ApplyOwnerVisual` expects), call `capture.ApplyOwnerVisual`, call `capture.SyncFromReplicated(owner)` (below), raise events, then `CheckTerritoryWin()` (master only, unchanged).
  - Master-only writers: `public void SetCaptured(int zone, int team, int bountyPaid)` and `public void SetNeutral(int zone)` → build the next snapshot from `current` with `PhotonNetwork.ServerTimestamp`, `WriteTo` a new Photon Hashtable, `PhotonNetwork.CurrentRoom.SetCustomProperties(hashtable)`. Do **not** apply locally before the server echo — PUN raises `OnRoomPropertiesUpdate` on the setter too (verify in play mode; if it doesn't in offline/this version, apply locally once and say so).
  - `UpdateTowerDictionary` is no longer called. Keep `RPC_UpdateTowerDictionary` in the file with a comment: "kept only because RpcList dispatches by index — nothing calls it since territory moved to Room Properties (Task 2.1b)". Do the same for nothing else.
- [ ] **Step 6: Wire `BuildingCapture`:**
  - `CompleteCapture(team)` → `BuildingManager.Instance.SetCaptured(buildingID, team, bountyPaid: 0)` (Task 2.4 fills the bounty) instead of `UpdateBuildingManager`. `NeutralizeBuilding` → `SetNeutral(buildingID)`. Keep local master-side fields and sounds as today.
  - `public void SyncFromReplicated(int owner)`: sets `controllingTeam`/`isCaptured` from replicated state so a **newly promoted master** doesn't start from blank fields (captureProgress = full for captured, 0 for neutral; clear decay/cooldown).
  - `OnTriggerEnter`: replace the inline adjacency loop with `manager.Map.MayCapture(player.teamID, buildingID, manager.Current.OwnersByZone())` (one rule). Keep the `HasTeam` guard.
  - `RPC_RemoveFromZone`: a player who left a zone they were never registered in (refused by the gate) is normal — downgrade that warning to nothing (keep the `Debug.Log` for a real removal). The smoke run logged it.
- [ ] **Step 7: Verify single client** (play mode, joined room): initial room snapshot exists with capitals owned (read `CurrentRoom.CustomProperties["tOwn"]`); capture tower 0 → property shows team; `TowerDictionary[0]` and the flag material follow; neutralise via eval (master `SetNeutral(0)`) → `tLast`/`tLastMs` set. **No buffered RPCs:** `PhotonNetwork.NetworkingClient` / room event cache — at minimum confirm `RPC_UpdateTowerDictionary` is never invoked (breakpoint-free: add a temporary log in eval? no — grep proves no callers).
- [ ] **Step 8: Verify two clients** (Task 2.0 harness): Editor captures tower 0 → the Player's `TowerDictionary[0]` and flag update; then **late joiner**: close the Player, capture tower 1 on the Editor, relaunch the Player and join → it reads both 0 and 1 owned without any capture event; `OwnershipChanged` did not fire on join (count via a static counter read by eval). **Master migration:** with the Player joined second, quit the Editor's play mode is not a migration test — instead make the Player master (`PhotonNetwork.SetMasterClient` from the Editor), then capture on the Player's side and confirm the Editor sees it; state results plainly.
- [ ] **Step 9: Commit + push** (pure + tests; manager; capture).

---

### Task 2.1c: The Tier-4 centre (tower 9)

**Files:** Modify `Assets/Scenes/Game Scene.unity`.

- [ ] **Step 1: Pick a spot.** Read the world positions of towers 3, 4, 5 (Tier 3) and 6, 7, 8 (capitals). Candidate = centroid of 3/4/5. Check it's clear: `Physics.OverlapBox` with the House_05 instance's renderer bounds (expanded 1m) against `Building` and `Default` excluding the terrain/ground collider → no hits; a downward raycast finds ground; and a player-sized capsule at the zone radius edge in 8 directions has ground under it. If blocked, search outward on a spiral (1m steps, ≤ 15m) and report the chosen point and why.
- [ ] **Step 2: Create tower 9** by duplicating tower 4 (a prefab instance of guid `61e5bb1ff6b7a0b4f9edef7a870d8ea8`, so it stays a prefab instance): `buildingID = 9`, `tier = 4`, same `territoryConfig`, name `House_05 (Centre)`. Keep the same capture radius unless it overlaps a Tier-3 radius (distance between centres < r1 + r2 → shrink to fit and report).
- [ ] **Step 3: Adjacency:** `BuildingManager.TowerDictionary[9] = { Adjacents = [3,4,5] }` and add 9 to 3, 4 and 5's `Adjacents` (data stays symmetric in the scene even though `TerritoryMap` tolerates one-sided links). Save the scene; `git diff` shows only tower 9, the dictionary entries.
- [ ] **Step 4: Verify:** play mode: tower 9 registers (`BuildingManager` captures dict has 9); room snapshot has 10 zones; with only your capital + 0 owned, entering 9 doesn't register; after capturing 3 (fake via master `SetCaptured(3, team, 0)`), 9 captures in ~15s solo (measured). Capture at 616×576 showing the centre building. Stop play mode.
- [ ] **Step 5: Commit + push.** Append the placement decision to assumptions.

---

### Task 2.1d: Visible capture progress

**Files:**
- Create: `Assets/scripts/Match/Rules/CaptureProgress.cs`, `Assets/Tests/CaptureProgressTests.cs`, `Assets/scripts/Match/CaptureProgressView.cs`
- Modify: `Assets/scripts/Player/Building capture.cs`, `Assets/scripts/UI/UiTheme.cs` + asset

`captureProgress` is private and master-only, so **nothing shows a capture happening** (GDD shows capture state; original plan Step 4). Sending progress every frame would flood the room, so the master publishes a *snapshot* only when the rate changes (someone enters/leaves, decay starts, capture completes), and every client extrapolates.

- [ ] **Step 1: Failing tests:**

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class CaptureProgressTests
    {
        [Test]
        public void IdleIsZero()
        {
            Assert.AreEqual(0f, CaptureProgress.Idle.Evaluate(123456), 1e-5f);
        }

        [Test]
        public void ProgressMovesAtItsRateFromTheStamp()
        {
            var p = new CaptureProgress(team: 1, progress01: 0.2f, ratePerSecond01: 0.1f, stampMs: 1000);
            Assert.AreEqual(0.5f, p.Evaluate(4000), 1e-4f);
        }

        [Test]
        public void ProgressClampsBetweenZeroAndOne()
        {
            var up = new CaptureProgress(1, 0.9f, 0.5f, 0);
            var down = new CaptureProgress(1, 0.1f, -0.5f, 0);
            Assert.AreEqual(1f, up.Evaluate(10000), 1e-5f);
            Assert.AreEqual(0f, down.Evaluate(10000), 1e-5f);
        }

        [Test]
        public void EvaluationSurvivesTimestampWrap()
        {
            var p = new CaptureProgress(1, 0f, 0.1f, int.MaxValue - 500);
            Assert.AreEqual(0.1f, p.Evaluate(unchecked(int.MaxValue + 501)), 1e-3f);
        }

        [Test]
        public void EncodingRoundTripsThroughInts()
        {
            var p = new CaptureProgress(2, 0.3337f, -0.2f, 77);
            CaptureProgress back = CaptureProgress.Decode(p.EncodeTeam(), p.EncodeProgress(), p.EncodeRate(), p.StampMs);
            Assert.AreEqual(2, back.Team);
            Assert.AreEqual(0.3337f, back.Progress01, 1e-4f);
            Assert.AreEqual(-0.2f, back.RatePerSecond01, 1e-4f);
            Assert.AreEqual(77, back.StampMs);
        }

        [Test]
        public void SameRateAndTeamNeedsNoRepublish()
        {
            var a = new CaptureProgress(1, 0.2f, 0.1f, 0);
            var b = new CaptureProgress(1, 0.5f, 0.1f, 3000);
            Assert.IsFalse(a.NeedsRepublishComparedTo(b));
            Assert.IsTrue(a.NeedsRepublishComparedTo(new CaptureProgress(1, 0.5f, 0.2f, 3000)));
            Assert.IsTrue(a.NeedsRepublishComparedTo(new CaptureProgress(2, 0.5f, 0.1f, 3000)));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** `Assets/scripts/Match/Rules/CaptureProgress.cs`:

```csharp
using System;

namespace Overpower.Match
{
    /// <summary>
    /// A capture in progress as "where it was, how fast it moves, since when". The master publishes
    /// one only when the speed changes; every client works out the current fill from the server
    /// clock, so a capture bar moves smoothly on every screen without a network message per frame.
    /// Progress is 0..1 of a full capture; a negative rate is an enemy draining an owned zone.
    /// </summary>
    public readonly struct CaptureProgress
    {
        public const string TeamKey = "cTeam";
        public const string ProgressKey = "cProg";
        public const string RateKey = "cRate";
        public const string StampKey = "cStamp";

        // Ints on the wire: Photon handles int[] natively, and 1/10000 of a capture is finer than a pixel.
        private const float Scale = 10000f;

        public static readonly CaptureProgress Idle = new CaptureProgress(-1, 0f, 0f, 0);

        public readonly int Team;
        public readonly float Progress01;
        public readonly float RatePerSecond01;
        public readonly int StampMs;

        public CaptureProgress(int team, float progress01, float ratePerSecond01, int stampMs)
        {
            Team = team;
            Progress01 = progress01;
            RatePerSecond01 = ratePerSecond01;
            StampMs = stampMs;
        }

        public float Evaluate(int nowMs)
        {
            if (Team < 0) return 0f;
            float seconds = unchecked(nowMs - StampMs) / 1000f;
            return Math.Max(0f, Math.Min(1f, Progress01 + RatePerSecond01 * seconds));
        }

        /// <summary>True when the other snapshot moves differently (a different team or speed), so
        /// the master must publish; the same team at the same speed extrapolates identically.</summary>
        public bool NeedsRepublishComparedTo(CaptureProgress other) =>
            Team != other.Team || Math.Abs(RatePerSecond01 - other.RatePerSecond01) > 1e-4f;

        public int EncodeTeam() => Team;
        public int EncodeProgress() => (int)Math.Round(Progress01 * Scale);
        public int EncodeRate() => (int)Math.Round(RatePerSecond01 * Scale);

        public static CaptureProgress Decode(int team, int progress, int rate, int stampMs) =>
            new CaptureProgress(team, progress / Scale, rate / Scale, stampMs);
    }
}
```

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: Master publishes.** In `BuildingCapture` (master only), compute the current `CaptureProgress` each `Update` from existing state: capturing → `team = capturingID`, `progress01 = captureProgress / captureSeconds`, `rate = eligibleCount / captureSeconds` (one-player-seconds semantics); decaying → `team = the enemy team`, `progress01 = captureProgress / captureSeconds` of the owned zone, `rate = -1 / decaySeconds`; otherwise `Idle`. When `NeedsRepublishComparedTo(lastPublished)`, ask `BuildingManager` to publish: `BuildingManager.PublishCaptureProgress(zone, progress)` builds the four int[] arrays (length `ZoneCount`) from a master-side cache and sets them in one `SetCustomProperties`. Every client reads them in `OnRoomPropertiesUpdate` into `public CaptureProgress CaptureProgressOf(int zone)`.
- [ ] **Step 6: The view.** `CaptureProgressView` on each tower (added to all 10 in the scene, or created by `BuildingCapture.Start` — prefer created in code so tower 9 and any future tower get it for free): a small world-space canvas above the flag with a filled `Image` (`theme.barSprite`, horizontal) coloured `theme.ShotColorFor(team)`; hidden when `Idle` or 0; billboarded like the player overhead bar (`RhinoGame.UIBillboard` — check what the player bar uses). Theme fields: `captureBarWidth`, `captureBarHeight`, `captureBarHeightOffset`, `captureBarTrackColor`. Read time from `PhotonNetwork.ServerTimestamp`.
- [ ] **Step 7: Verify:** single client — stand in tower 0 → bar fills at 1/15 per second (sample fill at two times, report), completes and hides; publish count during a clean 15s solo capture ≤ 3 (start, complete/idle). Two clients — the Player's view of the Editor's capture matches within 0.05 at two sample times. Capture at 616×576 mid-capture. Stop play mode; tests pass.
- [ ] **Step 8: Commit + push.** (`CaptureProgressView` needs the theme: add a serialized `UiTheme theme` to `BuildingCapture`, set on all 10 towers in the scene, and pass it on.)

---

### Task 2.2: Gold

**Files:**
- Create: `Assets/scripts/Match/Rules/GoldMath.cs`, `Assets/Tests/GoldMathTests.cs`, `Assets/scripts/Match/GoldWallet.cs`
- Modify: `Assets/scripts/BuildingManager.cs` (`TierOf(zone)`), `Assets/Resources/Multiplayer Player.prefab` (add `GoldWallet`), `Assets/scripts/UI/PlayerHud.cs` + `UiTheme` (gold readout), `Assets/scripts/TestRange/TestRangePanel.cs` (+1000 gold button)

Per-player gold [G], earned only from territory [G p.19]: team income per tier (T1 0, T2 5, T3 10, T4 8 [G table]) ÷ 3 per player [G sheet]. **The player's own client accrues and publishes it** (decision table above).

- [ ] **Step 1: Failing tests** (`Assets/Tests/GoldMathTests.cs`):

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class GoldMathTests
    {
        // Zone id -> tier for the real map: 0-2 Tier 2, 3-5 Tier 3, 6-8 capitals, 9 centre.
        private static readonly int[] Tiers = { 2, 2, 2, 3, 3, 3, 1, 1, 1, 4 };
        private static readonly int[] TeamGoldByTier = { 0, 5, 10, 8 };

        private static int[] Owners(params (int zone, int team)[] held)
        {
            var owners = new int[10];
            for (int i = 0; i < owners.Length; i++) owners[i] = TerritoryMap.Neutral;
            foreach (var (zone, team) in held) owners[zone] = team;
            return owners;
        }

        [Test]
        public void TheGddStrugglingRowEarnsFifteenPerSecond()
        {
            // GDD p.37: one Tier 3 + one Tier 2 = 15/sec for the team. Capitals earn nothing.
            int[] owners = Owners((6, 0), (0, 0), (3, 0));
            Assert.AreEqual(15, GoldMath.TeamIncomePerSecond(0, owners, Tiers, TeamGoldByTier));
        }

        [Test]
        public void TheGddAverageRowEarnsTwentyThree()
        {
            int[] owners = Owners((6, 0), (0, 0), (3, 0), (9, 0));
            Assert.AreEqual(23, GoldMath.TeamIncomePerSecond(0, owners, Tiers, TeamGoldByTier));
        }

        [Test]
        public void OtherTeamsZonesEarnYouNothing()
        {
            int[] owners = Owners((3, 1), (4, 2));
            Assert.AreEqual(0, GoldMath.TeamIncomePerSecond(0, owners, Tiers, TeamGoldByTier));
        }

        [Test]
        public void AZoneWithNoTierEarnsNothing()
        {
            int[] owners = Owners((0, 0));
            int[] noTiers = new int[10];
            Assert.AreEqual(0, GoldMath.TeamIncomePerSecond(0, owners, noTiers, TeamGoldByTier));
        }

        [Test]
        public void EachPlayerGetsTheTeamIncomeDividedByPlayersPerTeam()
        {
            Assert.AreEqual(5.0, GoldMath.PlayerIncomePerSecond(15, 3), 1e-9);
            Assert.AreEqual(15.0, GoldMath.PlayerIncomePerSecond(15, 0), 1e-9); // 0 treated as 1
        }

        [Test]
        public void RefundIsHalfRoundedDown()
        {
            Assert.AreEqual(600, GoldMath.Refund(1201, 0.5));
            Assert.AreEqual(800, GoldMath.Refund(1600, 0.5));
            Assert.AreEqual(0, GoldMath.Refund(0, 0.5));
            Assert.AreEqual(0, GoldMath.Refund(1000, -1));
        }

        [Test]
        public void WholeGoldAccruesAndFractionsCarry()
        {
            var wallet = new GoldAccrual(0);
            wallet.Accrue(23.0 / 3.0, 1.0);
            Assert.AreEqual(7, wallet.Balance);
            wallet.Accrue(23.0 / 3.0, 2.0);
            Assert.AreEqual(23, wallet.Balance);
        }

        [Test]
        public void ManySmallFramesAddUpToTheSameGold()
        {
            var wallet = new GoldAccrual(0);
            for (int i = 0; i < 60; i++) wallet.Accrue(5.0, 1.0 / 60.0);
            Assert.AreEqual(5, wallet.Balance);
        }

        [Test]
        public void SpendingMoreThanYouHoldIsRefusedAndChangesNothing()
        {
            var wallet = new GoldAccrual(100);
            Assert.IsFalse(wallet.TrySpend(101));
            Assert.AreEqual(100, wallet.Balance);
            Assert.IsTrue(wallet.TrySpend(100));
            Assert.AreEqual(0, wallet.Balance);
        }

        [Test]
        public void NegativeAmountsAreIgnored()
        {
            var wallet = new GoldAccrual(50);
            Assert.IsFalse(wallet.TrySpend(-10));
            wallet.Add(-10);
            wallet.Accrue(-5, 10);
            Assert.AreEqual(50, wallet.Balance);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** `Assets/scripts/Match/Rules/GoldMath.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>Territory income maths from the GDD balancing sheet (p.36-38): a team earns the sum of
    /// its zones' tier income, and each player receives that divided by the team size.</summary>
    public static class GoldMath
    {
        /// <param name="ownerByZone">Owner per zone id (Neutral = nobody).</param>
        /// <param name="tierByZone">Tier 1..4 per zone id; 0 = not a zone.</param>
        /// <param name="teamGoldByTier">Element 0 = Tier 1's team gold per second.</param>
        public static int TeamIncomePerSecond(int teamId, IReadOnlyList<int> ownerByZone,
                                              IReadOnlyList<int> tierByZone, IReadOnlyList<int> teamGoldByTier)
        {
            if (teamId < 0 || ownerByZone == null || tierByZone == null || teamGoldByTier == null)
                return 0;
            int total = 0;
            int zones = Math.Min(ownerByZone.Count, tierByZone.Count);
            for (int zone = 0; zone < zones; zone++)
            {
                if (ownerByZone[zone] != teamId) continue;
                int index = tierByZone[zone] - 1;
                if (index >= 0 && index < teamGoldByTier.Count)
                    total += teamGoldByTier[index];
            }
            return total;
        }

        public static double PlayerIncomePerSecond(int teamIncomePerSecond, int playersPerTeam) =>
            teamIncomePerSecond / (double)Math.Max(1, playersPerTeam);

        /// <summary>What selling back gives you: the rate of what you paid, rounded down.</summary>
        public static int Refund(int goldSpent, double refundRate)
        {
            if (goldSpent <= 0 || refundRate <= 0) return 0;
            return (int)Math.Floor(goldSpent * Math.Min(1.0, refundRate));
        }
    }

    /// <summary>
    /// A gold balance that earns a fractional rate every frame but only ever shows whole gold. The
    /// fraction is carried rather than dropped, so 7.67 gold/sec really pays 23 gold every 3 seconds.
    /// </summary>
    public sealed class GoldAccrual
    {
        // Floating-point sums of many tiny frames land a hair under a whole number (0.99999...).
        private const double WholeTolerance = 1e-6;

        private double carry;

        public int Balance { get; private set; }

        public GoldAccrual(int startingBalance)
        {
            Balance = Math.Max(0, startingBalance);
        }

        public void Accrue(double goldPerSecond, double seconds)
        {
            if (goldPerSecond <= 0 || seconds <= 0) return;
            carry += goldPerSecond * seconds;
            int whole = (int)Math.Floor(carry + WholeTolerance);
            if (whole <= 0) return;
            Balance += whole;
            carry = Math.Max(0, carry - whole);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > Balance) return false;
            Balance -= amount;
            return true;
        }

        public void Add(int amount)
        {
            if (amount > 0) Balance += amount;
        }
    }
}
```

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: `BuildingManager.TierOf(int zone)`** → the registered `BuildingCapture.tier`, 0 when no tower has that id. Plus `public int[] TierByZone()` (length `ZoneCount`) for the wallet.
- [ ] **Step 6: `GoldWallet : MonoBehaviourPun`** on the player prefab root (prefab-edit rule):
  - `public const string GoldKey = "gold";` Serialized: `TerritoryConfig territoryConfig` (tooltip).
  - `public int Balance` — owner: the accrual's balance; remote copy: the owner's `gold` Player Property (0 if absent).
  - `public double IncomePerSecond` (owner) for the HUD.
  - `public event System.Action<int> BalanceChanged;` raised on every client when the balance it reports changes.
  - Owner `Awake/Start`: `accrual = new GoldAccrual(territoryConfig.StartingGold)`; publish once.
  - Owner `Update`: if in a room, team known (`Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out team)`) and `BuildingManager.Instance?.Current != null`: build `teamGoldByTier` from the config rows, compute team then player income, `accrual.Accrue(income, Time.unscaledDeltaTime)` (unscaled: a debug timescale change must not change the economy). Dead players keep earning — territory belongs to the team [C].
  - Publishing: `PhotonNetwork.LocalPlayer.SetCustomProperties({ gold })` when the balance differs from the last published value and ≥ 1s since the last publish, **or immediately** after `TrySpend`/`Add`. Comment why (property traffic vs responsiveness).
  - `public bool TrySpend(int amount)` / `public void Add(int amount)` — owner only (log a warning and refuse on a remote copy).
  - Remote: `OnPlayerPropertiesUpdate` for this view's owner → `BalanceChanged`.
- [ ] **Step 7: HUD readout.** `PlayerHud` shows "Gold 1234  +7.7/s" above the slots row (theme: `goldTextColor`, reuse `bodyTextSize`; income formatted with `CultureInfo.InvariantCulture`). `TestRangePanel` gains a "+1000 gold" button next to "Fill ultimate" (calls `GoldWallet.Add(1000)`), so shop testing never waits for income.
- [ ] **Step 8: Verify:** single client — own capital + tower 0 (Tier 2): team 5/s → player 1.667/s; with decay frozen out of the way, read the balance at t0 and t0+30s (game timestamps) → +50 ±1 (report). "+1000 gold" → +1000 and the `gold` property updates immediately. Two clients — the Player reads the Editor player's `gold` property equal to the Editor's balance within 1s; the Editor's HUD and the Player's HUD each show their own gold (captures at 616×576). Stop play mode; tests pass.
- [ ] **Step 9: Commit + push.** Tell the controller Tudor must reload the player prefab.

---

### Task 2.3: Health regen by tier

**Files:**
- Create: `Assets/scripts/Match/Rules/HealthRegenRule.cs`, `Assets/Tests/HealthRegenRuleTests.cs`
- Modify: `Assets/scripts/BuildingManager.cs` (zone queries), `Assets/scripts/Player/PlayerHealth.cs`, player prefab (config reference)

GDD p.15: health regenerates only in Tier 1/2 territory, fastest in Tier 1; no rate given → T1 10/s, T2 4/s, only in a zone your team owns and once out of combat (the same clock armor uses) [C].

- [ ] **Step 1: Failing tests:**

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class HealthRegenRuleTests
    {
        [Test]
        public void RegensAtTheTierRateInYourOwnZoneOutOfCombat()
        {
            Assert.AreEqual(10f, HealthRegenRule.RegenPerSecond(true, 10f, 7f, 6f), 1e-5f);
        }

        [Test]
        public void NothingInsideCombat()
        {
            Assert.AreEqual(0f, HealthRegenRule.RegenPerSecond(true, 10f, 5.9f, 6f), 1e-5f);
        }

        [Test]
        public void NothingOutsideYourOwnZone()
        {
            Assert.AreEqual(0f, HealthRegenRule.RegenPerSecond(false, 10f, 60f, 6f), 1e-5f);
        }

        [Test]
        public void NothingOnATierWithoutRegen()
        {
            Assert.AreEqual(0f, HealthRegenRule.RegenPerSecond(true, 0f, 60f, 6f), 1e-5f);
        }

        [Test]
        public void ExactlyAtTheThresholdCounts()
        {
            Assert.AreEqual(4f, HealthRegenRule.RegenPerSecond(true, 4f, 6f, 6f), 1e-5f);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement:**

```csharp
namespace Overpower.Match
{
    /// <summary>GDD p.15: health only comes back in safe territory, fastest in the capital. Gated on the
    /// same out-of-combat clock as armor, so standing in your zone mid-fight is not free healing.</summary>
    public static class HealthRegenRule
    {
        public static float RegenPerSecond(bool standingInOwnZone, float tierRegenPerSecond,
                                           float secondsSinceCombat, float outOfCombatSeconds)
        {
            if (!standingInOwnZone || tierRegenPerSecond <= 0f) return 0f;
            return secondsSinceCombat >= outOfCombatSeconds ? tierRegenPerSecond : 0f;
        }
    }
}
```

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: Zone queries on `BuildingManager`** (one helper for regen, shop and OverPower):
  - `public bool TryGetZoneAt(Vector3 position, out int zoneId)` — registered captures whose flat (XZ) distance to `position` ≤ their `captureRadius`; the nearest wins.
  - `public float DistanceToOwnedZoneEdge(Vector3 position, int teamId)` — min over zones owned by `teamId` of `max(0, flatDistance − captureRadius)`; `float.PositiveInfinity` when the team owns nothing.
- [ ] **Step 6: `PlayerHealth`:** serialized `TerritoryConfig territoryConfig` (tooltip), assigned on the prefab. In the owner `Update` (after the existing armor tick, same `!IsMine || isDead` gate): when health < max, find the zone, `ownsZone = Current.OwnerOf(zone) == team`, `rate = HealthRegenRule.RegenPerSecond(ownsZone, config.ForTier(TierOf(zone)).healthRegenPerSecond, SecondsSinceCombat, gameplayConfig.<outOfCombatSeconds>)`, `health = Min(max, health + rate*dt)`, then the existing overhead-bar update. Confirm health reaches remote clients through the existing sync (read `PlayerNetSync`).
- [ ] **Step 7: Verify** (armor + health): take 60 damage (armor 25 then health −35), teleport into your capital, wait out of combat, sample health at two timestamps 3s apart → +30 ±1; the same in Tier 2 you own → +12 ±1; in a neutral or enemy zone → 0; damage again → stops for the out-of-combat window. Two clients: the Player sees the Editor player's health rising. Stop play mode; tests pass.
- [ ] **Step 8: Commit + push.**

---

### Task 2.4: Capture bounty

**Files:**
- Create: `Assets/scripts/Match/Rules/BountyRule.cs`, `Assets/Tests/BountyRuleTests.cs`
- Modify: `Assets/scripts/Player/Building capture.cs` (pay on capture), `Assets/scripts/Match/GoldWallet.cs` (receive)

GDD p.20: a team holding a Tier 3/4 zone more than 5 minutes uninterrupted puts a bounty on it; the enemy team that captures it gets the bounty instantly (T3 900, T4 1200 [G]). The hold that counts ends when the zone goes neutral (the only way this code changes owners); `TerritorySnapshot` already records it (`LastOwnerOf`, `LastHeldMs`). Each capturing player gets the full amount [C].

- [ ] **Step 1: Failing tests:**

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class BountyRuleTests
    {
        private const int Hold = 300000;

        [Test]
        public void FourFiftyNinePaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(newOwner: 1, lastOwner: 0, lastHeldMs: 299000, bounty: 900, holdMs: Hold));
        }

        [Test]
        public void FiveOhOnePaysTheBounty()
        {
            Assert.AreEqual(900, BountyRule.PayoutOnCapture(1, 0, 301000, 900, Hold));
        }

        [Test]
        public void ExactlyFiveMinutesPays()
        {
            Assert.AreEqual(1200, BountyRule.PayoutOnCapture(2, 0, Hold, 1200, Hold));
        }

        [Test]
        public void RetakingYourOwnZonePaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(0, 0, 900000, 900, Hold));
        }

        [Test]
        public void AZoneNobodyHeldPaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(1, TerritoryMap.Neutral, 900000, 900, Hold));
        }

        [Test]
        public void ATierWithoutABountyPaysNothing()
        {
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(1, 0, 900000, 0, Hold));
        }

        [Test]
        public void TheSnapshotSettlesTheHoldSoItCannotPayTwice()
        {
            var s = new TerritorySnapshot(10).WithCapture(3, 0, 0, 0).WithNeutral(3, 400000);
            int pay = BountyRule.PayoutOnCapture(1, s.LastOwnerOf(3), s.LastHeldMs(3), 900, Hold);
            var captured = s.WithCapture(3, 1, 410000, pay);
            Assert.AreEqual(900, captured.BountyPaidOnLastCapture(3));
            var again = captured.WithNeutral(3, 420000);
            Assert.AreEqual(0, BountyRule.PayoutOnCapture(2, again.LastOwnerOf(3), again.LastHeldMs(3), 900, Hold));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement:**

```csharp
namespace Overpower.Match
{
    /// <summary>GDD p.20 "bounce-back": a zone one team held uninterrupted for the hold time pays its
    /// bounty to the enemy team that takes it. Losing a zone and retaking it yourself pays nothing,
    /// and a contested zone that never went neutral never ended its hold.</summary>
    public static class BountyRule
    {
        public static int PayoutOnCapture(int newOwner, int lastOwner, int lastHeldMs, int bounty, int holdMs)
        {
            if (newOwner < 0 || lastOwner < 0 || newOwner == lastOwner || bounty <= 0)
                return 0;
            return lastHeldMs >= holdMs ? bounty : 0;
        }
    }
}
```

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: Pay on capture.** `BuildingCapture.CompleteCapture`: `bounty = BountyRule.PayoutOnCapture(team, current.LastOwnerOf(id), current.LastHeldMs(id), config.ForTier(tier).captureBounty, (int)(config.BountyHoldSeconds * 1000))` → `SetCaptured(id, team, bounty)`. Confirm a decay that is interrupted (defender returns) never calls `SetNeutral` (read `HandleCapturedState`) — that is what makes "contested-but-not-captured doesn't reset it" true.
- [ ] **Step 6: Receive.** `GoldWallet` (owner) subscribes to `BuildingManager.OwnershipChanged`; when `newOwner == my team` and `snapshot.BountyPaidOnLastCapture(zone) > 0` → `Add(bounty)` and a HUD toast "Bounty +900" (theme duration/colour; reuse the HUD label helper). The event doesn't fire for a joiner's first read, so nobody is paid twice.
- [ ] **Step 7: Verify:** craft snapshots through eval with the public API (`TerritorySnapshot` + `WriteTo` + `CurrentRoom.SetCustomProperties` as master): zone 3 held by team X since `ServerTimestamp − 301000`, neutralise, capture it with the local player's team Y → the local wallet +900 exactly once; the same with 299000 → +0; X retaking → +0. Two clients (different teams): only the capturing team's client gains gold. Stop play mode; tests pass.
- [ ] **Step 8: Commit + push.**

---

### Task 2.5: Shop on the P screen

**Files:**
- Create: `Assets/scripts/Match/Rules/ShopRules.cs`, `Assets/Tests/ShopRulesTests.cs`
- Modify: `Assets/scripts/UI/LoadoutScreen.cs`, `Assets/scripts/Data/GameplayConfig.cs` + asset (`freeLoadout`, `sellRefundRate`), `Assets/scripts/Player/PlayerLoadout.cs` (empty ultimate at start when the economy is on), 13 weapon + 14 ability assets (`goldCost`), player prefab (references)

The loadout screen was built as the future shop (polish Task 9). GDD rules: buy only in a territory your team owns and after 5s out of combat (`GameplayConfig.shopOutOfCombatSeconds` already exists) [G]; undo for 50% [G].

- [ ] **Step 1: Failing tests:**

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class ShopRulesTests
    {
        [Test]
        public void AllowedInYourZoneOutOfCombatWithEnoughGold()
        {
            Assert.AreEqual(PurchaseBlock.None, ShopRules.Check(true, 5f, 5f, 1200, 1200));
        }

        [Test]
        public void TerritoryIsCheckedFirst()
        {
            Assert.AreEqual(PurchaseBlock.NotInOwnTerritory, ShopRules.Check(false, 0f, 5f, 0, 1200));
        }

        [Test]
        public void RefusedInCombat()
        {
            Assert.AreEqual(PurchaseBlock.InCombat, ShopRules.Check(true, 4.9f, 5f, 5000, 1200));
        }

        [Test]
        public void RefusedWithoutEnoughGold()
        {
            Assert.AreEqual(PurchaseBlock.CannotAfford, ShopRules.Check(true, 10f, 5f, 1199, 1200));
        }

        [Test]
        public void AFreeItemNeedsNoGold()
        {
            Assert.AreEqual(PurchaseBlock.None, ShopRules.Check(true, 10f, 5f, 0, 0));
        }

        [Test]
        public void SecondsUntilAllowedCountsDown()
        {
            Assert.AreEqual(2.5f, ShopRules.SecondsUntilOutOfCombat(2.5f, 5f), 1e-5f);
            Assert.AreEqual(0f, ShopRules.SecondsUntilOutOfCombat(9f, 5f), 1e-5f);
        }

        [Test]
        public void SellingAWeaponPathRefundsHalfOfEverythingPaidOnIt()
        {
            var ledger = new PurchaseLedger();
            ledger.RecordWeapon(1200);
            ledger.RecordWeapon(1600);
            Assert.AreEqual(1400, ledger.SellWeapon(0.5));
            Assert.AreEqual(0, ledger.SellWeapon(0.5)); // nothing left to sell
        }

        [Test]
        public void ArmorAndWeaponRefundsAreSeparate()
        {
            var ledger = new PurchaseLedger();
            ledger.RecordWeapon(1200);
            ledger.RecordArmor(1400);
            Assert.AreEqual(700, ledger.SellArmor(0.5));
            Assert.AreEqual(1200, ledger.WeaponSpent);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement:**

```csharp
using System;

namespace Overpower.Match
{
    public enum PurchaseBlock
    {
        None,
        /// <summary>GDD p.18-19: shopping happens in territory your team holds.</summary>
        NotInOwnTerritory,
        /// <summary>GDD: out of combat for the shop's required seconds.</summary>
        InCombat,
        CannotAfford,
    }

    /// <summary>The shop's purchase rules, apart from any UI, so the screen and any future shop ask the
    /// same questions in the same order (the first failing reason is the one a player needs to fix).</summary>
    public static class ShopRules
    {
        public static PurchaseBlock Check(bool inOwnTerritory, float secondsSinceCombat,
                                          float requiredOutOfCombatSeconds, int balance, int price)
        {
            if (!inOwnTerritory) return PurchaseBlock.NotInOwnTerritory;
            if (secondsSinceCombat < requiredOutOfCombatSeconds) return PurchaseBlock.InCombat;
            if (price > 0 && balance < price) return PurchaseBlock.CannotAfford;
            return PurchaseBlock.None;
        }

        public static float SecondsUntilOutOfCombat(float secondsSinceCombat, float requiredOutOfCombatSeconds) =>
            Math.Max(0f, requiredOutOfCombatSeconds - secondsSinceCombat);
    }

    /// <summary>What a player has paid per category, so "undo" can refund part of it. Local to the
    /// owner: only the resulting gold is replicated.</summary>
    public sealed class PurchaseLedger
    {
        public int WeaponSpent { get; private set; }
        public int ArmorSpent { get; private set; }

        public void RecordWeapon(int price) { if (price > 0) WeaponSpent += price; }
        public void RecordArmor(int price) { if (price > 0) ArmorSpent += price; }

        public int SellWeapon(double refundRate)
        {
            int refund = GoldMath.Refund(WeaponSpent, refundRate);
            WeaponSpent = 0;
            return refund;
        }

        public int SellArmor(double refundRate)
        {
            int refund = GoldMath.Refund(ArmorSpent, refundRate);
            ArmorSpent = 0;
            return refund;
        }
    }
}
```

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: Data.** `GameplayConfig`: `freeLoadout` (bool, tooltip: "On = the P screen changes anything for free, anywhere — for testing weapons. Off = the full match economy: prices, territory and out-of-combat rules, ultimate bought."; **asset value true** so Tudor's pending free weapon test is unchanged) and `sellRefundRate` (0.5 [G]). Prices via eval on assets (`goldCost`): weapon 1 → 0; 2, 5, 8, 11 → 1200 [G]; 3, 4, 6, 7, 9, 10, 12, 13 → 1600 [G]; abilities 14–24 → 800 [C]; 25–27 → 1550 [G]; 901–903 → 0. Armor prices already live on `ArmorConfig.UpgradeCosts` (1400, 1800 [G]; a third entry 2200 exists but `maxArmorUpgrades` 2 never reaches it — leave it). `git diff` the assets.
- [ ] **Step 6: Starting kit.** When `freeLoadout` is off, `PlayerLoadout` publishes `LoadoutProperties.Empty` for the ultimate at spawn instead of the prefab's starting ultimate (read how it chooses starting ids; late joiners read the property). The HUD's ultimate slot must render an empty slot without errors (check `PlayerHud` handles `StatusFor == null`).
- [ ] **Step 7: The screen.** In `LoadoutScreen` (read the whole file; follow its helpers):
  - References: `GoldWallet`, `GameplayConfig`, `ArmorConfig` (existing), `PlayerHealth` (existing via player), `BuildingManager.Instance`.
  - Header: "Gold 1234" and a status line from the current block: "Go to a zone your team owns", "Out of combat in 2.4s", or "" — refreshed every frame while open (cheap; strings only change when the value's tenth-of-a-second changes).
  - Every weapon node and ability card shows its price ("1200", or "Owned"/"Equipped"); unaffordable or blocked items get `theme.lockedColor` text but stay hoverable.
  - Weapon click: `CanUpgrade` (existing) → `price = target.GoldCost` → if `freeLoadout` skip gold/gate; else `ShopRules.Check(inOwn, secondsSinceCombat, shopOutOfCombatSeconds, wallet.Balance, price)` → `wallet.TrySpend(price)` → `ledger.RecordWeapon(price)` → `SetWeapon`.
  - "Reset weapon" → label shows the refund "(+600)"; gate except affordability → `wallet.Add(ledger.SellWeapon(rate))` → `SetWeapon(root)`.
  - Armor +: `price = armorConfig.UpgradeCosts[absorbLevel + rechargeLevel]` (guard the index) → same flow with `RecordArmor`; "Reset armor (+700)" refunds via `SellArmor`.
  - Ability card: changing to a different ability costs its `GoldCost`; the already-equipped card costs nothing; an empty ultimate slot shows "Buy an ultimate".
  - `inOwn = BuildingManager.TryGetZoneAt(player position) && owner == team`.
  - `freeLoadout` on: prices hidden behind a single "Free (test mode)" header note; everything behaves as today.
- [ ] **Step 8: Verify** with `freeLoadout` temporarily **false** (restore it and prove with `git diff`):
  1. Spawn: ultimate slot empty; gold 0.
  2. Standing in a neutral zone: clicking Rocket refused, status "Go to a zone your team owns", gold unchanged, weapon still 1.
  3. In your capital out of combat with 0 gold: refused (CannotAfford). F1 "+1000" twice → buy Rocket → gold 800, weapon 2, ledger 1200.
  4. Reset weapon → gold 1400, weapon 1.
  5. +Absorb at 1400 → gold 0, absorb level 1; Reset armor → +700.
  6. Take damage → any purchase refused for 5s with a counting status; allowed after.
  7. Buy Zone (1550) into the empty ultimate slot.
  8. With `freeLoadout` true: everything free anywhere (today's behaviour).
  9. Two clients: the Player sees the Editor player's new weapon id and gold property after a purchase.
  Captures at 616×576 of the screen with prices and a blocked status. Stop play mode; tests pass.
- [ ] **Step 9: Commit in stages + push.** Append price and "each ultimate change costs full price" decisions to assumptions.

---

### Task 2.6: OverPower comeback buff (cuttable — second from the bottom)

**Files:**
- Create: `Assets/scripts/Match/Rules/OverPowerState.cs`, `Assets/Tests/OverPowerStateTests.cs`, `Assets/scripts/Match/OverPowerBuff.cs`
- Modify: `Assets/scripts/Weapons/WeaponFiring.cs` (stat multipliers), `Assets/scripts/Player/PlayerOverheat.cs` (suppression), `Assets/scripts/Data/GameplayConfig.cs` + asset, `PlayerHud` + `UiTheme` (indicator), player prefab

GDD p.20: attacked by **both** enemy teams within 3s near a territory you control → whenever you drop under 35 HP: shield refills instantly, +10% on three of your primary's highest parameters, overheat nullified; moving more than 15m from that territory ends it, triggered or not. Parameters = damage, fire rate, range [C].

- [ ] **Step 1: Failing tests:**

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class OverPowerStateTests
    {
        private static OverPowerState New() => new OverPowerState(windowSeconds: 3f, maxDistance: 15f, healthThreshold: 35f);

        [Test]
        public void TwoEnemyTeamsWithinTheWindowArmIt()
        {
            var s = New();
            s.RecordEnemyHit(attackerTeam: 1, time: 10f, distanceToOwnedZone: 2f);
            s.RecordEnemyHit(2, 12.5f, 2f);
            Assert.IsTrue(s.Armed);
        }

        [Test]
        public void OneTeamTwiceDoesNot()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(1, 11f, 2f);
            Assert.IsFalse(s.Armed);
        }

        [Test]
        public void HitsFurtherApartThanTheWindowDoNot()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(2, 13.1f, 2f);
            Assert.IsFalse(s.Armed);
        }

        [Test]
        public void HitsTakenFarFromYourTerritoryDoNotCount()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 16f);
            s.RecordEnemyHit(2, 11f, 2f);
            Assert.IsFalse(s.Armed);
        }

        [Test]
        public void DroppingBelowTheThresholdWhileArmedFiresOnce()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(2, 11f, 2f);
            Assert.IsFalse(s.CheckTrigger(40f));
            Assert.IsTrue(s.CheckTrigger(34f));
            Assert.IsTrue(s.Active);
            Assert.IsFalse(s.CheckTrigger(20f));
        }

        [Test]
        public void MovingTooFarEndsItArmedOrActive()
        {
            var s = New();
            s.RecordEnemyHit(1, 10f, 2f);
            s.RecordEnemyHit(2, 11f, 2f);
            s.CheckTrigger(30f);
            s.UpdateDistance(15.5f);
            Assert.IsFalse(s.Armed);
            Assert.IsFalse(s.Active);
        }

        [Test]
        public void NotArmedNeverTriggers()
        {
            Assert.IsFalse(New().CheckTrigger(1f));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement:**

```csharp
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// The OverPower comeback rule (GDD p.20) as a small state machine: being hit by both enemy teams
    /// within a short window near your own territory arms it; dropping low while armed fires it once;
    /// leaving that territory's area ends it either way. Rewards an outnumbered defender and punishes
    /// third-partying without touching anyone who isn't defending.
    /// </summary>
    public sealed class OverPowerState
    {
        private readonly float windowSeconds;
        private readonly float maxDistance;
        private readonly float healthThreshold;
        private readonly Dictionary<int, float> lastHitTimeByTeam = new Dictionary<int, float>();

        public bool Armed { get; private set; }
        public bool Active { get; private set; }

        public OverPowerState(float windowSeconds, float maxDistance, float healthThreshold)
        {
            this.windowSeconds = windowSeconds;
            this.maxDistance = maxDistance;
            this.healthThreshold = healthThreshold;
        }

        public void RecordEnemyHit(int attackerTeam, float time, float distanceToOwnedZone)
        {
            if (attackerTeam < 0 || distanceToOwnedZone > maxDistance) return;
            lastHitTimeByTeam[attackerTeam] = time;

            int teamsInWindow = 0;
            foreach (float hitTime in lastHitTimeByTeam.Values)
                if (time - hitTime <= windowSeconds) teamsInWindow++;
            if (teamsInWindow >= 2) Armed = true;
        }

        public void UpdateDistance(float distanceToOwnedZone)
        {
            if (distanceToOwnedZone <= maxDistance) return;
            Armed = false;
            Active = false;
            lastHitTimeByTeam.Clear();
        }

        /// <returns>True exactly once: the moment the buff should apply.</returns>
        public bool CheckTrigger(float health)
        {
            if (!Armed || Active || health >= healthThreshold) return false;
            Active = true;
            return true;
        }
    }
}
```

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: Weapon stat layer.** `WeaponFiring`: `public void SetStatMultipliers(float damage, float fireRate, float range)` (1 = unchanged; owner state). `TryFire`: `nextFireTime = Time.time + weapon.FireInterval / fireRate` — **keep `ChargeFraction()` read before `nextFireTime` is written**. Damage and range must be identical on every client, so `RPC_FireWeapon` gains `float damageMultiplier, float rangeMultiplier` parameters (appending parameters does not change the RpcList, which lists names; every client runs the same build anyway) → `BuildShots` multiplies damage; `ProjectileContext`'s max range and `Hitscan.ChargedRange`'s result multiply range (add a range multiplier into `ProjectileContext` rather than a second formula). `AimConeView` draws the multiplied range (expose `WeaponFiring.CurrentRangeMultiplier`). `RefundHeatIfBeamConnects` passes the same multipliers.
- [ ] **Step 6: Overheat suppression.** `PlayerOverheat.SetSuppressed(object key, bool)` (keyed like speed multipliers); `Add` does nothing while any key suppresses.
- [ ] **Step 7: `OverPowerBuff`** (player root, owner only): config on `GameplayConfig` (`overPowerWindowSeconds` 3, `overPowerRadius` 15, `overPowerHealthThreshold` 35, `overPowerStatBonus` 0.10 [G]). Subscribe `PlayerHealth.Damaged` → attacker team from `DamageInfo.SourceActorNumber` via `Teams.TryGetTeam(CurrentRoom.GetPlayer(actor))`, skip teammates/self/unknown → `RegisterHitFrom(team)`, a small private method that calls `RecordEnemyHit(team, Time.time, BuildingManager.DistanceToOwnedZoneEdge(pos, myTeam))` (kept separate from the actor lookup so the rule can be exercised without a real player on every team); each frame `UpdateDistance`; on `CheckTrigger(health)` → armor refill (`ArmorState.RefillToFull` path through `PlayerHealth`), `SetStatMultipliers(1.1, 1.1, 1.1)`, overheat suppressed; on becoming inactive → reset all three. HUD label "OVERPOWER" while active and a fainter "armed" hint (theme colours).
- [ ] **Step 8: Verify** (armor + health). A single client — and even two clients — has at most one real enemy team, so arm the buff by invoking the private `RegisterHitFrom(team)` through reflection with two different enemy team ids (say so in the report), then use real `ApplyDamage` for the health drop. With the two-client harness also confirm one real hit from the Player's avatar reaches `RegisterHitFrom` with the right team.
  - armed after hits from two teams 1s apart inside your capital; not after one team twice;
  - drop to 30 health → armor 25/25 at once, measured interval between shots = `FireInterval / 1.1` (±0.02s), damage on a dummy = 1.1× (armor+health), overheat stays 0 after 5 shots;
  - walk 16m from the zone edge → multipliers back to 1, overheat accrues again.
  Stop play mode; tests pass.
- [ ] **Step 9: Commit + push.** If this task runs long, cut Steps 5–8 before touching phase work, and record the cut.

---

### Task 2.7: Elimination and phase transitions (cuttable — bottom of the list)

**Files:**
- Create: `Assets/scripts/Match/Rules/MatchPhaseRules.cs`, `Assets/Tests/MatchPhaseRulesTests.cs`, `Assets/scripts/Match/MatchDirector.cs`
- Modify: `Assets/scripts/Player/PlayerLifecycle.cs`, `Assets/scripts/Player/MatchUI.cs`, `Assets/scripts/BuildingManager.cs`, `Assets/Scenes/Game Scene.unity` (a `MatchDirector` object)

GDD p.20-21: with three teams, a team is eliminated when its capital is captured **and** all its members are dead afterwards (the last stand). On the first elimination: Tier 3 zones go neutral and survivors return to their capitals (**map geometry reduction cut**). With two teams, a team is eliminated the moment it loses its capital. A team in its last stand that captures an enemy capital adopts it (cuttable step). Last team standing wins.

Today: `PlayerDied` makes a death permanent when your capital is lost; `RPC_HandleDeathMaster` counts dead players in **static** tallies that a new master doesn't have and a second match inherits. This task recomputes elimination from replicated state instead.

- [ ] **Step 1: Failing tests:**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class MatchPhaseRulesTests
    {
        private static TeamStatus Team(int id, int members, int alive, bool holdsCapital) =>
            new TeamStatus { TeamId = id, Members = members, AliveMembers = alive, HoldsItsCapital = holdsCapital };

        [Test]
        public void ThreeTeamsIsPhaseOne()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 3, true), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void InPhaseOneALostCapitalWithSurvivorsIsALastStand()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 1, false), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void InPhaseOneALostCapitalAndNobodyAliveEliminatesAndMovesToPhaseTwo()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, false), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void EveryoneDeadButStillHoldingTheCapitalIsNotEliminated()
        {
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 3, 0, true), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void InPhaseTwoLosingTheCapitalEliminatesAtOnce()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 2 }, new[] { Team(0, 3, 3, false), Team(1, 3, 3, true), Team(2, 3, 0, false) });
            CollectionAssert.AreEquivalent(new[] { 2, 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void EliminationIsPermanent()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 0 }, new[] { Team(0, 3, 3, true), Team(1, 3, 3, true), Team(2, 3, 3, true) });
            CollectionAssert.Contains(r.Eliminated, 0);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TeamsWithNoPlayersAreNotInTheMatch()
        {
            // A two-player test starts straight in the two-team phase.
            var r = MatchPhaseRules.Recompute(new List<int>(), new[] { Team(0, 1, 1, true), Team(1, 1, 1, true), Team(2, 0, 0, true) });
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TheLastTeamLeftWins()
        {
            var r = MatchPhaseRules.Recompute(new List<int> { 0, 2 }, new[] { Team(0, 3, 0, false), Team(1, 3, 2, true), Team(2, 3, 0, false) });
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void ALastStandTeamThatTakesAnEnemyCapitalAdoptsIt()
        {
            Assert.AreEqual(7, MatchPhaseRules.CapitalAfterCapture(currentCapital: 6, holdsCurrentCapital: false, capturedZone: 7, capturedZoneIsACapital: true));
            Assert.AreEqual(6, MatchPhaseRules.CapitalAfterCapture(6, true, 7, true));
            Assert.AreEqual(6, MatchPhaseRules.CapitalAfterCapture(6, false, 3, false));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement:**

```csharp
using System.Collections.Generic;

namespace Overpower.Match
{
    public enum MatchPhase
    {
        /// <summary>Three teams alive: losing your capital starts a last stand.</summary>
        ThreeTeams = 1,
        /// <summary>Two teams alive: losing your capital eliminates you at once (GDD p.21).</summary>
        TwoTeams = 2,
        Over = 3,
    }

    public struct TeamStatus
    {
        public int TeamId;
        public int Members;
        public int AliveMembers;
        public bool HoldsItsCapital;
    }

    public sealed class MatchPhaseResult
    {
        public MatchPhase Phase;
        public List<int> Eliminated = new List<int>();
        /// <summary>The winning team when Phase is Over, else -1.</summary>
        public int Winner = -1;
    }

    /// <summary>
    /// Who is out and which phase the match is in, recomputed from facts every client already has
    /// (capital owners, alive flags, team sizes) instead of a tally one machine keeps. That is what
    /// lets a new master after a disconnect - or a second match - reach the same answer.
    /// </summary>
    public static class MatchPhaseRules
    {
        public static MatchPhaseResult Recompute(IReadOnlyCollection<int> alreadyEliminated, IReadOnlyList<TeamStatus> teams)
        {
            var result = new MatchPhaseResult();
            result.Eliminated.AddRange(alreadyEliminated);

            // Repeat until nothing changes: the first elimination moves the match to two teams, and
            // the two-team rule can then immediately apply to a team that is already without a capital.
            bool changed = true;
            while (changed)
            {
                changed = false;
                MatchPhase phase = PhaseFor(teams, result.Eliminated);
                foreach (TeamStatus team in teams)
                {
                    if (team.Members <= 0 || result.Eliminated.Contains(team.TeamId) || team.HoldsItsCapital)
                        continue;
                    bool eliminatedNow = phase == MatchPhase.TwoTeams || team.AliveMembers <= 0;
                    if (eliminatedNow)
                    {
                        result.Eliminated.Add(team.TeamId);
                        changed = true;
                    }
                }
            }

            result.Phase = PhaseFor(teams, result.Eliminated);
            if (result.Phase == MatchPhase.Over)
                foreach (TeamStatus team in teams)
                    if (team.Members > 0 && !result.Eliminated.Contains(team.TeamId))
                        result.Winner = team.TeamId;
            return result;
        }

        private static MatchPhase PhaseFor(IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            int remaining = 0;
            foreach (TeamStatus team in teams)
                if (team.Members > 0 && !eliminated.Contains(team.TeamId)) remaining++;
            return remaining >= 3 ? MatchPhase.ThreeTeams : remaining == 2 ? MatchPhase.TwoTeams : MatchPhase.Over;
        }

        /// <summary>GDD p.20: a team in its last stand that captures an enemy capital makes it its own.</summary>
        public static int CapitalAfterCapture(int currentCapital, bool holdsCurrentCapital, int capturedZone, bool capturedZoneIsACapital) =>
            !holdsCurrentCapital && capturedZoneIsACapital ? capturedZone : currentCapital;
    }
}
```

Trace the tests by hand before running: `InPhaseTwoLosingTheCapitalEliminatesAtOnce` has team 2 already eliminated → two remain → TwoTeams → team 0 (no capital) out → one remains → Over, winner 1. `EliminationIsPermanent` keeps 0 out even holding a capital. Note one GDD-edge consequence to append to assumptions: with the fixpoint, a team that is in a last stand at the moment another team is eliminated is eliminated in the same instant (the two-team rule applies immediately).

- [ ] **Step 4: Tests pass.**
- [ ] **Step 5: `MatchDirector`** (a scene object `Match Director`; master computes, everyone applies):
  - Room Properties: `mPhase` (int), `mElim` (int[]), `mCap` (int[3] capital zone per team, starting 6/7/8), `mWin` (int, −1).
  - Master recomputes on `BuildingManager.OwnershipChanged`, `OnPlayerPropertiesUpdate` (alive flag `PlayerLifecycle.AliveKey`, team key), `OnPlayerLeftRoom`, `OnMasterClientSwitched`: build `TeamStatus` per team from `PhotonNetwork.PlayerList` (team + alive properties) and `BuildingManager.Current.OwnerOf(mCap[team]) == team`; call `Recompute(mElim, statuses)`; write properties only when something changed. Capital adoption (cuttable): on `OwnershipChanged` to team T of a zone that is another team's `mCap`, `mCap[T] = CapitalAfterCapture(...)`.
  - All clients, on change: newly eliminated team → its members' own clients show the lost panel (`MatchUI.ShowYouLost`, local call — no RPC needed now that the state replicates); `mWin >= 0` → everyone's `MatchUI.ShowMatchResult(winner)`; phase changed ThreeTeams → TwoTeams → master sets every Tier-3 zone neutral once (`BuildingManager.SetNeutral`), every living player's own client returns to its capital (`PlayerLifecycle.ReturnToSpawn`), and a HUD banner "Two teams left — losing your capital now eliminates you" (theme). 
  - `BuildingManager.CheckTerritoryWin` (hold all capitals) writes `mWin` through the director instead of its own RPC (keep `RPC_TerritoryWin` for RpcList; call the director).
- [ ] **Step 6: `PlayerLifecycle`:** `PlayerDied`'s permanent-death test reads `MatchDirector.CapitalOf(team)` (from `mCap`) instead of `CathedralBuildingIDs`, and shows the waiting panel locally; delete the three static tallies and the bookkeeping in `RPC_HandleDeathMaster` (keep the method, now logging and asking the director to recompute; comment why it stays). `CheckForCathedralCapture` uses the adopted capital too. Respawn at an adopted capital: `teamSpawnPoints[team]` is replaced by the capital zone's position + a ring offset only when adopted (cuttable).
- [ ] **Step 7: Verify.** Single client with crafted replicated state (write `alive` Player Properties for fake actors is impossible — use two or three clients; if only two are available, say so): 
  - **Two clients** (Editor team A, Player team B; the match starts in TwoTeams): Editor's team captures B's capital (fake adjacency via master snapshot write if needed) → B eliminated immediately, lost panel on the Player, won panel on the Editor, `mPhase` = Over. Player quits and rejoins as a late joiner → reads Over/winner.
  - **Three clients** if two Player instances can be driven (Task 2.0 harness with `--instance`): team C loses capital + all members dead → eliminated, phase TwoTeams, Tier 3 zones neutral, survivors teleported home. If only two clients: state plainly that the three-team transition is verified only by the pure tests and eval-crafted state.
  - Master migration: make the Player master mid-match; the next elimination is still decided (no static tallies).
  Stop play mode; tests pass.
- [ ] **Step 8: Commit in stages + push.** Append the "two-player test starts in the two-team phase" and fixpoint decisions to assumptions.

---

### Task 2.8: Phase 2 wrap-up

- [ ] **Step 1:** Controller check: `editor_status`, `recompile_status`, all edit-mode tests, `git status` clean, 0 unpushed, RpcList diff since `8b25a24` (expect none).
- [ ] **Step 2:** Smoke run (single client): all 13 weapons + 14 abilities + P screen in both `freeLoadout` modes + a full solo capture chain capital → 0 → 3 → 9 with gold/regen/bounty observed; 0 errors, 0 exceptions, no leaks.
- [ ] **Step 3:** Two-client run of the polish checklist (progress.md "Two-client checklist from the final review") and Phase 2's two-client items, recording pass/fail per item with evidence.
- [ ] **Step 4:** Final whole-phase review of `8b25a24..HEAD` (opus, read-only), fix pass, re-verify.
- [ ] **Step 5:** `progress.md` Phase 2 section, `HANDOFF.md` §3/§4, `assumptions-for-tudor.md` complete; push notification to Tudor.

---

## Final task: `findings.md` (the deliverable)

**File:** `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\findings.md` — one brief document (Tudor's instruction), from the 2026-09-12 plan:

1. **Numbers provenance table** — every value, where it lives (asset/field), and [T]/[G]/[C]. Not summarisable; `progress.md` records each number's source. Generate the value list by reading the assets (weapons, abilities, configs, theme) so nothing is missed.
2. **Feasibility verdict per ability and per match-loop system** — built / built with a caveat / not built and why.
3. **Answers to Tudor's five judging questions** (brief §"What I will judge this on"), including what it did that he would not have approved of, and which three files he should pick to test whether he can read code he didn't write.
4. **Every "build more" vs "keep it clean" conflict** and which was chosen.
5. **What is safe to playtest and what is not** (two-client coverage, three-client gaps).

---

## Self-review against the sources

| Requirement (source) | Task |
|---|---|
| Tiers, capture times, adjacency, centre (plan 2.1, GDD p.19) | 2.1a, 2.1c |
| Capture progress visible (plan 2.1 Step 4) | 2.1d (capture sound already positional — survey) |
| Mid-match joiner gets all zones (plan 2.1 Step 6) | 2.1b |
| Master migration safety (addenda) | 2.1b, 2.7 |
| Gold per player from tiers, T4 = 8, split /3 (plan 2.2, GDD p.36-38) | 2.2 |
| Health regen by tier (Tudor 2026-09-13: build it) | 2.3 |
| Bounty 5 min, 900/1200, reset only on ownership change (plan 2.5) | 2.4 |
| Shop gates, prices, 50% refund, starting kit, ultimate bought (plan 2.3, GDD p.17-19) | 2.5 |
| OverPower trigger/effect/expiry (plan 2.4, GDD p.20) | 2.6 |
| Phase rules, last stand, Tier 3 neutral + return home; map reduction cut; static tally fix (plan 2.6, addenda) | 2.7 |
| Second consecutive match (brief judging) | Static tallies removed (2.7); a rematch flow is not built — `MatchUI.MatchOver` latch documented; listed in assumptions |
| Two-client verification | 2.0 + each task |
| Findings deliverable | Final task |

