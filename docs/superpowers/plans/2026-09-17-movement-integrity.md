# Movement Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- On every other screen, a player's blink, portal travel, respawn or long teleport shows as an instant jump, not a ~0.25 s slide.
- Two dashes in a row, or a zip pull or knockback followed by a dash, can't carry a player through a thin wall.
- Blink and portals can't put a player outside the arena, and portal travel lands standing on the floor. An owner-side
  safety net returns anyone who still gets out.

**Architecture:**
- **Pure rules, tested in edit mode:**
  - `DisplacementSweepRule` + `SweepContact`
  - `ArenaBounds`
  - `OutOfArenaRule`
  - `RemoteSnapRule`
  - `PlayerSpaceProbe.RootHeightOnGround`
- **Physics adapters:**
  - `PlayerDisplacement` gathers capsule overlaps, a capsule cast and `Physics.ComputePenetration`, and asks the rule.
  - `PlayerSpaceProbe` answers "does a player fit here" and "is the path clear at knee height".
  - `PlayerMotor` runs the safety net and the remote snap.
- **Data:**
  - `ArenaSymmetry.sourceOutline` in Game Scene, turned into all three thirds with `RadialSymmetry`.
  - `ArenaSymmetryBuilder.Validate` checks that every boundary wall sits on it.
- **Networking:** no new synced field and no RPC.
  - The owner sends `rb.position` in the same stream slot.
  - Receivers decide snaps from consecutive updates and their `SentServerTimestamp`.

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, NUnit edit-mode tests, `unity` CLI, two-client harness.

**Spec:** none separate. The three root-cause investigations (2026-09-17) are summarised under "What the code does
today". Each file:line was re-read at HEAD `f87a17b`.

**Naming rule:** "T1–T4" means zone tiers only. This plan's tasks are "Task 1..7", called **movement step N** in
reports, commits and `progress.md`.

---


## Controller decisions on the plan's open risks (2026-09-17; these override the task text)

- **R1 — a portal's path is only blocked by the arena's own boundary walls, not by crates or houses.**
  Tudor's complaint is leaving the arena, and blinking past cover is deliberate (1.6b). So the portal path check casts
  against the boundary pieces only (the same `Boundry` colliders the outline is validated against), not all of
  Building. Placing a portal behind a crate keeps working exactly as it does today. Refusals stay for: a destination
  outside the arena, and a path that crosses a boundary wall.
- **R2 — accepted as it is.** A respawning player's copy can still appear at their death spot for at most one update
  (about 50 ms) before snapping to the spawn. Since the claim fix, only the owner reports zone entry, so this can no
  longer affect territory. Note it in the plan, don't hide the mesh.
- **R3 — accepted.** The arena outline is hand-maintained, guarded by the Validate check against every boundary
  collider, so an arena edit that moves a wall fails the check loudly.
- **R5 — accepted.** A blink that lands after two or more lost updates (about 1% of the time) still glides.
- **Sequencing:** movement step 1 Part B and step 7 need the tree clean of the ability-visuals work; the controller
  will only start them when that has landed.

## What the code does today (verified at `f87a17b`)

**A. Remote copies slide instead of snapping.**
- **Owner side.** `PlayerNetSync.cs:53-56` sends `transform.position`, `transform.rotation`, health and armor at
  `SerializationRate` 20 (`RoomManager.cs:30`), Unreliable.
- **Receive path.** `PhotonHandler.FixedUpdate` (`PhotonHandler.cs:147-158`) dispatches, because
  `MinimalTimeScaleToDispatchInFixedUpdate` is −1 (`PhotonNetwork.cs:801`). The update reaches
  `PlayerNetSync.cs:68` → `PlayerMotor.SetNetworkTarget`.
- **The snap.** `PlayerMotor.cs:148-151` writes `rb.position` when the packet is more than `remoteSnapDistance`
  (3 m) from `transform.position`.
- **The lerp that undoes it.** `PlayerMotor.cs:136-137` lerps `rb.MovePosition(Lerp(transform.position, target, dt·10))`
  every FixedUpdate.
  - `m_AutoSyncTransforms` is 0 (`ProjectSettings/DynamicsManager.asset:22`) and the prefab Rigidbody interpolates
    (`Multiplayer Player.prefab:3012`), so the transform still holds the old spot.
  - `PhotonHandler` and `PlayerMotor` both run at execution order 0, so the lerp can overwrite the snap.
- **Respawn.** The owner's first update after a respawn can still carry `transform.position`, the death spot,
  because the transform only follows `rb.position` after the next physics step.
- **Captures are not affected.** Since `6da541f`, only the owner's own copy reports zone entry
  (`Building capture.cs:692-704`), so a sliding remote copy is a visual and body-blocking problem only.

**B. Two dashes pass through thin walls.**
- **The sweep.** `PlayerDisplacement.cs:122` calls `rb.SweepTestAll` (mask Default|Building, `:70`), stops exactly
  on contact (`:130-132`) and otherwise moves with `MovePosition` (`:142`).
- **Walking.** `PlayerMotor.cs:213` walks with an unswept `MovePosition`.
- **Wall thickness.** Boundary wall BoxColliders are 0.7244 m thick (Game Scene, `Wall R 14m` box size z).
  - The player capsule is radius 0.7, height 2.6126, centre y 0.8063, so its bottom is 0.5 m below the root
    (`Multiplayer Player.prefab:3057-3060`).
  - Past mid-wall (centre 1.06 m beyond the inner face), depenetration pushes out through the far face.
- **The same mover** serves the zip pull (`ZipGunAbility.cs:229`) and Sonic Pulse knockback (`SonicPulseAbility.cs:169`).
- **Unverified:** that the sweep misses a wall it starts inside. Task 1 settles it.

**C. Blink and portals can leave the arena.**
- **Blink** checks only the landing spot (`BlinkAbility.cs:9-11,31-32`). Blinking past crates and houses is deliberate
  (Task 1.6b) and stays.
- **The ground probe** accepts Default (`GroundProbe.cs:40-58`), and the terrain (`Terrain1…`, layer 0) continues
  outside the walls.
- **Portal placement** is a ground probe plus an overlap box (`TeleportAbility.cs:162-178,413-427`). Nothing checks
  the arena bounds or the path.
- **Portal travel** gates only on `canAct && other != null && HasCharge` (`:187`), then teleports to
  `to.transform.position` (`:241,:343`). That is the raw ground point, so the capsule arrives 0.5 m sunk.
  Blink offsets it at `BlinkAbility.cs:121-122`.
- **The only net** is kill height −10 (`PlayerMotor.cs:128-129` → `PlayerLifecycle.cs:222-226`). A respawn key was
  rejected on purpose (`PlayerMotor.cs:125-127`).

**Measured scene geometry this plan uses** (Game Scene YAML, `Source (Team 2 third)/Boundry`, 22 BoxColliders):
- **Pivots and faces.** Every piece's pivot is its outer face, and its local +Z faces the arena. The inner face is
  at local z = 0.7308.
- **Centre.** C = (65.05, 53.34).
- **Main wall lines.** Inner faces are 30.31 m from C. The right flank pocket's back inner face is 33.31 m from C,
  and its side walls' inner faces are at s = ±9.77 along the wall.
- **Capital pocket.** Inner faces at x = 54.649 and 75.451, back at z = 121.401. Its corners with the converging
  walls are at z = 95.944.

---

## Decisions [C]

1. **[C] One stop rule for dash, zip pull and knockback (`DisplacementSweepRule`).**
   - **The cast.** `PlayerDisplacement` casts the player's own `CapsuleCollider`, lifted 0.05 m (clear of the floor
     it stands on), along the step plus a 0.05 m skin. The move stops 0.05 m short of the nearest wall.
   - **Walls it already overlaps** come from `OverlapCapsuleNonAlloc`, plus any cast hit at distance 0.
     - For each, `Physics.ComputePenetration` is asked at the pose **one skin width along the move**. At that pose a
       capsule merely touching a wall it moves into already overlaps it.
     - Such a wall blocks only when `dot(move, pushOut) < −0.1`. Moving along a wall you touch (within about 6°)
       or away from it is not blocked.
   - **Floor.** The floor rule is unchanged: normal.y > 0.5 is ground. Terrain colliders are skipped.
2. **[C] A dash that couldn't move 1 cm is refused before its charge is spent.**
   - `PlayerDisplacement.CanStartVoluntary(dir)` asks the same rule once. `DashAbility.TryBuildCast` returns false
     when it says no.
   - It also refuses while a knockback runs. Before, that press spent a charge for nothing.
   - The zip gun is not refused up front: its charge is spent when the bolt fires, before any hit is known. A pinned
     zip pull ends Blocked on its first step.
3. **[C] A knockback always starts** (Forced priority unchanged). Against a wall it ends Blocked on step one, so Sonic
   Pulse's collision stun works as before.
4. **[C] Two things stay as they are.**
   - Walking keeps its unswept `MovePosition`. Pressing about 0.1 m into a wall is harmless once the rule refuses
     the dash.
   - Riding over low edges is unchanged. Task 1 measures the height, and boundary walls are 5.98 m tall.
5. **[C] The arena outline is data on `ArenaSymmetry.sourceOutline`.**
   - `List<Vector2>`, where x is world X and y is world Z.
   - It holds 8 points (Task 3), counter-clockwise, tracing the Source third's boundary inner faces.
   - `ArenaBounds.FromSourceOutline` appends their 120° and 240° turns. In Play Mode, `ArenaSymmetry.OnEnable`
     publishes `Active`/`ActiveBounds`.
   - With no arena or no outline, the bounds allow everything, so a test scene isn't locked up.
6. **[C] Validate checks the walls against the outline.** Every `BoxCollider` under `Boundry` in all three thirds must
   have its +Z face centre within 0.15 m of the outline. Rebuild thirds ends with Validate, so a moved wall is
   reported.
7. **[C] Margins.**
   - **Destinations.** Blink and portal destinations need a player radius (0.7 m) inside the outline.
   - **Returning.** The safety net returns a player only when the centre is past a wall's inner face (signed
     distance < 0).
   - **Remembering.** It remembers a spot only when the player stands on floor with a radius to spare. A player
     pressed into a wall triggers neither.
8. **[C] Portal path rule.**
   - A 0.1 m sphere cast on the Building layer at knee height (0.5 m) must be clear, from the caster's feet to the
     spot (`PlayerSpaceProbe.IsPathClear`).
   - This refuses portals behind a crate, a house wall or a wall piece. It's a feel change; see the list below.
9. **[C] Portal exit re-check.**
   - While standing in a portal, the exit must be inside the outline with a radius to spare, and not inside any
     Building-layer collider. It is part of `canChannel`, so a blocked exit never channels and spends nothing.
   - Players don't block an exit, so an enemy standing on it can't lock your portal.
   - Arrival uses the root height Blink uses: `PlayerSpaceProbe.RootOnGround`.
10. **[C] The zip gun gets no bounds code.** Its pull goes through the Task 2 sweep, and it ends at a wall face or a
    body. The Task 4 net catches anything else.
11. **[C] Safety-net home.**
    - `PlayerMotor` checks next to the kill height and raises `LeftArena(lastSafe)`.
    - `PlayerLifecycle` cancels any displacement, calls `TeleportTo(lastSafe)` and logs `[VIS]`.
    - Nothing the player presses reaches it.
12. **[C] Remote snap rule.**
    - **When.** Snap when the distance between two consecutively received updates exceeds `remoteSnapDistance` ×
      the send intervals between their `SentServerTimestamp`s (1..4). The first update always snaps.
    - **Where.** The snap is applied in the next `PlayerMotor.FixedUpdate`: `rb.position`/`rb.rotation` are set,
      velocities zeroed if not kinematic, and there is no lerp that step.
    - **The lerp** starts from `rb.position`/`rb.rotation`.
    - **The owner** sends `rb.position`. Rotation stays `transform.rotation`, because `PlayerAim.cs:146` writes the
      transform every Update.
    - **`PlayerMotor.RemoteSnapCount`** is a harness read, like `SetAimOverride`.
13. **[C] No new designer value.**
    - `GameplayConfig.asset` is unchanged. The outline is level data on the scene's `ArenaSymmetry`.
    - Skin, lift, tolerances, knee height and interval cap are constants with "not a tuning value" comments, like
      the existing `CursorOnSelfThreshold`.
14. **[C] Task 1 has a two-client Part B.** A remote copy can't exist with one client, so the slide's before-numbers
    need `Builds/Client2`.
15. **[C] Not changed:**
    - `DummyTarget`'s own knockback sweep (test range only; comments updated);
    - short blinks under 3 m still glide on other screens;
    - the ≤ 1-update flash of a respawning remote copy at its death spot (risk R2).

## Statements in the brief that the code doesn't match (read before building)

1. **"Slides through zone triggers" no longer matters for captures.** Zone entry is sent only for `IsMine`
   (`Building capture.cs:698-703`). The slide still misdraws the player and puts their body in the wrong place for
   other bodies and local projectiles.
2. **"ComputePenetration to get the push-out direction" needs one change.** At the start pose, a capsule merely
   touching a wall has no penetration. The plan asks at the pose one skin width along the move (decision 1). The
   cast's "start inside" report isn't relied on alone either: an explicit overlap query is added.
3. **"Store the outline on `ArenaSymmetry`" gives it a runtime job.** Its class comment says it does nothing during
   play (`ArenaSymmetry.cs:19-20`). This plan adds one runtime job and corrects the comment.
4. **"Rebuild thirds keeps it consistent."** The generated thirds' outlines are computed by rotation, so nothing is
   copied. Rebuild's closing Validate is what catches drift.
5. **"The owner sends `rb.position`": position only** (decision 12).
6. **"Portal travel re-checks (`:241,:343`)" moves earlier.** At `:343` the charge is already spent (`:239`), so the
   re-check goes into the channel gate at `:187`.
7. **"Riding up edges under ~0.35 m".** With the 0.05 m lift the threshold moves to about 0.40 m. It is measured in
   Tasks 1 and 6 and not changed (decision 4).
8. **"The alive flag lands 1–2 ticks before the new position."** Sending `rb.position` removes the part caused by the
   stale transform. The Custom Property can still arrive up to one send interval before the next update (risk R2).

## Movement feel changes (intended; list them in commit bodies)

1. **Dash into a wall.** A dash into a wall you're touching, or pressed into, does nothing and keeps its charge.
   Before, it spent the charge, and the second dash could pass through a thin wall.
2. **Dash during a knockback.** A dash pressed during a knockback does nothing and keeps its charge. Before, it spent
   the charge and did nothing.
3. **Skin width.** Dashes, zip pulls and knockbacks stop 5 cm short of a wall instead of touching it.
4. **Blink out of the arena.** Blink can't land outside the arena. Aimed outward, it lands at the last in-arena spot
   along the line. From against the wall that is your own spot (charge spent), exactly as blinking into a wall already
   behaves.
5. **Portal placement.** Portals can't be placed outside the arena, or with a wall, house, crate or cover between your
   feet and the spot. A refused placement spends nothing, as today.
6. **Portal travel.** You arrive standing on the floor, with no 0.5 m sink-and-pop. A portal doesn't channel while its
   exit is outside the arena or inside a wall, house, crate or cover.
7. **Safety net.** A player whose centre ends up outside the arena, by any route, is put back on the last spot they
   stood inside, in the next physics step.
8. **Other screens.** Blinks, portal travel, respawns and teleports over 3 m are instant; ordinary movement smoothing
   is unchanged. Remote positions are up to one physics step (20 ms) more current.

## Constraints

- **Networking:** no RPC added, renamed or removed; `PhotonServerSettings.asset` unchanged. `PlayerNetSync` keeps
  its wire order and types (Vector3, Quaternion, float, float).
- **Assets and settings:** `GameplayConfig.asset`, `ProjectSettings/TimeManager.asset` and
  `ProjectSettings/DynamicsManager.asset` are unchanged.
- **Capture:** nothing in `Building capture.cs`, `BuildingManager.cs` or the capture rules changes. Capture timings
  are unchanged.
- **The one scene edit** is Task 3's `sourceOutline`.

## Risks for the controller (decide before or during the build)

- **R1: portal path rule.** Refusing a portal behind a crate or house wall within 5 m is a gameplay change beyond
  "can't leave the arena". Drop decision 8 to keep bounds-only placement; the mine plan (A9) wants the path rule anyway.
- **R2: remote respawn flash.**
  - **What remains.** A remote copy can show alive at its death spot for up to one update (≤ 50 ms) before the
    respawn update snaps it.
  - **Possible fix, not in this plan.** Keep a remote copy's mesh and collider off after `alive=true` until its next
    update arrives.
- **R3: hand-maintained outline.** A designer who moves a boundary wall must move the outline. Validate and
  `ArenaSymmetrySceneTests` catch a miss. Deriving it automatically from colliders was judged too fragile (corner
  notches, stretched pieces).
- **R4: two-client builds use the working tree.** If another agent has uncommitted edits when Task 1 Part B or Task 7
  builds, stop and ask.
- **R5: packet loss.** A blink whose update follows ≥ 2 consecutive lost updates still glides. That is about 1% of
  blinks at 10% loss. Accepted.

---

## Rules for every task (each has cost hours on this project)

1. **Branch and Editor.**
   - Branch `limit-testing` only; push after each task's commit.
   - `unity command editor_status` must answer before editing.
   - There is only one Editor. If another agent is driving it, stop and ask the controller.
2. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `unity command editor_stop`, poll
   `unity command editor_status` until `playMode: "stopped"` and `compiling: false`.
3. **Compile and test.**
   - Run `unity command recompile`, then poll `unity command recompile_status` until completed with `errors: []`. It
     is the only compile truth.
   - **Tests run async only:** `unity command run_tests -- --mode editor --async_tests true`, then poll
     `unity command test_status`.
4. **Dirty scene → modal dialog → silent Editor hang.**
   - **Check.** Before tests, recompile, build, Play Mode or a scene open, run
     `unity command eval -- --code "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"`.
     Read the answer, and continue only if it is `False` (don't chain it with `&&`).
   - **Discard.** `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", UnityEditor.SceneManagement.OpenSceneMode.Single)`.
   - **Hang.** If `editor_status` times out: stop and report. Don't click dialogs.
5. **CLI** (run every command from `C:\UniStuff\Y3\MinorSkilled\GitAccess\Project-OverPower`):
   - form: `unity command <name> -- --flag value`
   - files: `unity command eval_file -- --file "<path>" --timeout 20000`
   - scene scripts: `unity command --timeout 240 run_script -- --file "<path>" --entry Type.Run --timeout_ms 200000`
   - eval code writes `UnityEngine.Object`, never bare `Object`
   - Player: `unity command --runtime-path "Builds/Client2" <name> -- ...`; its eval needs reflection
     (`two-client-harness.md` §6)
6. **SCRATCH** = `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\movement`.
   - Every scratch script, recording and capture goes there, never under `Assets/`.
   - `__PLACEHOLDERS__` in a `_tpl` file are filled with PowerShell `-replace` into a new file for each use.
7. **Tudor uses the computer while agents work.**
   - Never rely on the real mouse, keyboard or window focus. Never call `editor_focus`, and never use
     `focus_switch.ps1`.
   - Aim only with `PlayerAim.SetAimOverride`. Cast through `AbilityRunner`'s private `TryCast` by reflection, never
     `PressSlot`.
   - If the Editor stops ticking while unfocused, run `unity command set_autotick -- --enable true`.
   - Client2's window may open over Tudor's work: never click it. It runs in the background (`runInBackground`,
     harness §11 item 15).
8. **Captures.** No task needs one. If you take one anyway:
   - it is 616×576, from the real Game view, saved to an explicit path and copied to SCRATCH;
   - read the PNG yourself before describing it;
   - if another window covered the Game view, retake it rather than describing a covered frame.
9. **Saving assets.**
   - `AssetDatabase.SaveAssets()` flushes every dirty asset: use `AssetDatabase.SaveAssetIfDirty(asset)`, then
     `git status`.
   - The Constraints files must stay unchanged. **Add no RPC, rename none, remove none.**
   - No new component implements `IPunObservable`.
10. **Moving and aiming the player.**
    - Never move a player with `transform.position`. Use `PlayerDisplacement.TeleportTo`, and wait at least a physics
      step before relying on the new position.
    - Change abilities with `PlayerLoadout.SetAbility`.
11. **Measure with game-time or physics-step stamps**, in an in-process coroutine that writes a file. The CLI round
    trip is several seconds. Never judge cross-client behaviour from a later poll (CODING-STANDARDS §6).
12. **Comments and values.** Every designer-facing value lives on an asset or scene component with a plain
    `[Tooltip]`, in one home. Comments explain *why*, for a designer reader. This plan adds no tuning value.
13. **Commits.**
    - Messages end with your own `Co-Authored-By:` line. No unmeasured number in a commit message.
    - Stage **only** the task's listed files; check `git status` first.
    - Other agents commit on this branch too: never stage their files.
14. **Assumptions.** Append judgement calls to
    `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md`:
    - under a new heading `## Movement integrity (2026-09-17)` at the end;
    - as short [C] lines;
    - outside the repo, so don't commit it;
    - then list the file's `## ` headings to confirm none was lost.
15. **Scene saves.** Only Task 3 saves Game Scene, through its script. Every other task leaves it unmodified: dirty
    check `False` after stopping Play Mode. Play Mode objects (test cubes, portals) vanish on stop.
16. **Two-client builds only from a clean tree.** If `git status --short` lists files not yet committed (anyone's),
    stop and ask the controller.

## File map

| File | Responsibility | Step |
|---|---|---|
| `SCRATCH\movement\movement_recorder_tpl.cs` (scratch) | Single-client scenarios: sweep, doubledash, hugdash, lowedge, blink, portal, pin, safetynet | 1 (used 2–4, 6) |
| `SCRATCH\movement\remote_view_recorder_tpl.cs`, `movement_driver_tpl.cs`, `join_room_tpl.cs`, `room_status.cs`, `analyze_movement.py` (scratch) | Two-client recording, owner driver, analysis | 1 (used 7) |
| `Assets/scripts/Combat/DisplacementSweepRule.cs` + `Assets/Tests/DisplacementSweepRuleTests.cs` (create) | The stop rule | 2 |
| `Assets/scripts/Player/PlayerDisplacement.cs` (modify) | Capsule overlap/cast adapter, `CanStartVoluntary` | 2 |
| `Assets/scripts/Abilities/Mobility/DashAbility.cs` (modify) | Refuse a dash that can't move | 2 |
| `Assets/scripts/TestRange/DummyTarget.cs` (comments only) | Stale references to `IsBlocker` / `SweepTestAll` | 2 |
| `Assets/scripts/Arena/ArenaBounds.cs` + `Assets/Tests/ArenaBoundsTests.cs` (create) | Outline polygon, signed distance | 3 |
| `Assets/scripts/Arena/ArenaSymmetry.cs` (modify) | `sourceOutline`, `Active`, `ActiveBounds`, `IsInsideArena`, gizmo | 3 |
| `Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs` + `Assets/Tests/ArenaSymmetryBuilderTests.cs` (modify) | Outline check in Validate | 3 |
| `Assets/Tests/ArenaSymmetrySceneTests.cs` (modify) | Spawns and towers inside the saved outline | 3 |
| `Assets/Scenes/Game Scene.unity` (modify, script) | The 8 outline points | 3 |
| `Assets/scripts/Abilities/Core/PlayerSpaceProbe.cs` + `Assets/Tests/PlayerSpaceProbeTests.cs` (create) | Root on ground, capsule blocked, knee-height path | 3 |
| `Assets/scripts/Abilities/Mobility/BlinkAbility.cs`, `TeleportAbility.cs` (modify) | Bounds, path, exit re-check, arrival height | 3 |
| `Assets/scripts/Arena/OutOfArenaRule.cs` + `Assets/Tests/OutOfArenaRuleTests.cs` (create) | Safety-net decision | 4 |
| `Assets/scripts/Player/PlayerMotor.cs` (modify) | Safety net (step 4); snap + lerp (step 5) | 4, 5 |
| `Assets/scripts/Player/PlayerLifecycle.cs` (modify) | `HandleLeftArena` | 4 |
| `Assets/scripts/Net/RemoteSnapRule.cs` + `Assets/Tests/RemoteSnapRuleTests.cs` (create) | Snap decision | 5 |
| `Assets/scripts/Player/PlayerNetSync.cs` (modify) | Send `rb.position`, pass `SentServerTimestamp` | 5 |

## Hook for ability visuals step 3 (A9, mines within 2 m) — provided here, not built here

`docs/superpowers/plans/2026-09-17-ability-visual-clarity.md` Task 3 places a mine on the aim point within 2 m. After
movement step 3 it can use the same rule portals use:

```csharp
// In MineAbility.TryBuildCast, for each candidate floor point walking back toward the caster (BlinkDestinationSearch.Find
// already does the walk-back with a probe delegate):
var capsule = Owner.Root.GetComponent<CapsuleCollider>();
Vector3 feet = PlayerSpaceProbe.FeetOf(capsule, ctx.Origin);
bool usable = Overpower.Arena.ArenaSymmetry.IsInsideArena(floorPoint, mineRadius)
              && PlayerSpaceProbe.IsPathClear(feet, floorPoint);
```

Order: movement step 3 should land before ability visuals step 3. The two plans share no file.

---

### Task 1 (movement step 1): reproduce first, settle the unverified sweep step, record before-numbers

**What exists:**
- The three bugs above.
- The harness templates in `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\two-client-eval\`. Their patterns
  are reused here; the files are not changed.

**Files:** scratch only (SCRATCH). No repo file changes; nothing to commit.

- [ ] **Step 0: Start state.**
  1. `unity command editor_status`: it answers, `playMode: "stopped"`, `compiling: false`.
  2. The dirty check (Rule 4) reads `False`.
  3. `git status --short`: report anything listed.
  4. `git rev-parse HEAD` → report it as **BASE**. Tasks 6 and 7 diff assets against it.
  5. `New-Item -ItemType Directory -Force "<SCRATCH>"`.

- [ ] **Step 1: Write `SCRATCH\movement_recorder_tpl.cs`.**
  - It compiles against today's code and after every later step.
  - Every API added later is reached by reflection and reads `n/a` when absent.
  - Part 1, setup and helpers:

```csharp
// Movement integrity recorder (movement steps 1-6). Editor Play Mode, in a room, ALONE (you are the master).
// Placeholders: __LABEL__ (before | step2 | step3 | step4 | after), __ONLY__ (comma-separated scenarios, or all).
// Scenarios: sweep, doubledash, hugdash, lowedge, blink, portal, pin, safetynet.
// Writes SCRATCH\movement-__LABEL__.txt, then movement-__LABEL__.done when finished (also after an exception).
var DIR = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\movement\";
string LABEL = "__LABEL__";
string ONLY = "__ONLY__";
System.IO.Directory.CreateDirectory(DIR);
string outPath = DIR + "movement-" + LABEL + ".txt";
string donePath = DIR + "movement-" + LABEL + ".done";
System.IO.File.Delete(donePath);
System.IO.File.WriteAllText(outPath, $"movement recorder {LABEL}, only={ONLY}, Time.time={Time.time:0.00}\n");

var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
var rb = me.GetComponent<Rigidbody>();
var motor = me.GetComponent<PlayerMotor>();
var disp = me.GetComponent<PlayerDisplacement>();
var aim = me.GetComponent<PlayerAim>();
var runner = me.GetComponent<AbilityRunner>();
var loadout = me.GetComponent<PlayerLoadout>();
var capsule = me.GetComponent<CapsuleCollider>();
var arena = UnityEngine.Object.FindFirstObjectByType<Overpower.Arena.ArenaSymmetry>();
Overpower.Net.Teams.TryGetTeam(Photon.Pun.PhotonNetwork.LocalPlayer, out int team);
Vector3 spawn = UnityEngine.Object.FindFirstObjectByType<RoomManager>().teamSpawnPoints[team].position;
int actor = Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber;
var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var tryCast = typeof(AbilityRunner).GetMethod("TryCast", NP);
var refill = typeof(Overpower.Abilities.AbilityModule).GetMethod("RefillCharges", NP);
var activeBoundsProp = typeof(Overpower.Arena.ArenaSymmetry).GetProperty("ActiveBounds");   // null before movement step 3
int buildingMask = LayerMask.GetMask("Building");
int solidMask = LayerMask.GetMask("Default", "Building");
var Mobility = Overpower.Data.AbilitySlot.Mobility;
var logs = new System.Collections.Generic.List<string>();
int netReturns = 0;

void Out(string s) => System.IO.File.AppendAllText(outPath, s + "\n");
bool Wanted(string scenario) => ONLY == "all" || System.Array.IndexOf(ONLY.Split(','), scenario) >= 0;
Application.LogCallback onLog = (msg, st, type) =>
{
    if (msg.StartsWith("[DASH]") || msg.StartsWith("[BLINK]") || msg.StartsWith("[ZIP]") || msg.StartsWith("[BlinkAbility]") || msg.StartsWith("[TeleportAbility]"))
        logs.Add($"{Time.time:0.00} {msg}");
    if (msg.Contains("left the arena")) { netReturns++; logs.Add($"{Time.time:0.00} {msg}"); }
    if (type == LogType.Exception) logs.Add($"{Time.time:0.00} EXCEPTION {msg}");
};
string TakeLogs() { string s = logs.Count > 0 ? string.Join(" | ", logs) : "(none)"; logs.Clear(); return s; }

Transform Named(int third, string objectName)
{
    Transform root = third == 0 ? arena.source : third == 1 ? arena.generated120 : arena.generated240;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        if (t.name == objectName) return t;
    return null;
}

// The floor under a point: the highest non-self hit below 1 m (skips wall, crate and roof tops).
float FloorY(Vector3 p)
{
    float best = float.NegativeInfinity;
    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 8f, p.z), Vector3.down, 20f, solidMask, QueryTriggerInteraction.Ignore))
        if (!h.collider.transform.IsChildOf(me.transform) && h.point.y < 1f && h.point.y > best) best = h.point.y;
    return float.IsNegativeInfinity(best) ? 0f : best;
}
float FloorOffset() => capsule.height * 0.5f - capsule.center.y;   // a standing root's height above the floor (0.5)
Vector3 Stand(Vector3 xz) => new Vector3(xz.x, FloorY(xz) + FloorOffset(), xz.z);

bool Free(Vector3 root)
{
    Vector3 c = root + capsule.center;
    float half = capsule.height * 0.5f - capsule.radius;
    foreach (Collider col in Physics.OverlapCapsule(c + Vector3.up * (half + 0.05f), c - Vector3.up * (half - 0.05f), capsule.radius, solidMask, QueryTriggerInteraction.Ignore))
        if (!col.transform.IsChildOf(me.transform)) return false;
    return true;
}
bool Clear(Vector3 root, Vector3 dir, float metres) =>
    !Physics.SphereCast(root + Vector3.up, capsule.radius + 0.1f, dir, out _, metres, solidMask, QueryTriggerInteraction.Ignore);

// An open floor spot near the team spawn with `metres` clear both ways along the returned direction.
bool OpenSpot(float metres, out Vector3 root, out Vector3 dir)
{
    for (int ring = 6; ring <= 14; ring += 4)
        for (int i = 0; i < 16; i++)
        {
            root = Stand(spawn + Quaternion.Euler(0f, 22.5f * i, 0f) * Vector3.forward * ring);
            if (!Free(root)) continue;
            for (int k = 0; k < 8; k++)
            {
                dir = Quaternion.Euler(0f, 45f * k, 0f) * Vector3.forward;
                if (Clear(root, dir, metres) && Clear(root, -dir, metres)) return true;
            }
        }
    root = spawn;
    dir = Vector3.forward;
    return false;
}

// A boundary piece's inner face centre, its inward normal (local +Z, flat), its along-the-wall axis and its thickness.
void Face(Transform piece, out Vector3 face, out Vector3 inward, out Vector3 along, out float thickness)
{
    var box = piece.GetComponent<BoxCollider>();
    face = piece.TransformPoint(box.center + new Vector3(0f, 0f, box.size.z * 0.5f));
    inward = piece.forward; inward.y = 0f; inward.Normalize();
    along = piece.right; along.y = 0f; along.Normalize();
    thickness = box.size.z * piece.lossyScale.z;
}

string ArenaSigned(Vector3 p)
{
    object bounds = activeBoundsProp != null ? activeBoundsProp.GetValue(null) : null;
    if (bounds == null) return "n/a";
    float s = (float)bounds.GetType().GetMethod("SignedDistance", new[] { typeof(Vector3) }).Invoke(bounds, new object[] { p });
    return s.ToString("+0.00;-0.00");
}

// The body now, measured along dir from start against one collider: the centre relative to its near face
// (-0.70 = touching, -0.75 = a skin width short, -0.60 = pressed 0.1 m in) and whether it got past the far face.
string Where(Vector3 start, Vector3 dir, Collider col)
{
    Vector3 chest = start + Vector3.up;
    float near = col.Raycast(new Ray(chest, dir), out RaycastHit a, 40f) ? a.distance : float.NaN;
    float far = col.Raycast(new Ray(chest + dir * 40f, -dir), out RaycastHit b, 40f) ? 40f - b.distance : float.NaN;
    Vector3 p = rb.position;
    float travelled = Vector3.Dot(p - start, dir);
    string side = float.IsNaN(near) ? "?" : travelled > far ? "BEYOND" : travelled > near - capsule.radius + 0.005f ? "IN" : "clear";
    return $"travelled={travelled:0.00} centreVsNearFace={travelled - near:+0.00;-0.00} {side} arenaSigned={ArenaSigned(p)}";
}

Overpower.Abilities.AbilityModule Module() => runner.StatusFor(Mobility) as Overpower.Abilities.AbilityModule;

// Casts the Mobility ability through AbilityRunner.TryCast (no press buffer to race). True when a charge was spent.
bool Cast()
{
    var m = Module();
    if (m == null) { Out("  no Mobility module"); return false; }
    int before = m.ChargesAvailable;
    tryCast.Invoke(runner, new object[] { m });
    return m.ChargesAvailable < before;
}

System.Collections.IEnumerator Equip(int id)
{
    loadout.SetAbility(Mobility, id);
    yield return null;
    var m = Module();
    if (m != null) refill.Invoke(m, null);
}

System.Collections.IEnumerator Place(Vector3 root)
{
    aim.SetAimOverride(null);
    disp.Cancel();
    if (!disp.TeleportTo(root)) Out($"  WARN TeleportTo({root:F2}) refused");
    yield return new WaitForSeconds(0.4f);
}

System.Collections.IEnumerator AimAt(Vector3 point)
{
    aim.SetAimOverride(point);
    yield return null;
    yield return null;
}

// PlayerMotor.Move's own push, done here because no key is held: MovePosition along dir at walking speed, unswept.
// Harness only: ExternalMotionControl keeps Move() from overwriting it for these steps.
System.Collections.IEnumerator WalkInto(Vector3 dir, float seconds)
{
    motor.ExternalMotionControl = true;
    float end = Time.time + seconds;
    while (Time.time < end)
    {
        yield return new WaitForFixedUpdate();
        rb.MovePosition(rb.position + dir * motor.CurrentSpeed * Time.fixedDeltaTime);
    }
    yield return new WaitForFixedUpdate();
    motor.ExternalMotionControl = false;
}

System.Collections.IEnumerator DoubleDash(string label, Vector3 startXZ, Vector3 dir, Collider col)
{
    Vector3 start = Stand(startXZ);
    if (!Free(start)) { Out($"[doubledash] {label}: start {start:F2} is not free - skipped"); yield break; }
    yield return Place(start);
    refill.Invoke(Module(), null);
    TakeLogs();
    yield return AimAt(start + dir * 6f);
    bool spent1 = Cast();
    yield return new WaitForSeconds(0.5f);
    string after1 = Where(start, dir, col);
    yield return WalkInto(dir, 0.4f);
    string walked = Where(start, dir, col);
    yield return AimAt(rb.position + dir * 6f);
    bool spent2 = Cast();
    yield return new WaitForSeconds(0.6f);
    string after2 = Where(start, dir, col);
    yield return new WaitForSeconds(0.6f);
    Out($"[doubledash] {label}\n  dash 1 spent={spent1}: {after1}\n  walked in 0.4 s: {walked}\n  dash 2 spent={spent2}: {after2}\n  0.6 s later: {Where(start, dir, col)}\n  log: {TakeLogs()}");
}

System.Collections.IEnumerator PinnedDashes(string label, Vector3 start, Vector3 dir, Collider col)
{
    refill.Invoke(Module(), null);
    TakeLogs();
    yield return AimAt(rb.position + dir * 6f);
    bool spent1 = Cast();
    yield return new WaitForSeconds(0.5f);
    string after1 = Where(start, dir, col);
    yield return AimAt(rb.position + dir * 6f);
    bool spent2 = Cast();
    yield return new WaitForSeconds(0.6f);
    Out($"[pin] {label}\n  dash 1 spent={spent1}: {after1}\n  dash 2 spent={spent2}: {Where(start, dir, col)}\n  log: {TakeLogs()}");
}

string NewestGate(Vector3 face, Vector3 inward, float thick)
{
    Overpower.Abilities.Portal newest = null;
    foreach (var p in Overpower.Abilities.Portal.ForOwner(actor)) if (newest == null || p.Seq > newest.Seq) newest = p;
    if (newest == null) return "";
    float beyondOuterFace = Vector3.Dot(face - newest.transform.position, inward) - thick;
    return $"; newest at {newest.transform.position:F2}, {(beyondOuterFace > 0f ? $"OUTSIDE by {beyondOuterFace:0.00} m" : "inside")} arenaSigned={ArenaSigned(newest.transform.position)}";
}
```

  Part 2, the scenarios (same file, directly after part 1):

```csharp
System.Collections.IEnumerator Run()
{
    Application.logMessageReceived += onLog;
    try
    {
        if (Wanted("sweep"))
        {
            // Settles the unverified step: does the sweep see a wall the capsule already overlaps? Each probe teleports and
            // queries in the same physics step, before depenetration can move the body.
            Transform piece = Named(0, "Pocket Back 1");
            Face(piece, out Vector3 face, out Vector3 inward, out _, out _);
            var wall = piece.GetComponent<BoxCollider>();
            foreach (float gap in new[] { 0.75f, 0.70f, 0.60f })
            {
                yield return Place(Stand(face + inward * 3f));
                yield return new WaitForFixedUpdate();
                disp.TeleportTo(Stand(face + inward * gap));
                bool overlapping = Physics.ComputePenetration(capsule, rb.position, rb.rotation, wall, piece.position, piece.rotation, out Vector3 pushDir, out float depth);
                var sweep = new System.Text.StringBuilder();
                foreach (RaycastHit h in rb.SweepTestAll(-inward, 0.36f, QueryTriggerInteraction.Ignore))
                    sweep.Append($" {h.collider.name}@{h.distance:0.000}");
                Vector3 c = rb.position + rb.rotation * capsule.center + Vector3.up * 0.05f;
                float half = capsule.height * 0.5f - capsule.radius;
                var cast = new System.Text.StringBuilder();
                foreach (RaycastHit h in Physics.CapsuleCastAll(c - Vector3.up * half, c + Vector3.up * half, capsule.radius, -inward, 0.41f, solidMask, QueryTriggerInteraction.Ignore))
                    if (!h.collider.transform.IsChildOf(me.transform)) cast.Append($" {h.collider.name}@{h.distance:0.000}");
                var overlap = new System.Text.StringBuilder();
                foreach (Collider o in Physics.OverlapCapsule(c - Vector3.up * half, c + Vector3.up * half, capsule.radius, solidMask, QueryTriggerInteraction.Ignore))
                    if (!o.transform.IsChildOf(me.transform)) overlap.Append($" {o.name}");
                Out($"[sweep] Pocket Back 1, centre {gap:0.00} m from its inner face: overlapsWall={overlapping} depth={depth:0.000} push={pushDir:F2}" +
                    $"\n  rb.SweepTestAll 0.36 m into it:{(sweep.Length > 0 ? sweep.ToString() : " none")}" +
                    $"\n  CapsuleCastAll lifted 0.05, 0.41 m:{(cast.Length > 0 ? cast.ToString() : " none")}" +
                    $"\n  OverlapCapsule lifted 0.05:{(overlap.Length > 0 ? overlap.ToString() : " none")}");
                yield return new WaitForSeconds(0.3f);
            }
        }

        if (Wanted("doubledash"))
        {
            yield return Equip(14);
            for (int third = 0; third < 3; third++)
            {
                foreach (string pieceName in new[] { "Wall R 21m", "Pocket Back 1", "Flank Back 0m", "Flank Side A" })
                {
                    Transform piece = Named(third, pieceName);
                    if (piece == null) { Out($"[doubledash] third {third} {pieceName}: not found"); continue; }
                    Face(piece, out Vector3 face, out Vector3 inward, out _, out _);
                    yield return DoubleDash($"third {third} {pieceName}", face + inward * 2f, -inward, piece.GetComponent<BoxCollider>());
                }
                foreach (string obstacleName in new[] { "Cover Crate Defender Front", "House_03 (2)" })
                {
                    Transform obstacle = Named(third, obstacleName);
                    Collider[] parts = obstacle != null ? obstacle.GetComponentsInChildren<Collider>() : new Collider[0];
                    if (parts.Length == 0) { Out($"[doubledash] third {third} {obstacleName}: not found"); continue; }
                    Bounds bounds = parts[0].bounds;
                    foreach (Collider part in parts) bounds.Encapsulate(part.bounds);
                    // Approach from the arena centre's side, 2 m in front of the face the dash line meets.
                    Vector3 toward = bounds.center - arena.centre; toward.y = 0f; toward.Normalize();
                    Vector3 probe = bounds.center - toward * (bounds.extents.magnitude + 3f);
                    probe.y = FloorY(probe) + 1.5f;
                    if (!Physics.Raycast(probe, toward, out RaycastHit hit, bounds.extents.magnitude * 2f + 6f, buildingMask, QueryTriggerInteraction.Ignore)
                        || !hit.collider.transform.IsChildOf(obstacle))
                    { Out($"[doubledash] third {third} {obstacleName}: something else stands in front - skipped"); continue; }
                    yield return DoubleDash($"third {third} {obstacleName}", hit.point - toward * 2f, toward, hit.collider);
                }
            }
        }

        if (Wanted("hugdash"))
        {
            yield return Equip(14);
            Transform piece = Named(0, "Wall R 21m");
            Face(piece, out Vector3 face, out Vector3 inward, out Vector3 along, out _);
            var wall = piece.GetComponent<BoxCollider>();
            string[] labels = { "along", "away", "into" };
            Vector3[] dirs = { along, inward, -inward };
            for (int i = 0; i < 3; i++)
            {
                Vector3 start = Stand(face + inward * 1.5f);
                yield return Place(start);
                yield return WalkInto(-inward, 0.4f);   // press against the wall, as a player walking into it does
                refill.Invoke(Module(), null);
                TakeLogs();
                Vector3 from = rb.position;
                yield return AimAt(from + dirs[i] * 6f);
                bool spent = Cast();
                yield return new WaitForSeconds(0.6f);
                Vector3 moved = rb.position - from; moved.y = 0f;
                Out($"[hugdash] pressed against Wall R 21m, dash {labels[i]}: spent={spent} travelled={moved.magnitude:0.00} {Where(start, -inward, wall)}\n  log: {TakeLogs()}");
            }
            if (OpenSpot(5f, out Vector3 open, out Vector3 openDir))
            {
                yield return Place(open);
                refill.Invoke(Module(), null);
                TakeLogs();
                yield return AimAt(open + openDir * 6f);
                bool spentOpen = Cast();
                yield return new WaitForSeconds(0.6f);
                Vector3 movedOpen = rb.position - open; movedOpen.y = 0f;
                Out($"[hugdash] open floor at {open:F2}: spent={spentOpen} travelled={movedOpen.magnitude:0.00}\n  log: {TakeLogs()}");
            }
            else Out("[hugdash] open floor: no open spot near the spawn");
        }

        if (Wanted("lowedge"))
        {
            // Play Mode only: a temporary Building-layer step, destroyed straight away. Does a dash ride over a low edge?
            yield return Equip(14);
            if (!OpenSpot(6f, out Vector3 open, out Vector3 openDir)) Out("[lowedge] no open spot near the spawn");
            else
            {
                foreach (float height in new[] { 0.2f, 0.35f, 0.5f })
                {
                    yield return Place(open);
                    var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    step.name = "movement-lowedge (Play Mode only)";
                    step.layer = LayerMask.NameToLayer("Building");
                    float floor = FloorY(open);
                    step.transform.SetPositionAndRotation(new Vector3(open.x, floor + height * 0.5f, open.z) + openDir * 2.5f, Quaternion.LookRotation(openDir));
                    step.transform.localScale = new Vector3(3f, height, 1f);
                    yield return new WaitForFixedUpdate();
                    refill.Invoke(Module(), null);
                    TakeLogs();
                    yield return AimAt(open + openDir * 6f);
                    bool spent = Cast();
                    yield return new WaitForSeconds(0.6f);
                    Vector3 moved = rb.position - open; moved.y = 0f;
                    Out($"[lowedge] {height:0.00} m step, near face 2.0 m ahead: spent={spent} travelled={moved.magnitude:0.00} rootAboveFloor={rb.position.y - floor:0.00} (0.50 = on the floor)\n  log: {TakeLogs()}");
                    UnityEngine.Object.Destroy(step);
                    yield return new WaitForSeconds(0.2f);
                }
            }
        }

        if (Wanted("blink"))
        {
            yield return Equip(15);
            foreach (string pieceName in new[] { "Pocket Back 1", "Wall R 21m" })
            {
                Transform piece = Named(0, pieceName);
                Face(piece, out Vector3 face, out Vector3 inward, out _, out _);
                var wall = piece.GetComponent<BoxCollider>();
                foreach (float gap in new[] { 0.72f, 2f, 5f })
                {
                    Vector3 start = Stand(face + inward * gap);
                    yield return Place(start);
                    refill.Invoke(Module(), null);
                    TakeLogs();
                    yield return AimAt(start - inward * 9f);
                    bool spent = Cast();
                    yield return new WaitForSeconds(0.5f);
                    Out($"[blink] {pieceName} from {gap:0.00} m, aimed 9 m straight through it: spent={spent} {Where(start, -inward, wall)} rootAboveFloor={rb.position.y - FloorY(rb.position):0.00}\n  log: {TakeLogs()}");
                }
            }
            Transform crate = Named(0, "Cover Crate Defender Front");
            Collider crateCol = crate != null ? crate.GetComponentInChildren<Collider>() : null;
            if (crateCol == null) Out("[blink] Cover Crate Defender Front not found");
            else
            {
                Vector3 toward = crateCol.bounds.center - arena.centre; toward.y = 0f; toward.Normalize();
                float reach = crateCol.bounds.extents.x + crateCol.bounds.extents.z + 2f;
                Vector3 crateStart = Stand(crateCol.bounds.center - toward * reach);
                yield return Place(crateStart);
                refill.Invoke(Module(), null);
                TakeLogs();
                yield return AimAt(crateCol.bounds.center + toward * reach);
                bool spentCrate = Cast();
                yield return new WaitForSeconds(0.5f);
                Out($"[blink] over Cover Crate Defender Front: spent={spentCrate} {Where(crateStart, toward, crateCol)}\n  log: {TakeLogs()}");
            }
        }

        if (Wanted("portal"))
        {
            yield return Equip(15);   // swapping the slot destroys any portals left from earlier
            yield return Equip(17);
            Transform piece = Named(0, "Pocket Back 1");
            Face(piece, out Vector3 face, out Vector3 inward, out _, out float thick);
            yield return Place(Stand(face + inward * 1f));
            int before = Overpower.Abilities.Portal.ForOwner(actor).Count;
            yield return AimAt(face - inward * (thick + 3f));
            Cast();
            yield return new WaitForSeconds(0.5f);
            Out($"[portal] 3 m past Pocket Back 1's outer face (4.7 m from you): portals {before} -> {Overpower.Abilities.Portal.ForOwner(actor).Count}{NewestGate(face, inward, thick)}\n  log: {TakeLogs()}");

            Transform crate = Named(0, "Cover Crate Defender Front");
            Collider crateCol = crate != null ? crate.GetComponentInChildren<Collider>() : null;
            if (crateCol != null)
            {
                yield return Equip(15);
                yield return Equip(17);
                Vector3 toward = crateCol.bounds.center - arena.centre; toward.y = 0f; toward.Normalize();
                float reach = crateCol.bounds.extents.x + crateCol.bounds.extents.z + 1f;
                yield return Place(Stand(crateCol.bounds.center - toward * reach));
                int beforeCrate = Overpower.Abilities.Portal.ForOwner(actor).Count;
                yield return AimAt(crateCol.bounds.center + toward * reach);
                Cast();
                yield return new WaitForSeconds(0.5f);
                Out($"[portal] 1 m past Cover Crate Defender Front ({2f * reach:0.0} m from you): portals {beforeCrate} -> {Overpower.Abilities.Portal.ForOwner(actor).Count}\n  log: {TakeLogs()}");
            }

            // Arrival height: two portals 3 m either side of an open spot; stand in one and time the travel.
            yield return Equip(15);
            yield return Equip(17);
            if (!OpenSpot(4f, out Vector3 open, out Vector3 openDir)) Out("[portal] arrival: no open spot near the spawn");
            else
            {
                yield return Place(open);
                yield return AimAt(open + openDir * 3f);
                Cast();
                yield return new WaitForSeconds(0.3f);
                yield return AimAt(open - openDir * 3f);
                Cast();
                yield return new WaitForSeconds(0.3f);
                aim.SetAimOverride(null);
                var gates = new System.Collections.Generic.List<Overpower.Abilities.Portal>(Overpower.Abilities.Portal.ForOwner(actor));
                if (gates.Count < 2) Out($"[portal] arrival: only {gates.Count} portals placed\n  log: {TakeLogs()}");
                else
                {
                    Vector3 entry = gates[0].transform.position, exit = gates[1].transform.position;
                    disp.TeleportTo(new Vector3(entry.x, entry.y + FloorOffset(), entry.z));
                    float t0 = Time.time;
                    int stepIndex = 0, arrivedStep = -1;
                    var heights = new System.Text.StringBuilder();
                    while (Time.time - t0 < 6f && (arrivedStep < 0 || stepIndex - arrivedStep < 12))
                    {
                        yield return new WaitForFixedUpdate();
                        stepIndex++;
                        Vector3 flat = rb.position - exit; flat.y = 0f;
                        if (arrivedStep < 0 && flat.magnitude < 1f) arrivedStep = stepIndex;
                        if (arrivedStep >= 0) heights.Append($" {rb.position.y - FloorY(rb.position):0.00}");
                    }
                    Out(arrivedStep < 0
                        ? $"[portal] arrival: never travelled in 6 s\n  log: {TakeLogs()}"
                        : $"[portal] arrival, root above the floor at each physics step from arrival (0.50 = standing):{heights}\n  log: {TakeLogs()}");
                }
            }
        }

        if (Wanted("pin"))
        {
            Transform piece = Named(0, "Wall R 21m");
            Face(piece, out Vector3 face, out Vector3 inward, out _, out _);
            var wall = piece.GetComponent<BoxCollider>();

            // A knockback (the call Sonic Pulse makes on its victims, here without the stun) from 2 m into the wall.
            yield return Equip(14);
            Vector3 start = Stand(face + inward * 2f);
            yield return Place(start);
            string pushEnd = "still running";
            disp.Displace(-inward, 5f, 14f, end => pushEnd = end.Outcome.ToString());
            yield return new WaitForSeconds(0.6f);
            yield return PinnedDashes($"knockback ended {pushEnd}: {Where(start, -inward, wall)}", start, -inward, wall);

            // The zip gun at the wall from 8 m, then two dashes.
            yield return Equip(18);
            Vector3 zipStart = Stand(face + inward * 8f);
            yield return Place(zipStart);
            TakeLogs();
            yield return AimAt(face);
            Cast();
            yield return new WaitForSeconds(1.2f);
            string afterZip = Where(zipStart, -inward, wall) + " zip log: " + TakeLogs();
            yield return Equip(14);
            yield return PinnedDashes($"zip pull: {afterZip}", zipStart, -inward, wall);
        }

        if (Wanted("safetynet"))
        {
            Transform piece = Named(0, "Pocket Back 1");
            Face(piece, out Vector3 face, out Vector3 inward, out _, out float thick);
            var wall = piece.GetComponent<BoxCollider>();
            Vector3 inside = Stand(face + inward * 3f);
            yield return Place(inside);
            yield return new WaitForSeconds(0.5f);   // standing still, well inside: the spot to come back to
            Vector3 outside = Stand(face - inward * (thick + 2f));
            int returnsBefore = netReturns;
            disp.TeleportTo(outside);
            float t0 = Time.time, backAfter = -1f;
            while (Time.time - t0 < 1f)
            {
                yield return new WaitForFixedUpdate();
                if (backAfter < 0f && Vector3.Dot(rb.position - face, inward) > 0f) backAfter = Time.time - t0;
            }
            Out($"[safetynet] teleported to {outside:F2}, {thick + 2f:0.00} m past Pocket Back 1's inner face: back inside={(backAfter >= 0f)} after {backAfter:0.00} s; now {Where(inside, -inward, wall)}; returns logged {netReturns - returnsBefore}\n  log: {TakeLogs()}");
        }

        yield return Place(Stand(spawn));
    }
    finally
    {
        Application.logMessageReceived -= onLog;
        aim.SetAimOverride(null);
        motor.ExternalMotionControl = false;
        Out($"[summary] safety-net returns logged in this run: {netReturns}");
        System.IO.File.WriteAllText(donePath, "done");
    }
}
me.StartCoroutine(Run());
return "movement recorder started: " + LABEL + " only=" + ONLY;
```

- [ ] **Step 2: Fill the template.** In PowerShell, with `$S` set to SCRATCH:

```powershell
$S = "C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\movement"
(Get-Content "$S\movement_recorder_tpl.cs" -Raw) -replace '__LABEL__','before' -replace '__ONLY__','all' | Set-Content -Encoding utf8 "$S\movement_before.cs"
```

- [ ] **Step 3: Run it, one client in Play Mode.**
  1. The dirty check reads `False`. `unity command editor_play`, then poll `editor_status` until playing.
  2. Join:
     `unity command eval -- --code "Photon.Pun.PhotonNetwork.NickName = \"EditorHost\"; Photon.Pun.PhotonNetwork.JoinLobby(); return \"joining\";"`.
     If it isn't connected yet, poll `return Photon.Pun.PhotonNetwork.IsConnectedAndReady;` first. Then poll
     `return Photon.Pun.PhotonNetwork.InRoom + " " + (PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber) != null);`
     until `True True`.
  3. `unity command eval_file -- --file "$S\movement_before.cs" --timeout 20000` → `movement recorder started: before only=all`.
  4. Wait for the marker:
     `for ($i = 0; $i -lt 96 -and -not (Test-Path "$S\movement-before.done"); $i++) { Start-Sleep -Seconds 5 }`.
     If it never appears, run `unity command get_console_logs` and report the exception.
  5. Read `$S\movement-before.txt` in full.

- [ ] **Step 4: Report the before-numbers and check the predictions.** Quote each scenario's lines.
  - **sweep** (settles the unverified step):
    - At gap 0.75, `rb.SweepTestAll` lists `Pocket Back 1@≈0.05`.
    - **Predicted:** at gap 0.60, `overlapsWall=True depth≈0.10`, `rb.SweepTestAll` lists **no** `Pocket Back 1`,
      and `OverlapCapsule` lists it.
    - Also report what `CapsuleCastAll` lists at 0.70 and 0.60. Task 2's adapter doesn't depend on it, but the
      reviewer wants it.
  - **doubledash:**
    - **Predicted for the four thin walls in every third:**
      - dash 1 ends `centreVsNearFace≈-0.70`;
      - the walk-in reads `≈-0.60 IN`;
      - dash 2 is `spent=True`, `[DASH] ... outcome=Completed`, and ends `BEYOND`.
    - **The crate and the house:** dash 2 never `BEYOND`.
  - **hugdash:** `along` and `away` travel ≈ 3.0–3.15; `into` is `spent=True`. Open floor travels 3.0–3.15.
  - **lowedge:** the travel and root height for 0.20 / 0.35 / 0.50 m (the ride-over threshold).
  - **blink:** predicted `BEYOND` from all three gaps at both walls, and `BEYOND` over the crate.
  - **portal:**
    - behind the wall: portals `0 -> 1`, `OUTSIDE`;
    - behind the crate: `0 -> 1`;
    - arrival heights start near `0.00` (sunk), not `0.50`.
  - **pin:** the knockback ends `Blocked` at ≈ −0.70; report both dashes.
  - **safetynet:** `back inside=False`.
  - **STOP conditions (report to the controller, don't start Task 2):**
    - at gap 0.60, `rb.SweepTestAll` **does** list `Pocket Back 1`;
    - or no thin wall shows `BEYOND` after dash 2.

    Either means the mechanism isn't what Task 2 fixes.

- [ ] **Step 5: Stop Play Mode.** `unity command editor_stop`; poll `editor_status` until stopped; the dirty check reads
  `False`.

#### Part B: the remote-slide before-numbers (two clients; decision 14)

- [ ] **Step 6: Build Client2 from BASE.**
  1. Rule 16: `git status --short` must be empty. If it isn't, stop and ask.
  2. Not in Play Mode; the dirty check reads `False`.
  3. `unity command set_build_settings --settings '{"developmentBuild":true}' --confirm true`.
  4. `unity command build --target StandaloneWindows64 --outputPath "Builds/Client2/OverPower.exe" --options '["Development"]' --confirm true`,
     then poll `unity command build_status` every 10 s until `completed` (about 80 s). Report `result` and the errors count.

- [ ] **Step 7: Write the two-client scripts** (all in SCRATCH).

`remote_view_recorder_tpl.cs` — reflection only, so it runs in the Editor or a Player. Placeholders `__SIDE__ __OUT__ __DUR__`:

```csharp
// Per-physics-step recording of the OTHER player's copy on this client (movement steps 1 and 7).
string SIDE = "__SIDE__"; string OUT = @"__OUT__"; float DUR = __DUR__f;
var BF = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var inv = System.Globalization.CultureInfo.InvariantCulture;
var pnT = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
var pvT = System.Type.GetType("Photon.Pun.PhotonView, PhotonUnityNetworking");
var nsT = System.Type.GetType("PlayerNetSync, Overpower.Runtime");
var pmT = System.Type.GetType("PlayerMotor, Overpower.Runtime");
var phT = System.Type.GetType("PlayerHealth, Overpower.Runtime");
var rmT = System.Type.GetType("RoomManager, Overpower.Runtime");
var asT = System.Type.GetType("Overpower.Arena.ArenaSymmetry, Overpower.Runtime");
var serverTs = pnT.GetProperty("ServerTimestamp");
var isMineP = pvT.GetProperty("IsMine");
var npP = nsT.GetProperty("NetworkPosition");
var snapsP = pmT.GetProperty("RemoteSnapCount");         // null before movement step 5
var activeBoundsP = asT.GetProperty("ActiveBounds");     // null before movement step 3
var aliveP = phT.GetProperty("IsAlive");
var host = (MonoBehaviour)UnityEngine.Object.FindFirstObjectByType(rmT);

Component Remote()
{
    foreach (var o in UnityEngine.Object.FindObjectsByType(nsT, FindObjectsSortMode.None))
    {
        var c = (Component)o;
        if (!(bool)isMineP.GetValue(c.GetComponent(pvT))) return c;
    }
    return null;
}
float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

System.IO.File.WriteAllText(OUT, "side,st,ft,packet,npx,npz,rbx,rbz,jump,sincePacketMs,gap,stepMove,snaps,arenaSigned,alive,kinematic\n");

System.Collections.IEnumerator Rec()
{
    var sb = new System.Text.StringBuilder();
    float start = Time.time;
    Vector3 lastNp = new Vector3(float.NaN, 0f, 0f), lastRb = Vector3.zero;
    int lastPacketSt = 0, rows = 0, bigJumps = 0, outsideRows = 0;
    bool havePacket = false;
    float minSigned = float.MaxValue;
    while (Time.time - start < DUR)
    {
        yield return new WaitForFixedUpdate();
        Component remote = Remote();
        if (remote == null) continue;
        var body = remote.GetComponent<Rigidbody>();
        Vector3 np = (Vector3)npP.GetValue(remote);
        Vector3 pos = body.position;
        int st = (int)serverTs.GetValue(null);
        bool packet = !(np == lastNp);
        float jump = packet && havePacket ? Flat(np, lastNp) : 0f;
        int sincePacket = packet && havePacket ? unchecked(st - lastPacketSt) : -1;
        if (packet)
        {
            if (havePacket && jump > 3f) bigJumps++;
            lastNp = np; lastPacketSt = st; havePacket = true;
        }
        float gap = Flat(np, pos);
        float stepMove = rows > 0 ? Flat(pos, lastRb) : 0f;
        lastRb = pos;
        object snaps = snapsP != null ? snapsP.GetValue(remote.GetComponent(pmT)) : (object)(-1);
        string signedText = "n/a";
        object bounds = activeBoundsP != null ? activeBoundsP.GetValue(null) : null;
        if (bounds != null)
        {
            float signed = (float)bounds.GetType().GetMethod("SignedDistance", new[] { typeof(Vector3) }).Invoke(bounds, new object[] { pos });
            signedText = signed.ToString("0.000", inv);
            minSigned = Mathf.Min(minSigned, signed);
            if (signed < 0f) outsideRows++;
        }
        bool alive = (bool)aliveP.GetValue(remote.GetComponent(phT));
        sb.Append(SIDE).Append(',').Append(st).Append(',').Append(Time.fixedTime.ToString("0.000", inv)).Append(',')
          .Append(packet ? 1 : 0).Append(',').Append(np.x.ToString("0.000", inv)).Append(',').Append(np.z.ToString("0.000", inv)).Append(',')
          .Append(pos.x.ToString("0.000", inv)).Append(',').Append(pos.z.ToString("0.000", inv)).Append(',')
          .Append(jump.ToString("0.000", inv)).Append(',').Append(sincePacket).Append(',').Append(gap.ToString("0.000", inv)).Append(',')
          .Append(stepMove.ToString("0.000", inv)).Append(',').Append(snaps).Append(',').Append(signedText).Append(',')
          .Append(alive).Append(',').Append(body.isKinematic).Append('\n');
        rows++;
        if (sb.Length > 4000) { System.IO.File.AppendAllText(OUT, sb.ToString()); sb.Length = 0; }
    }
    sb.Append("#END ").Append(SIDE).Append(" rows=").Append(rows).Append(" bigJumps=").Append(bigJumps)
      .Append(" minArenaSigned=").Append(minSigned == float.MaxValue ? "n/a" : minSigned.ToString("0.000", inv))
      .Append(" outsideRows=").Append(outsideRows).Append('\n');
    System.IO.File.AppendAllText(OUT, sb.ToString());
}
host.StartCoroutine(Rec());
return "remote view recorder started side=" + SIDE + " out=" + OUT;
```

`movement_driver_tpl.cs` — the owner's scripted moves, reflection only. Placeholders `__SIDE__ __OUT__ __PLAN__`:

```csharp
// Scripted owner-side moves for the two-client check (movement steps 1 and 7).
// PLAN tokens, run in order, ';' separated: wait:<s>  tele:<m>  spawn  dash  blink  zip  portal  die  wallrun  end
string SIDE = "__SIDE__"; string OUT = @"__OUT__"; string PLAN = "__PLAN__";
var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var inv = System.Globalization.CultureInfo.InvariantCulture;
System.Type T(string n) { var t = System.Type.GetType(n + ", Overpower.Runtime"); if (t == null) throw new System.Exception("no type " + n); return t; }
var pnT = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
var pvT = System.Type.GetType("Photon.Pun.PhotonView, PhotonUnityNetworking");
var phT = T("PlayerHealth"); var pdT = T("PlayerDisplacement"); var pmT = T("PlayerMotor"); var aimT = T("PlayerAim");
var loT = T("PlayerLoadout"); var arT = T("AbilityRunner"); var amT = T("Overpower.Abilities.AbilityModule");
var slotT = T("Overpower.Data.AbilitySlot"); var rmT = T("RoomManager"); var asT = T("Overpower.Arena.ArenaSymmetry");
var diT = T("Overpower.Combat.DamageInfo"); var dsT = T("Overpower.Combat.DamageSource"); var portalT = T("Overpower.Abilities.Portal");
var serverTs = pnT.GetProperty("ServerTimestamp");
object Mobility = System.Enum.ToObject(slotT, 3);
int buildingMask = LayerMask.GetMask("Building");
int solidMask = LayerMask.GetMask("Default", "Building");
System.IO.File.WriteAllText(OUT, "");
void L(string s) => System.IO.File.AppendAllText(OUT, SIDE + " st=" + serverTs.GetValue(null) + " t=" + Time.time.ToString("0.000", inv) + " " + s + "\n");

Component Me()
{
    foreach (var o in UnityEngine.Object.FindObjectsByType(phT, FindObjectsSortMode.None))
    {
        var c = (Component)o;
        if ((bool)pvT.GetProperty("IsMine").GetValue(c.GetComponent(pvT))) return c;
    }
    return null;
}
var me = Me();
var rb = me.GetComponent<Rigidbody>();
var capsule = me.GetComponent<CapsuleCollider>();
object disp = me.GetComponent(pdT), motor = me.GetComponent(pmT), aimer = me.GetComponent(aimT);
object loadout = me.GetComponent(loT), runner = me.GetComponent(arT), health = me.GetComponent(phT);
var host = (MonoBehaviour)me;
bool Alive() => (bool)phT.GetProperty("IsAlive").GetValue(health);
bool TeleportTo(Vector3 p) => (bool)pdT.GetMethod("TeleportTo").Invoke(disp, new object[] { p });
void Aim(Vector3? p) => aimT.GetMethod("SetAimOverride").Invoke(aimer, new object[] { p });
object Module() => arT.GetMethod("StatusFor").Invoke(runner, new object[] { Mobility });
void Equip(int id) => loT.GetMethod("SetAbility").Invoke(loadout, new object[] { Mobility, id });
void Refill() { var m = Module(); if (m != null) amT.GetMethod("RefillCharges", NP).Invoke(m, null); }
void Cast() { var m = Module(); if (m != null) arT.GetMethod("TryCast", NP).Invoke(runner, new object[] { m }); }
float FloorOffset() => capsule.height * 0.5f - capsule.center.y;

bool OpenDirection(float metres, out Vector3 dir)
{
    for (int i = 0; i < 16; i++)
    {
        dir = Quaternion.Euler(0f, 22.5f * i, 0f) * Vector3.forward;
        if (!Physics.SphereCast(rb.position + Vector3.up, capsule.radius + 0.1f, dir, out _, metres, solidMask, QueryTriggerInteraction.Ignore))
            return true;
    }
    dir = Vector3.forward;
    return false;
}

System.Collections.IEnumerator Tele(float metres)
{
    if (!OpenDirection(metres + 1f, out Vector3 dir)) { L("tele: no open direction"); yield break; }
    Vector3 to = rb.position + dir * metres;
    L($"tele {metres:0.0} m from {rb.position:F2} to {to:F2} ok={TeleportTo(to)}");
    yield return new WaitForSeconds(0.2f);
}

System.Collections.IEnumerator Spawn()
{
    var rooms = UnityEngine.Object.FindFirstObjectByType(rmT);
    var spawns = (Transform[])rmT.GetField("teamSpawnPoints").GetValue(rooms);
    int team = (int)phT.GetProperty("TeamId").GetValue(health);
    Vector3 to = spawns[Mathf.Clamp(team, 0, spawns.Length - 1)].position;
    L($"tele spawn from {rb.position:F2} to {to:F2} ok={TeleportTo(to)}");
    yield return new WaitForSeconds(0.5f);
}

System.Collections.IEnumerator Dash()
{
    Equip(14); yield return null; Refill();
    if (!OpenDirection(4f, out Vector3 dir)) { L("dash: no open direction"); yield break; }
    Aim(rb.position + dir * 6f); yield return null; yield return null;
    Vector3 from = rb.position;
    L("dash start"); Cast();
    yield return new WaitForSeconds(0.6f);
    L($"dash end travelled={Vector3.Distance(from, rb.position):0.00}");
    Aim(null);
}

System.Collections.IEnumerator Blink()
{
    Equip(15); yield return null; Refill();
    if (!OpenDirection(9f, out Vector3 dir)) { L("blink: no open direction"); yield break; }
    Aim(rb.position + dir * 8f); yield return null; yield return null;
    Vector3 from = rb.position;
    L("blink cast"); Cast();
    yield return new WaitForSeconds(0.4f);
    L($"blink end jumped={Vector3.Distance(from, rb.position):0.00}");
    Aim(null);
}

System.Collections.IEnumerator Zip()
{
    Equip(18); yield return null; Refill();
    Vector3 target = Vector3.zero; bool found = false;
    for (int i = 0; i < 16 && !found; i++)
    {
        Vector3 dir = Quaternion.Euler(0f, 22.5f * i, 0f) * Vector3.forward;
        if (Physics.Raycast(rb.position + Vector3.up, dir, out RaycastHit hit, 14f, buildingMask, QueryTriggerInteraction.Ignore) && hit.distance > 6f)
        { target = hit.point; found = true; }
    }
    if (!found) { L("zip: no wall 6-14 m away"); yield break; }
    Aim(new Vector3(target.x, rb.position.y, target.z)); yield return null; yield return null;
    Vector3 from = rb.position;
    L($"zip start at wall {target:F2}"); Cast();
    yield return new WaitForSeconds(1.6f);
    L($"zip end travelled={Vector3.Distance(from, rb.position):0.00}");
    Aim(null);
}

System.Collections.IEnumerator PortalTravel()
{
    Equip(17); yield return null; Refill();
    if (!OpenDirection(4f, out Vector3 dir)) { L("portal: no open direction"); yield break; }
    if (Physics.SphereCast(rb.position + Vector3.up, capsule.radius + 0.1f, -dir, out _, 4f, solidMask, QueryTriggerInteraction.Ignore))
    { L("portal: no room behind"); yield break; }
    Vector3 here = rb.position;
    Aim(here + dir * 3f); yield return null; yield return null; Cast();
    yield return new WaitForSeconds(0.4f);
    Aim(here - dir * 3f); yield return null; yield return null; Cast();
    yield return new WaitForSeconds(0.4f);
    Aim(null);
    int actor = (int)pvT.GetProperty("OwnerActorNr").GetValue(me.GetComponent(pvT));
    var gates = new System.Collections.Generic.List<Component>();
    foreach (object g in (System.Collections.IEnumerable)portalT.GetMethod("ForOwner").Invoke(null, new object[] { actor })) gates.Add((Component)g);
    L($"portal placed gates={gates.Count}");
    if (gates.Count < 2) yield break;
    Vector3 entry = gates[0].transform.position;
    TeleportTo(new Vector3(entry.x, entry.y + FloorOffset(), entry.z));
    Vector3 stoodAt = rb.position;
    L($"portal stand in {entry:F2}");
    float t0 = Time.time;
    while (Time.time - t0 < 6f && Vector3.Distance(rb.position, stoodAt) < 3f) yield return new WaitForFixedUpdate();
    L($"portal travelled={(Vector3.Distance(rb.position, stoodAt) >= 3f)} jumped={Vector3.Distance(rb.position, stoodAt):0.00}");
    yield return new WaitForSeconds(0.5f);
}

System.Collections.IEnumerator Die()
{
    object info = System.Activator.CreateInstance(diT, new object[] { 1000f, -1, -1, 0, System.Enum.ToObject(dsT, 4), true, rb.position, -1 });
    Vector3 deathSpot = rb.position;
    L("die");
    phT.GetMethod("ApplyDamage").Invoke(health, new object[] { info });
    float t0 = Time.time;
    while (Time.time - t0 < 25f && Alive()) yield return null;
    L($"dead={!Alive()} at {rb.position:F2}");
    while (Time.time - t0 < 25f && !Alive()) yield return new WaitForFixedUpdate();
    L($"respawned alive={Alive()} jumped={Vector3.Distance(deathSpot, rb.position):0.00}");
    yield return new WaitForSeconds(0.5f);
}

// Every way out of the arena, in one go: double dash through a thin wall, then a blink straight through it.
System.Collections.IEnumerator WallRun()
{
    var arena = UnityEngine.Object.FindFirstObjectByType(asT);
    var source = (Transform)asT.GetField("source").GetValue(arena);
    Transform piece = null;
    foreach (Transform t in source.GetComponentsInChildren<Transform>(true)) if (t.name == "Pocket Back 1") piece = t;
    if (piece == null) { L("wallrun: no Pocket Back 1"); yield break; }
    var box = piece.GetComponent<BoxCollider>();
    Vector3 face = piece.TransformPoint(box.center + new Vector3(0f, 0f, box.size.z * 0.5f));
    Vector3 inward = piece.forward; inward.y = 0f; inward.Normalize();
    Vector3 start = new Vector3(face.x, rb.position.y, face.z) + inward * 2f;
    if (Physics.Raycast(new Vector3(start.x, 8f, start.z), Vector3.down, out RaycastHit floor, 20f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
        start.y = floor.point.y + FloorOffset();
    L($"wallrun start {start:F2} ok={TeleportTo(start)}");
    yield return new WaitForSeconds(0.6f);
    Equip(14); yield return null; Refill();
    Aim(start - inward * 6f); yield return null; yield return null;
    L("wallrun dash 1"); Cast();
    yield return new WaitForSeconds(0.6f);
    pmT.GetProperty("ExternalMotionControl").SetValue(motor, true);
    float speed = (float)pmT.GetProperty("CurrentSpeed").GetValue(motor);
    float end = Time.time + 0.4f;
    while (Time.time < end) { yield return new WaitForFixedUpdate(); rb.MovePosition(rb.position - inward * speed * Time.fixedDeltaTime); }
    pmT.GetProperty("ExternalMotionControl").SetValue(motor, false);
    Aim(rb.position - inward * 6f); yield return null; yield return null;
    L("wallrun dash 2"); Cast();
    yield return new WaitForSeconds(0.9f);
    L($"wallrun after dashes centrePastInnerFace={Vector3.Dot(face - rb.position, inward):+0.00;-0.00}");
    Equip(15); yield return null; Refill();
    Aim(rb.position - inward * 9f); yield return null; yield return null;
    L("wallrun blink"); Cast();
    yield return new WaitForSeconds(0.6f);
    L($"wallrun after blink centrePastInnerFace={Vector3.Dot(face - rb.position, inward):+0.00;-0.00}");
    Aim(null);
    yield return Spawn();
}

System.Collections.IEnumerator Drive()
{
    L("driver start plan=" + PLAN);
    foreach (string raw in PLAN.Split(';'))
    {
        string token = raw.Trim();
        if (token.Length == 0) continue;
        string[] p = token.Split(':');
        if (!Alive() && p[0] != "wait" && p[0] != "end" && p[0] != "die") { L("skipped while dead: " + token); continue; }
        switch (p[0])
        {
            case "wait": yield return new WaitForSeconds(float.Parse(p[1], inv)); break;
            case "tele": yield return Tele(float.Parse(p[1], inv)); break;
            case "spawn": yield return Spawn(); break;
            case "dash": yield return Dash(); break;
            case "blink": yield return Blink(); break;
            case "zip": yield return Zip(); break;
            case "portal": yield return PortalTravel(); break;
            case "die": yield return Die(); break;
            case "wallrun": yield return WallRun(); break;
            case "end": L("driver end"); yield break;
        }
    }
    L("driver end");
}
host.StartCoroutine(Drive());
return "movement driver started " + SIDE;
```

`join_room_tpl.cs` (placeholder `__ROOM__`) and `room_status.cs`, both reflection only:

```csharp
// join_room_tpl.cs
var ty = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
ty.GetProperty("NickName").SetValue(null, "Client2");
var join = ty.GetMethod("JoinRoom", new System.Type[] { typeof(string), typeof(string[]) });
return "join sent=" + join.Invoke(null, new object[] { "__ROOM__", null }) + " state=" + ty.GetProperty("NetworkClientState").GetValue(null);
```

```csharp
// room_status.cs
var ty = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
object room = ty.GetProperty("CurrentRoom").GetValue(null);
return "ready=" + ty.GetProperty("IsConnectedAndReady").GetValue(null) + " inRoom=" + ty.GetProperty("InRoom").GetValue(null) +
       (room == null ? " room=-" : " room=" + room.GetType().GetProperty("Name").GetValue(room) + " players=" + room.GetType().GetProperty("PlayerCount").GetValue(room));
```

`analyze_movement.py`:

```python
# Joins the observer's per-step CSV with the owner's driver log (movement steps 1 and 7).
# usage: python analyze_movement.py <observer.csv> <driver.log> <out.txt> [extraLagMs]
import csv, re, sys

obs, drv, out = sys.argv[1], sys.argv[2], sys.argv[3]
lag = int(sys.argv[4]) if len(sys.argv) > 4 else 0
rows = [r for r in csv.DictReader(l for l in open(obs, encoding='utf-8') if not l.startswith('#'))]
for r in rows:
    for k in ('st', 'packet', 'sincePacketMs', 'snaps'):
        r[k] = int(r[k])
    for k in ('jump', 'gap', 'stepMove'):
        r[k] = float(r[k])

events = []
for line in open(drv, encoding='utf-8'):
    m = re.match(r'\S+ st=(-?\d+) t=\S+ (.*)', line.strip())
    if m:
        events.append((int(m.group(1)), m.group(2)))

def window(a, b):
    return [(i, r) for i, r in enumerate(rows) if a <= r['st'] <= b]

report = []
for st, text in events:
    if text.startswith('tele '):               kind, a, b = 'tele', st - 150, st + 450 + lag
    elif text.startswith('blink cast'):        kind, a, b = 'blink', st - 50, st + 600 + lag
    elif text.startswith('portal travelled=True'): kind, a, b = 'portal', st - 150, st + 450 + lag
    elif text.startswith('respawned alive=True'):  kind, a, b = 'respawn', st - 150, st + 600 + lag
    elif text.startswith('dash start') or text.startswith('wallrun dash'): kind, a, b = 'dash', st, st + 700 + lag
    elif text.startswith('zip start'):         kind, a, b = 'zip', st, st + 1700 + lag
    else: continue
    ws = window(a, b)
    if not ws:
        report.append('%-8s st=%d: NO ROWS' % (kind, st)); continue
    first, last = ws[0][0], ws[-1][0]
    snaps = None if rows[first]['snaps'] < 0 else rows[last]['snaps'] - rows[max(first - 1, 0)]['snaps']
    if kind in ('dash', 'zip'):
        verdict = 'n/a (before the fix)' if snaps is None else ('PASS' if snaps == 0 else 'FAIL: snapped')
        report.append('%-8s st=%d: snaps=%s maxJump=%.2f maxGap=%.2f maxStepMove=%.2f %s' % (
            kind, st, snaps, max(r['jump'] for _, r in ws), max(r['gap'] for _, r in ws),
            max(r['stepMove'] for _, r in ws), verdict))
        continue
    j, pk = max(ws, key=lambda ir: ir[1]['jump'])
    nxt = rows[j + 1] if j + 1 < len(rows) else pk
    until = next((k for k in range(0, 80) if j + k < len(rows) and rows[j + k]['gap'] < 0.1), None)
    if pk['jump'] <= 3.0:       verdict = 'SHORT: no jump over 3 m in this window'
    elif snaps is None:         verdict = 'before the fix'
    else:                       verdict = 'PASS' if (nxt['gap'] <= 0.1 and snaps >= 1) else 'FAIL'
    report.append('%-8s st=%d: jump=%.2f sincePacketMs=%d errPacketStep=%.3f errNextStep=%.3f stepsUntil0.1=%s snaps=%s %s' % (
        kind, st, pk['jump'], pk['sincePacketMs'], pk['gap'], nxt['gap'], until, snaps, verdict))

signed = [float(r['arenaSigned']) for r in rows if r['arenaSigned'] != 'n/a']
report.append('rows=%d minArenaSigned=%s outsideRows=%d' % (
    len(rows), ('%.3f' % min(signed)) if signed else 'n/a', sum(1 for s in signed if s < 0)))
open(out, 'w', encoding='utf-8').write('\n'.join(report) + '\n')
print('\n'.join(report))
```

- [ ] **Step 8: Run the two-client session (before).**
  1. Editor: dirty check `False`, `editor_play`, join (Step 3), then read the room name:
     `unity command eval -- --code "return Photon.Pun.PhotonNetwork.CurrentRoom.Name;"`.
  2. Launch Client2 (its window may appear over Tudor's work; never click it):
     `Start-Process "Builds\Client2\OverPower.exe" -ArgumentList "-screen-fullscreen 0 -screen-width 800 -screen-height 600 -logFile Builds\Client2\player.log"`.
  3. Poll `unity command --runtime-path "Builds/Client2" eval_file -- --file "$S\room_status.cs" --timeout 20000`
     until `ready=True`, then fill `join_room_tpl.cs` with the room name and run it on Client2. Poll both sides until
     each reads `inRoom=True ... players=2`.
  4. Recorder on the Editor (the observer):
     fill `remote_view_recorder_tpl.cs` with `__SIDE__` = `E`, `__OUT__` = `$S\remote-before-E.csv`, `__DUR__` = `120`,
     then `unity command eval_file -- --file "$S\remote_before_E.cs" --timeout 20000`.
  5. Driver on Client2 (the owner): fill `movement_driver_tpl.cs` with `__SIDE__` = `P`,
     `__OUT__` = `$S\driver-before-P.log`, and
     `__PLAN__` = `wait:3;spawn;wait:2;tele:8;wait:2;tele:8;wait:2;dash;wait:1.5;dash;wait:1.5;blink;wait:2;spawn;wait:1;zip;wait:2;spawn;wait:1;portal;wait:2;die;wait:1;wallrun;wait:2;end`,
     then run it with `--runtime-path "Builds/Client2" eval_file`.
  6. Wait for `driver end` in the log and `#END` in the CSV:
     `for ($i = 0; $i -lt 40 -and -not (Select-String -Path "$S\remote-before-E.csv" -Pattern '#END' -Quiet); $i++) { Start-Sleep -Seconds 5 }`.
  7. `python "$S\analyze_movement.py" "$S\remote-before-E.csv" "$S\driver-before-P.log" "$S\remote-before.txt"` and read it.

- [ ] **Step 9: Shut down.** `Stop-Process -Name OverPower -Force -ErrorAction SilentlyContinue`, then
  `Stop-Process -Name UnityCrashHandler64 -Force -ErrorAction SilentlyContinue`, then `unity command editor_stop`,
  poll until stopped, dirty check `False`.

- [ ] **Step 10: Report.** Per teleport-type event: `jump`, `errNextStep` and `stepsUntil0.1` (predicted: the error
  stays metres wide for ~13–20 steps, about 0.25 s). Also report `minArenaSigned` (`n/a` before movement step 3) and
  whether `wallrun` put the owner outside. Nothing is committed; put the numbers in `progress.md`.

---

### Task 2 (movement step 2): one stop rule for every displacement, and a dash that refuses to go nowhere

**What exists:**
- `PlayerDisplacement.FixedUpdate` (`:106-147`) sweeps with `rb.SweepTestAll` and stops exactly on contact.
- `IsBlocker` (`:262-277`) holds the layer, floor-normal and own-body rules.
- `DashAbility.TryBuildCast` (`:65-88`) never asks whether the dash can move.
- `DummyTarget.cs:124-125,326` name `SweepTestAll` and `IsBlocker` in comments.

**Files:**
- Create: `Assets/scripts/Combat/DisplacementSweepRule.cs`, `Assets/Tests/DisplacementSweepRuleTests.cs`
- Modify: `Assets/scripts/Player/PlayerDisplacement.cs`, `Assets/scripts/Abilities/Mobility/DashAbility.cs`
- Modify (comments only): `Assets/scripts/TestRange/DummyTarget.cs`

- [ ] **Step 1: Failing `DisplacementSweepRuleTests`.** Create `Assets/Tests/DisplacementSweepRuleTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The stop rule behind every displacement (movement step 2). "East" is the move; a wall to the east reports a
    /// normal pointing west, and physics would push a capsule inside it back west.
    /// </summary>
    public class DisplacementSweepRuleTests
    {
        private static readonly Vector3 East = Vector3.right;
        private static readonly Vector3 West = Vector3.left;

        private static float Allowed(float requested, Vector3 move, out int blocker, params SweepContact[] contacts) =>
            DisplacementSweepRule.AllowedTravel(requested, new List<SweepContact>(contacts), move, out blocker);

        [Test]
        public void NothingInTheWayAllowsTheWholeStep()
        {
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void AWallAheadStopsASkinWidthShortOfIt()
        {
            Assert.AreEqual(0.95f, Allowed(2f, East, out int blocker, SweepContact.Hit(1f, West)), 1e-5f);
            Assert.AreEqual(0, blocker);
        }

        [Test]
        public void AWallCloserThanTheSkinAllowsNoTravelAtAll()
        {
            Assert.AreEqual(0f, Allowed(0.36f, East, out int blocker, SweepContact.Hit(0.03f, West)), 1e-5f);
            Assert.AreEqual(0, blocker);
        }

        [Test]
        public void AWallBeyondTheStepDoesNotShortenIt()
        {
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker, SweepContact.Hit(0.5f, West)), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void GroundUnderfootNeverBlocks()
        {
            var slope = new Vector3(-0.43f, 0.9f, 0f).normalized;
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker, SweepContact.Hit(0.1f, slope)), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void GroundInFrontDoesNotHideAWallBehindIt()
        {
            float allowed = Allowed(2f, East, out int blocker, SweepContact.Hit(0.2f, Vector3.up), SweepContact.Hit(0.6f, West));
            Assert.AreEqual(0.55f, allowed, 1e-5f);
            Assert.AreEqual(1, blocker);
        }

        [Test]
        public void TheNearestWallWinsWhateverOrderTheyArriveIn()
        {
            float allowed = Allowed(3f, East, out int blocker, SweepContact.Hit(1.5f, West), SweepContact.Hit(0.8f, West));
            Assert.AreEqual(0.75f, allowed, 1e-5f);
            Assert.AreEqual(1, blocker);
        }

        [Test]
        public void StartingInsideAWallAndMovingDeeperStopsWhereYouStand()
        {
            // Walking presses a player about 0.1 m into a wall; the dash that follows must go nowhere.
            Assert.AreEqual(0f, Allowed(0.36f, East, out int blocker, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(0, blocker);
        }

        [Test]
        public void StartingInsideAWallAndMovingAwayOrAlongIsNotBlocked()
        {
            Assert.AreEqual(0.36f, Allowed(0.36f, West, out int away, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(-1, away);
            Assert.AreEqual(0.36f, Allowed(0.36f, Vector3.forward, out int along, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(-1, along);
        }

        [Test]
        public void ShallowIntoTheWallIsStillAlongIt()
        {
            // About 3 degrees into a wall you are touching still counts as running along it; 17 degrees does not.
            Vector3 shallow = new Vector3(0.05f, 0f, 1f).normalized;
            Vector3 steeper = new Vector3(0.3f, 0f, 1f).normalized;
            Assert.AreEqual(0.36f, Allowed(0.36f, shallow, out int soft, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(-1, soft);
            Assert.AreEqual(0f, Allowed(0.36f, steeper, out int hard, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(0, hard);
        }

        [Test]
        public void AnOverlapPhysicsCantExplainNeverBlocks()
        {
            // ComputePenetration found nothing at the probe pose: no push-out, so no reason to stop.
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker, SweepContact.InsideWithoutPushOut()), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void AMoveThatCoversLessThanACentimetreIsNotWorthStarting()
        {
            Assert.IsFalse(DisplacementSweepRule.IsUsefulTravel(0f));
            Assert.IsFalse(DisplacementSweepRule.IsUsefulTravel(0.009f));
            Assert.IsTrue(DisplacementSweepRule.IsUsefulTravel(0.01f));
        }
    }
}
```

- [ ] **Step 2: Recompile and confirm the failing state.** `unity command recompile`, poll `recompile_status`.
  Expected: errors naming `DisplacementSweepRule` and `SweepContact`, and nothing else.

- [ ] **Step 3: Create `Assets/scripts/Combat/DisplacementSweepRule.cs`.**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One thing a displacement's capsule sweep touched, reduced to the facts the stop rule needs. Built by
    /// PlayerDisplacement from a real overlap or capsule cast; plain data, so the rule is tested without a scene.
    /// </summary>
    public readonly struct SweepContact
    {
        /// <summary>How far along the move the capsule reaches it. 0 when the capsule already overlaps it.</summary>
        public readonly float Distance;

        /// <summary>The surface normal the cast reported. Meaningless when StartsInside: Unity reports minus the sweep
        /// direction there, which says nothing about which side the wall is on.</summary>
        public readonly Vector3 Normal;

        /// <summary>The capsule already touches or overlaps this collider before the move.</summary>
        public readonly bool StartsInside;

        /// <summary>Physics found an overlap one skin width along the move, so PushOut means something.</summary>
        public readonly bool HasPushOut;

        /// <summary>Unit direction physics would push the capsule out of this collider.</summary>
        public readonly Vector3 PushOut;

        public SweepContact(float distance, Vector3 normal, bool startsInside, bool hasPushOut, Vector3 pushOut)
        {
            Distance = distance;
            Normal = normal;
            StartsInside = startsInside;
            HasPushOut = hasPushOut;
            PushOut = pushOut;
        }

        public static SweepContact Hit(float distance, Vector3 normal) => new SweepContact(distance, normal, false, false, Vector3.zero);

        public static SweepContact Inside(Vector3 pushOut) => new SweepContact(0f, Vector3.zero, true, true, pushOut);

        public static SweepContact InsideWithoutPushOut() => new SweepContact(0f, Vector3.zero, true, false, Vector3.zero);
    }

    /// <summary>
    /// Where a displacement (a dash, a zip pull, a knockback) has to stop. One rule for all three, because they all
    /// travel through PlayerDisplacement - see IDisplaceable's class comment for why one mover exists.
    ///
    /// Three parts, each for a way a player used to end up somewhere they shouldn't:
    ///  - a wall ahead stops the move a SKIN width short, instead of exactly on contact, so the next step doesn't start
    ///    from inside the wall's contact;
    ///  - a wall the capsule ALREADY overlaps blocks only a move that goes deeper into it, judged by the direction
    ///    physics would push the capsule out (a dash along a wall you are pressed against still works);
    ///  - the floor underfoot is never a wall (its normal points roughly up).
    ///
    /// Plain C#, tested in edit mode (DisplacementSweepRuleTests): "can a second dash pass through a thin wall" is
    /// provable without a scene, a Rigidbody or a physics step.
    /// </summary>
    public static class DisplacementSweepRule
    {
        /// <summary>Metres a move stops short of the wall it hits. Not a tuning value: it is the gap that keeps the
        /// next step's queries from starting flush against that wall.</summary>
        public const float SkinMetres = 0.05f;

        /// <summary>Metres the swept capsule is raised, so the floor the player stands on is neither an overlap nor a
        /// hit. Not a tuning value.</summary>
        public const float LiftMetres = 0.05f;

        /// <summary>Above this, a hit's normal points up enough to be the ground rather than a wall. Kept from the
        /// original IsBlocker, where it was already not a tuning value.</summary>
        public const float FloorNormalY = 0.5f;

        /// <summary>How much of the move must point against a touching wall's push-out before it counts as going INTO
        /// it: about 6 degrees. Not a tuning value - it is the tolerance that lets a dash run along a wall the player
        /// is pressed against.</summary>
        public const float IntoSurfaceDot = -0.1f;

        /// <summary>A move that covers less than this isn't movement. Not a tuning value.</summary>
        public const float MinUsefulTravelMetres = 0.01f;

        /// <summary>Does this contact stop a move in <paramref name="moveDirection"/> (unit length)?</summary>
        public static bool Blocks(in SweepContact contact, Vector3 moveDirection)
        {
            if (contact.StartsInside)
                return contact.HasPushOut && Vector3.Dot(moveDirection, contact.PushOut) < IntoSurfaceDot;

            return contact.Normal.y <= FloorNormalY;
        }

        /// <summary>
        /// How far the move may go, and which contact stops it (-1 when none does). A contact that only stops the move
        /// beyond what was requested is not a blocker: nothing is in the way this step.
        /// </summary>
        public static float AllowedTravel(float requested, IReadOnlyList<SweepContact> contacts, Vector3 moveDirection,
                                           out int blockerIndex)
        {
            blockerIndex = -1;
            float allowed = Mathf.Max(0f, requested);
            if (contacts == null)
                return allowed;

            for (int i = 0; i < contacts.Count; i++)
            {
                SweepContact contact = contacts[i];
                if (!Blocks(contact, moveDirection))
                    continue;

                float stop = contact.StartsInside ? 0f : Mathf.Max(0f, contact.Distance - SkinMetres);
                if (stop < allowed)
                {
                    allowed = stop;
                    blockerIndex = i;
                }
            }

            return allowed;
        }

        /// <summary>Worth starting at all - see MinUsefulTravelMetres.</summary>
        public static bool IsUsefulTravel(float allowed) => allowed >= MinUsefulTravelMetres;
    }
}
```

- [ ] **Step 4: Recompile, dirty check (`False`), run the tests async.** Expected: compiles clean, every test passes.
  Report the total and the +12.

- [ ] **Step 5: `PlayerDisplacement`.**
  1. Add `using System.Collections.Generic;` under `using System;`.
  2. **Delete** the `FloorNormalYThreshold` block (`:34-37`, comment included). `DisplacementSweepRule.FloorNormalY`
     is its home now.
  3. After `private int blockMask;`, add:

```csharp
    // The player's own capsule: the shape every step is swept with (movement step 2). Read once in Awake, so what a
    // move is checked against is always the shape physics pushes around, never a second Inspector radius.
    private CapsuleCollider capsule;

    // Reused every physics step, so a move in flight allocates nothing. A player's capsule never touches 16 things at
    // once; anything past that is missed rather than allocated for.
    private readonly Collider[] overlapHits = new Collider[16];
    private readonly RaycastHit[] sweepHits = new RaycastHit[16];
    private readonly List<SweepContact> sweepContacts = new List<SweepContact>(16);
    private readonly List<Collider> sweepColliders = new List<Collider>(16);
```

  4. In `Awake`, after `blockMask = LayerMask.GetMask("Default", "Building");`:

```csharp
        capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
            Debug.LogError($"[PlayerDisplacement] {name}: CapsuleCollider is missing - no move can be checked against a wall.");
```

  5. Replace the whole of `FixedUpdate` (`:106-147`) with:

```csharp
    private void FixedUpdate()
    {
        if (!photonView.IsMine || activeKind == null)
            return;

        float step = Mathf.Min(speed * Time.fixedDeltaTime, remainingDistance);
        Vector3 fromPosition = rb.position;

        // One stop rule for a dash, a zip pull and a knockback (DisplacementSweepRule, movement step 2). The player's
        // own capsule is swept along the step and stops a skin width short of the first wall, and a wall the capsule
        // already overlaps blocks only a move deeper into it. Rigidbody.SweepTestAll, which this replaces, stopped
        // exactly on contact and did not report a wall the capsule had already been pressed into by walking - so a
        // second dash from there went straight through a thin (0.72 m) wall (measured, movement step 1).
        float allowed = AllowedTravel(fromPosition, direction, step, out Collider blocker);
        if (blocker != null)
        {
            Vector3 blockedPosition = fromPosition + direction * allowed;
            rb.MovePosition(blockedPosition);
            Finish(DisplaceOutcome.Blocked, blocker, blockedPosition);
            return;
        }

        // MovePosition on this Rigidbody (dynamic, gravity on, not kinematic) is solved by PhysX alongside whatever
        // contacts it is already resting in - not applied as a bare teleport - so a run can overshoot its requested
        // distance by a few centimetres (measured: 3.115m for a requested 3m). Accepted rather than snapped to the
        // exact figure: snapping would fight the same contact solving that keeps the capsule from sinking into the
        // floor it stands on.
        Vector3 nextPosition = fromPosition + direction * step;
        rb.MovePosition(nextPosition);
        remainingDistance -= step;

        if (remainingDistance <= 0f)
            Finish(DisplaceOutcome.Completed, null, nextPosition);
    }
```

  **If movement step 1 found something different, correct that comment to what it measured.**

  6. After the `CanTeleport` property, add:

```csharp
    /// <summary>
    /// Read-only query for a dash to check BEFORE it spends its charge (movement step 2): false while a knockback is
    /// running, and false when a Voluntary move along <paramref name="direction"/> could not cover even a centimetre
    /// because the player is touching, or pressed into, a wall that way. It asks the same rule the move itself asks
    /// every step, so "refused" and "would have gone nowhere" cannot disagree.
    /// </summary>
    public bool CanStartVoluntary(Vector3 direction)
    {
        if (!photonView.IsMine || rb == null)
            return false;

        if (!DisplacementPriority.Accepts(activeKind, DisplaceKind.Voluntary))
            return false;

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        float allowed = AllowedTravel(rb.position, dir, DisplacementSweepRule.MinUsefulTravelMetres, out _);
        return DisplacementSweepRule.IsUsefulTravel(allowed);
    }
```

  7. Replace `IsBlocker` (`:262-277`, its doc comment included) with:

```csharp
    /// <summary>
    /// How far the capsule may travel from <paramref name="from"/> along <paramref name="dir"/> (unit length), up to
    /// <paramref name="distance"/>, and the collider that stops it (null when nothing does). This half only gathers
    /// physics facts; DisplacementSweepRule makes the decision and is tested in edit mode.
    /// </summary>
    private float AllowedTravel(Vector3 from, Vector3 dir, float distance, out Collider blocker)
    {
        blocker = null;
        if (capsule == null)
            return distance; // Awake already logged it; moving unchecked beats a player who can never dash.

        Quaternion rotation = rb.rotation;
        // Lifted a little, so the floor the capsule rests on is neither an overlap nor a hit.
        Vector3 lift = Vector3.up * DisplacementSweepRule.LiftMetres;
        Vector3 centre = from + rotation * capsule.center + lift;
        float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
        Vector3 top = centre + Vector3.up * halfSegment;
        Vector3 bottom = centre - Vector3.up * halfSegment;
        // Where the push-out is asked for: one skin width along the move. At that pose a capsule merely TOUCHING a wall
        // it moves into already overlaps it, while one moving away or along it does not overlap it any deeper.
        Vector3 probe = from + dir * DisplacementSweepRule.SkinMetres + lift;

        sweepContacts.Clear();
        sweepColliders.Clear();

        // Walls the capsule already overlaps, asked for directly rather than left to the cast below: a cast reports a
        // collider it starts inside with distance 0 and no usable normal, and not dependably at all.
        int overlapCount = Physics.OverlapCapsuleNonAlloc(bottom, top, capsule.radius, overlapHits, blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
            AddStartInside(overlapHits[i], probe, rotation);

        int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, capsule.radius, dir, sweepHits,
            distance + DisplacementSweepRule.SkinMetres, blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sweepHits[i];
            if (hit.distance <= 0f)
            {
                AddStartInside(hit.collider, probe, rotation); // touching at the start: judged the same way
                continue;
            }

            if (IsOwnOrGround(hit.collider) || sweepColliders.Contains(hit.collider))
                continue;

            sweepContacts.Add(SweepContact.Hit(hit.distance, hit.normal));
            sweepColliders.Add(hit.collider);
        }

        float allowed = DisplacementSweepRule.AllowedTravel(distance, sweepContacts, dir, out int blockerIndex);
        if (blockerIndex >= 0)
            blocker = sweepColliders[blockerIndex];
        return allowed;
    }

    private void AddStartInside(Collider other, Vector3 probe, Quaternion rotation)
    {
        if (IsOwnOrGround(other) || sweepColliders.Contains(other))
            return;

        Transform otherTransform = other.transform;
        bool overlapsAhead = Physics.ComputePenetration(capsule, probe, rotation, other, otherTransform.position,
            otherTransform.rotation, out Vector3 pushOut, out float depth) && depth > 0f;

        sweepContacts.Add(overlapsAhead ? SweepContact.Inside(pushOut) : SweepContact.InsideWithoutPushOut());
        sweepColliders.Add(other);
    }

    /// <summary>Never blocked by our own body, and the ground is not a wall - a TerrainCollider is also the one shape
    /// Physics.ComputePenetration cannot be asked about.</summary>
    private bool IsOwnOrGround(Collider other) =>
        other == null
        || other.attachedRigidbody == rb || other.transform.IsChildOf(transform)
        || other is TerrainCollider;
```

- [ ] **Step 6: `DashAbility`.** In `TryBuildCast`, replace

```csharp
            payload = new CastPayload { Origin = ctx.Origin, Direction = dir.normalized };
            return true;
```

  with

```csharp
            payload = new CastPayload { Origin = ctx.Origin, Direction = dir.normalized };

            // Movement step 2: a dash that cannot move - the player is touching or pressed into a wall that way, or a
            // knockback owns the body - is refused HERE, before AbilityRunner spends the charge (the same reason
            // BlinkAbility checks CanTeleport up front). It used to spend a charge and, from against a thin wall,
            // carry the player through it.
            if (Owner.Displacement is PlayerDisplacement self && !self.CanStartVoluntary(payload.Direction))
            {
                LogDashRefused(payload.Direction);
                payload = default;
                return false;
            }

            return true;
```

  and add next to `LogDash`:

```csharp
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDashRefused(Vector3 direction)
        {
            Debug.Log($"[DASH] refused dir={direction:F2} (a wall at the start, or a knockback is running)");
        }
```

- [ ] **Step 7: `DummyTarget` comments only.**
  - `:124-125`: "(PlayerDisplacement sweeps a Rigidbody with Rigidbody.SweepTestAll)" →
    "(PlayerDisplacement sweeps the player's own capsule and judges it with DisplacementSweepRule)".
  - `:326`: "Same two exclusions PlayerDisplacement.IsBlocker applies" →
    "The two exclusions PlayerDisplacement applies through DisplacementSweepRule".

- [ ] **Step 8: Recompile, dirty check, tests async.** Expected: clean, all pass, same total as Step 4.

- [ ] **Step 9: Play Mode spot check.** Fill the recorder with `__LABEL__` = `step2`,
  `__ONLY__` = `sweep,doubledash,hugdash,lowedge,pin`, and run it as in Task 1 Step 3.
  - **Expected:**
    - every `doubledash`: dash 1 ends `centreVsNearFace≈-0.75 IN` or `clear`, dash 2 `spent=False` with
      `[DASH] refused`, and no line says `BEYOND`;
    - `hugdash`: `along` and `away` travel 2.9–3.15, `into` is `spent=False` and travels under 0.05;
    - `hugdash` open floor travels within 0.05 m of the Task 1 number;
    - `pin`: both dashes `spent=False` after the knockback and after the zip pull;
    - `lowedge`: report it; the ride-over threshold may move up by about 0.05 m.
  - Stop Play Mode; dirty check `False`.

- [ ] **Step 10: Commit and push.**

```
git add Assets/scripts/Combat/DisplacementSweepRule.cs Assets/scripts/Combat/DisplacementSweepRule.cs.meta Assets/Tests/DisplacementSweepRuleTests.cs Assets/Tests/DisplacementSweepRuleTests.cs.meta Assets/scripts/Player/PlayerDisplacement.cs Assets/scripts/Abilities/Mobility/DashAbility.cs Assets/scripts/TestRange/DummyTarget.cs
git status
git commit -m "fix(movement): displacements stop a skin short of walls and never start inside one (movement step 2)" -m "A dash into a wall you are touching is refused and keeps its charge, as is a dash during a knockback." -m "Co-Authored-By: <your model line>"
git push
```

  Add the [C] lines from decisions 1–4 under the assumptions heading (Rule 14).

---

### Task 3 (movement step 3): an arena outline, and blink and portals that respect it

**What exists:**
- `ArenaSymmetry` (`Assets/scripts/Arena/ArenaSymmetry.cs`): `centre` (65.05, 0, 53.34), `source`, `generated120`,
  `generated240`, and a class comment saying it does nothing during play (`:19-20`).
- `RadialSymmetry.RotatePoint` (`:23-24`) turns a point counter-clockwise about the centre.
- `ArenaSymmetryBuilder.Validate` (`:99-145`) and `CheckSetup` (`:147-215`); `Rebuild` ends with `Validate` (`:95`).
- `ArenaSymmetrySceneTests.TheArenaMatchesItsSourceThird` runs `Validate` on the saved scene.
- Game Scene: `Enviorment/Arena/Source (Team 2 third)/Boundry` holds 22 BoxColliders (`Wall L/R 14m..28m`,
  `Pocket L/R 0..3`, `Pocket Back 0..2`, `Flank Back -7m/0m/+7m`, `Flank Side A/B`). Each pivot is its outer face and
  its local +Z faces the arena.
- `BlinkAbility.TryBuildCast`'s `Probe` (`:108-129`) and `IsCapsuleBlocked` (`:162-188`).
- `TeleportAbility.TryBuildCast` (`:155-179`), `OwnerTick` (`:181-206`), `CompleteTravel` (`:232-242`).

**Files:**
- Create: `Assets/scripts/Arena/ArenaBounds.cs`, `Assets/Tests/ArenaBoundsTests.cs`
- Create: `Assets/scripts/Abilities/Core/PlayerSpaceProbe.cs`, `Assets/Tests/PlayerSpaceProbeTests.cs`
- Modify: `Assets/scripts/Arena/ArenaSymmetry.cs`, `Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs`
- Modify: `Assets/Tests/ArenaSymmetryBuilderTests.cs`, `Assets/Tests/ArenaSymmetrySceneTests.cs`
- Modify: `Assets/scripts/Abilities/Mobility/BlinkAbility.cs`, `Assets/scripts/Abilities/Mobility/TeleportAbility.cs`
- Modify: `Assets/Scenes/Game Scene.unity` (the 8 outline points, written by a scratch script)
- Scratch (not committed): `SCRATCH\SetArenaOutline.cs`

- [ ] **Step 1: Failing `ArenaBoundsTests`.** Create `Assets/Tests/ArenaBoundsTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The arena outline rule (movement step 3), on a toy arena: an equilateral triangle with circumradius 10 (so its
    /// inradius is 5), whose Source third contributes one edge midpoint and the top vertex. Everything else is the
    /// 120 and 240 degree turns of that, exactly as the real arena is built.
    /// </summary>
    public class ArenaBoundsTests
    {
        // Not at the origin, so nothing can secretly rely on a centre at world zero.
        private static readonly Vector3 Centre = new Vector3(10f, 0f, 20f);
        private static readonly Vector2 Outward30 = new Vector2(0.8660254f, 0.5f);  // (x, z) at map angle 30 degrees
        private static readonly Vector2 Along30 = new Vector2(-0.5f, 0.8660254f);   // along that edge, toward 90 degrees

        // A point on the right edge's own frame: s metres along the edge from its midpoint, d metres out from the centre.
        private static Vector2 P(float s, float d) => new Vector2(Centre.x, Centre.z) + Outward30 * d + Along30 * s;

        private static Vector3 World(Vector2 xz) => new Vector3(xz.x, 0.5f, xz.y);

        private static ArenaBounds Triangle() => ArenaBounds.FromSourceOutline(
            new List<Vector2> { P(0f, 5f), new Vector2(Centre.x, Centre.z + 10f) }, Centre);

        // The same triangle with a 2 m wide, 2 m deep pocket in the right edge, like a flank pocket.
        private static ArenaBounds TriangleWithPocket() => ArenaBounds.FromSourceOutline(
            new List<Vector2> { P(0f, 5f), P(2f, 5f), P(2f, 7f), P(4f, 7f), P(4f, 5f), new Vector2(Centre.x, Centre.z + 10f) },
            Centre);

        [Test]
        public void TheCentreIsInsideByTheInradius() =>
            Assert.AreEqual(5f, Triangle().SignedDistance(World(new Vector2(Centre.x, Centre.z))), 1e-3f);

        [Test]
        public void APointJustInsideAnEdgeIsInsideWithoutRoomForAPlayer()
        {
            ArenaBounds bounds = Triangle();
            Vector3 hugging = World(P(1f, 4.5f));
            Assert.AreEqual(0.5f, bounds.SignedDistance(hugging), 1e-3f);
            Assert.IsTrue(bounds.Contains(hugging, 0f));
            Assert.IsFalse(bounds.Contains(hugging, 0.7f));
        }

        [Test]
        public void APointPastAnEdgeIsOutsideByItsDistance() =>
            Assert.AreEqual(-1f, Triangle().SignedDistance(World(P(1f, 6f))), 1e-3f);

        [Test]
        public void TheOtherThirdsAreTheSourceTurned()
        {
            // The bottom edge (map angle 270) is not in the Source outline at all: only its turned copies make it.
            ArenaBounds bounds = Triangle();
            Assert.AreEqual(1f, bounds.SignedDistance(World(new Vector2(Centre.x, Centre.z - 4f))), 1e-3f);
            Assert.AreEqual(-1f, bounds.SignedDistance(World(new Vector2(Centre.x, Centre.z - 6f))), 1e-3f);
        }

        [Test]
        public void ThePolygonIsTheSourcePointsThenTheir120And240Turns()
        {
            IReadOnlyList<Vector2> polygon = Triangle().Polygon;
            Assert.AreEqual(6, polygon.Count);
            Assert.AreEqual(Centre.x - 4.330127f, polygon[2].x, 1e-3f);  // the 30 degree midpoint turned to 150
            Assert.AreEqual(Centre.z + 2.5f, polygon[2].y, 1e-3f);
            Assert.AreEqual(Centre.x, polygon[4].x, 1e-3f);              // and turned to 270
            Assert.AreEqual(Centre.z - 5f, polygon[4].y, 1e-3f);
        }

        [Test]
        public void InsideAPocketIsInsideAndBesideItIsOutside()
        {
            ArenaBounds bounds = TriangleWithPocket();
            Assert.AreEqual(1f, bounds.SignedDistance(World(P(3f, 6f))), 1e-3f);
            Assert.AreEqual(-1f, bounds.SignedDistance(World(P(1f, 6f))), 1e-3f);
        }

        [Test]
        public void AnOutlineWithFewerThanTwoPointsIsNoBoundsAtAll()
        {
            Assert.IsNull(ArenaBounds.FromSourceOutline(new List<Vector2> { P(0f, 5f) }, Centre));
            Assert.IsNull(ArenaBounds.FromSourceOutline(null, Centre));
        }
    }
}
```

- [ ] **Step 2: Create `Assets/scripts/Arena/ArenaBounds.cs`.**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The arena's playable outline seen from above, and the one "is this spot inside the arena" rule (movement
    /// step 3). Built from the Source third's outline (ArenaSymmetry.sourceOutline) turned into all three thirds with
    /// RadialSymmetry, so the outline obeys the same symmetry as the walls it traces.
    ///
    /// Why it exists: the ground outside the boundary walls is real terrain on the same layer as the arena floor, so
    /// "is there ground here" cannot tell inside from outside. Blink's landing, a portal's placement and arrival, and
    /// (through PlayerMotor) the out-of-arena safety net all ask this instead.
    ///
    /// Plain C#: tested in edit mode without a scene (ArenaBoundsTests). Vector2 here is a flat world point - x is
    /// world X and y is world Z.
    /// </summary>
    public sealed class ArenaBounds
    {
        private readonly Vector2[] polygon;

        private ArenaBounds(Vector2[] polygon) => this.polygon = polygon;

        /// <summary>The whole outline: the Source points, then their 120 degree turns, then their 240 degree turns.</summary>
        public IReadOnlyList<Vector2> Polygon => polygon;

        /// <summary>Null for fewer than two points: no outline means no arena bounds are known.</summary>
        public static ArenaBounds FromSourceOutline(IReadOnlyList<Vector2> sourceOutline, Vector3 centre)
        {
            if (sourceOutline == null || sourceOutline.Count < 2)
                return null;

            int count = sourceOutline.Count;
            var points = new Vector2[count * 3];
            for (int third = 0; third < 3; third++)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 turned = RadialSymmetry.RotatePoint(
                        new Vector3(sourceOutline[i].x, 0f, sourceOutline[i].y), centre, third);
                    points[third * count + i] = new Vector2(turned.x, turned.z);
                }
            }

            return new ArenaBounds(points);
        }

        /// <summary>Metres to the nearest edge: positive inside the outline, negative outside it.</summary>
        public float SignedDistance(Vector2 pointXZ)
        {
            float nearest = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i];
                nearest = Mathf.Min(nearest, DistanceToSegment(pointXZ, a, b));
                // Even-odd crossing count along a ray toward +x: it needs no winding order, so the outline may be
                // listed either way round.
                if ((a.y > pointXZ.y) != (b.y > pointXZ.y) &&
                    pointXZ.x < a.x + (pointXZ.y - a.y) * (b.x - a.x) / (b.y - a.y))
                    inside = !inside;
            }

            return inside ? nearest : -nearest;
        }

        /// <summary>The same, for a world point: its height is ignored. Kept as its own overload so a Vector3 can
        /// never be converted to a Vector2 by (x, y) and silently read the height as a Z.</summary>
        public float SignedDistance(Vector3 world) => SignedDistance(new Vector2(world.x, world.z));

        /// <summary>True when the point is inside with at least <paramref name="margin"/> metres to the nearest edge -
        /// pass a player's radius for a spot a player has to fit in.</summary>
        public bool Contains(Vector3 world, float margin) => SignedDistance(world) >= margin;

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            float t = lengthSqr > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
```

- [ ] **Step 3: `ArenaSymmetry`.**
  1. Replace the class-comment lines "This component does nothing during play or in a build. It only stores the setup
     that the Editor tool (Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs) reads." with:

```csharp
    /// During play this component has exactly one job: it publishes the arena outline as ArenaBounds (Active,
    /// ActiveBounds, IsInsideArena) for blink, portals and the out-of-arena safety net. Everything else it stores is
    /// read by the Editor tool (Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs).
```

  2. After the `centred` list, add:

```csharp
        [Tooltip("The arena's outer edge in the Source third, seen from above: the inner faces of the boundary walls " +
                 "under Source/Boundry, in order round the edge (x = world X, y = world Z). The other two thirds use " +
                 "it turned 120 and 240 degrees. Blink and portals can't land outside it, and a player who ends up " +
                 "outside is put back. Move a boundary wall and you must move these points onto its new inner face: " +
                 "Validate reports any wall more than 15 cm off this outline.")]
        public List<Vector2> sourceOutline = new List<Vector2>();

        /// <summary>The arena in the running game, or null outside Play Mode and in a scene without one.</summary>
        public static ArenaSymmetry Active { get; private set; }

        /// <summary>This arena's outline, built when it starts. Null when Source Outline has fewer than two points.</summary>
        public ArenaBounds Bounds { get; private set; }

        /// <summary>The running arena's outline, or null when there is no arena or no outline (a test scene).</summary>
        public static ArenaBounds ActiveBounds => Active != null ? Active.Bounds : null;

        /// <summary>True when the point is at least <paramref name="margin"/> metres inside the arena. With no arena
        /// bounds known it is also true: a scene without the arena allows everything rather than refusing every
        /// blink, portal and placement in it.</summary>
        public static bool IsInsideArena(Vector3 worldPoint, float margin)
        {
            ArenaBounds bounds = ActiveBounds;
            return bounds == null || bounds.Contains(worldPoint, margin);
        }

        private void OnEnable()
        {
            // Play Mode only: without ExecuteInEditMode, the Editor never runs this, and the tool reads the fields
            // directly anyway.
            Bounds = ArenaBounds.FromSourceOutline(sourceOutline, centre);
            if (Bounds == null)
                Debug.LogError($"[Arena] {name}: Source Outline has fewer than two points - blink, portals and the " +
                                "out-of-arena safety net cannot tell inside from outside.");
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }
```

  3. At the end of `OnDrawGizmosSelected`, add:

```csharp
            // The outline, all three thirds, so a designer can see it lying on the walls' inner faces.
            ArenaBounds outline = ArenaBounds.FromSourceOutline(sourceOutline, centre);
            if (outline == null)
                return;
            Gizmos.color = Color.cyan;
            IReadOnlyList<Vector2> points = outline.Polygon;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Count];
                Gizmos.DrawLine(new Vector3(a.x, centre.y + 0.2f, a.y), new Vector3(b.x, centre.y + 0.2f, b.y));
            }
```

- [ ] **Step 4: `ArenaSymmetryBuilder`.**
  1. Next to `PositionToleranceMetres`, add:

```csharp
        /// <summary>How far a boundary wall's inner face may sit from Source Outline before Validate reports it. Loose
        /// enough for a corner where two pieces meet, tight enough that a wall moved by hand is caught.</summary>
        public const float OutlineToleranceMetres = 0.15f;

        /// <summary>The child of Source (and of each generated third) holding the boundary walls. The spelling is the
        /// scene's own.</summary>
        public const string BoundaryGroupName = "Boundry";
```

  2. In `Validate`, replace the final `return problems;` with:

```csharp
            CheckOutline(arena, problems);

            return problems;
```

  3. Add, next to `CheckSetup`:

```csharp
        /// <summary>Every boundary wall must sit on Source Outline (movement step 3). A designer who moves a wall and
        /// forgets the outline would otherwise leave blink, portals and the safety net working off the old edge.</summary>
        private static void CheckOutline(ArenaSymmetry arena, List<string> problems)
        {
            // A tiny arena with no boundary walls (the builder's own tests) has nothing to check.
            if (arena.source.Find(BoundaryGroupName) == null)
                return;

            ArenaBounds outline = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre);
            if (outline == null)
            {
                problems.Add("Source has boundary walls but Source Outline has fewer than two points: set it to the " +
                             "inner faces of the walls under Source/" + BoundaryGroupName + ".");
                return;
            }

            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                Transform walls = third.Find(BoundaryGroupName);
                if (walls == null)
                    continue; // The copy check above already says a rebuild is needed.

                foreach (BoxCollider box in walls.GetComponentsInChildren<BoxCollider>(true))
                {
                    // A boundary piece's pivot is its outer face and its local +Z faces the arena, so the inner face
                    // is the box's +Z side.
                    Vector3 face = box.transform.TransformPoint(box.center + new Vector3(0f, 0f, box.size.z * 0.5f));
                    float off = Mathf.Abs(outline.SignedDistance(face));
                    if (off > OutlineToleranceMetres)
                        problems.Add($"{box.name} ({third.name}) has its inner face {off:0.00} m off Source Outline: " +
                                      "move the outline points onto the boundary walls' inner faces.");
                }
            }
        }
```

  4. Add `using Overpower.Arena;` if it isn't there already (it is: the file already uses `ArenaSymmetry`).

- [ ] **Step 5: `ArenaSymmetryBuilderTests`.** Add to the class (they use the existing `MakeChild`/`Centre` helpers):

```csharp
        // The Source third's share of a triangle arena's outline (circumradius 10, inradius 5): the right edge's
        // midpoint at map angle 30, and the top vertex.
        private static readonly Vector2[] TriangleSourceOutline =
        {
            new Vector2(Centre.x + 4.330127f, Centre.z + 2.5f),
            new Vector2(Centre.x, Centre.z + 10f),
        };

        // A boundary wall on that edge, 3 m along it, facing the centre with its local +Z like the real walls. Its box
        // is 0.5 m thick from the pivot, so its inner face lands exactly on the edge.
        private Transform MakeBoundaryWall()
        {
            Transform wall = MakeChild("Wall", MakeChild(ArenaSymmetryBuilder.BoundaryGroupName, arena.source));
            BoxCollider box = wall.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 3f, 0.5f);
            box.center = new Vector3(0f, 1.5f, 0.25f);
            var outward = new Vector3(0.8660254f, 0f, 0.5f);
            var along = new Vector3(-0.5f, 0f, 0.8660254f);
            wall.SetPositionAndRotation(Centre + outward * 5.5f + along * 3f, Quaternion.LookRotation(-outward));
            return wall;
        }

        [Test]
        public void ValidateAcceptsBoundaryWallsSittingOnTheOutlineInEveryThird()
        {
            arena.sourceOutline = new List<Vector2>(TriangleSourceOutline);
            MakeBoundaryWall();

            Assert.IsEmpty(ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false));
        }

        [Test]
        public void ValidateReportsABoundaryWallMovedOffTheOutline()
        {
            arena.sourceOutline = new List<Vector2>(TriangleSourceOutline);
            Transform wall = MakeBoundaryWall();
            wall.position += new Vector3(0.8660254f, 0f, 0.5f);   // 1 m further out

            List<string> problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Assert.AreEqual(3, problems.Count, string.Join("\n", problems));   // the Source wall and both copies
            StringAssert.Contains("1.00 m off Source Outline", problems[0]);
        }

        [Test]
        public void ValidateReportsBoundaryWallsWithNoOutlineAtAll()
        {
            MakeBoundaryWall();

            List<string> problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("Source Outline", problems[0]);
        }
```

  The file already has `using System.Collections.Generic;` and `using UnityEngine;`.

- [ ] **Step 6: `ArenaSymmetrySceneTests`.** Add `using UnityEditor;` and this test:

```csharp
        [Test]
        public void EverySpawnPointAndTowerIsInsideTheArenaOutline()
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                ArenaBounds bounds = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre);
                Assert.IsNotNull(bounds, "Source Outline is empty: set it to the boundary walls' inner faces.");

                // The real player's own capsule, so this test can't disagree with what blink and portals check against.
                float radius = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Multiplayer Player.prefab")
                    .GetComponent<CapsuleCollider>().radius;

                RoomManager rooms = Find<RoomManager>(scene).Single();
                foreach (Transform spawn in rooms.teamSpawnPoints.Concat(rooms.capitalUnderAttackSpawnPoints))
                {
                    if (spawn == null) continue;
                    Assert.GreaterOrEqual(bounds.SignedDistance(spawn.position), radius,
                        $"{spawn.name} is not a player's width inside the arena outline.");
                }

                foreach (BuildingCapture tower in Find<BuildingCapture>(scene))
                    Assert.Greater(bounds.SignedDistance(tower.transform.position), 0f,
                        $"Tower {tower.buildingID} is outside the arena outline.");
            });
        }
```

- [ ] **Step 7: Recompile and confirm the failing state.** Dirty check, tests async. Expected:
  - compiles clean;
  - `ArenaBoundsTests` (7) and the three new builder tests pass;
  - `TheArenaMatchesItsSourceThird` **fails** ("Source has boundary walls but Source Outline has fewer than two
    points");
  - `EverySpawnPointAndTowerIsInsideTheArenaOutline` **fails** ("Source Outline is empty");
  - nothing else fails. Report the totals.

- [ ] **Step 8: Write the outline into the scene.** Create `SCRATCH\SetArenaOutline.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// One-time (movement step 3): writes the Source third's arena outline - the inner faces of Source/Boundry - onto
/// ArenaSymmetry and saves the scene. Not committed; the scene is.
public static class SetArenaOutline
{
    // Measured from Game Scene's Source/Boundry BoxColliders (2026-09-17), counter-clockwise from the right flank
    // pocket to the capital pocket's left mouth corner. The flank pocket corners skip the 0.73 m notch where a side
    // wall meets the main wall line: no player's centre fits in it.
    private static readonly Vector2[] Outline =
    {
        new Vector2(96.184f, 60.034f),   // Flank Side A meets the main wall line
        new Vector2(98.782f, 61.534f),   // Flank Side A meets Flank Back
        new Vector2(89.012f, 78.456f),   // Flank Back meets Flank Side B
        new Vector2(86.414f, 76.956f),   // Flank Side B meets the main wall line
        new Vector2(75.451f, 95.944f),   // the right converging wall meets Pocket R
        new Vector2(75.451f, 121.401f),  // Pocket R meets Pocket Back
        new Vector2(54.649f, 121.401f),  // Pocket Back meets Pocket L
        new Vector2(54.649f, 95.944f),   // Pocket L meets the left converging wall
    };

    public static string Run()
    {
        if (EditorApplication.isPlaying) return "ABORT: play mode";
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Game Scene.unity") return "ABORT: active scene is " + scene.path;
        if (scene.isDirty) return "ABORT: scene is dirty before the edit - reload it from disk first";

        ArenaSymmetry[] arenas = UnityEngine.Object.FindObjectsByType<ArenaSymmetry>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (arenas.Length != 1) return $"ABORT: found {arenas.Length} ArenaSymmetry components";
        ArenaSymmetry arena = arenas[0];

        Transform walls = arena.source.Find(ArenaSymmetryBuilder.BoundaryGroupName);
        if (walls == null) return "ABORT: Source has no " + ArenaSymmetryBuilder.BoundaryGroupName + " group";

        // 1. Measure every Source boundary wall against the outline BEFORE writing anything.
        ArenaBounds bounds = ArenaBounds.FromSourceOutline(Outline, arena.centre);
        var log = new StringBuilder();
        float worst = 0f;
        foreach (BoxCollider box in walls.GetComponentsInChildren<BoxCollider>(true))
        {
            float half = box.size.x * 0.5f;
            Vector3 mid = box.transform.TransformPoint(box.center + new Vector3(0f, 0f, box.size.z * 0.5f));
            Vector3 end0 = box.transform.TransformPoint(box.center + new Vector3(-half, 0f, box.size.z * 0.5f));
            Vector3 end1 = box.transform.TransformPoint(box.center + new Vector3(half, 0f, box.size.z * 0.5f));
            worst = Mathf.Max(worst, Mathf.Abs(bounds.SignedDistance(mid)));
            log.AppendLine($"{box.name}: mid {bounds.SignedDistance(mid):+0.000;-0.000} ends " +
                           $"{bounds.SignedDistance(end0):+0.000;-0.000} {bounds.SignedDistance(end1):+0.000;-0.000}");
        }
        log.AppendLine($"worst inner-face mid offset {worst:0.000} m");
        if (worst > ArenaSymmetryBuilder.OutlineToleranceMetres)
            return "ABORT: the outline doesn't match the walls - nothing written\n" + log;

        // 2. Write and save.
        var so = new SerializedObject(arena);
        SerializedProperty list = so.FindProperty("sourceOutline");
        if (list == null) return "ABORT: sourceOutline not found on ArenaSymmetry\n" + log;
        list.arraySize = Outline.Length;
        for (int i = 0; i < Outline.Length; i++)
            list.GetArrayElementAtIndex(i).vector2Value = Outline[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) return "ERROR: SaveScene returned false - reload the scene from disk\n" + log;

        List<string> problems = ArenaSymmetryBuilder.Validate(arena);
        log.AppendLine($"saved; dirty after save={scene.isDirty}; Validate problems={problems.Count}");
        foreach (string p in problems) log.AppendLine("  " + p);
        return log.ToString();
    }
}
```

  Run it:
  `unity command --timeout 240 run_script -- --file "SCRATCH\SetArenaOutline.cs" --entry SetArenaOutline.Run --timeout_ms 200000`

  - **Expected:** every piece's `mid` within ±0.02; ends 0.000 except where a piece runs into a corner (up to about
    0.75 for `Flank Side A/B`); `worst` under 0.02; `dirty after save=False`; `Validate problems=0`.
  - If it aborts, report the whole log and stop: the measured faces disagree with the outline, and the controller
    decides the new points.

- [ ] **Step 9: Read the scene diff.** `git diff --stat -- "Assets/Scenes/Game Scene.unity"`, then `git diff`.
  - **Expected:** one added block on the `ArenaSymmetry` component, `sourceOutline:` plus 8 `- {x: …, y: …}` lines.
  - If Unity re-serialised anything else, report it and ask the controller before committing the extra lines.

- [ ] **Step 10: Create `Assets/scripts/Abilities/Core/PlayerSpaceProbe.cs`.**

```csharp
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// "Does a player fit here, and can I reach it" for every ability that puts the caster, or something the caster
    /// places, on a chosen spot (movement step 3): Blink's landing, a portal's placement and arrival, and (ability
    /// visuals step 3) a mine's placement. One copy, so those abilities cannot disagree about what blocked means.
    /// GroundProbe answers the other half, "is there floor here".
    /// </summary>
    public static class PlayerSpaceProbe
    {
        /// <summary>Height above the floor the path check looks along. Not a tuning value: below a crate's or a wall's
        /// top and above the arena floor's own bumps (0.33 m of relief), so it sees walls and crates but not ground.</summary>
        public const float KneeHeightMetres = 0.5f;

        /// <summary>The path check's thickness, so it can't slip through the hairline seam between two wall pieces.
        /// Not a tuning value.</summary>
        public const float PathProbeRadiusMetres = 0.1f;

        // One buffer, reused: these checks run on the caster's own client, one at a time, on the main thread.
        private static readonly Collider[] overlapBuffer = new Collider[16];

        /// <summary>The root height that stands a capsule on <paramref name="groundY"/>: its bottom (centre.y minus
        /// half its height, below the root) sits exactly on the floor. The derivation Blink has always used, in one
        /// place now that portal arrival needs it too.</summary>
        public static float RootHeightOnGround(float groundY, float capsuleCentreY, float capsuleHeight) =>
            groundY - (capsuleCentreY - capsuleHeight * 0.5f);

        /// <summary>The root position that stands this capsule on a ground point.</summary>
        public static Vector3 RootOnGround(CapsuleCollider capsule, Vector3 groundPoint) =>
            new Vector3(groundPoint.x,
                        RootHeightOnGround(groundPoint.y, capsule.center.y, capsule.height),
                        groundPoint.z);

        /// <summary>The floor point under a player's root: the capsule's own bottom.</summary>
        public static Vector3 FeetOf(CapsuleCollider capsule, Vector3 rootPosition) =>
            rootPosition + Vector3.up * (capsule.center.y - capsule.height * 0.5f);

        /// <summary>
        /// True when a player-sized capsule rooted at <paramref name="rootPosition"/> would overlap anything on
        /// <paramref name="mask"/> other than <paramref name="self"/>'s own colliders. Assumes the player prefab is not
        /// scaled and the capsule stands upright, the same simplification Blink has always made for this capsule.
        /// </summary>
        public static bool IsCapsuleBlocked(CapsuleCollider capsule, Vector3 rootPosition, int mask, Transform self)
        {
            Vector3 centre = rootPosition + capsule.center;
            float halfSegment = Mathf.Max(capsule.height * 0.5f - capsule.radius, 0f);
            int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * halfSegment, centre - Vector3.up * halfSegment,
                capsule.radius, overlapBuffer, mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (self != null && overlapBuffer[i].transform.IsChildOf(self))
                    continue; // never blocked by the caster's own body
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when nothing on the Building layer - a wall, a house, a crate, deployable cover - stands between two
        /// floor points, checked at knee height. Building only on purpose: another player standing in the way must not
        /// stop you placing something, and the floor itself is on Default.
        /// </summary>
        public static bool IsPathClear(Vector3 fromFeet, Vector3 toFeet)
        {
            Vector3 from = fromFeet + Vector3.up * KneeHeightMetres;
            Vector3 to = toFeet + Vector3.up * KneeHeightMetres;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return true;

            return !Physics.SphereCast(from, PathProbeRadiusMetres, delta / distance, out _, distance,
                LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        }
    }
}
```

  And `Assets/Tests/PlayerSpaceProbeTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Abilities;

namespace Overpower.Tests
{
    public class PlayerSpaceProbeTests
    {
        // Multiplayer Player.prefab's capsule: centre y 0.8063041, height 2.6126082, so its bottom is 0.5 m below the root.
        private const float CentreY = 0.8063041f;
        private const float Height = 2.6126082f;

        [Test]
        public void AStandingPlayersRootIsHalfAMetreAboveTheFloorItStandsOn()
        {
            // Portal travel used to teleport to the floor point itself, which sank the capsule that half metre.
            Assert.AreEqual(0.5f, PlayerSpaceProbe.RootHeightOnGround(0f, CentreY, Height), 1e-4f);
            Assert.AreEqual(1.3f, PlayerSpaceProbe.RootHeightOnGround(0.8f, CentreY, Height), 1e-4f);
        }
    }
}
```

- [ ] **Step 11: `BlinkAbility`.**
  1. In `Probe`, after `landingPoint = default;`, add:

```csharp
                // Movement step 3: never outside the arena. The terrain carries on past the boundary walls, so the
                // ground probe alone can't tell; the search's own walk back toward the caster then finds the last spot
                // inside. Blinking PAST a crate or a house inside the arena is unchanged (Task 1.6b).
                if (!Overpower.Arena.ArenaSymmetry.IsInsideArena(candidateXZ, capsule.radius))
                    return false;
```

  2. Replace the root-position block and its `IsCapsuleBlocked` call with:

```csharp
                // Root position such that the capsule's OWN bottom sits exactly on the ground found above - shared with
                // portal arrival since movement step 3, so the two can't drift apart.
                Vector3 rootPosition = PlayerSpaceProbe.RootOnGround(capsule, new Vector3(candidateXZ.x, ground.y, candidateXZ.z));

                if (PlayerSpaceProbe.IsCapsuleBlocked(capsule, rootPosition, blockMask, Owner.Root.transform))
                    return false;
```

  3. Delete the private `IsCapsuleBlocked` method and its doc comment (`:162-188`).
  4. In the class comment, after "only the destination itself has to be somewhere a player could actually stand", add
     "and inside the arena outline (movement step 3)".

- [ ] **Step 12: `TeleportAbility`.**
  1. After `private int blockMask;`, add:

```csharp
        // Building only, for the two checks added in movement step 3: what may stand between the caster and a
        // placement, and what makes an exit unusable. Players are deliberately NOT in it - an enemy standing on your
        // exit must not lock your portal.
        private int buildingMask;

        // The caster's own capsule, read in OnEquip like Blink's: a portal is placed for a player to arrive on, so it
        // is checked against the real player shape.
        private CapsuleCollider capsule;
```

  2. In `Awake`, after `blockMask = ...`, add `buildingMask = LayerMask.GetMask("Building");`.
  3. In `OnEquip`, after `portalTemplate = ...`, add:

```csharp
            capsule = Owner.Root.GetComponent<CapsuleCollider>();
            if (capsule == null)
                Debug.LogError($"[TeleportAbility] {name}: the player has no CapsuleCollider - a portal cannot be " +
                                "checked for the player who would arrive on it, so placing will always refuse.");
```

  4. In `TryBuildCast`, change the first guard to
     `if (portalTemplate == null || Owner.Motor == null || capsule == null)` and, after the `IsBlocked(ground)` check,
     add:

```csharp
            // Movement step 3: inside the arena with room for the player who arrives on it, and reachable - nothing on
            // the Building layer between the caster's feet and the spot. The terrain carries on past the boundary
            // walls, so the ground probe alone happily placed a gate outside the arena.
            if (!Overpower.Arena.ArenaSymmetry.IsInsideArena(ground, capsule.radius))
                return false;

            if (!PlayerSpaceProbe.IsPathClear(PlayerSpaceProbe.FeetOf(capsule, ctx.Origin), ground))
                return false;
```

  5. In `OwnerTick`, replace `bool canChannel = canAct && other != null && HasCharge;` with:

```csharp
            // Movement step 3: the exit is re-checked every tick, so a wall, crate or cover built on it later - or a
            // portal placed before this check existed - can't channel anyone into geometry or out of the arena. It is
            // part of the gate rather than a refusal at travel time, because by then the charge is already spent.
            bool canChannel = canAct && other != null && HasCharge && IsExitClear(other);
```

  6. In `CompleteTravel`, replace the `SendPhase(PhaseTravelled, …)` line with:

```csharp
            SendPhase(PhaseTravelled, new CastPayload { Origin = from.transform.position, Point = ArrivalRoot(to) });
```

  7. Add next to `IsBlocked`:

```csharp
        /// <summary>Where a traveller's root lands on a portal: standing on its floor point, the same height Blink
        /// uses. Travelling to the raw ground point sank the capsule half a metre into the floor, and physics popped
        /// it out in whatever direction it could.</summary>
        private Vector3 ArrivalRoot(Portal to) => PlayerSpaceProbe.RootOnGround(capsule, to.transform.position);

        /// <summary>True when a player can arrive on this portal: inside the arena with a player's width to spare, and
        /// not inside a wall, house, crate or cover. Other players don't count (see buildingMask).</summary>
        private bool IsExitClear(Portal to)
        {
            if (capsule == null)
                return false;

            Vector3 root = ArrivalRoot(to);
            return Overpower.Arena.ArenaSymmetry.IsInsideArena(root, capsule.radius)
                   && !PlayerSpaceProbe.IsCapsuleBlocked(capsule, root, buildingMask, Owner.Root.transform);
        }
```

- [ ] **Step 13: Recompile, dirty check, tests async.** Expected: clean, everything passes (the two red scene tests are
  green now). Report the totals.

- [ ] **Step 14: Play Mode spot check.** Run the recorder with `__LABEL__` = `step3`, `__ONLY__` = `blink,portal`.
  - **Expected:**
    - every `blink` line: not `BEYOND`, `arenaSigned` at least `+0.70`, and the crate blink still `BEYOND` the crate;
    - `portal` behind the wall and behind the crate: `0 -> 0`;
    - `portal` arrival: the first height is `0.50 ± 0.03` and stays there.
  - Stop Play Mode; dirty check `False`.

- [ ] **Step 15: Commit and push.**

```
git add Assets/scripts/Arena/ArenaBounds.cs Assets/scripts/Arena/ArenaBounds.cs.meta Assets/Tests/ArenaBoundsTests.cs Assets/Tests/ArenaBoundsTests.cs.meta Assets/scripts/Abilities/Core/PlayerSpaceProbe.cs Assets/scripts/Abilities/Core/PlayerSpaceProbe.cs.meta Assets/Tests/PlayerSpaceProbeTests.cs Assets/Tests/PlayerSpaceProbeTests.cs.meta Assets/scripts/Arena/ArenaSymmetry.cs Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs Assets/Tests/ArenaSymmetryBuilderTests.cs Assets/Tests/ArenaSymmetrySceneTests.cs Assets/scripts/Abilities/Mobility/BlinkAbility.cs Assets/scripts/Abilities/Mobility/TeleportAbility.cs "Assets/Scenes/Game Scene.unity"
git status
git commit -m "fix(abilities): blink and portals stay inside the arena outline, and portal travel lands standing (movement step 3)" -m "Portals also need a clear path at knee height, so they can no longer be placed through a wall or crate." -m "Co-Authored-By: <your model line>"
git push
```

  Add the [C] lines from decisions 5–10 under the assumptions heading, and tell the controller the scene changed, so
  Tudor reloads the project before playing (CODING-STANDARDS §7).

---

### Task 4 (movement step 4): the owner-side out-of-arena safety net

**What exists:**
- `PlayerMotor.FixedUpdate` (`:121-139`) raises `FellBelowKillHeight` for the owner; the comment at `:125-127` says
  why there is no respawn key.
- `PlayerLifecycle.HandleFellBelowKillHeight` (`:222-226`) → `ReturnToSpawn` → `TeleportToSpawnPoint`.
- `PlayerMotor` has no notion of "standing on the ground".

**Files:**
- Create: `Assets/scripts/Arena/OutOfArenaRule.cs`, `Assets/Tests/OutOfArenaRuleTests.cs`
- Modify: `Assets/scripts/Player/PlayerMotor.cs`, `Assets/scripts/Player/PlayerLifecycle.cs`

- [ ] **Step 1: Failing `OutOfArenaRuleTests`.** Create `Assets/Tests/OutOfArenaRuleTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>
    /// The safety net's decision (movement step 4). The number it is given is the signed distance to the arena
    /// outline: positive inside, negative outside. A player's radius is 0.7.
    /// </summary>
    public class OutOfArenaRuleTests
    {
        private const float Radius = 0.7f;

        [Test]
        public void StandingWellInsideRemembersTheSpot() =>
            Assert.AreEqual(OutOfArenaAction.RememberAsSafe, OutOfArenaRule.Decide(3f, Radius, true, false));

        [Test]
        public void ExactlyAPlayersWidthInsideStillCounts() =>
            Assert.AreEqual(OutOfArenaAction.RememberAsSafe, OutOfArenaRule.Decide(Radius, Radius, true, true));

        [Test]
        public void InTheAirRemembersNothing() =>
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(3f, Radius, false, true));

        [Test]
        public void HuggingOrPressedIntoAWallIsNeitherRememberedNorReturned()
        {
            // 0.70 in = touching the wall, 0.60 = walked about 0.1 m into it. Both are ordinary play.
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(0.69f, Radius, true, true));
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(0.6f, Radius, true, true));
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(0.01f, Radius, true, true));
        }

        [Test]
        public void PastTheWallsInnerFaceGoesBack() =>
            Assert.AreEqual(OutOfArenaAction.ReturnToLastSafe, OutOfArenaRule.Decide(-0.01f, Radius, true, true));

        [Test]
        public void OutsideWithNowhereRememberedDoesNothing() =>
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(-5f, Radius, false, false));

        [Test]
        public void OutsideInTheAirStillGoesBack() =>
            Assert.AreEqual(OutOfArenaAction.ReturnToLastSafe, OutOfArenaRule.Decide(-5f, Radius, false, true));
    }
}
```

- [ ] **Step 2: Recompile; expect errors naming `OutOfArenaRule` / `OutOfArenaAction` only. Then create
  `Assets/scripts/Arena/OutOfArenaRule.cs`:**

```csharp
namespace Overpower.Arena
{
    /// <summary>What the out-of-arena safety net does this physics step.</summary>
    public enum OutOfArenaAction
    {
        /// <summary>Nothing: near an edge, in the air, or outside with nowhere known to go back to.</summary>
        None,

        /// <summary>Standing on floor with a player's width inside the outline: remember this spot.</summary>
        RememberAsSafe,

        /// <summary>The player's centre is past the outline - through a boundary wall's inner face - so put them
        /// back.</summary>
        ReturnToLastSafe,
    }

    /// <summary>
    /// The out-of-arena safety net's decision (movement step 4), kept pure so it is tested without a scene. Movement
    /// steps 2 and 3 close every way out anyone has found; this catches the ones nobody has found yet, on the owner's
    /// own client, next to the kill-height check. It is not a respawn key: nothing the player presses reaches it.
    ///
    /// Two thresholds on purpose:
    ///  - a spot is REMEMBERED only with a whole player's width to spare, so the spot you are put back on never
    ///    overlaps a wall;
    ///  - the net only FIRES once the centre is past a wall's inner face, which a player merely pressed against a wall
    ///    (about 0.1 m in) never is, so hugging a wall can't trigger it.
    /// </summary>
    public static class OutOfArenaRule
    {
        public static OutOfArenaAction Decide(float signedDistance, float playerRadius, bool grounded, bool hasLastSafe)
        {
            if (signedDistance < 0f)
                return hasLastSafe ? OutOfArenaAction.ReturnToLastSafe : OutOfArenaAction.None;

            if (grounded && signedDistance >= playerRadius)
                return OutOfArenaAction.RememberAsSafe;

            return OutOfArenaAction.None;
        }
    }
}
```

- [ ] **Step 3: `PlayerMotor`.**
  1. Add `using Overpower.Arena;` under the existing usings.
  2. After the `speedMultipliers` field, add:

```csharp
    // Movement step 4, owner only: the last spot this player stood on floor with a player's width inside the arena
    // outline - where the safety net puts them back - and the shapes the check needs.
    private Vector3 lastSafePosition;
    private bool hasLastSafePosition;
    private CapsuleCollider capsule;
    private int groundMask;
    private float groundedProbeMetres;

    // Not a tuning value: how far BELOW the capsule's own bottom still counts as standing on something, so a small
    // bump or a step doesn't read as being in mid-air.
    private const float GroundedSlackMetres = 0.3f;
```

  3. After the `FellBelowKillHeight` event, add:

```csharp
    /// <summary>Fired when this (locally owned) player's centre ends up outside the arena outline, with the last spot
    /// they stood safely inside. Like FellBelowKillHeight, PlayerMotor only notices - PlayerLifecycle decides what to
    /// do about it.</summary>
    public event System.Action<Vector3> LeftArena;
```

  4. At the end of `Awake`, add:

```csharp
        capsule = GetComponent<CapsuleCollider>();
        groundMask = LayerMask.GetMask("Default", "Building");
        // The capsule's bottom sits (height/2 - centre.y) below the root; the slack is what a bump or a step may add.
        groundedProbeMetres = capsule != null
            ? capsule.height * 0.5f - capsule.center.y + GroundedSlackMetres
            : 1f;
```

  5. In `FixedUpdate`'s owner branch, after the kill-height check and before `if (!ExternalMotionControl)`, add
     `CheckArenaBounds();`.
  6. Next to `SetNetworkTarget`, add:

```csharp
    /// <summary>Movement step 4, owner only: remembers where this player last stood safely inside the arena, and
    /// reports it when they end up outside (OutOfArenaRule). A scene without an arena outline - the test range, a
    /// preview scene - has nothing to check.</summary>
    private void CheckArenaBounds()
    {
        ArenaBounds bounds = ArenaSymmetry.ActiveBounds;
        if (bounds == null || capsule == null)
            return;

        Vector3 position = rb.position;
        float signed = bounds.SignedDistance(position);
        // The ray only matters for a spot that could be remembered, so it is skipped everywhere else.
        bool grounded = signed >= capsule.radius && IsStandingOnFloor(position);

        switch (OutOfArenaRule.Decide(signed, capsule.radius, grounded, hasLastSafePosition))
        {
            case OutOfArenaAction.RememberAsSafe:
                lastSafePosition = position;
                hasLastSafePosition = true;
                break;

            case OutOfArenaAction.ReturnToLastSafe:
                LeftArena?.Invoke(lastSafePosition);
                break;
        }
    }

    // The ray starts inside this player's own capsule, which a raycast never reports, so only real floor below counts.
    private bool IsStandingOnFloor(Vector3 rootPosition) =>
        Physics.Raycast(rootPosition, Vector3.down, groundedProbeMetres, groundMask, QueryTriggerInteraction.Ignore);
```

- [ ] **Step 4: `PlayerLifecycle`.**
  1. In `Start`, after `playerMotor.FellBelowKillHeight += HandleFellBelowKillHeight;`, add
     `playerMotor.LeftArena += HandleLeftArena;`.
  2. In `OnDestroy`, inside the existing `if (playerMotor != null)` block, add
     `playerMotor.LeftArena -= HandleLeftArena;`.
  3. After `HandleFellBelowKillHeight`, add:

```csharp
    /// Movement step 4: PlayerMotor found this player's centre outside the arena outline - through a boundary wall, or
    /// by some way out nobody has found yet - and hands over the last spot they stood safely inside. Deliberately NOT a
    /// death, exactly like falling out of the world: leaving the arena is a level problem, not a play outcome. Whatever
    /// move is running is cancelled first, so it can't carry the body on from outside and so a knockback can't make
    /// TeleportTo refuse.
    private void HandleLeftArena(Vector3 lastSafePosition)
    {
        if (!isAlive || playerDisplacement == null)
            return;

        Vector3 outside = rigidbody != null ? rigidbody.position : transform.position;
        playerDisplacement.Cancel();
        if (playerDisplacement.TeleportTo(lastSafePosition))
            Debug.Log($"[VIS] left the arena at {outside}, returned to {lastSafePosition}");
    }
```

- [ ] **Step 5: Recompile, dirty check, tests async.** Expected: clean, all pass, +7.

- [ ] **Step 6: Play Mode spot check.** Run the recorder with `__LABEL__` = `step4`,
  `__ONLY__` = `safetynet,sweep,hugdash,doubledash`.
  - **Expected:**
    - `safetynet`: `back inside=True` after at most 0.04 s, one `[VIS] left the arena …` line, `returns logged 1`;
    - `[summary] safety-net returns logged in this run: 1` - the pressed-in teleports in `sweep`, the wall hugs in
      `hugdash` and every `doubledash` must **not** trigger a return;
    - the `doubledash` and `hugdash` lines match Task 2's.
  - Stop Play Mode; dirty check `False`.

- [ ] **Step 7: Commit and push.**

```
git add Assets/scripts/Arena/OutOfArenaRule.cs Assets/scripts/Arena/OutOfArenaRule.cs.meta Assets/Tests/OutOfArenaRuleTests.cs Assets/Tests/OutOfArenaRuleTests.cs.meta Assets/scripts/Player/PlayerMotor.cs Assets/scripts/Player/PlayerLifecycle.cs
git status
git commit -m "feat(movement): a player who ends up outside the arena is put back where they last stood inside (movement step 4)" -m "Co-Authored-By: <your model line>"
git push
```

  Add decision 11's [C] line under the assumptions heading.

---

### Task 5 (movement step 5): remote copies snap instead of sliding

**What exists:**
- `PlayerNetSync.OnPhotonSerializeView` (`:49-76`): sends `transform.position`, calls
  `playerMotor.SetNetworkTarget(NetworkPosition, NetworkRotation)`.
- `PlayerMotor.SetNetworkTarget` (`:143-156`) snaps on the distance from `transform.position`; `FixedUpdate`'s remote
  branch (`:134-138`) lerps from `transform.position`.
- `PhotonMessageInfo.SentServerTimestamp` (`PunClasses.cs:463`) is already on every received update.
- `PhotonNetwork.SerializationRate` is 20 (`RoomManager.cs:30`).

**Files:**
- Create: `Assets/scripts/Net/RemoteSnapRule.cs`, `Assets/Tests/RemoteSnapRuleTests.cs`
- Modify: `Assets/scripts/Player/PlayerMotor.cs`, `Assets/scripts/Player/PlayerNetSync.cs`

- [ ] **Step 1: Failing `RemoteSnapRuleTests`.** Create `Assets/Tests/RemoteSnapRuleTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Net;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// When a remote copy jumps instead of gliding (movement step 5). The numbers are this game's: Remote Snap
    /// Distance 3 m and 20 updates a second, so one send interval is 50 ms.
    /// </summary>
    public class RemoteSnapRuleTests
    {
        private const float Snap = 3f;
        private const float Interval = 50f;
        private static readonly Vector3 Somewhere = new Vector3(65f, 0.5f, 53f);

        private static bool Snaps(Vector3 previous, int previousMs, Vector3 next, int nextMs) =>
            RemoteSnapRule.ShouldSnap(true, previous, previousMs, next, nextMs, Snap, Interval);

        [Test]
        public void TheFirstUpdateAlwaysSnaps() =>
            Assert.IsTrue(RemoteSnapRule.ShouldSnap(false, Vector3.zero, 0, Somewhere, 1000, Snap, Interval));

        [Test]
        public void OrdinaryMovementGlides()
        {
            // The fastest ordinary movement is a zip pull: 25 m/s is 1.25 m per update.
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 1.25f, 1050));
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 3f, 1050));
        }

        [Test]
        public void ABlinkSnaps() =>
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 9f, 1050));

        [Test]
        public void JustOverTheDistanceSnaps() =>
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 3.01f, 1050));

        [Test]
        public void AZipPullWithLostUpdatesStillGlides()
        {
            // Two updates lost: 0.15 s of a 25 m/s pull is 3.75 m, which must not read as a teleport.
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 3.75f, 1150));
        }

        [Test]
        public void ABlinkAfterOneLostUpdateStillSnaps() =>
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 9f, 1100));

        [Test]
        public void AStallCountsAtMostFourIntervals()
        {
            Assert.AreEqual(4, RemoteSnapRule.ElapsedIntervals(0, 1000, Interval));
            Assert.IsTrue(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 13f, 2000));
            Assert.IsFalse(Snaps(Somewhere, 1000, Somewhere + Vector3.right * 11f, 2000));
        }

        [Test]
        public void JitteredIntervalsRoundToWholeUpdates()
        {
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(0, 70, Interval));
            Assert.AreEqual(2, RemoteSnapRule.ElapsedIntervals(0, 80, Interval));
        }

        [Test]
        public void AStampThatWrappedOrWentBackwardsCountsOneInterval()
        {
            // ServerTimestamp wraps about every 49.7 days, the same reason DeployableAge subtracts unchecked.
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(int.MaxValue - 20, int.MinValue + 29, Interval));
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(1000, 900, Interval));
            Assert.AreEqual(1, RemoteSnapRule.ElapsedIntervals(1000, 1000, Interval));
        }
    }
}
```

- [ ] **Step 2: Recompile; expect errors naming `RemoteSnapRule` only. Then create
  `Assets/scripts/Net/RemoteSnapRule.cs`:**

```csharp
using UnityEngine;

namespace Overpower.Net
{
    /// <summary>
    /// Whether a remote copy of a player jumps straight to the owner's newest position instead of gliding there
    /// (movement step 5). Pure, tested in edit mode (RemoteSnapRuleTests).
    ///
    /// Decided on the jump between two updates the owner actually sent, not on how far the smoothed copy trails: a zip
    /// pull at 25 m/s leaves that copy metres behind, and judging the trail snapped ordinary fast movement. Ordinary
    /// movement covers at most about 1.25 m per update at 20 updates a second; a blink, a portal, a respawn or a
    /// teleport covers many metres in one.
    ///
    /// Lost updates stretch an ordinary jump, so the allowance grows with the number of send intervals between the two
    /// updates' server timestamps - up to MaxCountedIntervals, past which the copy is so stale that catching up at once
    /// reads better than a long glide. The first update always snaps: until then the copy sits wherever it spawned.
    /// </summary>
    public static class RemoteSnapRule
    {
        /// <summary>Not a tuning value: after this many missed updates the copy is stale rather than moving.</summary>
        public const int MaxCountedIntervals = 4;

        public static bool ShouldSnap(bool hasPrevious, Vector3 previous, int previousStampMs,
                                       Vector3 next, int nextStampMs, float snapDistance, float sendIntervalMs)
        {
            if (!hasPrevious)
                return true;

            float jump = Vector3.Distance(previous, next);
            return jump > snapDistance * ElapsedIntervals(previousStampMs, nextStampMs, sendIntervalMs);
        }

        /// <summary>Send intervals between two server timestamps, 1..MaxCountedIntervals. The subtraction is unchecked
        /// because ServerTimestamp wraps about every 49.7 days (see DeployableAge); an earlier, equal or unusable stamp
        /// counts as one interval.</summary>
        public static int ElapsedIntervals(int previousStampMs, int nextStampMs, float sendIntervalMs)
        {
            int elapsedMs = unchecked(nextStampMs - previousStampMs);
            if (elapsedMs <= 0 || sendIntervalMs <= 0f)
                return 1;

            return Mathf.Clamp(Mathf.RoundToInt(elapsedMs / sendIntervalMs), 1, MaxCountedIntervals);
        }
    }
}
```

- [ ] **Step 3: `PlayerMotor`.**
  1. Add `using Overpower.Net;`.
  2. Replace `remoteSnapDistance`'s tooltip with:

```csharp
    [SerializeField, Tooltip("How far a remote player may move between two network updates before other screens show " +
             "the move as a jump instead of a glide - a blink, a portal, a respawn or a teleport. Measured between " +
             "the owner's own consecutive updates (with more allowed when updates were lost), not from where the " +
             "smoothed copy sits: a zip pull trails its copy by metres. Ordinary movement covers at most about 1.25 m " +
             "per update (a 25 m/s zip pull at SerializationRate 20, RoomManager.cs). A blink shorter than this still " +
             "glides.")]
    private float remoteSnapDistance = 3f;
```

  3. After the `networkPosition` / `networkRotation` fields, add:

```csharp
    // Movement step 5, remote copies only: the owner's previous update (for RemoteSnapRule) and a snap waiting for the
    // next physics step.
    private bool hasNetworkUpdate;
    private Vector3 previousNetworkPosition;
    private int previousNetworkStampMs;
    private bool snapPending;

    /// <summary>How many times this remote copy has jumped to its owner's position. Diagnostic only - the two-client
    /// harness reads it, like PlayerAim.SetAimOverride; nothing in the game does.</summary>
    public int RemoteSnapCount { get; private set; }
```

  4. Replace `FixedUpdate`'s `else` branch with:

```csharp
        else
        {
            // Movement step 5: a snap is applied HERE, in the physics step, not where the update arrived. Writing
            // rb.position on arrival lost to this lerp's own MovePosition in the same step (PhotonHandler and this
            // component both run at execution order 0), so a blink or a respawn drew as a quarter-second slide.
            if (snapPending)
            {
                snapPending = false;
                RemoteSnapCount++;
                rb.position = networkPosition;
                rb.rotation = networkRotation;
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                return;
            }

            // From the body's own physics pose, not transform.position: with interpolation on and transform auto-sync
            // off, the transform trails the body, which is the other half of why a snap used to be undone.
            float catchUp = Time.deltaTime * networkLerpSpeed;
            rb.MovePosition(Vector3.Lerp(rb.position, networkPosition, catchUp));
            rb.MoveRotation(Quaternion.Lerp(rb.rotation, networkRotation, catchUp));
        }
```

  5. Replace `SetNetworkTarget` with:

```csharp
    /// <summary>Called by PlayerNetSync's receive side with the update's own server send time, so PlayerMotor never has
    /// to reach into the networking code itself. Whether this update is a jump rather than a step is
    /// RemoteSnapRule's decision; the jump itself happens in the next physics step.</summary>
    public void SetNetworkTarget(Vector3 position, Quaternion rotation, int sentServerTimestampMs)
    {
        float sendIntervalMs = 1000f / Mathf.Max(1, PhotonNetwork.SerializationRate);
        if (RemoteSnapRule.ShouldSnap(hasNetworkUpdate, previousNetworkPosition, previousNetworkStampMs,
                position, sentServerTimestampMs, remoteSnapDistance, sendIntervalMs))
            snapPending = true;

        hasNetworkUpdate = true;
        previousNetworkPosition = position;
        previousNetworkStampMs = sentServerTimestampMs;

        networkPosition = position;
        networkRotation = rotation;
    }
```

- [ ] **Step 4: `PlayerNetSync`.**
  1. Add a `private Rigidbody rb;` field and `rb = GetComponent<Rigidbody>();` in `Awake`.
  2. In the writing branch, replace `stream.SendNext(transform.position);` with:

```csharp
                // The body's own physics position, not the transform's: with transform auto-sync off the transform
                // trails rb.position by up to a physics step, and right after a respawn or a teleport that stale value
                // is the spot the player just left (movement step 5). Same type and slot on the wire.
                stream.SendNext(rb != null ? rb.position : transform.position);
```

  3. In the reading branch, replace the `SetNetworkTarget` call with:

```csharp
            playerMotor.SetNetworkTarget(NetworkPosition, NetworkRotation, info.SentServerTimestamp);
```

  4. In the class comment, change "it sends transform.position, transform.rotation, health and armor" to "it sends the
     body's physics position (rb.position), transform.rotation, health and armor", and leave the "do not reorder"
     paragraph as it is.

- [ ] **Step 5: Recompile, dirty check, tests async.** Expected: clean, all pass (+9), including
  `NetworkPrefabObservablesTests`.

- [ ] **Step 6: One-client sanity (no remote copy exists with one client, so this only proves nothing broke).** Run the
  recorder with `__LABEL__` = `step5`, `__ONLY__` = `hugdash,safetynet`; the lines must match Task 4's. Stop Play Mode;
  dirty check `False`. The real check is Task 7.

- [ ] **Step 7: Commit and push.**

```
git add Assets/scripts/Net/RemoteSnapRule.cs Assets/scripts/Net/RemoteSnapRule.cs.meta Assets/Tests/RemoteSnapRuleTests.cs Assets/Tests/RemoteSnapRuleTests.cs.meta Assets/scripts/Player/PlayerMotor.cs Assets/scripts/Player/PlayerNetSync.cs
git status
git commit -m "fix(net): remote copies jump on a blink, portal, respawn or teleport instead of sliding (movement step 5)" -m "No new synced field and no RPC: the owner sends its physics position in the same slot, and receivers judge the jump from consecutive updates." -m "Co-Authored-By: <your model line>"
git push
```

  Add decision 12's [C] lines under the assumptions heading.

---

### Task 6 (movement step 6): the whole recipe on one client, before against after

**What exists:** Tasks 2–5 are committed, and `SCRATCH\movement-before.txt` holds Task 1's numbers.

**Files:** scratch only. Nothing is committed unless a fix is needed (then it belongs to the task that owns the file).

- [ ] **Step 1: Preconditions.** Editor answers; not in Play Mode; dirty check `False`; `git status --short` clean;
  `unity command recompile` clean; all tests pass async. Report the total.

- [ ] **Step 2: Run the whole recipe.** Fill the recorder with `__LABEL__` = `after`, `__ONLY__` = `all` and run it as
  in Task 1 Step 3 (Play Mode, in a room, alone). Wait for `movement-after.done`, then read the file in full.

- [ ] **Step 3: Compare, line by line, with `movement-before.txt`.** Report a table with one row per scenario:
  before, after, and pass or fail.

| Recipe item | Scenario | Pass means |
|---|---|---|
| Double dash into Wall, Pocket Back, Flank Back, Flank Side, a crate and a house, in all three thirds | `doubledash` (18 rows) | Dash 1 `centreVsNearFace` −0.80..−0.70; dash 2 `spent=False` with `[DASH] refused`; **no** row says `BEYOND`; `arenaSigned` never negative |
| A dash along, and away from, a hugged wall still reaches about 3 m | `hugdash` | `along` and `away` travel 2.9–3.15 and `spent=True`; `into` is `spent=False` and travels under 0.05; open floor within 0.05 m of the before-number |
| Blink at the outer wall from 0.7 / 2 / 5 m lands inside | `blink` (6 wall rows) | Never `BEYOND`; `arenaSigned` at least +0.70; `rootAboveFloor` 0.50 ± 0.05 |
| Blink over a crate still works | `blink` (crate row) | `BEYOND` the crate, `spent=True`, `arenaSigned` positive |
| Portals behind the outer wall are refused | `portal` (wall row) | `portals 0 -> 0` |
| Portals behind a crate are refused (feel change, decision 8) | `portal` (crate row) | `portals 0 -> 0` |
| Portal travel lands standing | `portal` (arrival row) | First height 0.50 ± 0.03, and it stays there |
| Zip and pulse pinning, then a dash | `pin` | The knockback ends `Blocked` at about −0.75; both dashes after each pin are `spent=False`; no `BEYOND` |
| The safety net returns a player pushed out with `TeleportTo` | `safetynet` | `back inside=True` within 0.04 s, one `[VIS] left the arena` line |
| Nothing else triggers the net | `[summary]` | `safety-net returns logged in this run: 1` |
| Low edges (measured, not changed) | `lowedge` | Report the three rows; the ride-over threshold may move up by about 0.05 m |
| The sweep facts | `sweep` | Report them again; they explain the rest |

- [ ] **Step 4: Assets unchanged since BASE.**

```
git diff --stat BASE -- Assets/Gameplay/Config/GameplayConfig.asset Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset ProjectSettings/
git diff BASE -- Assets/Resources/Multiplayer Player.prefab
```

  Both must be empty. The RpcList lives in `PhotonServerSettings.asset`, so that covers "no RPC added, renamed or
  removed".

- [ ] **Step 5: Stop Play Mode; dirty check `False`.** Report. If any row fails, report it and hand the fix back to the
  task that owns the file rather than patching it here.

---

### Task 7 (movement step 7): two clients

**What exists:** Task 1 Part B's scripts and its before-numbers (`remote-before.txt`).

**Files:** scratch only; nothing is committed.

- [ ] **Step 1: Preconditions.** Rule 16 (`git status --short` empty), tests pass, dirty check `False`, not in Play
  Mode. Report `git rev-parse HEAD`.

- [ ] **Step 2: Rebuild Client2 from HEAD** exactly as Task 1 Step 6. Report the build result and the errors count.

- [ ] **Step 3: Both clients in one room.** As Task 1 Step 8.1–8.3, checking `players=2` on both sides.

- [ ] **Step 4: Run A, no lag.**
  1. Recorder on the Editor: `__SIDE__` = `E`, `__OUT__` = `$S\remote-after-E.csv`, `__DUR__` = `120`.
  2. Driver on Client2: `__SIDE__` = `P`, `__OUT__` = `$S\driver-after-P.log`, and the same `__PLAN__` as Task 1.
  3. Wait for `driver end` and `#END`, then
     `python "$S\analyze_movement.py" "$S\remote-after-E.csv" "$S\driver-after-P.log" "$S\remote-after.txt"`.
  - **Pass:**
    - every `tele`, `blink`, `portal` and `respawn` row reads `PASS`: `errNextStep` ≤ 0.1 m and at least one snap;
    - every `dash` and `zip` row reads `PASS`: `snaps=0`;
    - `outsideRows=0`, and `minArenaSigned` ≥ 0 - the other screen never shows the player outside, the `wallrun`
      attempts included.

- [ ] **Step 5: Run B, lag simulator (100 ms, 10% loss on the Editor's incoming traffic).**
  1. Turn it on and read it back:

```
unity command eval -- --code "var p = Photon.Pun.PhotonNetwork.NetworkingClient.LoadBalancingPeer; p.IsSimulationEnabled = true; p.NetworkSimulationSettings.IncomingLag = 100; p.NetworkSimulationSettings.IncomingJitter = 0; p.NetworkSimulationSettings.IncomingLossPercentage = 10; return p.IsSimulationEnabled + \" lag=\" + p.NetworkSimulationSettings.IncomingLag + \" loss=\" + p.NetworkSimulationSettings.IncomingLossPercentage;"
```

     Expected `True lag=100 loss=10`. If it throws or reads back differently, record "the harness can't simulate lag"
     and skip to Step 6.
  2. Same recorder and driver, into `remote-after-lag-E.csv` / `driver-after-lag-P.log`, then analyse with the extra
     argument `100`.
  - **Pass:** `dash` and `zip` rows still read `snaps=0`; every teleport-type row either reads `PASS` or has
    `sincePacketMs` of 150 or more (two or more updates lost in a row, risk R5) - say which.
  3. Turn it off:
     `unity command eval -- --code "var p = Photon.Pun.PhotonNetwork.NetworkingClient.LoadBalancingPeer; p.IsSimulationEnabled = false; return p.IsSimulationEnabled;"` → `False`.

- [ ] **Step 6: Run C, the other direction (short).** Recorder on Client2 (`__SIDE__` = `P`,
  `__OUT__` = `$S\remote-after-reverse-P.csv`, `__DUR__` = `60`), driver on the **Editor** (`__SIDE__` = `E`,
  `__OUT__` = `$S\driver-after-reverse-E.log`, `__PLAN__` = `wait:2;tele:8;wait:2;blink;wait:2;dash;wait:2;end`).
  Analyse into `remote-after-reverse.txt`. Same pass criteria as Step 4; it proves the receive side behaves the same
  in a Player build.

- [ ] **Step 7: Shut down.** As Task 1 Step 9.

- [ ] **Step 8: Diff the assets against BASE** (Task 6 Step 4). Both empty.

- [ ] **Step 9: Report.**
  - A table: before (Task 1 Part B) against after, per teleport-type event: `jump`, `errNextStep`, `stepsUntil0.1`.
  - The `dash`/`zip` snap counts for both runs.
  - `minArenaSigned` and `outsideRows` for every run.
  - Whether the lag simulator worked.
  - Update `progress.md` and the assumptions file; tell the controller which two-client checks from HANDOFF §4 this
    covers (#11 remote blink/teleport snap, and the 2026-09-16 "remote players slide ~0.25 s" item).

---

## Self-review: coverage

| The task asked for | Where it is | How it is proven |
|---|---|---|
| Bug A: remote copies snap on teleport/respawn/blink | Task 5 (`RemoteSnapRule`, `PlayerMotor`, `PlayerNetSync`) | `RemoteSnapRuleTests` (9); Task 7 runs A and C |
| Lerp from `rb.position`/`rb.rotation` | Task 5 Step 3.4 | Task 7: `errNextStep` ≤ 0.1 m |
| Snap decided on consecutive packets, not the trailing body | Task 5 Step 2 (`ShouldSnap`, `ElapsedIntervals`) | `AZipPullWithLostUpdatesStillGlides`; Task 7 `dash`/`zip` rows `snaps=0` |
| Zero `rb.linearVelocity` on snap | Task 5 Step 3.4 (guarded for a kinematic corpse) | Compiles and runs in Task 7's respawn row |
| The owner sends `rb.position` | Task 5 Step 4.2 | Task 7's respawn row |
| Bug B: two dashes through a thin wall | Task 2 | Task 1 (before) and Task 6 (after) `doubledash`, 18 rows |
| `CapsuleCast` lifted 0.05, stop at hit − 0.05 skin | Task 2 Step 5.7, `DisplacementSweepRule.SkinMetres`/`LiftMetres` | `DisplacementSweepRuleTests` (12) |
| Start-inside hits judged with `ComputePenetration` | Task 2 Step 5.7 (`AddStartInside`, probe pose) | `StartingInsideAWall…` tests; Task 1 `sweep` |
| The decision lives in a pure, edit-mode-tested class | `DisplacementSweepRule` | `DisplacementSweepRuleTests` |
| [C] A dash into a hugged wall is refused and keeps its charge | Task 2 Steps 5.6 and 6 | Task 6 `doubledash` (`spent=False`) and `hugdash` |
| Zip pull and pulse knockback use the same rule | Task 2 (one mover) | Task 6 `pin` |
| Riding up low edges (unverified) | Decision 4; measured, not changed | `lowedge` in Tasks 1 and 6 |
| Bug C: blink and portals leave the arena | Task 3 | Task 6 `blink` and `portal` |
| Pure point-in-polygon with a player-radius margin | `ArenaBounds` | `ArenaBoundsTests` (7) |
| The Source outline on `ArenaSymmetry`, rotated by `RadialSymmetry` | Task 3 Steps 3 and 8 | `ThePolygonIsTheSourcePointsThenTheir120And240Turns`; scene diff |
| A Validate check, and Rebuild keeps it consistent | Task 3 Step 4 (`CheckOutline`, called from `Validate`, which `Rebuild` ends with) | Three builder tests; `ArenaSymmetrySceneTests` |
| Blink walks back to an in-bounds point | Task 3 Step 11.1 (the existing `BlinkDestinationSearch` walk-back) | Task 6 `blink`, 6 rows |
| Blinking past crates stays | Decision, untouched code | Task 6 `blink` crate row |
| Portal refused outside bounds or with the path blocked | Task 3 Step 12.4 | Task 6 `portal`, two rows |
| Portal travel re-checks | Task 3 Step 12.5 (`IsExitClear` in `canChannel`) | Task 6 `portal` arrival; decision 9 |
| Portal arrival gets blink's height offset | Task 3 Steps 10 and 12.6 | `PlayerSpaceProbeTests`; Task 6 arrival heights |
| Owner-side safety net via `TeleportTo` | Task 4 | `OutOfArenaRuleTests` (7); Task 6 `safetynet` |
| Exposed for the mines' 2 m placement | "Hook for ability visuals step 3" (`IsInsideArena`, `IsPathClear`, `FeetOf`) | Compiles; that plan's Task 3 calls it |
| Two-client verification with per-frame recorders | Task 1 Part B and Task 7 | `remote_view_recorder_tpl.cs`, `analyze_movement.py` |
| Lag simulator at 100 ms + 10% loss | Task 7 Step 5 | Read back before the run; skipped and reported if unsupported |
| No RPC added, renamed or removed | Constraints; nothing touches an RPC | Tasks 6 and 7 Step 4 diff `PhotonServerSettings.asset` |
| `GameplayConfig.asset` unchanged | Decision 13 | Tasks 6 and 7 Step 4 |
| Capture timings unchanged | No capture file is touched | The full test suite each task |
| Movement feel changes listed | "Movement feel changes" (8 items) | Each one has a Task 6 row, except 8 (Task 7) |
| Tudor at the keyboard: no focus, no real mouse | Rule 7 | Every scenario aims with `SetAimOverride` and casts through `TryCast` |
| "T1–T4" only means zone tiers | Naming rule; tasks are "movement step N" | - |

