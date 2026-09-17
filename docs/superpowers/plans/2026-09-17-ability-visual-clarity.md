# Ability Visual Clarity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** make six abilities readable from the game camera with rough primitive prefabs, visual only:
- mines and portals clearly different;
- the Electric Fence as a cage of posts and horizontal bars;
- the flamethrower as a flat cone from the caster;
- a square zip-gun hook;
- rocket blasts on the floor.

**Architecture:**
- **Pure, tested in edit mode:** `AbilityVisualGeometry` (fan vertices, post count, sphere-plane radius, root height,
  pull time, fade).
- **Shared kit:**
  - `GroundSnap`, `VisualTint`, `StandingBody`, `SnapVisualToGround`;
  - `BlastMarker` with its prefab;
  - two URP Unlit materials;
  - an `IDeployableView` hook that `NetworkedDeployable` calls on every client right after `OnPlaced`.
- **Views** (`MineView`, `PortalView`, `FenceCageView`, `FlameConeVisual`, `RocketBlastView`, `ZipBoltView`) sit on
  the existing prefabs. They read every size from the gameplay component itself.
- **Nothing new is networked:** each client builds its own look from state it already has (owner team, placement,
  the cast RPC it already receives).

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, NUnit edit-mode tests, `unity` CLI, two-client harness.

**Spec:** `docs/superpowers/specs/2026-09-17-ability-visual-clarity-design.md` (read it first; §2 has every gameplay
number and file:line this plan relies on).

**Naming rule:** "T1–T4" means zone tiers only. This plan's tasks are "Task 1..8", called **ability visuals step N**
in reports, commits and `progress.md`.

---

## Decisions [C] (Tudor delegated; all logged in `assumptions-for-tudor.md` by the task that builds them)

- **Order [C]:** start after the capture ring + minimap plan's step 7 is approved. Same single Editor; no shared files
  with that plan (this plan never edits `UiTheme`, `CameraTracking`, `BuildingManager` or `Building capture.cs`).
- **"The ultimate that cages" = Electric Fence (id 26) [C]:** the only ultimate that surrounds the caster.
- **Team colour = `UiTheme.ShotColorFor(ownerTeam)` [C]:** each view references `UiTheme.asset` through its own
  serialized `theme` field (like `ShotTeamVisuals`). **No `UiTheme` field is added**, so HANDOFF trap 8c never comes
  up.
- **Materials [C]:**
  - new `Assets/Gameplay/Abilities/Ability Visual Solid.mat` (URP Unlit, opaque);
  - new `Ability Visual Glass.mat` (URP Unlit, transparent, double-sided, no depth write);
  - every line reuses `Assets/Gameplay/UI/AimConeLine.mat`;
  - colour goes through `MaterialPropertyBlock` (`_BaseColor`) or line start/end colours.
- **New prefabs live next to their kin [C]:** `Blast Marker.prefab` in `Assets/Gameplay/Projectiles/`,
  `Flamethrower Cone.prefab` in `Assets/Gameplay/Abilities/`. Networked prefabs stay in `Assets/Resources/`.
- **Floor snap [C]:** a 0.25 m-up / 4 m-down raycast on Default|Building that skips anything with `IDamageable`. Only
  visual transforms move.
- **Mine [C]:**
  - dark 0.8 m puck, 4 spikes, a stud in the owner's team colour;
  - a Trigger Radius ring **for the owner's team only**;
  - a blast ring at Explosion Radius on every client;
  - detonation is read by `MineView` noticing `Mine` hide its own Visual (it already does on every client in
    `RPC_Detonate`), so **the detonation code is not edited**.
- **Portal [C]:** a see-through disc at the real diameter, a bright rim and team colour. A floating diamond on a stem
  shows **on the placer's screen only** ("yours to use"). No line between a player's two portals.
- **Cage [C]:**
  - posts ≤ 2.5 m apart (16 at radius 6), 1.6 m tall;
  - bars at 0.5, 1.0 and 1.5 m;
  - a floor band as wide as Ring Thickness;
  - see-through, **no colliders**.
- **Flamethrower [C]:**
  - a flat 24-slice fan plus outline; tip under the caster's **root** (the real apex); Cone Range and Cone Angle from
    the ability itself; repositioned every physics step like the hit check;
  - the Cone Range tooltip is corrected to say "root" (text only).
- **Zip gun [C]:**
  - a 0.45 m cube head, a **visual-only scale on the prefab's Head** (the 0.15 m Projectile Radius is untouched);
  - a rope to the shooter's muzzle in flight;
  - on the pull, every client shows a square anchor (side 2 × Tether Marker Radius) and a rope for
    max(Tether Marker Seconds, distance ÷ Pull Speed);
  - one fixed amber hook colour (ability projectiles stay untinted, `ShotTeamVisuals.cs:13-16`).
- **Rocket [C]:**
  - `ExplodeOnImpact` raises `Detonated(centre)` after the splash;
  - `RocketBlastView` drops a `BlastMarker` on the floor below: the splash sphere cut at a standing player's root height
    (radius ≈ 2.6 m at muzzle height, measured in step 6), in the shooter's team colour;
  - the old particle puff stays at the real hit point;
  - the cursor rocket's Fire Field **visual child** snaps to the floor.
- **Captures [C]:** freeze with `Time.timeScale = 0.0001` (not 0: `ProjectileMotor` despawns a shot whose step is 0), never `EditorApplication.isPaused` (it dropped the Photon
  connection, `progress.md` 2026-09-14). Remote Player captures are in-process `ScreenCapture`.

## Statements in the request or brief that the real code doesn't match (read before building)

1. **Nothing to separate from a gameplay collider.** None of the six has one:
   - mines and the fence use `OverlapSphere`;
   - portals use an XZ distance;
   - projectiles sphere-cast.

   Guard tests assert zero colliders instead (a `GameObject.CreatePrimitive` brings a collider, so scripts never use it).
2. **The fence doesn't trap or block.** It damages and slows on crossing its 6 ± 0.5 m band (`FenceCrossingState.cs:57-59`).
   The cage is drawn with no colliders; blocking would be a gameplay change for Tudor.
3. **The flamethrower's hit shape is already a true cone**, a flat sector from the root (`ConeFilter.cs:59-78`). Only its
   visual is a cylinder. No hit-shape mismatch, except that the Cone Range tooltip says "muzzle" (`FlamethrowerAbility.cs:85-87`)
   while the code measures from the root (`:218-229`).
4. **The hook has no collider.** Its hit is a 0.15 m sphere cast. Its current 0.2 m mesh is *smaller* than the 0.3 m hit
   diameter.
5. **Rocket "not on the ground":**
   - all blasts are about 2 m up (flat flight at muzzle height);
   - airbursts show **no** effect at all (`ProjectileMotor.cs:137,144,169,259-273`);
   - the cursor rocket's fire field disc and burn sphere both float (`DetonateAtCursor.cs:142-143`).

   The splash reach for a standing player is therefore about 2.6 m, not 3.
6. **The mine and fence already float 0.5 m** (placed at the caster's root, `MineAbility.cs:77`, `ElectricFenceAbility.cs:46`).
   Portals don't (ground-probed).
7. `Electric Fence.prefab` and `AoE Zone.prefab` have **two PhotonViews** each. Pinned as-is, not fixed.

---

## Rules for every task (each has cost hours on this project)

1. Branch `limit-testing` only; push after each task's commit. `unity command editor_status` must answer before editing.
2. **Visual only.** No change to:
   - gameplay numbers, hit shapes, timings or colliders;
   - RPCs or `RpcList`, Room Properties, `GameplayConfig.asset`, weapon assets.

   `AbilityVisualPrefabGuardTests` (step 1) is the contract. If it fails, **stop and report**; never edit a pinned value
   to make it pass.
3. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `unity command editor_stop`, poll
   `unity command editor_status` until `playMode: "stopped"` and `compiling: false`.
4. `unity command recompile`, then poll `unity command recompile_status` until completed with `errors: []` (the only
   compile truth). **Tests async only:** `unity command run_tests -- --mode editor --async_tests true`, then poll
   `unity command test_status`.
5. **Dirty scene → modal dialog → silent Editor hang.** Before tests, recompile, build, a prefab script or Play Mode, run
   `unity command eval -- --code "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"`.
   - Read the answer, and continue only if it is `False` (don't chain it with `&&`).
   - To discard: `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", UnityEditor.SceneManagement.OpenSceneMode.Single)`.
   - If `editor_status` times out: stop and report. Don't click dialogs.
6. **CLI:**
   - form: `unity command <name> -- --flag value`
   - files: `unity command eval_file -- --file "<path>" --timeout 20000`
   - prefab scripts: `unity command --timeout 240 run_script -- --file "<path>" --entry Type.Run --timeout_ms 200000`
   - eval code writes `UnityEngine.Object`, never bare `Object`
   - Player: `unity command --runtime-path "Builds/Client2" <name> -- ...`; its eval needs reflection (`two-client-harness.md` §6)
7. **SCRATCH** = `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ability-visuals\`.
   Every scratch script and capture goes there, never under `Assets/`. `__PLACEHOLDERS__` in a `_tpl` script are
   filled with PowerShell `-replace` into a new file for each use.
8. **Capture procedure** (every capture named `<name>`):
   1. Delete any old `SCRATCH\<name>.ready.txt`, then run the scenario script. It freezes the game with
      `Time.timeScale = 0.0001` at the right moment (not 0: `ProjectileMotor.Update` despawns any shot whose step is 0) and writes `SCRATCH\<name>.ready.txt` with measured facts.
   2. Wait for that file:
      `for ($i = 0; $i -lt 40 -and -not (Test-Path "SCRATCH\<name>.ready.txt"); $i++) { Start-Sleep -Seconds 2 }`
   3. `unity command capture_game_view -- --source screen --save_path "Temp/ability-visuals/<name>.png"`, then
      `Copy-Item "C:\UniStuff\Y3\MinorSkilled\GitAccess\Project-OverPower\Temp\ability-visuals\<name>.png" "SCRATCH\<name>.png"`.
      If a path outside `Assets/` is refused, use `Assets/Temp/ability-visuals/`, then delete that folder and its `.meta`.
   4. Unfreeze and release the aim:
      `unity command eval -- --code "UnityEngine.Time.timeScale = 1f; PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber).GetComponent<PlayerAim>().SetAimOverride(null); return UnityEngine.Time.timeScale;"` → `1`.
   5. **Read the PNG yourself and describe what you see**, plus the `.ready.txt` facts. The image must be 616×576 (the
      Game view). If not, say so. "Looks right" without looking has been wrong on this project four times.
9. **Prefab edits** only through the step's scratch script:
   - `PrefabUtility.LoadPrefabContents` → add parts with the `PrefabParts` helper (Task 2 Step 8), which creates them
     with `ObjectFactory.CreateGameObject` **inside the prefab's own preview scene** → `SaveAsPrefabAsset` →
     `UnloadPrefabContents`;
   - never `GameObject.CreatePrimitive` or `new GameObject` in an editor script (both land in the open Game Scene, and
     a primitive adds a collider);
   - the structure tests read every prefab back.
10. `AssetDatabase.SaveAssets()` flushes every dirty asset: use `AssetDatabase.SaveAssetIfDirty(asset)`, then
    `git status`. Must stay unchanged:
    - `Assets/Gameplay/Config/GameplayConfig.asset`, `Assets/Gameplay/Config/UiTheme.asset`;
    - `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset`;
    - everything in `Assets/Gameplay/Weapons/`;
    - `ProjectSettings/TimeManager.asset`.

    **Add no RPC, rename none, remove none.** New components never implement `IPunObservable`.
11. **Moving and aiming the player:**
    - never move a player with `transform.position`; use `PlayerDisplacement.TeleportTo` to a collider-free point, and
      wait at least a frame;
    - aim with `PlayerAim.SetAimOverride`;
    - cast abilities through `AbilityRunner`'s private `TryCast` by reflection, not `PressSlot` (harness §12);
    - change the loadout with `PlayerLoadout.SetAbility` / `SetWeapon`, so the other client equips too.
12. Measure with game-time stamps in an in-process coroutine that writes a file. The CLI round trip is several seconds.
13. Every look value is a serialized field with a plain `[Tooltip]` on the prefab component that uses it (one home).
    Comments explain *why*, for a designer reader.
14. Commit messages end with your own `Co-Authored-By:` line. No unmeasured number in a commit message. Stage **only**
    the task's listed files; check `git status` first. Other agents may have uncommitted telemetry or ring files in the
    tree: never stage those.
15. Append judgement calls to `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under
    `## Ability visual clarity (2026-09-17)` as short [C] lines (outside the repo; don't commit it).

**Capture prologue.** Every Play Mode capture script in Tasks 2–8 starts with this block, copied verbatim. Task 1's
scripts can't use it (they run before `GroundSnap` exists):

```csharp
// ---- capture prologue (ability visuals plan) ----
var SCR = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ability-visuals\";
var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
var runner = me.GetComponent<AbilityRunner>();
var loadout = me.GetComponent<PlayerLoadout>();
var aim = me.GetComponent<PlayerAim>();
var disp = me.GetComponent<PlayerDisplacement>();
var firing = me.GetComponent<Overpower.Weapons.WeaponFiring>();
var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
Overpower.Net.Teams.TryGetTeam(Photon.Pun.PhotonNetwork.LocalPlayer, out int myTeam);
int myActor = Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber;
var tryCast = typeof(AbilityRunner).GetMethod("TryCast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var spawn0 = UnityEngine.Object.FindFirstObjectByType<RoomManager>().teamSpawnPoints[0].position;
Overpower.TestRange.DummyTarget dummy = null;
foreach (var d in UnityEngine.Object.FindObjectsByType<Overpower.TestRange.DummyTarget>(FindObjectsSortMode.None))
    if (dummy == null || (d.transform.position - spawn0).sqrMagnitude < (dummy.transform.position - spawn0).sqrMagnitude)
        dummy = d;
int buildingMask = LayerMask.GetMask("Building");
var facts = new System.Text.StringBuilder();
var block = new MaterialPropertyBlock();
float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
Vector3 FreePoint(Vector3 target, float dist)
{
    for (int i = 0; i < 16; i++)
    {
        float a = i * 22.5f * Mathf.Deg2Rad;
        var p = new Vector3(target.x + Mathf.Sin(a) * dist, target.y, target.z + Mathf.Cos(a) * dist);
        if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
        if (Physics.Linecast(p + Vector3.up * 1.5f, target + Vector3.up * 1.5f, buildingMask, QueryTriggerInteraction.Ignore)) continue;
        return p;
    }
    facts.AppendLine($"WARN no free point {dist} m from {target:F1}");
    return target + new Vector3(dist, 0f, 0f);
}
void Cast(System.Type moduleType)
{
    Component module = runner.GetComponentInChildren(moduleType, true);
    facts.AppendLine($"cast {moduleType.Name} moduleFound={module != null}");
    if (module != null) tryCast.Invoke(runner, new object[] { module });
}
Color MeshColor(Renderer r) { r.GetPropertyBlock(block); return block.GetColor("_BaseColor"); }
float GroundUnder(Vector3 p) => Overpower.Abilities.GroundSnap.TryFindGroundY(p, out float y) ? y : float.NaN;
void Freeze(string name) { Time.timeScale = 0.0001f; System.IO.File.WriteAllText(SCR + name + ".ready.txt", facts.ToString()); }
// ---- end of prologue ----
```

**Play Mode join** (used by every Play Mode step; the Editor alone is the master):
1. Dirty check: `False`. Run `unity command editor_play`, then poll `editor_status` until playing.
2. `unity command eval -- --code "Photon.Pun.PhotonNetwork.NickName = \"EditorHost\"; Photon.Pun.PhotonNetwork.JoinLobby(); return \"joining\";"`
   - If it reports not connected, poll `return Photon.Pun.PhotonNetwork.IsConnectedAndReady;` first.
   - Then poll `return Photon.Pun.PhotonNetwork.InRoom + " " + (PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber) != null) + " " + UnityEngine.Object.FindObjectsByType<Overpower.TestRange.DummyTarget>(UnityEngine.FindObjectsSortMode.None).Length;`
     until `True True 3`.

## File map

| File | Responsibility | Step |
|---|---|---|
| `Assets/Tests/AbilityVisualPrefabGuardTests.cs` (create) | Pins every gameplay value and "no collider" on the touched prefabs | 1 |
| `Assets/scripts/Combat/AbilityVisualGeometry.cs` + `Assets/Tests/AbilityVisualGeometryTests.cs` (create) | Pure fan/post/sphere-plane/root/pull/fade maths | 2 |
| `Assets/scripts/Abilities/Visuals/GroundSnap.cs` + `Assets/Tests/GroundSnapTests.cs` (create) | Floor under a point, skipping bodies | 2 |
| `Assets/scripts/Abilities/Visuals/VisualTint.cs`, `StandingBody.cs`, `SnapVisualToGround.cs`, `BlastMarker.cs` (create) | Shared visual kit | 2 |
| `Assets/scripts/Abilities/Core/IDeployableView.cs` (create), `NetworkedDeployable.cs` (modify) | View hook after `OnPlaced` | 2 |
| `Assets/Gameplay/Abilities/Ability Visual Solid.mat`, `Ability Visual Glass.mat`, `Assets/Gameplay/Projectiles/Blast Marker.prefab` (script creates) | Materials and blast ring | 2 |
| `Assets/Tests/AbilityVisualStructureTests.cs` (create step 2, extend 3–7) | Prefab parts, wiring, no colliders | 2–7 |
| `Assets/scripts/Abilities/Visuals/MineView.cs`, `PortalView.cs` (create); `Assets/scripts/Abilities/Equipment/Mine.cs` (getters) | Mine and portal looks | 3 |
| `Assets/Resources/Mine.prefab`, `Assets/Resources/Portal.prefab` (modify) | | 3 |
| `Assets/scripts/Abilities/Visuals/FenceCageView.cs` (create); `Assets/scripts/Abilities/Ultimate/ElectricFence.cs` (getters); `Assets/Resources/Electric Fence.prefab` (modify) | Cage | 4 |
| `Assets/scripts/Abilities/Visuals/FlameConeVisual.cs` (create); `Assets/scripts/Abilities/Equipment/FlamethrowerAbility.cs` (modify); `Assets/Gameplay/Abilities/Flamethrower Cone.prefab` (create), `Flamethrower.prefab` (modify) | Flame fan | 5 |
| `Assets/scripts/Weapons/Effects/RocketBlastView.cs` (create); `Assets/scripts/Weapons/Effects/ExplodeOnImpact.cs` (modify); `Assets/Gameplay/Projectiles/Rocket.prefab`, `Rocket Distance.prefab`, `Rocket Cursor.prefab`, `Assets/Resources/Fire Field.prefab` (modify) | Floor blast ring | 6 |
| `Assets/scripts/Abilities/Visuals/ZipBoltView.cs` (create); `Assets/scripts/Abilities/Mobility/ZipGunAbility.cs` (modify); `Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab`, `Assets/Gameplay/Abilities/Zip Gun.prefab` (modify) | Square hook, rope, anchor | 7 |

---

### Task 1 (ability visuals step 1): pin the gameplay numbers, record a gameplay baseline, take "before" pictures

**What exists:** the values below were read from the prefab YAML on 2026-09-17 (spec §2). No test pins them today.

**Files:**
- Create: `Assets/Tests/AbilityVisualPrefabGuardTests.cs`
- Scratch (not committed): `SCRATCH\regression_recorder_tpl.cs`, `SCRATCH\before_captures_tpl.cs`

- [ ] **Step 0: Start state and BASE.**
  - `unity command editor_status` answers.
  - The dirty check reads `False`.
  - Run `git status` and note other agents' files.
  - Run `git rev-parse HEAD` and put the hash in your report as `BASE`; Task 8 diffs assets against it.
  - `New-Item -ItemType Directory -Force "SCRATCH"`.

- [ ] **Step 1: Create the guard tests** `Assets/Tests/AbilityVisualPrefabGuardTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Ability visuals step 1 (Tudor, 2026-09-17: make the abilities clearer - visual only). Pins every gameplay number on
    /// the prefabs the visual work touches, at the values they had before it started, and that none of them carries a
    /// collider. If a visual change moves one of these, it changed the gameplay: stop, don't edit the number here.
    /// Read-only: prefab assets are loaded, never instantiated or saved.
    /// </summary>
    public class AbilityVisualPrefabGuardTests
    {
        private static readonly string[] TouchedPrefabs =
        {
            "Assets/Resources/Mine.prefab",
            "Assets/Resources/Portal.prefab",
            "Assets/Resources/Electric Fence.prefab",
            "Assets/Resources/Fire Field.prefab",
            "Assets/Gameplay/Projectiles/Rocket.prefab",
            "Assets/Gameplay/Projectiles/Rocket Distance.prefab",
            "Assets/Gameplay/Projectiles/Rocket Cursor.prefab",
            "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab",
            "Assets/Gameplay/Abilities/Mines.prefab",
            "Assets/Gameplay/Abilities/Teleport.prefab",
            "Assets/Gameplay/Abilities/Electric Fence.prefab",
            "Assets/Gameplay/Abilities/Flamethrower.prefab",
            "Assets/Gameplay/Abilities/Zip Gun.prefab",
        };

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static SerializedObject Fields<T>(string path) where T : Component
        {
            T component = Load(path).GetComponent<T>();
            Assert.IsNotNull(component, $"{path} has no {typeof(T).Name}");
            return new SerializedObject(component);
        }

        private static SerializedProperty Field(SerializedObject so, string name)
        {
            SerializedProperty property = so.FindProperty(name);
            Assert.IsNotNull(property, $"{so.targetObject.GetType().Name}.{name}");
            return property;
        }

        private static void Float(SerializedObject so, string name, float expected) =>
            Assert.AreEqual(expected, Field(so, name).floatValue, 1e-5f, $"{so.targetObject.GetType().Name}.{name}");

        private static void Int(SerializedObject so, string name, int expected) =>
            Assert.AreEqual(expected, Field(so, name).intValue, $"{so.targetObject.GetType().Name}.{name}");

        private static void Bool(SerializedObject so, string name, bool expected) =>
            Assert.AreEqual(expected, Field(so, name).boolValue, $"{so.targetObject.GetType().Name}.{name}");

        private static void AssetAt(SerializedObject so, string name, string expectedPath) =>
            Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(Field(so, name).objectReferenceValue),
                $"{so.targetObject.GetType().Name}.{name}");

        private static void PhotonViews(string path, int expected) =>
            Assert.AreEqual(expected, Load(path).GetComponentsInChildren<PhotonView>(true).Length, path + " PhotonViews");

        [Test]
        public void NoTouchedPrefabHasACollider()
        {
            foreach (string path in TouchedPrefabs)
                Assert.IsEmpty(Load(path).GetComponentsInChildren<Collider>(true), path + " must have no collider");
        }

        [Test]
        public void MineNumbersAreUnchanged()
        {
            SerializedObject mine = Fields<Mine>("Assets/Resources/Mine.prefab");
            Float(mine, "lifetimeSeconds", 45f);
            Float(mine, "damage", 20f);
            Float(mine, "slowMagnitude", 0.4f);
            Float(mine, "slowSeconds", 2f);
            Float(mine, "triggerRadius", 1.8f);
            Float(mine, "explosionRadius", 2.2f);
            Float(mine, "armDelaySeconds", 0.5f);
            Float(mine, "destroyDelaySeconds", 0.5f);
            Int(mine, "detectionMask", -1);
            PhotonViews("Assets/Resources/Mine.prefab", 1);

            SerializedObject ability = Fields<MineAbility>("Assets/Gameplay/Abilities/Mines.prefab");
            Float(ability, "cooldownSeconds", 10f);
            Int(ability, "charges", 2);
            Int(ability, "maxActiveMines", 4);
            AssetAt(ability, "minePrefab", "Assets/Resources/Mine.prefab");
        }

        [Test]
        public void PortalNumbersAreUnchanged()
        {
            SerializedObject portal = Fields<Portal>("Assets/Resources/Portal.prefab");
            Float(portal, "lifetimeSeconds", 0f);
            Float(portal, "portalDiameter", 2.5f);
            PhotonViews("Assets/Resources/Portal.prefab", 1);

            SerializedObject ability = Fields<TeleportAbility>("Assets/Gameplay/Abilities/Teleport.prefab");
            Float(ability, "cooldownSeconds", 10f);
            Int(ability, "charges", 1);
            Float(ability, "placementRange", 5f);
            Int(ability, "maxPortals", 2);
            Float(ability, "channelSeconds", 3f);
            AssetAt(ability, "portalPrefab", "Assets/Resources/Portal.prefab");
        }

        [Test]
        public void ElectricFenceNumbersAreUnchanged()
        {
            SerializedObject fence = Fields<ElectricFence>("Assets/Resources/Electric Fence.prefab");
            Float(fence, "lifetimeSeconds", 8f);
            Float(fence, "radius", 6f);
            Float(fence, "ringThickness", 1f);
            Float(fence, "damagePerPass", 25f);
            Float(fence, "perTargetCooldownSeconds", 1f);
            Float(fence, "slowMagnitude", 0.5f);
            Float(fence, "slowSeconds", 1.5f);
            Bool(fence, "followsCaster", false);
            Int(fence, "detectionMask", -1);
            PhotonViews("Assets/Resources/Electric Fence.prefab", 2); // two today - flagged for Tudor, not fixed here

            SerializedObject ability = Fields<ElectricFenceAbility>("Assets/Gameplay/Abilities/Electric Fence.prefab");
            Float(ability, "cooldownSeconds", 0f);
            Int(ability, "charges", 1);
            AssetAt(ability, "fencePrefab", "Assets/Resources/Electric Fence.prefab");
        }

        [Test]
        public void FlamethrowerNumbersAreUnchanged()
        {
            SerializedObject flame = Fields<FlamethrowerAbility>("Assets/Gameplay/Abilities/Flamethrower.prefab");
            Float(flame, "cooldownSeconds", 13f);
            Int(flame, "charges", 1);
            Float(flame, "burnDamagePerSecond", 5f);
            Float(flame, "burnSeconds", 5f);
            Float(flame, "coneAngle", 45f);
            Float(flame, "coneRange", 7f);
            Float(flame, "spraySeconds", 1f);
            Int(flame, "detectionMask", -1);
        }

        [Test]
        public void ZipGunNumbersAreUnchanged()
        {
            SerializedObject zip = Fields<ZipGunAbility>("Assets/Gameplay/Abilities/Zip Gun.prefab");
            Float(zip, "cooldownSeconds", 15f);
            Int(zip, "charges", 1);
            Float(zip, "range", 15f);
            Float(zip, "projectileSpeed", 40f);
            Float(zip, "projectileRadius", 0.15f);
            Float(zip, "pullSpeed", 25f);
            AssetAt(zip, "projectilePrefab", "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab");

            SerializedObject motor = Fields<ProjectileMotor>("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab");
            Int(motor, "hitMask", 9);
            Float(motor, "maxLifetimeSeconds", 5f);
            Assert.IsNotNull(Load("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab").GetComponent<AbilityHitRelay>());
        }

        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab")]
        public void RocketSplashNumbersAreUnchanged(string path)
        {
            SerializedObject explode = Fields<ExplodeOnImpact>(path);
            Float(explode, "splashDamage", 20f);
            Float(explode, "splashRadius", 3f);
            Float(explode, "occlusionNudge", 0.1f);
            Int(explode, "splashMask", 1);
            Keyframe[] keys = Field(explode, "falloff").animationCurveValue.keys;
            Assert.AreEqual(2, keys.Length, "falloff keys");
            Assert.AreEqual(0f, keys[0].time, 1e-5f);
            Assert.AreEqual(1f, keys[0].value, 1e-5f);
            Assert.AreEqual(1f, keys[1].time, 1e-5f);
            Assert.AreEqual(0f, keys[1].value, 1e-5f);

            SerializedObject motor = Fields<ProjectileMotor>(path);
            Int(motor, "hitMask", 9);
            Float(motor, "maxLifetimeSeconds", 5f);
        }

        [Test]
        public void CursorRocketFireFieldNumbersAreUnchanged()
        {
            AssetAt(Fields<DetonateAtCursor>("Assets/Gameplay/Projectiles/Rocket Cursor.prefab"), "fireFieldPrefab",
                "Assets/Resources/Fire Field.prefab");
            SerializedObject field = Fields<FireField>("Assets/Resources/Fire Field.prefab");
            Float(field, "duration", 3f);
            Float(field, "damagePerSecond", 12f);
            Float(field, "radius", 2.5f);
            Float(field, "tickInterval", 1f);
            Int(field, "burnMask", 1);
            PhotonViews("Assets/Resources/Fire Field.prefab", 1);
        }
    }
}
```

- [ ] **Step 2: Recompile, dirty check (`False`), run tests async.** Expected: compiles clean, and **all** tests pass,
  because the guards describe today's prefabs. Report the totals and the new test count. If one fails, the prefab
  already differs from spec §2: report the value, don't change the test.

- [ ] **Step 3: Write the regression recorder** `SCRATCH\regression_recorder_tpl.cs`. Task 8 reuses it unchanged.

```csharp
// Ability visuals step 1 (__LABEL__ = base) and step 8 (__LABEL__ = after): gameplay regression recorder.
// Editor Play Mode, in a room, alone, test range on. Drives every ability the visual work touches against the practice
// dummy nearest team 0's spawn and writes what really happened. Visual-only work must leave the numbers identical.
var outPath = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ability-visuals\regression-__LABEL__.txt";
var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
var runner = me.GetComponent<AbilityRunner>();
var loadout = me.GetComponent<PlayerLoadout>();
var aim = me.GetComponent<PlayerAim>();
var disp = me.GetComponent<PlayerDisplacement>();
var firing = me.GetComponent<Overpower.Weapons.WeaponFiring>();
var ult = me.GetComponent<UltimateCharge>();
var spawn0 = UnityEngine.Object.FindFirstObjectByType<RoomManager>().teamSpawnPoints[0].position;
Overpower.TestRange.DummyTarget dummy = null;
foreach (var d in UnityEngine.Object.FindObjectsByType<Overpower.TestRange.DummyTarget>(FindObjectsSortMode.None))
    if (dummy == null || (d.transform.position - spawn0).sqrMagnitude < (dummy.transform.position - spawn0).sqrMagnitude)
        dummy = d;
var tryCast = typeof(AbilityRunner).GetMethod("TryCast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
int buildingMask = LayerMask.GetMask("Building");
var lines = new System.Collections.Generic.List<string>();
var totals = new System.Collections.Generic.SortedDictionary<string, float>();
var counts = new System.Collections.Generic.SortedDictionary<string, int>();
string section = "setup";
float sectionStart = 0f;

// Burn ticks every frame, so burn is summed; everything else is listed hit by hit.
System.Action<Overpower.TestRange.DummyTarget, Overpower.Combat.DamageResult, Overpower.Combat.DamageInfo> onDamaged = (d, result, info) =>
{
    if (d != dummy) return;
    string key = $"{section} | source={info.Source} weapon={info.WeaponId} ability={info.AbilityId}";
    totals[key] = (totals.TryGetValue(key, out float t) ? t : 0f) + info.Amount;
    counts[key] = (counts.TryGetValue(key, out int c) ? c : 0) + 1;
    if (info.Source != Overpower.Combat.DamageSource.Burn)
        lines.Add($"  hit +{Time.time - sectionStart:0.0}s amount={info.Amount:0.###} source={info.Source} weapon={info.WeaponId} ability={info.AbilityId}");
};

float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
Vector3 FreePoint(Vector3 target, float dist)
{
    for (int i = 0; i < 16; i++)
    {
        float a = i * 22.5f * Mathf.Deg2Rad;
        var p = new Vector3(target.x + Mathf.Sin(a) * dist, target.y, target.z + Mathf.Cos(a) * dist);
        if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
        if (Physics.Linecast(p + Vector3.up * 1.5f, target + Vector3.up * 1.5f, buildingMask, QueryTriggerInteraction.Ignore)) continue;
        return p;
    }
    lines.Add($"  WARN no free point {dist} m from {target:F1}");
    return target + new Vector3(dist, 0f, 0f);
}
void Begin(string name) { section = name; sectionStart = Time.time; dummy.ResetToFull(); lines.Add($"[{name}]"); }
void Cast(System.Type moduleType)
{
    Component module = runner.GetComponentInChildren(moduleType, true);
    lines.Add($"  cast {moduleType.Name} moduleFound={module != null}");
    if (module != null) tryCast.Invoke(runner, new object[] { module });
}

System.Collections.IEnumerator Run()
{
    Overpower.TestRange.DummyTarget.AnyDamaged += onDamaged;
    try
    {
        lines.Add($"dummy={dummy.name} at {dummy.transform.position:F2}");

        // 1. Mine (19) at the caster's feet, 1.6 m from the dummy - inside Trigger Radius 1.8.
        Begin("mine");
        loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 19);
        disp.TeleportTo(FreePoint(dummy.transform.position, 1.6f));
        yield return new WaitForSeconds(1f);
        Cast(typeof(Overpower.Abilities.MineAbility));
        yield return new WaitForSeconds(3f);

        // 2. Rocket (weapon 2), direct hit from 8 m.
        Begin("rocket");
        loadout.SetWeapon(2);
        disp.TeleportTo(FreePoint(dummy.transform.position, 8f));
        yield return new WaitForSeconds(1.5f);
        aim.SetAimOverride(dummy.transform.position);
        yield return null;
        yield return null;
        lines.Add($"  weapon={firing.Weapon.Id} fired={firing.TryFire()}");
        yield return new WaitForSeconds(2f);

        // 3. Cursor rocket (weapon 4): airburst 2 m beside the dummy - splash, then the fire field.
        Begin("cursor rocket");
        loadout.SetWeapon(4);
        yield return new WaitForSeconds(1.5f);
        Vector3 toDummy = dummy.transform.position - me.transform.position;
        toDummy.y = 0f;
        aim.SetAimOverride(dummy.transform.position + Vector3.Cross(Vector3.up, toDummy.normalized) * 2f);
        yield return null;
        yield return null;
        lines.Add($"  weapon={firing.Weapon.Id} fired={firing.TryFire()}");
        yield return new WaitForSeconds(5f);
        aim.SetAimOverride(null);
        loadout.SetWeapon(1);

        // 4. Electric fence (26): the dummy stands on the 6 m ring for the fence's whole 8 s life. Topped up every
        //    0.5 s so it never dies mid-count (ResetToFull leaves it alive, so the fence keeps tracking it).
        Begin("fence");
        loadout.SetAbility(Overpower.Data.AbilitySlot.Ultimate, 26);
        disp.TeleportTo(FreePoint(dummy.transform.position, 6f));
        yield return new WaitForSeconds(1f);
        ult.Fill();
        Cast(typeof(Overpower.Abilities.ElectricFenceAbility));
        for (int i = 0; i < 19; i++)
        {
            yield return new WaitForSeconds(0.5f);
            dummy.ResetToFull();
        }

        // 5. Flamethrower (21) from 4 m, then the whole burn.
        Begin("flamethrower");
        loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 21);
        disp.TeleportTo(FreePoint(dummy.transform.position, 4f));
        yield return new WaitForSeconds(1f);
        aim.SetAimOverride(dummy.transform.position);
        yield return null;
        yield return null;
        Cast(typeof(Overpower.Abilities.FlamethrowerAbility));
        yield return new WaitForSeconds(7f);
        aim.SetAimOverride(null);

        // 6. Zip gun (18) from 10 m at the dummy: how far the pull carries the caster.
        Begin("zip");
        loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 18);
        disp.TeleportTo(FreePoint(dummy.transform.position, 10f));
        yield return new WaitForSeconds(1f);
        aim.SetAimOverride(dummy.transform.position);
        yield return null;
        yield return null;
        Vector3 before = me.transform.position;
        Cast(typeof(Overpower.Abilities.ZipGunAbility));
        yield return new WaitForSeconds(1.5f);
        lines.Add($"  pulled={Flat(before, me.transform.position):0.0} m gapToDummy={Flat(me.transform.position, dummy.transform.position):0.0} m");
        aim.SetAimOverride(null);

        // 7. Portals (17): two gates 4 m either side of an open point; stand in one, arrive at the other.
        Begin("portal");
        loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 17);
        Vector3 open = FreePoint(dummy.transform.position, 12f);
        disp.TeleportTo(open);
        yield return new WaitForSeconds(1f);
        Vector3 away = open - dummy.transform.position;
        away.y = 0f;
        Vector3 across = Vector3.Cross(Vector3.up, away.normalized);
        aim.SetAimOverride(open + across * 4f);
        yield return null;
        yield return null;
        Cast(typeof(Overpower.Abilities.TeleportAbility));
        yield return new WaitForSeconds(0.5f);
        aim.SetAimOverride(open - across * 4f);
        yield return null;
        yield return null;
        Cast(typeof(Overpower.Abilities.TeleportAbility));
        yield return new WaitForSeconds(0.5f);
        aim.SetAimOverride(null);
        var gates = Overpower.Abilities.Portal.ForOwner(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
        lines.Add($"  gates={gates.Count}");
        if (gates.Count == 2)
        {
            var start = gates[0];
            var end = gates[1];
            lines.Add($"  diameters={start.PortalDiameter:0.00},{end.PortalDiameter:0.00}");
            disp.TeleportTo(new Vector3(start.transform.position.x, open.y, start.transform.position.z));
            yield return new WaitForSeconds(4f);
            lines.Add($"  after 4 s: toEndGate={Flat(me.transform.position, end.transform.position):0.0} m toStartGate={Flat(me.transform.position, start.transform.position):0.0} m");
        }
    }
    finally
    {
        Overpower.TestRange.DummyTarget.AnyDamaged -= onDamaged;
        aim.SetAimOverride(null);
        lines.Add("[totals]");
        foreach (var pair in totals)
            lines.Add($"  {pair.Key} count={counts[pair.Key]} total={pair.Value:0.0}");
        System.IO.File.WriteAllLines(outPath, lines);
    }
}
BuildingManager.Instance.StartCoroutine(Run());
return "recording (about 55 s) to " + outPath;
```

- [ ] **Step 4: Record the baseline.**
  1. Run **Play Mode join**.
  2. Fill `__LABEL__` with `base` into `SCRATCH\regression_recorder_base.cs`, then
     `unity command eval_file -- --file "SCRATCH\regression_recorder_base.cs" --timeout 20000`.
  3. Wait for `SCRATCH\regression-base.txt` (poll `Test-Path` every 5 s, up to 120 s). Read it and paste it into your
     report.
  4. Report what each section shows. **Don't fix anything here.** For orientation only:
     - the mine hits once for 20 (`Splash`, ability 19);
     - the rocket hits once for 38 (`Projectile`, weapon 2);
     - the cursor rocket shows `Splash` weapon 4 plus a `Burn` weapon 4 total;
     - the fence shows several `Zone` ability 26 hits of 25;
     - the flamethrower shows a `Burn` ability 21 total;
     - the zip pull shortens the gap;
     - the portal ends near the end gate.

- [ ] **Step 5: Write the "before" pictures script** `SCRATCH\before_captures_tpl.cs` (`__SHOT__` = 1, 2 or 3):

```csharp
// Ability visuals step 1: "before" pictures of today's visuals, frozen with Time.timeScale = 0.0001 for the capture.
var SCR = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ability-visuals\";
var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
var runner = me.GetComponent<AbilityRunner>();
var loadout = me.GetComponent<PlayerLoadout>();
var aim = me.GetComponent<PlayerAim>();
var disp = me.GetComponent<PlayerDisplacement>();
var firing = me.GetComponent<Overpower.Weapons.WeaponFiring>();
int myActor = Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber;
var tryCast = typeof(AbilityRunner).GetMethod("TryCast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var spawn0 = UnityEngine.Object.FindFirstObjectByType<RoomManager>().teamSpawnPoints[0].position;
Overpower.TestRange.DummyTarget dummy = null;
foreach (var d in UnityEngine.Object.FindObjectsByType<Overpower.TestRange.DummyTarget>(FindObjectsSortMode.None))
    if (dummy == null || (d.transform.position - spawn0).sqrMagnitude < (dummy.transform.position - spawn0).sqrMagnitude)
        dummy = d;
int buildingMask = LayerMask.GetMask("Building");
var facts = new System.Text.StringBuilder();
Vector3 FreePoint(Vector3 target, float dist)
{
    for (int i = 0; i < 16; i++)
    {
        float a = i * 22.5f * Mathf.Deg2Rad;
        var p = new Vector3(target.x + Mathf.Sin(a) * dist, target.y, target.z + Mathf.Cos(a) * dist);
        if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
        if (Physics.Linecast(p + Vector3.up * 1.5f, target + Vector3.up * 1.5f, buildingMask, QueryTriggerInteraction.Ignore)) continue;
        return p;
    }
    return target + new Vector3(dist, 0f, 0f);
}
void Cast(System.Type t) { tryCast.Invoke(runner, new object[] { runner.GetComponentInChildren(t, true) }); }
// The floor under a point, skipping bodies - written out here because GroundSnap doesn't exist until step 2.
float GroundUnderRaw(Vector3 p)
{
    float best = float.MaxValue, y = float.NaN;
    foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.25f, Vector3.down, 4.25f, LayerMask.GetMask("Default", "Building"), QueryTriggerInteraction.Ignore))
        if (h.collider.GetComponentInParent<Overpower.Combat.IDamageable>() == null && h.distance < best) { best = h.distance; y = h.point.y; }
    return y;
}
void Freeze(string name) { Time.timeScale = 0.0001f; System.IO.File.WriteAllText(SCR + name + ".ready.txt", facts.ToString()); }
int shot = __SHOT__;

System.Collections.IEnumerator Run()
{
    if (shot == 1)
    {
        // A mine at my feet, 12 m from the dummy, then two portals 4 m either side of it.
        Vector3 open = FreePoint(dummy.transform.position, 12f);
        loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 19);
        loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 17);
        disp.TeleportTo(open);
        yield return new WaitForSeconds(1f);
        Cast(typeof(Overpower.Abilities.MineAbility));
        Vector3 away = open - dummy.transform.position; away.y = 0f; away.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, away);
        disp.TeleportTo(open + away * 2f);
        yield return new WaitForSeconds(0.5f);
        aim.SetAimOverride(open + across * 4f); yield return null; yield return null;
        Cast(typeof(Overpower.Abilities.TeleportAbility));
        yield return new WaitForSeconds(0.5f);
        aim.SetAimOverride(open - across * 4f); yield return null; yield return null;
        Cast(typeof(Overpower.Abilities.TeleportAbility));
        yield return new WaitForSeconds(0.5f);
        aim.SetAimOverride(open);
        yield return new WaitForSeconds(0.5f);
        foreach (var m in Overpower.Abilities.Mine.ForOwner(myActor))
            facts.AppendLine($"mine root.y={m.transform.position.y:0.000} visual.y={m.transform.Find("Visual").position.y:0.000} ground.y={GroundUnderRaw(m.transform.position):0.000}");
        foreach (var p in Overpower.Abilities.Portal.ForOwner(myActor))
            facts.AppendLine($"portal root.y={p.transform.position.y:0.000} ground.y={GroundUnderRaw(p.transform.position):0.000} diameter={p.PortalDiameter}");
        Freeze("s1-before-mine-portal");
    }
    else if (shot == 2)
    {
        // The fence around me with the dummy 4 m away inside it, then a flamethrower spray at the dummy.
        loadout.SetAbility(Overpower.Data.AbilitySlot.Ultimate, 26);
        loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 21);
        disp.TeleportTo(FreePoint(dummy.transform.position, 4f));
        yield return new WaitForSeconds(1f);
        me.GetComponent<UltimateCharge>().Fill();
        Cast(typeof(Overpower.Abilities.ElectricFenceAbility));
        yield return new WaitForSeconds(0.3f);
        aim.SetAimOverride(dummy.transform.position); yield return null; yield return null;
        Cast(typeof(Overpower.Abilities.FlamethrowerAbility));
        yield return new WaitForSeconds(0.3f);
        foreach (var f in UnityEngine.Object.FindObjectsByType<Overpower.Abilities.ElectricFence>(FindObjectsSortMode.None))
        {
            Transform v = f.transform.Find("Visual");
            facts.AppendLine($"fence root.y={f.transform.position.y:0.000} visual.y={v.position.y:0.000} visualScale={v.localScale:F2} ground.y={GroundUnderRaw(f.transform.position):0.000}");
        }
        var spray = GameObject.Find("Flamethrower Spray VFX (cheap, cosmetic only)");
        facts.AppendLine(spray == null ? "NO SPRAY OBJECT"
            : $"flame centre={spray.transform.position:F2} scale={spray.transform.localScale:F2} me.root={me.transform.position:F2} ground.y={GroundUnderRaw(me.transform.position):0.000}");
        Freeze("s1-before-fence-flame");
    }
    else
    {
        // A rocket's direct hit on the dummy from 8 m, frozen the frame it lands.
        loadout.SetWeapon(2);
        disp.TeleportTo(FreePoint(dummy.transform.position, 8f));
        yield return new WaitForSeconds(1.5f);
        bool hit = false;
        System.Action<Overpower.TestRange.DummyTarget, Overpower.Combat.DamageResult, Overpower.Combat.DamageInfo> onHit =
            (d, r, info) => { if (d == dummy && info.WeaponId == 2) { hit = true; Time.timeScale = 0.0001f; } };
        Overpower.TestRange.DummyTarget.AnyDamaged += onHit;
        aim.SetAimOverride(dummy.transform.position); yield return null; yield return null;
        facts.AppendLine($"fired={firing.TryFire()} muzzle.y={firing.MuzzlePosition.y:0.000} root.y={me.transform.position.y:0.000} ground.y={GroundUnderRaw(me.transform.position):0.000}");
        float giveUp = Time.realtimeSinceStartup + 5f;
        while (!hit && Time.realtimeSinceStartup < giveUp) yield return null;
        Overpower.TestRange.DummyTarget.AnyDamaged -= onHit;
        yield return null;
        var puff = GameObject.Find("FXBulletExplosion(Clone)");
        facts.AppendLine($"hit={hit} " + (puff == null ? "NO PUFF OBJECT" : $"puff at {puff.transform.position:F2} ground.y={GroundUnderRaw(puff.transform.position):0.000}"));
        Freeze("s1-before-rocket");
    }
}
BuildingManager.Instance.StartCoroutine(Run());
return "running shot " + shot;
```

- [ ] **Step 6: Take the three "before" pictures** (still in Play Mode). For `__SHOT__` 1, 2 and 3 in turn, fill
  `SCRATCH\before_captures_1.cs` / `_2` / `_3`, run each with `eval_file`, and follow the **Capture procedure** for
  `s1-before-mine-portal`, `s1-before-fence-flame` and `s1-before-rocket`. Report the facts:
  - `visual.y − ground.y` for the mine;
  - fence visual y against the ground;
  - flame centre and scale;
  - the puff's height above the ground.

  These are the measured "floats" the spec only computed.

- [ ] **Step 7: Stop.** Run `unity command editor_stop`, then poll until stopped. Dirty check: `False`. Then
  `git status`: only your test file is new.

- [ ] **Step 8: Commit and push** (only these files):

```
git add Assets/Tests/AbilityVisualPrefabGuardTests.cs Assets/Tests/AbilityVisualPrefabGuardTests.cs.meta
git status
git commit -m "test(abilities): pin gameplay numbers before the visual clarity work (ability visuals step 1)" -m "Co-Authored-By: <your model line>"
git push
```

Add `[C] Ability visuals: gameplay values pinned by AbilityVisualPrefabGuardTests; baseline in regression-base.txt`
under the assumptions heading (Rule 15).

---
### Task 2 (ability visuals step 2): the shared visual kit

**What exists:**
- `NetworkedDeployable.InitializeAfterServerTimeIsReady` calls `OnPlaced` (`NetworkedDeployable.cs:203`), then hides
  an already-expired copy's renderers and colliders (`:205-214`, `:271-277`).
- `AimConeView.CreateLine` (`Assets/scripts/Player/AimConeView.cs:122-151`) shows the line settings this project uses.
- `ArenaSymmetryBuilderTests` shows how a test builds objects in a preview scene without touching the open scene.

**Files:**
- Create: `Assets/scripts/Combat/AbilityVisualGeometry.cs`, `Assets/Tests/AbilityVisualGeometryTests.cs`
- Create: `Assets/scripts/Abilities/Visuals/GroundSnap.cs`, `VisualTint.cs`, `StandingBody.cs`,
  `SnapVisualToGround.cs`, `BlastMarker.cs` (the folder's `.meta` too)
- Create: `Assets/scripts/Abilities/Core/IDeployableView.cs`
- Modify: `Assets/scripts/Abilities/Core/NetworkedDeployable.cs`
- Create: `Assets/Tests/GroundSnapTests.cs`, `Assets/Tests/AbilityVisualStructureTests.cs`
- Script creates: `Assets/Gameplay/Abilities/Ability Visual Solid.mat`, `Assets/Gameplay/Abilities/Ability Visual Glass.mat`,
  `Assets/Gameplay/Projectiles/Blast Marker.prefab`
- Scratch: `SCRATCH\MakeVisualKit.cs`, `SCRATCH\s2_marker_check.cs`

- [ ] **Step 1: Failing geometry tests.** Create `Assets/Tests/AbilityVisualGeometryTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    public class AbilityVisualGeometryTests
    {
        // Flamethrower.prefab's Cone Range and Cone Angle (pinned by AbilityVisualPrefabGuardTests).
        private const float Range = 7f;
        private const float Angle = 45f;
        private const int Slices = 24;

        private static void AssertClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), $"expected {expected:F4} but was {actual:F4}");

        [Test]
        public void CirclePointStartsAtPlusZAndTurnsClockwiseSeenFromAbove()
        {
            AssertClose(new Vector3(0f, 0f, 2f), AbilityVisualGeometry.CirclePoint(2f, 0f));
            AssertClose(new Vector3(2f, 0f, 0f), AbilityVisualGeometry.CirclePoint(2f, 90f));
            AssertClose(new Vector3(0f, 0f, -2f), AbilityVisualGeometry.CirclePoint(2f, 180f));
        }

        [Test]
        public void TheFanStartsAtTheCasterAndItsArcRunsEdgeToEdgeAtFullRange()
        {
            Assert.AreEqual(Slices + 2, AbilityVisualGeometry.FanVertexCount(Slices));
            AssertClose(Vector3.zero, AbilityVisualGeometry.FanVertex(0, Range, Angle, Slices));
            Vector3 left = AbilityVisualGeometry.FanVertex(1, Range, Angle, Slices);
            Vector3 right = AbilityVisualGeometry.FanVertex(Slices + 1, Range, Angle, Slices);
            Assert.AreEqual(Range, left.magnitude, 1e-4f);
            Assert.AreEqual(Range, right.magnitude, 1e-4f);
            Assert.AreEqual(Angle * 0.5f, Vector3.Angle(Vector3.forward, left), 1e-3f);
            Assert.AreEqual(Angle * 0.5f, Vector3.Angle(Vector3.forward, right), 1e-3f);
            Assert.Less(left.x, 0f, "the first arc point is the left edge");
            Assert.Greater(right.x, 0f, "the last arc point is the right edge");
        }

        [Test]
        public void EveryFanPointIsInsideTheFlamethrowersRealHitCone()
        {
            for (int i = 0; i < AbilityVisualGeometry.FanVertexCount(Slices); i++)
            {
                Vector3 p = AbilityVisualGeometry.FanVertex(i, Range, Angle, Slices);
                Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, p, Range + 1e-3f, Angle + 1e-2f), $"vertex {i} {p:F4}");
            }
            Assert.IsFalse(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, AbilityVisualGeometry.CirclePoint(Range, Angle * 0.5f + 1f), Range, Angle),
                "control: a point 1 degree past the edge is outside");
        }

        [Test]
        public void TheFansStraightArcPiecesStayWithinACentimetreOfTheRealArc()
        {
            for (int i = 1; i <= Slices; i++)
            {
                Vector3 mid = (AbilityVisualGeometry.FanVertex(i, Range, Angle, Slices) + AbilityVisualGeometry.FanVertex(i + 1, Range, Angle, Slices)) * 0.5f;
                Assert.Greater(mid.magnitude, Range - 0.01f, $"slice {i}");
            }
        }

        [Test]
        public void FanTrianglesAllShareTheApexAndFaceUp()
        {
            var triangles = new int[Slices * 3];
            AbilityVisualGeometry.FillFanTriangles(Slices, triangles);
            for (int t = 0; t < Slices; t++)
            {
                Assert.AreEqual(0, triangles[t * 3]);
                Vector3 a = AbilityVisualGeometry.FanVertex(triangles[t * 3], Range, Angle, Slices);
                Vector3 b = AbilityVisualGeometry.FanVertex(triangles[t * 3 + 1], Range, Angle, Slices);
                Vector3 c = AbilityVisualGeometry.FanVertex(triangles[t * 3 + 2], Range, Angle, Slices);
                Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f, $"triangle {t} faces up");
            }
        }

        [Test]
        public void CagePostsAreNeverFurtherApartThanTheSpacing()
        {
            // Electric Fence.prefab radius 6: 37.7 m around / 2.5 m = 15.08 -> 16 posts.
            Assert.AreEqual(16, AbilityVisualGeometry.CagePostCount(6f, 2.5f));
            Assert.AreEqual(3, AbilityVisualGeometry.CagePostCount(0.2f, 2.5f), "never fewer than 3");
            float gap = Vector3.Distance(AbilityVisualGeometry.CirclePoint(6f, 0f), AbilityVisualGeometry.CirclePoint(6f, 360f / 16));
            Assert.LessOrEqual(gap, 2.5f);
        }

        [Test]
        public void ASphereCutsAFlatPlaneInACircle()
        {
            Assert.AreEqual(3f, AbilityVisualGeometry.RadiusOnPlane(3f, 1f, 1f), 1e-5f);
            Assert.AreEqual(4f, AbilityVisualGeometry.RadiusOnPlane(5f, 3f, 0f), 1e-5f);
            Assert.AreEqual(4f, AbilityVisualGeometry.RadiusOnPlane(5f, 0f, 3f), 1e-5f, "above or below is the same");
            Assert.AreEqual(0f, AbilityVisualGeometry.RadiusOnPlane(3f, 5f, 1f), "a plane that misses the sphere");
        }

        [Test]
        public void ThePlayersRootSitsHalfAMetreAboveItsFeet()
        {
            // Multiplayer Player.prefab CapsuleCollider: centre y 0.8063041, height 2.6126082 (TestRangeSpawner.Grounded, measured 2026-09-13).
            Assert.AreEqual(0.5f, AbilityVisualGeometry.RootAboveFeet(0.8063041f, 2.6126082f, 1f), 1e-3f);
            Assert.AreEqual(1f, AbilityVisualGeometry.RootAboveFeet(0.8063041f, 2.6126082f, 2f), 2e-3f);
        }

        [Test]
        public void APullTakesDistanceOverSpeed()
        {
            Assert.AreEqual(0.6f, AbilityVisualGeometry.PullSeconds(15f, 25f), 1e-5f);
            Assert.AreEqual(0f, AbilityVisualGeometry.PullSeconds(0f, 25f));
            Assert.AreEqual(0f, AbilityVisualGeometry.PullSeconds(5f, 0f));
        }

        [Test]
        public void AFadeRunsFromOneToZero()
        {
            Assert.AreEqual(1f, AbilityVisualGeometry.Fade01(0f, 0.5f), 1e-5f);
            Assert.AreEqual(0.5f, AbilityVisualGeometry.Fade01(0.25f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, AbilityVisualGeometry.Fade01(0.7f, 0.5f));
            Assert.AreEqual(0f, AbilityVisualGeometry.Fade01(0f, 0f));
        }
    }
}
```

- [ ] **Step 2: Failing ground-snap tests.** Create `Assets/Tests/GroundSnapTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>GroundSnap against real colliders in a preview scene - never the open Game Scene, which must not get dirty.</summary>
    public class GroundSnapTests
    {
        /// <summary>Something with health standing on the floor: the probe must look past it.</summary>
        private class FakeBody : MonoBehaviour, IDamageable
        {
            public DamageResult ApplyDamage(in DamageInfo info) => default;
            public bool IsAlive => true;
            public int TeamId => 0;
            public int ActorNumber => 1;
            public bool HasLocalAuthority => true;
        }

        private const int Everything = ~0;
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject Box(string name, Vector3 centre, Vector3 size)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            return go;
        }

        private bool Probe(Vector3 from, out float groundY) =>
            GroundSnap.TryFindGroundY(scene.GetPhysicsScene(), from, GroundSnap.ProbeUp, GroundSnap.ProbeDown, Everything, out groundY);

        [Test]
        public void FindsTheTopOfTheFloorBelowAPoint()
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f)); // top at y = 0
            Assert.IsTrue(Probe(new Vector3(1f, 2f, 1f), out float y));
            Assert.AreEqual(0f, y, 1e-3f);
        }

        [Test]
        public void LooksPastABodyOnTheSpot()
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Box("Body", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f)).AddComponent<FakeBody>(); // 0..2 m tall
            Assert.IsTrue(Probe(new Vector3(0f, 2.1f, 0f), out float fromAbove), "a blast just above someone's head");
            Assert.AreEqual(0f, fromAbove, 1e-3f);
            Assert.IsTrue(Probe(new Vector3(0f, 0.5f, 0f), out float fromInside), "a mine placed at the caster's root, inside their body");
            Assert.AreEqual(0f, fromInside, 1e-3f);
        }

        [Test]
        public void AFloorFurtherDownThanTheProbeIsNotFound()
        {
            Box("Floor", new Vector3(0f, -10.5f, 0f), new Vector3(20f, 1f, 20f)); // top at y = -10
            Assert.IsFalse(Probe(new Vector3(0f, 0.5f, 0f), out float y));
            Assert.AreEqual(0.5f, y, 1e-5f, "falls back to the point's own height");
        }

        [Test]
        public void ARoofAboveThePointIsNotTakenForTheFloor()
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Box("Roof", new Vector3(0f, 3.5f, 0f), new Vector3(20f, 1f, 20f)); // underside at 3 m, above the 0.25 m start
            Assert.IsTrue(Probe(new Vector3(0f, 2f, 0f), out float y));
            Assert.AreEqual(0f, y, 1e-3f);
        }
    }
}
```

- [ ] **Step 3: Failing structure tests.** Create `Assets/Tests/AbilityVisualStructureTests.cs`. Steps 3–7 add tests to
  this class.

```csharp
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Ability visuals (Tudor, 2026-09-17): each ability's look is built from primitives on its prefab. These pin that
    /// structure - parts, materials, wiring - and that no part carries a collider (a collider would change gameplay).
    /// Read-only asset loads.
    /// </summary>
    public class AbilityVisualStructureTests
    {
        private const string SolidPath = "Assets/Gameplay/Abilities/Ability Visual Solid.mat";
        private const string GlassPath = "Assets/Gameplay/Abilities/Ability Visual Glass.mat";
        private const string LinePath = "Assets/Gameplay/UI/AimConeLine.mat";
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private const string MarkerPath = "Assets/Gameplay/Projectiles/Blast Marker.prefab";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static Transform Child(Transform parent, string path)
        {
            Transform child = parent.Find(path);
            Assert.IsNotNull(child, $"{parent.name}/{path}");
            return child;
        }

        /// <summary>meshName null = generated at runtime; only the renderer and material are checked.</summary>
        private static void AssertMesh(Transform part, string meshName, string materialPath)
        {
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            Assert.IsNotNull(filter, part.name + " MeshFilter");
            Assert.IsNotNull(renderer, part.name + " MeshRenderer");
            if (meshName != null)
            {
                Assert.IsNotNull(filter.sharedMesh, part.name + " mesh");
                Assert.AreEqual(meshName, filter.sharedMesh.name, part.name + " mesh");
            }
            Assert.AreEqual(materialPath, AssetDatabase.GetAssetPath(renderer.sharedMaterial), part.name + " material");
        }

        private static LineRenderer AssertLine(Transform part, bool flat)
        {
            var line = part.GetComponent<LineRenderer>();
            Assert.IsNotNull(line, part.name + " LineRenderer");
            Assert.AreEqual(LinePath, AssetDatabase.GetAssetPath(line.sharedMaterial), part.name + " material");
            Assert.AreEqual(flat ? LineAlignment.TransformZ : LineAlignment.View, line.alignment, part.name + " alignment");
            if (flat)
                Assert.Less(Quaternion.Angle(Quaternion.Euler(90f, 0f, 0f), part.localRotation), 0.01f, part.name + " lies flat");
            return line;
        }

        private static void AssertNoCollider(GameObject prefab) =>
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), prefab.name + " must have no collider");

        private static UnityEngine.Object Ref(Component component, string field)
        {
            SerializedProperty property = new SerializedObject(component).FindProperty(field);
            Assert.IsNotNull(property, $"{component.GetType().Name}.{field}");
            return property.objectReferenceValue;
        }

        [Test]
        public void TheVisualMaterialsAreUnlitOneSolidOneSeeThrough()
        {
            var solid = AssetDatabase.LoadAssetAtPath<Material>(SolidPath);
            var glass = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            Assert.IsNotNull(solid, SolidPath);
            Assert.IsNotNull(glass, GlassPath);
            Assert.AreEqual("Universal Render Pipeline/Unlit", solid.shader.name);
            Assert.AreEqual("Universal Render Pipeline/Unlit", glass.shader.name);
            Assert.AreEqual(0f, solid.GetFloat("_Surface"), "solid is opaque");
            Assert.AreEqual(1f, glass.GetFloat("_Surface"), "glass is transparent");
            Assert.AreEqual(0f, glass.GetFloat("_ZWrite"), "glass writes no depth, so players stay visible through it");
            Assert.AreEqual(0f, glass.GetFloat("_Cull"), "glass is double-sided");
            Assert.AreEqual(3000, glass.renderQueue);
            Assert.IsTrue(glass.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
        }

        [Test]
        public void TheBlastMarkerIsAFlatDiscAndRimWithNoCollider()
        {
            GameObject prefab = Load(MarkerPath);
            var marker = prefab.GetComponent<BlastMarker>();
            Assert.IsNotNull(marker);
            Transform disc = Child(prefab.transform, "Disc");
            AssertMesh(disc, "Cylinder", GlassPath);
            Assert.AreEqual(1f, disc.localScale.x, 1e-5f, "unit diameter - Spawn scales it to the blast");
            Assert.AreEqual(1f, disc.localScale.z, 1e-5f);
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            Assert.AreSame(disc, Ref(marker, "disc"));
            Assert.AreSame(rim, Ref(marker, "rim"));
            AssertNoCollider(prefab);
        }

        // ---- ability visuals steps 3-7 add their tests below this line ----
    }
}
```

- [ ] **Step 4: Recompile. Confirm the failing state.** Expected: compile errors naming only `AbilityVisualGeometry`,
  `GroundSnap` and `BlastMarker`. Report them.

- [ ] **Step 5: `AbilityVisualGeometry`.** Create `Assets/scripts/Combat/AbilityVisualGeometry.cs`:

```csharp
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The small maths behind the ability visuals (Tudor, 2026-09-17: "make the abilities a bit clearer"), kept pure so
    /// it is tested, and so every visual is drawn from the SAME number the gameplay reads - a mine's Trigger Radius, the
    /// flamethrower's Cone Angle - never a second copy typed into a visual.
    ///
    /// Angles are Unity yaw: degrees clockwise seen from above, 0 = +Z. Points are in the visual's own local space, flat
    /// on the floor (y = 0).
    /// </summary>
    public static class AbilityVisualGeometry
    {
        /// <summary>A point on a flat circle of this radius, yawDegrees clockwise from +Z.</summary>
        public static Vector3 CirclePoint(float radius, float yawDegrees)
        {
            float radians = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius);
        }

        /// <summary>Vertices in a flat cone ("fan") of arcSegments slices: the tip plus arcSegments + 1 arc points.</summary>
        public static int FanVertexCount(int arcSegments) => Mathf.Max(1, arcSegments) + 2;

        /// <summary>
        /// Fan vertex <paramref name="index"/>, opening along +Z: 0 is the tip (the caster), 1..arcSegments + 1 run along
        /// the arc from the left edge (-half angle) to the right edge (+half angle), all at <paramref name="range"/>. The
        /// same shape ConeFilter.IsWithinCone tests: range and half-angle measured flat from the tip.
        /// </summary>
        public static Vector3 FanVertex(int index, float range, float fullAngleDegrees, int arcSegments)
        {
            if (index <= 0)
                return Vector3.zero;

            int slices = Mathf.Max(1, arcSegments);
            float t = Mathf.Clamp01((index - 1) / (float)slices);
            return CirclePoint(range, Mathf.Lerp(-fullAngleDegrees * 0.5f, fullAngleDegrees * 0.5f, t));
        }

        /// <summary>Triangles (tip, i, i + 1) for every slice, clockwise seen from above so the fan faces up.
        /// <paramref name="triangles"/> must hold 3 * arcSegments entries.</summary>
        public static void FillFanTriangles(int arcSegments, int[] triangles)
        {
            int slices = Mathf.Max(1, arcSegments);
            for (int i = 0; i < slices; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        /// <summary>How many posts stand round a cage of this radius so no two neighbours are more than maxSpacing apart
        /// (at least 3).</summary>
        public static int CagePostCount(float radius, float maxSpacing)
        {
            if (radius <= 0f)
                return 3;
            return Mathf.Max(3, Mathf.CeilToInt(2f * Mathf.PI * radius / Mathf.Max(0.1f, maxSpacing)));
        }

        /// <summary>Radius of the circle where a sphere cuts a flat plane, 0 when the plane misses it. A rocket's splash is
        /// a sphere round the blast and its falloff is measured to a target's ROOT, so the circle at a standing player's
        /// root height is exactly where a standing player still takes splash.</summary>
        public static float RadiusOnPlane(float sphereRadius, float sphereCentreY, float planeY)
        {
            float dy = sphereCentreY - planeY;
            float squared = sphereRadius * sphereRadius - dy * dy;
            return squared > 0f ? Mathf.Sqrt(squared) : 0f;
        }

        /// <summary>How far a capsule's root sits above its feet - the same derivation TestRangeSpawner.Grounded uses
        /// (0.5 m for the player, measured 2026-09-13).</summary>
        public static float RootAboveFeet(float capsuleCentreY, float capsuleHeight, float scaleY) =>
            -(capsuleCentreY - capsuleHeight * 0.5f) * scaleY;

        /// <summary>Seconds a pull of this distance takes at this speed; 0 when either isn't positive.</summary>
        public static float PullSeconds(float distance, float speed) =>
            distance > 0f && speed > 0f ? distance / speed : 0f;

        /// <summary>1 at age 0, fading linearly to 0 at <paramref name="seconds"/>; 0 when seconds isn't positive.</summary>
        public static float Fade01(float ageSeconds, float seconds) =>
            seconds > 0f ? Mathf.Clamp01(1f - ageSeconds / seconds) : 0f;
    }
}
```

- [ ] **Step 6: The kit.** Create each file below.

`Assets/scripts/Abilities/Visuals/GroundSnap.cs`:

```csharp
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The floor under a point, for visuals that must lie on it (ability visuals step 2). Mines and the electric fence
    /// are placed at the caster's ROOT, 0.5 m above their feet, and a rocket blows up at muzzle height - so their flat
    /// visuals used to float. Visual only: nothing here moves a gameplay object or where a hit is measured from.
    ///
    /// Looks past anything with health (a player, a dummy, a cover wall), so the caster standing on the spot is never
    /// taken for the floor. Floor = Default (the terrain) or Building (floors, roofs): the same layers GroundProbe uses.
    /// </summary>
    public static class GroundSnap
    {
        // Not design tunables: the probe starts a curb's height above the point (never a roof) and looks down far enough
        // for a rocket blast about 2 m up.
        public const float ProbeUp = 0.25f;
        public const float ProbeDown = 4f;

        private const int MaxHits = 8;
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];
        private static int groundMask;

        private static int GroundMask
        {
            get
            {
                if (groundMask == 0)
                    groundMask = LayerMask.GetMask("Default", "Building");
                return groundMask;
            }
        }

        /// <summary>The floor height under <paramref name="from"/> in the game's own physics scene.</summary>
        public static bool TryFindGroundY(Vector3 from, out float groundY) =>
            TryFindGroundY(Physics.defaultPhysicsScene, from, ProbeUp, ProbeDown, GroundMask, out groundY);

        /// <summary>The nearest surface without health straight below <paramref name="from"/>, from probeUp above it to
        /// probeDown below it. False, with groundY = from.y, when there is none (a void, the map edge).</summary>
        public static bool TryFindGroundY(PhysicsScene physics, Vector3 from, float probeUp, float probeDown, int mask, out float groundY)
        {
            groundY = from.y;
            int count = physics.Raycast(from + Vector3.up * probeUp, Vector3.down, Hits, probeUp + probeDown, mask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Hits[i].collider;
                if (collider == null || collider.GetComponentInParent<IDamageable>() != null)
                    continue;
                if (Hits[i].distance < nearest)
                {
                    nearest = Hits[i].distance;
                    groundY = Hits[i].point.y;
                    found = true;
                }
            }
            return found;
        }
    }
}
```

`Assets/scripts/Abilities/Visuals/VisualTint.cs`:

```csharp
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>Colours and shapes the primitive parts of the ability visuals without touching their shared materials
    /// (ability visuals step 2): a MaterialPropertyBlock for meshes, start/end colour for lines. One material asset
    /// serves every team.</summary>
    public static class VisualTint
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public static void SetMeshColor(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            if (renderer == null || block == null)
                return;
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(block);
        }

        public static void SetLineColor(LineRenderer line, Color color)
        {
            if (line == null)
                return;
            line.startColor = color;
            line.endColor = color;
        }

        /// <summary>A closed circle lying flat. The line's own transform is turned 90 degrees on X (its Z faces up, and
        /// LineAlignment.TransformZ lays the band flat), which makes its local XY the floor.</summary>
        public static void FillFlatCircle(LineRenderer line, float radius, int segments)
        {
            if (line == null)
                return;
            int count = Mathf.Max(3, segments);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = AbilityVisualGeometry.CirclePoint(radius, i * 360f / count);
                line.SetPosition(i, new Vector3(p.x, p.z, 0f));
            }
        }

        /// <summary>A closed circle in the line's own local XZ plane, at the line's own height - a cage bar.</summary>
        public static void FillStandingCircle(LineRenderer line, float radius, int segments)
        {
            if (line == null)
                return;
            int count = Mathf.Max(3, segments);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
                line.SetPosition(i, AbilityVisualGeometry.CirclePoint(radius, i * 360f / count));
        }
    }
}
```

`Assets/scripts/Abilities/Visuals/StandingBody.cs`:

```csharp
using Overpower.Combat;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>How high a standing player's root is above the floor, read once from the local player's own
    /// CapsuleCollider - the derivation TestRangeSpawner.Grounded uses. Visual only: sizes the rocket's floor ring.</summary>
    public static class StandingBody
    {
        private static float cached = -1f;

        /// <summary>0 until a local player exists. A ring cut at the floor instead is a little smaller, never larger,
        /// than the real reach.</summary>
        public static float RootAboveFeet()
        {
            if (cached >= 0f)
                return cached;

            PhotonView view = PhotonNetwork.LocalPlayer != null ? PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) : null;
            CapsuleCollider capsule = view != null ? view.GetComponent<CapsuleCollider>() : null;
            if (capsule == null)
                return 0f;

            cached = Mathf.Max(0f, AbilityVisualGeometry.RootAboveFeet(capsule.center.y, capsule.height, capsule.transform.lossyScale.y));
            return cached;
        }
    }
}
```

`Assets/scripts/Abilities/Visuals/SnapVisualToGround.cs`:

```csharp
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Puts this visual on the floor the moment it spawns (ability visuals step 2) - for things placed at the caster's
    /// root (mine, electric fence) or at a rocket's airburst height (the cursor rocket's fire field), whose flat visuals
    /// otherwise float. Moves only this visual transform: the gameplay object, and every radius measured from it, stays
    /// exactly where the game put it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnapVisualToGround : MonoBehaviour
    {
        [SerializeField, Tooltip("How far above the floor this visual's origin sits, in metres - a hair, so flat parts " +
                 "don't flicker into the ground.")]
        private float heightAboveGround = 0.02f;

        private void Awake()
        {
            Vector3 from = transform.parent != null ? transform.parent.position : transform.position;
            if (GroundSnap.TryFindGroundY(from, out float groundY))
                transform.position = new Vector3(transform.position.x, groundY + heightAboveGround, transform.position.z);
        }
    }
}
```

`Assets/scripts/Abilities/Visuals/BlastMarker.cs`:

```csharp
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A flat ring on the floor that flashes where something blew up and fades away (ability visuals step 2): a rocket's
    /// splash, a mine's blast. Local and cosmetic - each client spawns its own from the blast it already simulates, no
    /// network traffic - and it never touches damage. The caller passes the radius from the gameplay component itself.
    /// </summary>
    public sealed class BlastMarker : MonoBehaviour
    {
        [SerializeField, Tooltip("The filled disc (a unit-diameter cylinder squashed flat); scaled sideways to the blast.")]
        private Transform disc;

        [SerializeField, Tooltip("The flat outline at the blast's edge.")]
        private LineRenderer rim;

        [SerializeField, Tooltip("Seconds the marker takes to fade out and remove itself. Long enough to read, short " +
                 "enough not to clutter a fight.")]
        private float fadeSeconds = 0.5f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the filled disc at the moment of the blast.")]
        private float discOpacity = 0.35f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the outline at the moment of the blast.")]
        private float rimOpacity = 0.9f;

        [SerializeField, Tooltip("Metres above the floor, so the flat parts don't flicker into the ground.")]
        private float heightAboveGround = 0.04f;

        [SerializeField, Tooltip("Points on the outline circle. 48 reads as round at the game camera's distance.")]
        private int rimSegments = 48;

        private Renderer discRenderer;
        private MaterialPropertyBlock block;
        private Color color = Color.white;
        private float age;

        /// <summary>The radius this marker was drawn at, in metres (read by the capture checks).</summary>
        public float Radius { get; private set; }

        /// <summary>Spawns a marker centred on <paramref name="groundPoint"/>, which is already on the floor. Nothing
        /// when there is no prefab or the radius isn't positive.</summary>
        public static BlastMarker Spawn(BlastMarker prefab, Vector3 groundPoint, float radius, Color color)
        {
            if (prefab == null || radius <= 0f)
                return null;
            BlastMarker marker = Instantiate(prefab, groundPoint, Quaternion.identity);
            marker.Show(radius, color);
            return marker;
        }

        private void Show(float radius, Color tint)
        {
            Radius = radius;
            color = tint;
            transform.position += Vector3.up * heightAboveGround;
            block = new MaterialPropertyBlock();
            if (disc != null)
            {
                disc.localScale = new Vector3(radius * 2f, disc.localScale.y, radius * 2f);
                discRenderer = disc.GetComponent<Renderer>();
            }
            VisualTint.FillFlatCircle(rim, radius, rimSegments);
            Apply(1f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float fade = AbilityVisualGeometry.Fade01(age, fadeSeconds);
            Apply(fade);
            if (fade <= 0f)
                Destroy(gameObject);
        }

        private void Apply(float fade)
        {
            VisualTint.SetMeshColor(discRenderer, block, VisualTint.WithAlpha(color, discOpacity * fade));
            VisualTint.SetLineColor(rim, VisualTint.WithAlpha(color, rimOpacity * fade));
        }
    }
}
```

`Assets/scripts/Abilities/Core/IDeployableView.cs`:

```csharp
namespace Overpower.Abilities
{
    /// <summary>
    /// A visual-only component on a NetworkedDeployable prefab (ability visuals step 2): the mine's, portal's and fence's
    /// looks. NetworkedDeployable calls it on EVERY client, a late joiner's replay included, right after OnPlaced - so
    /// OwnerActor, OwnerTeam and anything OnPlaced unpacks (a portal's diameter) are already set - and before an
    /// already-expired copy hides its renderers. It must only build and colour visuals.
    /// </summary>
    public interface IDeployableView
    {
        void OnDeployablePlaced(NetworkedDeployable deployable);
    }
}
```

- [ ] **Step 7: The hook in `NetworkedDeployable.cs`.** Two edits.
  1. Replace

```csharp
            OnPlaced(subclassData, info);
```

  with

```csharp
            OnPlaced(subclassData, info);

            // Ability visuals step 2: visual-only views build here - after OnPlaced, so a portal's diameter has
            // arrived, and before the IsExpired hide below, so an already-expired copy hides them too.
            NotifyViews();
```

  2. Insert this method directly above the `/// <summary>The IsExpired backstop's only visible effect` comment of
     `HideExpiredVisualAndColliders`:

```csharp
        /// <summary>Ability visuals step 2: hands the placed object to every visual-only IDeployableView on it. A broken
        /// visual must never stop what follows it here (the IsExpired hide, the owner's lifetime destroy), so each
        /// view's exception is logged and swallowed.</summary>
        private void NotifyViews()
        {
            foreach (IDeployableView view in GetComponentsInChildren<IDeployableView>(true))
            {
                try { view.OnDeployablePlaced(this); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }
        }

```

- [ ] **Step 8: Recompile, dirty check, run tests.** Expected:
  - compiles clean;
  - `AbilityVisualGeometryTests` 10/10 and `GroundSnapTests` 4/4 pass;
  - the two `AbilityVisualStructureTests` fail (the assets don't exist yet);
  - everything else passes.

  If a `GroundSnapTests` case finds nothing although its floor box exists, the preview scene has no usable physics
  scene. Report it, then leave `GroundSnapTests.cs` and its `.meta` out of this commit and delete them. The captures in
  steps 3–6 still prove the snap. Don't change `GroundSnap`.

- [ ] **Step 9: Write the kit script** `SCRATCH\MakeVisualKit.cs`. Its `PrefabParts` class is reused verbatim by the
  prefab scripts in steps 3–7, each of which copies it to the end of its own file:

```csharp
using System;
using System.Collections.Generic;
using Overpower.Abilities;
using Overpower.UI;
using Overpower.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// Ability visuals step 2 (scratch, not committed): the two materials and the Blast Marker prefab.
public static class MakeVisualKit
{
    public static string Run()
    {
        if (SceneManager.GetActiveScene().isDirty) return "ABORT: scene dirty before the edit";
        if (AssetDatabase.LoadAssetAtPath<Material>(PrefabParts.SolidPath) != null ||
            AssetDatabase.LoadAssetAtPath<Material>(PrefabParts.GlassPath) != null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabParts.MarkerPath) != null)
            return "ABORT: a kit asset already exists - report it";

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null) return "ABORT: URP Unlit shader not found";

        var solid = new Material(unlit) { name = "Ability Visual Solid" };
        solid.SetColor("_BaseColor", Color.white);
        solid.SetFloat("_Surface", 0f);
        AssetDatabase.CreateAsset(solid, PrefabParts.SolidPath);

        var glass = new Material(unlit) { name = "Ability Visual Glass" };
        glass.SetColor("_BaseColor", Color.white);
        glass.SetFloat("_Surface", 1f);
        glass.SetFloat("_Blend", 0f);
        glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        glass.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        glass.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        glass.SetFloat("_ZWrite", 0f);
        glass.SetFloat("_Cull", (float)CullMode.Off);
        glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glass.SetOverrideTag("RenderType", "Transparent");
        glass.renderQueue = (int)RenderQueue.Transparent;
        glass.SetShaderPassEnabled("DepthOnly", false);
        glass.SetShaderPassEnabled("ShadowCaster", false);
        AssetDatabase.CreateAsset(glass, PrefabParts.GlassPath);
        AssetDatabase.SaveAssetIfDirty(solid);
        AssetDatabase.SaveAssetIfDirty(glass);

        Material line = PrefabParts.Load<Material>(PrefabParts.LinePath);
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject root = PrefabParts.Empty(scene, null, "Blast Marker", typeof(BlastMarker));
            MeshRenderer disc = PrefabParts.MeshPart(scene, root.transform, "Disc", "Cylinder.fbx", glass,
                Vector3.zero, Vector3.zero, new Vector3(1f, 0.005f, 1f));
            LineRenderer rim = PrefabParts.LinePart(scene, root.transform, "Rim", line, 0.12f, flat: true, height: 0.01f);
            var marker = root.GetComponent<BlastMarker>();
            PrefabParts.Set(marker, "disc", disc.transform);
            PrefabParts.Set(marker, "rim", rim);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabParts.MarkerPath, out bool saved);
            if (!saved) return "ERROR: Blast Marker save failed";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        return $"materials + Blast Marker saved; scene dirty={SceneManager.GetActiveScene().isDirty}";
    }
}

/// Shared by every ability-visuals prefab script (scratch, not committed). Parts are created straight into the given
/// preview scene with ObjectFactory, so nothing ever lands in the open Game Scene and no primitive collider comes along.
public static class PrefabParts
{
    public const string SolidPath = "Assets/Gameplay/Abilities/Ability Visual Solid.mat";
    public const string GlassPath = "Assets/Gameplay/Abilities/Ability Visual Glass.mat";
    public const string LinePath = "Assets/Gameplay/UI/AimConeLine.mat";
    public const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
    public const string MarkerPath = "Assets/Gameplay/Projectiles/Blast Marker.prefab";

    public static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new Exception("missing asset " + path);
        return asset;
    }

    public static GameObject Empty(Scene scene, Transform parent, string name, params Type[] components)
    {
        GameObject go = ObjectFactory.CreateGameObject(scene, HideFlags.None, name, components);
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    public static MeshRenderer MeshPart(Scene scene, Transform parent, string name, string builtinMesh, Material material,
                                        Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
    {
        GameObject go = Empty(scene, parent, name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = localScale;
        go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(builtinMesh);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    /// flat = lies on the floor (turned 90 degrees on X, TransformZ alignment); otherwise faces the camera (a cage bar).
    public static LineRenderer LinePart(Scene scene, Transform parent, string name, Material material, float width, bool flat, float height)
    {
        GameObject go = Empty(scene, parent, name, typeof(LineRenderer));
        go.transform.localPosition = new Vector3(0f, height, 0f);
        go.transform.localRotation = flat ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        var line = go.GetComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 0;
        line.widthMultiplier = 1f;
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.alignment = flat ? LineAlignment.TransformZ : LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.allowOcclusionWhenDynamic = false;
        return line;
    }

    public static void Set(Component target, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null) throw new Exception($"{target.GetType().Name}.{field} not found");
        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetArray(Component target, string field, UnityEngine.Object[] values)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null) throw new Exception($"{target.GetType().Name}.{field} not found");
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
```

- [ ] **Step 10: Run it.**
  `unity command --timeout 240 run_script -- --file "SCRATCH\MakeVisualKit.cs" --entry MakeVisualKit.Run --timeout_ms 200000`
  - Expected: `materials + Blast Marker saved; scene dirty=False`.
  - Dirty check: `False`.
  - `git status`: besides your code, the only new assets are the two `.mat`, the prefab and their `.meta`.

- [ ] **Step 11: Recompile (no-op), run tests.** Expected: all pass, the structure tests included.

- [ ] **Step 12: Play Mode check of the kit.** It checks that the materials render and the ring lies on the floor.
  1. Run **Play Mode join**.
  2. Write `SCRATCH\s2_marker_check.cs` = the **capture prologue** followed by:

```csharp
Vector3 right = me.transform.right; right.y = 0f; right.Normalize();
Vector3 at = me.transform.position + right * 3f;
float gy = GroundUnder(at);
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Projectiles/Blast Marker.prefab").GetComponent<Overpower.Abilities.BlastMarker>();
var marker = Overpower.Abilities.BlastMarker.Spawn(prefab, new Vector3(at.x, gy, at.z), 3f, theme.ShotColorFor(myTeam));
facts.AppendLine($"root.y-ground.y={me.transform.position.y - GroundUnder(me.transform.position):0.000} StandingBody.RootAboveFeet={Overpower.Abilities.StandingBody.RootAboveFeet():0.000}");
facts.AppendLine($"marker at {marker.transform.position:F3} ground.y={gy:0.000} radius={marker.Radius:0.00} team={myTeam} colour={theme.ShotColorFor(myTeam)}");
Freeze("s2-blast-marker");
return facts.ToString();
```

  3. Run it with `eval_file`, then follow the **Capture procedure** for `s2-blast-marker`.
  4. Expected facts:
     - both root heights are 0.500 ± 0.01;
     - marker y = ground + 0.04.
  5. Expected picture: a flat, see-through disc in your team's colour, about 6 m across, with a brighter outline, on
     the floor beside the player. The floor shows through it, and the player isn't hidden.
  6. Stop Play Mode; dirty check: `False`.

- [ ] **Step 13: Commit and push** (only these files):

```
git add Assets/scripts/Combat/AbilityVisualGeometry.cs Assets/scripts/Combat/AbilityVisualGeometry.cs.meta Assets/scripts/Abilities/Visuals.meta Assets/scripts/Abilities/Visuals/GroundSnap.cs Assets/scripts/Abilities/Visuals/GroundSnap.cs.meta Assets/scripts/Abilities/Visuals/VisualTint.cs Assets/scripts/Abilities/Visuals/VisualTint.cs.meta Assets/scripts/Abilities/Visuals/StandingBody.cs Assets/scripts/Abilities/Visuals/StandingBody.cs.meta Assets/scripts/Abilities/Visuals/SnapVisualToGround.cs Assets/scripts/Abilities/Visuals/SnapVisualToGround.cs.meta Assets/scripts/Abilities/Visuals/BlastMarker.cs Assets/scripts/Abilities/Visuals/BlastMarker.cs.meta Assets/scripts/Abilities/Core/IDeployableView.cs Assets/scripts/Abilities/Core/IDeployableView.cs.meta Assets/scripts/Abilities/Core/NetworkedDeployable.cs
git add "Assets/Gameplay/Abilities/Ability Visual Solid.mat" "Assets/Gameplay/Abilities/Ability Visual Solid.mat.meta" "Assets/Gameplay/Abilities/Ability Visual Glass.mat" "Assets/Gameplay/Abilities/Ability Visual Glass.mat.meta" "Assets/Gameplay/Projectiles/Blast Marker.prefab" "Assets/Gameplay/Projectiles/Blast Marker.prefab.meta"
git add Assets/Tests/AbilityVisualGeometryTests.cs Assets/Tests/AbilityVisualGeometryTests.cs.meta Assets/Tests/GroundSnapTests.cs Assets/Tests/GroundSnapTests.cs.meta Assets/Tests/AbilityVisualStructureTests.cs Assets/Tests/AbilityVisualStructureTests.cs.meta
git status
git commit -m "feat(abilities): shared visual kit - floor snap, blast ring, unlit materials, deployable view hook (ability visuals step 2)" -m "Co-Authored-By: <your model line>"
git push
```

---

### Task 3 (ability visuals step 3): mines and portals that can't be confused

**What exists:**
- Both prefabs have a `Visual` child: the built-in Cylinder with the URP default Lit material
  (`Mine.prefab:31,43,67`, `Portal.prefab:31,43,67`).
- `Mine.visual` is hidden with `SetActive(false)` on every client inside `RPC_Detonate` (`Mine.cs:198,214-218`).
- `Portal.OnPlaced` scales `visual` X/Z to the diameter (`Portal.cs:77-78`).
- `Portal.Radius` exists; `Mine`'s radii are private (`Mine.cs:66,72`).

**Files:**
- Create: `Assets/scripts/Abilities/Visuals/MineView.cs`, `Assets/scripts/Abilities/Visuals/PortalView.cs`
- Modify: `Assets/scripts/Abilities/Equipment/Mine.cs` (two read-only getters)
- Modify: `Assets/Resources/Mine.prefab`, `Assets/Resources/Portal.prefab`
- Modify: `Assets/Tests/AbilityVisualStructureTests.cs`
- Scratch: `SCRATCH\BuildMineAndPortal.cs`, `SCRATCH\s3_own_view.cs`, `SCRATCH\s3_mine_blast.cs`

- [ ] **Step 1: Failing structure tests.** Add inside `AbilityVisualStructureTests`, below the steps 3-7 marker line:

```csharp
        [Test]
        public void TheMineIsADarkSpikedPuckWithATeamStudAndNoCollider()
        {
            GameObject prefab = Load("Assets/Resources/Mine.prefab");
            Transform visual = Child(prefab.transform, "Visual");
            Assert.AreSame(visual, Ref(prefab.GetComponent<Mine>(), "visual"), "Mine hides this the instant it detonates");
            Assert.AreEqual(Vector3.zero, visual.localPosition);
            Assert.AreEqual(Vector3.one, visual.localScale);
            Assert.IsNotNull(visual.GetComponent<SnapVisualToGround>());
            AssertMesh(Child(visual, "Body"), "Cylinder", SolidPath);
            AssertMesh(Child(visual, "Stud"), "Cube", SolidPath);
            for (int i = 1; i <= 4; i++)
                AssertMesh(Child(visual, "Spike " + i), "Cube", SolidPath);
            LineRenderer ring = AssertLine(Child(visual, "Trigger Ring"), flat: true);

            var view = prefab.GetComponent<MineView>();
            Assert.IsNotNull(view);
            var so = new SerializedObject(view);
            Assert.AreEqual(5, so.FindProperty("bodyParts").arraySize, "puck + 4 spikes");
            Assert.AreEqual(1, so.FindProperty("teamParts").arraySize, "the stud");
            Assert.AreSame(ring, Ref(view, "triggerRing"));
            Assert.AreSame(visual, Ref(view, "visualRoot"));
            Assert.AreEqual(MarkerPath, AssetDatabase.GetAssetPath(Ref(view, "blastMarkerPrefab")));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PhotonView>(true).Length);
        }

        [Test]
        public void ThePortalIsALightRimmedDiscWithAnOwnerOnlyBeaconAndNoCollider()
        {
            GameObject prefab = Load("Assets/Resources/Portal.prefab");
            Assert.IsNull(prefab.transform.Find("Visual"), "the old grey cylinder is gone");
            Transform footprint = Child(prefab.transform, "Footprint");
            AssertMesh(footprint, "Cylinder", GlassPath);
            Assert.AreEqual(1f, footprint.localScale.x, 1e-5f, "unit diameter - Portal scales it to Portal Diameter");
            Assert.AreSame(footprint, Ref(prefab.GetComponent<Portal>(), "visual"));
            LineRenderer rim = AssertLine(Child(prefab.transform, "Rim"), flat: true);
            Transform beacon = Child(prefab.transform, "Owner Beacon");
            AssertMesh(Child(beacon, "Diamond"), "Cube", SolidPath);
            AssertMesh(Child(beacon, "Stem"), "Cylinder", GlassPath);

            var view = prefab.GetComponent<PortalView>();
            Assert.IsNotNull(view);
            Assert.AreSame(footprint.GetComponent<MeshRenderer>(), Ref(view, "footprint"));
            Assert.AreSame(rim, Ref(view, "rim"));
            Assert.AreSame(beacon.gameObject, Ref(view, "ownerBeacon"));
            Assert.AreSame(Child(beacon, "Diamond").GetComponent<MeshRenderer>(), Ref(view, "beacon"));
            Assert.AreSame(Child(beacon, "Stem").GetComponent<MeshRenderer>(), Ref(view, "stem"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PhotonView>(true).Length);
        }
```

- [ ] **Step 2: Getters on `Mine.cs`.** Replace

```csharp
        public int Seq { get; private set; }
```

with

```csharp
        public int Seq { get; private set; }

        /// <summary>Trigger Radius, read-only - MineView (ability visuals step 3) draws the owner team's trigger ring
        /// from this one number.</summary>
        public float TriggerRadius => triggerRadius;

        /// <summary>Explosion Radius, read-only - MineView flashes the blast ring at this size.</summary>
        public float ExplosionRadius => explosionRadius;
```

- [ ] **Step 3: `MineView`.** Create `Assets/scripts/Abilities/Visuals/MineView.cs`:

```csharp
using Overpower.Net;
using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a mine looks like (ability visuals step 3; Tudor: mines and portals looked the same). A small dark puck with
    /// four stubby spikes and a stud in the owner's team colour, lying on the floor: squat, dark and small, where a
    /// portal is wide, light and see-through. The owner's own team also sees a thin ring at the real Trigger Radius;
    /// enemies don't (they only ever saw the mine itself, and still only do). When the mine goes off, every client
    /// flashes a BlastMarker at the real Explosion Radius.
    ///
    /// Visual only. Every size comes from Mine itself, and the detonation is noticed without touching the detonation
    /// code: Mine hides its own Visual on every client the instant it goes off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MineView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For), the same " +
                 "colours as each team's shots.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The dark parts: the puck and its spikes.")]
        private Renderer[] bodyParts;

        [SerializeField, Tooltip("Colour of the dark parts. Dark so a mine never reads as a portal.")]
        private Color bodyColor = new Color(0.1f, 0.1f, 0.12f, 1f);

        [SerializeField, Tooltip("The parts drawn in the owner's team colour: the stud on top.")]
        private Renderer[] teamParts;

        [SerializeField, Tooltip("Flat ring at the mine's Trigger Radius, shown only to the owner's own team.")]
        private LineRenderer triggerRing;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the trigger ring.")]
        private float triggerRingOpacity = 0.5f;

        [SerializeField, Tooltip("Points on the trigger ring.")]
        private int triggerRingSegments = 48;

        [SerializeField, Tooltip("The flash on the floor when the mine goes off - Assets/Gameplay/Projectiles/Blast Marker.prefab.")]
        private BlastMarker blastMarkerPrefab;

        [SerializeField, Tooltip("The mine's Visual: the container Snap Visual To Ground puts on the floor, and the one " +
                 "Mine hides when it detonates.")]
        private Transform visualRoot;

        private Mine mine;
        private Color teamColor = Color.white;
        private bool blastShown;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            mine = deployable as Mine;
            if (mine == null)
                return;

            teamColor = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            var block = new MaterialPropertyBlock();
            foreach (Renderer part in bodyParts)
                VisualTint.SetMeshColor(part, block, bodyColor);
            foreach (Renderer part in teamParts)
                VisualTint.SetMeshColor(part, block, teamColor);

            if (triggerRing != null)
            {
                VisualTint.FillFlatCircle(triggerRing, mine.TriggerRadius, triggerRingSegments);
                VisualTint.SetLineColor(triggerRing, VisualTint.WithAlpha(teamColor, triggerRingOpacity));
                triggerRing.enabled = IsLocalPlayersTeam(deployable.OwnerTeam);
            }
        }

        private static bool IsLocalPlayersTeam(int team) =>
            team >= 0 && PhotonNetwork.LocalPlayer != null &&
            Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int localTeam) && localTeam == team;

        private void LateUpdate()
        {
            // Mine.RPC_Detonate hides the Visual on every client the instant it goes off. A lifetime expiry or a prune
            // destroys the mine instead, and an expired late-join copy only switches its renderers off, so neither of
            // those flashes a blast.
            if (mine == null || blastShown || visualRoot == null || visualRoot.gameObject.activeSelf)
                return;

            blastShown = true;
            Vector3 at = mine.transform.position;
            BlastMarker.Spawn(blastMarkerPrefab, new Vector3(at.x, visualRoot.position.y, at.z), mine.ExplosionRadius, teamColor);
        }
    }
}
```

- [ ] **Step 4: `PortalView`.** Create `Assets/scripts/Abilities/Visuals/PortalView.cs`:

```csharp
using Overpower.UI;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a portal looks like (ability visuals step 3). A see-through disc at the real diameter with a bright rim, in
    /// the owner's team colour: wide and light, where a mine is small and dark. Only the player who placed it also sees
    /// a floating diamond on a stem - "this one is yours to use" - because nobody else, teammates included, can use it
    /// (Portal's class comment). Enemies still see the disc and rim, as they always saw the portal.
    ///
    /// Visual only: the rim is drawn from Portal.Radius, the same number TeleportAbility's channel check uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For).")]
        private UiTheme theme;

        [SerializeField, Tooltip("The see-through disc. It is Portal's own Visual, so Portal scales it to Portal Diameter.")]
        private Renderer footprint;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the disc. Low, so whoever stands in it stays visible.")]
        private float footprintOpacity = 0.3f;

        [SerializeField, Tooltip("The bright outline at the portal's real edge.")]
        private LineRenderer rim;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the outline.")]
        private float rimOpacity = 0.95f;

        [SerializeField, Tooltip("Points on the outline circle.")]
        private int rimSegments = 48;

        [SerializeField, Tooltip("Shown only on the screen of the player who placed this portal.")]
        private GameObject ownerBeacon;

        [SerializeField, Tooltip("The floating diamond inside the owner beacon.")]
        private Renderer beacon;

        [SerializeField, Tooltip("The thin stem under the diamond.")]
        private Renderer stem;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the stem.")]
        private float stemOpacity = 0.6f;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            var portal = deployable as Portal;
            if (portal == null)
                return;

            Color team = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            var block = new MaterialPropertyBlock();
            VisualTint.SetMeshColor(footprint, block, VisualTint.WithAlpha(team, footprintOpacity));
            VisualTint.SetMeshColor(beacon, block, VisualTint.WithAlpha(team, 1f));
            VisualTint.SetMeshColor(stem, block, VisualTint.WithAlpha(team, stemOpacity));
            VisualTint.FillFlatCircle(rim, portal.Radius, rimSegments);
            VisualTint.SetLineColor(rim, VisualTint.WithAlpha(team, rimOpacity));

            if (ownerBeacon != null)
                ownerBeacon.SetActive(PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == deployable.OwnerActor);
        }
    }
}
```

- [ ] **Step 5: Recompile, dirty check, run tests.** Expected: compiles clean; the two new structure tests fail (the
  prefabs aren't rebuilt yet); every guard test and everything else passes.

- [ ] **Step 6: Build the prefabs.** Write `SCRATCH\BuildMineAndPortal.cs` with the class below, followed by the
  `PrefabParts` class from Task 2 Step 9, and the same `using` lines as that file:

```csharp
/// Ability visuals step 3 (scratch, not committed): rebuilds the Visual of Mine.prefab and Portal.prefab from primitives.
public static class BuildMineAndPortal
{
    private const string MinePath = "Assets/Resources/Mine.prefab";
    private const string PortalPath = "Assets/Resources/Portal.prefab";

    public static string Run()
    {
        if (SceneManager.GetActiveScene().isDirty) return "ABORT: scene dirty before the edit";
        Material solid = PrefabParts.Load<Material>(PrefabParts.SolidPath);
        Material glass = PrefabParts.Load<Material>(PrefabParts.GlassPath);
        Material line = PrefabParts.Load<Material>(PrefabParts.LinePath);
        UiTheme theme = PrefabParts.Load<UiTheme>(PrefabParts.ThemePath);
        BlastMarker marker = PrefabParts.Load<GameObject>(PrefabParts.MarkerPath).GetComponent<BlastMarker>();

        // ---- Mine ----
        GameObject mineRoot = PrefabUtility.LoadPrefabContents(MinePath);
        try
        {
            Scene scene = mineRoot.scene;
            Transform old = mineRoot.transform.Find("Visual");
            if (old == null) return "ABORT: Mine.prefab has no Visual child";
            UnityEngine.Object.DestroyImmediate(old.gameObject);

            GameObject visual = PrefabParts.Empty(scene, mineRoot.transform, "Visual", typeof(SnapVisualToGround));
            MeshRenderer body = PrefabParts.MeshPart(scene, visual.transform, "Body", "Cylinder.fbx", solid,
                new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.8f, 0.06f, 0.8f));
            MeshRenderer stud = PrefabParts.MeshPart(scene, visual.transform, "Stud", "Cube.fbx", solid,
                new Vector3(0f, 0.17f, 0f), Vector3.zero, new Vector3(0.22f, 0.1f, 0.22f));
            var bodyParts = new List<UnityEngine.Object> { body };
            for (int i = 0; i < 4; i++)
            {
                float yaw = i * 90f;
                Vector3 at = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.06f, 0.45f);
                bodyParts.Add(PrefabParts.MeshPart(scene, visual.transform, "Spike " + (i + 1), "Cube.fbx", solid,
                    at, new Vector3(0f, yaw, 0f), new Vector3(0.12f, 0.08f, 0.24f)));
            }
            LineRenderer ring = PrefabParts.LinePart(scene, visual.transform, "Trigger Ring", line, 0.06f, flat: true, height: 0.03f);

            MineView view = mineRoot.AddComponent<MineView>();
            PrefabParts.Set(view, "theme", theme);
            PrefabParts.SetArray(view, "bodyParts", bodyParts.ToArray());
            PrefabParts.SetArray(view, "teamParts", new UnityEngine.Object[] { stud });
            PrefabParts.Set(view, "triggerRing", ring);
            PrefabParts.Set(view, "blastMarkerPrefab", marker);
            PrefabParts.Set(view, "visualRoot", visual.transform);
            PrefabParts.Set(mineRoot.GetComponent<Mine>(), "visual", visual.transform);

            PrefabUtility.SaveAsPrefabAsset(mineRoot, MinePath, out bool saved);
            if (!saved) return "ERROR: Mine.prefab save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(mineRoot); }

        // ---- Portal ----
        GameObject portalRoot = PrefabUtility.LoadPrefabContents(PortalPath);
        try
        {
            Scene scene = portalRoot.scene;
            Transform old = portalRoot.transform.Find("Visual");
            if (old == null) return "ABORT: Portal.prefab has no Visual child";
            UnityEngine.Object.DestroyImmediate(old.gameObject);

            MeshRenderer footprint = PrefabParts.MeshPart(scene, portalRoot.transform, "Footprint", "Cylinder.fbx", glass,
                new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(1f, 0.01f, 1f));
            LineRenderer rim = PrefabParts.LinePart(scene, portalRoot.transform, "Rim", line, 0.15f, flat: true, height: 0.05f);
            GameObject beacon = PrefabParts.Empty(scene, portalRoot.transform, "Owner Beacon");
            MeshRenderer diamond = PrefabParts.MeshPart(scene, beacon.transform, "Diamond", "Cube.fbx", solid,
                new Vector3(0f, 1.4f, 0f), new Vector3(45f, 0f, 45f), new Vector3(0.45f, 0.45f, 0.45f));
            MeshRenderer stem = PrefabParts.MeshPart(scene, beacon.transform, "Stem", "Cylinder.fbx", glass,
                new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(0.06f, 0.6f, 0.06f));

            PortalView view = portalRoot.AddComponent<PortalView>();
            PrefabParts.Set(view, "theme", theme);
            PrefabParts.Set(view, "footprint", footprint);
            PrefabParts.Set(view, "rim", rim);
            PrefabParts.Set(view, "ownerBeacon", beacon);
            PrefabParts.Set(view, "beacon", diamond);
            PrefabParts.Set(view, "stem", stem);
            PrefabParts.Set(portalRoot.GetComponent<Portal>(), "visual", footprint.transform);

            PrefabUtility.SaveAsPrefabAsset(portalRoot, PortalPath, out bool saved);
            if (!saved) return "ERROR: Portal.prefab save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(portalRoot); }

        return $"Mine + Portal rebuilt; scene dirty={SceneManager.GetActiveScene().isDirty}";
    }
}
```

Run: `unity command --timeout 240 run_script -- --file "SCRATCH\BuildMineAndPortal.cs" --entry BuildMineAndPortal.Run --timeout_ms 200000`.
Expected: `Mine + Portal rebuilt; scene dirty=False`.

- [ ] **Step 7: Read back.**
  - Dirty check: `False`.
  - Recompile (no-op), then run tests: **all** pass. The guards are unchanged; that is the proof that Mine's and
    Portal's numbers didn't move.
  - `git diff --stat -- Assets/Resources/Mine.prefab Assets/Resources/Portal.prefab`.
  - `git diff -- Assets/Resources/Mine.prefab | Select-String "damage|Radius|Delay|lifetime|slow"`: no changed value
    lines.

- [ ] **Step 8: Play Mode captures.** Run **Play Mode join**.

  **(a) Your own mine and portals.** `SCRATCH\s3_own_view.cs` = the **capture prologue** followed by:

```csharp
System.Collections.IEnumerator Run()
{
    Vector3 open = FreePoint(dummy.transform.position, 12f);
    loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 19);
    loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 17);
    disp.TeleportTo(open);
    yield return new WaitForSeconds(1f);
    Cast(typeof(Overpower.Abilities.MineAbility));                       // the mine at my feet
    Vector3 away = open - dummy.transform.position; away.y = 0f; away.Normalize();
    Vector3 across = Vector3.Cross(Vector3.up, away);
    disp.TeleportTo(open + away * 2f);                                   // step back so the camera frames all three
    yield return new WaitForSeconds(0.5f);
    aim.SetAimOverride(open + across * 4f); yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.TeleportAbility));
    yield return new WaitForSeconds(0.5f);
    aim.SetAimOverride(open - across * 4f); yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.TeleportAbility));
    yield return new WaitForSeconds(0.5f);
    aim.SetAimOverride(open);
    yield return new WaitForSeconds(1f);

    foreach (var mine in Overpower.Abilities.Mine.ForOwner(myActor))
    {
        Transform visual = mine.transform.Find("Visual");
        var ring = visual.Find("Trigger Ring").GetComponent<LineRenderer>();
        facts.AppendLine($"mine team={mine.OwnerTeam} root.y={mine.transform.position.y:0.000} visual.y={visual.position.y:0.000} ground.y={GroundUnder(mine.transform.position):0.000}");
        facts.AppendLine($"  stud={MeshColor(visual.Find("Stud").GetComponent<Renderer>())} theme={theme.ShotColorFor(mine.OwnerTeam)} body={MeshColor(visual.Find("Body").GetComponent<Renderer>())}");
        facts.AppendLine($"  ringEnabled={ring.enabled} ringRadius={Flat(ring.transform.TransformPoint(ring.GetPosition(0)), mine.transform.position):0.000} triggerRadius={mine.TriggerRadius} ringColour={ring.startColor}");
    }
    foreach (var portal in Overpower.Abilities.Portal.ForOwner(myActor))
    {
        var rim = portal.transform.Find("Rim").GetComponent<LineRenderer>();
        Transform footprint = portal.transform.Find("Footprint");
        facts.AppendLine($"portal team={portal.OwnerTeam} radius={portal.Radius} rimRadius={Flat(rim.transform.TransformPoint(rim.GetPosition(0)), portal.transform.position):0.000} footprintScaleX={footprint.localScale.x:0.000}");
        facts.AppendLine($"  footprint={MeshColor(footprint.GetComponent<Renderer>())} beaconActive={portal.transform.Find("Owner Beacon").gameObject.activeSelf}");
    }
    Freeze("s3-own-mine-portal");
}
BuildingManager.Instance.StartCoroutine(Run());
return "running";
```

  Run it with `eval_file`, then follow the **Capture procedure** for `s3-own-mine-portal`.
  - **Expected facts:**
    - mine `visual.y − ground.y` ≈ 0.02 (it floated 0.5 before, step 1);
    - stud = theme colour;
    - body ≈ (0.10, 0.10, 0.12);
    - `ringEnabled=True`, `ringRadius` = 1.800;
    - each portal has `rimRadius` = 1.250, `footprintScaleX` = 2.500, footprint = theme colour at alpha 0.3, and
      `beaconActive=True`.
  - **Expected picture:** a small dark spiked puck with a coloured stud and a thin coloured ring round it, clearly
    unlike two wide light rimmed discs, each with a floating diamond. Compare with `s1-before-mine-portal.png` and say
    what changed.

  **(b) A mine going off.** `SCRATCH\s3_mine_blast.cs` = the **capture prologue** followed by:

```csharp
System.Collections.IEnumerator Run()
{
    loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 19);
    dummy.ResetToFull();
    disp.TeleportTo(FreePoint(dummy.transform.position, 1.6f));    // inside the 1.8 m trigger
    yield return new WaitForSeconds(1f);
    aim.SetAimOverride(dummy.transform.position);
    var before = new System.Collections.Generic.HashSet<Overpower.Abilities.BlastMarker>(UnityEngine.Object.FindObjectsByType<Overpower.Abilities.BlastMarker>(FindObjectsSortMode.None));
    Cast(typeof(Overpower.Abilities.MineAbility));
    yield return null;
    Vector3 minePos = me.transform.position;
    Overpower.Abilities.BlastMarker marker = null;
    float giveUp = Time.realtimeSinceStartup + 6f;
    while (marker == null && Time.realtimeSinceStartup < giveUp)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<Overpower.Abilities.BlastMarker>(FindObjectsSortMode.None))
            if (!before.Contains(m)) marker = m;
        if (marker == null) yield return null;
    }
    if (marker == null) { facts.AppendLine("NO BLAST MARKER within 6 s"); Freeze("s3-mine-blast"); yield break; }
    facts.AppendLine($"marker radius={marker.Radius:0.000} (Explosion Radius 2.2) centreToMine(flat)={Flat(marker.transform.position, minePos):0.000} marker.y-ground={marker.transform.position.y - GroundUnder(marker.transform.position):0.000} colour team={myTeam}");
    Freeze("s3-mine-blast");
}
BuildingManager.Instance.StartCoroutine(Run());
return "running";
```

  Run it, then follow the **Capture procedure** for `s3-mine-blast`.
  - **Expected facts:** radius 2.200, centre-to-mine ≤ 0.05, marker height ≈ 0.06.
  - **Expected picture:** a flat, see-through team-coloured disc with a rim, about 4.4 m across, on the floor round the
    dummy. No puck left.

  Stop Play Mode; dirty check: `False`.

- [ ] **Step 9: Commit and push** (only these files):

```
git add Assets/scripts/Abilities/Visuals/MineView.cs Assets/scripts/Abilities/Visuals/MineView.cs.meta Assets/scripts/Abilities/Visuals/PortalView.cs Assets/scripts/Abilities/Visuals/PortalView.cs.meta Assets/scripts/Abilities/Equipment/Mine.cs Assets/Resources/Mine.prefab Assets/Resources/Portal.prefab Assets/Tests/AbilityVisualStructureTests.cs
git status
git commit -m "feat(abilities): mines are dark spiked pucks, portals light rimmed gates, both in team colour (ability visuals step 3)" -m "Co-Authored-By: <your model line>"
git push
```

Log under the assumptions heading:
- `[C] Mine look: dark puck + team stud; trigger ring for own team only; blast ring at Explosion Radius`
- `[C] Portal look: see-through disc + rim in team colour; diamond beacon on the placer's screen only; no pair line`

---

### Task 4 (ability visuals step 4): the Electric Fence becomes a cage

**What exists:**
- `Electric Fence.prefab`'s `Visual` is a flat Cylinder, scaled to 12 m by `ElectricFence.OnPlaced`
  (`ElectricFence.cs:122-123`), at the caster's root height.
- Radius and Ring Thickness are private (`:39,44`).
- The fence blocks nothing: hits come from `FenceCrossingState` on root distance (`FenceCrossingState.cs:57-59`).

**Files:**
- Create: `Assets/scripts/Abilities/Visuals/FenceCageView.cs`
- Modify: `Assets/scripts/Abilities/Ultimate/ElectricFence.cs` (two read-only getters)
- Modify: `Assets/Resources/Electric Fence.prefab`, `Assets/Tests/AbilityVisualStructureTests.cs`
- Scratch: `SCRATCH\BuildFenceCage.cs`, `SCRATCH\s4_cage_tpl.cs`

- [ ] **Step 1: Failing structure test.** Add inside `AbilityVisualStructureTests`:

```csharp
        [Test]
        public void TheElectricFenceIsACageOfPostsAndBarsWithNoCollider()
        {
            GameObject prefab = Load("Assets/Resources/Electric Fence.prefab");
            Assert.IsNull(Ref(prefab.GetComponent<ElectricFence>(), "visual"), "nothing is stretched to the radius any more");
            Assert.IsNull(prefab.transform.Find("Visual"), "the old flat disc is gone");
            Transform cage = Child(prefab.transform, "Cage");
            Assert.IsNotNull(cage.GetComponent<SnapVisualToGround>());
            Transform post = Child(cage, "Post");
            AssertMesh(post, "Cube", GlassPath);

            var view = prefab.GetComponent<FenceCageView>();
            Assert.IsNotNull(view);
            SerializedProperty bars = new SerializedObject(view).FindProperty("bars");
            string[] names = { "Bar Low", "Bar Mid", "Bar Top" };
            Assert.AreEqual(names.Length, bars.arraySize);
            float below = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                LineRenderer bar = AssertLine(Child(cage, names[i]), flat: false);
                Assert.Greater(bar.transform.localPosition.y, below, names[i] + " is above the bar below it");
                below = bar.transform.localPosition.y;
                Assert.AreSame(bar, bars.GetArrayElementAtIndex(i).objectReferenceValue);
            }
            Assert.LessOrEqual(below, post.localPosition.y * 2f, "the top bar is no higher than the posts");
            LineRenderer band = AssertLine(Child(cage, "Band"), flat: true);
            Assert.AreSame(post, Ref(view, "post"));
            Assert.AreSame(band, Ref(view, "band"));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
            Assert.AreEqual(2, prefab.GetComponentsInChildren<PhotonView>(true).Length, "unchanged - flagged for Tudor, not fixed here");
        }
```

- [ ] **Step 2: Getters on `ElectricFence.cs`.** Replace

```csharp
        public int AbilityId { get; private set; } = -1;
```

with

```csharp
        public int AbilityId { get; private set; } = -1;

        /// <summary>Radius, read-only - FenceCageView (ability visuals step 4) builds the cage on this one number.</summary>
        public float Radius => radius;

        /// <summary>Ring Thickness, read-only - FenceCageView draws the floor band exactly this wide.</summary>
        public float RingThickness => ringThickness;
```

- [ ] **Step 3: `FenceCageView`.** Create `Assets/scripts/Abilities/Visuals/FenceCageView.cs`:

```csharp
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Electric Fence as a cage (ability visuals step 4; Tudor: "walls instead of zones that have horizontal bars").
    /// Thin posts stand round the fence's real Radius with see-through horizontal bar circles between them, plus a faint
    /// band on the floor exactly as wide as Ring Thickness - where a hit really lands (an enemy whose centre is in the
    /// band, or crosses the ring). Owner's team colour.
    ///
    /// VISUAL ONLY, NO COLLIDERS. The fence damages and slows on crossing; it doesn't block. Posts or bars with colliders
    /// would stop players and shots, which is a gameplay change for Tudor to decide.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FenceCageView : MonoBehaviour, IDeployableView
    {
        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For).")]
        private UiTheme theme;

        [SerializeField, Tooltip("One post, already on the prefab; copied round the ring. Its height and thickness are " +
                 "authored on its own transform (its centre height is half its height).")]
        private Transform post;

        [SerializeField, Tooltip("The horizontal bars: one circle each, at each bar's own height on the prefab.")]
        private LineRenderer[] bars;

        [SerializeField, Tooltip("A faint flat band on the floor, as wide as the fence's Ring Thickness.")]
        private LineRenderer band;

        [SerializeField, Tooltip("Largest gap between two neighbouring posts, in metres. Smaller reads more like a cage and " +
                 "costs more posts.")]
        private float maxPostSpacing = 2.5f;

        [SerializeField, Tooltip("Points per bar and band circle.")]
        private int circleSegments = 64;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the posts. See-through, so players inside stay visible.")]
        private float postOpacity = 0.85f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the bars.")]
        private float barOpacity = 0.85f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the floor band.")]
        private float bandOpacity = 0.2f;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            var fence = deployable as ElectricFence;
            if (fence == null || post == null)
                return;

            Color team = theme != null ? theme.ShotColorFor(deployable.OwnerTeam) : Color.white;
            var block = new MaterialPropertyBlock();

            int count = AbilityVisualGeometry.CagePostCount(fence.Radius, maxPostSpacing);
            float postCentreY = post.localPosition.y;
            for (int i = 0; i < count; i++)
            {
                Transform p = i == 0 ? post : Instantiate(post, post.parent);
                float yaw = i * 360f / count;
                Vector3 onRing = AbilityVisualGeometry.CirclePoint(fence.Radius, yaw);
                p.localPosition = new Vector3(onRing.x, postCentreY, onRing.z);
                p.localRotation = Quaternion.Euler(0f, yaw, 0f);
                VisualTint.SetMeshColor(p.GetComponent<Renderer>(), block, VisualTint.WithAlpha(team, postOpacity));
            }

            foreach (LineRenderer bar in bars)
            {
                VisualTint.FillStandingCircle(bar, fence.Radius, circleSegments);
                VisualTint.SetLineColor(bar, VisualTint.WithAlpha(team, barOpacity));
            }

            if (band != null)
            {
                VisualTint.FillFlatCircle(band, fence.Radius, circleSegments);
                band.startWidth = fence.RingThickness;
                band.endWidth = fence.RingThickness;
                VisualTint.SetLineColor(band, VisualTint.WithAlpha(team, bandOpacity));
            }
        }
    }
}
```

- [ ] **Step 4: Recompile, dirty check, run tests.** Expected: the new structure test fails; everything else passes.

- [ ] **Step 5: Build the prefab.** Write `SCRATCH\BuildFenceCage.cs` with the class below, followed by `PrefabParts`
  (Task 2 Step 9), using the same `using` lines:

```csharp
/// Ability visuals step 4 (scratch, not committed): Electric Fence.prefab's flat disc becomes a cage.
public static class BuildFenceCage
{
    private const string FencePath = "Assets/Resources/Electric Fence.prefab";

    public static string Run()
    {
        if (SceneManager.GetActiveScene().isDirty) return "ABORT: scene dirty before the edit";
        Material glass = PrefabParts.Load<Material>(PrefabParts.GlassPath);
        Material line = PrefabParts.Load<Material>(PrefabParts.LinePath);
        UiTheme theme = PrefabParts.Load<UiTheme>(PrefabParts.ThemePath);

        GameObject root = PrefabUtility.LoadPrefabContents(FencePath);
        try
        {
            Scene scene = root.scene;
            Transform old = root.transform.Find("Visual");
            if (old == null) return "ABORT: Electric Fence.prefab has no Visual child";
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            PrefabParts.Set(root.GetComponent<ElectricFence>(), "visual", null);

            GameObject cage = PrefabParts.Empty(scene, root.transform, "Cage", typeof(SnapVisualToGround));
            MeshRenderer post = PrefabParts.MeshPart(scene, cage.transform, "Post", "Cube.fbx", glass,
                new Vector3(0f, 0.8f, 0f), Vector3.zero, new Vector3(0.12f, 1.6f, 0.12f));
            LineRenderer low = PrefabParts.LinePart(scene, cage.transform, "Bar Low", line, 0.08f, flat: false, height: 0.5f);
            LineRenderer mid = PrefabParts.LinePart(scene, cage.transform, "Bar Mid", line, 0.08f, flat: false, height: 1.0f);
            LineRenderer top = PrefabParts.LinePart(scene, cage.transform, "Bar Top", line, 0.08f, flat: false, height: 1.5f);
            LineRenderer band = PrefabParts.LinePart(scene, cage.transform, "Band", line, 1f, flat: true, height: 0.03f);

            FenceCageView view = root.AddComponent<FenceCageView>();
            PrefabParts.Set(view, "theme", theme);
            PrefabParts.Set(view, "post", post.transform);
            PrefabParts.SetArray(view, "bars", new UnityEngine.Object[] { low, mid, top });
            PrefabParts.Set(view, "band", band);

            PrefabUtility.SaveAsPrefabAsset(root, FencePath, out bool saved);
            if (!saved) return "ERROR: Electric Fence.prefab save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        return $"Electric Fence rebuilt; scene dirty={SceneManager.GetActiveScene().isDirty}";
    }
}
```

Run: `unity command --timeout 240 run_script -- --file "SCRATCH\BuildFenceCage.cs" --entry BuildFenceCage.Run --timeout_ms 200000`.
Expected: `Electric Fence rebuilt; scene dirty=False`.

- [ ] **Step 6: Read back.** Dirty check: `False`. Recompile (no-op), then run tests: **all** pass, including
  `ElectricFenceNumbersAreUnchanged`.

- [ ] **Step 7: Play Mode captures at two zooms.** Run **Play Mode join**. `SCRATCH\s4_cage_tpl.cs` = the
  **capture prologue** followed by (`__ZOOM__` = `1` for the camera as played, then `1.6` to see the whole ring):

```csharp
var cam = UnityEngine.Object.FindFirstObjectByType<CameraTracking>();
typeof(CameraTracking).GetField("currentZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(cam, __ZOOM__f);
System.Collections.IEnumerator Run()
{
    loadout.SetAbility(Overpower.Data.AbilitySlot.Ultimate, 26);
    disp.TeleportTo(FreePoint(dummy.transform.position, 4f));      // the dummy ends up inside the cage, 2 m in from the ring
    yield return new WaitForSeconds(1f);
    aim.SetAimOverride(dummy.transform.position);
    me.GetComponent<UltimateCharge>().Fill();
    Cast(typeof(Overpower.Abilities.ElectricFenceAbility));
    yield return new WaitForSeconds(1f);
    foreach (var fence in UnityEngine.Object.FindObjectsByType<Overpower.Abilities.ElectricFence>(FindObjectsSortMode.None))
    {
        if (fence.OwnerActor != myActor) continue;
        Transform cage = fence.transform.Find("Cage");
        int posts = 0; float minR = float.MaxValue, maxR = 0f; Renderer firstPost = null;
        foreach (Transform child in cage)
        {
            if (!child.name.StartsWith("Post")) continue;
            posts++;
            float r = Flat(child.position, fence.transform.position);
            minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
            if (firstPost == null) firstPost = child.GetComponent<Renderer>();
        }
        facts.AppendLine($"fence radius={fence.Radius} thickness={fence.RingThickness} root.y={fence.transform.position.y:0.000} cage.y={cage.position.y:0.000} ground.y={GroundUnder(fence.transform.position):0.000}");
        facts.AppendLine($"posts={posts} postRadius min={minR:0.000} max={maxR:0.000} postColour={MeshColor(firstPost)} theme={theme.ShotColorFor(fence.OwnerTeam)}");
        foreach (string barName in new[] { "Bar Low", "Bar Mid", "Bar Top" })
        {
            var bar = cage.Find(barName).GetComponent<LineRenderer>();
            Vector3 p0 = bar.transform.TransformPoint(bar.GetPosition(0));
            facts.AppendLine($"{barName} points={bar.positionCount} radius={Flat(p0, fence.transform.position):0.000} aboveCage={p0.y - cage.position.y:0.000} colour={bar.startColor}");
        }
        var band = cage.Find("Band").GetComponent<LineRenderer>();
        Vector3 b0 = band.transform.TransformPoint(band.GetPosition(0));
        facts.AppendLine($"band width={band.startWidth:0.000} radius={Flat(b0, fence.transform.position):0.000} aboveGround={b0.y - GroundUnder(fence.transform.position):0.000} colour={band.startColor}");
        facts.AppendLine($"collidersUnderFence={fence.GetComponentsInChildren<Collider>(true).Length}");
    }
    Freeze("s4-cage-zoom__ZOOM__");
}
BuildingManager.Instance.StartCoroutine(Run());
return "running";
```

  For each zoom: fill the template, run it, and follow the **Capture procedure** for `s4-cage-zoom1` and
  `s4-cage-zoom1.6`. Wait 9 s between the two runs so the first fence has expired. After the second capture, reset the
  zoom:
  `unity command eval -- --code "typeof(CameraTracking).GetField(\"currentZoom\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(UnityEngine.Object.FindFirstObjectByType<CameraTracking>(), 1f); return 1;"`

  - **Expected facts:**
    - `cage.y − ground.y` ≈ 0.02 (it floated 0.5 before);
    - `posts=16`, post radius 6.000 min and max;
    - post colour = theme;
    - three bars of 64 points at radius 6.000, 0.5/1.0/1.5 above the cage;
    - band width 1.000 at radius 6.000;
    - `collidersUnderFence=0`.
  - **Expected pictures:** a ring of thin see-through posts joined by three horizontal bars in team colour, reading as
    a cage round the player, with a faint band on the floor at its foot. Players inside stay visible. At zoom 1 the
    cage runs partly off screen (12 m across); say whether it still reads as a cage. At 1.6 the whole ring shows.
    Compare with `s1-before-fence-flame.png`.

  Stop Play Mode; dirty check: `False`.

- [ ] **Step 8: Commit and push** (only these files):

```
git add Assets/scripts/Abilities/Visuals/FenceCageView.cs Assets/scripts/Abilities/Visuals/FenceCageView.cs.meta Assets/scripts/Abilities/Ultimate/ElectricFence.cs "Assets/Resources/Electric Fence.prefab" Assets/Tests/AbilityVisualStructureTests.cs
git status
git commit -m "feat(abilities): the electric fence is drawn as a cage of posts and bars, no colliders (ability visuals step 4)" -m "Co-Authored-By: <your model line>"
git push
```

Log under the assumptions heading:
- `[C] Fence cage: 16 posts, bars at 0.5/1.0/1.5 m, floor band = Ring Thickness; no colliders`
- In **Questions for you**: `The fence now looks like a cage but blocks nothing [bars have no colliders; blocking would be a gameplay change]`

---

### Task 5 (ability visuals step 5): the flamethrower draws its real cone

**What exists:**
- `FlamethrowerAbility.BuildVfx` makes a `CreatePrimitive(Cylinder)` with a `new Material` per module
  (`FlamethrowerAbility.cs:321-335`).
- `PositionVfx` stretches it to 5.8 m wide along the whole 7 m, centred at root height (`:301-313`).
- The hit check is a flat sector from the root (`:218-229,249-250`, `ConeFilter.cs:59-78`).
- The Cone Range tooltip wrongly says "muzzle" (`:85-87`).

**Files:**
- Create: `Assets/scripts/Abilities/Visuals/FlameConeVisual.cs`
- Modify: `Assets/scripts/Abilities/Equipment/FlamethrowerAbility.cs` (VFX code and two tooltips; no gameplay line)
- Create (script): `Assets/Gameplay/Abilities/Flamethrower Cone.prefab`
- Modify: `Assets/Gameplay/Abilities/Flamethrower.prefab`, `Assets/Tests/AbilityVisualStructureTests.cs`
- Scratch: `SCRATCH\BuildFlameCone.cs`, `SCRATCH\s5_flame.cs`

- [ ] **Step 1: Failing structure test.** Add inside `AbilityVisualStructureTests`:

```csharp
        [Test]
        public void TheFlamethrowerDrawsAFlatFanPrefabWithNoCollider()
        {
            GameObject module = Load("Assets/Gameplay/Abilities/Flamethrower.prefab");
            var cone = Ref(module.GetComponent<FlamethrowerAbility>(), "sprayVfxPrefab") as FlameConeVisual;
            Assert.IsNotNull(cone, "Flamethrower › Spray Vfx Prefab");
            Assert.AreEqual("Assets/Gameplay/Abilities/Flamethrower Cone.prefab", AssetDatabase.GetAssetPath(cone));
            Transform fan = Child(cone.transform, "Fan");
            AssertMesh(fan, null, GlassPath); // the fan mesh is generated from Cone Range/Angle at runtime
            LineRenderer edge = AssertLine(Child(cone.transform, "Edge"), flat: true);
            Assert.AreSame(fan.GetComponent<MeshFilter>(), Ref(cone, "fan"));
            Assert.AreSame(edge, Ref(cone, "edge"));
            AssertNoCollider(cone.gameObject);
            AssertNoCollider(module); // AbilityDefinition forbids colliders on a module prefab
        }
```

- [ ] **Step 2: `FlameConeVisual`.** Create `Assets/scripts/Abilities/Visuals/FlameConeVisual.cs`:

```csharp
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The flamethrower's cone (ability visuals step 5; Tudor: "just a cylinder instead of a cone that starts from the
    /// player"). A flat fan on the floor with its tip under the caster, opening to the ability's own Cone Range and Cone
    /// Angle - exactly the shape ConeFilter.IsWithinCone tests (flat, measured from the caster's root) - plus a brighter
    /// outline. Flat, because the hit test ignores height.
    ///
    /// Visual only. The mesh is built once and rebuilt only when the range or angle changes; Place runs every physics
    /// step while spraying and allocates nothing.
    /// </summary>
    public sealed class FlameConeVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("The filled fan: a MeshFilter whose mesh is generated here, drawn see-through.")]
        private MeshFilter fan;

        [SerializeField, Tooltip("The outline: a flat line child tracing tip, arc and back.")]
        private LineRenderer edge;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the filled fan. Low, so players inside it stay visible.")]
        private float fanOpacity = 0.35f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the outline.")]
        private float edgeOpacity = 0.9f;

        [SerializeField, Tooltip("Straight pieces the arc is made of. 24 keeps the drawn edge within 1 cm of the real " +
                 "arc at 7 m.")]
        private int arcSegments = 24;

        [SerializeField, Tooltip("Metres above the floor, so the fan doesn't flicker into the ground.")]
        private float heightAboveGround = 0.05f;

        private Mesh mesh;
        private Vector3[] vertices;
        private int[] triangles;
        private float builtRange = -1f;
        private float builtAngle = -1f;
        private MaterialPropertyBlock block;

        /// <summary>Sizes the cone to the ability's real numbers and colours it. Cheap when nothing changed.</summary>
        public void Configure(float range, float fullAngleDegrees, Color color)
        {
            if (range != builtRange || fullAngleDegrees != builtAngle)
                Rebuild(range, fullAngleDegrees);

            if (block == null)
                block = new MaterialPropertyBlock();
            VisualTint.SetMeshColor(fan != null ? fan.GetComponent<Renderer>() : null, block, VisualTint.WithAlpha(color, fanOpacity));
            VisualTint.SetLineColor(edge, VisualTint.WithAlpha(color, edgeOpacity));
        }

        /// <summary>Tip on the floor under <paramref name="apex"/>, opening along <paramref name="forward"/>'s flat direction.</summary>
        public void Place(Vector3 apex, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return;

            float floor = GroundSnap.TryFindGroundY(apex, out float groundY) ? groundY : apex.y;
            transform.SetPositionAndRotation(new Vector3(apex.x, floor + heightAboveGround, apex.z), Quaternion.LookRotation(forward));
        }

        private void Rebuild(float range, float fullAngleDegrees)
        {
            int slices = Mathf.Max(1, arcSegments);
            int count = AbilityVisualGeometry.FanVertexCount(slices);
            if (vertices == null || vertices.Length != count)
            {
                vertices = new Vector3[count];
                triangles = new int[slices * 3];
            }

            for (int i = 0; i < count; i++)
                vertices[i] = AbilityVisualGeometry.FanVertex(i, range, fullAngleDegrees, slices);
            AbilityVisualGeometry.FillFanTriangles(slices, triangles);

            if (mesh == null)
            {
                mesh = new Mesh { name = "Flame Cone (generated)" };
                if (fan != null)
                    fan.sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (edge != null)
            {
                edge.useWorldSpace = false;
                edge.loop = true;
                edge.positionCount = count;
                for (int i = 0; i < count; i++)
                    edge.SetPosition(i, new Vector3(vertices[i].x, vertices[i].z, 0f)); // the flat child's local XY is the floor
            }

            builtRange = range;
            builtAngle = fullAngleDegrees;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
```

- [ ] **Step 3: `FlamethrowerAbility.cs` edits** (visual code and text only).
  1. The Cone Range tooltip. Replace

```csharp
        [SerializeField, Tooltip("How far the spray reaches, in metres, measured on the ground plane " +
                 "from the caster's own muzzle. Controller's call.")]
```

  with

```csharp
        [SerializeField, Tooltip("How far the spray reaches, in metres, measured on the ground plane " +
                 "from the caster's own position (their root, not the muzzle - see TickCone). Controller's call.")]
```

  2. The VFX fields. Replace

```csharp
        [Header("VFX (cheap, cosmetic only)")]
        [SerializeField, Tooltip("Colour of the placeholder cone mesh shown on every client for the " +
                 "duration of Spray Seconds. This is a stretched primitive, not a real particle " +
                 "system - cheap enough to run for several simultaneous sprays without a hitch. Swap " +
                 "for real flame art whenever that becomes this project's priority; nothing about the " +
                 "burn itself depends on it.")]
        private Color vfxColor = new Color(1f, 0.45f, 0.1f, 1f);
```

  with

```csharp
        [Header("VFX (cheap, cosmetic only)")]
        [SerializeField, Tooltip("Colour of the flame cone drawn on the floor on every client for the " +
                 "duration of Spray Seconds (its opacity is on the cone prefab). Nothing about the burn " +
                 "itself depends on it.")]
        private Color vfxColor = new Color(1f, 0.45f, 0.1f, 1f);

        [SerializeField, Tooltip("The flat flame cone - Assets/Gameplay/Abilities/Flamethrower Cone.prefab. " +
                 "Drawn from Cone Angle and Cone Range above, with its tip under the caster, so it always " +
                 "shows exactly the area the burn checks (ability visuals step 5). Visual only.")]
        private FlameConeVisual sprayVfxPrefab;
```

  3. Replace `        private Transform vfx;` with `        private FlameConeVisual vfx;`.
  4. In `OnDestroy`'s comment, replace `(see BuildVfx)` with `(see ShowVfx)`.
  5. Replace everything from `        private void ShowVfx()` to the end of `BuildVfx` (its closing brace, just before
     the class's closing brace) with:

```csharp
        private void ShowVfx()
        {
            if (vfx == null)
            {
                if (sprayVfxPrefab == null)
                    return; // Nothing to draw; the burn works the same without it.

                // Unparented, as before: the module sits under the player, whose hierarchy moves to the
                // DeadPlayer layer on death. OnDestroy removes it.
                vfx = Instantiate(sprayVfxPrefab);
                vfx.name = "Flamethrower Cone VFX (cheap, cosmetic only)";
            }

            // Re-read on every spray, so an Inspector change to Cone Angle/Range shows on the next cast.
            vfx.Configure(coneRange, coneAngle, vfxColor);
            vfx.gameObject.SetActive(true);
        }

        private void PositionVfx(Vector3 apex, Vector3 forward)
        {
            if (vfx != null)
                vfx.Place(apex, forward);
        }

        private void HideVfx()
        {
            if (vfx != null)
                vfx.gameObject.SetActive(false);
        }
```

  Then check with `git diff -- Assets/scripts/Abilities/Equipment/FlamethrowerAbility.cs` that no line of `TickCone`,
  `SprayRoutine`, `IsOccludedByWall`, `ExecuteCast` or the burn/cone fields changed.

- [ ] **Step 4: Recompile, dirty check, run tests.** Expected: the new structure test fails; everything else passes.

- [ ] **Step 5: Build the prefabs.** Write `SCRATCH\BuildFlameCone.cs` with the class below, followed by `PrefabParts`
  (Task 2 Step 9), using the same `using` lines:

```csharp
/// Ability visuals step 5 (scratch, not committed): Flamethrower Cone.prefab, wired into Flamethrower.prefab.
public static class BuildFlameCone
{
    private const string ConePath = "Assets/Gameplay/Abilities/Flamethrower Cone.prefab";
    private const string ModulePath = "Assets/Gameplay/Abilities/Flamethrower.prefab";

    public static string Run()
    {
        if (SceneManager.GetActiveScene().isDirty) return "ABORT: scene dirty before the edit";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ConePath) != null) return "ABORT: the cone prefab already exists";
        Material glass = PrefabParts.Load<Material>(PrefabParts.GlassPath);
        Material line = PrefabParts.Load<Material>(PrefabParts.LinePath);

        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject root = PrefabParts.Empty(scene, null, "Flamethrower Cone", typeof(FlameConeVisual));
            GameObject fan = PrefabParts.Empty(scene, root.transform, "Fan", typeof(MeshFilter), typeof(MeshRenderer));
            var fanRenderer = fan.GetComponent<MeshRenderer>();
            fanRenderer.sharedMaterial = glass;
            fanRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fanRenderer.receiveShadows = false;
            fanRenderer.lightProbeUsage = LightProbeUsage.Off;
            fanRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            LineRenderer edge = PrefabParts.LinePart(scene, root.transform, "Edge", line, 0.06f, flat: true, height: 0.01f);

            var cone = root.GetComponent<FlameConeVisual>();
            PrefabParts.Set(cone, "fan", fan.GetComponent<MeshFilter>());
            PrefabParts.Set(cone, "edge", edge);
            PrefabUtility.SaveAsPrefabAsset(root, ConePath, out bool saved);
            if (!saved) return "ERROR: cone prefab save failed";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        FlameConeVisual conePrefab = PrefabParts.Load<GameObject>(ConePath).GetComponent<FlameConeVisual>();
        GameObject module = PrefabUtility.LoadPrefabContents(ModulePath);
        try
        {
            PrefabParts.Set(module.GetComponent<FlamethrowerAbility>(), "sprayVfxPrefab", conePrefab);
            PrefabUtility.SaveAsPrefabAsset(module, ModulePath, out bool saved);
            if (!saved) return "ERROR: Flamethrower.prefab save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(module); }

        return $"Flamethrower Cone saved and wired; scene dirty={SceneManager.GetActiveScene().isDirty}";
    }
}
```

Run: `unity command --timeout 240 run_script -- --file "SCRATCH\BuildFlameCone.cs" --entry BuildFlameCone.Run --timeout_ms 200000`.
Expected: `Flamethrower Cone saved and wired; scene dirty=False`.

- [ ] **Step 6: Read back.** Dirty check: `False`. Recompile (no-op), then run tests: **all** pass, including
  `FlamethrowerNumbersAreUnchanged` and `AbilityDefinitionTests`. Run
  `git diff -- "Assets/Gameplay/Abilities/Flamethrower.prefab"`: only the added `sprayVfxPrefab` line.

- [ ] **Step 7: Play Mode capture.** Run **Play Mode join**. `SCRATCH\s5_flame.cs` = the **capture prologue** followed by:

```csharp
System.Collections.IEnumerator Run()
{
    loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 21);
    disp.TeleportTo(FreePoint(dummy.transform.position, 4f));
    yield return new WaitForSeconds(1f);
    aim.SetAimOverride(dummy.transform.position);
    yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.FlamethrowerAbility));
    yield return new WaitForSeconds(0.4f);                                  // mid-spray (1 s)
    var cone = UnityEngine.Object.FindFirstObjectByType<Overpower.Abilities.FlameConeVisual>();
    if (cone == null) { facts.AppendLine("NO ACTIVE CONE"); Freeze("s5-flame"); yield break; }
    var edge = cone.transform.Find("Edge").GetComponent<LineRenderer>();
    Vector3 tip = edge.transform.TransformPoint(edge.GetPosition(0));
    Vector3 left = edge.transform.TransformPoint(edge.GetPosition(1)) - tip; left.y = 0f;
    Vector3 right = edge.transform.TransformPoint(edge.GetPosition(edge.positionCount - 1)) - tip; right.y = 0f;
    Vector3 facing = me.transform.forward; facing.y = 0f;
    facts.AppendLine($"tipToRoot(flat)={Flat(tip, me.transform.position):0.000} cone.y-ground={cone.transform.position.y - GroundUnder(me.transform.position):0.000} points={edge.positionCount}");
    facts.AppendLine($"left len={left.magnitude:0.000} angle={Vector3.SignedAngle(facing, left, Vector3.up):0.00}; right len={right.magnitude:0.000} angle={Vector3.SignedAngle(facing, right, Vector3.up):0.00}");
    facts.AppendLine($"dummyInRealCone={Overpower.Combat.ConeFilter.IsWithinCone(me.transform.position, me.transform.forward, dummy.transform.position, 7f, 45f)} fanColour={MeshColor(cone.transform.Find("Fan").GetComponent<Renderer>())} coneCount={UnityEngine.Object.FindObjectsByType<Overpower.Abilities.FlameConeVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");
    Freeze("s5-flame");
}
BuildingManager.Instance.StartCoroutine(Run());
return "running";
```

  Run it, then follow the **Capture procedure** for `s5-flame`.
  - **Expected facts:**
    - tip-to-root ≤ 0.01, cone above ground 0.05, 26 points;
    - both edges 7.000 m long at −22.50 and +22.50 degrees;
    - `dummyInRealCone=True`;
    - fan colour (1, 0.45, 0.1, 0.35);
    - `coneCount=1` (one cone per module, reused).
  - **Expected picture:** a see-through orange wedge on the floor with its point under the player, opening toward and
    past the dummy, with a brighter outline. No tube, nothing above the player's head. Compare with
    `s1-before-fence-flame.png`.

  Stop Play Mode; dirty check: `False`.

- [ ] **Step 8: Commit and push** (only these files):

```
git add Assets/scripts/Abilities/Visuals/FlameConeVisual.cs Assets/scripts/Abilities/Visuals/FlameConeVisual.cs.meta Assets/scripts/Abilities/Equipment/FlamethrowerAbility.cs "Assets/Gameplay/Abilities/Flamethrower Cone.prefab" "Assets/Gameplay/Abilities/Flamethrower Cone.prefab.meta" "Assets/Gameplay/Abilities/Flamethrower.prefab" Assets/Tests/AbilityVisualStructureTests.cs
git status
git commit -m "feat(abilities): the flamethrower draws its real flat cone from the caster (ability visuals step 5)" -m "Co-Authored-By: <your model line>"
git push
```

Log under the assumptions heading:
- `[C] Flame cone: flat fan from the root at the real 7 m / 45°`
- `[C] Cone Range tooltip now says root (code unchanged)`
- In **Questions for you**: `Measure flamethrower range from the muzzle instead of the root? [no - it would add about 1.4 m of reach]`

---

### Task 6 (ability visuals step 6): rocket blasts drawn on the floor

**What exists:**
- `ExplodeOnImpact.Detonate` runs once per rocket on every client, at `hit.point` (hit) or `transform.position`
  (airburst) (`ExplodeOnImpact.cs:118,132,155-204`).
- Rockets fly flat at muzzle height.
- `ProjectileMotor.Despawn` plays the bullet puff only on a real hit (`ProjectileMotor.cs:259-273`).
- The cursor rocket's `FireField` spawns at the rocket (`DetonateAtCursor.cs:142-143`); its `Visual` child is scaled
  sideways only (`FireField.cs:169-170`).

**Files:**
- Create: `Assets/scripts/Weapons/Effects/RocketBlastView.cs`
- Modify: `Assets/scripts/Weapons/Effects/ExplodeOnImpact.cs` (a read-only getter and an event raised after the splash)
- Modify: `Assets/Gameplay/Projectiles/Rocket.prefab`, `Rocket Distance.prefab`, `Rocket Cursor.prefab`,
  `Assets/Resources/Fire Field.prefab`, `Assets/Tests/AbilityVisualStructureTests.cs`
- Scratch: `SCRATCH\BuildRocketBlastViews.cs`, `SCRATCH\s6_rocket_tpl.cs`

- [ ] **Step 1: Failing structure tests.** Add inside `AbilityVisualStructureTests`:

```csharp
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab")]
        public void EveryRocketDropsItsBlastRingOnTheFloor(string path)
        {
            GameObject prefab = Load(path);
            var view = prefab.GetComponent<RocketBlastView>();
            Assert.IsNotNull(view, path);
            Assert.AreEqual(MarkerPath, AssetDatabase.GetAssetPath(Ref(view, "blastMarkerPrefab")));
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(view, "theme")));
            AssertNoCollider(prefab);
        }

        [Test]
        public void TheCursorRocketsFireFieldDiscSitsOnTheFloor()
        {
            GameObject prefab = Load("Assets/Resources/Fire Field.prefab");
            Transform visual = Child(prefab.transform, "Visual");
            Assert.IsNotNull(visual.GetComponent<SnapVisualToGround>());
            Assert.AreSame(visual, Ref(prefab.GetComponent<FireField>(), "visual"));
            AssertNoCollider(prefab);
        }
```

- [ ] **Step 2: `ExplodeOnImpact.cs`.** Two additive edits.
  1. Replace

```csharp
        private bool detonated;
```

  with

```csharp
        private bool detonated;

        /// <summary>Splash Radius, read-only - RocketBlastView (ability visuals step 6) draws the floor ring from this
        /// one number.</summary>
        public float SplashRadius => splashRadius;

        /// <summary>Raised once per rocket, on every client, right after the splash has been applied, with the blast
        /// centre - a hit or an airburst alike. Visual only: RocketBlastView draws the floor ring from it. Nothing that
        /// affects damage listens.</summary>
        public event System.Action<Vector3> Detonated;
```

  2. Replace the end of `Detonate`:

```csharp
                target.ApplyDamage(new DamageInfo(amount, context.ShooterActorNumber,
                                                   context.ShooterTeamId, context.Weapon.Id,
                                                   DamageSource.Splash, false, at, -1));
            }
        }
```

  with

```csharp
                target.ApplyDamage(new DamageInfo(amount, context.ShooterActorNumber,
                                                   context.ShooterTeamId, context.Weapon.Id,
                                                   DamageSource.Splash, false, at, -1));
            }

            // Ability visuals step 6: after every splash hit above, so a visual listener can't affect one.
            Detonated?.Invoke(at);
        }
```

- [ ] **Step 3: `RocketBlastView`.** Create `Assets/scripts/Weapons/Effects/RocketBlastView.cs`:

```csharp
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// A rocket's blast drawn on the floor (ability visuals step 6; Tudor: "the rocket explosion is not on the ground
    /// which looks weird"). Rockets fly at muzzle height, so every blast happens about 2 m up: the old puff hung in the
    /// air, and an airburst (range end, or the cursor rocket reaching the cursor) showed nothing at all.
    ///
    /// Listens to ExplodeOnImpact.Detonated, so it fires exactly once per blast, on every client, at the exact splash
    /// centre. It drops a BlastMarker on the floor straight below, sized to where a STANDING player still takes splash:
    /// the Splash Radius sphere cut at a standing player's root height, because falloff is measured to the target's root
    /// and reaches 0 at Splash Radius. The shooter's team colour. Visual only - an IProjectileBehaviour that never keeps
    /// a shot flying.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ExplodeOnImpact))]
    public sealed class RocketBlastView : MonoBehaviour, IProjectileBehaviour
    {
        // Not a design tunable: how far back along the flight the floor probe starts, so a blast on a wall's face looks
        // down on the near side of that wall instead of grazing it.
        private const float ProbeBackOffMetres = 0.3f;

        [SerializeField, Tooltip("The ring left on the floor - Assets/Gameplay/Projectiles/Blast Marker.prefab.")]
        private BlastMarker blastMarkerPrefab;

        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset. The ring is the shooter's colour.")]
        private UiTheme theme;

        private ExplodeOnImpact explode;
        private ProjectileMotor motor;
        private int shooterTeam = -1;

        private void Awake()
        {
            explode = GetComponent<ExplodeOnImpact>();
            explode.Detonated += HandleDetonated;
        }

        private void OnDestroy()
        {
            if (explode != null)
                explode.Detonated -= HandleDetonated;
        }

        public void OnSpawned(ProjectileMotor projectileMotor, ProjectileContext context)
        {
            motor = projectileMotor;
            shooterTeam = context.ShooterTeamId;
        }

        public ProjectileHitResponse OnHit(ProjectileMotor projectileMotor, ProjectileContext context,
                                            RaycastHit hit, IDamageable victim) => ProjectileHitResponse.Despawn;

        public void OnExpired(ProjectileMotor projectileMotor, ProjectileContext context) { }

        private void HandleDetonated(Vector3 centre)
        {
            Vector3 back = motor != null ? motor.Direction : Vector3.zero;
            back.y = 0f;
            if (!GroundSnap.TryFindGroundY(centre - back * ProbeBackOffMetres, out float groundY))
                return; // Over a void: there is no floor to draw on.

            float radius = AbilityVisualGeometry.RadiusOnPlane(explode.SplashRadius, centre.y, groundY + StandingBody.RootAboveFeet());
            Color color = theme != null ? theme.ShotColorFor(shooterTeam) : Color.white;
            BlastMarker.Spawn(blastMarkerPrefab, new Vector3(centre.x, groundY, centre.z), radius, color);
        }
    }
}
```

- [ ] **Step 4: Recompile, dirty check, run tests.** Expected: the four new structure cases fail; everything else
  passes, including `RocketSplashNumbersAreUnchanged` and `DetonateAtCursorTests`.

- [ ] **Step 5: Wire the prefabs.** Write `SCRATCH\BuildRocketBlastViews.cs` with the class below, followed by
  `PrefabParts` (Task 2 Step 9), using the same `using` lines:

```csharp
/// Ability visuals step 6 (scratch, not committed): RocketBlastView on the three rockets; the fire field's disc on the floor.
public static class BuildRocketBlastViews
{
    private static readonly string[] Rockets =
    {
        "Assets/Gameplay/Projectiles/Rocket.prefab",
        "Assets/Gameplay/Projectiles/Rocket Distance.prefab",
        "Assets/Gameplay/Projectiles/Rocket Cursor.prefab",
    };
    private const string FireFieldPath = "Assets/Resources/Fire Field.prefab";

    public static string Run()
    {
        if (SceneManager.GetActiveScene().isDirty) return "ABORT: scene dirty before the edit";
        UiTheme theme = PrefabParts.Load<UiTheme>(PrefabParts.ThemePath);
        BlastMarker marker = PrefabParts.Load<GameObject>(PrefabParts.MarkerPath).GetComponent<BlastMarker>();

        foreach (string path in Rockets)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<RocketBlastView>() != null) return "ABORT: RocketBlastView already on " + path;
                RocketBlastView view = root.AddComponent<RocketBlastView>();
                PrefabParts.Set(view, "blastMarkerPrefab", marker);
                PrefabParts.Set(view, "theme", theme);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) return "ERROR: save failed " + path;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        GameObject field = PrefabUtility.LoadPrefabContents(FireFieldPath);
        try
        {
            Transform visual = field.transform.Find("Visual");
            if (visual == null) return "ABORT: Fire Field.prefab has no Visual child";
            if (visual.GetComponent<SnapVisualToGround>() == null)
                visual.gameObject.AddComponent<SnapVisualToGround>();
            PrefabUtility.SaveAsPrefabAsset(field, FireFieldPath, out bool saved);
            if (!saved) return "ERROR: Fire Field save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(field); }

        return $"3 rockets + fire field wired; scene dirty={SceneManager.GetActiveScene().isDirty}";
    }
}
```

Run: `unity command --timeout 240 run_script -- --file "SCRATCH\BuildRocketBlastViews.cs" --entry BuildRocketBlastViews.Run --timeout_ms 200000`.
Expected: `3 rockets + fire field wired; scene dirty=False`.

- [ ] **Step 6: Read back.**
  - Dirty check: `False`.
  - Recompile (no-op), then run tests: **all** pass.
  - `git diff --stat` on the four prefabs: additions only. In the rockets' diffs no `splash`, `hitMask` or `falloff`
    line changes.

- [ ] **Step 7: Play Mode captures.** Run **Play Mode join**. `SCRATCH\s6_rocket_tpl.cs` = the **capture prologue**
  followed by (`__SHOT__`: 1 = direct hit on the dummy, 2 = cursor-rocket airburst beside it, 3 = a wall hit):

```csharp
int shot = __SHOT__;
System.Collections.IEnumerator Run()
{
    string name = shot == 1 ? "s6-rocket-direct" : shot == 2 ? "s6-rocket-cursor" : "s6-rocket-wall";
    loadout.SetWeapon(shot == 2 ? 4 : 2);
    Vector3 aimPoint = dummy.transform.position;
    if (shot == 3)
    {
        disp.TeleportTo(FreePoint(dummy.transform.position, 6f));
        yield return new WaitForSeconds(0.5f);
        Vector3 muzzle = firing.MuzzlePosition;
        float best = float.MaxValue;
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (Physics.Raycast(muzzle, dir, out RaycastHit h, 8f, buildingMask, QueryTriggerInteraction.Ignore) && h.distance > 3f && h.distance < best)
            {
                best = h.distance;
                aimPoint = new Vector3(h.point.x, me.transform.position.y, h.point.z);
            }
        }
        facts.AppendLine(best < float.MaxValue ? $"wall {best:0.0} m away at {aimPoint:F2}" : "NO WALL 3-8 m from here - rerun after teleporting next to a building");
    }
    else
    {
        disp.TeleportTo(FreePoint(dummy.transform.position, 8f));
        yield return new WaitForSeconds(0.5f);
        Vector3 toDummy = dummy.transform.position - me.transform.position; toDummy.y = 0f;
        if (shot == 2) aimPoint = dummy.transform.position + Vector3.Cross(Vector3.up, toDummy.normalized) * 2f;
    }
    yield return new WaitForSeconds(1.5f);
    aim.SetAimOverride(aimPoint);
    yield return null; yield return null;
    var before = new System.Collections.Generic.HashSet<Overpower.Abilities.BlastMarker>(UnityEngine.Object.FindObjectsByType<Overpower.Abilities.BlastMarker>(FindObjectsSortMode.None));
    float muzzleY = firing.MuzzlePosition.y;
    facts.AppendLine($"weapon={firing.Weapon.Id} fired={firing.TryFire()} muzzle.y={muzzleY:0.000} root.y={me.transform.position.y:0.000} ground.y={GroundUnder(me.transform.position):0.000} StandingBody={Overpower.Abilities.StandingBody.RootAboveFeet():0.000}");
    Overpower.Abilities.BlastMarker marker = null;
    float giveUp = Time.realtimeSinceStartup + 6f;
    while (marker == null && Time.realtimeSinceStartup < giveUp)
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<Overpower.Abilities.BlastMarker>(FindObjectsSortMode.None))
            if (!before.Contains(m)) marker = m;
        if (marker == null) yield return null;
    }
    if (marker == null) { facts.AppendLine("NO BLAST MARKER within 6 s"); Freeze(name); yield break; }
    float gy = GroundUnder(marker.transform.position);
    float expected = Overpower.Combat.AbilityVisualGeometry.RadiusOnPlane(3f, muzzleY, gy + Overpower.Abilities.StandingBody.RootAboveFeet());
    facts.AppendLine($"marker at {marker.transform.position:F3} radius={marker.Radius:0.000} expected(3 m sphere at muzzle height, cut at a standing root)={expected:0.000} marker.y-ground={marker.transform.position.y - gy:0.000}");
    if (shot == 1) facts.AppendLine($"markerToDummy(flat)={Flat(marker.transform.position, dummy.transform.position):0.00}");
    if (shot == 3) facts.AppendLine($"markerToWallPoint(flat)={Flat(marker.transform.position, aimPoint):0.00}");
    var puff = GameObject.Find("FXBulletExplosion(Clone)");
    facts.AppendLine(puff == null ? "no puff (expected for an airburst)" : $"puff at {puff.transform.position:F2} (the real hit point, left in the air on purpose)");
    foreach (var field in UnityEngine.Object.FindObjectsByType<Overpower.Weapons.FireField>(FindObjectsSortMode.None))
    {
        Transform v = field.transform.Find("Visual");
        facts.AppendLine($"fireField root.y={field.transform.position.y:0.000} visual.y={v.position.y:0.000} ground.y={GroundUnder(field.transform.position):0.000} visualScaleX={v.localScale.x:0.00}");
    }
    Freeze(name);
}
BuildingManager.Instance.StartCoroutine(Run());
return "running shot " + shot;
```

  For `__SHOT__` 1, 2 and 3 in turn: fill the template, run it, and follow the **Capture procedure** for
  `s6-rocket-direct`, `s6-rocket-cursor` and `s6-rocket-wall`.
  - **Expected facts:**
    - `radius` equals `expected` within 0.01 (about 2.6 m; report the measured value, it goes in the commit message);
    - marker 0.04 above the ground;
    - the direct hit's marker within 1.0 m (flat) of the dummy's centre (the hit is on its 0.7 m capsule surface);
    - the wall marker within 0.5 m of the wall point;
    - the cursor shot has a fire field whose `visual.y − ground.y` ≈ 0.02 while `root.y` stays at muzzle height, and
      `visualScaleX` 5.00.
  - **Expected pictures:** a flat, see-through ring in your team colour on the floor under each blast, with the small
    puff still at the real hit (direct, wall). For the cursor rocket, the ring and the orange fire disc both lie on the
    floor. Compare with `s1-before-rocket.png`.

  Stop Play Mode; dirty check: `False`.

- [ ] **Step 8: Commit and push** (only these files; put the measured ring radius in the message):

```
git add Assets/scripts/Weapons/Effects/RocketBlastView.cs Assets/scripts/Weapons/Effects/RocketBlastView.cs.meta Assets/scripts/Weapons/Effects/ExplodeOnImpact.cs "Assets/Gameplay/Projectiles/Rocket.prefab" "Assets/Gameplay/Projectiles/Rocket Distance.prefab" "Assets/Gameplay/Projectiles/Rocket Cursor.prefab" "Assets/Resources/Fire Field.prefab" Assets/Tests/AbilityVisualStructureTests.cs
git status
git commit -m "feat(weapons): rocket blasts leave a ring on the floor sized to the real standing reach (ability visuals step 6)" -m "Measured ring radius at muzzle height: <value from s6-rocket-direct.ready.txt> m." -m "Co-Authored-By: <your model line>"
git push
```

Log under the assumptions heading:
- `[C] Rocket floor ring = splash sphere cut at standing root height (measured <value> m); puff kept at the hit point; fire field disc on the floor`
- In **Questions for you**:
  - `Rocket splash reaches about <value> m on the floor, not 3 [unchanged]`
  - `Cursor fire field burns from a sphere about 2 m up [unchanged]`

---

### Task 7 (ability visuals step 7): a square zip-gun hook on a rope

**What exists:**
- `Zip Gun Bullet.prefab` is one object: `ProjectileMotor`, `AbilityHitRelay`, and a built-in Sphere renderer at
  scale 0.2 (`Zip Gun Bullet.prefab:33,45`).
- The hit is `ProjectileMotor`'s sphere cast with `ZipGunAbility.projectileRadius` 0.15 (`ProjectileMotor.cs:179`);
  the root's scale is never read.
- Remote clients get a 0.35 m `CreatePrimitive` sphere for 0.3 s at the bite (`ZipGunAbility.cs:169-174,251-265`).
- `PhaseTether` already reaches every client with the real hit point.

**Files:**
- Create: `Assets/scripts/Abilities/Visuals/ZipBoltView.cs`
- Modify: `Assets/scripts/Abilities/Mobility/ZipGunAbility.cs` (the cosmetic tether only; no RPC or phase added)
- Modify: `Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab`, `Assets/Gameplay/Abilities/Zip Gun.prefab`,
  `Assets/Tests/AbilityVisualStructureTests.cs`
- Scratch: `SCRATCH\BuildZipHook.cs`, `SCRATCH\s7_zip_tpl.cs`

- [ ] **Step 1: Failing structure test.** Add inside `AbilityVisualStructureTests`:

```csharp
        [Test]
        public void TheZipHookIsASquareHeadOnARopeWithNoCollider()
        {
            GameObject prefab = Load("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab");
            Assert.AreEqual(Vector3.one, prefab.transform.localScale, "the old 0.2 sphere scale is gone");
            Assert.IsNull(prefab.GetComponent<MeshRenderer>(), "the head is a child now");
            Assert.IsNotNull(prefab.GetComponent<ProjectileMotor>());
            Assert.IsNotNull(prefab.GetComponent<AbilityHitRelay>());
            Transform head = Child(prefab.transform, "Head");
            AssertMesh(head, "Cube", SolidPath);
            Assert.AreEqual(0.45f, head.localScale.x, 1e-5f, "a visual-only size; the hit radius lives on Zip Gun.prefab");
            var rope = Child(prefab.transform, "Rope").GetComponent<LineRenderer>();
            Assert.IsNotNull(rope);
            Assert.IsTrue(rope.useWorldSpace);
            Assert.AreEqual(2, rope.positionCount);
            Assert.AreEqual(LinePath, AssetDatabase.GetAssetPath(rope.sharedMaterial));
            var view = prefab.GetComponent<ZipBoltView>();
            Assert.IsNotNull(view);
            Assert.AreSame(head.GetComponent<MeshRenderer>(), Ref(view, "head"));
            Assert.AreSame(rope, Ref(view, "rope"));
            AssertNoCollider(prefab);

            var ability = Load("Assets/Gameplay/Abilities/Zip Gun.prefab").GetComponent<ZipGunAbility>();
            Assert.AreEqual(SolidPath, AssetDatabase.GetAssetPath(Ref(ability, "anchorMaterial")));
            Assert.AreEqual(LinePath, AssetDatabase.GetAssetPath(Ref(ability, "ropeMaterial")));
        }
```

- [ ] **Step 2: `ZipBoltView`.** Create `Assets/scripts/Abilities/Visuals/ZipBoltView.cs`:

```csharp
using Overpower.Combat;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The zip gun's hook in flight (ability visuals step 7; Tudor: "too small and round - at least a square"): a cube
    /// head facing its flight, and a rope back to the shooter's muzzle. Every client already spawns this projectile
    /// locally from the cast RPC, so every client draws the same.
    ///
    /// Visual only: an IProjectileBehaviour that never keeps a shot flying and never touches the sweep. The hit is still
    /// ProjectileMotor's sphere of Zip Gun.prefab › Projectile Radius (0.15 m). The head is drawn larger than that on
    /// purpose, so it reads at the camera's distance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZipBoltView : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Colour of the hook head, its rope and the square anchor where it bites (the zip gun " +
                 "reads it from here). Not a team colour: ability projectiles keep one fixed look.")]
        private Color hookColor = new Color(1f, 0.8f, 0.2f, 1f);

        [SerializeField, Tooltip("The cube head. Its size is its transform scale on this prefab - visual only.")]
        private Renderer head;

        [SerializeField, Tooltip("The rope from the shooter's muzzle to the head while it flies.")]
        private LineRenderer rope;

        [SerializeField, Range(0f, 1f), Tooltip("Rope opacity.")]
        private float ropeOpacity = 0.8f;

        private WeaponFiring shooterWeapon;
        private Vector3 fallbackStart;

        public Color HookColor => hookColor;

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context)
        {
            fallbackStart = transform.position;
            PhotonView shooter = PlayerLookup.GetPhotonViewFor(context.ShooterActorNumber);
            shooterWeapon = shooter != null ? shooter.GetComponent<WeaponFiring>() : null;

            VisualTint.SetMeshColor(head, new MaterialPropertyBlock(), hookColor);
            VisualTint.SetLineColor(rope, VisualTint.WithAlpha(hookColor, ropeOpacity));
            UpdateRope();
        }

        private void LateUpdate() => UpdateRope();

        private void UpdateRope()
        {
            if (rope == null)
                return;
            rope.SetPosition(0, shooterWeapon != null ? shooterWeapon.MuzzlePosition : fallbackStart);
            rope.SetPosition(1, transform.position);
        }

        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context, RaycastHit hit, IDamageable victim) =>
            ProjectileHitResponse.Despawn;

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
```

- [ ] **Step 3: `ZipGunAbility.cs` edits** (cosmetic tether only).
  1. Replace the tether fields

```csharp
        [Header("Remote tether (cosmetic only)")]
        [SerializeField, Tooltip("Radius in metres of the cheap marker shown at the impact point on " +
                 "every OTHER client's screen when your bolt lands a hit, so the shot reads as " +
                 "landing somewhere real even though only your own screen shows the actual pull. 0 " +
                 "shows nothing.")]
        private float tetherMarkerRadius = 0.35f;

        [SerializeField, Tooltip("Seconds the impact marker stays up before it disappears on its own.")]
        private float tetherMarkerSeconds = 0.3f;
```

  with

```csharp
        [Header("Tether (cosmetic only)")]
        [SerializeField, Tooltip("Half the side, in metres, of the square anchor shown where your bolt bit, on " +
                 "every client (yours included). 0 shows no anchor.")]
        private float tetherMarkerRadius = 0.35f;

        [SerializeField, Tooltip("Shortest time, in seconds, the anchor and the rope stay up. They stay longer " +
                 "when the pull itself takes longer (distance ÷ Pull Speed).")]
        private float tetherMarkerSeconds = 0.3f;

        [SerializeField, Tooltip("Material of the square anchor - Assets/Gameplay/Abilities/Ability Visual Solid.mat. " +
                 "Its colour is the hook colour on the projectile prefab (Zip Bolt View).")]
        private Material anchorMaterial;

        [SerializeField, Tooltip("Material of the rope from you to the anchor during the pull - " +
                 "Assets/Gameplay/UI/AimConeLine.mat.")]
        private Material ropeMaterial;

        [SerializeField, Tooltip("Rope thickness, in metres.")]
        private float ropeWidth = 0.05f;
```

  2. Replace

```csharp
        private bool pulling;
```

  with

```csharp
        private bool pulling;

        // Every client: the pull rope, built once and reused. Unparented, like the flamethrower's cone, because this
        // module sits under the player, whose hierarchy changes layer on death.
        private LineRenderer pullRope;
        private Vector3 tetherPoint;
        private float ropeUntil;
        private MaterialPropertyBlock anchorBlock;
```

  3. Replace

```csharp
        private void OnDestroy()
        {
            CombatEvents.LocalTakedown -= HandleLocalTakedown;
        }
```

  with

```csharp
        private void OnDestroy()
        {
            CombatEvents.LocalTakedown -= HandleLocalTakedown;
            if (pullRope != null)
                Destroy(pullRope.gameObject);
        }
```

  4. Replace

```csharp
                case PhaseTether:
                    // The caster's own screen already shows the real pull; this marker is only for
                    // everyone else, exactly like TeleportAbility's arrival VFX.
                    if (!cast.IsCasterClient)
                        PlayTetherMarker(cast.Payload.Point);
                    return;
```

  with

```csharp
                case PhaseTether:
                    // Ability visuals step 7: every client, the caster's own included, draws the square
                    // anchor and a rope for as long as the pull takes. The caster used to see the pull
                    // with nothing connecting them to where the hook bit.
                    PlayTether(cast.Payload.Point);
                    return;
```

  5. Replace the whole `PlayTetherMarker` method (from `        private void PlayTetherMarker(Vector3 point)` to its
     closing brace) with:

```csharp
        private void PlayTether(Vector3 point)
        {
            Vector3 toPoint = point - Owner.Root.transform.position;
            toPoint.y = 0f;
            float travel = Mathf.Max(0f, toPoint.magnitude - (capsule != null ? capsule.radius : 0f));
            float seconds = Mathf.Max(tetherMarkerSeconds, AbilityVisualGeometry.PullSeconds(travel, pullSpeed));
            Color color = HookColor();

            if (tetherMarkerRadius > 0f)
            {
                GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                anchor.name = "Zip Anchor VFX (cheap, cosmetic only)";
                // Removed immediately, not with Destroy, so the cube is never a solid object in the world, even
                // for one frame - the same trick the old sphere marker used.
                DestroyImmediate(anchor.GetComponent<Collider>());
                anchor.transform.SetPositionAndRotation(point,
                    toPoint.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPoint) : Quaternion.identity);
                anchor.transform.localScale = Vector3.one * tetherMarkerRadius * 2f;
                var renderer = anchor.GetComponent<MeshRenderer>();
                if (anchorMaterial != null)
                    renderer.sharedMaterial = anchorMaterial;
                if (anchorBlock == null)
                    anchorBlock = new MaterialPropertyBlock();
                VisualTint.SetMeshColor(renderer, anchorBlock, color);
                Destroy(anchor, seconds);
            }

            if (ropeMaterial == null)
                return;
            if (pullRope == null)
                pullRope = BuildRope();
            VisualTint.SetLineColor(pullRope, color);
            tetherPoint = point;
            ropeUntil = Time.time + seconds;
            pullRope.enabled = true;
            UpdateRope();
        }

        private Color HookColor()
        {
            ZipBoltView view = projectilePrefab != null ? projectilePrefab.GetComponent<ZipBoltView>() : null;
            return view != null ? view.HookColor : Color.white;
        }

        private LineRenderer BuildRope()
        {
            var go = new GameObject("Zip Rope VFX (cheap, cosmetic only)");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = ropeMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = ropeWidth;
            line.endWidth = ropeWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            if (pullRope == null || !pullRope.enabled)
                return;
            if (Time.time >= ropeUntil)
            {
                pullRope.enabled = false;
                return;
            }
            UpdateRope();
        }

        private void UpdateRope()
        {
            Vector3 start = Owner.Weapon != null ? Owner.Weapon.MuzzlePosition : Owner.Root.transform.position;
            pullRope.SetPosition(0, start);
            pullRope.SetPosition(1, tetherPoint);
        }
```

  Then `git diff -- Assets/scripts/Abilities/Mobility/ZipGunAbility.cs`: `TryBuildCast`, `FireProjectile`,
  `HandleZipHit`, `Interrupt` and the projectile/pull fields are unchanged.

- [ ] **Step 4: Recompile, dirty check, run tests.** Expected: the new structure test fails; everything else passes,
  including `ZipGunNumbersAreUnchanged`.

- [ ] **Step 5: Rebuild the bolt and wire the module.** Write `SCRATCH\BuildZipHook.cs` with the class below, followed
  by `PrefabParts` (Task 2 Step 9), using the same `using` lines:

```csharp
/// Ability visuals step 7 (scratch, not committed): Zip Gun Bullet becomes a cube head + rope; Zip Gun gets its tether materials.
public static class BuildZipHook
{
    private const string BoltPath = "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab";
    private const string ModulePath = "Assets/Gameplay/Abilities/Zip Gun.prefab";

    public static string Run()
    {
        if (SceneManager.GetActiveScene().isDirty) return "ABORT: scene dirty before the edit";
        Material solid = PrefabParts.Load<Material>(PrefabParts.SolidPath);
        Material line = PrefabParts.Load<Material>(PrefabParts.LinePath);

        GameObject bolt = PrefabUtility.LoadPrefabContents(BoltPath);
        try
        {
            Scene scene = bolt.scene;
            var oldRenderer = bolt.GetComponent<MeshRenderer>();
            var oldFilter = bolt.GetComponent<MeshFilter>();
            if (oldRenderer == null || oldFilter == null) return "ABORT: Zip Gun Bullet has no root mesh to replace";
            UnityEngine.Object.DestroyImmediate(oldRenderer);
            UnityEngine.Object.DestroyImmediate(oldFilter);
            bolt.transform.localScale = Vector3.one;

            MeshRenderer head = PrefabParts.MeshPart(scene, bolt.transform, "Head", "Cube.fbx", solid,
                Vector3.zero, Vector3.zero, new Vector3(0.45f, 0.45f, 0.45f));
            LineRenderer rope = PrefabParts.LinePart(scene, bolt.transform, "Rope", line, 0.05f, flat: false, height: 0f);
            rope.useWorldSpace = true;
            rope.loop = false;
            rope.positionCount = 2;

            ZipBoltView view = bolt.AddComponent<ZipBoltView>();
            PrefabParts.Set(view, "head", head);
            PrefabParts.Set(view, "rope", rope);
            PrefabUtility.SaveAsPrefabAsset(bolt, BoltPath, out bool saved);
            if (!saved) return "ERROR: Zip Gun Bullet save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(bolt); }

        GameObject module = PrefabUtility.LoadPrefabContents(ModulePath);
        try
        {
            var ability = module.GetComponent<ZipGunAbility>();
            PrefabParts.Set(ability, "anchorMaterial", solid);
            PrefabParts.Set(ability, "ropeMaterial", line);
            PrefabUtility.SaveAsPrefabAsset(module, ModulePath, out bool saved);
            if (!saved) return "ERROR: Zip Gun.prefab save failed";
        }
        finally { PrefabUtility.UnloadPrefabContents(module); }

        return $"zip hook rebuilt; scene dirty={SceneManager.GetActiveScene().isDirty}";
    }
}
```

Run: `unity command --timeout 240 run_script -- --file "SCRATCH\BuildZipHook.cs" --entry BuildZipHook.Run --timeout_ms 200000`.
Expected: `zip hook rebuilt; scene dirty=False`.

- [ ] **Step 6: Read back.**
  - Dirty check: `False`.
  - Recompile (no-op), then run tests: **all** pass.
  - `git diff -- "Assets/Gameplay/Abilities/Zip Gun.prefab"` shows only `anchorMaterial`, `ropeMaterial` and
    `ropeWidth` lines added; no `range`, `projectile*` or `pullSpeed` lines.

- [ ] **Step 7: Play Mode captures.** Run **Play Mode join**. `SCRATCH\s7_zip_tpl.cs` = the **capture prologue**
  followed by (`__SHOT__`: 1 = mid-flight, 2 = during the pull):

```csharp
int shot = __SHOT__;
System.Collections.IEnumerator Run()
{
    // A fresh module each run, so the charge is full: Dash (14), then back to the zip gun.
    loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 14);
    yield return null;
    loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 18);
    disp.TeleportTo(FreePoint(dummy.transform.position, 10f));
    yield return new WaitForSeconds(1f);
    aim.SetAimOverride(dummy.transform.position);
    yield return null; yield return null;
    Vector3 muzzleAtCast = firing.MuzzlePosition;
    Cast(typeof(Overpower.Abilities.ZipGunAbility));
    float giveUp = Time.realtimeSinceStartup + 5f;
    if (shot == 1)
    {
        Overpower.Abilities.ZipBoltView bolt = null;
        while (Time.realtimeSinceStartup < giveUp)
        {
            bolt = UnityEngine.Object.FindFirstObjectByType<Overpower.Abilities.ZipBoltView>();
            if (bolt != null && Flat(bolt.transform.position, muzzleAtCast) > 3f) break;
            yield return null;
        }
        Time.timeScale = 0.0001f;
        yield return null; // let LateUpdate draw the rope for this frame
        if (bolt == null) { facts.AppendLine("NO BOLT SEEN"); Freeze("s7-zip-flight"); yield break; }
        var head = bolt.transform.Find("Head");
        var rope = bolt.transform.Find("Rope").GetComponent<LineRenderer>();
        facts.AppendLine($"bolt flown={Flat(bolt.transform.position, muzzleAtCast):0.00} headSize={head.lossyScale:F3} headColour={MeshColor(head.GetComponent<Renderer>())}");
        facts.AppendLine($"ropeStartToMuzzle={Vector3.Distance(rope.GetPosition(0), firing.MuzzlePosition):0.000} ropeEndToBolt={Vector3.Distance(rope.GetPosition(1), bolt.transform.position):0.000} ropeColour={rope.startColor}");
        Freeze("s7-zip-flight");
    }
    else
    {
        GameObject anchor = null;
        while (anchor == null && Time.realtimeSinceStartup < giveUp)
        {
            anchor = GameObject.Find("Zip Anchor VFX (cheap, cosmetic only)");
            if (anchor == null) yield return null;
        }
        yield return new WaitForSeconds(0.1f); // part-way through the pull
        Time.timeScale = 0.0001f;
        yield return null;
        if (anchor == null) { facts.AppendLine("NO ANCHOR SEEN"); Freeze("s7-zip-pull"); yield break; }
        var ropeObject = GameObject.Find("Zip Rope VFX (cheap, cosmetic only)");
        var pullRope = ropeObject != null ? ropeObject.GetComponent<LineRenderer>() : null;
        facts.AppendLine($"anchor size={anchor.transform.localScale.x:0.00} colour={MeshColor(anchor.GetComponent<Renderer>())} collider={(anchor.GetComponent<Collider>() != null)}");
        facts.AppendLine(pullRope == null ? "NO PULL ROPE" : $"ropeEnabled={pullRope.enabled} startToMuzzle={Vector3.Distance(pullRope.GetPosition(0), firing.MuzzlePosition):0.000} endToAnchor={Vector3.Distance(pullRope.GetPosition(1), anchor.transform.position):0.000}");
        facts.AppendLine($"gapToDummy={Flat(me.transform.position, dummy.transform.position):0.00}");
        Freeze("s7-zip-pull");
    }
}
BuildingManager.Instance.StartCoroutine(Run());
return "running shot " + shot;
```

  For `__SHOT__` 1 then 2: fill the template, run it, and follow the **Capture procedure** for `s7-zip-flight` and
  `s7-zip-pull`.
  - **Expected facts:**
    - `headSize` (0.450, 0.450, 0.450); head colour (1, 0.8, 0.2);
    - rope ends ≤ 0.01 from the muzzle and the bolt;
    - anchor size 0.70, `collider=False`;
    - `ropeEnabled=True`, rope ends ≤ 0.01.
  - **Expected pictures:** (1) a clearly square amber block about halfway to the dummy, with a thin amber line back to
    the player. (2) An amber square at the dummy's edge, with a line from the player to it.

  Stop Play Mode; dirty check: `False`.

- [ ] **Step 8: Commit and push** (only these files):

```
git add Assets/scripts/Abilities/Visuals/ZipBoltView.cs Assets/scripts/Abilities/Visuals/ZipBoltView.cs.meta Assets/scripts/Abilities/Mobility/ZipGunAbility.cs "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab" "Assets/Gameplay/Abilities/Zip Gun.prefab" Assets/Tests/AbilityVisualStructureTests.cs
git status
git commit -m "feat(abilities): the zip gun fires a square hook on a rope, with an anchor and rope during the pull (ability visuals step 7)" -m "Co-Authored-By: <your model line>"
git push
```

Log under the assumptions heading:
- `[C] Zip hook: 0.45 m cube head (visual-only scale; hit radius 0.15 unchanged), rope in flight, square anchor + rope for the pull on every client, one amber colour`
- In **Questions for you**: `Make the hook easier to land to match its bigger look? [no - Projectile Radius unchanged]`

---

### Task 8 (ability visuals step 8): two clients see the same thing, and the gameplay didn't move

Read `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\two-client-harness.md` §3–§9, §11 and §12 first.

**Roles:** A = the Editor (the master). B = the dev Player in `Builds/Client2`, launched at 616×576.

**Files:** none committed. Everything is scratch in `SCRATCH`: `b_place_tpl.cs`, `a_sees_b_tpl.cs`, `b_watch_tpl.cs`,
`a_casts_tpl.cs`, and the step 1 recorder.

- [ ] **Step 1: Start state.**
  - Steps 1–7 are committed and pushed.
  - `git status` is clean apart from other agents' files.
  - Edit-mode tests all pass.
  - Editor stopped; dirty check `False`.
  - Record `HEAD` as `AFTER`.

- [ ] **Step 2: Build and launch B.**
  1. `unity command set_build_settings -- --settings '{"developmentBuild":true}' --confirm true`
  2. `unity command build -- --target StandaloneWindows64 --outputPath "Builds/Client2/OverPower.exe" --options '["Development"]' --confirm true`
  3. Poll `unity command build_status` every 10 s until `completed` and `Succeeded`.
  4. `Start-Process "Builds/Client2/OverPower.exe" -ArgumentList "-screen-fullscreen 0 -screen-width 616 -screen-height 576 -logFile Builds/Client2/player.log"`
  5. Poll `unity command --runtime-path "Builds/Client2" runtime_status` until it answers.

- [ ] **Step 3: One room, two teams.**
  1. A runs **Play Mode join** (the dummies appear on A only; they're local scenery).
  2. Read the room name: `unity command eval -- --code "return Photon.Pun.PhotonNetwork.CurrentRoom.Name;"`.
  3. B joins it by name with the harness §5 reflection snippet.
  4. Poll both until `InRoom` and `PlayerCount 2`.
  5. Read both actors and teams on A:
     `unity command eval -- --code "var s = \"\"; foreach (var p in Photon.Pun.PhotonNetwork.PlayerList) { Overpower.Net.Teams.TryGetTeam(p, out int t); s += p.ActorNumber + \":\" + t + \" \"; } return s;"`
  6. Record `AACTOR`, `ATEAM`, `BACTOR` and `BTEAM`. The teams must differ. If they don't, B leaves the room
     (reflection `LeaveRoom`) and joins again.
  7. A moves to open ground away from the dummies:
     `unity command eval -- --code "var me = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber); var s = UnityEngine.Object.FindFirstObjectByType<RoomManager>().teamSpawnPoints[0]; me.GetComponent<PlayerDisplacement>().TeleportTo(s.position - s.forward * 6f); return me.transform.position;"`
  8. After 1 s, read A's position (`AX`, `AY`, `AZ`) with the same `return ...transform.position` eval.

- [ ] **Step 4: R1 - B places, A looks** (enemy view, B's colour on A's screen).
  1. Write `SCRATCH\b_place_tpl.cs` (runtime reflection, harness §6), fill `__AX__ __AY__ __AZ__`, and run
     `unity command --runtime-path "Builds/Client2" eval_file -- --file "SCRATCH\b_place.cs" --timeout 20000`:

```csharp
// Ability visuals step 8, R1, on B: a mine, two portals and a fence near A, then B's own game clock is held still
// (timeScale 0.0001) so B's 8 s fence is still up when A captures.
string SCR = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ability-visuals\";
var aPos = new UnityEngine.Vector3(__AX__f, __AY__f, __AZ__f);
System.Type T(string name) => System.Type.GetType(name + ", Overpower.Runtime");
var pn = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
var local = pn.GetProperty("LocalPlayer").GetValue(null);
int actor = (int)local.GetType().GetProperty("ActorNumber").GetValue(local);
var view = (UnityEngine.Component)T("PlayerLookup").GetMethod("GetPhotonViewFor").Invoke(null, new object[] { actor });
var runner = view.GetComponent(T("AbilityRunner"));
var loadout = view.GetComponent(T("PlayerLoadout"));
var aim = view.GetComponent(T("PlayerAim"));
var disp = view.GetComponent(T("PlayerDisplacement"));
var ult = view.GetComponent(T("UltimateCharge"));
var slotType = T("Overpower.Data.AbilitySlot");
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
void Equip(int slot, int id) => loadout.GetType().GetMethod("SetAbility").Invoke(loadout, new object[] { System.Enum.ToObject(slotType, slot), id });
void Aim(object point) => aim.GetType().GetMethod("SetAimOverride").Invoke(aim, new object[] { point });
void Teleport(UnityEngine.Vector3 p) => disp.GetType().GetMethod("TeleportTo").Invoke(disp, new object[] { p });
void Cast(string moduleType) => runner.GetType().GetMethod("TryCast", flags).Invoke(runner, new object[] { view.GetComponentInChildren(T(moduleType), true) });
UnityEngine.Vector3 Free(UnityEngine.Vector3 target, float dist)
{
    for (int i = 0; i < 16; i++)
    {
        float a = i * 22.5f * UnityEngine.Mathf.Deg2Rad;
        var p = new UnityEngine.Vector3(target.x + UnityEngine.Mathf.Sin(a) * dist, target.y, target.z + UnityEngine.Mathf.Cos(a) * dist);
        if (UnityEngine.Physics.CheckCapsule(p + UnityEngine.Vector3.up * 0.8f, p + UnityEngine.Vector3.up * 1.6f, 0.6f, ~0, UnityEngine.QueryTriggerInteraction.Ignore)) continue;
        return p;
    }
    return target + new UnityEngine.Vector3(dist, 0f, 0f);
}
System.Collections.IEnumerator Run()
{
    Equip(1, 19); Equip(3, 17); Equip(2, 26);                        // Equipment mines, Mobility teleport, Ultimate fence
    UnityEngine.Vector3 spot = Free(aPos, 7f);
    Teleport(spot);
    yield return new UnityEngine.WaitForSeconds(1.5f);
    Cast("Overpower.Abilities.MineAbility");                          // 7 m from A, far outside its 1.8 m trigger
    UnityEngine.Vector3 away = spot - aPos; away.y = 0f; away.Normalize();
    UnityEngine.Vector3 across = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, away);
    Teleport(spot + away * 2f);
    yield return new UnityEngine.WaitForSeconds(0.5f);
    Aim(spot + across * 4f); yield return null; yield return null;
    Cast("Overpower.Abilities.TeleportAbility");
    yield return new UnityEngine.WaitForSeconds(0.5f);
    Aim(spot - across * 4f); yield return null; yield return null;
    Cast("Overpower.Abilities.TeleportAbility");
    yield return new UnityEngine.WaitForSeconds(0.5f);
    Aim(null);
    ult.GetType().GetMethod("Fill").Invoke(ult, null);
    Cast("Overpower.Abilities.ElectricFenceAbility");                 // centred about 9 m from A, outside its 6.5 m band edge
    yield return new UnityEngine.WaitForSeconds(1f);
    UnityEngine.Time.timeScale = 0.0001f;
    System.IO.File.WriteAllText(SCR + "s8-B-placed.txt", $"B actor={actor} mineSpot={spot} fenceCentre={view.transform.position}");
}
((UnityEngine.MonoBehaviour)runner).StartCoroutine(Run());
return "placing";
```

  2. Wait for `SCRATCH\s8-B-placed.txt`.
  3. Write `SCRATCH\a_sees_b_tpl.cs` = the **capture prologue** followed by the block below. Fill `__BACTOR__`, run it
     on A with `eval_file`, and follow the **Capture procedure** for `s8-A-sees-B`.

```csharp
int bActor = __BACTOR__;
Overpower.Net.Teams.TryGetTeam(bActor, out int bTeam);
typeof(CameraTracking).GetField("currentZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(UnityEngine.Object.FindFirstObjectByType<CameraTracking>(), 1.6f);
System.Collections.IEnumerator Run()
{
    yield return null; yield return null;
    facts.AppendLine($"A actor={myActor} team={myTeam}; B actor={bActor} team={bTeam}; B colour={theme.ShotColorFor(bTeam)}");
    foreach (var mine in Overpower.Abilities.Mine.ForOwner(bActor))
    {
        Transform visual = mine.transform.Find("Visual");
        facts.AppendLine($"B mine ownerTeam={mine.OwnerTeam} stud={MeshColor(visual.Find("Stud").GetComponent<Renderer>())} triggerRingEnabled={visual.Find("Trigger Ring").GetComponent<LineRenderer>().enabled} visual-ground={visual.position.y - GroundUnder(mine.transform.position):0.000}");
    }
    foreach (var portal in Overpower.Abilities.Portal.ForOwner(bActor))
        facts.AppendLine($"B portal ownerTeam={portal.OwnerTeam} footprint={MeshColor(portal.transform.Find("Footprint").GetComponent<Renderer>())} rim={portal.transform.Find("Rim").GetComponent<LineRenderer>().startColor} beaconActive={portal.transform.Find("Owner Beacon").gameObject.activeSelf}");
    foreach (var fence in UnityEngine.Object.FindObjectsByType<Overpower.Abilities.ElectricFence>(FindObjectsSortMode.None))
        if (fence.OwnerActor == bActor)
            facts.AppendLine($"B fence ownerTeam={fence.OwnerTeam} post={MeshColor(fence.transform.Find("Cage/Post").GetComponent<Renderer>())} band={fence.transform.Find("Cage/Band").GetComponent<LineRenderer>().startColor}");
    Freeze("s8-A-sees-B");
}
BuildingManager.Instance.StartCoroutine(Run());
return "checking";
```

  4. **Expected facts:**
     - `ownerTeam` = `BTEAM` everywhere;
     - stud, footprint (rgb), rim (rgb), post (rgb) and band (rgb) all equal B's colour;
     - `triggerRingEnabled=False` (A is an enemy);
     - `beaconActive=False` for both portals (not A's);
     - mine `visual-ground` ≈ 0.02.
  5. **Expected picture:** B's dark mine with a B-coloured stud and no ring, two B-coloured rimmed discs without
     diamonds, and B's cage, all on the floor.
  6. Then unfreeze B:
     `unity command --runtime-path "Builds/Client2" eval -- --code "UnityEngine.Time.timeScale = 1f; return UnityEngine.Time.timeScale;"`
  7. Reset A's zoom (Task 4 Step 7 command).

- [ ] **Step 5: R2 - A casts, B looks** (remote transient visuals and A's colour on B's screen).
  1. Write `SCRATCH\b_watch_tpl.cs`, fill `__AACTOR__`, and run it on B **before** A casts:

```csharp
// Ability visuals step 8, R2, on B: photographs A's visuals the frame they appear on B's own screen, and reads A's team
// tint off B's own copies. Writes SCRATCH\s8-B-sees-A-*.png/.txt and s8-B-watch-done.txt.
string SCR = @"C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad\ability-visuals\";
int aActor = __AACTOR__;
System.Type T(string name) => System.Type.GetType(name + ", Overpower.Runtime");
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var pn = System.Type.GetType("Photon.Pun.PhotonNetwork, PhotonUnityNetworking");
var local = pn.GetProperty("LocalPlayer").GetValue(null);
int actor = (int)local.GetType().GetProperty("ActorNumber").GetValue(local);
var view = (UnityEngine.Component)T("PlayerLookup").GetMethod("GetPhotonViewFor").Invoke(null, new object[] { actor });
var host = (UnityEngine.MonoBehaviour)view.GetComponent(T("AbilityRunner"));
T("CameraTracking").GetField("currentZoom", flags).SetValue(UnityEngine.Object.FindFirstObjectByType(T("CameraTracking")), 1.6f);
var block = new UnityEngine.MaterialPropertyBlock();
UnityEngine.Color MeshColor(UnityEngine.Transform part) { part.GetComponent<UnityEngine.Renderer>().GetPropertyBlock(block); return block.GetColor("_BaseColor"); }
int Owner(UnityEngine.Component c) => (int)c.GetType().GetProperty("OwnerActor").GetValue(c);
UnityEngine.Component OwnedByA(string typeName)
{
    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(T(typeName), UnityEngine.FindObjectsSortMode.None))
        if (Owner((UnityEngine.Component)o) == aActor) return (UnityEngine.Component)o;
    return null;
}
UnityEngine.Component FirstActive(string typeName)
{
    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(T(typeName), UnityEngine.FindObjectsSortMode.None))
        if (((UnityEngine.Component)o).gameObject.activeInHierarchy) return (UnityEngine.Component)o;
    return null;
}
System.Collections.IEnumerator Shoot(string name, string facts)
{
    UnityEngine.ScreenCapture.CaptureScreenshot(SCR + name + ".png");
    System.IO.File.WriteAllText(SCR + name + ".txt", facts);
    yield return null;
}
System.Collections.IEnumerator Watch()
{
    bool deployables = false, cone = false, bolt = false, blast = false;
    float giveUpAt = UnityEngine.Time.realtimeSinceStartup + 300f;
    while (UnityEngine.Time.realtimeSinceStartup < giveUpAt && !(deployables && cone && bolt && blast))
    {
        if (!deployables)
        {
            var mine = OwnedByA("Overpower.Abilities.Mine");
            var portal = OwnedByA("Overpower.Abilities.Portal");
            var fence = OwnedByA("Overpower.Abilities.ElectricFence");
            if (mine != null && portal != null && fence != null)
            {
                deployables = true;
                yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
                var mineView = mine.GetComponent(T("Overpower.Abilities.MineView"));
                object theme = mineView.GetType().GetField("theme", flags).GetValue(mineView);
                int ownerTeam = (int)mine.GetType().GetProperty("OwnerTeam").GetValue(mine);
                object expected = theme.GetType().GetMethod("ShotColorFor").Invoke(theme, new object[] { ownerTeam });
                string facts = $"A mine ownerTeam={ownerTeam} expected={expected} stud={MeshColor(mine.transform.Find("Visual/Stud"))} triggerRingEnabled={mine.transform.Find("Visual/Trigger Ring").GetComponent<UnityEngine.LineRenderer>().enabled}\n" +
                               $"A portal footprint={MeshColor(portal.transform.Find("Footprint"))} beaconActive={portal.transform.Find("Owner Beacon").gameObject.activeSelf}\n" +
                               $"A fence post={MeshColor(fence.transform.Find("Cage/Post"))} band={fence.transform.Find("Cage/Band").GetComponent<UnityEngine.LineRenderer>().startColor}";
                yield return Shoot("s8-B-sees-A-deployables", facts);
            }
        }
        if (!cone)
        {
            var c = FirstActive("Overpower.Abilities.FlameConeVisual");
            if (c != null) { cone = true; yield return Shoot("s8-B-sees-A-flame", $"cone at {c.transform.position} facing {c.transform.forward}"); }
        }
        if (!bolt)
        {
            var b = FirstActive("Overpower.Abilities.ZipBoltView");
            if (b != null) { bolt = true; yield return Shoot("s8-B-sees-A-hook", $"bolt at {b.transform.position} headSize={b.transform.Find("Head").lossyScale}"); }
        }
        if (!blast)
        {
            var m = FirstActive("Overpower.Abilities.BlastMarker");
            if (m != null) { blast = true; yield return Shoot("s8-B-sees-A-blast", $"marker at {m.transform.position} radius={m.GetType().GetProperty("Radius").GetValue(m)} rim={m.transform.Find("Rim").GetComponent<UnityEngine.LineRenderer>().startColor}"); }
        }
        yield return null;
    }
    System.IO.File.WriteAllText(SCR + "s8-B-watch-done.txt", $"deployables={deployables} cone={cone} bolt={bolt} blast={blast}");
}
host.StartCoroutine(Watch());
return "watching";
```

  2. Write `SCRATCH\a_casts_tpl.cs` = the **capture prologue** followed by the block below. Fill `__BACTOR__` and run
     it on A:

```csharp
int bActor = __BACTOR__;
var bView = PlayerLookup.GetPhotonViewFor(bActor);
System.Collections.IEnumerator Run()
{
    Vector3 bPos = bView.transform.position;
    loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 19);
    loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 17);
    loadout.SetAbility(Overpower.Data.AbilitySlot.Ultimate, 26);
    Vector3 spot = FreePoint(bPos, 7f);
    disp.TeleportTo(spot);
    yield return new WaitForSeconds(1.5f);
    Cast(typeof(Overpower.Abilities.MineAbility));
    Vector3 away = spot - bPos; away.y = 0f; away.Normalize();
    Vector3 across = Vector3.Cross(Vector3.up, away);
    disp.TeleportTo(spot + away * 2f);
    yield return new WaitForSeconds(0.5f);
    aim.SetAimOverride(spot + across * 4f); yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.TeleportAbility));
    yield return new WaitForSeconds(0.5f);
    aim.SetAimOverride(spot - across * 4f); yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.TeleportAbility));
    yield return new WaitForSeconds(0.5f);
    me.GetComponent<UltimateCharge>().Fill();
    Cast(typeof(Overpower.Abilities.ElectricFenceAbility));       // centred about 9 m from B
    yield return new WaitForSeconds(3f);                           // B photographs the deployables

    loadout.SetAbility(Overpower.Data.AbilitySlot.Equipment, 21);
    disp.TeleportTo(FreePoint(bPos, 5f));
    yield return new WaitForSeconds(1f);
    aim.SetAimOverride(bView.transform.position); yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.FlamethrowerAbility));
    yield return new WaitForSeconds(2f);

    loadout.SetAbility(Overpower.Data.AbilitySlot.Mobility, 18);
    disp.TeleportTo(FreePoint(bPos, 10f));
    yield return new WaitForSeconds(1f);
    aim.SetAimOverride(bView.transform.position); yield return null; yield return null;
    Cast(typeof(Overpower.Abilities.ZipGunAbility));
    yield return new WaitForSeconds(2f);

    loadout.SetWeapon(2);
    disp.TeleportTo(FreePoint(bPos, 8f));
    yield return new WaitForSeconds(1.5f);
    aim.SetAimOverride(bView.transform.position); yield return null; yield return null;
    facts.AppendLine($"rocket fired={firing.TryFire()} A team={myTeam} A colour={theme.ShotColorFor(myTeam)}");
    yield return new WaitForSeconds(2f);
    aim.SetAimOverride(null);
    loadout.SetWeapon(1);
    System.IO.File.WriteAllText(SCR + "s8-A-cast-done.txt", facts.ToString());
}
BuildingManager.Instance.StartCoroutine(Run());
return "casting (about 25 s)";
```

  3. Wait for `SCRATCH\s8-A-cast-done.txt` and `SCRATCH\s8-B-watch-done.txt`.
  4. Read all four `s8-B-sees-A-*.png` and their `.txt`.
  5. **Expected:**
     - `watch-done` is all True.
     - Deployables: the stud equals `expected` (A's colour); `triggerRingEnabled=False`; `beaconActive=False`; post and
       band rgb equal A's colour.
     - Flame: an orange wedge on the floor from A toward B.
     - Hook: an amber square and a rope from A.
     - Blast: a ring on the floor at B in A's colour (rim rgb = A's colour), radius about the step 6 value.
     - If B's in-process capture is black or missing, say so and repeat that one cast with a CLI capture on B:
       `unity command --runtime-path "Builds/Client2" capture_game_view -- --source screen --save_path "..."`.

- [ ] **Step 6: Shut down B.**
  - `Stop-Process -Name OverPower -Force -ErrorAction SilentlyContinue; Stop-Process -Name UnityCrashHandler64 -Force -ErrorAction SilentlyContinue`.
  - `unity command editor_stop`; poll until stopped; dirty check `False`.

- [ ] **Step 7: Gameplay regression.**
  1. Run **Play Mode join** (A alone again, as in step 1).
  2. Fill the step 1 recorder with `__LABEL__` = `after`, run it, and wait for `SCRATCH\regression-after.txt`.
  3. Compare, ignoring the in-section time stamps:
     `Compare-Object (Get-Content "SCRATCH\regression-base.txt" | % { $_ -replace '\+\d+(\.\d+)?s ', '' }) (Get-Content "SCRATCH\regression-after.txt" | % { $_ -replace '\+\d+(\.\d+)?s ', '' })`
  4. **Pass rule:**
     - every `hit` line and every non-Burn total/count is identical;
     - `source=Burn` totals may differ by at most 1.0 and their counts may differ (burn ticks per frame);
     - a `fence` Zone count may differ by 1 (per-second cooldown against the 8 s life, sampled per physics step);
     - `pulled`, `gapToDummy`, `toEndGate` and `toStartGate` within 0.1 m.
  5. Anything else is a gameplay change: stop and report it.
  6. `unity command editor_stop`; poll; dirty check `False`.

- [ ] **Step 8: Static checks** (`BASE` from step 1). Each must print nothing:

```
git diff <BASE> -- Assets/Gameplay/Config/GameplayConfig.asset
git diff <BASE> -- Assets/Gameplay/Config/UiTheme.asset
git diff <BASE> -- Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset
git diff <BASE> -- Assets/Gameplay/Weapons
git diff <BASE> -- ProjectSettings/TimeManager.asset
```

  Then run the full edit-mode suite once more and report the totals. `git status`: nothing of yours left uncommitted.
  Don't commit captures; if `Assets/Temp/ability-visuals` was used, delete it and its `.meta`.

- [ ] **Step 9: Report** (no commit, unless the controller asks for a progress note):
  - every capture path, with what you saw in it;
  - the R1 and R2 facts;
  - the regression comparison output;
  - the five static diffs;
  - the test totals;
  - anything that didn't match.

---

## Self-review coverage

| Requirement (Tudor's request, brief, spec) | Task |
|---|---|
| Mines and portals look different; rough primitive prefabs | 3 (captures s3-own-mine-portal vs s1-before-mine-portal) |
| Team tint from the existing team colour source (mines, portal, cage) | 3, 4 (`theme.ShotColorFor`); 8 R1/R2 measured on the other client |
| Cage ultimate: walls with horizontal bars, no colliders, mechanic unchanged | 4 (structure test, `collidersUnderFence=0`); 1/8 guard + recorder `fence` |
| Flamethrower: a cone from the player at the real range and angle; hit shape unchanged; mismatch flagged | 5 (fan inside `ConeFilter` tested; tip-to-root measured); spec §5.4; 1/8 `flamethrower` |
| Grappling hook bigger and square, rope; size choice explained | 7 (visual-only Head scale, radius pinned in step 1) |
| Rocket explosion on the floor; why it floated; splash unchanged | 6 (floor ring, airburst covered, wall back-off); spec §2; 1/8 `rocket`, `cursor rocket` |
| Visual only: numbers, colliders, timings, RPCs, Room Properties, GameplayConfig unchanged | Rule 2, 10; 1 (guard tests); 8 (recorder comparison + static diffs) |
| Visual child vs gameplay collider separation | Plan preamble item 1: none exist; guard tests assert zero colliders |
| Driven by the same config values, never a second copy | 2 (`AbilityVisualGeometry`, getters); views read `Mine`/`Portal`/`ElectricFence`/`ExplodeOnImpact`/ability fields |
| Readable from the real camera (63°, 11.2 m, zoom) | Spec §1; captures at 616×576 in 1–7, zoom 1 and 1.6 in 4 |
| No per-frame allocations | 2 (`GroundSnap`/`VisualTint` static buffers), 5 (mesh rebuilt only on change), 7 (rope reused) |
| Prefabs next to the existing ability prefabs | Decisions [C]; file map |
| Edit-mode tests where pure | 1, 2 (geometry, ground snap), 3–7 (structure) |
| Play Mode capture per task, saved to SCRATCH and looked at | Rule 8; 1–8 |
| Commit per task, staging only its files | Last step of 1–7 |
| Two-client check with team tint on the other client, plus a damage regression | 8 |
| Mismatches and questions for Tudor | Spec §5; assumptions lines in 3–7 |
