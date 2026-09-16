# Arena symmetry — design

**Date:** 2026-09-16 · **Branch:** `limit-testing` · **Status:** approved by Tudor in brainstorming, 2026-09-16

Tudor: the arena isn't symmetrical, and the T1 corners need more space. The easiest fix is to make the current arena
symmetrical; a graybox built from primitives waits until the map is designed in the GDD. This spec is the easy fix,
done so that the same tool still serves the graybox later.

Markers: [T] Tudor decided · [G] GDD · [C] Claude's starting value (Inspector-tunable, Tudor has not designed it).

---

## Decisions taken in brainstorming (do not re-litigate)

| Topic | Decision |
|---|---|
| Approach | **Symmetrise the current art with a re-runnable Editor tool** [T]. No graybox yet |
| Symmetry | **Rotation only** (three identical thirds, 120° apart) [T]. No mirroring: a team's left and right flanks may differ, which keeps lane identity possible later |
| Centre zone | **Tier 4 replaces the fountain** at the true centre [T] |
| Source third | **The top third (Team 2's side)** [C]. Previews from all three thirds looked nearly identical; the top one was picked |
| Order | Arena first, then 2.6 OverPower, 2.7 phases, then telemetry, then 2.8 [T] |

## What exists (measured 2026-09-16 in `Game Scene`)

- **Ground:** four flat 64 m `Terrain` tiles covering x 0–128, z 0–128 (0.33 m of relief). Terrain can't rotate; it
  doesn't need to. No baked lighting, no NavMesh.
- **Walls (`Enviorment/Boundry`, 47 `Wall_01`/`Wall_02` pieces):** an equilateral triangle, side ≈107.5 m. Vertices ≈
  (11.28, 22.3), (118.81, 22.3), (65.05, 115.42). **Centroid (65.05, 53.34)**; the three capitals' centroid is
  (65.17, 53.30).
- **Towers** (`BuildingCapture` on house models under `Enviorment/Houses`; flag carpets with a `PhotonView` under
  `capture points`), in degrees counter-clockwise from +x around the centroid:

  | Third (capital axis) | T1 capital | T2 | T3 (counter-clockwise border) |
  |---|---|---|---|
  | Team 2, top (90°) — **source** | #8 (64.90, 111.00), r 57.7 | #2 (64.57, 87.36), r 34.1 | #5 (46.77, 62.44), r 20.5, 154° |
  | Team 0, bottom-left (210°) | #6 (15.08, 24.71), r 57.7 | #0 (33.48, 34.50), r 36.8 | #3 (65.90, 31.73), r 21.6, 272° |
  | Team 1, bottom-right (330°) | #7 (115.54, 24.18), r 58.2 | #1 (96.90, 35.00), r 36.6 | #4 (83.15, 63.28), r 20.6, 29° |

  T4 #9 sits at (63.28, 46.48), 7 m off centre, radius 4.5 m, because the fountain blocks the middle.
- **Spawn points:** `team (2)` (65.20, 105.42) top; `team (1)` (20.21, 27.56) bottom-left = team 0; `team`
  (109.55, 27.60) bottom-right = team 1.
- **The asymmetry is in the props, not the structure.** Houses already follow the rotated pattern within 1–3 m. The
  differences are the centre clusters (V at the top, L-shapes at the bottom), barrier placement per wall, and crates.
- **T1 corners:** each capital sits 4.4 m from its 60° vertex, where the corridor is ~5 m wide. About half of its 10 m
  capture circle is outside the walls.
- Nothing in code finds arena objects by name. Adjacency is explicit per tower (not position-based), so moving towers
  doesn't change it.
- A preview render (preview scene, nothing saved) confirmed that rotating one third reproduces the layout. It also showed
  the two problems the tool must handle: pieces crossing a third's border, and towers that must be snapped rather than
  copied.

---

## Part 1 — The `ArenaSymmetry` tool

**Scene structure** (new root under `Enviorment`):

```
Enviorment/Arena                 ArenaSymmetry component
  Source (Team 2 third)          the only part anyone edits
  Generated 120°                 rebuilt by the tool, NotEditable
  Generated 240°                 rebuilt by the tool, NotEditable
```

**`ArenaSymmetry` (MonoBehaviour, Editor-time only behaviour):**
- `centre` (Vector3) — pivot of the rotation, default the wall centroid (65.05, 0, 53.34). Tooltip explains that
  moving it moves every generated piece.
- `source`, `generated120`, `generated240` (Transforms).
- `towerTriplets` — three rows of `BuildingCapture` references, source first then +120° then +240°:
  capitals [#8, #6, #7], T2 [#2, #0, #1], T3 [#5, #3, #4]. `centreTower` = #9.
- `spawnTriplet` — [`team (2)`, `team (1)`, `team`].
- Custom Inspector with two buttons and a plain explanation of the workflow: **Rebuild thirds** and **Validate**. Also
  under the menu `OverPower › Arena › Rebuild thirds`.

**Rebuild thirds does, in order:**
1. **Refuses and lists the offenders** if anything under `source` has a `PhotonView`, `BuildingCapture`, or a
   `MonoBehaviourPun`/`IPunObservable`: networked scene objects can't be copied (their view ids must stay unique).
2. Deletes every child of `generated120` and `generated240`.
3. Copies each direct child of `source` into both, rotated about `centre` by +120° and +240° (counter-clockwise from
   above; Unity yaw −120° / −240°), keeping local scale, layer, tag, static flags and colliders. Copies are plain
   (unpacked) GameObjects. Each generated root gets `HideFlags.NotEditable`, so the Inspector greys it out.
4. Snaps towers: for each triplet, the +120° and +240° towers (and their `flagRenderer` carpets) take the rotated
   position and rotation of the source tower and carpet. `centreTower` and its carpet go to `centre` (carpet keeps its
   height).
5. Snaps spawn points the same way.
6. Marks the scene dirty (it does **not** save; the designer saves).

**Validate** reports, per generated object and snapped tower/spawn, any position more than 5 cm or yaw more than 0.5°
away from its rotated source. Rebuild ends by running it.

**Pure maths in `RadialSymmetry` (static, edit-mode tested):** rotate a point about a centre by k thirds; rotate a
rotation; which third a point belongs to (half-open [a, a+120°)).

**Tests:**
- `RadialSymmetryTests` (pure): rotation of points and yaw, three rotations return to start, border classification.
- `ArenaSymmetrySceneTests`: opens `Game Scene` **additively**, runs Validate, asserts no mismatches and exactly three
  capitals/T2s/T3s 120° apart (±5 cm), then closes it. Guards against a hand-edited copy or a forgotten rebuild.

**Designer workflow (goes in the class comment):** edit anything under `Source`, press Rebuild thirds, look, save the
scene. Moving a source tower moves its two partners. Never edit a generated third; the next rebuild erases it.

## Part 2 — Migrating today's arena into the tool (one-time)

1. Create the structure above. Place the source towers exactly on their axes, keeping their distances from the
   centre: #8 and #2 on the 90° axis, #5 on the 150° axis.
2. **Placement units** are GameObjects inside the wall triangle (+3 m) with their own `Renderer` or `Collider`, or
   groups no wider than 30 m. Groups wider than that are walked into, which is needed because `Nature/*` and
   `Props/Markets/Box` have their pivots at the origin.
3. Units in the source third, [30°, 150°), move under `Source`, keeping world transforms. Units in the other thirds
   are deleted. The fountain and its tiles (within 6 m of the centre) are deleted. Scenery outside the triangle
   (mountains, power lines, markets outside the walls) and the terrain tiles stay where they are, untouched. Groups left
   empty are deleted.
4. **Seams:** wall runs and props crossing the 30° and 150° borders are trimmed or nudged so the rebuilt thirds meet with
   no gap and no doubled piece. This is judged on a top-down render.
5. **T4:** tower #9 at the centre, `captureRadius` **8 m** [C]. The T3 circles' inner edges are ~10.5 m out, which
   leaves a 2.5 m gap.
6. **Capital pocket** (edited in the source third only):
   - **Requirement:** the whole 10 m capital circle sits inside the walls with **≥ 3 m** of walkable ground around it [C].
     The back wall stays inside the terrain (≤ 11 m beyond today's vertex, which the terrain edge allows).
   - **Starting shape [C]:** where the corridor narrows to 26 m wide (~22 m before today's vertex), the two side walls
     stop converging and run parallel to the capital axis. A back wall closes the pocket 13 m behind the capital.
   - The corner crates and palms are removed. The capital stays at its current distance from the centre, so the lanes
     keep their length.
   - The GDD's "enclosed with limited entry points" (p.29) is read as: the pocket opens only toward its lane.
   - Tuned on renders. **Tudor gets before/after top-down renders** and decides whether to keep it.
7. Rebuild, Validate, save the scene.

## Verification

- `recompile_status` clean; all edit-mode tests pass (async).
- Top-down renders before and after, looked at by the controller and sent to Tudor. A 616×576 Game view capture from
  inside one capital pocket and one at the T4 centre.
- Play mode, one client: spawn in each capital (teleport via `PlayerDisplacement.TeleportTo`), walk the pocket edge,
  capture T4 from its 8 m edge, confirm no player gets stuck on a seam.
- `git status`: only the scene, new scripts, tests and `.meta` files; `GameplayConfig.asset` unchanged.
- Two clients: towers and capture behave the same on the second client. Scene view ids are unchanged, since towers
  weren't recreated.

## Out of scope

Mirror symmetry, a primitive graybox, tall-wall vs jersey-barrier rules (GDD p.29), the phase-2 map reduction, lighting,
NavMesh, and visual polish of the copies.

## Risks

- **Scene file size grows**, since copies are unpacked. Accepted.
- **Prefab links on moved source objects** stay; generated copies have none. Swapping a prop's prefab later means editing
  the source and rebuilding.
- **Test range dummies** (`TestRangeSpawner`) could spawn inside new geometry. Check once in play mode.
