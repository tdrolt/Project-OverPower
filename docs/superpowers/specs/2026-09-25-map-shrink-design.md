# The map shrinks when a team is knocked out: design

**Date:** 2026-09-25. **Branch:** `limit-testing`. **Source:** GDD p.20-21 (phase transition) and p.27 ("The map after
one team gets eliminated"); Tudor's answers in this session (logged in `Resources/loops/Limit Test/assumptions-for-tudor.md`,
"The map shrinks on a knockout, 2026-09-25").

## What the player sees

When the first team is knocked out (three teams become two), that team's corner of the triangle closes:

- A full-height wall, like the outer walls, appears across the arena **6.3 m past the centre**, toward the knocked-out
  team's capital. Nobody can walk, dash, zip, shoot, blink or portal past it. It appears the moment the team is out,
  with the "two teams left" banner (the survivors are already sent home at that moment; the knocked-out team is all
  dead).
- **The wall has a recess** behind the centre tower: the same shape as the Tier III recesses in the outer walls
  (19.5 m wide, 3.25 m deep) with a jersey barrier (10.2 m) across the middle of its mouth. No planks (Tudor's open
  question; default).
- **Behind the wall everything disappears:** the knocked-out capital, its Tier II, the two side Tier III that lead to
  it (Tudor: "removed"), and every house, crate and barrier whose centre is behind the wall. The floor and the outer
  walls stay.
- **The centre plays as a Tier III** from then on: Tier III capture time, gold and bounty (the same `TerritoryConfig`
  row as the others), three columns, "III" on the minimap. It keeps its own 8 m capture area.
- **Every Tier III and the centre reset to neutral** without bounty history (the GDD's "all Tier 3 territories become
  neutral"), and so does every zone behind the wall (so nobody keeps an income or a way in from it).
- Mines, cover walls, fences and portals behind the wall vanish; if either of a player's portals is behind it, all of
  that player's portals go.
- **Minimap:** the closed part is darkened, the wall is drawn, and the cut zones' bubbles and links are hidden.
- **Lanes (unchanged geometry):** short lane via the Tier III between the survivors (~59 m tower to tower), long lane
  via the centre (~68 m).
- **A host-started two-team match plays on this cut map from the moment it goes live** (the left-out team's corner).
- **Which corner closes:** the knocked-out team's, unless a surviving team's only capital is that corner's (it lost its
  own and took theirs); then the corner no surviving team depends on closes instead (lowest team number first).

## Design values (one home each)

`ArenaLayout.asset` (Inspector, header "Phase two cut"): wall distance from the centre (6.3 m), recess width (19.54 m),
recess depth (3.25 m), recess barrier size (10.18 × 1 × 0.6 m, like a barrier row). The wall's thickness, height,
material and the barrier's blocking band reuse the existing `ArenaLayout` values.
`UiTheme.asset`: the minimap's closed-area colour, wall colour and wall width.
Tier numbers: the existing `TerritoryConfig` Tier III row.

## How it works

**The state is one Room Property.** `mCut` (int, the team whose corner is closed) is written by the master in the same
`SetCustomProperties` call as the phase change (never an RPC; the RPC list is untouched). A host start needs no key:
the cut team is the one missing from `mTeams`. Every client derives everything else from that one number and the
scene, so late joiners and a new master agree. `MatchDirector.CutTeam` reads it.

**Pure rules** (`Match/Rules/PhaseTwoCutRules.cs`, edit-mode tested): the cut team from the room facts; which zones a
cut removes (the cut team's capital, the Tier II next to it, and the Tier III next to that Tier II, found from the
territory links and the towers' own tiers, no hard-coded ids); the effective tier (Tier IV plays as III while a cut is
active); which zones to neutralise; which corner to cut.

**Pure geometry** (`Arena/PhaseTwoCutGeometry.cs`, edit-mode tested): from the arena's full outline, the centre, the
direction to the cut capital and the layout values, it builds the wall line (edge, recess, edge), the new playable
outline, the removed outline, the wall boxes (through `ArenaWallPlan`, so the corners close exactly like the outer
walls), and the barrier's place. `ArenaBounds.FromPolygon` turns the outlines into the same inside/outside test blink,
portals and the out-of-arena safety net already use.

**Choke points reused, not duplicated:**
- `MatchDirector.IsOutOfPlay(zone)` gains the cut rule. The capture rule, the ring, the tower, the minimap and the
  territory win already ask it.
- `BuildingManager.TierOf`/`TierByZone` and `BuildingCapture`'s capture time and bounty return the effective tier.
  Gold income and health regen follow.
- `ArenaSymmetry` publishes the playable outline instead of the full one while a cut is active.
- `NetworkedDeployable` gains one "destroy what I own where ..." pass beside the existing match-start one.

**The arena view** (`Arena/ArenaPhaseTwoCut.cs`, added at runtime by `ArenaSymmetry`, no scene object): each frame it
compares `MatchDirector.CutTeam` with what it last applied. On a change it builds the wall boxes and the barrier as
runtime primitives on the same layers and materials as the editor-built ones (walls under `Source/Boundry`, so a
portal's path check sees them), swaps the published outline, hides the pieces behind the wall, and clears this
client's own placed objects behind it. On "no cut" (a new room) it undoes all of it.

## What it does not do

- No change to the RPC list, the phase rules, the elimination rules or the adoption rules.
- No change to the edit-time arena builder's output or the scene's walls; the scene gains one reference (the layout
  on `ArenaSymmetry`).
- No planks on the new recess (open question for Tudor).
- No telemetry line of its own (the existing `phase` line already marks the moment).

## Testing

- Pure: the rules, the geometry (with the real outline: survivors' zones inside, cut zones outside, the recess inside,
  the wall runs meeting the outer walls), the minimap mask.
- Scene: for each of the three corners, with the layout's own values, the survivors' capitals, Tier II, the shared
  Tier III and the centre tower are inside; the cut capital is outside; no block that stays overlaps the new wall.
- Play Mode, one client: a host start ([0,1]) cuts team 2's corner; walking, a dash, a shot and a blink at the wall
  are stopped; the centre's capture time and income are Tier III's; captures of the arena and the minimap, looked at.
- Two clients (host start) and three clients (a real knockout): the same wall, outline and minimap on every client,
  zones neutralised, a placed object behind the wall removed.
