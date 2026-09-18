# Arena Rebuild from Primitives: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to carry out this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal (Tudor, 2026-09-18):**
- **Primitives only.** The arena is rebuilt from Unity's built-in cubes and cylinders, with no imported meshes left in play.
- **Round towers.** Every objective is a round tower with **as many columns as its tier**.
- **Owner pieces.** Every tower has pieces (a crown on top and a cap on each column) that **change colour with the owner**.
- **Level bounces.** Shots, the Bounce burst above all, meet only flat, upright surfaces, so a bounce stays level.
- **Same map.** Zones, capture rings, spawns, wall lines and cover footprints stay where they are.

**Architecture:**
- **Pure rules, tested in edit mode:**
  - `OwnerPaint` (new, `Match/Rules`): what a zone's owner-coloured things show right now. It reads the same `CaptureRingState` the ring on the ground already gets every frame. The ring's edge moves onto the same rule.
  - `TowerLookRules` (new, `Arena`): columns per tier, and where each column stands.
- **One prefab, `Tower Look.prefab`:**
  - parts: plinth, drum, crown, four column slots (a shaft and a cap each), and one upright capsule collider;
  - `TowerLook` shows N columns and paints the crown and caps with a `MaterialPropertyBlock` on one shared Unlit material.
- **One layout home, `ArenaLayout.asset`:** rows (name, wall or block, centre, yaw, size) for the Source third, captured once from today's colliders, box for box.
- **The Editor builder,** `ArenaPrimitiveBuilder` (menu **OverPower › Arena › Build primitive arena**). It is re-runnable, idempotent, never runs in Play Mode and never saves from the menu. It:
  1. stamps a tower look on each of the ten tower objects and hides their house;
  2. moves the old art under a switched-off `Old Arena (off)`;
  3. builds Source from the layout and lays a flat floor;
  4. runs the existing Rebuild thirds and the minimap bake.
- **Networking: nothing new.**
  - The towers are the same networked objects with the same 21 scene PhotonViews.
  - The colour is drawn from replicated state every client already reads.
  - **No RPC added, renamed or removed.** No new room or player property.

**Tech stack:** Unity 6000.0.70f1 (URP 17.0.4, Mono), Photon PUN 2, NUnit edit-mode tests, the `unity` CLI.

**Naming rule:** tasks are **"arena step N"** (0 to 9) in reports, commits and `progress.md`. "T1-T4" / "Tier I-IV" mean zone tiers only.

**Provenance:**
- Written by a read-only opus planner. Every `file:line` was read at HEAD **`9266fdc`** (1030 tests).
- The arena is 4th in `Resources/loops/Limit Test/editor-queue.md`, after 2.7b step 11, the mark laser and damage numbers. Other commits land first. **Re-read every `file:line` before editing.**

---

## Tudor's words (verbatim)

> "we also need to remake the arena using primitives or i can install the probuilder package if that helps. i would
> like the objectives to be round towers that each have as many columns as their tiers and each should have a piece or
> multiple that would change color depending on who owns it."

**Why he wants it:** 2026-09-18, `progress.md:1916`, his answer 3: *"these assets mess with the bouncing of the burst projectile"*. It is a gameplay reason first. `assumptions-for-tudor.md:108-110` also parks the "team 0 near-white against white railings" question until this rebuild.

**Standing rule:** ProBuilder is not installed (`Packages/manifest.json` has no `com.unity.probuilder`). Nothing here relies on it (Open #1).

---

## What exists (verified at `9266fdc`)

### A. The arena in `Assets/Scenes/Game Scene.unity` (303 GameObjects, 65 prefab instances)
- **Root `Enviorment`** (`:24742`) holds:
  - `Houses` (`:21853`): the ten towers;
  - `Arena` (`:26129`), with `ArenaSymmetry`;
  - `Props` (power lines, outside the walls);
  - `Mountains` (`:35330`): 14 hills and mountains with MeshColliders, outside the walls;
  - six `Terrain1` tiles (four prefab instances of `Assets/Prefabs/Map/Terrain1.prefab`, two plain);
  - an almost empty `Nature` prefab instance.
- **All imported art.** Everything visible is LowPolyFPS2 art (`Assets/Sources/LowPolyFPS2 (1)/…`) on one atlas material (guid `6fb4cad7…`).
- **`Arena/Source (Team 2 third)`** has four groups:
  - **`Boundry`**: 22 wall pieces (`Wall_01`/`Wall_02`). Each has a BoxCollider 7 × 5.98 × 0.72 m, pivot on the outer face, local +Z facing into the arena.
  - **`Buildings`**: 4 houses (House_03 ×2, House_06L ×2).
  - **`Props`**: House_04R, House_05 (2), six "Cover Crate" `Box_02` (2.06 × 1.96 × 2.06 box at y-scale 1.34, so 2.62 m tall), and `Furniture_02 (8)` with 11 small props.
  - **`Nature`**: 8 `Plant_01` and 7 `Rock_03`, each with a MeshCollider, on Default.
- **Generated 120° / 240°** are copies.
- **Houses:** they collide through a BoxCollider; their own MeshColliders are off. Their `Door_01` children keep enabled non-convex MeshColliders, part of the "22 non-convex MeshColliders" (`progress.md:1917`).
- **Layers:** walls, houses and crates on Building (3); plants, rocks, furniture and terrain on Default (0). No static flags anywhere; every `m_StaticEditorFlags` is 0.

### B. The ten zones, and what "tier" means

| Zone | Tier | Scene object | World x, z | Radius (scene line) | Owner at start | Links |
|---|---|---|---|---|---|---|
| 0 | II | `Houses/House_05 (15)` (unpacked copy) | 35.59, 36.33 | 10 (`:24182`) | neutral | 6, 3, 5 |
| 1 | II | `House_05 (26)` (prefab instance) | 94.51, 36.33 | 10 (`:27031`) | neutral | 7, 3, 4 |
| 2 | II | `House_05 (13)` (prefab) | 65.05, 87.36 | 10 (`:13269`) | neutral | 8, 4, 5 |
| 3 | III | `House_05 (12)` (unpacked) | 65.05, 32.90 | 10 (`:35031`) | neutral | 0, 1 |
| 4 | III | `House_05 (19)` (prefab) | 82.75, 63.56 | 10 (`:22908`) | neutral | 1, 2 |
| 5 | III | `House_05 (18)` (prefab) | 47.35, 63.56 | 10 (`:27654`) | neutral | 0, 2 |
| 6 | I, team 0 | `Cathedral_Team0` (unpacked) | 15.11, 24.51 | 10 (`:35713`) | 0 | 0 |
| 7 | I, team 1 | `Cathedral_Team1` (prefab) | 114.99, 24.51 | 10 (`:505`) | 1 | 1 |
| 8 | I, team 2 | `Cathedral_Team2` (prefab) | 65.05, 111.00 | 10 (`:27824`) | 2 | 2 |
| 9 | IV | `House_05 (Centre)` (prefab) | 65.05, 53.34 | **8** (`:28397`) | neutral | 0, 1, 2 |

- **Geometry:**
  - Links come from `BuildingManager.TowerDictionary` (scene `:11752-11831`); capitals from `CathedralBuildingIDs` (`BuildingManager.cs:27`).
  - Distances from the centre: capitals 57.66 m, T2 34.02 m, T3 20.44 m. The T3s sit between two capitals.
  - All towers have scale 0.8 and are `BuildingCapture` + `PhotonView` + trigger `SphereCollider` + `AudioSource` on a House_05 mesh.
  - House_05's box is 5.72 × 7.16 × 5.74, so 4.58 × 5.73 × 4.59 m at 0.8.
- **What tier means in code:**
  - `BuildingCapture.tier` 1-4: "1 = Capital, 2 = Transition, 3 = Flanking, 4 = Centre" (`Building capture.cs:20-22`).
  - It selects a `TerritoryConfig` row (`TerritoryConfig.cs:40-47`; asset `:16-33`):

    | Tier | Capture time | Gold/s | Bounty | Regen/s |
    |---|---|---|---|---|
    | I | 20 s | 0 | 0 | 10 |
    | II | 15 s | 5 | 0 | 4 |
    | III | 10 s | 10 | 900 | 0 |
    | IV | 15 s | 8 | 1200 | 0 |

  - It also sets the minimap bubble size (36/24/24/30).
  - **Radius is per tower (`captureRadius`, `:17`), not per tier.**
- **The stale values, settled:**
  - Tower 9's radius is **8 m** (scene `:28397`). The "4.5 m" in `assumptions-for-tudor.md:138` is from before the symmetry work (the tower sat off-centre then).
  - T4 gold is **8** (`TerritoryConfig.asset:29`). "10" is the GDD's narrative text, never reconciled (`findings.md:359`).
  - This plan changes neither.

### C. Owner colour today
- **The carpets are the flags.** Each tower's owner "flag" is a `Carpet_03` prefab instance under the root `capture points`. The root sits at y 11.37 and the carpets at local y −5.66, so about 5.7 m up: on the old roofs.
  - Each carpet has a PhotonView (sceneViewIds 1-6, 14-16, 21) and no script.
  - They are `BuildingCapture.flagRenderer` and are in the symmetry triplets.
- **`ApplyOwnerVisual` swaps `flagRenderer.material`** between four materials (`Building capture.cs:891-908`). It is called on every client from `BuildingManager.Apply` for each changed zone (`BuildingManager.cs:497-501`) and from `RegisterCapture` (`:296-301`).
- **The ring edge colour** is in `CaptureRingView.cs:107-114`:
  - team colour, or dim white when neutral;
  - pulses to the drainer's colour while drained, otherwise to the warning colour while under attack;
  - `outOfPlayZoneColor` when out of play.
  - It is driven every frame on every client by `BuildingCapture.RefreshRingView` (`Building capture.cs:231-245`) from `CaptureRingState` (`Match/Rules/CaptureRingState.cs`). That includes `ZoneOutOfPlay` (`:331`) for 2.7b's cut capital.
- **The minimap** recolours bubbles in `MinimapView.cs:602-613`.
- **Theme tokens:**
  - team colours `teamShotColors` (`UiTheme.cs:246-253`: team 0 near-white, team 1 violet, team 2 cyan) via `ShotColorFor` (`:301-307`);
  - `captureRingNeutralColor` (`:446`, alpha 0.35), `captureRingWarningColor` (`:455`), `captureRingPulseSpeed` (`:458`);
  - `minimapNeutralColor` (`:503`), `outOfPlayZoneColor` (`:647`);
  - `CaptureRingGeometry.Pulse01` is 0 at t = 0 (`CaptureRingGeometry.cs:37-38`).

### D. The symmetry tool and the arena outline
- **`ArenaSymmetry` fields:**
  - `centre` (65.05, 0, 53.34) (scene `:26165`);
  - `snappedTriplets` (`:26170-26194`): six tower+carpet rows and two spawn rows;
  - `centred`: tower 9 and its carpet;
  - `sourceOutline`: 8 points (`:26198-26206`).
- **Runtime uses:**
  - At play it builds `ArenaBounds` for blink, portals and the out-of-arena safety net (`ArenaSymmetry.cs:148-157`).
  - `PathCrossesBoundary` counts only hits under a child named `Boundry` (`:29`, `:109-146`).
- **`ArenaSymmetryBuilder.Rebuild`** (`:36-105`):
  - deletes and re-copies every Source child into both thirds, unpacking prefabs and setting NotEditable;
  - refuses networked components under Source (`:215-241`);
  - `Validate` checks every `Boundry` BoxCollider's inner (+Z) face against the outline within 0.15 m (`:160-191`, the face at `:184`).
- **The menu:** "Rebuild thirds" also bakes the minimap (`ArenaSymmetryInspector.cs:36-44`).

### E. The minimap
- **Baked image:**
  - **OverPower › Arena › Bake minimap image** (`MinimapBaker.cs:30`, `:53-95`) renders top-down with `TopDownRender`. That camera is HideAndDontSave and never dirties the scene (`TopDownRender.cs:24`).
  - Output: `Assets/Gameplay/UI/ArenaMinimap.png`, plus `worldCentre`/`worldSizeMetres` in `MinimapConfig.asset:16-17` ((65.05, 53.34), 198.62 m).
  - Framing: the farthest mesh corner under Source and the thirds (`:163-176`), and a triangle pointing at the Tier I towers (`:104-130`).
- **Bubbles** are placed from tower positions (`BuildingManager.TryGetZoneCentre`, `:234-243`), not from the picture.

### F. Movement, bounds, ground and spawns
- **PlayerMotor:**
  - kill height from `GameplayConfig` (asset `:20`, −10) (`PlayerMotor.cs:133`, `:168-169`);
  - `CheckArenaBounds` uses `ArenaSymmetry.ActiveBounds` (`:224-246`);
  - the ground ray's mask is Default+Building (`:136`, `:249-250`).
- **`PlayerSpaceProbe.IsPathClear`** sweeps Building only, "and the floor itself is on Default" (`PlayerSpaceProbe.cs:63-79`).
- **Spawns** (`RoomManager`, scene `:1787-1794`). Each spawn faces its own capital.

  | Slot | Object | World x, z |
  |---|---|---|
  | `teamSpawnPoints[0]` | `Spawn Points/team (1)` | 19.95, 27.30 |
  | `[1]` | `team` | 110.15, 27.30 |
  | `[2]` | `team (2)` | 65.05, 105.42 |
  | `capitalUnderAttackSpawnPoints[0]` | `team (1) under attack` | 39.92, 38.83 |
  | `[1]` | `team under attack` | 90.18, 38.83 |
  | `[2]` | `team (2) under attack` | 65.05, 82.36 |

- **Respawn** writes `rb.position` directly, with no overlap test (`PlayerLifecycle.cs:716-725`, `PlayerDisplacement.cs:218-233`).
- **The only terrain reader** in code is `CaptureRingView.GroundHeight`, which falls back to the tower's own height (`CaptureRingView.cs:206-225`).

### G. Shots and layers
- **Layers:** Default 0, Building 3, DeadPlayer 6, Bullet 7 (`ProjectSettings/TagManager.asset`).
- **Every projectile's `hitMask` is 9** (Default + Building), e.g. `Burst Bullet - Bounce.prefab:253-255`.
- **Bounce:** `BounceOffWalls` reflects on `hit.normal` in 3-D (`BounceOffWalls.cs:55-68`). Any surface that isn't upright sends the shot up or down. Shots are horizontal, with the muzzle at about 1.97-1.99 m.
- **"X-Ray" is weapon 13** (`13 Laser - Through Walls.asset:16`). `IgnoreWalls` removes only Building (`IgnoreWalls.cs:29-37`), so today X-Ray **stops** on Default plants and rocks. There is no see-through render effect.
- **Building-only checks:**
  - rocket splash occlusion (`ExplodeOnImpact.cs:64-111`);
  - `WeaponFiring.buildingMask` (`:206`, `SafeMuzzlePosition`);
  - the aim cone clip (`AimConeView.cs:78`, `:301`);
  - the flamethrower and sonic pulse.
- **Laser warning line:** its length comes from `Hitscan.PredictBeamLength` (`Hitscan.cs:237`, `WeaponFiring.cs:678`).

### H. Camera
- `baseOffset` (0, 10, −5), FOV 60, `teamYawOffset` 120 (scene `:4815-4819`, `:4725`).
- Yaw comes from the direction to the spawn (`CameraTracking.cs:190-223`).
- Old heights: House_05 box 5.73 m, walls 5.98 m.

### I. The test range
- `TestRangeSpawner` places dummies along `teamSpawnPoints[0].forward` (`TestRangeSpawner.cs:83-103`). `testRangeEnabled: 1` (`GameplayConfig.asset:38`).
- By the scene numbers, that spawn faces capital 6. The 5 m dummy lands about 0.6 m from the capital's centre and the 25 m one past the pocket's back wall. This is pre-existing (Decision 20).

### J. Rendering and lighting
- URP `PC_RPAsset`: SRP Batcher on (`:71`), dynamic batching off (`:72`), shadow distance 50 (`:56`), GPU Resident Drawer off (`:85`).
- The directional light is realtime (scene `:5844`, `m_Lightmapping 4`) with soft shadows. There is no lighting data asset (`:96`).

### K. What the tests pin (1030 at `9266fdc`)
- **`ArenaSymmetrySceneTests`:**
  - `Validate` returns nothing;
  - every T1/T2/T3 tower **and its carpet** are snapped (`flagRenderer != null`), and T4 is centred;
  - spawns and towers are inside the outline.
- **`TerritoryAdjacencySceneTests`:** zone 9 links to {0, 1, 2}; each capital links to its own T2.
- **Constants:** `MinimapLayoutTests` and `RadialSymmetryTests` use centre (65.05, 53.34), capital radius 57.66 and yaw offset 120.
- **Preview scenes only:** `ArenaSymmetryBuilderTests`, `MinimapBakerTests`, `GroundSnapTests`.
- **Nothing pins** house names, wall meshes, terrain, or the minimap PNG.

---

## Where the code or docs differ from the brief
1. There are **ten** zones (0-9): three capitals, three T2s, three T3s and one T4.
2. Tower 9's radius is 8 m, and T4 pays 8 gold/s (section B). Radius is per tower, not per tier.
3. Today a tower's owner colour is a **carpet on its roof**. The houses and walls themselves never change colour.
4. The bounce offenders are the Default-layer plant/rock MeshColliders and the doors' non-convex MeshColliders. The houses' own boxes are upright already.
5. "X-ray" is weapon 13, which ignores the Building layer. No X-ray rendering exists.

---

## Decisions [C]

1. **Primitives only.**
   - Use the built-in `Cube.fbx`/`Cylinder.fbx` meshes (`Resources.GetBuiltinResource<Mesh>`), a `MeshRenderer`, and one collider the builder chooses.
   - **Never `GameObject.CreatePrimitive`:** it creates in the active scene (dirtying Game Scene) and adds a default collider.
   - The Cylinder's default is a CapsuleCollider with rounded ends. On a short, wide cylinder those ends reach shot height and would tilt a bounce.
2. **Keep the map.** None of these move or change parent:
   - the ten tower objects and their transforms, triggers and radii;
   - `TowerDictionary` and `CathedralBuildingIDs`;
   - both spawn arrays and the carpets;
   - `ArenaSymmetry.centre`, `sourceOutline` and its triplets.

   So every test in K stays green unchanged, and bubbles, spawns, camera yaw, bounds and territory rules need no change. A layout change is Open #3.
3. **Cover is today's colliders, box for box.** Each placement unit under the old Source becomes one block with exactly its collider's centre, yaw and size:
   - a BoxCollider for walls, houses and crates;
   - the mesh bounds for plants and rocks.

   The physics sightlines stay what they are today. Exceptions:
   - colliders on sub-parts (window frames, doors, the furniture's jugs) are dropped; they stick out 0.4 m at most;
   - anything whose centre is inside a tower's footprint is dropped (only `Furniture_02 (8)`, inside tower 2);
   - a house's visible roof above its box is gone, so views over low houses open slightly.
4. **One home for the layout: `Assets/Gameplay/Config/ArenaLayout.asset`.**
   - The builder owns every child of Source; its groups carry an `ArenaBuiltGroup` marker.
   - It **refuses** to build while Source holds anything else, so it can never delete old art or a hand-placed piece.
   - A designer edits rows in the Inspector, or moves blocks in the Scene view and presses **Capture layout from Source**, then presses **Build primitive arena**.
   - `ArenaSymmetry`'s comment and help box are updated to say so.
5. **Towers keep their networked objects; the house is hidden, not removed.**
   - On the tower object: its `MeshRenderer` is disabled, its non-trigger colliders are disabled, and its old child parts are deactivated. Every change is recorded with `PrefabUtility.RecordPrefabInstancePropertyModifications`, otherwise a scripted change to a prefab instance can be lost on save (the trap in `ArenaSymmetryBuilder.cs:281-283`).
   - The look is a child `Tower Look`, stamped from the prefab and then **unpacked** (plain objects, like the generated thirds). No per-tower prefab override exists to be lost, and re-running the builder re-stamps it.
   - Its local scale is `1 / tower.lossyScale` (the towers are 0.8), so the look is authored and checked in true metres.
   - No new PhotonView: the same 21 sceneViewIds.
6. **The shape, in true metres** (starting values [C]; they live in the prefab):

   | Part | Radius | Height span |
   |---|---|---|
   | Plinth | 2.6 m | 0-0.4 m |
   | Drum | 2.0 m | 0.4-5.0 m |
   | Crown | 2.35 m | 5.0-5.35 m |
   | Four column slots, on a 2.25 m ring: shaft | 0.35 m | 0.4-5.4 m |
   | Four column slots, on a 2.25 m ring: cap | 0.45 m | 5.4-5.7 m |

   - The top at 5.7 m matches today's House_05 box (5.73 m), so the camera sees no more occlusion than now.
   - **One collider for the whole tower:** an upright CapsuleCollider, radius 2.6, straight from −1 m to +7 m (height 13.2, centre y 3), on Building. The plinth, drum, crown, shafts and caps have none.
   - So every tower gives the same cover whatever its tier, and the columns add none.
   - A shot at any height meets a vertical wall. The rounded ends sit below the floor and far above anything that flies.
   - It is also exact and cheap.
7. **Columns = tier:** I → 1, II → 2, III → 3, IV → 4. This matches the numeral on the minimap bubble.
   - The mapping is one function, `TowerLookRules.ColumnsForTier` (Open #2).
   - Columns are evenly spaced, with the first facing the tower's local +Z. Towers are snapped by rotation, so every third looks the same.
   - The centre's four columns aren't three-fold symmetric, but only in looks: its collider is round.
   - **One prefab with four slots, not one prefab per tier:** four prefabs would be four copies of the same sizes.
8. **The owner colour.**
   - The crown and the caps of the shown columns share `Tower Owner.mat` (URP **Unlit**, so a shadow never darkens a team colour). Each is tinted through one reused `MaterialPropertyBlock` (`_BaseColor`), so there is **no material copy per tower**.
   - The colour is `OwnerPaintColours.For(OwnerPaint.From(state), theme, theme.towerNeutralColor, time)`.
   - `state` is the very `CaptureRingState` that `BuildingCapture.RefreshRingView` already builds every frame on every client. A late joiner is right on the first frame, and no event or network traffic is added.
   - It deliberately does **not** listen to `BuildingManager.OwnershipChanged`. That event isn't raised for the snapshot a client reads on joining (`BuildingManager.cs:129-133`) and carries no under-attack, drain or out-of-play state.
   - What each state shows:

     | State | Colour |
     |---|---|
     | Owned | `ShotColorFor(owner)` |
     | Neutral | new `UiTheme.towerNeutralColor` (0.42 grey [C]) |
     | Under attack | pulses to `captureRingWarningColor` at `captureRingPulseSpeed` |
     | Draining | pulses to the drainer's colour |
     | Out of play (2.7b's cut capital) | `outOfPlayZoneColor` |
     | Capturing or contested | no change; the ring shows it (Open #7) |

     The neutral colour is not the ring's: the ring's neutral is a see-through white, which on an opaque piece reads as team 0.
   - The block is written only when the colour changes, so only a pulsing tower writes it every frame.
9. **The ring's edge moves onto the same rule.** `CaptureRingView.cs:107-114` becomes one call with `theme.captureRingNeutralColor`.
   - Behaviour is identical by construction, and it is now tested.
   - The tower and the ring can't drift apart.
10. **The carpets:** only their renderers are hidden (`flagRenderer.enabled = false`), otherwise they would float at 5.7 m. Their objects, PhotonViews and `flagRenderer` links stay, so `ApplyOwnerVisual` and the snapped-carpet test are untouched.
11. **Layers.**
    - Every solid piece is on **Building**: walls, blocks and the tower collider.
    - The **floor stays on Default**, like the terrain. A Building floor would make every dash start "inside a wall" (`PlayerSpaceProbe.cs:63-65`).
    - Where plants and rocks were, behaviour changes. X-Ray now passes through; rocket splash, the aim cone, the flamethrower, the sonic pulse and placement paths now stop there.
    - It is consistent: a block is a wall (Open #5).
12. **The floor** is one Cube slab, 220 × 4 × 220 m, with its top at y 0, centred on the arena centre, on Default.
    - It stays outside Source; inside, Rebuild would copy it three times.
    - It replaces the six terrain tiles. The rings fall back to the tower's height (y 0 or −0.02), which puts them 0.04-0.06 m above the floor.
    - `Mountains`, `Props` (the power lines) and `Nature` go to the old art (Open #4).
13. **The old art is moved, never deleted** (until Tudor says so). `Enviorment/Old Arena (off)` (inactive) gets:
    - the old Source groups;
    - both generated thirds' old copies, moved out before Rebuild could delete them;
    - the terrain tiles, `Mountains`, `Props` and `Nature`.

    **Rollback:** move them back, re-enable the tower houses and carpets, delete the `Tower Look` children and `Arena Floor`, and press Rebuild thirds. Deletion is step 9 (Open #8).
14. **Scene edits go through the builder only.** No hand placement, no scene YAML edits.
    - It refuses in Play Mode, and the menu never saves (like Rebuild thirds).
    - The agents' scratch entry builds, validates and saves in **one** `run_script` call, then reads `isDirty` in a separate call.
    - **Idempotent** means a second run gives the same content dump. Byte equality is impossible: Rebuild thirds already re-creates the copies with new file ids on every run.
15. **Lighting stays as it is:** a realtime directional light with soft shadows. No bake, so nothing needs re-baking after a layout change.
16. **Draw calls.**
    - The SRP Batcher is on (`PC_RPAsset.asset:71`).
    - Every wall, block, floor and tower-stone renderer gets **`BatchingStatic`**. Nothing moves at runtime, and Instantiate keeps the flag on the copies (step 5 tests it).
    - Owner pieces are not static. Their property block takes them out of the SRP Batcher: about 32 renderers (10 crowns and 22 caps).
    - GPU instancing is not enabled: for URP Lit the SRP Batcher takes precedence.
    - Measured before and after at three spots (steps 0, 3 and 5).
17. **The minimap** is re-baked by the builder (`MinimapBaker.Bake`).
    - Bubbles follow tower positions, which don't move, so they stay aligned.
    - `MinimapConfig.worldSizeMetres` may shift a little, because it follows the farthest wall corner.
18. **Five new materials** in `Assets/Gameplay/Arena/Materials/`. The colours are [C], tuned in step 7 by pixel sampling, and team colours never go in a material:

    | Material | Shader | Base colour |
    |---|---|---|
    | `Arena Floor` | Lit | 0.36/0.37/0.39 |
    | `Arena Wall` | Lit | 0.24/0.25/0.28 |
    | `Arena Block` | Lit | 0.52/0.53/0.56 |
    | `Tower Stone` | Lit | 0.30/0.28/0.26 |
    | `Tower Owner` | Unlit | 0.42 grey, which is only what shows before Play |

19. **No renames** of scene objects (`Houses`, `House_05 (12)`, `Cathedral_Team0`…). They would be cosmetic, would muddy the diff, and old docs use the names.
20. **The test range isn't moved.** Its dummy placement (section I) is pre-existing and doesn't change with this plan. Step 0 measures it, step 6 re-checks it, and it goes to the controller as a separate small fix.

---

## Open for Tudor (each has a default; nothing is blocked)

1. **ProBuilder: not needed.**
   - **What it gives:** a modelling tool inside Unity for shapes the basic blocks can't make, such as ramps, arches, bevelled or curved walls, or a tower with a real doorway.
   - **What it costs:** one more package on every machine and build. Its shapes are custom meshes saved inside the scene, which makes big, hard-to-review scene changes. A custom mesh also needs a mesh collider, the kind that throws off the Bounce shots today.
   - Everything here (walls, blocks, round towers, columns) is Unity's built-in cube and cylinder.
   - **Default:** don't install it. Worth another look if a later pass wants ramps, arches or curved walls.
2. **Which way do the columns count?** **Default:** as the minimap numerals: capitals (I) get 1 column, Tier II 2, Tier III 3, the centre (IV) 4. The alternative is reversed, so your capital is the grandest; it's one line to flip (arena step 3).
3. **Same map, new look?** **Default:** every zone, ring, spawn, wall line and piece of cover stays put. Houses, crates, rocks and bushes become plain blocks of the same size, so the playtest compares like with like. A new layout would be its own pass after a playtest.
4. **The ground and the scenery.** **Default:** a flat grey floor replaces the orange sand, and the mountains and power lines outside the walls are switched off. Near-white, violet and cyan read far better on grey; the sand is what made team 0 hard to see.
5. **Cover acts like a wall everywhere.** Today rocks and bushes stop the X-Ray laser but let rocket splash through, the opposite of walls. **Default:** every block behaves like a wall: X-Ray passes through, splash doesn't, and Bounce shots bounce flat off it.
6. **Tower size.** **Default:** every tower is the same size (the same cover at every zone); only the columns show the tier. The alternative is bigger towers for higher tiers (more cover at the centre).
7. **A tower being captured.** **Default:** it stays grey until the capture completes, and the ring on the ground shows the progress. It flashes red while an enemy is inside your zone and flashes the attacker's colour while being drained, exactly like the ring. The alternative is a crown that fills with the capturer's colour.
8. **When the old art goes.** **Default:** switched off, not deleted, until you've played on the new arena; then deleted in one commit.

---

## Rules for every task

1. **Read first:** `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\HANDOFF.md` §5 (the traps) and CODING-STANDARDS §6.
2. **Branch:** `limit-testing` only; push after each commit.
3. **The Editor.**
   - **Take the lock** before the first `unity` command: `powershell -NoProfile -ExecutionPolicy Bypass -File "C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\editor-lock.ps1" take`. **Release** it after the last one, with Play Mode stopped and the scene not dirty.
   - A refused take means stop and report.
   - `unity command editor_status` must answer. **Never `editor_focus`.**
4. **Compiling and tests:** never recompile, run tests, build or edit `.cs` in Play Mode.
   - `recompile`, then poll `recompile_status`; it is the only compile truth.
   - Tests are async only: `run_tests -- --mode editor --async_tests true`, then poll `test_status`.
5. **Dirty scene → modal dialog → a silent Editor hang.**
   - Before tests, a recompile, a build, Play Mode or opening a scene, eval `return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;`.
   - **Read it on its own; never chain it with `&&`.** Continue only on `False`.
   - To discard: `EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", OpenSceneMode.Single)`.
6. **The scene gate.**
   - **Steps 1, 2, 4, 6 (unless a fix) and 7:** `if git diff --quiet <previous step's commit> -- "Assets/Scenes/Game Scene.unity"; then echo UNCHANGED; else echo CHANGED; fi` must print UNCHANGED.
   - **Steps 3, 5 and 9** change it on purpose:
     - after the build and save, record `git hash-object "Assets/Scenes/Game Scene.unity"` as **SCENE_SAVED**;
     - after **every** Play Mode session, hash it again; it must equal SCENE_SAVED. Unity has autosaved into this scene before.
     - **Reload from disk before the tests** (`OpenScene … Single` on the clean scene), so the tests see what was saved, not what is in memory.
7. **Building rules.**
   - The scene changes only through `ArenaPrimitiveBuilder`.
   - Temporary objects (prefab authoring, tests) live only in a **preview scene**: `EditorUtility.CreateGameObjectWithHideFlags` plus `SceneManager.MoveGameObjectToScene`, as `ArenaSymmetryBuilderTests.cs:45-57` does. Never `CreatePrimitive` or `new GameObject` in the open scene.
8. **Before any Play Mode measurement,** list the room's actors (the HUD plan's rule 7 command). Expected: `1 | <n>:EditorHost(me)`; anything else means stop. Note the local team.
9. **In-process only; Tudor is using this computer.**
   - Move with `PlayerDisplacement.TeleportTo`, aim with `PlayerAim.SetAimOverride`, fire with `WeaponFiring.TryFire()`, from `run_script` drivers in the pattern of `SCRATCH\cone-measure\TestFireCoroutine.cs`.
   - Never the real mouse or keyboard. Time with game time, in coroutines that write their results to a file.
10. **SCRATCH** = `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\ff89ef31-6781-4dad-a39f-d996ebdaf3fa\scratchpad\arena-rebuild\`. Captures go in `…\arena-rebuild\captures\`. Never under `Assets/`.
11. **Captures.**
    - The Game view is **1920×1080**; never change it. The CLI writes **1280×720** PNGs, so say so and compare proportions.
    - HUD shots need `--source screen`. Always give an explicit save path, then copy the file out of `Temp`/`Assets/Temp` and delete it there.
    - **Read every PNG yourself** and describe it honestly. Back colour claims with pixel samples.
12. **Designer values:** every one lives on an asset with a plain `[Tooltip]`, in one home. Comments say *why*, for a designer reader.
13. **`UiTheme.asset` is hand-edited**, one YAML line per new field (the HUD plan's rule 10). Never `SetDirty` + `SaveAssets` on it. Save other assets with `AssetDatabase.SaveAssetIfDirty` only.
14. **Must stay byte-unchanged in every step:**
    - `GameplayConfig.asset`, `TerritoryConfig.asset`, `Assets/Resources/Multiplayer Player.prefab`, `PhotonServerSettings.asset` (its RpcList must match `rpclist-base.txt`);
    - anything under `Assets/Sources/`. **Never apply overrides to `House_05.prefab`.**
    - Also: no RPC added, renamed or removed; `PlayerNetSync` stays the only `IPunObservable`; the scene keeps exactly 21 PhotonViews with sceneViewIds 1-21, each once, on the same objects.
15. **Serialized fields:** this plan adds none to an existing prefab or scene component. If one becomes necessary, set it through `SerializedObject`, save, and grep the YAML (CODING-STANDARDS §6).
16. **Commits:** run `git status --short` first and stage only the step's files. Use the session attribution line. No unmeasured number in a message.
17. **Assumptions:** add `[C]` lines under `## Arena rebuild (2026-09-18)` at the end of `assumptions-for-tudor.md` (outside the repo, never committed), then list its `## ` headings.
18. **Line drift:** other agents commit on this branch. Re-read every `file:line` before editing.

---

## File map

| File | Responsibility | Step |
|---|---|---|
| `Assets/scripts/Match/Rules/OwnerPaint.cs` (create) | Pure: what a zone's owner pieces show | 1 |
| `Assets/Tests/OwnerPaintTests.cs` (create) | +7 | 1 |
| `Assets/scripts/Arena/TowerLookRules.cs` (create) | Pure: columns per tier, column yaws | 1 |
| `Assets/Tests/TowerLookRulesTests.cs` (create) | +4 | 1 |
| `Assets/scripts/Match/OwnerPaintColours.cs` (create) | `OwnerPaint` → a theme colour (pulses) | 2 |
| `Assets/scripts/Match/CaptureRingView.cs` | Edge colour via `OwnerPaintColours` | 2 |
| `Assets/scripts/Arena/TowerLook.cs` (create) | Columns shown; crown and cap paint | 2 |
| `Assets/scripts/Player/Building capture.cs` | Find `TowerLook`, `Bind`, `Refresh` in `RefreshRingView` | 2 |
| `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset` | `towerNeutralColor` (1 line) | 2 (maybe 7) |
| `Assets/Gameplay/Arena/Tower Look.prefab` (create) | The tower | 2 |
| `Assets/Gameplay/Arena/Materials/Tower Stone.mat`, `Tower Owner.mat` (create) | | 2 (tuned 7) |
| `Assets/Tests/TowerLookPrefabTests.cs`, `Assets/Tests/OwnerPaintColoursTests.cs` (create) | +4, +2 | 2 |
| `Assets/scripts/Editor/Arena/ArenaPrimitiveBuilder.cs` (create) | Towers phase (3); old art, Source, floor (4); `BuildAll` and menus (5); `DeleteOldArt` (9) | 3, 4, 5, 9 |
| `Assets/Tests/ArenaPrimitiveSceneTests.cs` (create) | +3 (3), +3 (5) | 3, 5 |
| `Assets/Scenes/Game Scene.unity` | Towers (3); the arena (5); old art deleted (9) | 3, 5, 9 |
| `Assets/scripts/Data/ArenaLayout.cs` (create) | Layout rows, materials, floor | 4 |
| `Assets/scripts/Arena/ArenaBuiltGroup.cs` (create) | Marker for builder-owned groups | 4 |
| `Assets/scripts/Editor/Arena/ArenaLayoutCapture.cs` (create) | Colliders → rows | 4 |
| `Assets/Gameplay/Config/ArenaLayout.asset` (create) | The layout (captured once) | 4 |
| `Assets/Gameplay/Arena/Materials/Arena Floor.mat`, `Arena Wall.mat`, `Arena Block.mat` (create) | | 4 (tuned 7) |
| `Assets/Tests/ArenaPrimitiveBuilderTests.cs` (create) | +5 (preview scenes) | 4 |
| `Assets/Gameplay/UI/ArenaMinimap.png`, `Assets/Gameplay/Config/MinimapConfig.asset` | Re-baked | 5 |
| `Assets/scripts/Arena/ArenaSymmetry.cs`, `Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs` | Workflow comment, help box, Build button | 5 |
| `Assets/scripts/BuildingManager.cs` | Stale comment `:317-322` ("rebuilt … in a separate session") | 5 |
| SCRATCH (never committed) | `ArenaFacts.cs`, `ArenaDump.cs`, `PerfProbe.cs`, `BounceProbe.cs`, `TowerColourProbe.cs`, `MakeTowerLook.cs`, `CaptureLayout.cs`, `BuildAndSave.cs`, `SplashXRayProbe.cs` | all |

---

### Step 0: start state and the BEFORE record (no commit)

- [ ] **1. Editor, lock and tree.**
  - Take the lock.
  - `editor_status` shows stopped and not compiling. The dirty check reads False.
  - `git status --short` is clean.
  - `git rev-parse HEAD` is **BASE**.
  - Save the RpcList: `git show BASE:Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset | grep -A400 RpcList > SCRATCH/rpclist-base.txt`.
  - Run the tests: **BASE TESTS** (1030 at `9266fdc`, plus whatever has landed since). Stop on any failure.
- [ ] **2. Facts (edit mode, read-only).** `ArenaFacts.Run` (`run_script`) writes sorted lines to `facts-before.txt`:
  - per `BuildingCapture`: id, tier, `captureRadius`, world position (mm), yaw, lossy scale, carpet name;
  - both spawn arrays: name, world position, yaw;
  - `ArenaSymmetry`: centre, outline, and `ArenaSymmetryBuilder.Validate` (expect empty);
  - every PhotonView: sceneViewId and path (expect 21, ids 1-21);
  - enabled colliders on active objects, counted by type and layer; every active MeshCollider path;
  - `MinimapConfig` centre and size.

  Dirty check afterwards: False.
- [ ] **3. Top-down render.** Eval:
  ```
  System.IO.File.WriteAllBytes(@"<SCRATCH>\captures\topdown-before.png",
    Overpower.EditorTools.TopDownRender.RenderPng(new UnityEngine.Vector2(65.05f, 53.34f), 200f, 1024)); return "ok";
  ```
  Dirty check: False.
- [ ] **4. Play Mode: the BEFORE record.**
  - Play, join as `EditorHost`, run the actor check (rule 8) and note the team.
  - **The spots.** At each: `TeleportTo`, wait 20 physics steps, read `rb.position`. If `Physics.CheckCapsule` against Building overlaps, step 1 m toward the centre and say so. Keep the final points in `facts-before.txt`.
    - **S1:** your own team spawn.
    - **S2:** 6 m from (65.05, 53.34) toward your capital.
    - **S3:** (65.05, 37.90), 5 m inside zone 3 toward the centre.
  - **Captures** (`--source screen`): `before-S1.png`, `before-S2.png`, `before-S3.png`. Then `MinimapView.Local.ToggleLarge()` for `before-map-large.png`, and close it again.
  - **Performance** (`PerfProbe`): at each spot, wait 30 frames, then average `UnityEditor.UnityStats` batches, setPassCalls, triangles and vertices over 60 frames. Confirm the property names by reflection first. Write `perf-before.txt`.
  - **Bounce baseline** (`BounceProbe`):
    - `PlayerLoadout.SetWeapon(7)` (`07 Burst - Bounce.asset:15`).
    - Fire 20 pulls at each target, aimed with `SetAimOverride`. Every frame, record each live `ProjectileMotor` that has `BounceOffWalls`, and log each change of `Direction`: position, new direction and |y|.
    - **B1:** a `Boundry` wall of your third, from 8 m at 45° to its face.
    - **B2:** a house face with a door, in your third.
    - **B3:** the nearest `Rock_03` or `Plant_01`.
    - **B4:** tower 9's house.
    - Report per target: redirects, how many have |y| > 0.01, and the largest |y|. **This is Tudor's complaint in numbers.** Save the targets' world points to reuse in step 6.
  - **Ring edges:** read `Capture Ring/Edge` `startColor` by reflection for tower 6 (owned) and tower 9 (neutral) into `ring-before.txt`.
  - **Dummies:** for every `DummyTarget`, record its position, whether it overlaps Building (`CheckCapsule`), and whether a 1.98 m ray from the team-0 spawn reaches it.
  - **Finish:**
    - `editor_stop`, poll until stopped.
    - The dirty check reads False. The scene gate prints UNCHANGED against BASE.
    - Release the lock.
  - **Read every PNG yourself** and write two honest sentences per capture.

---

### Step 1: the pure rules (red first)

**Files:** `OwnerPaint.cs`, `TowerLookRules.cs`, `OwnerPaintTests.cs`, `TowerLookRulesTests.cs` (all new).

- [ ] **1. Red.** Write the tests. They must fail to compile with `CS0246 … 'OwnerPaint'`; report that exact message. Build states with `new CaptureRingState(phase, fill01, arcTeam, outlineTeam, drainerTeam, underAttack, outOfPlay)`.
  - **`OwnerPaintTests`** (7):
    - `AnOwnedZoneShowsItsOwnerAndDoesNotPulse`: (Idle, owner 1) → Base Team, Team 1, no pulse.
    - `ANeutralZoneShowsNeutral`: owner −1 → Base Neutral, Team −1.
    - `OutOfPlayWinsOverOwnerAttackAndDrain`: (Draining, owner 1, drainer 0, under attack, out of play) → Base OutOfPlay, no pulse.
    - `AnAttackedOwnedZonePulsesToTheWarning`: (Idle, owner 2, under attack) → Team 2, `PulsesToWarning`.
    - `ADrainPulsesToTheDrainerNotTheWarning`: (Draining, owner 0, drainer 1, under attack) → Team 0, `PulseTeam` 1, not warning. This mirrors `CaptureRingView.cs:109-114`, where the drain check comes first.
    - `ACaptureInProgressLeavesANeutralZoneNeutral`: (Capturing, arc 2, owner −1) → Neutral, no pulse.
    - `APausedCaptureChangesNothing`: (Paused, arc 1, owner 0) → Team 0, no pulse.
  - **`TowerLookRulesTests`** (4):
    - `EachTierShowsItsOwnNumberOfColumns`: 1..4 → 1..4.
    - `AnOutOfRangeTierIsClampedNotThrown`: 0 and −3 → 1; 5 → 4.
    - `ColumnsAreEvenlySpaced`: (1,3) → 120, (2,3) → 240, (1,2) → 180, (3,4) → 270.
    - `TheFirstColumnAlwaysFacesTheFront`: index 0 → 0 for counts 1..4.
- [ ] **2. `OwnerPaint`** (`namespace Overpower.Match`, pure):
  ```csharp
  public enum OwnerPaintBase { Neutral, Team, OutOfPlay }

  /// <summary>What a zone's owner-coloured things show right now - the capture ring's edge on the ground and a tower's
  /// crown and column caps (arena step 1). Worked out from the CaptureRingState every client already builds each
  /// frame from replicated state, so the ring and the tower can never disagree and a late joiner is right at once.
  /// OwnerPaintColours turns it into a colour.</summary>
  public readonly struct OwnerPaint
  {
      public readonly OwnerPaintBase Base;
      /// <summary>The owner when Base is Team; -1 otherwise.</summary>
      public readonly int Team;
      /// <summary>An owned zone with an enemy inside (or just gone): pulse to the warning colour.</summary>
      public readonly bool PulsesToWarning;
      /// <summary>The team draining this zone, which the paint pulses to; -1 when nobody drains it.</summary>
      public readonly int PulseTeam;

      public OwnerPaint(OwnerPaintBase paintBase, int team, bool pulsesToWarning, int pulseTeam)
      { Base = paintBase; Team = team; PulsesToWarning = pulsesToWarning; PulseTeam = pulseTeam; }

      public static OwnerPaint From(CaptureRingState state)
      {
          if (state.OutOfPlay)
              return new OwnerPaint(OwnerPaintBase.OutOfPlay, TerritoryMap.Neutral, false, TerritoryMap.Neutral);

          bool owned = state.OutlineTeam >= 0;
          var paintBase = owned ? OwnerPaintBase.Team : OwnerPaintBase.Neutral;
          int team = owned ? state.OutlineTeam : TerritoryMap.Neutral;

          // The order the ring has always drawn: a drain pulses in the drainer's colour; only otherwise does "under
          // attack" pulse to the warning (CaptureRingView before arena step 2).
          if (state.Phase == CaptureRingPhase.Draining && state.DrainerTeam >= 0)
              return new OwnerPaint(paintBase, team, false, state.DrainerTeam);
          if (state.UnderAttack)
              return new OwnerPaint(paintBase, team, true, TerritoryMap.Neutral);
          return new OwnerPaint(paintBase, team, false, TerritoryMap.Neutral);
      }
  }
  ```
- [ ] **3. `TowerLookRules`** (`namespace Overpower.Arena`, pure, `System.Math` only):
  ```csharp
  public static class TowerLookRules
  {
      public const int MaxColumns = 4;

      /// <summary>Tudor, 2026-09-18: "round towers that each have as many columns as their tiers". Tier I (a capital)
      /// shows 1 column, Tier IV (the centre) 4 - the numeral the minimap prints on the zone's bubble. The one line
      /// to change if he counts the other way (plan Open #2). Out-of-range tiers are clamped, never thrown.</summary>
      public static int ColumnsForTier(int tier) => Math.Max(1, Math.Min(MaxColumns, tier));

      /// <summary>Columns stand evenly round the tower; the first faces the tower's own front (+Z).</summary>
      public static float ColumnYawDegrees(int index, int count) => 360f * index / Math.Max(1, count);
  }
  ```
- [ ] **4. Green.** Expect **BASE TESTS + 11**.
- [ ] **5. Play Mode smoke.** Nothing uses the new code yet. Join, run the actor check and capture `s1-smoke.png` at S1: it matches `before-S1.png`. The scene gate prints UNCHANGED.
- [ ] **6. Commit** the four files: `feat(arena): arena step 1 - the owner-paint and tower-column rules (pure, tested)`.

---

### Step 2: the tower look prefab and the owner colour code (scene unchanged)

**Files:** `OwnerPaintColours.cs`, `TowerLook.cs`, `Tower Look.prefab`, `Tower Stone.mat`, `Tower Owner.mat`, `TowerLookPrefabTests.cs` and `OwnerPaintColoursTests.cs` (all new); `CaptureRingView.cs`, `Building capture.cs`, `UiTheme.cs`, `UiTheme.asset`.

- [ ] **1. The theme field.** At the end of `UiTheme`'s fields, after `matchLiveTwoTeamsToastText`:
  ```csharp
        [Header("Towers (arena rebuild)")]
        [Tooltip("Colour of a tower's crown and column caps while nobody owns its zone. Owned towers use their team's " +
                 "colour (Team Shot Colors), a capital cut from a two-team match uses Out Of Play Zone Colour, and an " +
                 "attacked or drained tower pulses exactly like its ring on the ground. Keep it a plain mid grey: " +
                 "team 0 is near-white, so a light neutral would read as owned by team 0.")]
        public Color towerNeutralColor = new Color(0.42f, 0.42f, 0.42f, 1f);
  ```
  Recompile. Append `  towerNeutralColor: {r: 0.42, g: 0.42, b: 0.42, a: 1}` after the asset's last line (`UiTheme.asset:211`). Force-import, read the value back, and check that `git diff` shows exactly **1** added line.
- [ ] **2. Red: the tests** (6). They fail to compile on `TowerLook` and `OwnerPaintColours`.
  - **`TowerLookPrefabTests`** (loads `Assets/Gameplay/Arena/Tower Look.prefab`):
    1. `TheLookHasACrownAndFourColumnSlotsEachWithAShaftAndACap`. The crown and every cap share `Tower Owner.mat` and are not static. The plinth, drum and shafts share `Tower Stone.mat` and are `BatchingStatic`.
    2. `OnlyOneColliderAndItIsAnUprightCapsuleOnTheBuildingLayer`. No MeshCollider, no PhotonView and no `MonoBehaviourPun` anywhere in the prefab.
    3. `TheCapsuleIsStraightFromBelowTheFloorToAboveTheCaps`: `center.y − (height/2 − radius) ≤ −0.5`, and `center.y + (height/2 − radius)` ≥ the highest renderer-bounds top.
    4. `ApplyColumnsShowsExactlyThatManyEvenlySpaced`: an instance in a preview scene. For 1..4: the active slot count, each slot's yaw from `TowerLookRules`, and each slot's distance equal to `columnRingRadius`.
  - **`OwnerPaintColoursTests`** (a `ScriptableObject.CreateInstance<UiTheme>()`):
    5. `OwnedNeutralAndOutOfPlayMapToTheThemeColours` at time 0, where `Pulse01` is 0.
    6. `AtThePulsePeakAnAttackedZoneShowsTheWarningAndADrainShowsTheDrainer` at `t = 0.5 / captureRingPulseSpeed`.
- [ ] **3. `OwnerPaintColours`** (`Assets/scripts/Match/`, next to `CaptureRingView`):
  ```csharp
  /// <summary>Turns an OwnerPaint into the colour to draw. One home for "which theme colour, and how it pulses", shared
  /// by the capture ring's edge (neutral = the ring's dim white) and a tower's crown and caps (neutral = Tower Neutral
  /// Colour), so the two can never drift apart.</summary>
  public static class OwnerPaintColours
  {
      public static Color For(OwnerPaint paint, UiTheme theme, Color neutral, float timeSeconds)
      {
          Color colour = paint.Base == OwnerPaintBase.OutOfPlay ? theme.outOfPlayZoneColor
                       : paint.Base == OwnerPaintBase.Team ? theme.ShotColorFor(paint.Team) : neutral;
          if (paint.PulseTeam >= 0)
              return Color.Lerp(colour, theme.ShotColorFor(paint.PulseTeam),
                                CaptureRingGeometry.Pulse01(timeSeconds, theme.captureRingPulseSpeed));
          if (paint.PulsesToWarning)
              return Color.Lerp(colour, theme.captureRingWarningColor,
                                CaptureRingGeometry.Pulse01(timeSeconds, theme.captureRingPulseSpeed));
          return colour;
      }
  }
  ```
- [ ] **4. The ring.** Replace `CaptureRingView.cs:107-114` with `Color edgeColor = OwnerPaintColours.For(OwnerPaint.From(state), theme, theme.captureRingNeutralColor, time);`. Keep the comment's 2.7b sentence.
- [ ] **5. `TowerLook`** (`Assets/scripts/Arena/`, `Overpower.Arena`, `[DisallowMultipleComponent] public sealed class`):
  - **Class comment:** Tudor's quote; four slots with the first N shown; `ApplyColumns` runs at edit time from the builder, so nothing is built at runtime; one shared Unlit material tinted through a property block; `Refresh` gets the same state as the ring; the pieces are grey before Play; no collider depends on the tier.
  - **Fields, each with a plain `[Tooltip]`:** `Transform[] columnSlots` (4), `Renderer[] columnCaps` (4), `Renderer crown`, `float columnRingRadius = 2.25f` ("keep a column's outer edge inside the tower's collider radius so columns never add cover").
  - **Methods:**
    - `ApplyColumns(int count)`: activates slots `[0, count)`, places each at `AbilityVisualGeometry.CirclePoint(columnRingRadius, TowerLookRules.ColumnYawDegrees(i, count))`, turns it to that yaw, and deactivates the rest.
    - `int ShownColumns` (counts active slots).
    - `Bind(UiTheme theme)`.
    - `Refresh(CaptureRingState state, float timeSeconds)`: returns if not bound; computes the colour; if it changed, writes `_BaseColor` through one cached `MaterialPropertyBlock` (`GetPropertyBlock`/`SetPropertyBlock`, the `VisualTint.SetMeshColor` pattern) on the crown and each **active** cap.
    - `Color ShownColor` (diagnostic).
  - **Never** touch `.material`.
- [ ] **6. The materials and the prefab** (`SCRATCH\MakeTowerLook.cs`, `run_script`, edit mode):
  - Materials: `new Material(Shader.Find("Universal Render Pipeline/Lit"))` for `Tower Stone` (base 0.30/0.28/0.26) and `…/Unlit` for `Tower Owner` (base 0.42 grey). Create each with `AssetDatabase.CreateAsset`.
  - A preview scene. Every object comes from `CreateGameObjectWithHideFlags(…, HideAndDontSave)`, is moved in with `MoveGameObjectToScene`, and is parented.
  - Meshes: `Resources.GetBuiltinResource<Mesh>("Cylinder.fbx")`. **Stop if it returns null.** The unit cylinder is 2 tall and 1 wide, so a radius r and height h need scale `(2r, h/2, 2r)`.
  - The parts:

    | Part | Scale | Local y |
    |---|---|---|
    | `Plinth` | (5.2, 0.2, 5.2) | 0.2 |
    | `Drum` | (4, 2.3, 4) | 2.7 |
    | `Crown` | (4.7, 0.175, 4.7) | 5.175 |
    | `Column Slot 1..4` (empty, y 0): `Shaft` | (0.7, 2.5, 0.7) | 2.9 |
    | `Column Slot 1..4` (empty, y 0): `Cap` | (0.9, 0.15, 0.9) | 5.55 |
    | `Collider` | CapsuleCollider: direction Y, radius 2.6, height 13.2, center (0, 3, 0) | — |

  - Layer 3 on everything. `GameObjectUtility.SetStaticEditorFlags(…, BatchingStatic)` on the plinth, drum and shafts only.
  - `TowerLook` fields set through `SerializedObject`, then `ApplyColumns(4)`.
  - **Set `hideFlags = HideFlags.None` on the whole tree**, then `PrefabUtility.SaveAsPrefabAsset(root, "Assets/Gameplay/Arena/Tower Look.prefab")` and close the preview scene.
  - Check: the dirty check reads False; the prefab YAML contains `columnRingRadius: 2.25` and `m_Layer: 3`; `git status` shows only the new files and their `.meta`s.
- [ ] **7. `BuildingCapture`.**
  - Add `private TowerLook towerLook;`, commented as "found once; painted every frame from the ring's own state; null on a tower without a look, which skips it".
  - In `Start`, after the ring: `towerLook = GetComponentInChildren<TowerLook>(true); if (towerLook != null && theme != null) towerLook.Bind(theme);`.
  - In `RefreshRingView`: return only if `manager == null || (ringView == null && towerLook == null)`. Build `state` once. Refresh the ring when present (unchanged), then call `towerLook.Refresh(state, Time.unscaledTime)`. Update its doc comment.
  - **No new serialized field.**
- [ ] **8. Green.** Expect **+6** (BASE + 17).
- [ ] **9. Single-client Play Mode check** (actor check first):
  - Instantiate five prefab copies in a row 6 m from S1 (runtime only, never saved) and `Bind(theme)` each.
  - `ApplyColumns(1..4)` on four of them.
  - `Refresh` states: owned 0, owned 1, owned 2, neutral, out of play. Read each crown and active cap's `_BaseColor` with `GetPropertyBlock`: they equal the expected theme colours exactly. `sharedMaterial.name == "Tower Owner"` everywhere (no "(Instance)").
  - One more copy set to under attack: a 2 s in-process recording of `ShownColor` shows min/max spanning the owner colour to the warning colour.
  - **Ring regression:** tower 6's and tower 9's edge colours equal `ring-before.txt`.
  - Capture `s2-tower-row.png`. **The controller looks:** five round towers with 1, 2, 3 and 4 visible columns, three team colours, a grey one and a dark one.
  - Stop. The scene gate prints UNCHANGED.
- [ ] **10. Commit** the eleven files and their `.meta`s: `feat(arena): arena step 2 - the tower look prefab and owner paint (ring and tower share one rule)`.

---

### Step 3: the towers in the scene

**Files:** `ArenaPrimitiveBuilder.cs` (new; towers phase), `ArenaPrimitiveSceneTests.cs` (new; +3), `Game Scene.unity`.

- [ ] **1. Red.** `ArenaPrimitiveSceneTests` uses the `WithGameScene` helper from `ArenaSymmetrySceneTests.cs:90-101`. Report the failures.
  1. `EveryTowerShowsATowerLookWithOneColumnPerTier`: every `BuildingCapture` has one `TowerLook` child, with `ShownColumns == ColumnsForTier(tier)`.
  2. `EveryTowerLookIsWorldSizedAndItsColliderIsUpright`: `lossyScale` is 1 ±0.001; the capsule's up axis is world up; its world straight part runs from ≤ 0 to ≥ 5.7 m.
  3. `TheOldHouseAndCarpetOfEveryTowerAreHidden`: on the tower object, the `MeshRenderer` is disabled and there is no enabled non-trigger collider outside `Tower Look`; the old children are inactive; `flagRenderer` is assigned but disabled.
- [ ] **2. The builder:** `public static List<string> BuildTowerLooks(Scene scene, GameObject towerLookPrefab)` (`Overpower.EditorTools`).
  - Refuse in Play Mode (the `ArenaSymmetryBuilder.cs:40-41` wording).
  - Per `BuildingCapture` in the scene:
    - hide the house: disable the `MeshRenderer` and the non-trigger colliders on the tower object, and deactivate every child except `Tower Look`, recording prefab-instance modifications for each;
    - hide the carpet: `flagRenderer.enabled = false`, recorded;
    - destroy any existing `Tower Look` child;
    - `PrefabUtility.InstantiatePrefab` under the tower, then `UnpackPrefabInstance(…, Completely, AutomatedAction)`;
    - set local position and rotation to zero, and local scale to `Vector3.one / tower.lossyScale.x`. Report a PROBLEM if the scale isn't uniform;
    - `ApplyColumns(TowerLookRules.ColumnsForTier(tier))`.
  - `MarkSceneDirty`. Return one line per tower: id, tier, columns, scale.
  - Add a temporary menu **OverPower › Arena › Build tower looks** (no save). Step 5 folds it into `BuildAll`.
- [ ] **3. Build and save.** Recompile. `BuildAndSave.Towers`:
  - check not in Play Mode, the scene open and not already dirty;
  - build, then `ArenaSymmetryBuilder.Validate`;
  - on any PROBLEM, reopen from disk and **do not save**;
  - otherwise `SaveScene` and return the report.

  Then read the dirty flag in a separate call: False. Record **SCENE_SAVED**.
- [ ] **4. Idempotent.** `ArenaDump` gives `dump-s3a.txt`: path, active, layer, static flags, transform (mm), mesh, material, collider type and size, sorted. Run `BuildAndSave.Towers` again and dump to `dump-s3b.txt`. `cmp` prints SAME. The dirty check reads False, then re-record SCENE_SAVED.
- [ ] **5. The facts after.** `ArenaFacts` gives `facts-s3.txt`. Towers, spawns, outline and PhotonViews are **identical** to `facts-before.txt`. Only the active-MeshCollider list may shrink (the tower doors).
- [ ] **6. Reload from disk, then run the tests.** Expect **+3** (BASE + 20). The five existing scene tests pass unchanged.
- [ ] **7. Single-client Play Mode check** (actor check):
  - **Colours:** `TowerColourProbe` compares each tower's `ShownColor` with its replicated owner (6/7/8 → teams 0/1/2, the rest neutral); exact.
  - **Capture:** `BuildingManager.SetCaptured(9, <your team>, 0, 0)` (`:558`, the Editor is master). A recorder shows tower 9's colour turning to your team colour within one frame of `Current.OwnerOf(9)` changing.
  - **Under attack:** `TeleportTo` 6 m inside an enemy capital's ring and record for 3 s. The tower's colour equals that ring's edge colour **every frame** (both go through `OwnerPaintColours` with a team colour).
  - **Out of play:** call `MatchDirector.GoLive(new[] {0, 1})` by reflection (`MatchDirector.Live.cs:190`, private): tower 8 shows `outOfPlayZoneColor`. Leave the room and rejoin.
  - **Materials:** every crown and cap's `sharedMaterial` is `Tower Owner`.
  - **Bounce B4:** 20 pulls at tower 9 from 8 m at 0°, 30° and 60° off its centre line. Every redirect has |y| < 0.01; compare with step 0.
  - **Captures:** `s3-t4.png` (S2), `s3-capital.png` (at S1 facing your capital: 1 column), `s3-t3.png` (S3: 3 columns). **The controller looks:** columns countable from the game camera, crowns in the right colours, nothing floating, no tower hiding the player.
  - **Performance** at S1-S3: `perf-s3.txt`.
  - Stop. The scene hash equals SCENE_SAVED and the dirty check reads False.
- [ ] **8. Commit** the builder, the tests and the scene: `feat(arena): arena step 3 - round towers with one column per tier on every zone`. The body gives the report's tower lines and the B4 numbers.

---

### Step 4: the layout data and the arena builder (scene unchanged)

**Files:** `ArenaLayout.cs`, `ArenaBuiltGroup.cs`, `ArenaLayoutCapture.cs`, `ArenaLayout.asset`, `Arena Floor.mat`, `Arena Wall.mat`, `Arena Block.mat` and `ArenaPrimitiveBuilderTests.cs` (all new); `ArenaPrimitiveBuilder.cs`.

- [ ] **1. Red:** the tests (+5), all in preview scenes.
  1. `CaptureMakesOneRowPerUnitFromItsOwnCollider`: a unit with a BoxCollider at yaw 30° and scale 0.8 gives the exact centre, size and yaw; a child's collider is ignored and counted.
  2. `CaptureSkipsAUnitInsideATowerFootprintAndReportsIt`.
  3. `BuildMakesOneUprightStaticBoxPerRowOnTheBuildingLayer`: walls under `Boundry`, blocks under `Blocks`, both groups marked; each piece a unit cube with a unit BoxCollider, the right material per kind, `BatchingStatic`, no MeshCollider.
  4. `BuildingTwiceReplacesItsOwnGroupsAndRefusesForeignOnes`: the same child count after a second build; a foreign child under Source makes it refuse and delete nothing.
  5. `BuiltWallsSitOnTheOutline`: capture two BoxCollider walls, build, and `ArenaSymmetryBuilder.Validate` on the tiny arena returns nothing.
- [ ] **2. `ArenaLayout`** (`Overpower.Data`, sealed `ScriptableObject`, private serialized fields with read-only accessors, like `TerritoryConfig`).
  - `enum PieceKind { Wall, Block }`.
  - `[Serializable] struct Piece { string name; PieceKind kind; Vector3 centre; float yawDegrees; Vector3 size; }`, each field with a plain tooltip. Examples: "Wall = the arena's edge; goes under Boundry and must sit on Source Outline, with +Z facing in"; "centre in world metres, in the Source (Team 2) third".
  - `List<Piece> pieces`; `Material wallMaterial`, `blockMaterial`, `floorMaterial`; `Vector2 floorSize = (220, 220)`; `float floorThickness = 4` ("thick, so a player spawned a little into the floor is always pushed up, never through").
- [ ] **3. `ArenaBuiltGroup`** (`Overpower.Arena`): an empty `MonoBehaviour`. Its comment: it marks a group the arena builder made and may replace; anything else under Source is never deleted.
- [ ] **4. `ArenaLayoutCapture.Capture(Transform root, IReadOnlyList<(Vector3 centre, float radius)> keepClear, List<string> report)` → rows.**
  - Each group is a direct child of `root`; each unit is a group's direct child.
  - Kind is Wall under `ArenaSymmetry.BoundaryGroupName`, otherwise Block.
  - The box: the unit's own first enabled non-trigger BoxCollider, else its own MeshCollider's `sharedMesh.bounds`, turned into world centre, `size × lossyScale` and yaw. With neither, skip and report.
  - Report pitch or roll above 0.5° as a PROBLEM.
  - Skip a unit whose XZ centre is within `radius + 0.5` of a tower, and report it.
  - Count and ignore colliders on children.
  - Menu **OverPower › Arena › Capture layout from Source** writes the rows into the asset (`SaveAssetIfDirty`).
- [ ] **5. The builder phases** (public, tested in preview scenes; **not run on Game Scene in this step**):
  - `MoveOldArtAside(ArenaSymmetry arena, Transform environment)`: creates or reuses an inactive `Old Arena (off)`. It moves, keeping world positions:
    - every Source child without the marker into `…/Source (Team 2 third)`;
    - every **unmarked** child of each generated third into `…/Generated 120°` and `…/Generated 240°`;
    - `Mountains`, `Props`, `Nature` and every `Terrain` under `environment` into `…/Scenery`.

    It reports each move and is idempotent.
  - `BuildSource(ArenaSymmetry arena, ArenaLayout layout)`:
    - refuse in Play Mode, and while Source holds an unmarked child ("move the old art aside first");
    - delete its own groups, then create `Boundry` and `Blocks` with the marker;
    - each row becomes a GameObject named after it: Building layer, `BatchingStatic`, `MeshFilter` with the built-in `Cube.fbx`, `MeshRenderer` with the kind's material, a default `BoxCollider`, position `centre`, rotation `Euler(0, yaw, 0)`, scale `size`.
  - `BuildFloor(Transform environment, ArenaLayout layout, Vector3 centre)`: replaces the marked `Arena Floor` cube (Default layer, `BatchingStatic`, the floor material) at `(cx, −t/2, cz)`, scaled `(x, t, z)`.
- [ ] **6. The materials** (`SCRATCH\MakeArenaMaterials.cs`): `Arena Floor`, `Arena Wall` and `Arena Block`, URP Lit, with Decision 18's colours.
- [ ] **7. Capture the real layout** (`SCRATCH\CaptureLayout.cs`). It only reads the open scene's `ArenaSymmetry.source`; the keep-clear list is the ten towers at the prefab capsule's radius. It creates `ArenaLayout.asset` with the rows, materials and floor settings.
  - **Report the counts.** Expected: 22 walls; 6 house blocks (4 in `Buildings`, plus `House_04R (3)` and `House_05 (2)`); 6 crates; 15 from `Nature`; `Furniture_02 (8)` skipped (inside tower 2); the number of child colliders ignored; every row.
  - The dirty check reads False, and the scene gate prints UNCHANGED.
- [ ] **8. Green.** Expect **+5** (BASE + 25).
- [ ] **9. Play Mode smoke:** as step 3, capture `s4-smoke.png`; the scene gate prints UNCHANGED.
- [ ] **10. Commit** the ten files and their `.meta`s: `feat(arena): arena step 4 - the arena layout asset, its capture and the primitive builder (not applied yet)`.

---

### Step 5: the primitive arena in the scene

**Files:** `ArenaPrimitiveBuilder.cs`, `ArenaSymmetry.cs`, `ArenaSymmetryInspector.cs`, `BuildingManager.cs` (comment only), `Game Scene.unity`, `ArenaMinimap.png`, `MinimapConfig.asset`, `ArenaPrimitiveSceneTests.cs` (+3).

- [ ] **1. Red** (+3):
  1. `EveryArenaPieceIsAnUprightStaticBoxOnTheBuildingLayer`: in Source and both thirds, every collider is an enabled BoxCollider on Building, the object's up axis is world up within 0.5°, and every renderer is `BatchingStatic`. If the copies lose the flag, the builder sets it after Rebuild.
  2. `NoMeshColliderAndNoOldArtIsActive`: no enabled MeshCollider on any active object in the scene, and no enabled renderer under `Enviorment` outside `Old Arena (off)` uses a mesh or material from `Assets/Sources/`.
  3. `TheFloorIsOneDefaultLayerSlabWithItsTopAtZero`: outside Source, top at y 0 ±0.001.
- [ ] **2. `BuildAll(Scene scene)`**, in this order:
  1. `BuildTowerLooks`
  2. `MoveOldArtAside`
  3. `BuildSource`
  4. `BuildFloor`
  5. `ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false)`
  6. `MinimapBaker.Bake(arena)`
  7. The report and `Validate`.

  Menus: **OverPower › Arena › Build primitive arena** (no save) and **Capture layout from Source**. Remove step 3's temporary menu.
- [ ] **3. Comments.**
  - `ArenaSymmetry`'s "HOW TO EDIT THE ARENA": Source is built from `ArenaLayout.asset`; edit the asset, or move blocks and Capture, then Build primitive arena; Rebuild thirds alone still works for a quick look.
  - The Inspector help box gains a **Build primitive arena** button.
  - `BuildingManager.cs:317-322`: the rebuild is done. Keep the reason `MatchDirector` has no scene footprint.
- [ ] **4. Build and save.** `BuildAndSave.All`, then the dirty check (separately): False. Record SCENE_SAVED. Report:
  - Source: `Boundry` 22, `Blocks` 27;
  - both thirds equal to Source; Validate empty;
  - the objects moved into the old art, per group;
  - the floor;
  - the minimap bake line (size and farthest mesh).
- [ ] **5. Idempotent:** dump, run again, dump; SAME. Save; the dirty check reads False; re-record SCENE_SAVED.
- [ ] **6. The facts after:** towers, spawns, outline and PhotonViews identical to `facts-before.txt`, and **0** active MeshColliders.
- [ ] **7. Top-down render:** `topdown-after.png`. **The controller compares it with `topdown-before.png`:** the same wall lines, blocks where houses, crates, rocks and bushes were, and a round tower on every zone.
- [ ] **8. Reload from disk, then run the tests.** Expect **+3** (BASE + 28). `ArenaSymmetrySceneTests` (3) and `TerritoryAdjacencySceneTests` (2) pass unchanged.
- [ ] **9. Single-client Play Mode check** (actor check):
  - **Spawns:** for each of the six spawn points, `TeleportTo` and wait 30 physics steps. `rb.position.y` is within 0.05 m of step 0's standing height at S1, `CheckCapsule` (Building) is false, and `LeftArena` never fires.
  - **Captures:** S1, S2, S3, and the large map. **The controller looks:** a grey floor, walls, blocks, towers, and bubbles over the tower tops.
  - **Performance:** `perf-s5.txt`, tabulated against before and step 3.
  - Stop. The scene hash equals SCENE_SAVED.
- [ ] **10. Commit** the eight files: `feat(arena): arena step 5 - the arena rebuilt from primitives (old art switched off, not deleted)`. The body gives the report's counts.

---

### Step 6: everything that depends on the arena, re-checked (commit only on a fix)

One Play Mode session, the actor check first, and in-process recorders throughout.

| # | Check | How | Expected |
|---|---|---|---|
| V1 | Shots stop on walls and blocks | Weapon 1 at a wall and at an ex-house block from 8 m; record the last position before despawn | Within 0.15 m of the face |
| V2 | Bounce stays level | `BounceProbe` B1-B4 at step 0's points (B2 and B3 are now blocks) | Every redirect \|y\| < 0.01; step 0's tilted bounces gone |
| V3 | Rocket splash occlusion | Two `DummyTarget`s, grounded like `TestRangeSpawner.Grounded`: 1 m behind a block, and 1 m from the burst in the open. Weapon 2 at the block's near face | Behind: 0 splash. Open: 13.33 at 1 m (the known falloff, 20 / 13.33 / 3.33) |
| V4 | X-Ray through walls | Weapon 13 at dummies behind a wall and behind an ex-rock block; then weapon 11 | 13 damages both; 11 damages neither |
| V5 | Laser warning line | Weapon 11: the line's length against the distance to the face. Weapon 13: full range | ±0.1 m; full range |
| V6 | The safety net | `TeleportTo` 1.5 m outside a `Boundry` wall, on the slab | `LeftArena` fires; back at the last safe spot |
| V7 | Blink and portal edge | `ArenaSymmetry.PathCrossesBoundary(inside, outside, 0.35f)` across a new wall; `IsInsideArena` at a spawn | True; true |
| V8 | Kill height | `TeleportTo` y −12 | `FellBelowKillHeight` fires; respawn at your own spawn |
| V9 | Movement integrity | The single-client recipe from `2026-09-17-movement-integrity.md` step 6: dash into a wall, blink along one, portal across one | As recorded there (re-run asked for at `progress.md:1917`) |
| V10 | Camera | S1-S3 at default zoom and at maximum zoom-out | A player beside a tower or wall is no more hidden than in `before-*.png`. **The controller looks** |
| V11 | Minimap | Big-map capture; `MinimapLayout.WorldToMap(tower)` against the crown's pixel in `ArenaMinimap.png` | Within 3 px. **The controller looks** |
| V12 | Dummies | Positions and `CheckCapsule` against step 0 | The same relationship; the pre-existing placement (Decision 20) reported |
| V13 | Performance | The final table: before / step 3 / step 5 | Batches and triangles ≤ before at every spot, or stop and report |

**Fixes:** a layout fix is a row edit in `ArenaLayout.asset` plus `BuildAndSave.All`; a code fix gets its own red test. Commit only then.

---

### Step 7: the look pass (materials, and at most one theme value)

- [ ] **1. Samples.** In Play Mode, with the master: `SetCaptured` zones 3/4/5 to teams 0/1/2, leave 9 neutral, and use `GoLive([0,1])` for an out-of-play capital (then rejoin).
  - Record screen points with `Camera.main.WorldToScreenPoint` (scaled 1280/1920).
  - Sample the capture's pixels in scratch Python: crown against Tower Stone for every state; each team's shot trail and each team's player mesh against the floor (team 1's mesh is black).
- [ ] **2. Thresholds** (summed |ΔRGB|):
  - every crown colour against stone ≥ 60;
  - neutral against team 0 ≥ 60;
  - every trail and mesh against the floor ≥ 60.

  Report every number.
- [ ] **3. Tune** the five materials' base colours and, only if needed, `towerNeutralColor` (one YAML line). Capture `s7-palette.png`. **The controller looks.** The scene gate prints UNCHANGED.
- [ ] **4. Commit** the materials (and the theme, if changed): `tune(arena): arena step 7 - floor, wall, block and tower colours read against all three teams`.

---

### Step 8: assumptions and progress (outside the repo; never committed)

- [ ] Under `## Arena rebuild (2026-09-18)`:
  - every Decision as a `[C]` line in play terms, and the eight Open items as QUESTIONs with their defaults;
  - replace the parked "team 0 near-white against white railings" question with step 7's numbers;
  - mark `:138`'s "4.5 m" as superseded (8 m).
- [ ] Add one `progress.md` row per step, and update HANDOFF §2's "Arena" line. List the `## ` headings.

---

### Step 9: delete the old art (only after Tudor says so)

- [ ] `ArenaPrimitiveBuilder.DeleteOldArt()` destroys `Old Arena (off)` only.
  - The towers' disabled house components stay (harmless), as do the carpets (networked and pinned by a test).
  - Save; the dirty check reads False; reload; the tests show the same total.
  - Play Mode smoke with a capture.
- [ ] **Commit:** `chore(arena): arena step 9 - old imported arena art deleted (Tudor's OK)`.

---

## Risks

- **R1: a scripted change to a prefab-instance tower lost on save.** Every change is recorded (Decision 5), and the tests run after a reload from disk (rule 6).
- **R2: the scene diff is huge**, especially in step 5: Rebuild thirds re-creates every copy with new file ids. Review it with the content dump and the facts file, not the YAML.
- **R3: Play Mode writes into the scene.** The SCENE_SAVED hash is checked after every session.
- **R4: terrain to box floor.** A spawn sits 0.5 m into the floor, as on the terrain today. The 4 m slab pushes the player up (step 5 checks all six spawns).
- **R5: behaviour change where plants and rocks were** (X-Ray, splash, aim cone, flame, pulse, placement paths). Stated as Open #5, not silent.
- **R6: owner pieces leave the SRP Batcher** (about 32 draws). Measured; V13 stops the plan if the totals rise.
- **R7: the tower look's scale.** The towers are 0.8; the compensation is pinned by step 3 test 2.
- **R8: the carpets stay networked while hidden.** No RPC targets them, and `ApplyOwnerVisual` writes a hidden renderer harmlessly.
- **R9: the test range was already misplaced** (Decision 20). Reported, not fixed here.
- **R10: line drift.** Three other items land first (the editor queue). Re-read before editing.
- **R11: the ProBuilder or columns answer arrives mid-plan.**
  - ProBuilder changes nothing here.
  - A reversed column count is one line in `TowerLookRules`, two test assertions, and a re-run of `BuildAll`.
- **R12: a dirty-scene hang.** Every scene step saves inside the same `run_script` call and then reads `isDirty` separately. Prefab and test objects are only ever created in preview scenes.
- **R13: an edge colour regression on the ring.** Pure tests (step 2) plus the `ring-before.txt` comparison.
- **R14: the scene file stays bigger** until step 9, because the old art is kept switched off.

---

## Order and commits

| Step | What | Tests | Scene | Commit |
|---|---|---|---|---|
| 0 | Start state; BEFORE captures, facts, performance, bounce | BASE | unchanged | none |
| 1 | `OwnerPaint`, `TowerLookRules` | +11 | unchanged | yes |
| 2 | Tower look prefab, `TowerLook`, owner colours, the ring on the same rule, `towerNeutralColor` | +6 | unchanged | yes |
| 3 | Towers in the scene | +3 | **changed** | yes |
| 4 | `ArenaLayout`, capture, builder phases (not applied) | +5 | unchanged | yes |
| 5 | The primitive arena in the scene, minimap re-baked | +3 | **changed** | yes |
| 6 | Dependents re-checked | 0 | unchanged unless a fix | only on a fix |
| 7 | Look pass (materials) | 0 | unchanged | yes |
| 8 | Assumptions and progress | — | — | no (outside the repo) |
| 9 | Delete the old art (after Tudor's OK) | 0 | **changed** | yes |

**Total: BASE TESTS + 28.**
- **Ordering:** step 3 needs step 2; step 4 needs step 2 (the capsule radius for keep-clear). Steps 3 and 4 can run in either order. Step 5 needs both. Steps 6 and 7 follow step 5.
- **Playtest-ready from step 3 onward:** the old arena with new towers after step 3, the full new arena after step 5.
