# Capture ring, minimap and GDD territory links — design

**Date:** 2026-09-16 · **Branch:** `limit-testing` · **Status:** approved by Tudor in brainstorming, 2026-09-16

Tudor:
- The capture progress display is confusing. Show it more clearly, like a loading bar.
- Add a minimap with the zones, who owns them and how they're connected, like the GDD, so the prototype's UI gets
  closer to the GDD.

**Order agreed:** 2.6 OverPower → telemetry → **this** → 2.7 phases → 2.8.

Markers: [T] Tudor decided · [G] GDD · [C] Claude's starting value (Inspector-tunable).

---

## Decisions taken in brainstorming (do not re-litigate)

| Topic | Decision |
|---|---|
| Capture progress | **A ground ring only** [T]: a ring on the floor marks each zone's capture circle and fills as a capture or drain progresses. **It replaces the world-space bar above each tower** (`CaptureProgressView`). No screen bar |
| Minimap | **A corner minimap, plus a large map on M** [T] (the `ExpandMap` action, already bound and unused) |
| Minimap look | Like GDD p.27 [G]: a bubble per territory labelled I/II/III/IV and sized by tier, filled in the owner's colour (neutral grey); links between adjacent zones; over a top-down image of the arena |
| Territory links | **Match the GDD** [T]: each T2 also links directly to T4 (the prototype only linked T4 to the T3s) |

## What exists (verified 2026-09-16)

- `CaptureProgress` (Room Properties, master-published) holds team, progress 0..1, rate and stamp.
  - A capture has rate > 0; a drain has team = the drainer and rate < 0.
  - **Held progress is published as `Idle`**, which loses the value when a capture pauses (contested, blocked link,
    capturers left).
- `ZonePresenceTracker.IsUnderAttack(zone)` and `BuildingManager` owner, tier and adjacency (`Map.AdjacentTo`) are
  available on every client. Capture Radius is world metres to the player's centre.
- `CameraTracking` rotates the camera per team (`yaw` from the team's spawn point around the centre of all spawns), so
  each team sees its own capital at the bottom.
- Screen-space UI is built in code from `UiTheme` (`PlayerHud`, `LoadoutScreen`). `UiTheme.barSprite` exists.
  **A filled image without a sprite ignores its fill amount** (an earlier bug).
- `PlayerInputRouter` has `ExpandMap` → `MapToggled`, with nothing subscribed. The shoot-through-UI gate counts any
  raycast-target UI under the pointer.
- Scene adjacency: T2 0: 6,3,5 · 1: 7,3,4 · 2: 8,4,5. T4 9: 3,4,5. `TerritoryMapTests` has cases asserting T4 isn't
  capturable from a T2 alone. **Those change on purpose.**
- `ArenaSymmetry` + `ArenaRender` (a top-down orthographic render in a preview scene that doesn't dirty the scene)
  exist from the arena work.

## Part 1 — GDD territory links

- **Data:** add 0, 1 and 2 to tower 9's adjacency in `BuildingManager.TowerDictionary` (a link listed on one side
  counts for both). T4 then neighbours all three T2s and all three T3s.
- **Tests:** update `TerritoryMapTests`' fixture to the new links. Replace the "T4 not capturable from a T2" assertions
  with "capturable from a T2" ones, and add one asserting a capital still links only to its own T2.
- **Verify:** on the master, a team owning only its T2 can capture T4. Also check the capture-link block still applies
  through T2.
- Logged as a [T] decision. It gives earlier T4 access and changes territory flow.

## Part 2 — Capture ring (replaces the tower bar)

**One `CaptureRingView` per tower**, built in code by `BuildingCapture.Start` (as the bar was), lying flat on the ground
at the zone centre, sized to the Capture Radius in world metres.

- **Outline:** a thin circle, always visible, marking the zone edge. Owned → the owner's team colour. Neutral → dim
  white [G p.45: neutral/contested territories use a dim white].
- **Progress arc:** a thicker band just inside the outline, filling clockwise from the side facing the local camera's
  "up" [C]:
  - **Capturing** (neutral → team): the arc grows in the capturing team's colour.
  - **Draining** (owned → neutral): the arc shows the owner's remaining hold, in the owner's colour, shrinking. The
    outline pulses in the drainer's colour.
  - **Paused** (contested, link blocked, drain paused, capturers left): the arc stays at its value and blinks slowly at
    reduced opacity.
  - **Idle:** no arc.
- **Under attack** (owned zone, `IsUnderAttack`): the outline pulses in a warning colour even when nothing is draining,
  e.g. a defender contesting.
- **Replication change:** publish held progress as `CaptureProgress(team, progress, rate 0)` instead of `Idle` whenever
  progress > 0, so every client can draw a paused arc. `Idle` stays for "nothing in progress". The republish rule
  (team or rate changed) still covers it.
- **Rendering:**
  - URP Unlit, no collider, no shadow casting, a small height above the terrain to avoid z-fighting.
  - Geometry from a procedural mesh or two `LineRenderer`s; the implementation plan picks.
  - Its arc is only rebuilt while it's changing (capturing, draining, or blinking).
- **Theme** (`UiTheme › Capture ring`, tooltips):
  - outline width, arc width, height offset, neutral colour;
  - paused blink speed and opacity;
  - under-attack pulse colour and speed;
  - segment count.
- **Removed:** `CaptureProgressView` and its `UiTheme` capture-bar fields, if nothing else uses them.

## Part 3 — Minimap

**Built in code** (`Assets/scripts/UI/MinimapView.cs` + a pure `MinimapLayout` for the maths), one per local player
like `PlayerHud`, screen-space, **no raycast targets** so it never blocks shooting.

- **Background image:** a top-down arena render baked by an Editor action, **OverPower › Arena › Bake minimap image**.
  - It uses the `ArenaRender` approach, square and framed on the arena bounds.
  - It saves `Assets/Gameplay/UI/ArenaMinimap.png` and records the world rectangle it covers on a `MinimapConfig` asset.
  - **Rebuild thirds bakes it too**, so it can't go stale after an arena edit.
  - A tooltip explains the Bake button for other changes.
- **Rotation:** the map rotates with the local camera's team yaw (`CameraTracking` exposes it), so "up" on the minimap is
  "up" on screen. Labels and player markers stay upright.
- **Zones:** a bubble per zone at its world position.
  - Diameter by tier (T1 largest, T4 medium, T2/T3 small, as in the GDD), on `UiTheme`.
  - Fill = owner colour, neutral = grey. The label is the Roman numeral of the tier.
  - A radial progress ring around the bubble mirrors the ground ring's arc (same states). It uses a sprite, so the fill
    works.
  - Under attack: the bubble outline pulses in the warning colour.
- **Links:** a line between each pair of adjacent zones.
  - Both ends owned by the same team → a solid line in that team's colour.
  - One end owned by team X and the other not → an arrowhead from X's zone toward the other, in X's colour (a way
    in, as the GDD's arrows show).
  - Otherwise → a thin grey line.
- **Players:** your own marker (an arrow in your facing) and your teammates' dots, from `PlayerLookup` and replicated
  positions. **Enemies aren't shown** [C]; there are no vision rules to decide who may see whom.
- **Corner minimap:** always visible, in the top-right corner (currently empty; the HUD is bottom-centre, chat
  bottom-left, the Loadout button bottom-right). Size and margin on `UiTheme`.
- **Large map:** `ExpandMap` (M) toggles a large centred version, and the corner one hides while it's open. It closes
  when the P screen opens, and vice versa.
- **Theme** (`UiTheme › Minimap`, tooltips): sizes, margins, bubble diameters per tier, line widths, arrowhead size,
  neutral grey, the background tint/opacity, and the player marker size and colours.
- **Performance:** bubbles and lines are built once. Colours update only when ownership changes (`OwnershipChanged`),
  progress rings only while a capture is active, and player markers every frame.

## Verification

- **Edit-mode tests:**
  - `MinimapLayout`: world → map coordinates (inside the baked rectangle, rotation by yaw, labels upright).
  - Link style rules (same owner / way in / neutral) as a pure function.
  - `TerritoryMapTests` updated for the GDD links.
  - The ring's state from a `CaptureProgress` + owner + under-attack as a pure function (capturing / draining /
    paused / idle / under attack).
- **Play mode, two clients, captures saved to the scratchpad at 616×576 and looked at by the controller:**
  1. The ground ring's outline at a neutral zone and at your own capital.
  2. A capture at ~50%.
  3. A paused capture (enemy inside).
  4. A drain.
  5. The corner minimap at start.
  6. The corner minimap after capturing a T2 (bubble colour + arrows).
  7. The large map on M, with your capital at the bottom (both teams' views differ correctly).
  8. A zone under attack pulsing on both.
- **Regressions:**
  - shooting through the minimap area still fires (no raycast blocking);
  - the P screen and the M map don't overlap;
  - the capture timings from Phase 2 are unchanged (a solo T2 capture is 15 s).
- `RpcList` unchanged. `GameplayConfig.asset` unchanged. The `UiTheme` asset diff shows only the new or removed fields.

## Out of scope

Fog of war / enemy visibility, pings, a clickable map, the phase-2 reduced map layout (2.7 will need the minimap to
drop eliminated zones, noted for 2.7), and emissive environment recolouring (GDD p.45).
