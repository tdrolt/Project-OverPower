# Vision: fog of war, the sight cone, shared team sight and the centre scan: design

**Date:** 2026-09-30. **Branch:** `vision` (from `main` `94afbeb` + Tasks 14-16). **Source:** GDD v2 (`Resources/POP GDD -
2026-09-30.docx`): Player Kit › Vision ("a wide cone in the direction of the cursor and a small circle around them ...
does not go through walls. Team mates also have shared vision"), Territory › Tier 4 Perk ("reveals the current state
of adjacent territories every 10 seconds", changed by Tudor below), the MDA aesthetic "Uncertainty", Arena Theming's
"scan wave". Tudor's answers in this session (two rounds, 2026-09-30 evening). Mockups and Tudor's picks:
https://claude.ai/artifact/1wzRfnyx86oKVt1RJXHvoF.

## What the player sees

**Your team's sight.** You see in a **cone toward your cursor** (90° wide, 22 m long) and a **circle around you**
(7 m). Anything that stops a bullet stops sight: tall walls, blocks, planks, the walls that appear when the map
shrinks, Cover Walls. You see over jersey barriers, like shots pass over them. Every living teammate's cone and circle
count for the whole team, anywhere on the map. Mouse-wheel zoom never changes how far you see (zoomed out you see more
fog, not more enemies).

- **Scope, while held:** a narrow long beam instead: the cone becomes 30° wide and 26 m long, the circle shrinks by
  3 m (7 → 4 m). Range traded for width.
- **Dead:** you see what your living teammates see. **Spectating after a knockout:** you see what the team you're
  watching sees.
- The fog is on in the warm-up too. One switch turns the whole system off (for tests).

**The fog (look 1A + 2A).** Everything outside your team's sight is dimmed (dark `1A1C22` at about half strength) with
a **hard edge**; the arena stays readable under it. Walls cut the sight shape, so the fog lies behind a wall.

**Hidden in the fog:**
- Enemy players: body, name, health/armour bar, STUNNED/SLOWED label, Invulnerability ring, anything on them. They
  **pop** in and out at the sight edge (look 3A).
- Enemy shots: bullets, rockets, Stun Gun and Zip Gun bolts are visible only while inside your team's sight (a bullet
  from the fog appears as it crosses the edge); a hidden shooter's muzzle flash isn't shown. Nothing extra marks a hit
  from the fog (look 4A): the shot itself is the hint.
- Enemy lasers, Raybeam and the laser warning line: shown whenever the line passes through your team's sight.
- Blasts: shown if the blast's centre is in sight or it hurts someone on your team.
- Enemy Blink / Teleport / Dash effects: the departure only if that spot is in sight, the arrival only if that spot
  is in sight.
- Enemy AoE Zone and fire fields: only while in sight.

**Not hidden:** the arena, walls, barriers, capture rings, health packs and their state, teammates, test-range
dummies. Mines keep their own rule (they already fade out for enemies). Cover Walls, Electric Fences and Portals stay
visible to everyone. Your own damage numbers on a hidden enemy still show. The OverPower buff, the Mark laser's mark,
the aim cone: unchanged (they are already owner- or victim-only). Hits from the fog count for OverPower as today.

**Zones in the fog** (switch "Zone owners visible without sight", **ON** for now):
- **ON:** everything about zones is live for everyone, as today (owner colours, capture progress, under attack).
- **OFF:** a zone's state (owner colour, capture progress, under attack, the moment of capture) updates for your team
  only while your team sees it, while your team owns it, or when your team's centre scan passes over it; otherwise it
  stays frozen on what your team last knew, in the game (tower colour, capture ring) and on the minimap alike.

**Sounds.** Every gun's shot and impact sound gets a direction: full volume up to 15 m, fading evenly to silent at 35
m, heard from the fog too. Other sounds unchanged.

**X-Ray blind hits.** A hit from the X-Ray laser on an enemy in the fog reveals that enemy to the shooter's team for
0.5 s. This is a field on every weapon ("Reveal on hit, seconds"), 0 everywhere except the X-Ray laser.

**Minimap and M map (look 5C, no last-seen marks).** The fog is drawn on the minimap too (your team's current sight);
enemies your team sees now are **red dots** (not their team colour: you know an ally saw someone, you have to talk to
learn who). No last-known positions.

**The centre scan (Tier 4 perk, changed by Tudor).** While your team holds the centre, a **red sonar wave** rings out
from the centre **the moment you capture it and every 30 s after**, across the whole arena in about 1.5 s (40 m/s),
through walls.
- **Everyone sees the wave** rolling across the game world, so an enemy can see it coming and **Blink over it**
  (never caught) or across it the other way (caught twice: two dots, a trick).
- **The holding team** also sees it on the minimap: each enemy is caught **each time the wave front passes them**, as a
  red dot at that spot; dots don't move or update and fade after 4 s. Zone states of the scanned tiers (default: all
  four, I to IV) refresh to that moment for the holding team (this only matters with the zone switch OFF).
- The scan stops the moment the centre is lost or goes neutral, and **ends for good when the map shrinks** (the
  centre then plays as a Tier III).

## Design values (one home each)

- **`VisionConfig` asset** (new, `Assets/Gameplay/Config/VisionConfig.asset`, menu `OverPower/Vision Config`, a
  tooltip per field): fog on/off; zone owners visible without sight (on); cone angle (90°), cone length (22 m), circle
  radius (7 m); fog colour and darkness; minimap enemy-dot colour (red); **Centre scan:** interval (30 s), wave speed
  (40 m/s), wave colour (red), dot time (4 s), scanned tiers (I, II, III, IV each a tick box). Anything smaller the
  build needs (sight update rate, the sight texture's resolution) goes here too, with a tooltip.
- **Scope ability prefab** (`Assets/Gameplay/Abilities/…Scope…prefab`, next to its zoom): sight while held: cone
  length (26 m), circle change (−3 m), cone angle (30°).
- **Every weapon asset:** "Reveal On Hit Seconds" (0.5 on `13 Laser - Through Walls`, 0 elsewhere).
- **The shot-sound prefab** (`AudioSourcePrefab`): 3D, full volume to 15 m, linear to silent at 35 m.

## How it works

**Each client works out its own team's sight** (Tudor's choice A). Every client already knows every player's position,
facing (the body turns toward the cursor, `PlayerNetSync` sends the rotation), team (`teamID`) and alive state
(`alive`). **No RPC is added, renamed or removed; no new Room Property.** One new Player Property, `vScp` (bool, the
owner writes it when the Scope starts and ends), lets teammates' games use a scoped teammate's narrow cone; the Scope
today only moves the holder's own camera, so nothing else carries it. A hacked client could see through the fog;
accepted for a prototype. Towers stop bullets (they are on the `Building` layer), so they block sight too.

**The centre scan's clock is shared already:** zone 9's `tSince` (the server-clock moment its owner took it, in
`TerritorySnapshot`). Scan k happens at `tSince + k × interval` for every client alike; the wave's radius at any moment
follows from the server clock, so every screen shows the same wave. The shrink is read from `MatchDirector.CutTeam`.

**Pure rules** (plain C#, edit-mode tested, no engine types beyond maths):
- `VisionRules`: is a point inside one viewer's cone or circle (angle, length, radius, the Scope trade); which
  viewers are "my team's eyes" (living teammates; while dead, the living teammates; spectating, the watched team; team
  unknown = nobody); a team sees a point if any eye sees it and no wall is in between (the wall test is passed in, so
  it can be faked in tests).
- `ZoneKnowledgeRules`: with the switch off, which zone state a team knows (own, in sight, scanned, else last known).
- `CentreScanRules`: when scans happen (from `tSince`, the interval, the holder, the cut), the wave radius at a
  moment, and whether the front passed a player between two frames (so a Blink across it is caught or missed exactly
  as it happened).

**In the game:**
- **Sight shape:** from each eye, rays against the `Building` layer (walls, blocks, planks, phase-two walls, Cover
  Walls) build a sight polygon each frame; the team's polygons are drawn into a top-down sight texture.
- **Fog drawing:** a full-screen pass darkens every pixel whose ground position is outside the sight texture. This
  needs the depth texture switched on in the URP asset (`PC_RPAsset`), so walls and floor are fogged correctly under
  the tilted camera.
- **Enemy visibility:** per enemy, per frame, the same `VisionRules` with a line-of-sight ray; hide = the renderers and
  the overhead canvas off (not the GameObject, so networking and physics keep running). Test-range dummies are left
  alone.
- **Effects:** gated where they are spawned on each client (`RPC_FireWeapon` receive, `Hitscan.DrawBeam`,
  `ShowWarnings`, `RaybeamAbility`, blasts, the ability modules' local cosmetics); projectiles check themselves each
  frame; networked deployables (AoE Zone, Fire Field) toggle their renderers.
- **X-Ray reveal:** every client runs every shot (`RpcTarget.AllViaServer`), so each client of the shooter's team sees
  the beam hit and reveals the enemy locally for the weapon's reveal time.
- **Minimap:** a fog layer from the same sight data; red dots from the same enemy visibility; the scan's wave, dots
  and zone refresh from `CentreScanRules`.

## Checks

- Tests guard rules, never Tudor's numbers ("the Scope's cone is narrower and longer than the normal one", not "30°").
- **Vision is only real with several clients:** every task that hides or shows something is checked with two or three
  clients in its own brief, recorders on every client, a fresh process for any join, the room's actors listed first.
- At the end, one three-client check of the whole system, like Task 10.

## Out of scope

Anti-cheat (the host deciding sight); last-known positions; a hit-direction arc; fog-dependent sounds beyond the
distance fade; enemies revealed by any weapon other than through its "Reveal On Hit Seconds" field.
