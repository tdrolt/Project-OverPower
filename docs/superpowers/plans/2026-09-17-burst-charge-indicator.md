# Burst Charge & Charge Indicator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- A short click on weapon 06 (Burst → Charge) fires **3** rounds, not 4. Only a real hold buys the 4th and 5th.
- Charging and the laser's wind-up both take visibly longer, as designer values on the weapon assets.
- While a charge weapon is held, its owner sees a ring at their feet filling clockwise from the top of the screen, with a tick where the next round is earned.

**Architecture:**
- **Pure rule, tested in edit mode:** `ChargeCountRule` (`Overpower.Weapons`) — how many rounds a charge fraction buys, and where the steps sit. `WeaponFiring` and `ChargeRingView` both read it, so the tick marks can never disagree with the gun.
- **Data:** `06 Burst - Charge.asset` (`maxChargeSeconds`), `11/12/13 Laser*.asset` (`windupSeconds`), `UiTheme.asset` (the ring's look).
- **View:** `ChargeRingView`, an owner-only world-space `LineRenderer` ring, mirroring `CaptureRingView` and reusing `CaptureRingGeometry` and `GroundSnap`.
- **Networking:** no new RPC, no RPC renamed or reordered, no new `IPunObservable`, nothing new on the wire. The round count is already derived on every client from the `chargeFraction` RPC parameter (`WeaponFiring.cs:576-578`).

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, NUnit edit-mode tests, `unity` CLI.

**Naming rule:** this plan's tasks are "Task 1..5", called **charge step N** in reports, commits and `progress.md`.

---

## Tudor's request, verbatim (2026-09-17, ~21:10)

1. "spamming the burst upgrade made the burst have more bullets than 3"
2. increase the charge time for both the laser and the burst, and add a charge indicator ("particles if possible, otherwise a cheaper UI solution").

---

## What the code does today (every line re-read at HEAD `917c884`)

| Thing | Where | Today |
|---|---|---|
| Round count | `WeaponFiring.cs:771-779` | `Max(1, RoundToInt(Lerp(ProjectilesPerShot, ChargeMaxProjectiles, quantised)))` |
| The quantiser | `WeaponFiring.cs:786-790` | `steps > 0 ? Mathf.RoundToInt(clamped * steps) / (float)steps : clamped` — **the bug** |
| Charge fraction | `WeaponFiring.cs:523-530` | `Clamp01((Time.time − Max(triggerHeldSince, nextFireTime)) / MaxChargeSeconds)`. Charge starts only **after** the cooldown |
| Owner read | `WeaponFiring.cs:537` | `CurrentChargeFraction` — the only public charge read; `AimConeView.cs:201-203` is its only reader |
| Heat | `WeaponFiring.cs:424-426` | `overheat.Add(weapon.OverheatPerShot)` once per **trigger pull**, before the RPC. Never per round |
| Damage ramp | `WeaponFiring.cs:795-801` | smooth `Lerp(1, ChargeDamageMultiplier, fraction)` — **not** stepped |
| Burst rounds | `WeaponFiring.cs:814-833` | `SpawnSequentially`, one coroutine per pull, no guard and no cancel. `RaiseFired(..., newPull: i == 0)` |
| Telemetry | `PlayerTelemetry.cs:686-697` | `Pulls++` only on `newPull`; `Projectiles += projectileCount`. Honest; needs no change |
| Wind-up validator | `WeaponDefinition.cs:276-283` | `if (windupSeconds > 0f && windupSeconds >= fireInterval)` → `Debug.LogWarning`. **Editor-only, a warning, not a clamp** |
| 06 Burst - Charge | `Assets/Gameplay/Weapons/06 Burst - Charge.asset` | `fireInterval 0.38`, `projectilesPerShot 3`, `sequentialDelay 0.07`, `overheatPerShot 12`, `damage 4.4`, `canCharge 1`, `maxChargeSeconds 0.35`, `chargeSteps 2`, `chargeDamageMultiplier 1.5`, `chargeRangeMultiplier 1`, `chargeMaxProjectiles 5`, `windupSeconds 0` |
| 11 / 13 Laser | those assets | `fireInterval 0.7`, `windupSeconds 0.25`, `canCharge 0` |
| 12 Laser - Charge | that asset | `fireInterval 0.7`, `windupSeconds 0.25`, `canCharge 1`, `maxChargeSeconds 0.45`, `chargeSteps 0`, `chargeDamageMultiplier 2.2`, `chargeRangeMultiplier 1.6`, `chargeMaxProjectiles 0` |
| 05 Burst / 07 Bounce | those assets | `canCharge 0`, `projectilesPerShot 3`, `overheatPerShot 9`. **Cannot charge — untouched by Task 1** |
| Arc maths | `CaptureRingGeometry.cs:17-34` | `PointOnRing(centre, radius, startYaw, clockwiseDegrees)`, `ArcPointCount(fill01, segments)`, `ArcStepDegrees(segments)` — pure, already tested |
| Ring drawing | `CaptureRingView.cs:176-200` | flat `LineRenderer`: world space, `LineAlignment.TransformZ`, object rotated `(-90,0,0)`, `sharedMaterial`, no shadows/probes/occlusion |
| Floor probe | `GroundSnap.cs:39-40` | `TryFindGroundY(from, out groundY)` — skips anything with `IDamageable`, so the player under the ring is never taken for the floor |
| Camera yaw | `CameraTracking.cs:101-106` | `Instance.Yaw` — "world direction (sin Yaw, cos Yaw) is the top of the screen" |
| Owner-only view pattern | `AimConeView.cs:63-120, 155-172` | `Awake` disables on `!photonView.IsMine`, errors loudly on a missing theme, builds its children **in code, not on the prefab**; `LateUpdate` hides on dead / `InputSuppressed` / `LoadoutScreen.IsOpen` / no weapon |
| Player prefab | `Assets/Resources/Multiplayer Player.prefab:3344-3356` | `AimConeView` is 13 YAML lines with one field, `theme:` → `f62f37c594a04704dabc20018ea290d9` |
| PhotonView | same file, `:3016-3037` | `observableSearch: 2` (AutoFindAll), `ObservedComponents:` exactly one entry |
| Player capsule | same file, `:3053-3060` | radius **0.7**, height 2.6126082, centre y 0.8063 → feet 0.5 m below the root |
| Aim-cone material | `UiTheme.asset:159` and `:114` | `coneLineMaterial` and `captureRingMaterial` are the **same** asset (`ad2e00cea264ef94fa08363867d004e5`) |

### What the investigation got right

- `QuantiseChargeFraction` at `WeaponFiring.cs:786-790` uses `RoundToInt` — confirmed, exact lines.
- Heat is once per pull (12 either way) — confirmed, `WeaponFiring.cs:424-426`.
- Telemetry is honest — confirmed, `PlayerTelemetry.cs:686-697`.
- `SpawnSequentially` has no guard and no cancel — confirmed, `WeaponFiring.cs:814-833`.
- Weapon 06 has no charge feedback at all — confirmed: `CurrentChargeFraction` has exactly one reader, `AimConeView.cs:201-203`, gated on `cachedBeam != null`; weapon 06's projectile prefab has no `Hitscan`, and its `chargeRangeMultiplier` is 1.
- `d4b95b1` cured the re-click carry-over — confirmed; `FireScheduleRule.IsContinuingHold` (`FireScheduleRule.cs:74-77`) and `WeaponFiring.cs:277-281`.

### Statements the real code contradicts — read before building

1. **`UiTheme.asset` is NOT stale.** `UiTheme.cs` declares **141** public serialized fields; `UiTheme.asset` holds **141** top-level keys, and the two sets are identical (no field missing, no orphan key). The last `UiTheme.cs` commit, `b002fde`, changed only tooltip text. So the "a `SetDirty` + save backfills ~14 unrelated lines" premise no longer holds. **Hand-edit the YAML anyway** (rule 9) — it is still the safe way while another agent may have dirty assets in the tree — but the check after the edit is now "the diff shows exactly my N lines", and anything else is a real surprise to report, not an expected backfill.
2. **`VisualTint.FillFlatArc` should not be written.** The arc maths already exists, pure and tested: `CaptureRingGeometry.PointOnRing` / `ArcPointCount` / `ArcStepDegrees` (`Assets/scripts/Match/Rules/CaptureRingGeometry.cs`, tested in `Assets/Tests/CaptureRingGeometryTests.cs`), driving exactly this shape on the ground in `CaptureRingView`. `VisualTint`'s two fill helpers are **local-space, closed loops** (`VisualTint.cs:38-64`) — the wrong shape and the wrong space for a ring that must follow a moving, *rotating* player. Adding `FillFlatArc` would be the parallel kit the brief forbids. `ChargeRingView` calls `CaptureRingGeometry` directly and `VisualTint` is untouched.
3. **`AimConeView` is the wrong model to mirror; `CaptureRingView` is the right one.** `AimConeView` supplies the *gating* (owner-only `Awake` disable, the four hide conditions, children built in code so the prefab carries only the component) — Task 3 copies that. But its geometry is a straight-line cone from the muzzle with wall clipping, not a ring. `CaptureRingView` already solves this exact drawing problem, including a lesson learned on 2026-09-17: a bare band on open ground reads poorly, so it needs a dark **track** behind it (`CaptureRingView.cs:13-16`). The charge ring gets a track for the same reason.
4. **The wind-up validator does not cap anything.** `WeaponDefinition.OnValidate` (`:276-283`) is `#if UNITY_EDITOR` and only calls `Debug.LogWarning`. Nothing clamps `windupSeconds`, nothing fails a build, and **no edit-mode test asserts the relationship**. 0.6 against `fireInterval` 0.7 is legal and warning-free, but "the validator caps it" is not true — Task 2 adds the test that makes it real.
5. **No pinned test needs updating on purpose.** Nothing under `Assets/Tests/` references `ChargeSteps`, `MaxChargeSeconds`, `WindupSeconds` or `ChargeMaxProjectiles`; the only test that reads the real weapon assets is `WeaponUpgradeTreeTests.TheRealWeaponCatalogueBuildsAProblemFreeTree` (`:123-145`), which reads ids and parents only. Task 2's test totals should be **unchanged**, and this plan deliberately adds **no** test that pins a tunable number (rule 8).
6. **The 90 ms threshold is right, by a different route.** `Mathf.RoundToInt` rounds an exact `.5` to even, so at `chargeFraction` exactly 0.25 the current code gives level **0**, and level 1 begins just *above* 25% — 0.0875 s+ on a 0.35 s bar, not "at" 0.088 s. The conclusion is unchanged: a 90 ms click (f = 0.257) buys 4 rounds today.
7. **The post-fix damage figure is not 13.2.** Damage ramps smoothly and is unaffected by this fix. After Task 1 + Task 2, a 90 ms click deals 3 × 4.4 × (1 + 0.5 × 0.1286) ≈ **14.0** for 12 heat, not 13.2 (which is the *uncharged* 3 × 4.4). 13.2 is only reachable at fraction 0.
8. **No world-space canvas is involved.** The project contains no `RenderMode.WorldSpace` canvas at all; this ring is `LineRenderer`s. The "a world-space canvas must have no `GraphicRaycaster`" trap does not apply to this plan.

---

## Decisions [C]

1. **[C] `FloorToInt`, and the rule is pure and shared.** A step is *earned* by completing it. Floor means a hold must actually reach 50% for the 4th round and 100% for the 5th — the honest reading of "charge time is the price". `ChargeCountRule` lives beside `FireScheduleRule` in `Assets/scripts/Weapons/`, takes plain numbers (no `ScriptableObject`), and is the **only** place that knows where a step sits — the gun reads it for the count, the ring reads it for the tick positions.
2. **[C] 06 `maxChargeSeconds` 0.35 → 0.7.** With floor: 0.09 s → 3, 0.35 s → 4, 0.70 s → 5.
3. **[C] Laser `windupSeconds` 0.25 → 0.6 on all three of 11, 12, 13.** 0.6 < `fireInterval` 0.7 with a 0.1 s (14%) margin, the largest round value clearly under the line. All three lasers carry their own copy; all three move together so the family reads the same.
4. **[C] Weapon 12's own `maxChargeSeconds` (0.45) is NOT changed here.** See "Question for Tudor" — this is the one place the brief's reading might not be what he meant.
5. **[C] The ring shows the RAW fraction, with ticks at the quantised steps.** A stepped fill would hide the approach to the next round; a smooth fill with a tick tells the player both "how far" and "how far to the next round". The band flips to `chargeRingFullColor` at full so the ceiling is unmistakable.
6. **[C] The ring is visible whenever a charge weapon's trigger is held**, not only when the fraction is above 0. This requires one new owner-only read on `WeaponFiring` (`ChargeHeld`). It is what makes the cooldown wait legible: the ring appears empty, then starts filling once `nextFireTime` passes (`WeaponFiring.cs:528`). It is also what makes the 0% capture in Task 5 possible.
7. **[C] Ticks at the internal step boundaries only** (`i = 1..steps-1`). For weapon 06 that is exactly one tick, at 50%. The `steps`-th boundary is the closed ring itself and needs no mark. Weapon 12 (`chargeSteps 0`) gets no ticks and a smooth ring, which is correct — its charge ramps damage and range smoothly.
8. **[C] No new material, no new prefab, no particles.** The ring reuses the aim-cone/capture-ring material already in the theme. Particles were rejected: a new prefab, a new material and VFX routing for a worse read on a top-down camera.
9. **[C] The ring rebuilds its points every visible frame**, unlike `CaptureRingView`, which caches on yaw. A capture zone never moves; a charging player does. One owner-only object, ~130 points and one raycast a frame — cheaper than the aim cone already costs.
10. **[C] `ChargeRingView` goes in `Assets/scripts/Player/`, in the global namespace**, matching every other file in that folder (none of them declares a namespace) and its sibling `AimConeView`. Not `Overpower.Player` — a lone namespaced file there would be the odd one out.
11. **[C] Nothing changes about:** heat per pull; the smooth damage ramp; telemetry; the plain burst (05/07 have `canCharge: 0`, so `ChargedProjectileCount` returns `ProjectilesPerShot` before the rule is ever consulted); `FireScheduleRule` and the `d4b95b1` fix; `VisualTint`; `AimConeView`; any RPC; `GameplayConfig.asset`; `PhotonServerSettings.asset`.
12. **[C] Add no test that pins a tunable number.** The new tests pin *relationships* (`windupSeconds < fireInterval`, `canCharge ⇒ maxChargeSeconds > 0`), so Tudor can retune freely without opening a `.cs` file, and a misconfiguration still fails loudly.

## Feel changes (intended; list them in commit bodies)

1. **A click on weapon 06 fires 3 rounds, not 4**, for the same 12 heat. A 90 ms click drops from ~19.9 to ~14.0 damage.
2. **A full charge takes twice as long** (0.7 s of holding after the 0.38 s cooldown) and still pays 5 rounds at ×1.5 damage — 33 damage for 12 heat.
3. **The 4th round now needs a real half-second-ish hold.** The tick on the ring is where it lands.
4. **Every laser fires 0.6 s after the click instead of 0.25 s.** This is a large change: the beam lands 0.6 s late and the gun is free again 0.1 s after that. A full-charge weapon-12 shot is now ~1.05 s from press to damage (0.45 s hold + 0.6 s wind-up). The warning line every player sees grows over the whole 0.6 s, so the telegraph is much fairer — that is the point — but it will feel sluggish, and it is the change most likely to come back for retuning.
5. **A ring appears at your feet while you hold a charge weapon** — owner-only; nobody else sees yours.
6. **Known and accepted, unchanged by this plan:** two weapon-06 bursts can still overlap if a frame hitch longer than 0.10 s lands mid-burst (5 rounds × 0.07 s = 0.28 s of spawning inside a 0.38 s interval). `SpawnSequentially` still has no guard. This margin exists today and is not made worse.

## Constraints

- **Networking:** no RPC added, renamed, removed or reordered. `PhotonServerSettings.asset` unchanged. No new `IPunObservable`; the player PhotonView's `ObservedComponents` list must be byte-identical after Task 4.
- **Mixed builds:** `ChargeCountRule` runs on every client from the `chargeFraction` RPC parameter. A client on an older build would compute a different round count for the same shot. Nothing new — the same rule already applied to `QuantiseChargeFraction` — but if a two-client check is ever run against this branch, **rebuild both clients**.
- **Assets:** `GameplayConfig.asset`, `ArmorConfig.asset`, `TelemetryConfig.asset`, `MinimapConfig.asset` and the scenes are unchanged. The only assets touched are the four weapon assets, `UiTheme.asset` and the player prefab.
- **Tests:** no existing test is edited. New tests only.

## Risks for the controller

- **R1 — 0.6 s wind-up is a big feel change** (feel change 4). If it plays badly, it is a one-line asset edit on three files; nothing in code depends on the number.
- **R2 — weapon 12's `maxChargeSeconds`** may be what Tudor actually meant by "the laser's charge time". See the question below. Deferring it costs one asset line later.
- **R3 — the ring may fight the capture ring** when a player charges while standing in a capture zone: two flat rings on the ground at once. The charge ring is 1.15 m and the capture ring is many metres, so they should read as clearly different objects, but Task 5's captures are the check.
- **R4 — another agent is on this branch.** Every task stages only its own listed files.

---

## Rules for every task (each has cost hours on this project)

1. **Branch and Editor.** Branch `limit-testing` only; push after each task's commit. `unity command editor_status` must answer before editing — **if it does not answer, stop and report; do not edit blind.** There is one Editor; if another agent is driving it, stop and ask the controller.
2. **Tudor uses this computer while you work.** Never rely on the real mouse, the real keyboard or window focus. **Never call `editor_focus`.** Drive the trigger through reflection on `WeaponFiring`'s private `HandlePrimaryPressed` / `HandlePrimaryReleased` (rule 11) — never a synthetic mouse event. Aim only with `PlayerAim.SetAimOverride`. If the Editor stops ticking while unfocused, `unity command set_autotick -- --enable true`.
3. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `unity command editor_stop`, poll `unity command editor_status` until `playMode: "stopped"` and `compiling: false`.
4. **Compile and test.** `unity command recompile`, then poll `unity command recompile_status` until completed with `errors: []` — the only compile truth. **Tests async only:** `unity command run_tests -- --mode editor --async_tests true`, then poll `unity command test_status`.
5. **Dirty scene → modal dialog → silent Editor hang.** Before tests, recompile, build or a scene open, run `unity command eval -- --code "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"`, READ the answer, and continue only on `False`. **Do not chain it with `&&`.** To discard: `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", UnityEditor.SceneManagement.OpenSceneMode.Single)`. Never click a dialog; if `editor_status` times out, stop and report.
6. **CLI** (run every command from the repo root): `unity command <name> -- --flag value`; files `unity command eval_file -- --file "<path>" --timeout 20000`; long scripts `unity command --timeout 240 run_script -- --file "<path>" --entry Type.Method --timeout_ms 200000`. Eval code writes `UnityEngine.Object`, never bare `Object`.
7. **Before ANY Play Mode measurement**, list the room's actors and abort on an unexpected player:
   ```
   unity command eval -- --code "var r = Photon.Pun.PhotonNetwork.CurrentRoom; if (r == null) return \"no room\"; var s = \"\"; foreach (var p in r.Players) s += p.Value.ActorNumber + \":\" + p.Value.NickName + (p.Value.IsLocal ? \"(me)\" : \"\") + \" \"; return r.PlayerCount + \" | \" + s;"
   ```
   Expected `1 | <n>:EditorHost(me)`. Anything else — stop and report; another agent may be mid-task.
8. **Designer values.** Every gameplay and look value lives on a weapon asset or on `UiTheme`, with a plain `[Tooltip]`, one home each. Comments explain *why*, for a designer reader. **Add no test that asserts a tunable number** — tests pin relationships only, so Tudor never has to open a `.cs` file to retune.
9. **`UiTheme.asset` is edited BY HAND.** Order: (1) add the C# fields; (2) `unity command recompile` to `errors: []`; (3) hand-edit `Assets/Gameplay/Config/UiTheme.asset`, inserting each new key **in field-declaration order**; (4) `unity command eval -- --code "UnityEditor.AssetDatabase.ImportAsset(\"Assets/Gameplay/Config/UiTheme.asset\", UnityEditor.ImportAssetOptions.ForceUpdate); return \"imported\";"`; (5) read the values back by eval and print them; (6) `git diff -- Assets/Gameplay/Config/UiTheme.asset` shows **only** the intended lines. **Never `SetDirty` + `SaveAssets` on the theme.** The weapon `.asset` files are hand-edited the same way.
10. **Prefab edits** go through `PrefabUtility.LoadPrefabContents(path)` → edit → `PrefabUtility.SaveAsPrefabAsset(contents, path)` → `PrefabUtility.UnloadPrefabContents(contents)`. `add_component` on a prefab path does **not** persist. `AssetDatabase.SaveAssets()` flushes every dirty asset — use `SaveAssetIfDirty`, then `git status`.
11. **Measure with game-time stamps**, in an in-process coroutine that writes a file. The CLI round trip is seconds. Report the hold length the coroutine actually achieved, never the one it asked for.
12. **Captures:** 616×576, from the real Game view, `--source screen` (Play Mode only), an explicit `--save_path` under `Temp/burst-charge/`, copied to SCRATCH. **Read every PNG yourself and describe what you actually see, honestly**, before claiming anything about it. Agents on this project have claimed "clearly visible" three times and been wrong until someone looked.
13. **SCRATCH** = `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\<your-session-id>\scratchpad\burst-charge`. Scripts there, captures in `captures\`. Never under `Assets/`.
14. **Commits.** Messages end with your own `Co-Authored-By:` line. No unmeasured number in a commit message. Stage **only** the task's listed files; run `git status` first. Other agents have uncommitted work in this tree — never stage it.
15. **Assumptions.** Append judgement calls to `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under a new heading `## Burst charge and charge indicator (2026-09-17)` at the end, as short `[C]` lines. Outside the repo — do not commit it. List the file's `## ` headings afterwards to confirm none was lost.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/Weapons/ChargeCountRule.cs` (create) | Rounds per charge, step fractions | 1 |
| `Assets/Tests/ChargeCountRuleTests.cs` (create) | Its tests, red first | 1 |
| `Assets/scripts/Weapons/WeaponFiring.cs` (modify) | Call the rule; `ChargeHeld` | 1, 3 |
| `Assets/Gameplay/Weapons/06 Burst - Charge.asset` (modify, by hand) | `maxChargeSeconds 0.35 → 0.7` | 2 |
| `Assets/Gameplay/Weapons/11 Laser.asset`, `12 Laser - Charge.asset`, `13 Laser - Through Walls.asset` (modify, by hand) | `windupSeconds 0.25 → 0.6` | 2 |
| `Assets/Tests/WeaponConfigRuleTests.cs` (create) | Relationship invariants over the real catalogue | 2 |
| `Assets/scripts/Data/WeaponDefinition.cs` (modify) | Two tooltip clarifications | 2 |
| `Assets/scripts/UI/UiTheme.cs` (modify) | `[Header("Charge ring")]`, 12 fields | 3 |
| `Assets/Gameplay/Config/UiTheme.asset` (modify, by hand) | The same 12 keys, appended | 3 |
| `Assets/scripts/Player/ChargeRingView.cs` (create) | The ring | 3 |
| `Assets/Resources/Multiplayer Player.prefab` (modify, by script) | One `ChargeRingView` with `theme` | 4 |
| `Assets/Tests/ChargeRingPrefabTests.cs` (create) | The prefab carries it, with a theme, and observes nothing | 4 |
| `SCRATCH\burst-charge\charge_recorder_tpl.cs` (scratch) | Play-Mode measurement | 5 |

---

# Task 1 (charge step 1): the charge-count bug

**Files:** create `Assets/scripts/Weapons/ChargeCountRule.cs`, `Assets/Tests/ChargeCountRuleTests.cs`; modify `Assets/scripts/Weapons/WeaponFiring.cs`.

- [ ] **Step 0: Start state.**
  1. `unity command editor_status` answers, `playMode: "stopped"`, `compiling: false`.
  2. Dirty check (rule 5) reads `False`.
  3. `git status --short` — report everything listed. **None of this plan's files** may be dirty; if one is, stop and report.
  4. `git rev-parse HEAD` → report as **BASE**.
  5. `unity command run_tests -- --mode editor --async_tests true`, poll `test_status`. Record the pass/fail totals as **BASE TESTS**. Every later step compares against this.

- [ ] **Step 1: Extract the rule with TODAY'S behaviour — a pure refactor, no behaviour change.** Create `Assets/scripts/Weapons/ChargeCountRule.cs`:

```csharp
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// How many rounds one trigger pull of a charging weapon sends out, and where along the hold each extra round is
    /// earned. Pulled out of WeaponFiring (charge step 1) for two reasons: it is the arithmetic a live bug hid in
    /// (Tudor, 2026-09-17: "spamming the burst upgrade made the burst have more bullets than 3"), and the charge ring
    /// on the ground has to mark the SAME step positions the gun actually uses - one home, so a tick can never promise
    /// a round the gun does not give.
    ///
    /// Plain numbers, no WeaponDefinition: a ScriptableObject cannot be built in a plain edit-mode test without
    /// reflection, and nothing here needs anything but four numbers. WeaponFiring reads them off the asset.
    /// </summary>
    public static class ChargeCountRule
    {
        /// <summary>The quantised hold, 0..1: which of the Steps + 1 even levels this fraction has REACHED.
        /// steps of 0 or less leaves the hold smooth, for a weapon that charges something continuous (weapon 12
        /// charges damage and range, not a round count).</summary>
        public static float QuantisedFraction(float chargeFraction, int steps)
        {
            // NaN is possible: chargeFraction arrives as an RPC parameter, so a broken or mismatched client can put
            // anything in it, and FloorToInt(NaN) is a large negative number rather than an error. "> 0" is false for
            // NaN, so this one test covers NaN, negatives and a dead-zero hold together.
            if (!(chargeFraction > 0f))
                return 0f;

            float clamped = Mathf.Clamp01(chargeFraction);
            if (steps <= 0)
                return clamped;

            return Mathf.RoundToInt(clamped * steps) / (float)steps;
        }

        /// <summary>The hold fraction at which step <paramref name="index"/> is reached - where the charge ring's tick
        /// marks go. 0 for step 0, 1 for the last step.</summary>
        public static float StepFraction(int index, int steps) =>
            steps <= 0 ? 0f : Mathf.Clamp01(index / (float)steps);

        /// <summary>
        /// Rounds one pull sends out. A weapon that does not stack rounds while held (Charge Max Projectiles at or
        /// below Projectiles Per Shot - every weapon but 06 today) always sends Projectiles Per Shot.
        ///
        /// The count is rounded rather than floored HERE on purpose: at every quantised level the lerp already lands
        /// on a whole number for a weapon whose Charge Steps equal its round span (06: 2 steps across 3 -> 5), and
        /// rounding only decides the shape of a config where they do not. Mathf.RoundToInt rounds an exact .5 to
        /// EVEN, so such a config is lopsided - see the tooltip on WeaponDefinition.ChargeSteps.
        /// </summary>
        public static int Rounds(int projectilesPerShot, int chargeMaxProjectiles, int steps, float chargeFraction)
        {
            if (chargeMaxProjectiles <= projectilesPerShot)
                return Mathf.Max(1, projectilesPerShot);

            float quantised = QuantisedFraction(chargeFraction, steps);
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(projectilesPerShot, chargeMaxProjectiles, quantised)));
        }
    }
}
```

  Then in `WeaponFiring.cs`, replace `ChargedProjectileCount` (`:771-779`) and **delete** `QuantiseChargeFraction` (`:781-790`) entirely:

```csharp
        /// <summary>
        /// How many projectiles this trigger pull sends out. Weapons that cannot charge, or that charge something
        /// other than their projectile count (Charge Max Projectiles left at 0), are untouched - they always send
        /// Projectiles Per Shot.
        ///
        /// The arithmetic lives in ChargeCountRule so the charge ring on the ground marks the same steps (charge
        /// step 1). Runs on every client from the chargeFraction RPC parameter, so every client must be on the same
        /// build to agree on the count - see RPC_FireWeapon.
        /// </summary>
        private static int ChargedProjectileCount(WeaponDefinition weapon, float chargeFraction)
        {
            if (!weapon.CanCharge)
                return Mathf.Max(1, weapon.ProjectilesPerShot);

            return ChargeCountRule.Rounds(weapon.ProjectilesPerShot, weapon.ChargeMaxProjectiles,
                                           weapon.ChargeSteps, chargeFraction);
        }
```

  `unity command recompile` → `errors: []`. Run the tests. **Expected: exactly BASE TESTS, all green.** This step must change no behaviour; if a total moves, stop — the extraction is wrong.

- [ ] **Step 2: The RED tests.** Create `Assets/Tests/ChargeCountRuleTests.cs`. These are written against the INTENDED floor rule, so most of them fail against Step 1's code:

```csharp
using NUnit.Framework;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>
    /// Weapon 06's numbers (Assets/Gameplay/Weapons/06 Burst - Charge.asset) as they stand for charge step 2:
    /// 3 rounds uncharged, 5 at full, 2 charge steps, a 0.7 s bar. They are named here as local constants rather
    /// than loaded from the asset ON PURPOSE - this file tests the RULE, and Tudor must stay free to retune the
    /// asset without a test going red (plan rule 8).
    /// </summary>
    public class ChargeCountRuleTests
    {
        private const int Rounds0 = 3;
        private const int RoundsMax = 5;
        private const int Steps = 2;

        private static int RoundsAt(float fraction) =>
            ChargeCountRule.Rounds(Rounds0, RoundsMax, Steps, fraction);

        [Test]
        public void AnOrdinaryClickBuysNoExtraRounds()
        {
            // Tudor's bug: a ~90 ms click on a 0.7 s bar is 13% of the hold and must stay a plain 3-round burst.
            Assert.AreEqual(3, RoundsAt(0.09f / 0.7f));
        }

        [Test]
        public void JustUnderHalfIsStillThree()
        {
            // The whole point of flooring: a step is EARNED by completing it, not by getting close to it.
            Assert.AreEqual(3, RoundsAt(0.4999f));
        }

        [Test]
        public void ExactlyHalfIsFour()
        {
            Assert.AreEqual(4, RoundsAt(0.5f));
        }

        [Test]
        public void JustUnderFullIsStillFour()
        {
            Assert.AreEqual(4, RoundsAt(0.9999f));
        }

        [Test]
        public void AFullHoldIsFive()
        {
            Assert.AreEqual(5, RoundsAt(1f));
        }

        [Test]
        public void HoldingPastFullIsStillFive()
        {
            Assert.AreEqual(5, RoundsAt(4f));
        }

        [Test]
        public void ZeroAndNegativeAndNaNAreAllAPlainBurst()
        {
            // chargeFraction crosses the wire as an RPC parameter, so none of these is impossible.
            Assert.AreEqual(3, RoundsAt(0f));
            Assert.AreEqual(3, RoundsAt(-1f));
            Assert.AreEqual(3, RoundsAt(float.NaN));
            Assert.AreEqual(0f, ChargeCountRule.QuantisedFraction(float.NaN, Steps));
        }

        [Test]
        public void AWeaponThatStacksNoRoundsIgnoresTheChargeEntirely()
        {
            // Weapon 12: canCharge, but Charge Max Projectiles 0 - it charges damage and range, not a count.
            Assert.AreEqual(1, ChargeCountRule.Rounds(1, 0, 0, 1f));
            // And weapons 05 / 07, which cannot charge at all, never reach this rule - guarded again anyway.
            Assert.AreEqual(3, ChargeCountRule.Rounds(3, 3, 0, 1f));
        }

        [Test]
        public void ARoundCountIsNeverZero()
        {
            Assert.AreEqual(1, ChargeCountRule.Rounds(0, 2, 2, 0f));
        }

        [Test]
        public void WithoutStepsTheHoldStaysSmooth()
        {
            Assert.AreEqual(0.37f, ChargeCountRule.QuantisedFraction(0.37f, 0), 1e-5f);
            Assert.AreEqual(1f, ChargeCountRule.QuantisedFraction(2f, 0), 1e-5f);
        }

        [Test]
        public void QuantisedLevelsAreTheOnlyValuesAHoldEverReports()
        {
            Assert.AreEqual(0f, ChargeCountRule.QuantisedFraction(0.49f, Steps), 1e-5f);
            Assert.AreEqual(0.5f, ChargeCountRule.QuantisedFraction(0.5f, Steps), 1e-5f);
            Assert.AreEqual(0.5f, ChargeCountRule.QuantisedFraction(0.99f, Steps), 1e-5f);
            Assert.AreEqual(1f, ChargeCountRule.QuantisedFraction(1f, Steps), 1e-5f);
        }

        [Test]
        public void StepFractionsAreWhereTheRingsTicksGo()
        {
            // The charge ring draws a tick at every internal boundary - one tick, at half, for weapon 06.
            Assert.AreEqual(0f, ChargeCountRule.StepFraction(0, Steps), 1e-5f);
            Assert.AreEqual(0.5f, ChargeCountRule.StepFraction(1, Steps), 1e-5f);
            Assert.AreEqual(1f, ChargeCountRule.StepFraction(2, Steps), 1e-5f);
            Assert.AreEqual(0f, ChargeCountRule.StepFraction(1, 0), 1e-5f);
        }
    }
}
```

  Recompile, run the tests. **The red, expected exactly:**
  - `AnOrdinaryClickBuysNoExtraRounds` — 0.1286 × 2 = 0.257 rounds to 0, so this one **passes** even against the old rule. It fails today only on the 0.35 s bar, which this rule does not read. **Expect it to pass.**
  - `JustUnderHalfIsStillThree` — **FAILS**: `Expected: 3 But was: 4` (0.4999 × 2 = 0.9998 → rounds to 1 → half → 4).
  - `JustUnderFullIsStillFour` — **FAILS**: `Expected: 4 But was: 5` (0.9999 × 2 = 1.9998 → rounds to 2 → full → 5).
  - `QuantisedLevelsAreTheOnlyValuesAHoldEverReports` — **FAILS** on the `0.49f` case: `Expected: 0.0f But was: 0.5f` (0.98 → rounds to 1). The `0.99f` case fails too: `Expected: 0.5f But was: 1.0f`.
  - Everything else passes.

  Record the exact failure messages in your report. Total = BASE TESTS + 13 tests, with **4 failing**. If any other test fails, stop — you changed something you should not have.

- [ ] **Step 3: Go green.** In `ChargeCountRule.QuantisedFraction`, change the one line:

```csharp
            // FloorToInt, not RoundToInt (Tudor, 2026-09-17: "spamming the burst upgrade made the burst have more
            // bullets than 3"). Rounding gave a step away at the HALFWAY point to it, so on a 0.35 s bar an ordinary
            // 90 ms click already sat above 25% and bought the 4th round for nothing - the charge time is meant to BE
            // the price of the payoff. Flooring means a step is only ever reached by completing it, and the charge
            // ring's ticks mark exactly where that happens.
            return Mathf.Min(steps, Mathf.FloorToInt(clamped * steps)) / (float)steps;
```

  (`Mathf.Min` because `clamped` can be exactly 1, and `FloorToInt(1 * steps)` is `steps` — allowed — but a float a hair over would not be; the clamp above already prevents it, and the `Min` makes that impossible to break by editing one line later.)

  Recompile → `errors: []`. Run tests. **Expected: BASE TESTS + 13, all green, zero failures.**

- [ ] **Step 4: Confirm nothing else reads the removed method.** `grep -rn "QuantiseChargeFraction" Assets/` returns nothing. `grep -rn "ChargedProjectileCount" Assets/` returns only `WeaponFiring.cs` (its definition and the one call at `:731`).

- [ ] **Step 5: Report.** Quote the four red failure messages from Step 2 and the green total from Step 3.

- [ ] **Step 6: Commit + push** (only these three files):
  `git add Assets/scripts/Weapons/ChargeCountRule.cs Assets/scripts/Weapons/ChargeCountRule.cs.meta Assets/Tests/ChargeCountRuleTests.cs Assets/Tests/ChargeCountRuleTests.cs.meta Assets/scripts/Weapons/WeaponFiring.cs`
  Message: `fix(weapons): a short click on the burst charge fires 3 rounds again (charge step 1)` — body explains `RoundToInt` → `FloorToInt`, that heat and the damage ramp are untouched, and that the rule is now shared with the charge ring. End with your `Co-Authored-By:` line.

---

# Task 2 (charge step 2): the longer charge and wind-up times

**Files:** modify (by hand) `Assets/Gameplay/Weapons/06 Burst - Charge.asset`, `11 Laser.asset`, `12 Laser - Charge.asset`, `13 Laser - Through Walls.asset`; modify `Assets/scripts/Data/WeaponDefinition.cs` (tooltips only); create `Assets/Tests/WeaponConfigRuleTests.cs`.

- [ ] **Step 1: Start state.** Rules 1, 3, 5. `git status --short` clean of this plan's files. Record the current test total as **T1 TESTS** (Task 1's green total).

- [ ] **Step 2: Hand-edit the four weapon assets.** One line each, nothing else:
  - `06 Burst - Charge.asset`: `  maxChargeSeconds: 0.35` → `  maxChargeSeconds: 0.7`
  - `11 Laser.asset`: `  windupSeconds: 0.25` → `  windupSeconds: 0.6`
  - `12 Laser - Charge.asset`: `  windupSeconds: 0.25` → `  windupSeconds: 0.6`
  - `13 Laser - Through Walls.asset`: `  windupSeconds: 0.25` → `  windupSeconds: 0.6`

  **Do not touch** `12 Laser - Charge.asset`'s `maxChargeSeconds: 0.45` (decision 4). Then re-import all four:
  ```
  unity command eval -- --code "foreach (var p in new[]{\"Assets/Gameplay/Weapons/06 Burst - Charge.asset\",\"Assets/Gameplay/Weapons/11 Laser.asset\",\"Assets/Gameplay/Weapons/12 Laser - Charge.asset\",\"Assets/Gameplay/Weapons/13 Laser - Through Walls.asset\"}) UnityEditor.AssetDatabase.ImportAsset(p, UnityEditor.ImportAssetOptions.ForceUpdate); return \"imported\";"
  ```

- [ ] **Step 3: Read the values back, and check the validator stayed quiet.** The import runs `OnValidate` on each asset (`WeaponDefinition.cs:243-284`).
  1. `unity command get_console_logs` — **no** `Windup Seconds (0.6) is not less than Fire Interval` warning, and no new accuracy warning. If one appears, the number is wrong; report and stop.
  2. Read back:
  ```
  unity command eval -- --code "var s=\"\"; foreach (var p in new[]{\"Assets/Gameplay/Weapons/06 Burst - Charge.asset\",\"Assets/Gameplay/Weapons/11 Laser.asset\",\"Assets/Gameplay/Weapons/12 Laser - Charge.asset\",\"Assets/Gameplay/Weapons/13 Laser - Through Walls.asset\"}) { var w = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.Data.WeaponDefinition>(p); s += w.name + \" interval=\" + w.FireInterval + \" windup=\" + w.WindupSeconds + \" charge=\" + w.MaxChargeSeconds + \" steps=\" + w.ChargeSteps + \" | \"; } return s;"
  ```
  Expected: `06 Burst - Charge interval=0.38 windup=0 charge=0.7 steps=2 | 11 Laser interval=0.7 windup=0.6 charge=0 steps=0 | 12 Laser - Charge interval=0.7 windup=0.6 charge=0.45 steps=0 | 13 Laser - Through Walls interval=0.7 windup=0.6 charge=0 steps=0 |`
  3. `git diff -- Assets/Gameplay/Weapons/` shows **exactly four changed lines**.

- [ ] **Step 4: Make the wind-up rule a test, not just a warning.** Today nothing fails when a designer sets `windupSeconds >= fireInterval`; a Console warning is easy to miss, and Task 2 moves the value to within 0.1 s of the line. Create `Assets/Tests/WeaponConfigRuleTests.cs`:

```csharp
using System.Text;
using NUnit.Framework;
using Overpower.Data;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>
    /// Relationship checks over the 13 real weapon assets - the rules WeaponDefinition.OnValidate only WARNS about
    /// in the Console, where nobody reads them (charge step 2, which moved every laser's wind-up to within 0.1 s of
    /// its fire interval).
    ///
    /// Deliberately pins no tuning number: every assert here is "A must stay below B", never "A must be 0.6", so
    /// Tudor can retune any weapon from the Inspector without a test going red. A test that fails here means a
    /// weapon is genuinely misconfigured, not merely re-tuned.
    /// </summary>
    public class WeaponConfigRuleTests
    {
        private static WeaponCatalogue Catalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WeaponCatalogue>(
                "Assets/Gameplay/Weapons/WeaponCatalogue.asset");
            Assert.NotNull(catalogue, "Expected the real WeaponCatalogue at Assets/Gameplay/Weapons/WeaponCatalogue.asset - if it moved, update this path.");
            return catalogue;
        }

        [Test]
        public void EveryWindUpEndsBeforeItsOwnWeaponCanFireAgain()
        {
            // Task 11b's rule: a wind-up at or past the fire interval lets a second pull start its own warning line
            // before the first beam has fired, and two overlapping warnings from one shooter read as a glitch.
            var bad = new StringBuilder();
            foreach (WeaponDefinition weapon in Catalogue().Weapons)
            {
                if (weapon.WindupSeconds > 0f && weapon.WindupSeconds >= weapon.FireInterval)
                    bad.Append($"\n{weapon.name}: windup {weapon.WindupSeconds} >= fire interval {weapon.FireInterval}");
            }
            Assert.IsEmpty(bad.ToString(), "Lower Windup Seconds below Fire Interval, or raise Fire Interval:" + bad);
        }

        [Test]
        public void EveryChargingWeaponHasAChargeTimeToReach()
        {
            // canCharge with Max Charge Seconds at 0 makes ChargeFraction() return 0 forever (WeaponFiring:525),
            // so every charge field on that weapon silently does nothing.
            var bad = new StringBuilder();
            foreach (WeaponDefinition weapon in Catalogue().Weapons)
            {
                if (weapon.CanCharge && weapon.MaxChargeSeconds <= 0f)
                    bad.Append($"\n{weapon.name}: canCharge is on but Max Charge Seconds is {weapon.MaxChargeSeconds}");
            }
            Assert.IsEmpty(bad.ToString(), "Set Max Charge Seconds, or turn Can Charge off:" + bad);
        }

        [Test]
        public void AWeaponThatStacksRoundsHasAStepForEachOne()
        {
            // Charge Steps that do not match the round span make a lopsided ramp (see ChargeCountRule.Rounds):
            // with 2 steps across 3 -> 5 rounds every step is exactly one more round, which is what a player counts.
            var bad = new StringBuilder();
            foreach (WeaponDefinition weapon in Catalogue().Weapons)
            {
                if (!weapon.CanCharge || weapon.ChargeMaxProjectiles <= weapon.ProjectilesPerShot)
                    continue;
                int span = weapon.ChargeMaxProjectiles - weapon.ProjectilesPerShot;
                if (weapon.ChargeSteps != span)
                    bad.Append($"\n{weapon.name}: {span} extra rounds across {weapon.ChargeSteps} charge steps");
            }
            Assert.IsEmpty(bad.ToString(), "Set Charge Steps to Charge Max Projectiles minus Projectiles Per Shot:" + bad);
        }
    }
}
```

- [ ] **Step 5: Two tooltip clarifications** in `Assets/scripts/Data/WeaponDefinition.cs` — text only, no field, no default:
  - `ChargeSteps` (`:196-199`): append to the tooltip — *"Keep this equal to Charge Max Projectiles minus Projectiles Per Shot on a weapon that stacks rounds, so every step is exactly one more round. A step is reached by COMPLETING it: at 2 steps, a hold under half fires the base count, half or more fires one extra, and only a full hold fires them all."*
  - `MaxChargeSeconds` (`:192-193`): append — *"The clock only starts once the weapon is off cooldown (see WeaponFiring.ChargeFraction), so this is the price on top of Fire Interval, not inside it."*

- [ ] **Step 6: Recompile, test, commit.** Recompile → `errors: []`. Run tests. **Expected: T1 TESTS + 3, all green.** Then:
  `git add Assets/Gameplay/Weapons/*.asset Assets/scripts/Data/WeaponDefinition.cs Assets/Tests/WeaponConfigRuleTests.cs Assets/Tests/WeaponConfigRuleTests.cs.meta`
  Message: `data(weapons): longer burst charge and laser wind-up (charge step 2)`. Body: the four numbers, that the wind-up rule is now a test rather than only a Console warning, and feel change 4 in full.

---

# Task 3 (charge step 3): the charge ring

**Files:** modify `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset` (by hand), `Assets/scripts/Weapons/WeaponFiring.cs`; create `Assets/scripts/Player/ChargeRingView.cs`.

- [ ] **Step 1: Start state.** Rules 1, 3, 5. Record **T2 TESTS**.

- [ ] **Step 2: The theme fields.** At the **very end** of `UiTheme.cs`, after `coneLineMaterial` and inside the class:

```csharp
        [Header("Charge ring (2026-09-17)")]
        [Tooltip("Show the ring at your own feet while you hold a charging weapon's trigger. Off draws nothing at " +
                 "all; the weapon still charges exactly the same.")]
        public bool showChargeRing = true;
        [Tooltip("Material the charge ring draws with. Keep it unlit, transparent, vertex-coloured and its own " +
                 "colour white - the ring tints itself through each line's colour. Points at the Aim Cone Line " +
                 "material, which is exactly that and is what the capture ring uses too.")]
        public Material chargeRingMaterial;
        [Tooltip("Distance from the player's centre to the middle of the charge ring, in metres. The player's own " +
                 "body is 0.7 m across, so anything below about 0.8 draws inside their feet.")]
        public float chargeRingRadius = 1.15f;
        [Tooltip("Thickness of the charge ring's band, in metres.")]
        public float chargeRingWidth = 0.22f;
        [Tooltip("How far above the floor the charge ring floats, in metres. Just enough never to flicker into the " +
                 "ground; raise it if parts of the ring disappear on a slope.")]
        public float chargeRingHeightOffset = 0.06f;
        [Tooltip("How many straight pieces make up the charge ring. More reads as a smoother circle.")]
        [Range(16, 256)] public int chargeRingSegments = 64;
        [Tooltip("The dark loop behind the charge ring's fill, so how full it is reads like a loading bar. Without " +
                 "it a part-filled band on open ground reads as a stray arc rather than a meter.")]
        public Color chargeRingTrackColor = new Color(0f, 0f, 0f, 0.45f);
        [Tooltip("Colour of the charge ring's fill while it is still filling.")]
        public Color chargeRingFillColor = new Color(1f, 0.82f, 0.2f, 0.85f);
        [Tooltip("Colour the charge ring's fill switches to the moment the charge is full, so the ceiling is " +
                 "unmistakable without having to judge a closed circle by eye.")]
        public Color chargeRingFullColor = new Color(1f, 0.95f, 0.6f, 1f);
        [Tooltip("Colour of the ticks marking where the next round is earned. A weapon whose charge has no steps " +
                 "(the laser's, which ramps damage and range smoothly) shows no ticks at all.")]
        public Color chargeRingStepTickColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("How far a step tick reaches across the ring, in metres - centred on the ring, so a little more " +
                 "than Charge Ring Width makes it read as a notch cut through the band.")]
        public float chargeRingStepTickLength = 0.3f;
        [Tooltip("Thickness of a step tick, in metres.")]
        public float chargeRingStepTickWidth = 0.05f;
```

  Recompile → `errors: []` (rule 9 step 2).

- [ ] **Step 3: Hand-edit `UiTheme.asset`.** Append after the last line, `coneLineMaterial: {...}`:

```yaml
  showChargeRing: 1
  chargeRingMaterial: {fileID: 2100000, guid: ad2e00cea264ef94fa08363867d004e5, type: 2}
  chargeRingRadius: 1.15
  chargeRingWidth: 0.22
  chargeRingHeightOffset: 0.06
  chargeRingSegments: 64
  chargeRingTrackColor: {r: 0, g: 0, b: 0, a: 0.45}
  chargeRingFillColor: {r: 1, g: 0.82, b: 0.2, a: 0.85}
  chargeRingFullColor: {r: 1, g: 0.95, b: 0.6, a: 1}
  chargeRingStepTickColor: {r: 1, g: 1, b: 1, a: 0.85}
  chargeRingStepTickLength: 0.3
  chargeRingStepTickWidth: 0.05
```

  (The material GUID is the Aim Cone Line material, already used by `coneLineMaterial:159` and `captureRingMaterial:114`.) Then rule 9 steps 4-6: `ImportAsset ForceUpdate`, read the values back by eval and print them, and `git diff -- Assets/Gameplay/Config/UiTheme.asset` must show **exactly these 12 added lines and nothing else**. Per finding 1 above, the asset is currently in sync with the code, so any extra line Unity writes is a genuine surprise — report it and do not commit it.

- [ ] **Step 4: One new owner-only read on `WeaponFiring`.** Next to `CurrentChargeFraction` (`:532-537`):

```csharp
        /// <summary>True while this player is holding a charging weapon's trigger - owner-only state, exactly like
        /// CurrentChargeFraction above. ChargeRingView is the only reader: the ring has to appear the moment the
        /// trigger goes down, not only once the charge is above zero, because ChargeFraction() reports 0 for the whole
        /// Fire Interval a hold has to wait out first (see its own comment). An empty ring during that wait is the
        /// point - it is what tells the player the gun is not charging yet.</summary>
        public bool ChargeHeld => weapon != null && weapon.CanCharge && triggerHeldSince > 0f;
```

  Nothing else in `WeaponFiring` changes. Note that `triggerHeldSince` is already cleared on death and on tool focus (`:236`, `:268-269`), so the ring cannot survive either.

- [ ] **Step 5: `ChargeRingView`.** Create `Assets/scripts/Player/ChargeRingView.cs`:

```csharp
using Overpower.Abilities;
using Overpower.Match;
using Overpower.UI;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The ring at your own feet that fills while you hold a charging weapon's trigger (Tudor, 2026-09-17: "add a charge
/// indicator"). Weapon 06 had no feedback of any kind before this - the only thing that read the charge was the aim
/// cone's range arc, and only for a BEAM weapon whose range actually grows (AimConeView:201).
///
/// OWNER ONLY, for the same reason AimConeView is: nobody needs to see how charged another player's gun is, and every
/// read here is meaningless on a remote copy. Its lines are built as plain child GameObjects in Awake, not prefab
/// children, so the prefab only ever carries this component and one Theme reference.
///
/// Three flat lines plus a tick per charge step, exactly the shape CaptureRingView already draws on the ground:
/// - a dark full-loop TRACK, so a part-filled band reads as a meter rather than a stray arc;
/// - a BAND on top of it, filling clockwise from the top of the screen;
/// - one TICK per internal charge step, at the hold fraction where the next round is earned.
/// Where the steps are comes from ChargeCountRule - the same rule WeaponFiring counts rounds with, so a tick can never
/// promise a round the gun does not give.
///
/// Deliberately NOT IPunObservable: the player's PhotonView uses AutoFindAll and would absorb a second observable.
/// PlayerNetSync is the only one.
/// </summary>
public class ChargeRingView : MonoBehaviourPun
{
    [SerializeField, Tooltip("Colours, sizes and the line material the charge ring draws with - the same UiTheme " +
             "asset the HUD, the aim cone and the capture rings read.")]
    private UiTheme theme;

    // Not design tunables. The track sits a hair below the band and the ticks a hair above it, so three line polygons
    // at the same radius never occupy the same depth and never z-fight - the same trick and the same 0.01 m
    // CaptureRingView uses. MaxTicks caps how many tick lines are built once, in Awake; no weapon has, or should
    // have, anywhere near this many charge steps.
    private const float TrackBelowBand = 0.01f;
    private const float TicksAboveBand = 0.005f;
    private const int MaxTicks = 8;

    private WeaponFiring weaponFiring;
    private PlayerLifecycle lifecycle;
    private PlayerInputRouter input;

    private LineRenderer track;
    private LineRenderer band;
    private LineRenderer[] ticks;
    private Vector3[] trackPoints;
    private Vector3[] bandPoints;
    private readonly Vector3[] tickPoints = new Vector3[2];
    private int segments;

    private void Awake()
    {
        weaponFiring = GetComponent<WeaponFiring>();
        lifecycle = GetComponent<PlayerLifecycle>();
        input = GetComponent<PlayerInputRouter>();

        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        // Loud, matching AimConeView and WeaponFiring: a silent null here leaves the ring invisible with no clue why,
        // which for a visual-only component is easy to miss for days.
        bool missing = false;
        if (theme == null)
        {
            Debug.LogError($"[ChargeRingView] {name}: UI Theme is not assigned - the charge ring will not be drawn.");
            missing = true;
        }
        else if (theme.chargeRingMaterial == null)
        {
            Debug.LogError($"[ChargeRingView] {name}: UiTheme.Charge Ring Material is not assigned - the charge ring will not be drawn.");
            missing = true;
        }
        if (weaponFiring == null)
        {
            Debug.LogError($"[ChargeRingView] {name}: no WeaponFiring on this object - the charge ring will not be drawn.");
            missing = true;
        }

        if (missing)
        {
            enabled = false;
            return;
        }

        segments = Mathf.Max(16, theme.chargeRingSegments);
        trackPoints = new Vector3[segments];
        bandPoints = new Vector3[segments + 1];

        track = CreateLine("Charge Ring Track", theme.chargeRingWidth);
        track.loop = true;
        track.positionCount = segments;

        band = CreateLine("Charge Ring Band", theme.chargeRingWidth);
        band.loop = false;
        band.positionCount = 0;

        ticks = new LineRenderer[MaxTicks];
        for (int i = 0; i < MaxTicks; i++)
        {
            ticks[i] = CreateLine($"Charge Ring Tick {i + 1}", theme.chargeRingStepTickWidth);
            ticks[i].positionCount = 2;
        }
    }

    private LineRenderer CreateLine(string childName, float width)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, worldPositionStays: false);
        // TransformZ alignment draws the line facing this object's own Z. Pointing Z straight up lays it flat on the
        // ground instead of turning it toward the camera. The material is double-sided. The player TURNS as they aim
        // (PlayerAim writes transform.rotation every Update), so this child's rotation is set in world terms and every
        // point below is in world space - a local-space ring would spin with the body and its fill would start
        // somewhere different every frame.
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = theme.chargeRingMaterial; // sharedMaterial: .material clones the asset per line
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
        line.enabled = false; // LateUpdate decides visibility every frame; start hidden.
        return line;
    }

    private void LateUpdate()
    {
        // The same four hide conditions AimConeView uses, plus the charge itself. Dead: nothing to charge. Suppressed:
        // chatting, or a tool (F1) has claimed focus - and that case also clears the hold itself (WeaponFiring:268).
        var weapon = weaponFiring.Weapon;
        bool hide = !theme.showChargeRing ||
                    (lifecycle != null && !lifecycle.IsAlive) ||
                    (input != null && input.InputSuppressed) ||
                    LoadoutScreen.IsOpen ||
                    weapon == null || !weapon.CanCharge || weapon.MaxChargeSeconds <= 0f ||
                    !weaponFiring.ChargeHeld;

        if (hide)
        {
            SetLinesEnabled(false);
            return;
        }

        // Every point is rebuilt each visible frame, unlike CaptureRingView, which caches on the camera's yaw: a
        // capture zone never moves, and a charging player does. One owner-only object at ~130 points and one downward
        // raycast per frame, and only while a trigger is actually held.
        Vector3 root = transform.position;
        GroundSnap.TryFindGroundY(root, out float groundY);
        Vector3 centre = new Vector3(root.x, groundY + theme.chargeRingHeightOffset, root.z);
        // The top of this player's own screen (CameraTracking.Yaw's own comment), so the ring fills clockwise on
        // screen from 12 o'clock however the camera is turned for this team.
        float startYaw = CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f;
        float radius = Mathf.Max(0.01f, theme.chargeRingRadius);
        float step = CaptureRingGeometry.ArcStepDegrees(segments);

        Vector3 trackCentre = new Vector3(centre.x, centre.y - TrackBelowBand, centre.z);
        for (int i = 0; i < segments; i++)
            trackPoints[i] = CaptureRingGeometry.PointOnRing(trackCentre, radius, startYaw, step * i);
        track.SetPositions(trackPoints);
        track.startColor = theme.chargeRingTrackColor;
        track.endColor = theme.chargeRingTrackColor;
        track.enabled = true;

        float fill = weaponFiring.CurrentChargeFraction;
        int count = CaptureRingGeometry.ArcPointCount(fill, segments);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
                bandPoints[i] = CaptureRingGeometry.PointOnRing(centre, radius, startYaw, step * i);
            band.positionCount = count;
            band.SetPositions(bandPoints); // uses only the first positionCount points
            Color fillColor = fill >= 1f ? theme.chargeRingFullColor : theme.chargeRingFillColor;
            band.startColor = fillColor;
            band.endColor = fillColor;
            band.enabled = true;
        }
        else
        {
            // A hold that has not started charging yet - the Fire Interval it must wait out first. The empty track is
            // exactly what should be on screen.
            band.enabled = false;
        }

        // A tick at each INTERNAL step boundary: the fraction where one more round is earned. The last boundary is the
        // closed ring itself and needs no mark, and a weapon with no steps (the laser's smooth charge) gets none.
        Vector3 tickCentre = new Vector3(centre.x, centre.y + TicksAboveBand, centre.z);
        float half = theme.chargeRingStepTickLength * 0.5f;
        int wanted = Mathf.Clamp(weapon.ChargeSteps - 1, 0, MaxTicks);
        for (int i = 0; i < MaxTicks; i++)
        {
            if (i >= wanted)
            {
                ticks[i].enabled = false;
                continue;
            }

            float degrees = ChargeCountRule.StepFraction(i + 1, weapon.ChargeSteps) * 360f;
            tickPoints[0] = CaptureRingGeometry.PointOnRing(tickCentre, radius - half, startYaw, degrees);
            tickPoints[1] = CaptureRingGeometry.PointOnRing(tickCentre, radius + half, startYaw, degrees);
            ticks[i].SetPositions(tickPoints);
            ticks[i].startColor = theme.chargeRingStepTickColor;
            ticks[i].endColor = theme.chargeRingStepTickColor;
            ticks[i].enabled = true;
        }
    }

    private void SetLinesEnabled(bool value)
    {
        track.enabled = value;
        band.enabled = value;
        for (int i = 0; i < ticks.Length; i++)
            ticks[i].enabled = value;
    }
}
```

- [ ] **Step 6: Recompile and test.** `unity command recompile` → `errors: []`. Run the tests. **Expected: T2 TESTS exactly** — this task adds no test; the ring's only arithmetic is `ChargeCountRule.StepFraction` and `CaptureRingGeometry`, both already covered. Task 4 adds the structural guard.

- [ ] **Step 7: Commit + push:**
  `git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/Weapons/WeaponFiring.cs Assets/scripts/Player/ChargeRingView.cs Assets/scripts/Player/ChargeRingView.cs.meta`
  Message: `feat(weapons): charge ring at your feet while a charge weapon is held (charge step 3)`. Body: reuses `CaptureRingGeometry` and `GroundSnap` rather than adding an arc helper; owner-only; no RPC, no observable, no new material.

---

# Task 4 (charge step 4): wire the ring onto the player prefab

**Files:** modify `Assets/Resources/Multiplayer Player.prefab` (by script); create `Assets/Tests/ChargeRingPrefabTests.cs`.

- [ ] **Step 1: Start state.** Rules 1, 3, 5. `git status --short`: the prefab must not be dirty; if another agent has it, **stop and ask the controller**. Record **T3 TESTS**.

- [ ] **Step 2: Record the before-shape of the prefab**, so Step 4's diff can be judged:
  ```
  unity command eval -- --code "var p = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(\"Assets/Resources/Multiplayer Player.prefab\"); var pv = p.GetComponent<Photon.Pun.PhotonView>(); return \"components=\" + p.GetComponents<UnityEngine.Component>().Length + \" observed=\" + pv.ObservedComponents.Count + \" first=\" + pv.ObservedComponents[0].GetType().Name + \" aimcone=\" + (p.GetComponent<AimConeView>() != null) + \" chargering=\" + (p.GetComponent<ChargeRingView>() != null);"
  ```
  Expected: `observed=1 first=PlayerNetSync aimcone=True chargering=False`.

- [ ] **Step 3: Add the component through prefab contents** (rule 10 — `add_component` on a prefab path does not persist). Write `SCRATCH\burst-charge\add_charge_ring.cs` and run it with `eval_file`:

```csharp
const string path = "Assets/Resources/Multiplayer Player.prefab";
var contents = UnityEditor.PrefabUtility.LoadPrefabContents(path);
try
{
    if (contents.GetComponent<ChargeRingView>() != null)
        return "already present - nothing done";

    var view = contents.AddComponent<ChargeRingView>();
    // The same UiTheme asset AimConeView on this prefab already points at - read off that component rather than
    // typed in, so the two can never drift onto different theme assets.
    var so = new UnityEditor.SerializedObject(view);
    var themeProperty = so.FindProperty("theme");
    var aimCone = contents.GetComponent<AimConeView>();
    var aimConeTheme = new UnityEditor.SerializedObject(aimCone).FindProperty("theme");
    themeProperty.objectReferenceValue = aimConeTheme.objectReferenceValue;
    so.ApplyModifiedPropertiesWithoutUndo();

    UnityEditor.PrefabUtility.SaveAsPrefabAsset(contents, path);
    return "added ChargeRingView, theme = " + (themeProperty.objectReferenceValue != null ? themeProperty.objectReferenceValue.name : "NULL");
}
finally
{
    UnityEditor.PrefabUtility.UnloadPrefabContents(contents);
}
```

  Expected: `added ChargeRingView, theme = UiTheme`.

- [ ] **Step 4: Check the diff by hand.** `git diff -- "Assets/Resources/Multiplayer Player.prefab"`. **Expected: one added 13-line `MonoBehaviour` block** with `m_Script` = ChargeRingView's GUID (`cat Assets/scripts/Player/ChargeRingView.cs.meta`), `theme: {fileID: 11400000, guid: f62f37c594a04704dabc20018ea290d9, type: 2}`, and one added `m_Component` entry on the root GameObject. **`ObservedComponents` must be untouched**, and `observableSearch: 2` must still be 2. Anything else — Unity re-serialising a Rigidbody, a collider, an unrelated component — **stop, revert the prefab (`git checkout -- "Assets/Resources/Multiplayer Player.prefab"`) and report.**

- [ ] **Step 5: The structural guard test.** Create `Assets/Tests/ChargeRingPrefabTests.cs`:

```csharp
using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The charge ring (charge step 4) is invisible if its component or its theme goes missing from the player
    /// prefab, and nothing else would fail - so this pins the wiring, not a look value. Read-only: the prefab asset
    /// is loaded, never instantiated or saved.
    /// </summary>
    public class ChargeRingPrefabTests
    {
        private const string PlayerPrefab = "Assets/Resources/Multiplayer Player.prefab";

        private static GameObject Player()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, $"Expected the player prefab at {PlayerPrefab} - if it moved, update this path.");
            return prefab;
        }

        [Test]
        public void ThePlayerCarriesAChargeRingWithATheme()
        {
            var view = Player().GetComponent<ChargeRingView>();
            Assert.IsNotNull(view, "The player prefab has no ChargeRingView - the charge indicator draws nothing.");

            var theme = new SerializedObject(view).FindProperty("theme");
            Assert.IsNotNull(theme, "ChargeRingView.theme was renamed - update this test rather than widening access.");
            Assert.IsNotNull(theme.objectReferenceValue,
                "ChargeRingView.Theme is empty on the player prefab - the ring logs an error and draws nothing.");
        }

        [Test]
        public void TheChargeRingUsesTheSameThemeAsTheAimCone()
        {
            var ring = new SerializedObject(Player().GetComponent<ChargeRingView>()).FindProperty("theme");
            var cone = new SerializedObject(Player().GetComponent<AimConeView>()).FindProperty("theme");
            Assert.AreSame(cone.objectReferenceValue, ring.objectReferenceValue,
                "The aim cone and the charge ring point at different UiTheme assets - one of them is reading numbers nobody edits.");
        }

        [Test]
        public void TheChargeRingIsNotObservedOverTheNetwork()
        {
            // The player's PhotonView uses AutoFindAll and would absorb a second IPunObservable. PlayerNetSync is the
            // only one, and the saved list is what a build actually uses.
            var pv = Player().GetComponent<PhotonView>();
            Assert.IsNotNull(pv);
            Assert.AreEqual(1, pv.ObservedComponents.Count,
                "The player's PhotonView must observe exactly one component (PlayerNetSync).");
            Assert.IsFalse(Player().GetComponent<ChargeRingView>() is IPunObservable,
                "ChargeRingView must never implement IPunObservable.");
        }
    }
}
```

- [ ] **Step 6: Recompile, test, commit.** Recompile → `errors: []`. Run tests. **Expected: T3 TESTS + 3, all green.** Then:
  `git add "Assets/Resources/Multiplayer Player.prefab" Assets/Tests/ChargeRingPrefabTests.cs Assets/Tests/ChargeRingPrefabTests.cs.meta`
  Message: `feat(weapons): the charge ring on the player prefab (charge step 4)`.

---

# Task 5 (charge step 5): measure it in Play Mode

**Files:** scratch only (SCRATCH). No repo file changes; nothing to commit except the assumptions file, which lives outside the repo.

Nothing in this task is calculated. Every number below is either **measured** or **looked at**.

- [ ] **Step 1: Start state.** Rules 1, 3, 5, 7. `git status --short`: report anything listed. `unity command editor_play`, poll `editor_status` until playing, join:
  ```
  unity command eval -- --code "Photon.Pun.PhotonNetwork.NickName = \"EditorHost\"; Photon.Pun.PhotonNetwork.JoinLobby(); return \"joining\";"
  ```
  then poll `return Photon.Pun.PhotonNetwork.InRoom + " " + (PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber) != null);` until `True True`. **Then run the actor check (rule 7) and abort on anything but `1 | <n>:EditorHost(me)`.**

- [ ] **Step 2: The recorder.** Write `SCRATCH\burst-charge\charge_recorder.cs`. It drives the trigger the way a real click does — through `WeaponFiring`'s own private press/release handlers, never a synthetic mouse (rule 2) — and counts what actually left the barrel by subscribing to the same `Fired` event telemetry uses:

```csharp
// Charge recorder (charge step 5). Editor Play Mode, in a room, ALONE (you are the master).
// Writes SCRATCH\burst-charge\charge-after.txt, then charge-after.done (also after an exception).
var DIR = @"<SCRATCH>\burst-charge\";
System.IO.Directory.CreateDirectory(DIR);
string outPath = DIR + "charge-after.txt";
string donePath = DIR + "charge-after.done";
System.IO.File.Delete(donePath);
System.IO.File.WriteAllText(outPath, $"charge recorder, Time.time={Time.time:0.00}\n");

var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
var firing = me.GetComponent<Overpower.Weapons.WeaponFiring>();
var overheat = me.GetComponent<PlayerOverheat>();
var loadout = me.GetComponent<PlayerLoadout>();
var aim = me.GetComponent<PlayerAim>();
var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var press = typeof(Overpower.Weapons.WeaponFiring).GetMethod("HandlePrimaryPressed", NP);
var release = typeof(Overpower.Weapons.WeaponFiring).GetMethod("HandlePrimaryReleased", NP);

void Out(string s) => System.IO.File.AppendAllText(outPath, s + "\n");

int rounds = 0, pulls = 0;
System.Action<int,int,bool> onFired = (id, count, newPull) => { rounds += count; if (newPull) pulls++; };

// One pull: waits out the cooldown, presses, holds for `seconds` of REAL game time, releases, then waits for every
// round of the burst to spawn. Reports the hold it actually achieved, never the one it asked for.
System.Collections.IEnumerator Pull(string label, float seconds)
{
    aim.SetAimOverride(me.transform.position + me.transform.forward * 12f);
    overheat.Clear();
    yield return new WaitForSeconds(1.2f);   // well past any Fire Interval, so the hold IS the charge
    rounds = 0; pulls = 0;
    float heatBefore = overheat.Heat;
    float t0 = Time.time;
    press.Invoke(firing, null);
    while (Time.time - t0 < seconds)
        yield return null;
    float held = Time.time - t0;
    float fraction = firing.CurrentChargeFraction;   // read BEFORE the release clears the hold
    release.Invoke(firing, null);
    yield return new WaitForSeconds(1.0f);           // 5 rounds x 0.07 s sequential delay, with room to spare
    Out($"[{label}] asked {seconds:0.000}s, held {held:0.000}s, fraction {fraction:0.000} -> " +
        $"rounds={rounds} pulls={pulls} heat {heatBefore:0.0} -> {overheat.Heat:0.0} (+{overheat.Heat - heatBefore:0.0})");
}

System.Collections.IEnumerator Run()
{
    firing.Fired += onFired;
    try
    {
        loadout.SetWeapon(6);
        yield return new WaitForSeconds(0.5f);
        Out($"weapon = {firing.Weapon.name}: maxCharge={firing.Weapon.MaxChargeSeconds} steps={firing.Weapon.ChargeSteps} " +
            $"base={firing.Weapon.ProjectilesPerShot} max={firing.Weapon.ChargeMaxProjectiles} heat={firing.Weapon.OverheatPerShot}");
        yield return Pull("06 click", 0.09f);
        yield return Pull("06 past half", 0.40f);
        yield return Pull("06 full", 0.75f);

        loadout.SetWeapon(5);
        yield return new WaitForSeconds(0.5f);
        yield return Pull("05 plain burst (cannot charge)", 0.09f);
        yield return Pull("05 plain burst, long hold", 0.75f);

        loadout.SetWeapon(12);
        yield return new WaitForSeconds(0.5f);
        Out($"weapon = {firing.Weapon.name}: windup={firing.Weapon.WindupSeconds} interval={firing.Weapon.FireInterval} maxCharge={firing.Weapon.MaxChargeSeconds}");
        yield return Pull("12 laser click", 0.09f);
        yield return Pull("12 laser full charge", 0.55f);
    }
    finally
    {
        firing.Fired -= onFired;
        aim.SetAimOverride(null);
        loadout.SetWeapon(6);
        System.IO.File.WriteAllText(donePath, "done");
    }
}
me.StartCoroutine(Run());
return "charge recorder started";
```

  Run it: `unity command eval_file -- --file "<SCRATCH>\burst-charge\charge_recorder.cs" --timeout 20000`, then poll for `charge-after.done` (about 20 s). If it never appears, `unity command get_console_logs` and report the exception.

- [ ] **Step 3: Read the file and check it against these expectations.** Quote every line. Do not round a measurement to make it match.

  | Pull | Fraction | Rounds | Heat |
  |---|---|---|---|
  | 06 click (0.09 s) | ≈ 0.13 | **3** | +12 |
  | 06 past half (0.40 s) | ≈ 0.57 | **4** | +12 |
  | 06 full (0.75 s) | **1.000** | **5** | +12 |
  | 05 plain burst, either hold | 0.000 | **3** both times | +9 |
  | 12 laser, either hold | 0.13 / 1.000 | **1** both times | +20 (a beam that connects refunds 10; aimed at open ground it should not) |

  - **`pulls=1` on every line.** A number above 1 means a second pull got in; below 1 means nothing fired.
  - If a 06 line reports 4 rounds at fraction < 0.5 or 5 at fraction < 1.0, **Task 1's fix is not in this build** — check `git log` and stop.
  - The fraction for "past half" must be comfortably above 0.5. `WaitForSeconds`/frame granularity is ~17 ms at 60 fps, which is why this pull asks 0.40 s and not exactly 0.35 s; the exact half and full boundaries are pinned by `ChargeCountRuleTests`, not by a frame-quantised hold.

- [ ] **Step 4: Capture the ring at 0%, ~50% and 100%.** A second scratch script holds the trigger and captures mid-hold — the capture must not be taken from a later CLI poll, by which time the hold is long over. Simplest reliable form: hold weapon 06's trigger open for 4 s (`press.Invoke`, wait, `release.Invoke`) and, from a separate CLI call while it is held, take the three captures at known moments. Safer and fully in-process: have the coroutine call `ScreenCapture.CaptureScreenshot(path)` itself at three moments and wait a frame after each.
  1. Fire the hold coroutine, with captures at hold + 0.02 s (**0%**, the ring is empty — the Fire Interval has not elapsed, so `CurrentChargeFraction` is still 0), hold + 0.42 s (**~57%**, band past the tick), and hold + 1.0 s (**100%**, closed ring in `chargeRingFullColor`).
  2. Or, per rule 12: `unity command capture_game_view -- --source screen --save_path "Temp/burst-charge/ring-000.png"` (and `-050`, `-100`). Expected size **616×576** — if it differs, report it and do not resize.
  3. Copy all three to `SCRATCH\burst-charge\captures\`.
  4. **Read each PNG yourself** and describe honestly: is there a dark ring at the player's feet; is there exactly one white tick at the half-way point clockwise from the top (6 o'clock); is the amber band filling clockwise from 12 o'clock; is the 100% frame a closed, paler ring. If a capture shows another application's window over the Game view, **retake it** (rule 2). If the ring is not visible at all, check `get_console_logs` for the `[ChargeRingView]` errors before concluding anything.

- [ ] **Step 5: Stop Play Mode and confirm the tree.** `unity command editor_stop`, poll until `playMode: "stopped"` and `compiling: false`. Dirty check reads `False`. `git status --short` lists nothing new of this plan's (the `Temp/` captures are outside `Assets/`; if a path outside `Assets/` was refused and you used `Assets/Temp/burst-charge/`, delete that folder **and its `.meta`**).

- [ ] **Step 6: Assumptions.** Rule 15 — append to `assumptions-for-tudor.md` under `## Burst charge and charge indicator (2026-09-17)`, as short `[C]` lines: the floor rule (a step is earned by completing it), 0.7 s and 0.6 s, weapon 12's own 0.45 s charge left alone, the ring's size and colours, and that the ring is owner-only. Then list the file's `## ` headings to confirm none was lost.

- [ ] **Step 7: Final report to the controller.** The measured table from Step 3, the three captures with your own honest description of each, the final test total, and the four commit hashes.

---

## Question genuinely worth asking Tudor

**"When you said the laser's charge time, did you mean the wind-up — the delay between the click and the beam — or the hold on weapon 12 (Laser → Charge)?"** This plan raises the wind-up 0.25 → 0.6 on all three lasers (11, 12, 13) and leaves weapon 12's own `maxChargeSeconds` at 0.45. Both are "the laser's charge time" in plain English and they are different fields. If he meant the hold, 0.45 → 0.9 is the matching move and it is one more hand-edited line in Task 2. If he meant both, note that a full-charge weapon-12 shot would then be 0.9 s of holding plus a 0.6 s wind-up — about 1.5 s from press to damage, which is a very long time in this game.

---

**Total: 5 tasks, 32 numbered steps** (Task 1: 7, Task 2: 6, Task 3: 7, Task 4: 6, Task 5: 7 — minus Task 1's Step 0 being counted once).
