# Capture Ring, Minimap and GDD Territory Links Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Each T2 zone links directly to T4, as in the GDD.
- A ring on the ground replaces the capture bar over each tower. It shows capturing, draining, paused and under attack.
- A corner minimap, and a large map on M, look like GDD p.27: bubbles, links, progress rings and player markers over a baked top-down image of the arena.

**Architecture:**
- **Pure rules, tested in edit mode:**
  - `CaptureProgress.Held`
  - `CaptureRingState` and `CaptureRingGeometry`
  - `MinimapLayout` and `MinimapLinkStyle`
  - the `TerritoryMap` fixture
- **Replication:** the master publishes held progress as rate 0 instead of `Idle`. No new keys and no RPCs.
- **Views, built in code and drawn from replicated state on every client:**
  - `CaptureRingView`: two flat `LineRenderer`s per tower.
  - `MinimapView`: an owner-only screen-space canvas on the player prefab.
- **Editor:** `MinimapBaker` renders the arena top-down to a PNG and records the world square on a `MinimapConfig` asset. Rebuild thirds calls it too.

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2 (Room Properties), NUnit edit-mode tests, `unity` CLI, two-client harness.

**Spec:** `docs/superpowers/specs/2026-09-16-capture-ring-minimap-design.md` (approved; read it first).

**Naming rule:** "T1–T4" means zone tiers only. This plan's tasks are "Task 1..7", called **ring/minimap step N** in
reports, commits and `progress.md`.

---

## Controller amendments (2026-09-17; these override the task text below)

1. **[C] Start after telemetry step 7 is approved.** Telemetry step 7's review fixes are still landing in
   `TelemetryAggregator.cs`, `HtmlReportWriter.cs`, `TelemetryMenu.cs` and `MatchTelemetry.cs`. Don't begin step 2
   until the controller says they have landed.
2. **[C] A link between two different teams' zones is a `Border`, not a grey line.**
   - This replaces the "thin grey line" decision below.
   - Why: a border between X and Y is a way in for both teams, and grey would hide the front line.
   - **Drawing:** split the line at its midpoint. Each half takes its own end's owner colour at the owned-line width,
     with no arrowhead.
   - **Code:**
     - `MinimapLinkKind` gains `Border`.
     - `MinimapLinkStyle` gains `TeamB`. For `Border`, `Team` = owner A and `TeamB` = owner B. For other kinds,
       `TeamB` = `Team`.
     - `For(0, 1)` returns `Border` (0, 1). The test `...Neutral...For(0, 1)` becomes a `Border` test that asserts
       both teams, plus `For(1, 0)` → `Border` (1, 0).
     - In `MinimapView`, every link is two half-line Images. `Owned` and `Neutral` colour both halves the same.
3. **[C] Step 2 also fixes the drain-start label (it's in the telemetry).**
   - **The bug:** a drain's `Progress01` is the owner's remaining hold, starting at 1.0
     (`Building capture.cs:275`). So `CaptureTransitionClassifier.Classify` labels every fresh drain `drainResumed`
     (`CaptureTransitionClassifier.cs:57-58`), and the aggregator never sees `drainStarted`.
   - **The fix:**
     - a capture resumes when `Progress01 > ResumeThreshold01` (unchanged);
     - a drain resumes when `Progress01 < 1 - ResumeThreshold01`.
   - Update the classifier doc comment to match.
   - **Tests** (red first):
     - a drain starting at 1.0 → `DrainStarted`;
     - a drain moving again from a `Held` 0.6 → `DrainResumed`;
     - a drain at 0.995 → `DrainStarted`.
   - **Check the aggregator.** In `TelemetryAggregator`'s captures table, a `drainStarted` now opens a fresh attempt
     (`CaptureFreshStartStates`). Run the telemetry suites and confirm the existing fixture expectations still hold;
     if any relied on the old label, fix the fixture, not the rule.
   - **Update Task 7's expected telemetry:** the drain opens with `drainStarted`, and the "older than this plan"
     note is removed.
   - The file stops being "comment only": stage it in step 2's commit.
4. **[C] Keep the camera's Team Yaw Offset at 120.**
   - Your capital sits lower-left, as the plan's captures expect.
   - The minimap follows the camera. The spec's "capital at the bottom" was a mistaken description of the current
     camera, not a request.
   - Changing the play camera is Tudor's call; the controller reports it to him.
5. **Readings confirmed:**
   - M works while dead and while the P screen is open (M closes P).
   - The large map doesn't take tool focus.
   - "Capturers left" shows Idle, because the capture rule resets.

## Decisions the plan makes (the spec left these open)

- **[C] Ground ring = two `LineRenderer`s per zone, not a procedural mesh.**
  - The pieces: an edge loop plus a progress arc, laid flat with `LineAlignment.TransformZ`.
  - They draw with `Assets/Gameplay/UI/AimConeLine.mat` through a new `UiTheme.captureRingMaterial` slot. That material is URP Particles/Unlit: transparent, `_Cull: 0` (double-sided), vertex colour and no shadow pass.
  - Why:
    - `AimConeView.CreateLine` (`Assets/scripts/Player/AimConeView.cs:125-151`) already draws unlit overlays this way.
    - The colour and opacity of each ring (team, pulse, blink) are just start/end colours. No material instances, property blocks or mesh colour uploads.
    - There is no triangle code to get wrong.
  - Arc points are rewritten only when the arc's rounded point count or the camera yaw changes.
- **[C] Ring height:** measured once at build. Take the highest terrain height under the zone centre and 8 points on its edge, then add `captureRingHeightOffset`. Fall back to the tower's y where no terrain covers the point.
- **[C] Arc start and direction:** it starts at the side facing the local camera's "up" (`CameraTracking.Yaw`) and fills clockwise on screen. The minimap progress ring uses `Origin360.Top` + clockwise inside a group turned back upright, so both match.
- **[C] Edge pulse priority:** while draining, the edge pulses to the drainer's colour. Otherwise, while under attack, it pulses to the warning colour. A paused drain has no drainer pulse, only the band blink (the zone is still under attack, so the warning pulse shows). The drain pulse uses the same `captureRingPulseSpeed` as under attack.
- **[C] Held progress:**
  - `CaptureProgress.Held(team, progress01, stampMs)` is rate 0. It is `Idle` when `team < 0` or nothing is banked.
  - The master publishes it for:
    - a contested neutral capture;
    - a link-blocked neutral capture;
    - a paused drain.
  - The two replicated updates (owner and progress) can arrive a moment apart. During that gap, `CaptureRingState` reads two cases as Idle: a capture by the zone's own owner, and a drain on a zone that is already neutral.
- **[C] Where the map image and its world square are stored:**
  - The image is `Assets/Gameplay/UI/ArenaMinimap.png` (Default texture, clamp, mipmaps).
  - `Assets/Gameplay/Config/MinimapConfig.asset` (class `Overpower.Data.MinimapConfig`) holds `arenaImage`, `worldCentre` (x, z) and `worldSizeMetres`, all written by the bake, plus the bake settings `imagePixels` 1024 and `marginMetres` 4.
  - The square is centred on `ArenaSymmetry.centre`, not on the bounding-box centre.
  - Its side is 2 × (farthest `MeshRenderer` bounds corner under Source and both generated thirds + margin). The arena then stays inside the inscribed circle at every yaw, and the three teams see the same map, only rotated.
- **[C] Minimap shape:**
  - The minimap is round: a `Mask` over a generated disc. A rotated square image never shows its corners.
  - One map hierarchy serves both views. M re-anchors it to the screen centre and scales it by `minimapLargeSize / minimapCornerSize`, so the corner is empty while the large map is open.
- **[C] Sprites are generated at runtime** (`GeneratedSprites.Disc`, `.Triangle`; no sprite assets).
  - The minimap progress ring is a `Filled`/`Radial360` disc behind the bubble's outline disc. Its visible band is exactly `minimapProgressRingWidth` canvas units at every bubble size.
  - The ring has a sprite, so its fill works (spec).
- **[C] Where the minimap lives:** `MinimapView` is a component on `Assets/Resources/Multiplayer Player.prefab` and acts for the owner only, like `PlayerHud`.
  - Canvas sortingOrder −9: above the HUD (−10), below the loadout screen (−5) and MatchUI (0).
  - No `GraphicRaycaster`; every Graphic has `raycastTarget = false`.
- **[C] Teammate dots:**
  - The list comes from iterating `PhotonNetwork.CurrentRoom.Players`, a `Dictionary<int, Player>` (`Assets/Photon/PhotonRealtime/Code/Room.cs:191`). Its struct enumerator doesn't allocate, unlike `PhotonNetwork.PlayerList`.
  - Team comes from `Teams.TryGetTeam`, and "alive" from the `alive` Custom Property (`PlayerLifecycle.AliveKey`).
  - Position is `PlayerLookup.GetPhotonViewFor(actor).transform.position`, which `PlayerNetSync` replicates.
  - Dead teammates are hidden, and so is your own marker while you are dead.
- **[C] M works while the P screen is open.**
  - Today `MapToggled` is gated by `InputSuppressed`, which includes the loadout screen's tool focus (`Assets/scripts/Player/PlayerInputRouter.cs:92,168`). "M closes P" could never happen.
  - It gets its own gate, like `ShopToggled`: blocked only when dead or typing.
  - The large map does not claim tool focus, so the player keeps playing.
- **~~[C] Link style when the two ends have different owners:~~ superseded by controller amendment 2 (`Border`).** Originally: a thin grey line. This reads "one end owned by team X and the other not" literally, as neutral.
- **[C] Where Rebuild thirds bakes:** in `ArenaSymmetryInspector`, after both the button and the menu item's rebuild. It does not bake inside `ArenaSymmetryBuilder.Rebuild`, because `ArenaSymmetryBuilderTests` call that in preview scenes and must not write the PNG.
- **[C] Split change from the controller's suggestion:**
  - The `CameraTracking` yaw exposure moves from step 4 into **ring/minimap step 3**, because the ground ring needs it first.
  - `BuildingManager.TryGetZoneCentre` is added in step 6, where the minimap needs it.
- **[C] Render helper:** a new `Overpower.EditorTools.TopDownRender`. The telemetry `ArenaReportRender` stays untouched, because another agent is working in the telemetry files.
- **[C] Hook for 2.7:** `MinimapView.Local.SetZoneShown(zone, false)` hides a zone's bubble and every link to it. It is remembered if called before the map is built. 2.7 itself is not built here.

## Spec statements the real code doesn't match (read before building)

1. **"Capturers left" never pauses.**
   - For a neutral capture: `BuildingCapture.EndCaptureIfCapturersLeft` resets progress to 0 (`Assets/scripts/Player/Building capture.cs:775-784`, `DrainRule.LeavingEndsCapture`, `Assets/scripts/Match/Rules/DrainRule.cs:100-101`).
   - For a drain: `DrainRule.Decide` returns `Stop`, and the next drain starts from full (`DrainRule.cs:9-10,93`).
   - The ring therefore shows **Idle** in both cases. This plan does not change the capture rule (the spec requires unchanged timings).
2. **"Your capital at the bottom" doesn't hold with the scene's camera offset.**
   - The camera yaw is `atan2(toSpawn) + teamYawOffset` (`Assets/scripts/CameraTracking.cs:161`), and Game Scene sets `teamYawOffset: 120` (`Assets/Scenes/Game Scene.unity:4819`).
   - So each team sees its own capital **120° counter-clockwise from screen-up (lower-left)**.
   - The minimap follows the camera, as the spec requires, so capture 7 expects lower-left for both teams. Putting it at the bottom means `teamYawOffset` 180, which is a controller/Tudor decision.
3. **"A paused capture (enemy inside)" needs an enemy who may capture that zone.**
   - `playersInZone` only lists players `TerritoryMap.MayCapture` let in (`Building capture.cs:679`).
   - Task 7 therefore gives the enemy an adjacent zone first.
4. **Team 0's colour is near-white** (`Assets/scripts/UI/UiTheme.cs:187`). A team-0 edge and the neutral "dim white" edge differ mainly in opacity. Capture 1 must show that they read differently.
5. **`capture_game_view` defaults to `--source camera`, which misses Screen Space Overlay UI.** Every capture that should show the minimap uses `--source screen` (Play Mode only), or an in-process `ScreenCapture.CaptureScreenshot`.

---

## Rules for every task (each has cost hours on this project)

1. Branch `limit-testing` only; push after each task's commit. `unity command editor_status` must answer before editing.
2. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `unity command editor_stop`, poll
   `unity command editor_status` until `playMode: "stopped"` and `compiling: false`.
3. `unity command recompile`, then poll `unity command recompile_status` until completed with `errors: []` (the only
   compile truth). **Tests async only:** `unity command run_tests -- --mode editor --async_tests true`, then poll
   `unity command test_status`.
4. **Dirty scene → modal dialog → silent Editor hang.** Before tests, recompile, build or scene open run
   `unity command eval -- --code "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"`, read the
   answer, and continue only if it is `False` (don't chain it with `&&`). To discard:
   `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", UnityEditor.SceneManagement.OpenSceneMode.Single)`.
   If `editor_status` times out: stop and report. Don't click dialogs.
5. **CLI:**
   - form: `unity command <name> -- --flag value`
   - files: `unity command eval_file -- --file "<path>" --timeout 20000`
   - long scripts: `unity command --timeout 240 run_script -- --file "<path>" --entry Type.Method --timeout_ms 200000`
   - eval code writes `UnityEngine.Object`, never bare `Object`
   - Player: `unity command --runtime-path "Builds/Client2" <name> -- ...`; its eval needs reflection (`two-client-harness.md` §6)
6. **SCRATCH** = `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad`.
   Scratch scripts go in `SCRATCH\ring-minimap\`, captures in `SCRATCH\ring-minimap\captures\`. Never under `Assets/`.
7. **Captures:**
   - Size 616×576, from the real Game view.
   - Always saved to an explicit path. `capture_game_view` with `--save_path "Temp/ring-minimap/<name>.png"` goes under the project `Temp/`; copy the file to SCRATCH. If a path outside `Assets/` is refused, use `Assets/Temp/ring-minimap/`, then delete that folder and its `.meta`.
   - **Look at every capture yourself** (Read the PNG) before describing it.
8. **`UiTheme.asset` is edited by hand** (HANDOFF trap 8c). Order:
   1. Add or remove the C# fields.
   2. Recompile.
   3. Hand-edit the YAML in field-declaration order.
   4. `AssetDatabase.ImportAsset(path, ForceUpdate)`.
   5. Read the values back by eval.
   6. `git diff` the asset: only the intended lines.
   - Never `SetDirty` + `SaveAssets` on the theme.
9. `AssetDatabase.SaveAssets()` flushes every dirty asset. Use `AssetDatabase.SaveAssetIfDirty(asset)`, then `git status`.
   `Assets/Gameplay/Config/GameplayConfig.asset` and
   `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` (RpcList) must stay unchanged. **Add no
   RPC, rename none, remove none.**
10. Prefabs: `PrefabUtility.LoadPrefabContents` → edit → `SaveAsPrefabAsset` → `UnloadPrefabContents`, then read back
    (`add_component` on a prefab path doesn't persist).
11. Never move a player with `transform.position`. Use `PlayerDisplacement.TeleportTo` to a point outside colliders, and
    wait at least a frame before relying on the new position. Aim with `PlayerAim.SetAimOverride`.
12. Measure with game-time stamps, not CLI wall time. The CLI round trip is several seconds, so anything timed runs in
    an in-process coroutine that writes its result to a file.
13. Every designer-facing value lives on an asset with a plain `[Tooltip]`, in one home. Comments explain *why*, for a
    designer reader.
14. Commit messages end with your own `Co-Authored-By:` line. No unmeasured number in a commit message. Stage **only**
    the task's files (listed per task); check `git status` first. Other agents may have uncommitted telemetry edits in
    the tree: never stage those.
15. Append judgement calls to `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under
    `## Capture ring + minimap (2026-09-17)` as short [C] lines (outside the repo; don't commit it).

## File map

| File | Responsibility | Step |
|---|---|---|
| `Assets/Scenes/Game Scene.unity` (modify) | `BuildingManager.TowerDictionary` key 9 gains adjacents 0, 1, 2 | 1 |
| `Assets/Tests/TerritoryMapTests.cs` (modify) | GDD link fixture; T4-from-T2 cases | 1 |
| `Assets/Tests/TerritoryAdjacencySceneTests.cs` (create) | Guards the saved scene's links | 1 |
| `Assets/scripts/Match/Rules/CaptureProgress.cs` (modify) + `Assets/Tests/CaptureProgressTests.cs` | `Held`, `IsHeld` | 2 |
| `Assets/scripts/Player/Building capture.cs` (modify) | Publish held progress at rate 0 (step 2); build and refresh the ring (step 3) | 2, 3 |
| `Assets/scripts/Match/Rules/CaptureRingState.cs` + `Assets/Tests/CaptureRingStateTests.cs` (create) | Ring state from progress + owner + under attack | 2 |
| `Assets/scripts/Match/Rules/CaptureRingGeometry.cs` + `Assets/Tests/CaptureRingGeometryTests.cs` (create) | Ring points, arc point count, pulse/blink | 2 |
| `Assets/scripts/Telemetry/CaptureTransitionClassifier.cs` (comment only) + `Assets/Tests/CaptureTransitionClassifierTests.cs` | Held transitions pinned | 2 |
| `Assets/scripts/Match/CaptureRingView.cs` (create) | The ground ring | 3 |
| `Assets/scripts/Match/CaptureProgressView.cs` (+ `.meta`) (delete) | Old tower bar | 3 |
| `Assets/scripts/UI/UiTheme.cs` + `Assets/Gameplay/Config/UiTheme.asset` (modify) | − Capture bar, + Capture ring (step 3); + Minimap (step 6) | 3, 6 |
| `Assets/scripts/CameraTracking.cs` (modify) | `Instance`, `Yaw`, `YawResolved` | 3 |
| `Assets/scripts/UI/MinimapLayout.cs` + `Assets/Tests/MinimapLayoutTests.cs` (create) | World → map, rotation, segments, link pairs, tier labels | 4 |
| `Assets/scripts/UI/MinimapLinkStyle.cs` + `Assets/Tests/MinimapLinkStyleTests.cs` (create) | Same owner / way in / neutral | 4 |
| `Assets/scripts/Data/MinimapConfig.cs` (create), `Assets/Gameplay/Config/MinimapConfig.asset` (bake creates) | Baked image + world square | 5 |
| `Assets/scripts/Editor/Arena/TopDownRender.cs`, `Assets/scripts/Editor/Arena/MinimapBaker.cs` (create) | Bake minimap image | 5 |
| `Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs` (modify) | Rebuild thirds also bakes | 5 |
| `Assets/Tests/MinimapBakerTests.cs` (create) | Arena radius | 5 |
| `Assets/Gameplay/UI/ArenaMinimap.png` (bake creates) | The image | 5 |
| `Assets/scripts/UI/GeneratedSprites.cs`, `Assets/scripts/UI/MinimapView.cs` (create) | The minimap | 6 |
| `Assets/scripts/BuildingManager.cs` (modify) | `TryGetZoneCentre` | 6 |
| `Assets/scripts/Player/PlayerInputRouter.cs` (modify) | `MapToggled` own gate | 6 |
| `Assets/Resources/Multiplayer Player.prefab` (modify) | `MinimapView` component | 6 |

---

### Task 1 (ring/minimap step 1): GDD T2↔T4 links

**What exists:**
- `BuildingManager.TowerDictionary` (`Assets/scripts/BuildingManager.cs:25`) is serialized in Game Scene at
  `Assets/Scenes/Game Scene.unity:11752-11833`.
- Key 9's `Adjacents: 030000000400000005000000` (line 11831) is 3, 4, 5.
- `BuildMap` (`BuildingManager.cs:311-331`) turns it into a `TerritoryMap`. A link listed on one side counts for both
  (`Assets/scripts/Match/Rules/TerritoryMap.cs:24-43`).
- `TerritoryMapTests.TheCentreNeedsAFlankingZone` (`Assets/Tests/TerritoryMapTests.cs:57-65`) asserts that T4 isn't
  capturable from a T2 alone. **It changes on purpose.**

**Files:**
- Modify: `Assets/Tests/TerritoryMapTests.cs`
- Create: `Assets/Tests/TerritoryAdjacencySceneTests.cs`
- Modify: `Assets/Scenes/Game Scene.unity` (one line)
- Scratch (not committed): `SCRATCH\ring-minimap\AddGddLinks.cs`, `SCRATCH\ring-minimap\T4FromT2.cs`

- [ ] **Step 0: Record BASE.** Run `git rev-parse HEAD` and put the hash in your report as `BASE`. Task 7 diffs the
  UiTheme, GameplayConfig and RpcList assets against it.

- [ ] **Step 1: Update `TerritoryMapTests`.** Replace the `RealMap()` fixture (lines 9-18) with:

```csharp
        // The scene's real adjacency, including the GDD p.27 links (Tudor, 2026-09-16): the centre (9) links to every
        // Tier 2 zone as well as every Tier 3. TerritoryAdjacencySceneTests checks the saved scene matches this copy.
        private static TerritoryMap RealMap() => new TerritoryMap(
            new List<(int, IEnumerable<int>)>
            {
                (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                (9, new[] { 3, 4, 5, 0, 1, 2 }),
            },
            new List<(int, int)> { (6, 0), (7, 1), (8, 2) });
```

Delete the whole `TheCentreNeedsAFlankingZone` test (lines 57-65) and add these inside the class:

```csharp
        [Test]
        public void TheCentreIsCapturableFromATier2Alone()
        {
            var owners = StartOwners();
            owners[0] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners));
        }

        [Test]
        public void TheCentreIsStillCapturableFromAFlankingZone()
        {
            var owners = StartOwners();
            owners[3] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners));
        }

        [Test]
        public void TheCentreStillNeedsAZoneOfYoursNextToIt()
        {
            // Team 0 owns only its capital 6, which isn't next to the centre.
            Assert.IsFalse(RealMap().MayCapture(0, 9, StartOwners()));
        }

        [Test]
        public void ACapitalStillLinksOnlyToItsOwnTier2()
        {
            TerritoryMap map = RealMap();
            CollectionAssert.AreEqual(new[] { 0 }, map.AdjacentTo(6));
            CollectionAssert.AreEqual(new[] { 1 }, map.AdjacentTo(7));
            CollectionAssert.AreEqual(new[] { 2 }, map.AdjacentTo(8));
        }

        [Test]
        public void ATier2UnderAttackIsNotAWayIntoTheCentre()
        {
            // The capital-under-attack link block (2026-09-16) applies to the new T2 -> T4 link too.
            var owners = StartOwners();
            owners[0] = 0;
            Assert.IsFalse(RealMap().MayCapture(0, 9, owners, zone => zone == 0));
            owners[3] = 0;
            Assert.IsTrue(RealMap().MayCapture(0, 9, owners, zone => zone == 0), "a second, safe link still works");
        }
```

- [ ] **Step 2: Create the scene guard** `Assets/Tests/TerritoryAdjacencySceneTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Match;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Guards Game Scene's own territory links (GDD p.27, Tudor 2026-09-16). TerritoryMapTests test the capture rule on
    /// a copy of these links; this test fails if the scene's BuildingManager.TowerDictionary drifts from that copy.
    /// Read-only: the scene is used as it is loaded, or opened additively and closed again without saving.
    /// </summary>
    public class TerritoryAdjacencySceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void TheSceneLinksTheCentreToEveryTier2AndTier3()
        {
            WithGameScene(scene =>
            {
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, map.AdjacentTo(9));
            });
        }

        [Test]
        public void TheScenesCapitalsStillLinkOnlyToTheirOwnTier2()
        {
            WithGameScene(scene =>
            {
                TerritoryMap map = MapOf(Find<BuildingManager>(scene).Single());
                CollectionAssert.AreEqual(new[] { 0 }, map.AdjacentTo(6));
                CollectionAssert.AreEqual(new[] { 1 }, map.AdjacentTo(7));
                CollectionAssert.AreEqual(new[] { 2 }, map.AdjacentTo(8));
            });
        }

        // The same construction BuildingManager.BuildMap does at runtime.
        private static TerritoryMap MapOf(BuildingManager manager) =>
            new TerritoryMap(
                manager.TowerDictionary.Select(pair => (pair.Key, (IEnumerable<int>)pair.Value.Adjacents)).ToList(),
                manager.CathedralBuildingIDs.Select(pair => (pair.Key, pair.Value)).ToList());

        // Same helper as ArenaSymmetrySceneTests: never opens the scene Single, which would prompt to save a dirty scene.
        private static void WithGameScene(Action<Scene> body)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try { body(scene); }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        private static IEnumerable<T> Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
```

- [ ] **Step 3: Recompile and run the tests. Confirm the failing state.**

```
unity command recompile
unity command recompile_status
unity command eval -- --code "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"
unity command run_tests -- --mode editor --async_tests true
unity command test_status
```

Expected:
- compiles clean;
- `TheSceneLinksTheCentreToEveryTier2AndTier3` **fails** (it reads `[3, 4, 5]`);
- every `TerritoryMapTests` test passes, because they use the fixture;
- everything else passes.

Report the totals.

- [ ] **Step 4: Add the links to the scene.** Write `SCRATCH\ring-minimap\AddGddLinks.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// One-time (ring/minimap step 1): GDD p.27 links the centre (tower 9) to every Tier 2 zone. Not committed; the scene is.
public static class AddGddLinks
{
    private static readonly int[] Tier2Zones = { 0, 1, 2 };

    public static string Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Game Scene.unity") return "ABORT: active scene is " + scene.path;
        if (scene.isDirty) return "ABORT: scene is dirty before the edit - reload it from disk first";

        BuildingManager[] managers = UnityEngine.Object.FindObjectsByType<BuildingManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (managers.Length != 1) return $"ABORT: found {managers.Length} BuildingManagers";

        var so = new SerializedObject(managers[0]);
        SerializedProperty list = so.FindProperty("TowerDictionary.dictionaryList");
        if (list == null) return "ABORT: TowerDictionary.dictionaryList not found";

        SerializedProperty adjacents = null;
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("Key").intValue == 9)
                adjacents = entry.FindPropertyRelative("Value.Adjacents");
        }
        if (adjacents == null) return "ABORT: no TowerDictionary entry for tower 9";

        var now = new List<int>();
        for (int i = 0; i < adjacents.arraySize; i++)
            now.Add(adjacents.GetArrayElementAtIndex(i).intValue);
        foreach (int zone in Tier2Zones)
        {
            if (now.Contains(zone)) continue;
            adjacents.InsertArrayElementAtIndex(adjacents.arraySize);
            adjacents.GetArrayElementAtIndex(adjacents.arraySize - 1).intValue = zone;
            now.Add(zone);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) return "ERROR: SaveScene returned false - reload the scene from disk";
        return $"tower 9 adjacents [{string.Join(",", now)}], dirty after save={scene.isDirty}";
    }
}
```

Run it:
`unity command --timeout 240 run_script -- --file "SCRATCH\ring-minimap\AddGddLinks.cs" --entry AddGddLinks.Run --timeout_ms 200000`

Expected: `tower 9 adjacents [3,4,5,0,1,2], dirty after save=False`.

- [ ] **Step 5: Read the scene diff.** Run `git diff --stat -- "Assets/Scenes/Game Scene.unity"`, then `git diff`.
  - Expected: exactly one changed line, `Adjacents: 030000000400000005000000` → `Adjacents: 030000000400000005000000000000000100000002000000`.
  - If Unity re-serialized anything else, **don't commit the extra lines**. Report them, and ask the controller whether to keep them.

- [ ] **Step 6: Recompile, dirty check (False), run tests async.** Expected: all pass, and the total is the Step 3
  total. `TerritoryMapTests` net +4 and the scene tests +2 were already counted there.

- [ ] **Step 7: Master-side check in Play Mode (one client, the Editor, who is the master).**
  1. Confirm the dirty check reads False. Run `unity command editor_play`, then poll `editor_status` until playing.
  2. Join the room:
     `unity command eval -- --code "Photon.Pun.PhotonNetwork.NickName = \"EditorHost\"; Photon.Pun.PhotonNetwork.JoinLobby(); return \"joining\";"`
     - If it reports not connected, poll `return Photon.Pun.PhotonNetwork.IsConnectedAndReady;` first.
     - Poll `return Photon.Pun.PhotonNetwork.InRoom + " " + (PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber) != null);` until `True True`.
  3. Write `SCRATCH\ring-minimap\T4FromT2.cs`. It hands the team its own T2, then walks it into the centre.

```csharp
// Editor Play Mode, master, alone. Writes SCRATCH\ring-minimap\t4-from-t2.txt.
var m = BuildingManager.Instance;
Overpower.Net.Teams.TryGetTeam(Photon.Pun.PhotonNetwork.LocalPlayer, out int team);
int capital = m.Map.CapitalOf(team);
int t2 = m.Map.AdjacentTo(capital)[0];
string outPath = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ring-minimap\t4-from-t2.txt";
m.SetCaptured(t2, team, 0, 0); // master shortcut: the team owns its capital and its Tier 2 only

System.Collections.IEnumerator Run()
{
    yield return new WaitForSeconds(1.5f); // the territory write echoes back
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"team={team} capital={capital} t2={t2} owner(t2)={m.Current.OwnerOf(t2)} owner(3,4,5)={m.Current.OwnerOf(3)},{m.Current.OwnerOf(4)},{m.Current.OwnerOf(5)}");
    sb.AppendLine($"MayCapture(9) plain={m.Map.MayCapture(team, 9, m.CurrentOwners)} withT2UnderAttack={m.Map.MayCapture(team, 9, m.CurrentOwners, z => z == t2)}");

    BuildingCapture centre = null;
    foreach (var b in UnityEngine.Object.FindObjectsByType<BuildingCapture>(FindObjectsSortMode.None))
        if (b.buildingID == 9) centre = b;
    var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
    var disp = me.GetComponent<PlayerDisplacement>();
    Vector3 c = centre.transform.position;
    bool moved = false;
    for (int i = 0; i < 8 && !moved; i++)
    {
        float a = i * 45f * Mathf.Deg2Rad;
        Vector3 p = new Vector3(c.x + Mathf.Sin(a) * 4f, me.transform.position.y, c.z + Mathf.Cos(a) * 4f);
        if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
        moved = disp.TeleportTo(p);
        sb.AppendLine($"teleport {moved} to {p:F2}");
    }
    float arrived = Time.time;
    yield return new WaitForSeconds(3f);
    var progress = m.CaptureProgressOf(9);
    sb.AppendLine($"after {Time.time - arrived:0.00}s in zone 9: team={progress.Team} rate={progress.RatePerSecond01:0.0000} fill={progress.Evaluate(Photon.Pun.PhotonNetwork.ServerTimestamp):0.000}");
    System.IO.File.WriteAllText(outPath, sb.ToString());
}
m.StartCoroutine(Run());
return "running";
```

  4. Run `unity command eval_file -- --file "SCRATCH\ring-minimap\T4FromT2.cs" --timeout 20000`. Wait about 10 s, then
     Read `t4-from-t2.txt`.
  5. Expected:
     - `owner(t2)` is the team, and zones 3, 4 and 5 are -1.
     - `MayCapture(9) plain=True withT2UnderAttack=False`.
     - In zone 9: `team` is the local team and `rate` is about `0.0667` (1/15: T4's `captureSeconds` is 15 in
       `TerritoryConfig.asset`).
     - A live check of the link block needs a second client; it is in Task 7, R2.
  6. `unity command editor_stop`; poll until stopped. Dirty check: False. The Play Mode territory writes aren't saved.

- [ ] **Step 8: Commit + push** (only these files):

```
git add "Assets/Scenes/Game Scene.unity" Assets/Tests/TerritoryMapTests.cs Assets/Tests/TerritoryAdjacencySceneTests.cs Assets/Tests/TerritoryAdjacencySceneTests.cs.meta
git status
git commit -m "feat(territory): the centre links to every Tier 2 zone, as in the GDD (ring/minimap step 1)" -m "Co-Authored-By: <your model line>"
git push
```

Also add `[T] T2<->T4 links match GDD p.27` under the assumptions heading (Rule 15).

---

### Task 2 (ring/minimap step 2): held progress at rate 0, pure ring state, consumer tests

**What exists:**
- **Where Idle is published today.** `BuildingCapture.ComputeCurrentProgress` (`Assets/scripts/Player/Building capture.cs:265-295`) returns `Idle` in two cases:
  - a paused drain (lines 271-273);
  - a contested or link-blocked neutral capture (lines 289-290).
- **Publishing.** `PublishProgressIfNeeded` (lines 244-263) publishes only when `NeedsRepublishComparedTo` says the team or the rate changed (`Assets/scripts/Match/Rules/CaptureProgress.cs:45-46`).
- **The event.** `BuildingManager.ApplyCaptureProgressIfPresent` (`BuildingManager.cs:724-764`) raises `CaptureProgressChanged` using the same predicate.
- **Every consumer of `CaptureProgress` / `Idle`** (grep, 2026-09-17):
  - `CaptureProgressView` (removed in step 3).
  - `MatchTelemetry.HandleCaptureProgressChanged` (`Assets/scripts/Telemetry/MatchTelemetry.cs`, currently around line 329). It feeds `CaptureTransitionClassifier.Classify` (`Assets/scripts/Telemetry/CaptureTransitionClassifier.cs:47-77`).
  - `TelemetryAggregator`, which reads only the logged state strings (`Assets/scripts/Editor/Telemetry/TelemetryAggregator.cs:86-94`).
  - No two-client-eval template reads capture progress.

**Why the telemetry log doesn't change.** `Classify` already judges "active" by the rate alone, not by comparing with
`Idle` (its own doc comment, lines 37-46, and tests at lines 94-125). Every publish sequence before and after:

| Situation | Published before | Published now | `capture` line logged |
|---|---|---|---|
| Capturing, then contested or link blocked | active → Idle | active → Held | `paused` with `old.Evaluate(now)`, both times |
| Held capture resumes | Idle → active | Held → active | `resumed` if progress > 0.01, both times |
| Held capture, capturers leave (reset) | nothing (Idle → Idle) | Held → Idle (team changed) | none, both times (neither side active) |
| Draining, then paused (link under attack) | active → Idle | active → Held | `drainPaused` |
| Held drain resumes | Idle → active | Held → active | `drainResumed` |
| Held drain, defender arrives (Stop) | nothing | Held → Idle | none |
| Held capture of team A, team B's fresh capture | Idle → active(B) | Held(A) → active(B) | `started` for team B |

The only new wire traffic is `Held → Idle`. It raises `CaptureProgressChanged`, `Classify` returns null for it, and
nothing is logged. `CaptureProgressPublishCount` is diagnostic only and has no test. The capital-under-attack plan's
old check, "a blocked capture's progress stays Idle", now reads `Held` once anything is banked.

**Files:**
- Modify: `Assets/scripts/Match/Rules/CaptureProgress.cs`, `Assets/Tests/CaptureProgressTests.cs`
- Modify: `Assets/scripts/Player/Building capture.cs` (the `PublishProgressIfNeeded` doc comment and `ComputeCurrentProgress`)
- Create: `Assets/scripts/Match/Rules/CaptureRingState.cs`, `Assets/Tests/CaptureRingStateTests.cs`
- Create: `Assets/scripts/Match/Rules/CaptureRingGeometry.cs`, `Assets/Tests/CaptureRingGeometryTests.cs`
- Modify: `Assets/Tests/CaptureTransitionClassifierTests.cs`, `Assets/scripts/Telemetry/CaptureTransitionClassifier.cs` (doc comment only)

- [ ] **Step 1: Failing `CaptureProgressTests`.** Add these inside the class:

```csharp
        [Test]
        public void HeldKeepsTeamAndProgressAtRateZeroAndDoesNotMove()
        {
            CaptureProgress held = CaptureProgress.Held(1, 0.4f, 500);
            Assert.AreEqual(1, held.Team);
            Assert.AreEqual(0.4f, held.Progress01, 1e-5f);
            Assert.AreEqual(0f, held.RatePerSecond01);
            Assert.IsTrue(held.IsHeld);
            Assert.AreEqual(0.4f, held.Evaluate(99999), 1e-5f);
        }

        [Test]
        public void HeldWithNothingBankedOrNoTeamIsIdle()
        {
            Assert.AreEqual(-1, CaptureProgress.Held(1, 0f, 500).Team);
            Assert.AreEqual(-1, CaptureProgress.Held(-1, 0.5f, 500).Team);
            Assert.IsFalse(CaptureProgress.Held(1, 0f, 500).IsHeld);
        }

        [Test]
        public void MovingIntoOrOutOfAHoldNeedsARepublish()
        {
            var moving = new CaptureProgress(1, 0.2f, 0.1f, 0);
            CaptureProgress held = CaptureProgress.Held(1, 0.3f, 1000);
            Assert.IsTrue(moving.NeedsRepublishComparedTo(held));
            Assert.IsTrue(held.NeedsRepublishComparedTo(moving));
            Assert.IsTrue(held.NeedsRepublishComparedTo(CaptureProgress.Idle), "a hold ending in Idle changes the team");
        }

        [Test]
        public void IdleAndMovingProgressAreNotHeld()
        {
            Assert.IsFalse(CaptureProgress.Idle.IsHeld);
            Assert.IsFalse(new CaptureProgress(0, 0.5f, -0.2f, 0).IsHeld);
        }
```

- [ ] **Step 2: Failing `CaptureRingStateTests`.** Create `Assets/Tests/CaptureRingStateTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class CaptureRingStateTests
    {
        [Test]
        public void NothingInProgressOnANeutralZoneShowsNoBandAndANeutralEdge()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Idle, -1, false, 1000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.IsFalse(s.ShowsArc);
            Assert.AreEqual(-1, s.OutlineTeam);
            Assert.IsFalse(s.UnderAttack);
        }

        [Test]
        public void NothingInProgressOnAnOwnedZoneEdgesInTheOwnersColour()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Idle, 2, false, 1000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.AreEqual(2, s.OutlineTeam);
        }

        [Test]
        public void ACaptureGrowsInTheCapturersColourFromTheServerClock()
        {
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0.2f, 1f / 15f, 1000), -1, false, 4000);
            Assert.AreEqual(CaptureRingPhase.Capturing, s.Phase);
            Assert.AreEqual(0.4f, s.Fill01, 1e-4f);
            Assert.AreEqual(1, s.ArcTeam);
            Assert.AreEqual(-1, s.OutlineTeam);
            Assert.IsTrue(s.ShowsArc);
        }

        [Test]
        public void ADrainShowsTheOwnersRemainingHoldAndNamesTheDrainer()
        {
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(2, 0.8f, -0.2f, 1000), 0, true, 2000);
            Assert.AreEqual(CaptureRingPhase.Draining, s.Phase);
            Assert.AreEqual(0.6f, s.Fill01, 1e-4f);
            Assert.AreEqual(0, s.ArcTeam, "the band is the owner's remaining hold, in the owner's colour");
            Assert.AreEqual(0, s.OutlineTeam);
            Assert.AreEqual(2, s.DrainerTeam);
            Assert.IsTrue(s.UnderAttack);
        }

        [Test]
        public void AHeldCaptureIsPausedAtItsValueInTheCapturersColour()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Held(1, 0.35f, 1000), -1, false, 99999);
            Assert.AreEqual(CaptureRingPhase.Paused, s.Phase);
            Assert.AreEqual(0.35f, s.Fill01, 1e-5f);
            Assert.AreEqual(1, s.ArcTeam);
            Assert.AreEqual(-1, s.DrainerTeam);
        }

        [Test]
        public void AHeldDrainIsPausedInTheOwnersColour()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Held(2, 0.6f, 1000), 0, true, 5000);
            Assert.AreEqual(CaptureRingPhase.Paused, s.Phase);
            Assert.AreEqual(0.6f, s.Fill01, 1e-5f);
            Assert.AreEqual(0, s.ArcTeam);
            Assert.AreEqual(-1, s.DrainerTeam, "only a moving drain pulses in the drainer's colour");
            Assert.IsTrue(s.UnderAttack);
        }

        [Test]
        public void AHoldWithNothingBankedIsIdle()
        {
            // Decoded from the room: a team with 0 progress and rate 0 (a hold below 1/10000 rounds to 0 on the wire).
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0f, 0f, 1000), -1, false, 2000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
        }

        [Test]
        public void UnderAttackOnlyCountsForAnOwnedZoneAndShowsWithNothingDraining()
        {
            Assert.IsFalse(CaptureRingState.From(CaptureProgress.Idle, -1, true, 1000).UnderAttack);
            CaptureRingState owned = CaptureRingState.From(CaptureProgress.Idle, 1, true, 1000);
            Assert.IsTrue(owned.UnderAttack);
            Assert.AreEqual(CaptureRingPhase.Idle, owned.Phase);
        }

        [Test]
        public void ADrainOnAZoneThatAlreadyWentNeutralIsIdle()
        {
            // The owner and the progress are two separate room updates; the neutral owner can arrive first.
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(2, 0.1f, -0.2f, 1000), -1, false, 1400);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
        }

        [Test]
        public void ACaptureByTheZonesOwnOwnerIsIdle()
        {
            // The same gap after a capture completes: the new owner arrives before the progress goes Idle.
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0.95f, 1f / 15f, 1000), 1, false, 2000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.AreEqual(1, s.OutlineTeam);
        }

        [Test]
        public void WithoutAServerClockTheBandShowsThePublishedValue()
        {
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0.25f, 1f / 15f, 1000), -1, false, 0);
            Assert.AreEqual(0.25f, s.Fill01, 1e-5f);
        }
    }
}
```

- [ ] **Step 3: Failing `CaptureRingGeometryTests`.** Create `Assets/Tests/CaptureRingGeometryTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Match;
using UnityEngine;

namespace Overpower.Tests
{
    public class CaptureRingGeometryTests
    {
        private static void AssertClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected:F4} but was {actual:F4}");

        [Test]
        public void PointOnRingStartsAtTheCamerasUpAndGoesClockwiseSeenFromAbove()
        {
            var centre = new Vector3(10f, 0.5f, 20f);
            // Camera yaw 0 looks toward +Z, so screen-up on the ground is +Z; clockwise from above turns +Z toward +X.
            AssertClose(new Vector3(10f, 0.5f, 25f), CaptureRingGeometry.PointOnRing(centre, 5f, 0f, 0f));
            AssertClose(new Vector3(15f, 0.5f, 20f), CaptureRingGeometry.PointOnRing(centre, 5f, 0f, 90f));
            // A camera turned 90° has +X at the top of the screen.
            AssertClose(new Vector3(15f, 0.5f, 20f), CaptureRingGeometry.PointOnRing(centre, 5f, 90f, 0f));
            AssertClose(new Vector3(10f, 0.5f, 15f), CaptureRingGeometry.PointOnRing(centre, 5f, 90f, 90f));
        }

        [Test]
        public void ArcPointCountFollowsTheFillAndNeverDrawsASinglePoint()
        {
            Assert.AreEqual(0, CaptureRingGeometry.ArcPointCount(0f, 96));
            Assert.AreEqual(2, CaptureRingGeometry.ArcPointCount(0.001f, 96));
            Assert.AreEqual(49, CaptureRingGeometry.ArcPointCount(0.5f, 96));
            Assert.AreEqual(97, CaptureRingGeometry.ArcPointCount(1f, 96), "a full band closes the circle");
            Assert.AreEqual(97, CaptureRingGeometry.ArcPointCount(1.5f, 96));
        }

        [Test]
        public void ArcStepSplitsTheCircleEvenly()
        {
            Assert.AreEqual(3.75f, CaptureRingGeometry.ArcStepDegrees(96), 1e-5f);
        }

        [Test]
        public void PulseStartsAtZeroAndPeaksHalfwayThroughAPulse()
        {
            Assert.AreEqual(0f, CaptureRingGeometry.Pulse01(0f, 2f), 1e-5f);
            Assert.AreEqual(1f, CaptureRingGeometry.Pulse01(0.25f, 2f), 1e-5f);
            Assert.AreEqual(0f, CaptureRingGeometry.Pulse01(0.5f, 2f), 1e-5f);
        }

        [Test]
        public void BlinkStartsFullyOnAndIsTheOppositeOfThePulse()
        {
            Assert.AreEqual(1f, CaptureRingGeometry.Blink01(0f, 0.7f), 1e-5f);
            Assert.AreEqual(1f - CaptureRingGeometry.Pulse01(0.3f, 0.7f), CaptureRingGeometry.Blink01(0.3f, 0.7f), 1e-5f);
        }
    }
}
```

- [ ] **Step 4: Classifier tests.** In `Assets/Tests/CaptureTransitionClassifierTests.cs`:
  - Rename `ActiveToARateZeroHoldStillLogsPausedNotSilence` → `ActiveToAHeldStateLogsPaused`, and build its new value
    with `CaptureProgress.Held(0, 0.2f + 2f / 15f, 3000)`.
  - Rename `ResumingOutOfAFutureHeldStateIsResumedNotStarted` → `ResumingOutOfAHeldStateIsResumed`, and build its held
    value with `CaptureProgress.Held(0, 0.35f, 3000)`.
  - In both, replace "future" in the comments with "held (published since the capture ring change, 2026-09-17)".
  - Add inside the class:

```csharp
        [Test]
        public void DrainingIntoAHeldDrainIsDrainPaused()
        {
            var wasDraining = new CaptureProgress(2, 0.8f, -0.2f, 1000);
            string state = CaptureTransitionClassifier.Classify(wasDraining, CaptureProgress.Held(2, 0.6f, 2000),
                2000, out int team, out float progress);
            Assert.AreEqual(CaptureTransitionClassifier.DrainPaused, state);
            Assert.AreEqual(2, team);
            Assert.AreEqual(0.6f, progress, 1e-4f);
        }

        [Test]
        public void AHeldDrainMovingAgainIsDrainResumed()
        {
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Held(2, 0.6f, 2000),
                new CaptureProgress(2, 0.6f, -0.2f, 5000), 5000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.DrainResumed, state);
            Assert.AreEqual(2, team);
        }

        [Test]
        public void AHoldEndingInIdleLogsNothing()
        {
            // Capturers left a held capture (it resets) or a defender stopped a held drain: the pause was already logged.
            Assert.IsNull(CaptureTransitionClassifier.Classify(CaptureProgress.Held(0, 0.4f, 3000), CaptureProgress.Idle,
                4000, out _, out _));
        }

        [Test]
        public void IdleIntoAHoldLogsNothing()
        {
            Assert.IsNull(CaptureTransitionClassifier.Classify(CaptureProgress.Idle, CaptureProgress.Held(0, 0.4f, 3000),
                3000, out _, out _));
        }

        [Test]
        public void AnotherTeamsFreshCaptureAfterAHoldIsStartedForThatTeam()
        {
            string state = CaptureTransitionClassifier.Classify(CaptureProgress.Held(0, 0.4f, 3000),
                new CaptureProgress(1, 0.001f, 1f / 15f, 4000), 4000, out int team, out _);
            Assert.AreEqual(CaptureTransitionClassifier.Started, state);
            Assert.AreEqual(1, team);
        }
```

- [ ] **Step 5: Recompile. Confirm the failing state.** Expected: compile errors naming `CaptureProgress.Held`,
  `IsHeld`, `CaptureRingState`, `CaptureRingPhase` and `CaptureRingGeometry`, and nothing else.

- [ ] **Step 6: `CaptureProgress`.** Add to `Assets/scripts/Match/Rules/CaptureProgress.cs`, below `Idle`:

```csharp
        /// <summary>A capture or drain on hold (Tudor, 2026-09-16 capture ring): a contested capture, one whose link is
        /// under attack, or a paused drain. It keeps its team and how far it got, at rate 0, so every client can draw
        /// the paused band. The same team at the same rate never republishes, and a hold never moves, so its
        /// progress can't go stale on the wire. Idle when there is no team or nothing banked.</summary>
        public static CaptureProgress Held(int team, float progress01, int stampMs) =>
            team < 0 || progress01 <= 0f ? Idle : new CaptureProgress(team, Math.Min(1f, progress01), 0f, stampMs);

        /// <summary>A team with progress that isn't moving - see Held.</summary>
        public bool IsHeld => Team >= 0 && RatePerSecond01 == 0f;
```

- [ ] **Step 7: `CaptureRingGeometry`.** Create `Assets/scripts/Match/Rules/CaptureRingGeometry.cs`:

```csharp
using UnityEngine;

namespace Overpower.Match
{
    /// <summary>
    /// The small maths behind the capture ring on the ground and the progress ring on the minimap, kept pure so it's
    /// tested: where a point on a ring is, how many points a part-filled band needs, and the pulse/blink curves.
    ///
    /// Angles are Unity yaw: degrees clockwise seen from above, 0 = +Z. That is also "clockwise on screen" for this
    /// game's camera, which looks down at the player without rolling, so a band filling clockwise here fills
    /// clockwise on screen.
    /// </summary>
    public static class CaptureRingGeometry
    {
        /// <summary>The point <paramref name="clockwiseDegrees"/> round from <paramref name="startYawDegrees"/> on a
        /// flat ring. Pass the camera's yaw as the start to begin at the top of the screen. Height is the centre's.</summary>
        public static Vector3 PointOnRing(Vector3 centre, float radius, float startYawDegrees, float clockwiseDegrees)
        {
            float radians = (startYawDegrees + clockwiseDegrees) * Mathf.Deg2Rad;
            return new Vector3(centre.x + Mathf.Sin(radians) * radius, centre.y, centre.z + Mathf.Cos(radians) * radius);
        }

        /// <summary>Points in a band filled to <paramref name="fill01"/> of a ring made of <paramref name="segments"/>
        /// pieces: 0 when empty, at least 2 (a line needs two), segments + 1 when full (the last point closes the
        /// circle). Rounded to whole pieces, so the band only needs new points when this number changes.</summary>
        public static int ArcPointCount(float fill01, int segments)
        {
            if (fill01 <= 0f || segments < 1)
                return 0;
            int pieces = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fill01) * segments), 1, segments);
            return pieces + 1;
        }

        public static float ArcStepDegrees(int segments) => 360f / Mathf.Max(1, segments);

        /// <summary>0 → 1 → 0 once per 1/perSecond seconds, starting at 0: a pulse that begins at the base colour.</summary>
        public static float Pulse01(float timeSeconds, float perSecond) =>
            0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * perSecond * timeSeconds);

        /// <summary>1 → 0 → 1, starting fully visible: a paused band blinks off and back on.</summary>
        public static float Blink01(float timeSeconds, float perSecond) => 1f - Pulse01(timeSeconds, perSecond);
    }
}
```

- [ ] **Step 8: `CaptureRingState`.** Create `Assets/scripts/Match/Rules/CaptureRingState.cs`:

```csharp
namespace Overpower.Match
{
    /// <summary>What a zone's capture ring (and its minimap progress ring) shows.</summary>
    public enum CaptureRingPhase
    {
        /// <summary>Nothing in progress: no band.</summary>
        Idle,
        /// <summary>A team is taking a neutral zone: the band grows in that team's colour.</summary>
        Capturing,
        /// <summary>An enemy drains an owned zone: the band is the owner's remaining hold, shrinking, in the owner's colour.</summary>
        Draining,
        /// <summary>A capture or drain on hold (contested, its link under attack, a paused drain): the band stays and blinks.</summary>
        Paused,
    }

    /// <summary>
    /// The capture ring's state (spec 2026-09-16, "Capture ring"), worked out from what every client already has: the
    /// zone's replicated CaptureProgress, its owner and whether it is under attack. Pure, so the ground ring and the
    /// minimap can't disagree, and every case is tested.
    /// </summary>
    public readonly struct CaptureRingState
    {
        public readonly CaptureRingPhase Phase;
        /// <summary>How much of the band is drawn, 0..1.</summary>
        public readonly float Fill01;
        /// <summary>The band's team colour; -1 when Idle.</summary>
        public readonly int ArcTeam;
        /// <summary>The edge's team colour: the owner, -1 = neutral.</summary>
        public readonly int OutlineTeam;
        /// <summary>The team draining the zone while Draining (the edge pulses in its colour); -1 otherwise.</summary>
        public readonly int DrainerTeam;
        /// <summary>An owned zone with a living enemy inside or just gone (ZonePresenceTracker).</summary>
        public readonly bool UnderAttack;

        public CaptureRingState(CaptureRingPhase phase, float fill01, int arcTeam, int outlineTeam, int drainerTeam, bool underAttack)
        {
            Phase = phase;
            Fill01 = fill01;
            ArcTeam = arcTeam;
            OutlineTeam = outlineTeam;
            DrainerTeam = drainerTeam;
            UnderAttack = underAttack;
        }

        public bool ShowsArc => Phase != CaptureRingPhase.Idle && Fill01 > 0f;

        /// <param name="owner">The zone's owner, or -1 for neutral.</param>
        /// <param name="underAttack">ZonePresenceTracker.IsUnderAttack(zone). Ignored for a neutral zone.</param>
        /// <param name="nowMs">PhotonNetwork.ServerTimestamp; 0 = not synced yet.</param>
        public static CaptureRingState From(CaptureProgress progress, int owner, bool underAttack, int nowMs)
        {
            int outlineTeam = owner >= 0 ? owner : TerritoryMap.Neutral;
            bool attacked = owner >= 0 && underAttack;

            // A team never captures or drains its own zone. Seeing it means the room's owner update arrived before its
            // progress update (they are separate writes): the capture just completed.
            if (progress.Team < 0 || progress.Team == owner)
                return Idle(outlineTeam, attacked);

            // Without a synced clock, extrapolating from "now = 0" would flash the band full or empty; show the
            // published value instead (the old bar skipped the frame for the same reason).
            float fill = nowMs == 0 || progress.StampMs == 0 ? Clamp01(progress.Progress01) : progress.Evaluate(nowMs);

            if (progress.RatePerSecond01 > 0f)
                return new CaptureRingState(CaptureRingPhase.Capturing, fill, progress.Team, outlineTeam, TerritoryMap.Neutral, attacked);

            if (progress.RatePerSecond01 < 0f)
            {
                // Same two-update gap, for a drain: the zone already went neutral.
                if (owner < 0)
                    return Idle(outlineTeam, attacked);
                return new CaptureRingState(CaptureRingPhase.Draining, fill, owner, outlineTeam, progress.Team, attacked);
            }

            float held = Clamp01(progress.Progress01);
            if (held <= 0f)
                return Idle(outlineTeam, attacked);
            // A held drain is the owner's remaining hold (owner's colour); a held capture of a neutral zone is the
            // capturer's.
            return new CaptureRingState(CaptureRingPhase.Paused, held, owner >= 0 ? owner : progress.Team, outlineTeam,
                                        TerritoryMap.Neutral, attacked);
        }

        private static CaptureRingState Idle(int outlineTeam, bool attacked) =>
            new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, outlineTeam, TerritoryMap.Neutral, attacked);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
```

- [ ] **Step 9: Publish held progress.** In `Assets/scripts/Player/Building capture.cs`, replace `ComputeCurrentProgress`
  (lines 265-295) with:

```csharp
    private CaptureProgress ComputeCurrentProgress(int nowMs)
    {
        float captureSeconds = CaptureSeconds;
        if (captureSeconds <= 0f)
            return CaptureProgress.Idle;

        if (isCaptured)
        {
            if (!isDecaying)
                return CaptureProgress.Idle;

            float decayProgress01 = captureProgress / captureSeconds;
            // A paused drain (its drainers' way in is under attack - see DrainRule) keeps its team and the owner's
            // remaining hold at rate 0, so every client's capture ring can show it paused (2026-09-17). It used to go
            // Idle, which hid how far the drain had got.
            if (isDrainPaused)
                return CaptureProgress.Held(capturingID, decayProgress01, nowMs);

            float decayRate = DecaySeconds > 0f ? -1f / DecaySeconds : 0f;
            return new CaptureProgress(capturingID, decayProgress01, decayRate, nowMs);
        }

        if (isOnCooldown || capturingID == -1 || playersInZone.Count == 0)
            return CaptureProgress.Idle;

        float progress01 = captureProgress / captureSeconds;

        // Mirrors CalculateCaptureProgress's own eligibility check: only "N of my team, nobody
        // else, and still allowed to capture" actually moves the bar - otherwise
        // CalculateCaptureProgress itself is not advancing captureProgress this frame either, so the
        // bar must not claim it is. TeamMayCaptureNow gives both the same answer within a frame.
        var eligiblePlayers = playersInZone.Where(p => p.teamID == capturingID).ToList();
        bool enemyPresent = playersInZone.Any(p => p.teamID != capturingID);
        if (!eligiblePlayers.Any() || enemyPresent || !TeamMayCaptureNow(capturingID))
            // Held, not Idle (2026-09-17): a contested or link-blocked capture keeps what it has banked, and the
            // ring shows it paused. Capturers who LEAVE still reset it (EndCaptureIfCapturersLeft), so this is
            // Idle then: Held returns Idle when nothing is banked.
            return CaptureProgress.Held(capturingID, progress01, nowMs);

        float rate = eligiblePlayers.Count / captureSeconds;
        return new CaptureProgress(capturingID, progress01, rate, nowMs);
    }
```

In `PublishProgressIfNeeded`'s doc comment (lines 236-243), replace the last sentence ("Anything else ... Idle, which
hides the bar.") with: "A capture or drain on hold with something banked (contested, its link under attack, a paused
drain): CaptureProgress.Held, rate 0. Nothing in progress (idle, on cooldown, captured with nobody contesting it):
Idle."

- [ ] **Step 10: Classifier comment.** In `CaptureTransitionClassifier.Classify`'s doc comment (lines 37-46), replace
  "a future task that publishes a genuinely paused, rate-0 hold with its team and progress still set (rather than
  collapsing to Idle)" with "a held capture or drain (CaptureProgress.Held, published since the capture ring change,
  2026-09-17)". The code is unchanged.

- [ ] **Step 11: Recompile, dirty check, tests.** Expected: compiles clean, all pass. The total is Task 1's total
  + 25:
  - CaptureProgress +4
  - CaptureRingState +11
  - CaptureRingGeometry +5
  - classifier +5, where the two renames are not new
  - `TelemetryAggregatorTests`, `TelemetryAggregatorReviewFixesTests` and the phase-split tests pass unchanged.

- [ ] **Step 12: Commit + push:**

```
git add Assets/scripts/Match/Rules/CaptureProgress.cs Assets/Tests/CaptureProgressTests.cs "Assets/scripts/Player/Building capture.cs" Assets/scripts/Match/Rules/CaptureRingState.cs Assets/scripts/Match/Rules/CaptureRingState.cs.meta Assets/Tests/CaptureRingStateTests.cs Assets/Tests/CaptureRingStateTests.cs.meta Assets/scripts/Match/Rules/CaptureRingGeometry.cs Assets/scripts/Match/Rules/CaptureRingGeometry.cs.meta Assets/Tests/CaptureRingGeometryTests.cs Assets/Tests/CaptureRingGeometryTests.cs.meta Assets/Tests/CaptureTransitionClassifierTests.cs Assets/scripts/Telemetry/CaptureTransitionClassifier.cs
git status
git commit -m "feat(match): publish held capture progress at rate 0, and a pure capture ring state (ring/minimap step 2)" -m "Co-Authored-By: <your model line>"
git push
```

If another agent has uncommitted edits in `CaptureTransitionClassifier.cs` or in the classifier tests, leave that file
out of this commit and report it: interactive staging isn't available, and the change is a comment only.

---

### Task 3 (ring/minimap step 3): the ground ring replaces the tower bar

**What exists:**
- **The bar.** `BuildingCapture.Start` builds `CaptureProgressView` (`Building capture.cs:109-112`). `Update` calls `RefreshProgressView` on every client (lines 187, 228-234).
- **Nothing else uses the bar.** `CaptureProgressView` (`Assets/scripts/Match/CaptureProgressView.cs`) is the only user of the `UiTheme` "Capture bar" fields (`UiTheme.cs:353-361`; asset lines 114-117). Grep, 2026-09-17: no other reference.
- **Team colours.** They come from `UiTheme.ShotColorFor` (`UiTheme.cs:240-246`).
- **Camera yaw.** `CameraTracking` keeps it private (`CameraTracking.cs:96`, resolved in `ResolveTeamYaw`, 131-165). There is one instance, on the scene's main camera (`Game Scene.unity:4811`).
- **Height.** The towers sit on terrain tiles (`Terrain1`…`Terrain1 (5)`).

**Files:**
- Modify: `Assets/scripts/CameraTracking.cs`
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`
- Create: `Assets/scripts/Match/CaptureRingView.cs`
- Modify: `Assets/scripts/Player/Building capture.cs`
- Delete: `Assets/scripts/Match/CaptureProgressView.cs`, `Assets/scripts/Match/CaptureProgressView.cs.meta`

- [ ] **Step 1: `CameraTracking` exposes its yaw.** In `Assets/scripts/CameraTracking.cs`, add inside the class, above `Start`:

```csharp
    /// <summary>The camera following the local player (there is one, on the scene's main camera). The capture rings and
    /// the minimap read its Yaw, so "up" on them is "up" on screen.</summary>
    public static CameraTracking Instance { get; private set; }

    /// <summary>Degrees this camera is turned about the vertical axis (Unity yaw: clockwise seen from above; 0 = looking
    /// toward +Z). 0 until this player's team is known - see ResolveTeamYaw - then fixed for the match. World
    /// direction (sin Yaw, cos Yaw) is the top of the screen.</summary>
    public float Yaw => yaw;

    public bool YawResolved => teamYawResolved;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
```

- [ ] **Step 2: Theme fields in C#.** In `Assets/scripts/UI/UiTheme.cs`, replace the whole `[Header("Capture bar")]` block
  (lines 353-361, the four `captureBar*` fields) with:

```csharp
        [Header("Capture ring (2026-09-16)")]
        [Tooltip("Material every capture ring line draws with. Keep it unlit, transparent, vertex-coloured and its own " +
                 "colour white: each ring tints itself per team. Points at the Aim Cone Line material, which is exactly that.")]
        public Material captureRingMaterial;
        [Tooltip("Thickness of the thin circle on the ground marking the edge of every capture zone, in metres. Its " +
                 "outer edge sits exactly on the zone's Capture Radius.")]
        public float captureRingOutlineWidth = 0.15f;
        [Tooltip("Thickness of the progress band drawn just inside the edge while a zone is being captured or drained, " +
                 "in metres.")]
        public float captureRingArcWidth = 0.45f;
        [Tooltip("Gap between the edge circle and the progress band, in metres.")]
        public float captureRingArcGap = 0.1f;
        [Tooltip("How far above the ground the ring floats, in metres. Just enough never to flicker into the ground; " +
                 "raise it if parts of a ring disappear on uneven ground.")]
        public float captureRingHeightOffset = 0.06f;
        [Tooltip("Edge colour of a zone nobody owns: a dim white (GDD p.45, neutral territories use a dim white). Owned " +
                 "zones use their team colour.")]
        public Color captureRingNeutralColor = new Color(1f, 1f, 1f, 0.35f);
        [Tooltip("Blinks per second of a paused progress band (the capture is contested, its link is under attack, or " +
                 "a drain is on hold). Keep it slower than the pulse below, so a pause reads differently from an attack.")]
        public float captureRingPausedBlinkSpeed = 0.7f;
        [Tooltip("Brightest opacity (0-1) of a paused progress band while it blinks. Lower than a moving band, so a " +
                 "pause reads as 'on hold'. The minimap's progress rings blink the same way.")]
        [Range(0f, 1f)] public float captureRingPausedOpacity = 0.6f;
        [Tooltip("Colour an owned zone's edge pulses to while an enemy is inside (under attack), even before anything " +
                 "drains. The minimap bubble's outline pulses to it too.")]
        public Color captureRingWarningColor = new Color(1f, 0.2f, 0.15f, 1f);
        [Tooltip("Pulses per second of a zone's edge while it is under attack, or being drained (a drain pulses in the " +
                 "draining team's colour instead).")]
        public float captureRingPulseSpeed = 1.6f;
        [Tooltip("How many straight pieces make up each ring. More reads as a smoother circle; 96 is smooth at every zoom.")]
        [Range(16, 256)] public int captureRingSegments = 96;
```

- [ ] **Step 3: Delete the bar and recompile.**
  1. `git rm Assets/scripts/Match/CaptureProgressView.cs Assets/scripts/Match/CaptureProgressView.cs.meta`
  2. In `Building capture.cs`:
     - Replace lines 85-87 (the `progressView` comment and field) with:

```csharp
    // The ring on the ground marking this zone and its capture progress (2026-09-16; it replaced the bar that floated
    // over the tower). Built in Start so every tower gets one - see CaptureRingView. Null if the theme is unassigned.
    private CaptureRingView ringView;
```

     - Replace lines 109-112 (the theme check in `Start`) with:

```csharp
        if (theme == null)
            Debug.LogError($"[BuildingCapture] Tower {buildingID} has no UI Theme assigned - no capture ring will be shown.", this);
        else if (theme.captureRingMaterial == null)
            Debug.LogError($"[BuildingCapture] Tower {buildingID}: UiTheme's Capture Ring Material is not assigned - no capture ring will be shown.", this);
        else
            ringView = CaptureRingView.Create(transform, captureRadius, theme);
```

     - Change the `theme` tooltip (lines 28-29) to:
       `"Colours, widths and material of the capture ring on the ground around this tower - every tower should point at the same asset, same as Territory Config."`
     - In `Update` (line 187), change `RefreshProgressView();` to `RefreshRingView();`. Replace the method at lines 228-234 with:

```csharp
    /// <summary>Every client, every frame: draws this zone's ring from replicated state only (capture progress, the
    /// owner, under attack), so a late joiner sees exactly what everyone else does.</summary>
    private void RefreshRingView()
    {
        BuildingManager manager = BuildingManager.Instance;
        if (ringView == null || manager == null)
            return;

        int owner = manager.Current != null ? manager.Current.OwnerOf(buildingID) : TerritoryMap.Neutral;
        bool underAttack = ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(buildingID);
        CaptureRingState state = CaptureRingState.From(manager.CaptureProgressOf(buildingID), owner, underAttack,
                                                       PhotonNetwork.ServerTimestamp);
        ringView.Refresh(state, CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f);
    }
```

     - In the `CaptureProgressFraction` comment (lines 870-875), replace "extrapolated CaptureProgressView fill" with "extrapolated capture ring fill".
  3. Create `Assets/scripts/Match/CaptureRingView.cs`:

```csharp
using Overpower.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Match
{
    /// <summary>
    /// The ring on the ground that marks a capture zone and shows a capture or drain filling (Tudor, 2026-09-16: it
    /// replaces the small bar that floated above each tower). BuildingCapture.Start builds one per tower, so a new
    /// tower gets one with nothing to wire up.
    ///
    /// Two flat lines:
    /// - a thin EDGE, always shown, on the Capture Radius: team colour when owned, dim white when neutral;
    /// - a thicker BAND just inside it that fills clockwise from the top of this player's screen.
    /// Which colours, fill, pulse and blink to use is decided by the pure CaptureRingState; this class only draws it.
    ///
    /// Line renderers, not a mesh: the same unlit, double-sided, vertex-colour material the aim cone lines use, tinted
    /// per ring through each line's own colour (no material copies). No collider, no shadows. The band's points are
    /// rewritten only when its length (in whole pieces) or the camera's yaw changes; pulses and blinks only change a
    /// colour.
    /// </summary>
    public sealed class CaptureRingView : MonoBehaviour
    {
        private UiTheme theme;
        private LineRenderer edge;
        private LineRenderer band;
        private Vector3 centre;
        private float bandRadius;
        private int segments;
        private Vector3[] bandPoints;

        // What is currently drawn, so an unchanged frame touches nothing.
        private int shownBandPointCount = -1;
        private float shownBandStartYaw = float.NaN;
        private Color shownEdgeColor;
        private Color shownBandColor;
        private bool edgeColorSet;
        private bool bandColorSet;

        public static CaptureRingView Create(Transform tower, float captureRadius, UiTheme theme)
        {
            var root = new GameObject("Capture Ring");
            // Parented to the tower so it's easy to find and goes with it, but every point is in world space: the
            // tower's own scale (0.8 on these towers) must not shrink the ring away from the real Capture Radius.
            root.transform.SetParent(tower, false);

            var view = root.AddComponent<CaptureRingView>();
            view.theme = theme;
            view.segments = Mathf.Max(16, theme.captureRingSegments);
            Vector3 towerPosition = tower.position;
            view.centre = new Vector3(towerPosition.x, GroundHeight(towerPosition, captureRadius) + theme.captureRingHeightOffset,
                                      towerPosition.z);

            float edgeRadius = Mathf.Max(0.01f, captureRadius - theme.captureRingOutlineWidth / 2f);
            view.bandRadius = Mathf.Max(0.01f, captureRadius - theme.captureRingOutlineWidth - theme.captureRingArcGap
                                                - theme.captureRingArcWidth / 2f);

            view.edge = CreateLine(root.transform, "Edge", theme.captureRingMaterial, theme.captureRingOutlineWidth);
            view.edge.loop = true;
            view.edge.positionCount = view.segments;
            var edgePoints = new Vector3[view.segments];
            float step = CaptureRingGeometry.ArcStepDegrees(view.segments);
            for (int i = 0; i < view.segments; i++)
                edgePoints[i] = CaptureRingGeometry.PointOnRing(view.centre, edgeRadius, 0f, step * i);
            view.edge.SetPositions(edgePoints);

            view.band = CreateLine(root.transform, "Progress Band", theme.captureRingMaterial, theme.captureRingArcWidth);
            view.band.loop = false;
            view.band.positionCount = 0;
            view.band.enabled = false;
            view.bandPoints = new Vector3[view.segments + 1];
            return view;
        }

        /// <summary>Called every frame by BuildingCapture.Update on every client.</summary>
        public void Refresh(CaptureRingState state, float cameraYawDegrees)
        {
            float time = Time.unscaledTime; // presentation only: a paused Time.timeScale must not freeze a pulse

            Color edgeColor = state.OutlineTeam >= 0 ? theme.ShotColorFor(state.OutlineTeam) : theme.captureRingNeutralColor;
            if (state.Phase == CaptureRingPhase.Draining && state.DrainerTeam >= 0)
                edgeColor = Color.Lerp(edgeColor, theme.ShotColorFor(state.DrainerTeam),
                                       CaptureRingGeometry.Pulse01(time, theme.captureRingPulseSpeed));
            else if (state.UnderAttack)
                edgeColor = Color.Lerp(edgeColor, theme.captureRingWarningColor,
                                       CaptureRingGeometry.Pulse01(time, theme.captureRingPulseSpeed));
            if (!edgeColorSet || edgeColor != shownEdgeColor)
            {
                edge.startColor = edgeColor;
                edge.endColor = edgeColor;
                shownEdgeColor = edgeColor;
                edgeColorSet = true;
            }

            if (!state.ShowsArc)
            {
                if (band.enabled)
                    band.enabled = false;
                return;
            }

            int count = CaptureRingGeometry.ArcPointCount(state.Fill01, segments);
            if (count != shownBandPointCount || !Mathf.Approximately(cameraYawDegrees, shownBandStartYaw))
            {
                float step = CaptureRingGeometry.ArcStepDegrees(segments);
                for (int i = 0; i < count; i++)
                    bandPoints[i] = CaptureRingGeometry.PointOnRing(centre, bandRadius, cameraYawDegrees, step * i);
                band.positionCount = count;
                band.SetPositions(bandPoints); // uses only the first positionCount points
                shownBandPointCount = count;
                shownBandStartYaw = cameraYawDegrees;
            }

            Color bandColor = theme.ShotColorFor(state.ArcTeam);
            if (state.Phase == CaptureRingPhase.Paused)
                bandColor.a *= theme.captureRingPausedOpacity * CaptureRingGeometry.Blink01(time, theme.captureRingPausedBlinkSpeed);
            if (!bandColorSet || bandColor != shownBandColor)
            {
                band.startColor = bandColor;
                band.endColor = bandColor;
                shownBandColor = bandColor;
                bandColorSet = true;
            }

            if (!band.enabled)
                band.enabled = true;
        }

        private static LineRenderer CreateLine(Transform parent, string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // TransformZ alignment draws a line facing this object's Z axis. Pointing Z straight up lays the line flat
            // on the ground, instead of turning it toward the camera like a normal line. The material is double-sided.
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; // sharedMaterial: .material would clone it per line
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.widthMultiplier = 1f;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.allowOcclusionWhenDynamic = false;
            return line;
        }

        /// <summary>The highest terrain point under the zone centre and eight points on its edge, so a ring on a slope
        /// never dips into the ground. The tower's own height where no terrain covers the zone. Runs once per tower.</summary>
        private static float GroundHeight(Vector3 towerPosition, float radius)
        {
            Terrain[] terrains = Terrain.activeTerrains;
            float best = float.NegativeInfinity;
            for (int i = 0; i < 9; i++)
            {
                Vector3 point = i == 0 ? towerPosition : CaptureRingGeometry.PointOnRing(towerPosition, radius, 0f, 45f * (i - 1));
                foreach (Terrain terrain in terrains)
                {
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    if (point.x < origin.x || point.x > origin.x + size.x || point.z < origin.z || point.z > origin.z + size.z)
                        continue;
                    best = Mathf.Max(best, origin.y + terrain.SampleHeight(point));
                }
            }
            return float.IsNegativeInfinity(best) ? towerPosition.y : best;
        }
    }
}
```

  4. Recompile. Expected: completed, `errors: []`. If an error names `captureBar*`, a reference was missed: grep for it
     and report it.

- [ ] **Step 4: Hand-edit `Assets/Gameplay/Config/UiTheme.asset`** (Rule 8). Replace lines 114-117 (the four
  `captureBar*` lines, between `capitalUnderAttackNoteBackingColor` and `overPowerActiveText`) with:

```yaml
  captureRingMaterial: {fileID: 2100000, guid: ad2e00cea264ef94fa08363867d004e5, type: 2}
  captureRingOutlineWidth: 0.15
  captureRingArcWidth: 0.45
  captureRingArcGap: 0.1
  captureRingHeightOffset: 0.06
  captureRingNeutralColor: {r: 1, g: 1, b: 1, a: 0.35}
  captureRingPausedBlinkSpeed: 0.7
  captureRingPausedOpacity: 0.6
  captureRingWarningColor: {r: 1, g: 0.2, b: 0.15, a: 1}
  captureRingPulseSpeed: 1.6
  captureRingSegments: 96
```

(`ad2e00cea264ef94fa08363867d004e5` is `Assets/Gameplay/UI/AimConeLine.mat`, the material `coneLineMaterial` already uses.)

Then reimport and read the values back:
`unity command eval -- --code "UnityEditor.AssetDatabase.ImportAsset(\"Assets/Gameplay/Config/UiTheme.asset\", UnityEditor.ImportAssetOptions.ForceUpdate); var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>(\"Assets/Gameplay/Config/UiTheme.asset\"); return t.captureRingMaterial.name + \" \" + t.captureRingSegments + \" \" + t.captureRingNeutralColor + \" \" + t.captureRingPausedOpacity;"`

Expected: `AimConeLine 96 RGBA(1.000, 1.000, 1.000, 0.350) 0.6`.

Check `git diff -- Assets/Gameplay/Config/UiTheme.asset`: 4 lines removed and 11 added, nothing else.

- [ ] **Step 5: Dirty check (False), then run tests async.** Expected: all pass, and the total equals Task 2's (this
  step adds no tests).

- [ ] **Step 6: Look at it in Play Mode (one client).**
  1. Dirty check False. `unity command editor_play`, then join as in Task 1 Step 7.2.
  2. Confirm the Game view size: `unity command eval -- --code "return UnityEngine.Screen.width + \"x\" + UnityEngine.Screen.height;"`.
     Expected `616x576`. If it differs, report it, and don't resize.
  3. Zoom the camera out for framing:
     `unity command eval -- --code "typeof(CameraTracking).GetField(\"currentZoom\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(CameraTracking.Instance, 1.8f); return CameraTracking.Instance.Yaw;"`
     Report the yaw.
  4. Ring report. Write `SCRATCH\ring-minimap\RingReport.cs`:

```csharp
var sb = new System.Text.StringBuilder();
foreach (var ring in UnityEngine.Object.FindObjectsByType<Overpower.Match.CaptureRingView>(FindObjectsSortMode.None))
{
    var tower = ring.GetComponentInParent<BuildingCapture>();
    var lines = ring.GetComponentsInChildren<LineRenderer>(true);
    Vector3 p0 = lines[0].GetPosition(0);
    float flat = new Vector2(p0.x - tower.transform.position.x, p0.z - tower.transform.position.z).magnitude;
    sb.AppendLine($"tower {tower.buildingID} radius={tower.captureRadius} edgePointDistance={flat:0.000} ringY={p0.y:0.000} towerY={tower.transform.position.y:0.000} lines={lines.Length} colliders={ring.GetComponentsInChildren<Collider>(true).Length} shadows={lines[0].shadowCastingMode} material={lines[0].sharedMaterial.name}");
}
sb.AppendLine("old bars left: " + GameObject.Find("Capture Progress Bar"));
return sb.ToString();
```

     Run `unity command eval_file -- --file "SCRATCH\ring-minimap\RingReport.cs" --timeout 20000`.
     Expected:
     - 10 rings.
     - `edgePointDistance` = radius − 0.075 (9.925, or 7.925 for tower 9).
     - `lines=2 colliders=0 shadows=Off material=AimConeLine`.
     - `old bars left: null`.
     - Report every `ringY`/`towerY` pair.
  5. Capture `unity command capture_game_view -- --source screen --save_path "Temp/ring-minimap/s3-own-capital.png"`,
     copy it to `SCRATCH\ring-minimap\captures\`, and look. Expected: the edge of your spawn capital in your team
     colour, no band, and no bar over the tower.
  6. Teleport to 2 m outside your T2's edge. This is the T4FromT2 loop from Task 1 with distance `radius + 2` from the
     T2 tower (look the T2 up with `m.Map.AdjacentTo(capital)[0]`). Capture `s3-neutral-t2.png`. Expected: a dim white
     edge. Say whether it reads clearly differently from a team edge.
  7. Teleport 5 m from the T2 centre, inside the zone. Wait about 5 s, then capture `s3-capturing.png`. Expected:
     - a band about a third full, in your team colour;
     - it starts at the top of the screen and runs clockwise;
     - it sits just inside the edge.
  8. `unity command editor_stop`; poll until stopped. Dirty check: False.

- [ ] **Step 7: Commit + push:**

```
git add Assets/scripts/CameraTracking.cs Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/Match/CaptureRingView.cs Assets/scripts/Match/CaptureRingView.cs.meta "Assets/scripts/Player/Building capture.cs"
git status   # CaptureProgressView.cs and its .meta show as deleted (staged by git rm)
git commit -m "feat(match): capture ring on the ground replaces the tower capture bar (ring/minimap step 3)" -m "Co-Authored-By: <your model line>"
git push
```

---

### Task 4 (ring/minimap step 4): pure minimap maths and link styles

**What exists:**
- **The camera.** It sits at `target + AngleAxis(yaw, up) * baseOffset` (`CameraTracking.cs:120`), with `baseOffset` (0, 10, -5), and looks at the player.
  - Screen-up on the ground is (sin yaw, cos yaw).
  - Screen-right is (cos yaw, −sin yaw).
- **UI rotation** is counter-clockwise-positive, and Unity yaw is clockwise seen from above. So turning the map image by +yaw puts world direction `yaw` at the top. The tests below pin that against `Quaternion.AngleAxis`.

**Files:**
- Create: `Assets/scripts/UI/MinimapLayout.cs`, `Assets/Tests/MinimapLayoutTests.cs`
- Create: `Assets/scripts/UI/MinimapLinkStyle.cs`, `Assets/Tests/MinimapLinkStyleTests.cs`

- [ ] **Step 1: Failing `MinimapLayoutTests`:**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapLayoutTests
    {
        private static readonly Vector2 Centre = new Vector2(65.05f, 53.34f); // ArenaSymmetry.centre (x, z)
        private const float WorldSize = 154f;
        private const float MapSize = 300f;

        private static void AssertClose(Vector2 expected, Vector2 actual, float tolerance = 1e-3f) =>
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(tolerance), $"expected {expected:F4} but was {actual:F4}");

        private static Vector3 World(float dx, float dz) => new Vector3(Centre.x + dx, 3f, Centre.y + dz);

        [Test]
        public void TheBakedCentreIsTheMiddleOfTheMap()
        {
            AssertClose(Vector2.zero, MinimapLayout.WorldToMap(World(0f, 0f), Centre, WorldSize, MapSize));
        }

        [Test]
        public void EastIsRightAndNorthIsUpBeforeTheMapTurns()
        {
            AssertClose(new Vector2(150f, 0f), MinimapLayout.WorldToMap(World(77f, 0f), Centre, WorldSize, MapSize));
            AssertClose(new Vector2(0f, 150f), MinimapLayout.WorldToMap(World(0f, 77f), Centre, WorldSize, MapSize));
        }

        [Test]
        public void OnlyPointsInsideTheBakedSquareCountAsInside()
        {
            Assert.IsTrue(MinimapLayout.IsInsideBakedArea(World(77f, -77f), Centre, WorldSize));
            Assert.IsFalse(MinimapLayout.IsInsideBakedArea(World(77.1f, 0f), Centre, WorldSize));
            Assert.IsFalse(MinimapLayout.IsInsideBakedArea(World(0f, -77.1f), Centre, WorldSize));
        }

        [Test]
        public void TurningPutsTheCamerasUpDirectionAtTheTop()
        {
            // A camera turned 90° looks toward +X: +X is at the top of the screen and +Z on the left.
            AssertClose(new Vector2(0f, 150f), MinimapLayout.TurnWithCamera(new Vector2(150f, 0f), 90f));
            AssertClose(new Vector2(-150f, 0f), MinimapLayout.TurnWithCamera(new Vector2(0f, 150f), 90f));
        }

        [Test]
        public void TurningMatchesWhatTheCameraShows()
        {
            const float yaw = 37f;
            Vector3 offset = new Vector3(12f, 0f, -31f);
            Vector3 screenRight = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
            Vector3 screenUp = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
            float scale = MapSize / WorldSize;
            var expected = new Vector2(Vector3.Dot(offset, screenRight), Vector3.Dot(offset, screenUp)) * scale;

            Vector2 map = MinimapLayout.WorldToMap(World(offset.x, offset.z), Centre, WorldSize, MapSize);
            AssertClose(expected, MinimapLayout.TurnWithCamera(map, yaw));
        }

        [Test]
        public void EachTeamSeesItsOwnCapitalInTheSamePlace()
        {
            // Capitals sit 57.66 m from the centre at map angles 90/210/330 (arena symmetry). CameraTracking.ResolveTeamYaw
            // turns each team's camera to atan2(toSpawn) + Team Yaw Offset (120 in Game Scene).
            const float radius = 57.66f;
            var expected = new Vector2(-0.8660254f, -0.5f) * (radius * MapSize / WorldSize); // 120° counter-clockwise from up
            foreach (float mapAngle in new[] { 90f, 210f, 330f })
            {
                float rad = mapAngle * Mathf.Deg2Rad;
                Vector3 capital = World(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
                float yaw = Mathf.Atan2(capital.x - Centre.x, capital.z - Centre.y) * Mathf.Rad2Deg + 120f;
                Vector2 onScreen = MinimapLayout.TurnWithCamera(MinimapLayout.WorldToMap(capital, Centre, WorldSize, MapSize), yaw);
                AssertClose(expected, onScreen, 0.01f);
            }
        }

        [Test]
        public void LabelsAndRingsStayUprightWhateverTheYaw()
        {
            foreach (float yaw in new[] { 0f, 37f, 120f, 240f, -75f })
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, MinimapLayout.MapRotationDegrees(yaw) + MinimapLayout.UprightRotationDegrees(yaw)), 1e-3f);
        }

        [Test]
        public void YourMarkerPointsWhereYouFaceOnScreen()
        {
            const float cameraYaw = 37f;
            // Facing the camera's own direction points straight up.
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, MinimapLayout.MapRotationDegrees(cameraYaw) + MinimapLayout.FacingRotationDegrees(cameraYaw)), 1e-3f);
            // Facing 90° clockwise from it points right: a UI rotation of -90.
            Assert.AreEqual(-90f, Mathf.DeltaAngle(0f, MinimapLayout.MapRotationDegrees(cameraYaw) + MinimapLayout.FacingRotationDegrees(cameraYaw + 90f)), 1e-3f);
        }

        [Test]
        public void ASegmentHasItsMiddleLengthAndAngle()
        {
            var (centre, length, angle) = MinimapLayout.Segment(new Vector2(0f, 0f), new Vector2(0f, 10f));
            AssertClose(new Vector2(0f, 5f), centre);
            Assert.AreEqual(10f, length, 1e-4f);
            Assert.AreEqual(90f, angle, 1e-3f);
            Assert.AreEqual(180f, MinimapLayout.Segment(new Vector2(10f, 0f), Vector2.zero).AngleDegrees, 1e-3f);
        }

        [Test]
        public void AnArrowheadStopsShortOfTheZoneItPointsAt()
        {
            AssertClose(new Vector2(7f, 0f), MinimapLayout.PointBeforeEnd(Vector2.zero, new Vector2(10f, 0f), 3f));
            AssertClose(new Vector2(5f, 5f), MinimapLayout.PointBeforeEnd(new Vector2(5f, 5f), new Vector2(5f, 5f), 3f));
        }

        [Test]
        public void TheFramedSquareClearsTheArenaOnEverySide()
        {
            Assert.AreEqual(154.4f, MinimapLayout.FramedSizeMetres(73.2f, 4f), 1e-3f);
            Assert.AreEqual(8f, MinimapLayout.FramedSizeMetres(-1f, 4f), 1e-3f);
        }

        [Test]
        public void TheGddLinksMakeFifteenLinesWithNoDuplicates()
        {
            var map = new TerritoryMap(
                new List<(int, IEnumerable<int>)>
                {
                    (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                    (3, new[] { 0, 1, 9 }), (4, new[] { 1, 2, 9 }), (5, new[] { 0, 2, 9 }),
                    (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                    (9, new[] { 3, 4, 5, 0, 1, 2 }),
                },
                new List<(int, int)> { (6, 0), (7, 1), (8, 2) });

            List<(int A, int B)> pairs = MinimapLayout.LinkPairs(map, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            Assert.AreEqual(15, pairs.Count);
            CollectionAssert.Contains(pairs, (0, 9));
            CollectionAssert.Contains(pairs, (2, 9));
            CollectionAssert.DoesNotContain(pairs, (6, 9));
            foreach ((int a, int b) in pairs)
                Assert.Less(a, b);
        }

        [Test]
        public void TierLabelsAreRomanNumerals()
        {
            Assert.AreEqual("I", MinimapLayout.TierLabel(1));
            Assert.AreEqual("II", MinimapLayout.TierLabel(2));
            Assert.AreEqual("III", MinimapLayout.TierLabel(3));
            Assert.AreEqual("IV", MinimapLayout.TierLabel(4));
            Assert.AreEqual("5", MinimapLayout.TierLabel(5));
        }
    }
}
```

- [ ] **Step 2: Failing `MinimapLinkStyleTests`:**

```csharp
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    public class MinimapLinkStyleTests
    {
        [Test]
        public void BothEndsOwnedByOneTeamIsASolidLineInThatTeam()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(1, 1);
            Assert.AreEqual(MinimapLinkKind.Owned, s.Kind);
            Assert.AreEqual(1, s.Team);
        }

        [Test]
        public void OwnedToNeutralIsAWayInPointingAtTheNeutralZone()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(2, -1);
            Assert.AreEqual(MinimapLinkKind.WayIn, s.Kind);
            Assert.AreEqual(2, s.Team);
            Assert.IsTrue(s.TowardB);
        }

        [Test]
        public void NeutralToOwnedPointsTheOtherWay()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(-1, 0);
            Assert.AreEqual(MinimapLinkKind.WayIn, s.Kind);
            Assert.AreEqual(0, s.Team);
            Assert.IsFalse(s.TowardB);
        }

        [Test]
        public void TwoNeutralEndsAreAThinGreyLine()
        {
            Assert.AreEqual(MinimapLinkKind.Neutral, MinimapLinkStyle.For(-1, -1).Kind);
            Assert.AreEqual(-1, MinimapLinkStyle.For(-1, -1).Team);
        }

        [Test]
        public void EndsOwnedByDifferentTeamsAreAThinGreyLine()
        {
            // [C] "One end owned by team X and the other not" is read as "the other end is neutral"; a border between
            // two teams falls under "otherwise".
            Assert.AreEqual(MinimapLinkKind.Neutral, MinimapLinkStyle.For(0, 1).Kind);
        }
    }
}
```

- [ ] **Step 3: Recompile. Confirm the failing state.** Expected: errors naming `MinimapLayout`, `MinimapLinkStyle` and
  `MinimapLinkKind` only.

- [ ] **Step 4: `MinimapLayout`.** Create `Assets/scripts/UI/MinimapLayout.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using Overpower.Match;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap's maths, kept pure so it's tested: where a world point lands on the map, how the map turns with
    /// the camera, and the shapes of links.
    ///
    /// MAP SPACE is canvas units with (0, 0) at the map's centre, +x = world +X (east) and +y = world +Z (north): the
    /// baked image's own orientation. MinimapView turns the whole map by the camera's team yaw, so "up" on the map is
    /// "up" on screen, and turns labels and markers back so they stay upright.
    ///
    /// Why +yaw: Unity yaw turns clockwise seen from above, and a UI rotation turns counter-clockwise, so turning
    /// the image by the same number brings world direction "yaw" to the top.
    /// </summary>
    public static class MinimapLayout
    {
        /// <summary>A world point's map-space position. The baked image covers a square of worldSizeMetres centred on
        /// worldCentreXZ (world x, z), drawn mapSize canvas units across.</summary>
        public static Vector2 WorldToMap(Vector3 world, Vector2 worldCentreXZ, float worldSizeMetres, float mapSize)
        {
            if (worldSizeMetres <= 0f)
                return Vector2.zero;
            float scale = mapSize / worldSizeMetres;
            return new Vector2((world.x - worldCentreXZ.x) * scale, (world.z - worldCentreXZ.y) * scale);
        }

        public static bool IsInsideBakedArea(Vector3 world, Vector2 worldCentreXZ, float worldSizeMetres)
        {
            float half = worldSizeMetres / 2f;
            return Mathf.Abs(world.x - worldCentreXZ.x) <= half && Mathf.Abs(world.z - worldCentreXZ.y) <= half;
        }

        /// <summary>The map's UI rotation (z degrees) for the camera's yaw.</summary>
        public static float MapRotationDegrees(float cameraYawDegrees) => cameraYawDegrees;

        /// <summary>The UI rotation that turns a label or progress ring inside the turned map back to upright.</summary>
        public static float UprightRotationDegrees(float cameraYawDegrees) => -cameraYawDegrees;

        /// <summary>Your marker's UI rotation inside the (turned) map for the way you face (Unity yaw). A marker drawn
        /// pointing up then points where you face on screen.</summary>
        public static float FacingRotationDegrees(float facingYawDegrees) => -facingYawDegrees;

        /// <summary>Where a map-space point ends up once the map is turned by the camera's yaw: what the player sees.</summary>
        public static Vector2 TurnWithCamera(Vector2 mapPoint, float cameraYawDegrees)
        {
            float radians = cameraYawDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(mapPoint.x * cos - mapPoint.y * sin, mapPoint.x * sin + mapPoint.y * cos);
        }

        /// <summary>A straight line drawn as a stretched image: its middle, length and UI rotation.</summary>
        public static (Vector2 Centre, float Length, float AngleDegrees) Segment(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return ((from + to) / 2f, delta.magnitude, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        /// <summary>The point <paramref name="distanceFromEnd"/> back from <paramref name="to"/> toward
        /// <paramref name="from"/>: where an arrowhead sits so it touches the edge of the zone it points at.</summary>
        public static Vector2 PointBeforeEnd(Vector2 from, Vector2 to, float distanceFromEnd)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length <= 0.0001f)
                return to;
            return to - delta / length * Mathf.Min(distanceFromEnd, length);
        }

        /// <summary>The side of the baked square: the arena's farthest point from its centre, plus a margin, on every
        /// side. Centred on the arena's symmetry centre, so turning the map to any team's yaw keeps the whole arena
        /// inside the round minimap.</summary>
        public static float FramedSizeMetres(float arenaRadiusMetres, float marginMetres) =>
            2f * (Mathf.Max(0f, arenaRadiusMetres) + Mathf.Max(0f, marginMetres));

        /// <summary>The bubble label for a tier, as in the GDD (I = capital ... IV = centre).</summary>
        public static string TierLabel(int tier) => tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            _ => tier.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Every adjacent pair of the given zones once, lowest id first, sorted: the lines the map draws.</summary>
        public static List<(int A, int B)> LinkPairs(TerritoryMap map, IEnumerable<int> zones)
        {
            var known = new HashSet<int>(zones);
            var sorted = new List<int>(known);
            sorted.Sort();
            var pairs = new List<(int A, int B)>();
            foreach (int a in sorted)
                foreach (int b in map.AdjacentTo(a))
                    if (b > a && known.Contains(b))
                        pairs.Add((a, b));
            return pairs;
        }
    }
}
```

- [ ] **Step 5: `MinimapLinkStyle`.** Create `Assets/scripts/UI/MinimapLinkStyle.cs`:

```csharp
namespace Overpower.UI
{
    public enum MinimapLinkKind
    {
        /// <summary>A thin grey line: nobody's way in (both neutral, or two different teams' zones).</summary>
        Neutral,
        /// <summary>A solid line in the team's colour: the team owns both ends.</summary>
        Owned,
        /// <summary>A line with an arrowhead in the team's colour, from its zone toward the neutral one: a way in (GDD p.27).</summary>
        WayIn,
    }

    /// <summary>How the minimap draws the link between two adjacent zones, from their owners only (spec 2026-09-16,
    /// "Links"). Pure, so the rule is tested.</summary>
    public readonly struct MinimapLinkStyle
    {
        public readonly MinimapLinkKind Kind;
        /// <summary>The team whose colour the line takes; -1 for Neutral.</summary>
        public readonly int Team;
        /// <summary>WayIn only: true when the arrow points from zone A toward zone B.</summary>
        public readonly bool TowardB;

        public MinimapLinkStyle(MinimapLinkKind kind, int team, bool towardB)
        {
            Kind = kind;
            Team = team;
            TowardB = towardB;
        }

        /// <param name="ownerA">Owner of zone A, -1 = neutral.</param>
        /// <param name="ownerB">Owner of zone B, -1 = neutral.</param>
        public static MinimapLinkStyle For(int ownerA, int ownerB)
        {
            if (ownerA >= 0 && ownerA == ownerB)
                return new MinimapLinkStyle(MinimapLinkKind.Owned, ownerA, false);
            if (ownerA >= 0 && ownerB < 0)
                return new MinimapLinkStyle(MinimapLinkKind.WayIn, ownerA, true);
            if (ownerB >= 0 && ownerA < 0)
                return new MinimapLinkStyle(MinimapLinkKind.WayIn, ownerB, false);
            return new MinimapLinkStyle(MinimapLinkKind.Neutral, -1, false);
        }
    }
}
```

- [ ] **Step 6: Recompile, dirty check (False), tests async.** Expected: all pass. The total is Task 3's + 18 (13 layout
  + 5 link style).

- [ ] **Step 7: Commit + push:**

```
git add Assets/scripts/UI/MinimapLayout.cs Assets/scripts/UI/MinimapLayout.cs.meta Assets/Tests/MinimapLayoutTests.cs Assets/Tests/MinimapLayoutTests.cs.meta Assets/scripts/UI/MinimapLinkStyle.cs Assets/scripts/UI/MinimapLinkStyle.cs.meta Assets/Tests/MinimapLinkStyleTests.cs Assets/Tests/MinimapLinkStyleTests.cs.meta
git status
git commit -m "feat(ui): minimap layout maths and link style rule (ring/minimap step 4)" -m "Co-Authored-By: <your model line>"
git push
```

Add `[C] a link between two different teams' zones is a thin grey line` to the assumptions file.

---

### Task 5 (ring/minimap step 5): Bake minimap image, `MinimapConfig`, Rebuild thirds bakes too

**What exists:**
- **The render approach.** `ArenaReportRender.Render` (`Assets/scripts/Editor/Telemetry/ArenaReportRender.cs:52-121`) is the proven one: a HideAndDontSave orthographic camera, rendered once and destroyed. Game Scene isn't dirtied (measured in the arena and telemetry work). **Don't edit that file:** the telemetry agent is working in that folder.
- **The arena tool.** `ArenaSymmetry` (`Assets/scripts/Arena/ArenaSymmetry.cs`) names `centre`, `source`, `generated120` and `generated240`.
- **Where the buttons live.** `ArenaSymmetryInspector` (`Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs:13-35`) holds the Rebuild thirds button and the `OverPower/Arena/Rebuild thirds` menu item.
- **The Editor assembly.** It is `Overpower.Editor`, namespace `Overpower.EditorTools` (`Assets/scripts/Editor/Overpower.Editor.asmdef`). The test asmdef references it.
- **Data-class convention** (`Assets/scripts/Data/TerritoryConfig.cs`): `[SerializeField] private` fields with read-only properties. The baker writes them through `SerializedObject`.

**Files:**
- Create: `Assets/scripts/Data/MinimapConfig.cs`
- Create: `Assets/scripts/Editor/Arena/TopDownRender.cs`, `Assets/scripts/Editor/Arena/MinimapBaker.cs`
- Modify: `Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs`
- Create: `Assets/Tests/MinimapBakerTests.cs`
- Created by the bake: `Assets/Gameplay/Config/MinimapConfig.asset`, `Assets/Gameplay/UI/ArenaMinimap.png` (+ `.meta` files)

- [ ] **Step 1: Failing `MinimapBakerTests`.** Create `Assets/Tests/MinimapBakerTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// The bake frames the minimap on the arena's farthest renderer from its symmetry centre. This builds a tiny arena
    /// in a PREVIEW scene: a preview scene never marks Game Scene dirty, and a dirty Game Scene hangs the Editor behind a
    /// modal save dialog (same reason as ArenaSymmetryBuilderTests).
    /// </summary>
    public class MinimapBakerTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject Make(string name, Transform parent = null)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        [Test]
        public void ArenaRadiusIsTheFarthestMeshCornerFromTheCentreAcrossAllThreeThirds()
        {
            var arena = Make("Arena").AddComponent<ArenaSymmetry>();
            arena.centre = new Vector3(10f, 0f, 20f);
            arena.source = Make("Source", arena.transform).transform;
            arena.generated120 = Make("Generated 120", arena.transform).transform;
            arena.generated240 = Make("Generated 240", arena.transform).transform;

            GameObject near = Make("Near crate", arena.source);
            near.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            near.AddComponent<MeshRenderer>();
            near.transform.position = new Vector3(15f, 0f, 20f);

            GameObject far = Make("Far wall", arena.generated240);
            far.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            far.AddComponent<MeshRenderer>();
            far.transform.position = new Vector3(40f, 0f, 20f); // a 1 m cube: far corners 30.5 m along X, 0.5 m along Z

            float radius = MinimapBaker.ArenaRadius(arena, out string farthest);

            Assert.AreEqual(Mathf.Sqrt(30.5f * 30.5f + 0.5f * 0.5f), radius, 1e-3f);
            Assert.AreEqual("Far wall", farthest);
        }
    }
}
```

- [ ] **Step 2: Recompile. Confirm the failing state.** Expected: errors naming `MinimapBaker` only.

- [ ] **Step 3: `MinimapConfig`.** Create `Assets/scripts/Data/MinimapConfig.cs`:

```csharp
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The picture under the minimap and the patch of the world it shows.
    ///
    /// HOW IT STAYS UP TO DATE: pressing Rebuild thirds on Enviorment/Arena re-bakes it automatically. After any
    /// other change you can see from above (terrain paint, lighting, props outside Source), use
    /// OverPower > Arena > Bake minimap image. The bake writes the image and the three "written by the bake" values;
    /// don't type those by hand, or the map's bubbles won't line up with the picture.
    ///
    /// Fields are [SerializeField] private with read-only properties, like the rest of the Data folder.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Minimap Config", fileName = "MinimapConfig")]
    public sealed class MinimapConfig : ScriptableObject
    {
        [Header("Written by the bake - don't edit")]
        [Tooltip("Top-down picture of the arena drawn under the minimap. Rebuild thirds re-bakes it; for any other change " +
                 "you can see from above, use OverPower > Arena > Bake minimap image.")]
        [SerializeField] private Texture2D arenaImage;

        [Tooltip("World X and Z at the centre of the picture: the arena's symmetry centre. Written by the bake.")]
        [SerializeField] private Vector2 worldCentre = new Vector2(65.05f, 53.34f);

        [Tooltip("How many metres the picture's side covers. Written by the bake.")]
        [SerializeField, Min(1f)] private float worldSizeMetres = 154f;

        [Header("Bake settings")]
        [Tooltip("Pixels along each side of the baked picture. 1024 stays sharp on the large map; more costs memory " +
                 "for no visible gain at minimap sizes. Press Bake minimap image after changing it.")]
        [SerializeField, Range(256, 2048)] private int imagePixels = 1024;

        [Tooltip("Extra metres of ground shown beyond the arena's farthest wall, on every side. Press Bake minimap image " +
                 "after changing it.")]
        [SerializeField, Min(0f)] private float marginMetres = 4f;

        public Texture2D ArenaImage => arenaImage;
        public Vector2 WorldCentre => worldCentre;
        public float WorldSizeMetres => worldSizeMetres;
        public int ImagePixels => imagePixels;
        public float MarginMetres => marginMetres;
    }
}
```

- [ ] **Step 4: `TopDownRender`.** Create `Assets/scripts/Editor/Arena/TopDownRender.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Renders the open scene straight down onto a square PNG: the same approach as ArenaReportRender (telemetry
    /// heatmaps), for any centre and size. The camera is HideAndDontSave and destroyed straight after, so the scene
    /// is never marked dirty. Image up = world +Z, right = world +X.
    /// </summary>
    public static class TopDownRender
    {
        public static byte[] RenderPng(Vector2 centreXZ, float spanMetres, int pixels)
        {
            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                go = EditorUtility.CreateGameObjectWithHideFlags("TopDownRenderCam", HideFlags.HideAndDontSave, typeof(Camera));
                var cam = go.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = spanMetres / 2f;
                cam.transform.SetPositionAndRotation(new Vector3(centreXZ.x, 150f, centreXZ.y), Quaternion.Euler(90f, 0f, 0f));
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;

                rt = new RenderTexture(pixels, pixels, 24);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(pixels, pixels, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                return ImageConversion.EncodeToPNG(tex);
            }
            finally
            {
                if (rt != null)
                {
                    Camera cam = go != null ? go.GetComponent<Camera>() : null;
                    if (cam != null) cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
```

- [ ] **Step 5: `MinimapBaker`.** Create `Assets/scripts/Editor/Arena/MinimapBaker.cs`:

```csharp
using System.IO;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// OverPower > Arena > Bake minimap image: renders the arena from above into Assets/Gameplay/UI/ArenaMinimap.png
    /// and records the world square it covers on Assets/Gameplay/Config/MinimapConfig.asset (created if missing).
    ///
    /// Framing: a square centred on ArenaSymmetry's centre, whose side clears the farthest mesh under Source and both
    /// generated thirds by the config's margin. Centring on the symmetry centre (not the bounding box) means turning
    /// the map to any team's camera yaw keeps the arena inside the round minimap, and all three teams see the same map.
    ///
    /// Rebuild thirds calls this too (ArenaSymmetryInspector), so the image can't go stale after an arena edit. It
    /// never touches the scene. It saves only the config asset, never SaveAssets(), which would also write any other
    /// dirty asset.
    /// </summary>
    public static class MinimapBaker
    {
        public const string ImagePath = "Assets/Gameplay/UI/ArenaMinimap.png";
        public const string ConfigPath = "Assets/Gameplay/Config/MinimapConfig.asset";

        [MenuItem("OverPower/Arena/Bake minimap image")]
        private static void BakeFromMenu() => Debug.Log("[Minimap] " + BakeOpenScene());

        /// <summary>Bakes the one ArenaSymmetry in the open scenes. Returns a one-line report (what the menu logs).</summary>
        public static string BakeOpenScene()
        {
            ArenaSymmetry[] arenas = UnityEngine.Object.FindObjectsByType<ArenaSymmetry>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (arenas.Length != 1)
                return $"not baked: found {arenas.Length} ArenaSymmetry components in the open scenes (need exactly one).";
            return Bake(arenas[0]);
        }

        public static string Bake(ArenaSymmetry arena)
        {
            if (arena == null)
                return "not baked: no ArenaSymmetry given.";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "not baked: stop Play Mode first (the picture would include players and capture rings).";
            if (EditorSceneManager.IsPreviewScene(arena.gameObject.scene))
                return "not baked: this arena is in a preview scene.";
            if (arena.source == null || arena.generated120 == null || arena.generated240 == null)
                return "not baked: Source, Generated 120 and Generated 240 must all be assigned on ArenaSymmetry.";

            MinimapConfig config = LoadOrCreateConfig();
            float radius = ArenaRadius(arena, out string farthest);
            if (radius <= 0f)
                return "not baked: there are no meshes under Source or the generated thirds.";

            float size = MinimapLayout.FramedSizeMetres(radius, config.MarginMetres);
            var centre = new Vector2(arena.centre.x, arena.centre.z);
            int pixels = Mathf.Clamp(config.ImagePixels, 256, 2048);

            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ImagePath);
            File.WriteAllBytes(fullPath, TopDownRender.RenderPng(centre, size, pixels));
            AssetDatabase.ImportAsset(ImagePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ConfigureImporter(pixels);

            var so = new SerializedObject(config);
            so.FindProperty("arenaImage").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath);
            so.FindProperty("worldCentre").vector2Value = centre;
            so.FindProperty("worldSizeMetres").floatValue = size;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);

            return $"baked {ImagePath} ({pixels} px) covering {size:0.00} m centred on ({centre.x:0.00}, {centre.y:0.00}); " +
                   $"arena radius {radius:0.00} m set by '{farthest}'.";
        }

        /// <summary>The farthest flat distance from the arena centre to any corner of a MeshRenderer's bounds under
        /// Source or a generated third, and that object's name. Meshes only: particles and lines have loose bounds.</summary>
        public static float ArenaRadius(ArenaSymmetry arena, out string farthestName)
        {
            float best = 0f;
            farthestName = "";
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                if (third == null) continue;
                foreach (MeshRenderer renderer in third.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Bounds bounds = renderer.bounds;
                    foreach (float x in new[] { bounds.min.x, bounds.max.x })
                        foreach (float z in new[] { bounds.min.z, bounds.max.z })
                        {
                            float distance = new Vector2(x - arena.centre.x, z - arena.centre.z).magnitude;
                            if (distance > best)
                            {
                                best = distance;
                                farthestName = renderer.name;
                            }
                        }
                }
            }
            return best;
        }

        private static MinimapConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<MinimapConfig>(ConfigPath);
            if (config != null)
                return config;
            config = ScriptableObject.CreateInstance<MinimapConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        // A plain colour picture, sampled smaller on the corner map: clamp so the edges don't wrap, mipmaps so it stays
        // smooth when shrunk.
        private static void ConfigureImporter(int pixels)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
            if (importer == null)
                return;
            bool changed = importer.textureType != TextureImporterType.Default
                           || importer.wrapMode != TextureWrapMode.Clamp
                           || !importer.mipmapEnabled
                           || importer.maxTextureSize < pixels;
            if (!changed)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, pixels);
            importer.SaveAndReimport();
        }
    }
}
```

- [ ] **Step 6: Rebuild thirds also bakes.** In `ArenaSymmetryInspector.cs`:
  - Change the HelpBox text (lines 16-17) to:
    `"Edit only the objects under Source, then press Rebuild thirds, check the arena, and save the scene. The two generated thirds are rebuilt from Source every time, so edits made to them are thrown away. Rebuild thirds also re-bakes the minimap image; for other changes you can see from above, use OverPower > Arena > Bake minimap image."`
  - Replace the Rebuild button block (lines 23-24) with:

```csharp
            if (GUILayout.Button("Rebuild thirds"))
            {
                Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
                // Here and in the menu item, not inside ArenaSymmetryBuilder.Rebuild: its tests rebuild tiny arenas in
                // preview scenes and must not overwrite the real minimap image.
                Debug.Log("[Minimap] " + MinimapBaker.Bake(arena));
            }
```

  - Replace `RebuildFromMenu`'s body (lines 32-34) with:

```csharp
            ArenaSymmetry arena = FindArena();
            if (arena == null)
                return;
            Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
            Debug.Log("[Minimap] " + MinimapBaker.Bake(arena));
```

- [ ] **Step 7: Recompile, dirty check, tests.** Expected: all pass, total = Task 4's + 1. Then dirty check again
  (expect False): the test must not have touched Game Scene.

- [ ] **Step 8: Bake.**
  1. Dirty check False, and not in Play Mode.
  2. Run `unity command eval -- --code "return Overpower.EditorTools.MinimapBaker.BakeOpenScene();" --timeout 60000`.
  3. Expected:
     - `baked Assets/Gameplay/UI/ArenaMinimap.png (1024 px) covering <size> m centred on (65.05, 53.34); arena radius <r> m set by '<name>'`;
     - `r` between 70 and 80 (the capital pockets' back corners are about 73.2 m out, arena-symmetry plan);
     - `<name>` is a wall piece.
     - If `r` is outside 70-80, or the farthest object isn't a wall, report the name and don't commit: a stray mesh would shrink the whole map.
  4. Dirty check: False.
  5. Copy `Assets/Gameplay/UI/ArenaMinimap.png` to `SCRATCH\ring-minimap\captures\s5-minimap-image.png` and look:
     - the whole arena, all three capital pockets, towers visible, centred;
     - north (+Z) up: the Source third's capital (tower 8) at the top.
  6. Read the config back:
     `unity command eval -- --code "var c = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.Data.MinimapConfig>(\"Assets/Gameplay/Config/MinimapConfig.asset\"); return c.ArenaImage.name + \" \" + c.WorldCentre + \" \" + c.WorldSizeMetres;"`
  7. `git status`: only the new PNG, the config asset and their `.meta` files are new, besides this task's scripts.
     `GameplayConfig.asset` and `UiTheme.asset` are unchanged.

- [ ] **Step 9: Rebuild thirds bakes too.**
  1. Note the PNG's write time: `(Get-Item Assets/Gameplay/UI/ArenaMinimap.png).LastWriteTime`.
  2. Run `unity command eval -- --code "return UnityEditor.EditorApplication.ExecuteMenuItem(\"OverPower/Arena/Rebuild thirds\");" --timeout 60000`.
  3. Expected: `True`, a console line `[Minimap] baked ...` (`unity command get_console_logs`), and a newer PNG write time.
  4. Rebuild marks the scene dirty on purpose. **Discard it; don't save:**
     `unity command eval -- --code "UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\"Assets/Scenes/Game Scene.unity\", UnityEditor.SceneManagement.OpenSceneMode.Single); return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"`
     Expected: `False`.
  5. Run `git diff --stat -- Assets/Gameplay/UI/ArenaMinimap.png Assets/Gameplay/Config/MinimapConfig.asset`. A byte-level
     PNG change is fine; the config's numbers must match Step 8.
  6. Run tests async once more: `ArenaSymmetrySceneTests` passes.

- [ ] **Step 10: Commit + push:**

```
git add Assets/scripts/Data/MinimapConfig.cs Assets/scripts/Data/MinimapConfig.cs.meta Assets/scripts/Editor/Arena/TopDownRender.cs Assets/scripts/Editor/Arena/TopDownRender.cs.meta Assets/scripts/Editor/Arena/MinimapBaker.cs Assets/scripts/Editor/Arena/MinimapBaker.cs.meta Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs Assets/Tests/MinimapBakerTests.cs Assets/Tests/MinimapBakerTests.cs.meta Assets/Gameplay/Config/MinimapConfig.asset Assets/Gameplay/Config/MinimapConfig.asset.meta Assets/Gameplay/UI/ArenaMinimap.png Assets/Gameplay/UI/ArenaMinimap.png.meta
git status
git commit -m "feat(arena): Bake minimap image, MinimapConfig, and Rebuild thirds re-bakes it (ring/minimap step 5)" -m "Co-Authored-By: <your model line>"
git push
```

---

### Task 6 (ring/minimap step 6): the minimap view

**What exists:**
- **Code-built owner UI.** `PlayerHud` (`Assets/scripts/UI/PlayerHud.cs`) sets the pattern:
  - `Awake` bails for a non-owner (154-161);
  - the canvas is built in `BuildUi` (654-679): overlay, `overrideSorting`, sortingOrder −10, a `CanvasScaler` from `theme.referenceResolution`/`matchWidthOrHeight`, **no GraphicRaycaster**;
  - labels come from `AddLabel`/`ApplyOutline` (1202-1241).
- **The loadout screen** (`Assets/scripts/UI/LoadoutScreen.cs`):
  - static `IsOpen` (120), public `Open` (448), `Close` (467), `Toggle` (490);
  - it subscribes `inputRouter.ShopToggled += Toggle` (330);
  - it claims tool focus while open (460).
- **The router.** `PlayerInputRouter` raises `MapToggled` through `Emit` (168), which `InputSuppressed` gates (92, including tool focus). Nothing subscribes today (74-78). The "pointer over UI" gate is `EventSystem.IsPointerOverGameObject` (219), which only sees raycast-target Graphics on canvases that carry a `GraphicRaycaster`.
- **`BuildingManager`** keeps registered towers in a private `captures` dictionary (32). It has no public zone position yet.
- **Players.** `PlayerLookup.GetPhotonViewFor(actor)` (`Assets/scripts/Player/PlayerLookup.cs:18-22`) and `PlayerLifecycle.AliveKey` (`PlayerLifecycle.cs:35`).
- **The prefab.** `Assets/Resources/Multiplayer Player.prefab` carries `PlayerHud` and `LoadoutScreen`. `NetworkPrefabObservablesTests` guards its PhotonView. `MinimapView` is not an `IPunObservable`.

**Files:**
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`
- Create: `Assets/scripts/UI/GeneratedSprites.cs`, `Assets/scripts/UI/MinimapView.cs`
- Modify: `Assets/scripts/BuildingManager.cs`, `Assets/scripts/Player/PlayerInputRouter.cs`
- Modify: `Assets/Resources/Multiplayer Player.prefab` (via script)
- Scratch: `SCRATCH\ring-minimap\AddMinimapToPlayer.cs`, `SCRATCH\ring-minimap\MinimapCheck.cs`

- [ ] **Step 1: Theme fields in C#.** In `UiTheme.cs`, directly after the `captureRingSegments` field (the end of the
  Capture ring block), add:

```csharp
        [Header("Minimap (2026-09-16)")]
        [Tooltip("Diameter of the round minimap in the top-right corner, in canvas units.")]
        public float minimapCornerSize = 340f;
        [Tooltip("Gap between the corner minimap's frame and the top and right screen edges, in canvas units.")]
        public float minimapCornerMargin = 24f;
        [Tooltip("Diameter of the large map shown in the middle of the screen while M is toggled on, in canvas units. " +
                 "Everything on it (bubbles, lines, labels) scales up from the corner sizes by the same amount.")]
        public float minimapLargeSize = 860f;
        [Tooltip("Width of the dark ring framing the minimap, in canvas units.")]
        public float minimapFrameWidth = 5f;
        [Tooltip("Colour of the minimap's frame (and its background if the baked arena image is missing).")]
        public Color minimapFrameColor = new Color(0.06f, 0.06f, 0.08f, 0.9f);
        [Tooltip("Tint and opacity of the baked arena picture under the minimap. Lower the alpha or darken it so the " +
                 "bubbles and lines stand out more.")]
        public Color minimapBackgroundTint = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("Bubble diameter of a Tier 1 zone (capital) on the corner minimap, in canvas units. The GDD draws " +
                 "capitals largest.")]
        public float minimapBubbleDiameterTier1 = 36f;
        [Tooltip("Bubble diameter of a Tier 2 zone on the corner minimap, in canvas units (small in the GDD).")]
        public float minimapBubbleDiameterTier2 = 24f;
        [Tooltip("Bubble diameter of a Tier 3 zone on the corner minimap, in canvas units (small in the GDD).")]
        public float minimapBubbleDiameterTier3 = 24f;
        [Tooltip("Bubble diameter of the Tier 4 centre zone on the corner minimap, in canvas units (medium in the GDD).")]
        public float minimapBubbleDiameterTier4 = 30f;
        [Tooltip("Width of the dark outline around each zone bubble, in canvas units. It pulses to Capture Ring Warning " +
                 "Colour while the zone is under attack.")]
        public float minimapBubbleOutlineWidth = 3f;
        [Tooltip("Colour of a zone bubble's outline when nothing is attacking it.")]
        public Color minimapBubbleOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        [Tooltip("Fill of a neutral zone's bubble, and colour of a link nobody owns.")]
        public Color minimapNeutralColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        [Tooltip("Font size of the I / II / III / IV label on each bubble, in canvas units.")]
        public float minimapLabelSize = 16f;
        [Tooltip("Thickness of the capture progress ring around a bubble, in canvas units. It fills and blinks like " +
                 "the ring on the ground.")]
        public float minimapProgressRingWidth = 4f;
        [Tooltip("Width of a link one team owns both ends of, or a way-in link with its arrowhead, in canvas units.")]
        public float minimapOwnedLinkWidth = 4f;
        [Tooltip("Width of a link nobody owns (a thin grey line), in canvas units.")]
        public float minimapNeutralLinkWidth = 2f;
        [Tooltip("Size of a way-in arrowhead, in canvas units. It points from a team's zone toward the neutral zone next to it.")]
        public float minimapArrowheadSize = 12f;
        [Tooltip("Size of your own arrow on the minimap, in canvas units. It points where you face.")]
        public float minimapOwnMarkerSize = 18f;
        [Tooltip("Colour of your own arrow on the minimap.")]
        public Color minimapOwnMarkerColor = new Color(1f, 0.85f, 0.25f, 1f);
        [Tooltip("Diameter of a teammate's dot on the minimap, in canvas units. Enemies aren't shown.")]
        public float minimapTeammateDotSize = 10f;
        [Tooltip("Colour of a teammate's dot on the minimap.")]
        public Color minimapTeammateDotColor = new Color(0.45f, 1f, 0.45f, 1f);

        /// <summary>The corner-map bubble diameter for a zone tier (1 capital ... 4 centre).</summary>
        public float MinimapBubbleDiameter(int tier) => tier switch
        {
            1 => minimapBubbleDiameterTier1,
            3 => minimapBubbleDiameterTier3,
            4 => minimapBubbleDiameterTier4,
            _ => minimapBubbleDiameterTier2,
        };
```

Recompile (expect clean). Then hand-edit `UiTheme.asset` (Rule 8): directly after the `captureRingSegments: 96` line,
insert:

```yaml
  minimapCornerSize: 340
  minimapCornerMargin: 24
  minimapLargeSize: 860
  minimapFrameWidth: 5
  minimapFrameColor: {r: 0.06, g: 0.06, b: 0.08, a: 0.9}
  minimapBackgroundTint: {r: 1, g: 1, b: 1, a: 0.9}
  minimapBubbleDiameterTier1: 36
  minimapBubbleDiameterTier2: 24
  minimapBubbleDiameterTier3: 24
  minimapBubbleDiameterTier4: 30
  minimapBubbleOutlineWidth: 3
  minimapBubbleOutlineColor: {r: 0, g: 0, b: 0, a: 0.85}
  minimapNeutralColor: {r: 0.55, g: 0.55, b: 0.55, a: 1}
  minimapLabelSize: 16
  minimapProgressRingWidth: 4
  minimapOwnedLinkWidth: 4
  minimapNeutralLinkWidth: 2
  minimapArrowheadSize: 12
  minimapOwnMarkerSize: 18
  minimapOwnMarkerColor: {r: 1, g: 0.85, b: 0.25, a: 1}
  minimapTeammateDotSize: 10
  minimapTeammateDotColor: {r: 0.45, g: 1, b: 0.45, a: 1}
```

Reimport and read back:
`unity command eval -- --code "UnityEditor.AssetDatabase.ImportAsset(\"Assets/Gameplay/Config/UiTheme.asset\", UnityEditor.ImportAssetOptions.ForceUpdate); var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>(\"Assets/Gameplay/Config/UiTheme.asset\"); return t.minimapCornerSize + \" \" + t.MinimapBubbleDiameter(4) + \" \" + t.minimapTeammateDotColor;"`
Expected: `340 30 RGBA(0.450, 1.000, 0.450, 1.000)`.

Check `git diff -- Assets/Gameplay/Config/UiTheme.asset`: exactly 22 added lines.

- [ ] **Step 2: `BuildingManager.TryGetZoneCentre`.** Add to `Assets/scripts/BuildingManager.cs`, directly after
  `TryGetZoneAt` (ends at line 229):

```csharp
    /// <summary>The world position of a zone's tower, its capture centre. False while that tower hasn't registered
    /// yet (RegisterCapture runs in BuildingCapture.Start). The minimap places its bubbles from this.</summary>
    public bool TryGetZoneCentre(int zone, out Vector3 centre)
    {
        if (captures.TryGetValue(zone, out BuildingCapture capture) && capture != null)
        {
            centre = capture.transform.position;
            return true;
        }
        centre = default;
        return false;
    }
```

- [ ] **Step 3: M gets its own gate.** In `Assets/scripts/Player/PlayerInputRouter.cs`:
  - Replace the comment above the events (lines 74-77) with:
    `// Scoreboard still has nothing subscribed. Shop (LoadoutScreen) and Map (MinimapView) each have their own, narrower emit gate - see ShopSuppressed and MapSuppressed.`
  - Under `ShopSuppressed` (line 99), add:

```csharp
    /// <summary>MapToggled's gate: the same as ShopSuppressed, and for the same reason. The loadout screen holds tool
    /// focus while open, and M must still work then, because opening the large map closes the P screen (capture ring
    /// + minimap spec, 2026-09-16). The map is only something to look at; it claims no focus of its own.</summary>
    private bool MapSuppressed => !isAlive || IsTypingInChat();
```

  - In `Awake` (line 168), change `mapAction.started += _ => Emit(MapToggled);` to `mapAction.started += _ => EmitMap();`.
  - Under `EmitShop` (ends at line 248), add:

```csharp
    /// <summary>MapToggled's own emit path - see MapSuppressed.</summary>
    private void EmitMap()
    {
        if (!MapSuppressed)
            MapToggled?.Invoke();
    }
```

- [ ] **Step 4: `GeneratedSprites`.** Create `Assets/scripts/UI/GeneratedSprites.cs`:

```csharp
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Two plain white shapes the minimap draws with - a disc and an upward triangle - generated once in code, so
    /// there's no sprite asset to keep in sync, and each is tinted by its Image's colour.
    ///
    /// A sprite is required, not decoration: a Filled Image without one ignores its fill amount and draws full (a
    /// bug this project has hit before). The capture progress ring is a Filled disc. The edges are anti-aliased over
    /// one pixel, so shapes stay smooth when drawn small.
    /// </summary>
    public static class GeneratedSprites
    {
        private const int Size = 128;
        private static Sprite disc;
        private static Sprite triangle;

        public static Sprite Disc => disc != null ? disc : (disc = Build("Generated Disc", DiscAlpha));
        public static Sprite Triangle => triangle != null ? triangle : (triangle = Build("Generated Triangle", TriangleAlpha));

        private static Sprite Build(string name, System.Func<float, float, float> alphaAt)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f))));
            texture.SetPixels32(pixels);
            texture.Apply(false, true); // no longer readable: frees the CPU copy

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static float DiscAlpha(float x, float y)
        {
            float half = Size / 2f;
            float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
            return (half - 1f) - distance + 0.5f;
        }

        // Apex at the top centre, base along the bottom: points up (+y) before any rotation.
        private static float TriangleAlpha(float x, float y)
        {
            var p = new Vector2(x, y);
            var left = new Vector2(2f, 2f);
            var right = new Vector2(Size - 2f, 2f);
            var apex = new Vector2(Size / 2f, Size - 2f);
            float inside = Mathf.Min(EdgeDistance(p, left, right), Mathf.Min(EdgeDistance(p, right, apex), EdgeDistance(p, apex, left)));
            return inside + 0.5f;
        }

        // Signed distance from p to the line a->b, positive on the inside of a counter-clockwise triangle.
        private static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            return (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / edge.magnitude;
        }
    }
}
```

- [ ] **Step 5: `MinimapView`.** Create `Assets/scripts/UI/MinimapView.cs`:

```csharp
using System.Collections.Generic;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap (Tudor, 2026-09-16; it looks like GDD p.27): a round map in the top-right corner, and a large one
    /// in the middle of the screen while M is toggled on. What it shows:
    /// - a baked top-down picture of the arena (MinimapConfig);
    /// - a bubble per zone, sized by tier, filled in the owner's colour and labelled I-IV;
    /// - the links between zones (see MinimapLinkStyle): solid when one team owns both ends, an arrowhead when a
    ///   team owns one end and the other is neutral (a way in);
    /// - each zone's capture progress as a ring around its bubble, matching the ring on the ground;
    /// - a pulsing outline on zones under attack;
    /// - your own arrow and your teammates' dots. Enemies aren't shown: there are no vision rules to decide who
    ///   may see whom.
    ///
    /// OWNER ONLY, built in code like PlayerHud (see its class comment for why code-built). Everything comes from state
    /// every client already has: BuildingManager (owners, capture progress, links), ZonePresenceTracker (under attack)
    /// and the replicated player positions.
    ///
    /// NEVER BLOCKS A SHOT: its canvas has no GraphicRaycaster and no Graphic is a raycast target, so
    /// PlayerInputRouter's "pointer over UI" check never sees it.
    ///
    /// TURNS WITH YOUR CAMERA: the map turns by CameraTracking's team yaw, so "up" on the map is "up" on screen. Labels,
    /// progress rings and markers are turned back so they read upright (maths and tests: MinimapLayout).
    ///
    /// COST: bubbles and lines are built once. Bubble and link colours change only when ownership changes
    /// (BuildingManager.OwnershipChanged, plus the first territory read, which raises no event). Each zone's ring and
    /// outline are worked out every frame from CaptureRingState, but written to the UI only when they change, so an
    /// idle zone touches nothing. Player markers move every frame. Nothing here allocates per frame.
    ///
    /// M and P: opening the large map closes the loadout screen, and opening the loadout screen closes the large map.
    ///
    /// PHASES (2.7): call MinimapView.Local.SetZoneShown(zone, false) for each zone taken out of play; it hides the
    /// bubble and every link to it.
    /// </summary>
    public sealed class MinimapView : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Sizes, colours and widths for the minimap (UiTheme > Minimap). The team colours, " +
                 "warning colour and paused blink come from the Shots and Capture ring sections, shared with the rings " +
                 "on the ground.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The baked arena picture and the world square it covers. Rebuild thirds re-bakes it, " +
                 "or use OverPower > Arena > Bake minimap image.")]
        private MinimapConfig config;

        /// <summary>The local player's minimap, or null before it spawns.</summary>
        public static MinimapView Local { get; private set; }

        public bool IsBuilt => built;
        public bool IsLargeOpen => largeOpen;
        /// <summary>The map's root, for harness checks (its screen rectangle).</summary>
        public RectTransform MapRoot => root;
        public int ZoneBubbleCount => zones.Count;
        public int LinkCount => links.Count;

        private sealed class ZoneUi
        {
            public int Zone;
            public float Diameter;
            public Vector2 MapPosition;
            public RectTransform Upright;
            public Image Ring;
            public Image Outline;
            public Image Fill;
            public bool Shown = true;
            public float ShownRingFill = -1f;
            public Color ShownRingColor;
            public Color ShownOutlineColor;
        }

        private sealed class LinkUi
        {
            public int A;
            public int B;
            public Image Line;
            public Image Arrow;
        }

        private readonly List<ZoneUi> zones = new List<ZoneUi>();
        private readonly Dictionary<int, ZoneUi> zoneById = new Dictionary<int, ZoneUi>();
        private readonly List<LinkUi> links = new List<LinkUi>();
        private readonly List<RectTransform> teammateDots = new List<RectTransform>();
        private readonly HashSet<int> hiddenZones = new HashSet<int>();

        private PlayerInputRouter inputRouter;
        private LoadoutScreen loadoutScreen;
        private PlayerLifecycle lifecycle;
        private BuildingManager manager;

        private RectTransform root;
        private RectTransform map;
        private RectTransform linksLayer;
        private RectTransform zonesLayer;
        private RectTransform teammatesLayer;
        private RectTransform ownMarker;
        private Material textMaterial;

        private bool built;
        private bool largeOpen;
        private bool ownershipDirty = true;
        private float appliedYaw = float.NaN;

        private void Awake()
        {
            // Every remote copy stays dormant, like PlayerHud: nobody needs another player's minimap.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }
            if (theme == null || config == null)
            {
                Debug.LogError($"[Minimap] {name}: UiTheme or MinimapConfig is not assigned - the minimap is not built.");
                enabled = false;
                return;
            }
            if (config.ArenaImage == null)
                Debug.LogError("[Minimap] MinimapConfig has no arena image - press OverPower > Arena > Bake minimap image. " +
                               "The minimap draws zones on a plain background meanwhile.");

            inputRouter = GetComponent<PlayerInputRouter>();
            loadoutScreen = GetComponent<LoadoutScreen>();
            lifecycle = GetComponent<PlayerLifecycle>();
            Local = this;
            if (inputRouter != null)
                inputRouter.MapToggled += ToggleLarge;
        }

        private void OnDestroy()
        {
            if (inputRouter != null)
                inputRouter.MapToggled -= ToggleLarge;
            if (manager != null)
                manager.OwnershipChanged -= HandleOwnershipChanged;
            if (Local == this)
                Local = null;
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        /// <summary>M: open the large map (closing the loadout screen), or close it.</summary>
        public void ToggleLarge()
        {
            if (!built)
                return;
            if (largeOpen)
            {
                SetLarge(false);
                return;
            }
            if (loadoutScreen != null && LoadoutScreen.IsOpen)
                loadoutScreen.Close();
            SetLarge(true);
        }

        /// <summary>2.7 hook: hide (or show again) a zone's bubble and every link to it. Remembered if called before the
        /// map is built.</summary>
        public void SetZoneShown(int zone, bool shown)
        {
            if (shown) hiddenZones.Remove(zone);
            else hiddenZones.Add(zone);

            if (!built || !zoneById.TryGetValue(zone, out ZoneUi ui) || ui.Shown == shown)
                return;
            ui.Shown = shown;
            ui.Upright.gameObject.SetActive(shown);
            ownershipDirty = true; // re-applies which links show, next LateUpdate
        }

        private void LateUpdate()
        {
            if (!built && !TryBuild())
                return;

            // The P screen opened (by P or by its button): the large map closes.
            if (largeOpen && LoadoutScreen.IsOpen)
                SetLarge(false);

            ApplyYawIfChanged();

            if (ownershipDirty && manager.Current != null)
            {
                RecolourOwnership();
                ownershipDirty = false;
            }

            UpdateZones();
            UpdatePlayers();
        }

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot) =>
            ownershipDirty = true;

        // ---------------------------------------------------------------- build (once)

        private bool TryBuild()
        {
            manager = BuildingManager.Instance;
            if (manager == null || manager.Map == null || manager.TowerDictionary == null || manager.TowerDictionary.Count == 0)
                return false;
            // Towers register in their own Start; wait until every zone has, so no bubble is missing.
            foreach (int zone in manager.TowerDictionary.Keys)
                if (manager.TierOf(zone) <= 0 || !manager.TryGetZoneCentre(zone, out _))
                    return false;

            BuildFrame();
            var zoneIds = new List<int>(manager.TowerDictionary.Keys);
            zoneIds.Sort();
            foreach (int zone in zoneIds)
                BuildZone(zone);
            foreach ((int a, int b) in MinimapLayout.LinkPairs(manager.Map, zoneIds))
                BuildLink(a, b);

            ownMarker = BuildMarker("You", map, GeneratedSprites.Triangle, theme.minimapOwnMarkerColor, theme.minimapOwnMarkerSize);

            manager.OwnershipChanged += HandleOwnershipChanged;
            built = true;
            SetLarge(false);
            return true;
        }

        private void BuildFrame()
        {
            var canvasGo = new GameObject("Minimap Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD (-10), below the loadout screen (-5), MatchUI's panels (0) and the F1 test panel (500).
            canvas.overrideSorting = true;
            canvas.sortingOrder = -9;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster, on purpose: see the class comment. Adding one would make every shot fired with the
            // cursor over the map silently fail.

            root = NewRect("Minimap", canvasGo.transform);
            root.sizeDelta = Vector2.one * theme.minimapCornerSize;

            NewImage("Frame", root, GeneratedSprites.Disc, theme.minimapFrameColor, theme.minimapCornerSize + 2f * theme.minimapFrameWidth);

            Image viewport = NewImage("Viewport", root, GeneratedSprites.Disc, Color.white, theme.minimapCornerSize);
            // Round mask: the turned square picture never shows its corners.
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            map = NewRect("Map", viewport.transform);
            map.sizeDelta = Vector2.one * theme.minimapCornerSize;

            var pictureGo = new GameObject("Arena Picture", typeof(RectTransform));
            pictureGo.transform.SetParent(map, false);
            RawImage picture = pictureGo.AddComponent<RawImage>();
            picture.texture = config.ArenaImage;
            picture.color = config.ArenaImage != null ? theme.minimapBackgroundTint : theme.minimapFrameColor;
            picture.raycastTarget = false;
            Stretch(picture.rectTransform);

            // Sibling order is draw order: lines under bubbles, bubbles under player markers.
            linksLayer = NewLayer("Links", map);
            zonesLayer = NewLayer("Zones", map);
            teammatesLayer = NewLayer("Teammates", map);
        }

        private void BuildZone(int zone)
        {
            manager.TryGetZoneCentre(zone, out Vector3 centre);
            int tier = manager.TierOf(zone);
            var ui = new ZoneUi { Zone = zone, Diameter = theme.MinimapBubbleDiameter(tier) };
            ui.MapPosition = MinimapLayout.WorldToMap(centre, config.WorldCentre, config.WorldSizeMetres, theme.minimapCornerSize);

            ui.Upright = NewRect($"Zone {zone}", zonesLayer);
            ui.Upright.anchoredPosition = ui.MapPosition;
            ui.Upright.sizeDelta = Vector2.one * ui.Diameter;

            // The progress ring is a Filled disc behind the outline disc, so exactly Progress Ring Width shows around it
            // at every bubble size. Sprite first: a Filled Image without a sprite ignores its fill amount.
            float ringDiameter = ui.Diameter + 2f * (theme.minimapBubbleOutlineWidth + theme.minimapProgressRingWidth);
            ui.Ring = NewImage("Progress Ring", ui.Upright, GeneratedSprites.Disc, Color.white, ringDiameter);
            ui.Ring.type = Image.Type.Filled;
            ui.Ring.fillMethod = Image.FillMethod.Radial360;
            ui.Ring.fillOrigin = (int)Image.Origin360.Top; // the top of the screen, like the band on the ground
            ui.Ring.fillClockwise = true;
            ui.Ring.fillAmount = 0f;
            ui.Ring.enabled = false;

            ui.Outline = NewImage("Outline", ui.Upright, GeneratedSprites.Disc, theme.minimapBubbleOutlineColor,
                                  ui.Diameter + 2f * theme.minimapBubbleOutlineWidth);
            ui.ShownOutlineColor = theme.minimapBubbleOutlineColor;
            ui.Fill = NewImage("Fill", ui.Upright, GeneratedSprites.Disc, theme.minimapNeutralColor, ui.Diameter);
            AddLabel(ui.Upright, MinimapLayout.TierLabel(tier), theme.minimapLabelSize);

            ui.Shown = !hiddenZones.Contains(zone);
            ui.Upright.gameObject.SetActive(ui.Shown);
            zones.Add(ui);
            zoneById[zone] = ui;
        }

        private void BuildLink(int a, int b)
        {
            var link = new LinkUi { A = a, B = b };
            link.Line = NewImage($"Link {a}-{b}", linksLayer, null, theme.minimapNeutralColor, 0f);
            (Vector2 centre, float length, float angle) = MinimapLayout.Segment(zoneById[a].MapPosition, zoneById[b].MapPosition);
            link.Line.rectTransform.anchoredPosition = centre;
            link.Line.rectTransform.sizeDelta = new Vector2(length, theme.minimapNeutralLinkWidth);
            link.Line.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
            link.Arrow = NewImage($"Arrow {a}-{b}", linksLayer, GeneratedSprites.Triangle, theme.minimapNeutralColor, theme.minimapArrowheadSize);
            link.Arrow.gameObject.SetActive(false);
            links.Add(link);
        }

        // ---------------------------------------------------------------- per-frame updates

        private void SetLarge(bool open)
        {
            largeOpen = open;
            Vector2 anchor = open ? new Vector2(0.5f, 0.5f) : Vector2.one;
            root.anchorMin = anchor;
            root.anchorMax = anchor;
            root.pivot = anchor;
            float inset = theme.minimapCornerMargin + theme.minimapFrameWidth;
            root.anchoredPosition = open ? Vector2.zero : new Vector2(-inset, -inset);
            // One hierarchy for both views: the large map is the corner map scaled up, so every size scales together.
            root.localScale = Vector3.one * (open ? theme.minimapLargeSize / Mathf.Max(1f, theme.minimapCornerSize) : 1f);
        }

        private void ApplyYawIfChanged()
        {
            float yaw = CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f;
            if (Mathf.Approximately(yaw, appliedYaw))
                return;
            appliedYaw = yaw;
            map.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.MapRotationDegrees(yaw));
            var upright = Quaternion.Euler(0f, 0f, MinimapLayout.UprightRotationDegrees(yaw));
            foreach (ZoneUi zone in zones)
                zone.Upright.localRotation = upright;
        }

        private void RecolourOwnership()
        {
            TerritorySnapshot snapshot = manager.Current;
            foreach (ZoneUi zone in zones)
            {
                int owner = snapshot.OwnerOf(zone.Zone);
                zone.Fill.color = owner >= 0 ? theme.ShotColorFor(owner) : theme.minimapNeutralColor;
            }
            foreach (LinkUi link in links)
                ApplyLinkStyle(link, MinimapLinkStyle.For(snapshot.OwnerOf(link.A), snapshot.OwnerOf(link.B)));
        }

        private void ApplyLinkStyle(LinkUi link, MinimapLinkStyle style)
        {
            ZoneUi a = zoneById[link.A];
            ZoneUi b = zoneById[link.B];
            bool shown = a.Shown && b.Shown;
            link.Line.gameObject.SetActive(shown);
            bool arrow = shown && style.Kind == MinimapLinkKind.WayIn;
            link.Arrow.gameObject.SetActive(arrow);
            if (!shown)
                return;

            bool neutral = style.Kind == MinimapLinkKind.Neutral;
            Color colour = neutral ? theme.minimapNeutralColor : theme.ShotColorFor(style.Team);
            link.Line.color = colour;
            RectTransform line = link.Line.rectTransform;
            line.sizeDelta = new Vector2(line.sizeDelta.x, neutral ? theme.minimapNeutralLinkWidth : theme.minimapOwnedLinkWidth);

            if (!arrow)
                return;
            ZoneUi from = style.TowardB ? a : b;
            ZoneUi to = style.TowardB ? b : a;
            // Just outside the target's bubble, outline and progress ring, pointing at it.
            float stop = to.Diameter / 2f + theme.minimapBubbleOutlineWidth + theme.minimapProgressRingWidth + theme.minimapArrowheadSize / 2f;
            RectTransform arrowRect = link.Arrow.rectTransform;
            arrowRect.anchoredPosition = MinimapLayout.PointBeforeEnd(from.MapPosition, to.MapPosition, stop);
            // The triangle sprite points up (+y); a segment's angle is measured from +x.
            arrowRect.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.Segment(from.MapPosition, to.MapPosition).AngleDegrees - 90f);
            link.Arrow.color = colour;
        }

        private void UpdateZones()
        {
            int nowMs = PhotonNetwork.ServerTimestamp;
            float time = Time.unscaledTime;
            float pulse = CaptureRingGeometry.Pulse01(time, theme.captureRingPulseSpeed);
            float blink = CaptureRingGeometry.Blink01(time, theme.captureRingPausedBlinkSpeed);
            ZonePresenceTracker presence = ZonePresenceTracker.Instance;
            TerritorySnapshot snapshot = manager.Current;

            foreach (ZoneUi zone in zones)
            {
                if (!zone.Shown)
                    continue;

                int owner = snapshot != null ? snapshot.OwnerOf(zone.Zone) : TerritoryMap.Neutral;
                bool attacked = presence != null && presence.IsUnderAttack(zone.Zone);
                CaptureRingState state = CaptureRingState.From(manager.CaptureProgressOf(zone.Zone), owner, attacked, nowMs);

                Color outline = state.UnderAttack
                    ? Color.Lerp(theme.minimapBubbleOutlineColor, theme.captureRingWarningColor, pulse)
                    : theme.minimapBubbleOutlineColor;
                if (outline != zone.ShownOutlineColor)
                {
                    zone.Outline.color = outline;
                    zone.ShownOutlineColor = outline;
                }

                bool ringShown = state.ShowsArc;
                if (zone.Ring.enabled != ringShown)
                    zone.Ring.enabled = ringShown;
                if (!ringShown)
                    continue;

                if (Mathf.Abs(state.Fill01 - zone.ShownRingFill) > 0.001f)
                {
                    zone.Ring.fillAmount = state.Fill01;
                    zone.ShownRingFill = state.Fill01;
                }
                Color ring = theme.ShotColorFor(state.ArcTeam);
                if (state.Phase == CaptureRingPhase.Paused)
                    ring.a *= theme.captureRingPausedOpacity * blink;
                if (ring != zone.ShownRingColor)
                {
                    zone.Ring.color = ring;
                    zone.ShownRingColor = ring;
                }
            }
        }

        private void UpdatePlayers()
        {
            bool alive = lifecycle == null || lifecycle.IsAlive;
            if (ownMarker.gameObject.activeSelf != alive)
                ownMarker.gameObject.SetActive(alive);
            if (alive)
            {
                ownMarker.anchoredPosition = MinimapLayout.WorldToMap(transform.position, config.WorldCentre, config.WorldSizeMetres, theme.minimapCornerSize);
                ownMarker.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.FacingRotationDegrees(transform.eulerAngles.y));
            }

            int used = 0;
            Room room = PhotonNetwork.CurrentRoom;
            if (room != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam))
            {
                // Room.Players is a Dictionary: its enumerator is a struct, so this allocates nothing (PhotonNetwork.PlayerList
                // would build a new sorted array every frame).
                foreach (KeyValuePair<int, Player> pair in room.Players)
                {
                    Player player = pair.Value;
                    if (player.IsLocal || !Teams.TryGetTeam(player, out int team) || team != myTeam)
                        continue;
                    if (player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool isAlive && !isAlive)
                        continue;
                    PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                    if (view == null)
                        continue;

                    RectTransform dot = DotAt(used++);
                    dot.anchoredPosition = MinimapLayout.WorldToMap(view.transform.position, config.WorldCentre, config.WorldSizeMetres, theme.minimapCornerSize);
                }
            }
            for (int i = used; i < teammateDots.Count; i++)
                if (teammateDots[i].gameObject.activeSelf)
                    teammateDots[i].gameObject.SetActive(false);
        }

        private RectTransform DotAt(int index)
        {
            while (teammateDots.Count <= index)
                teammateDots.Add(BuildMarker("Teammate", teammatesLayer, GeneratedSprites.Disc, theme.minimapTeammateDotColor, theme.minimapTeammateDotSize));
            RectTransform dot = teammateDots[index];
            if (!dot.gameObject.activeSelf)
                dot.gameObject.SetActive(true);
            return dot;
        }

        // ---------------------------------------------------------------- UI helpers

        /// <summary>A marker with a dark outline copy behind it, so it reads on any part of the picture.</summary>
        private RectTransform BuildMarker(string name, Transform parent, Sprite sprite, Color colour, float size)
        {
            RectTransform marker = NewRect(name, parent);
            marker.sizeDelta = Vector2.one * size;
            NewImage("Outline", marker, sprite, theme.minimapBubbleOutlineColor, size + 2f * theme.minimapBubbleOutlineWidth);
            NewImage("Fill", marker, sprite, colour, size);
            return marker;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private static RectTransform NewLayer(string name, Transform parent)
        {
            RectTransform layer = NewRect(name, parent);
            Stretch(layer);
            return layer;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color colour, float size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = Vector2.one * size;
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false; // never swallow a shot - see the class comment
            return image;
        }

        /// <summary>Same recipe as PlayerHud.AddLabel/ApplyOutline: one shared outline material for every label.</summary>
        private void AddLabel(Transform parent, string text, float fontSize)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = "Label";
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            if (theme.font != null)
                tmp.font = theme.font; // before touching the material: assigning a font resets it
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = theme.textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, theme.textOutlineWidth);
                textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, theme.textOutlineColor);
            }
            tmp.fontSharedMaterial = textMaterial;
            Stretch(tmp.rectTransform);
        }
    }
}
```

Notes for the implementer:
- `MapRoot`'s world corners are screen pixels, because the canvas is Screen Space Overlay. The harness uses this to aim the pointer at the minimap.
- `SetLarge` is called once from `TryBuild`, after `built = true`, so the corner layout applies before the first frame shows.

- [ ] **Step 6: Recompile, dirty check (False), tests.** Expected: compiles clean, all pass, total = Task 5's (no new tests).

- [ ] **Step 7: Put `MinimapView` on the player prefab.** Write `SCRATCH\ring-minimap\AddMinimapToPlayer.cs`:

```csharp
using UnityEditor;
using UnityEngine;

public static class AddMinimapToPlayer
{
    private const string PrefabPath = "Assets/Resources/Multiplayer Player.prefab";

    public static string Run()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            return "ABORT: scene is dirty";

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (root.GetComponent<Overpower.UI.MinimapView>() == null)
            {
                var view = root.AddComponent<Overpower.UI.MinimapView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
                so.FindProperty("config").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Overpower.Data.MinimapConfig>("Assets/Gameplay/Config/MinimapConfig.asset");
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Overpower.UI.MinimapView>();
        if (saved == null) return "ERROR: MinimapView not on the saved prefab";
        var check = new SerializedObject(saved);
        return $"theme={check.FindProperty("theme").objectReferenceValue} config={check.FindProperty("config").objectReferenceValue}";
    }
}
```

Run `unity command --timeout 240 run_script -- --file "SCRATCH\ring-minimap\AddMinimapToPlayer.cs" --entry AddMinimapToPlayer.Run --timeout_ms 200000`.
- Expected: `theme=UiTheme (Overpower.UI.UiTheme) config=MinimapConfig (Overpower.Data.MinimapConfig)`.
- Run `git diff --stat -- "Assets/Resources/Multiplayer Player.prefab"`, then read the diff. Expected: one new
  `MonoBehaviour` block with `theme`/`config` references, plus one new component entry on the root GameObject.
- Anything else re-serialized: report it, and don't commit it without the controller's OK.
- `git status`: no other asset changed.
- Run tests async: `NetworkPrefabObservablesTests` passes.

- [ ] **Step 8: Look at it in Play Mode (one client).**
  1. Dirty check False → `editor_play` → join (Task 1 Step 7.2). Wait until the local player exists.
  2. Write `SCRATCH\ring-minimap\MinimapCheck.cs`:

```csharp
var mv = Overpower.UI.MinimapView.Local;
if (mv == null) return "no local MinimapView";
var canvas = mv.MapRoot != null ? mv.MapRoot.GetComponentInParent<Canvas>() : null;
int raycastTargets = 0;
if (canvas != null)
    foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        if (g.raycastTarget) raycastTargets++;
var corners = new Vector3[4];
mv.MapRoot.GetWorldCorners(corners);
Vector3 centre = (corners[0] + corners[2]) / 2f;
var es = UnityEngine.EventSystems.EventSystem.current;
var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
if (es != null)
    es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = centre }, hits);
return $"built={mv.IsBuilt} large={mv.IsLargeOpen} bubbles={mv.ZoneBubbleCount} links={mv.LinkCount} " +
       $"raycaster={(canvas != null && canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null)} raycastTargets={raycastTargets} " +
       $"screenCentre=({centre.x:0},{centre.y:0}) uiHitsThere={hits.Count} [{string.Join(",", hits.ConvertAll(h => h.gameObject.name))}] " +
       $"loadoutOpen={Overpower.UI.LoadoutScreen.IsOpen} yaw={(CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : float.NaN):0.0}";
```

     Run `unity command eval_file -- --file "SCRATCH\ring-minimap\MinimapCheck.cs" --timeout 20000`.
     Expected:
     - `built=True large=False bubbles=10 links=15 raycaster=False raycastTargets=0`;
     - `uiHitsThere=0`: nothing under the minimap's centre catches the pointer;
     - the centre sits in the top-right of the 616×576 view.
  3. Capture `unity command capture_game_view -- --source screen --save_path "Temp/ring-minimap/s6-corner.png"`, copy it
     to `SCRATCH\ring-minimap\captures\`, and look. Expected:
     - a round map in the top-right corner, clear of the HUD and chat;
     - the arena picture, turned so your capital is lower-left (Team Yaw Offset 120);
     - three capital bubbles in team colours, the other seven grey;
     - I/II/III/IV labels upright;
     - an arrowhead from each capital toward its T2, in that team's colour; other links thin grey;
     - your amber arrow at your spawn.
  4. Large map: `unity command eval -- --code "Overpower.UI.MinimapView.Local.ToggleLarge(); return Overpower.UI.MinimapView.Local.IsLargeOpen;"`
     → `True`. Capture `s6-large.png`. Expected: the same map, large and centred, and the corner empty.
  5. P closes M: `unity command eval -- --code "PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber).GetComponent<Overpower.UI.LoadoutScreen>().Open(); return Overpower.UI.LoadoutScreen.IsOpen;"`
     → `True`. Then run MinimapCheck again. Expected: `large=False loadoutOpen=True`.
  6. M closes P: `unity command eval -- --code "Overpower.UI.MinimapView.Local.ToggleLarge(); return Overpower.UI.MinimapView.Local.IsLargeOpen + \" \" + Overpower.UI.LoadoutScreen.IsOpen;"`
     → `True False`. Capture `s6-m-after-p.png`: the large map, no loadout screen. Close it with `ToggleLarge()` again.
  7. The real M key reaches the minimap through the router's new gate in Task 7 (R3), on the Player build.
  8. `unity command editor_stop`; poll until stopped. Dirty check: False.

- [ ] **Step 9: Commit + push:**

```
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/GeneratedSprites.cs Assets/scripts/UI/GeneratedSprites.cs.meta Assets/scripts/UI/MinimapView.cs Assets/scripts/UI/MinimapView.cs.meta Assets/scripts/BuildingManager.cs Assets/scripts/Player/PlayerInputRouter.cs "Assets/Resources/Multiplayer Player.prefab"
git status
git commit -m "feat(ui): corner minimap and large map on M, GDD p.27 style (ring/minimap step 6)" -m "Co-Authored-By: <your model line>"
git push
```

Add these [C] lines to the assumptions file:
- round minimap;
- M works while P is open;
- teammates only;
- the large map doesn't pause input.

---

### Task 7 (ring/minimap step 7): two-client verification

Read `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\two-client-harness.md` §3-§9 and §11 first.

**Roles:**
- A = the Editor, the master.
- B = the dev Player in `Builds/Client2`.

The zone ids come from Step 2's report. With A on team 0 and B on team 1:

| Zone | Id |
|---|---|
| A's capital (`capA`) | 6 |
| A's T2 (`t2A`) | 0 |
| B's capital (`capB`) | 7 |
| B's T2 (`t2B`) | 1 |
| The T3 between the two T2s (`t3AB`) | 3 |
| The centre (T4) | 9 |

**Files:** none committed, apart from a possible notes update (Step 14). Everything else is scratch in
`SCRATCH\ring-minimap\`, with captures in `SCRATCH\ring-minimap\captures\`.

**Templates.** Write these first, and fill the `__PLACEHOLDERS__` with PowerShell `-replace` into a new file for each
use.

`editor_capture_when_tpl.cs` (Editor Play Mode). It captures in-process the moment a condition holds, and writes what
it saw next to the PNG. The CLI is too slow to catch a 50% fill or a pulse peak.

```csharp
// __ZONE__ zone id; __OUT__ absolute .png path; __CONDITION__ C# bool over p, fill, owner, attacked, pulse, blink; __TIMEOUT__ seconds
var m = BuildingManager.Instance;
var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
System.Collections.IEnumerator Run()
{
    float giveUpAt = Time.realtimeSinceStartup + __TIMEOUT__f;
    while (Time.realtimeSinceStartup < giveUpAt)
    {
        Overpower.Match.CaptureProgress p = m.CaptureProgressOf(__ZONE__);
        float fill = p.Evaluate(Photon.Pun.PhotonNetwork.ServerTimestamp);
        int owner = m.Current != null ? m.Current.OwnerOf(__ZONE__) : -1;
        bool attacked = ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(__ZONE__);
        float pulse = Overpower.Match.CaptureRingGeometry.Pulse01(Time.unscaledTime, theme.captureRingPulseSpeed);
        float blink = Overpower.Match.CaptureRingGeometry.Blink01(Time.unscaledTime, theme.captureRingPausedBlinkSpeed);
        if (__CONDITION__)
        {
            ScreenCapture.CaptureScreenshot(@"__OUT__");
            System.IO.File.WriteAllText(@"__OUT__.txt",
                $"t={Time.time:0.00} team={p.Team} prog={p.Progress01:0.000} rate={p.RatePerSecond01:0.0000} fill={fill:0.000} owner={owner} attacked={attacked} pulse={pulse:0.00} blink={blink:0.00}");
            yield break;
        }
        yield return null;
    }
    System.IO.File.WriteAllText(@"__OUT__.txt", "TIMED OUT");
}
m.StartCoroutine(Run());
return "waiting";
```

- If the `.txt` exists but the PNG doesn't within 3 s, the in-process capture didn't render in the unfocused Editor.
- In that case, repeat the scenario, and when the `.txt` appears, run
  `unity command capture_game_view -- --source screen --save_path "Temp/ring-minimap/<name>.png"` right away.
- Report which method produced each image, plus the `.txt` values.

`editor_teleport_tpl.cs` (Editor). It moves A to a collider-free point `__DIST__` metres from zone `__ZONE__`'s centre:

```csharp
var m = BuildingManager.Instance;
m.TryGetZoneCentre(__ZONE__, out Vector3 c);
var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
var disp = me.GetComponent<PlayerDisplacement>();
for (int i = 0; i < 8; i++)
{
    float a = i * 45f * Mathf.Deg2Rad;
    Vector3 p = new Vector3(c.x + Mathf.Sin(a) * __DIST__f, me.transform.position.y, c.z + Mathf.Cos(a) * __DIST__f);
    if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
    return $"teleport {disp.TeleportTo(p)} to {p:F2}";
}
return "no free point";
```

`player_teleport_tpl.cs` (Player; reflection, `two-client-harness.md` §6). It does the same for B:

```csharp
var bmType = System.Type.GetType("BuildingManager, Overpower.Runtime");
var bm = bmType.GetProperty("Instance").GetValue(null);
object[] zoneArgs = { __ZONE__, null };
bmType.GetMethod("TryGetZoneCentre").Invoke(bm, zoneArgs);
var c = (UnityEngine.Vector3)zoneArgs[1];
var pn = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
var local = pn.GetProperty("LocalPlayer").GetValue(null);
int actor = (int)local.GetType().GetProperty("ActorNumber").GetValue(local);
var view = (UnityEngine.Component)System.Type.GetType("PlayerLookup, Overpower.Runtime").GetMethod("GetPhotonViewFor").Invoke(null, new object[] { actor });
var dispType = System.Type.GetType("PlayerDisplacement, Overpower.Runtime");
var disp = view.GetComponent(dispType);
for (int i = 0; i < 8; i++)
{
    float a = i * 45f * UnityEngine.Mathf.Deg2Rad;
    var p = new UnityEngine.Vector3(c.x + UnityEngine.Mathf.Sin(a) * __DIST__f, view.transform.position.y, c.z + UnityEngine.Mathf.Cos(a) * __DIST__f);
    if (UnityEngine.Physics.CheckCapsule(p + UnityEngine.Vector3.up * 0.8f, p + UnityEngine.Vector3.up * 1.6f, 0.6f, ~0, UnityEngine.QueryTriggerInteraction.Ignore)) continue;
    return "teleport " + dispType.GetMethod("TeleportTo").Invoke(disp, new object[] { p }) + " to " + p;
}
return "no free point";
```

`player_shots_tpl.cs` (Player). It takes `__COUNT__` screenshots `__GAP__` s apart, to catch a pulse peak on B's screen:

```csharp
var host = (UnityEngine.MonoBehaviour)UnityEngine.Object.FindObjectOfType(System.Type.GetType("BuildingManager, Overpower.Runtime"));
System.Collections.IEnumerator Shots()
{
    for (int i = 0; i < __COUNT__; i++)
    {
        UnityEngine.ScreenCapture.CaptureScreenshot(@"__OUT__-" + i + ".png");
        yield return new UnityEngine.WaitForSecondsRealtime(__GAP__f);
    }
}
host.StartCoroutine(Shots());
return "capturing";
```

If `UnityEngine.ScreenCapture` doesn't compile in Player eval, call it through
`System.Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule").GetMethod("CaptureScreenshot", new[] { typeof(string) }).Invoke(null, new object[] { path })`.

`player_progress_tpl.cs` (Player). It reads B's own copy of a zone's replicated progress:

```csharp
var bmType = System.Type.GetType("BuildingManager, Overpower.Runtime");
var bm = bmType.GetProperty("Instance").GetValue(null);
var p = bmType.GetMethod("CaptureProgressOf").Invoke(bm, new object[] { __ZONE__ });
var t = p.GetType();
return "team=" + t.GetField("Team").GetValue(p) + " prog=" + t.GetField("Progress01").GetValue(p) + " rate=" + t.GetField("RatePerSecond01").GetValue(p);
```

`editor_progress_recorder_tpl.cs` (Editor). It logs every change of zone `__ZONE__`'s team or rate, and of zone
`__LINK__`'s under-attack state, with game time, for `__SECONDS__` s:

```csharp
var m = BuildingManager.Instance;
System.Collections.IEnumerator Run()
{
    var sb = new System.Text.StringBuilder();
    int lastTeam = int.MinValue; float lastRate = float.NaN; bool lastAttacked = false; bool first = true;
    float end = Time.time + __SECONDS__f;
    while (Time.time < end)
    {
        var p = m.CaptureProgressOf(__ZONE__);
        bool attacked = ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(__LINK__);
        if (first || p.Team != lastTeam || p.RatePerSecond01 != lastRate || attacked != lastAttacked)
        {
            sb.AppendLine($"t={Time.time:0.00} zone __ZONE__ team={p.Team} prog={p.Progress01:0.000} rate={p.RatePerSecond01:0.0000} fill={p.Evaluate(Photon.Pun.PhotonNetwork.ServerTimestamp):0.000} | zone __LINK__ underAttack={attacked}");
            lastTeam = p.Team; lastRate = p.RatePerSecond01; lastAttacked = attacked; first = false;
        }
        yield return null;
    }
    System.IO.File.WriteAllText(@"__OUT__", sb.ToString());
}
m.StartCoroutine(Run());
return "recording";
```

`editor_solo_timing_tpl.cs` (Editor). It measures a solo capture in game time:

```csharp
var m = BuildingManager.Instance;
System.Collections.IEnumerator Run()
{
    float started = -1f, startRate = 0f, giveUpAt = Time.realtimeSinceStartup + 60f;
    while (Time.realtimeSinceStartup < giveUpAt)
    {
        var p = m.CaptureProgressOf(__ZONE__);
        if (started < 0f && p.Team == __TEAM__ && p.RatePerSecond01 > 0f) { started = Time.time; startRate = p.RatePerSecond01; }
        if (started >= 0f && m.Current != null && m.Current.OwnerOf(__ZONE__) == __TEAM__)
        {
            System.IO.File.WriteAllText(@"__OUT__", $"started={started:0.00} owned={Time.time:0.00} seconds={Time.time - started:0.00} rate={startRate:0.0000}");
            yield break;
        }
        yield return null;
    }
    System.IO.File.WriteAllText(@"__OUT__", $"TIMED OUT started={started:0.00}");
}
m.StartCoroutine(Run());
return "timing";
```

- [ ] **Step 1: Build and launch B.**
  1. Not in Play Mode; dirty check False. Run all tests async: they all pass, and the total matches Task 6.
  2. `unity command set_build_settings --settings '{"developmentBuild":true}' --confirm true`
  3. `unity command build --target StandaloneWindows64 --outputPath "Builds/Client2/OverPower.exe" --options '["Development"]' --confirm true`,
     then poll `unity command build_status` every 5-10 s until completed with `result: "Succeeded"`.
  4. `Remove-Item "Builds/Client2/.unity-pipeline-runtime-port" -ErrorAction SilentlyContinue`
  5. `Start-Process "Builds/Client2/OverPower.exe" -ArgumentList "-screen-fullscreen 0 -screen-width 616 -screen-height 576 -logFile Builds/Client2/player.log"`
  6. Poll `unity command --runtime-path "Builds/Client2" runtime_status` until it answers.

- [ ] **Step 2: Both in one room.**
  1. `editor_play`, then the Editor joins as in Task 1 Step 7.2. Read the room name:
     `unity command eval -- --code "return Photon.Pun.PhotonNetwork.CurrentRoom.Name;"`
  2. B joins that room by name with the reflection snippet in harness §5.
  3. Poll until both report `InRoom=True`, `PlayerCount=2`, and B's player exists.
  4. Zoom both cameras out to 1.8: Task 3 Step 6.3 on the Editor. On the Player, the same through reflection on
     `System.Type.GetType("CameraTracking, Overpower.Runtime")` (`Instance` property, private `currentZoom` field).
  5. Report teams, ids and room with this Editor eval:
     `var m = BuildingManager.Instance; var sb = new System.Text.StringBuilder(); foreach (var p in Photon.Pun.PhotonNetwork.PlayerList) { Overpower.Net.Teams.TryGetTeam(p, out int t); sb.Append($"actor {p.ActorNumber} team {t} capital {m.Map.CapitalOf(t)}; "); } return sb.ToString();`
     Fill the zone table above from it.
  6. Confirm the Editor's `UnityEngine.Screen.width x height` is 616x576.

- [ ] **Step 3: Capture 5, both corner minimaps at start.**
  - Editor: `unity command capture_game_view -- --source screen --save_path "Temp/ring-minimap/05-corner-start-A.png"`, then copy it to SCRATCH.
  - Player: `player_shots_tpl` with `__COUNT__` 1, `__GAP__` 0.1, `__OUT__` = `SCRATCH\ring-minimap\captures\05-corner-start-B`.
  - Expected on each:
    - the three capitals filled in team colours, everything else grey;
    - an arrowhead from each capital toward its own T2;
    - your arrow at your spawn;
    - your own capital lower-left of the map centre;
    - the two maps turned 120° relative to each other.
  - The teammate dot can't be shown with one player per team: say so. It is checked by reading only (Step 13).

- [ ] **Step 4: Capture 1, ground ring edges (A).**
  1. At A's spawn (inside `capA`), capture `01a-edge-own-capital.png` (`capture_game_view --source screen`). Expected: a
     team-coloured edge around the capital, no band.
  2. `editor_teleport_tpl` with zone `t2A`, distance `12` (just outside the 10 m radius). Wait 2 s, then capture
     `01b-edge-neutral.png`. Expected: a dim white edge.
  3. **Say plainly whether team 0's near-white edge and the neutral dim white edge are easy to tell apart.**

- [ ] **Step 5: Capture 2 and the solo T2 timing (R1: must still be 15 s).**
  1. Start `editor_solo_timing_tpl` with zone `t2A`, team A's team, and `__OUT__` = `SCRATCH\ring-minimap\r1-solo-t2.txt`.
  2. Start `editor_capture_when_tpl` with zone `t2A`, condition `p.RatePerSecond01 > 0f && fill >= 0.5f`, timeout 40,
     and `__OUT__` = `...\captures\02-capture-50.png`.
  3. `editor_teleport_tpl` with zone `t2A`, distance `5`.
  4. Wait 20 s. Read both `.txt` files and look at the PNG.
  5. Expected:
     - `seconds` between 14.7 and 15.4, with `rate=0.0667`;
     - the capture shows a band about half full, in A's colour, starting at the top of the screen and clockwise;
     - the corner minimap shows the same half ring around the T2 bubble.

- [ ] **Step 6: Capture 6, corner minimap after capturing the T2 (A).** Capture `06-corner-after-t2.png` at once.
  Expected:
  - the T2 bubble is in A's colour;
  - the `capA`–`t2A` link is solid in A's colour;
  - arrowheads in A's colour point from `t2A` toward `t3AB`, the other T3 next to it, and T4.

- [ ] **Step 7: R2, the T2 → T4 link and its under-attack block (the spec's Part 1 check, live).**
  1. On the Editor, hand B its own T2: `unity command eval -- --code "var m = BuildingManager.Instance; Overpower.Net.Teams.TryGetTeam(Photon.Pun.PhotonNetwork.PlayerList[1], out int tb); m.SetCaptured(<t2B>, tb, 0, 0); return tb;"`.
     Use the actor that is B.
  2. Start `editor_progress_recorder_tpl` with zone 9, link `t2A`, 40 seconds, and `__OUT__` = `SCRATCH\ring-minimap\r2-t4-link.txt`.
  3. `editor_teleport_tpl` with zone 9, distance 4. A now owns `capA` + `t2A` only.
  4. After about 4 s: `player_teleport_tpl` with zone `t2A`, distance 5. B stands in A's T2, so it is under attack.
  5. After about 6 s, read B's view: `player_progress_tpl` with zone 9.
  6. Then `player_teleport_tpl` with zone `t2B`, distance 5 (B leaves).
  7. Wait until the recorder file appears (40 s).
  8. Expected, in order:
     - zone 9 `rate=0.0667`, team A (the capture starts through T2);
     - `underAttack=True` on `t2A`;
     - zone 9 moves to a **rate 0 hold with progress > 0** within 0.5 s;
     - B's own read shows the same team and progress at rate 0;
     - `underAttack=False` about 3 s after B left;
     - rate back to `0.0667`.
  9. Move A out of zone 9 (`editor_teleport_tpl` zone `capA`, distance 5). The capture resets: expect team -1 in a later read.

- [ ] **Step 8: Capture 3, a paused capture with the enemy inside.**
  - B owns `t2B` (Step 7), so B may capture `t3AB` and counts as inside it.
  1. Start `editor_capture_when_tpl` with zone `t3AB`, condition `p.Team == <teamA> && p.RatePerSecond01 == 0f && p.Progress01 > 0.1f && blink > 0.85f`, timeout 40, and `__OUT__` = `...\captures\03-paused.png`.
  2. `editor_teleport_tpl` with zone `t3AB`, distance 5. A captures (10 s solo).
  3. After about 3 s: `player_teleport_tpl` with zone `t3AB`, distance 5. B enters.
  4. When `03-paused.png.txt` appears, read B's copy (`player_progress_tpl` zone `t3AB`), then look at the PNG.
  5. Expected:
     - `.txt` shows `team=A rate=0.0000 prog≈0.3`;
     - B reads the same;
     - the band stays at about 30% in A's colour, captured near a blink peak, dimmer than a moving band;
     - the minimap ring around the T3 bubble matches.
  6. A then leaves (`editor_teleport_tpl` zone `t2A`, distance 5). B, now alone, captures `t3AB` from 0: 10 s. Wait until
     `m.Current.OwnerOf(t3AB)` is B's team.

- [ ] **Step 9: Capture 4, a drain.**
  - A owns `t2A`, and B owns `t3AB`, which is next to it, so B may drain `t2A`.
  1. Move A outside `t2A`'s radius: `editor_teleport_tpl` zone `t2A`, distance 13. The camera still sees the ring.
  2. Start `editor_capture_when_tpl` with zone `t2A`, condition `p.RatePerSecond01 < 0f && fill < 0.75f && fill > 0.3f && pulse > 0.8f`, timeout 30, and `__OUT__` = `...\captures\04-drain.png`.
  3. `player_teleport_tpl` with zone `t2A`, distance 5.
  4. Expected:
     - `.txt` shows `team=B rate=-0.2000`, `owner=A`, `attacked=True`;
     - the band is in A's colour, part-empty and shrinking;
     - the edge is pulsing toward B's colour (near a peak);
     - the minimap bubble's outline pulses in the warning colour and its ring matches.
  5. Wait about 6 s: `t2A` goes neutral.

- [ ] **Step 10: Capture 8, a zone under attack on both screens.**
  1. `editor_teleport_tpl` with zone `capA`, distance 5 (A inside its own capital).
  2. Start `editor_capture_when_tpl` with zone `capA`, condition `attacked && pulse > 0.85f`, timeout 30, and `__OUT__` = `...\captures\08-under-attack-A.png`.
  3. `player_teleport_tpl` with zone `capA`, distance 6 (B inside A's capital).
  4. About 1 s after B arrives: `player_shots_tpl` with count 5, gap 0.12, and `__OUT__` = `...\captures\08-under-attack-B`.
  5. Expected on A:
     - A's capital edge pulses toward the warning colour, with no band (nothing drains: B's team may not capture A's
       capital while `t2A` is neutral);
     - the minimap's capital bubble outline is red.
  6. Expected on B: at least one of the 5 shots shows the same edge and bubble pulse. Name the shot.

- [ ] **Step 11: Capture 7, the large map on M, both teams.**
  1. Player, real key: `unity command --runtime-path "Builds/Client2" simulate_key -- --key M`, then check `IsLargeOpen`
     through reflection:
     `var t = System.Type.GetType("Overpower.UI.MinimapView, Overpower.Runtime"); var mv = t.GetProperty("Local").GetValue(null); return t.GetProperty("IsLargeOpen").GetValue(mv);`
     Expected: `True`. This proves the router's new MapToggled gate.
  2. `player_shots_tpl` with count 1 → `07-large-B`.
  3. Editor: `unity command eval -- --code "Overpower.UI.MinimapView.Local.ToggleLarge(); return Overpower.UI.MinimapView.Local.IsLargeOpen;"`.
     Capture `07-large-A.png` (`capture_game_view --source screen`).
  4. Expected on both:
     - a large centred map, with the corner empty;
     - **your own capital lower-left of the map centre, at the same screen spot on both** (Team Yaw Offset 120; see
       "Spec statements" item 2);
     - the maps are the same arena turned 120° against each other;
     - bubble colours and arrows match the territory as it stands;
     - labels upright.
  5. Close both: Player `simulate_key M` (then check `False`); Editor `ToggleLarge()`.

- [ ] **Step 12: R3, P and M never overlap (Player, real keys).**
  1. Run `simulate_key -- --key M`, and read `IsLargeOpen` and `LoadoutScreen.IsOpen` (static, through reflection on
     `Overpower.UI.LoadoutScreen, Overpower.Runtime`). Expected `True False`.
  2. `simulate_key -- --key P`, wait 1 s, read. Expected `False True`. Capture `r3-p-closes-m-B`.
  3. `simulate_key -- --key M`, read. Expected `True False`. Capture `r3-m-closes-p-B`.
  4. Look at both: exactly one of the two screens on each.
  5. `simulate_key -- --key M` to close.

- [ ] **Step 13: R4, shooting through the minimap area (Player, real pointer).**
  1. B stands in open ground, `player_teleport_tpl` zone `capB`, distance 6. Then run this Player eval (aim override,
     shot counter, minimap screen centre):

```csharp
var pn = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
var local = pn.GetProperty("LocalPlayer").GetValue(null);
int actor = (int)local.GetType().GetProperty("ActorNumber").GetValue(local);
var view = (UnityEngine.Component)System.Type.GetType("PlayerLookup, Overpower.Runtime").GetMethod("GetPhotonViewFor").Invoke(null, new object[] { actor });
var aimType = System.Type.GetType("PlayerAim, Overpower.Runtime");
aimType.GetMethod("SetAimOverride").Invoke(view.GetComponent(aimType), new object[] { (UnityEngine.Vector3?)(view.transform.position + view.transform.forward * 6f) });
var firingType = System.Type.GetType("Overpower.Weapons.WeaponFiring, Overpower.Runtime");
System.AppDomain.CurrentDomain.SetData("minimapShots", 0);
System.Action<int, int, bool> onFired = (a, b, c) => System.AppDomain.CurrentDomain.SetData("minimapShots", (int)System.AppDomain.CurrentDomain.GetData("minimapShots") + 1);
firingType.GetEvent("Fired").AddEventHandler(view.GetComponent(firingType), onFired);
var mvType = System.Type.GetType("Overpower.UI.MinimapView, Overpower.Runtime");
var rootRect = (UnityEngine.RectTransform)mvType.GetProperty("MapRoot").GetValue(mvType.GetProperty("Local").GetValue(null));
var corners = new UnityEngine.Vector3[4];
rootRect.GetWorldCorners(corners);
var centre = (corners[0] + corners[2]) / 2f;
return "minimap centre x=" + (int)centre.x + " y=" + (int)centre.y;
```

  2. `unity command --runtime-path "Builds/Client2" simulate_pointer -- --x <x> --y <y> --action move`
  3. After 1 s, read the router's cached gate through reflection:
     `var rt = System.Type.GetType("PlayerInputRouter, Overpower.Runtime"); ... rt.GetField("pointerOverUi", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(view.GetComponent(rt))`
     Expected: `False`.
  4. `simulate_pointer -- --x <x> --y <y> --action click`. After 1 s, read `System.AppDomain.CurrentDomain.GetData("minimapShots")`.
     Expected: ≥ 1.
  5. If the weapon was overheated or on cooldown, wait 3 s and click again. Release the override with
     `SetAimOverride(null)`.
  6. The Editor-side check is Task 6 Step 8.2: `raycaster=False raycastTargets=0 uiHitsThere=0`.

- [ ] **Step 14: R5, telemetry still labels captures correctly.**
  1. Editor: `unity command eval -- --code "return Overpower.Telemetry.MatchTelemetry.Instance != null ? Overpower.Telemetry.MatchTelemetry.Instance.CurrentFolder : \"no telemetry\";"`.
  2. In the master's `.jsonl` (the Editor's actor file in that folder), list every `capture` event in order, with t,
     zone, team, state and progress.
  3. Expected, one line each, no duplicates:
     - `t2A`: `started` → `paused` at progress ≈ 1 (completed);
     - zone 9: `started` → `paused` (the link came under attack) → `resumed` → `paused` (A left);
     - `t3AB`: A `started` → `paused` ≈ 0.3 (contested) → B `started` → `paused` ≈ 1;
     - `t2A` drain: B `drainStarted` → `drainPaused` ≈ 0 (neutralised).
  4. The drain must open with `drainStarted` (fixed in ring/minimap step 2, controller amendment 3). A `drainResumed`
     here is a regression: report it.
  5. No `capture` line may appear for a hold ending in Idle.
  6. If telemetry is disabled (`no telemetry` or an empty folder), report that R5 could not run, and why.

- [ ] **Step 15: Shutdown and static checks.**
  1. Stop the Player: `Stop-Process -Name OverPower -Force -ErrorAction SilentlyContinue; Stop-Process -Name UnityCrashHandler64 -Force -ErrorAction SilentlyContinue`.
  2. `unity command editor_stop`; poll until stopped. Dirty check: False.
  3. `git diff <BASE> -- Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` → empty (RpcList unchanged).
  4. `git diff <BASE> -- Assets/Gameplay/Config/GameplayConfig.asset` → empty.
  5. `git diff <BASE> -- Assets/Gameplay/Config/UiTheme.asset` → exactly −4 `captureBar*` lines, +11 `captureRing*`
     lines and +22 `minimap*` lines.
  6. `git status` → clean apart from other agents' files. Copy the `Temp/ring-minimap` captures out; don't commit them.
     If `Assets/Temp/ring-minimap` was used, delete it and its `.meta`.

- [ ] **Step 16: Report** (no commit, unless the controller asks for a progress note):
  - every capture path, with what you saw in it;
  - the R1 seconds;
  - the R2 recorder lines;
  - the R3/R4 reads;
  - the R5 capture event list;
  - the three static diffs;
  - anything that didn't match.

---

## Self-review against the spec

| Spec section / item | Task |
|---|---|
| Part 1: tower 9 gains 0, 1, 2 in `TowerDictionary` | 1 (steps 4-5) |
| Part 1: `TerritoryMapTests` fixture; "T4 capturable from a T2"; capital links only to its T2 | 1 (step 1) |
| Part 1: verify on the master that T2-only captures T4; link block through T2 | 1 (step 7, and the pure test in step 1); 7 (R2, live with an enemy) |
| Part 1: logged as [T] | 1 (step 8) |
| Part 2: one `CaptureRingView` per tower, built in `BuildingCapture.Start`, flat, sized to the Capture Radius | 3 |
| Part 2: edge always shown; owner colour / neutral dim white | 2 (state), 3 (view); 7 capture 1 |
| Part 2: progress arc from camera-up, clockwise; capturing / draining (owner's hold + drainer pulse) / paused (blink, reduced opacity) / idle | 2 (`CaptureRingState`, `CaptureRingGeometry`), 3; 7 captures 2, 3, 4 |
| Part 2: under attack pulses the warning colour even without a drain | 2, 3; 7 capture 8 |
| Part 2: held progress published as rate 0; Idle stays for nothing in progress; republish rule unchanged | 2 (steps 6, 9); 7 (R2, capture 3 reads on both clients) |
| Part 2: URP unlit, no collider, no shadows, height offset; mesh-or-LineRenderer choice made | Decisions; 3 (step 6.4 report) |
| Part 2: arc rebuilt only while changing | 3 (`Refresh`: points only on count/yaw change) |
| Part 2: theme fields with tooltips (outline, arc width, height, neutral colour, blink speed/opacity, warning colour/speed, segments) | 3 (steps 2, 4) |
| Part 2: `CaptureProgressView` and capture-bar theme fields removed | 3 (step 3); 7 (step 15 theme diff) |
| Part 3: `MinimapView` + pure `MinimapLayout`, one per local player, screen space, no raycast targets | 4, 6 |
| Part 3: Bake minimap image menu; ArenaRender approach, square on the arena; PNG + world rect on `MinimapConfig`; Rebuild thirds bakes; tooltip explains the button | 5 |
| Part 3: map rotates with the team yaw (`CameraTracking` exposes it); labels and markers upright | 3 (step 1), 4, 6; 7 capture 7 |
| Part 3: bubbles by tier (T1 > T4 > T2 = T3), owner fill / grey, Roman numeral, radial progress ring with a sprite, under-attack outline pulse | 4 (`TierLabel`), 6; 7 captures 5, 6, 8 |
| Part 3: links same owner / way-in arrowhead / thin grey | 4 (`MinimapLinkStyle`), 6; 7 captures 5, 6 |
| Part 3: own arrow + teammates' dots from `PlayerLookup` and replicated positions; no enemies | 6 (`UpdatePlayers`); 7 captures 5, 7 (dots can't show 1v1: stated) |
| Part 3: corner top-right, sizes and margins on the theme | 6 |
| Part 3: M toggles the large map, the corner hides; closes when P opens and vice versa | 6 (steps 3, 5, 8); 7 (capture 7, R3 real keys) |
| Part 3: minimap theme fields | 6 (step 1) |
| Part 3: performance (built once; colours on OwnershipChanged; rings only while active; markers per frame) | 6 (`MinimapView` class comment and structure) |
| Out of scope: 2.7 drops eliminated zones (hook only) | 6 (`SetZoneShown`, `Local`) |
| Verification: edit-mode tests for layout, link style, TerritoryMap, ring state | 1, 2, 4 (+5 bake radius) |
| Verification: 8 captures at 616×576, saved and looked at | 7 (steps 3-11) |
| Regressions: shoot through the minimap, P/M no overlap, solo T2 = 15 s | 7 (R4, R3, R1) |
| `RpcList` unchanged, `GameplayConfig.asset` unchanged, UiTheme diff only new/removed fields | Rules 9, 8; 7 (step 15) |
