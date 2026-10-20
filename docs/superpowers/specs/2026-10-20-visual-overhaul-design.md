# Visual overhaul: Tron materials, hard shapes, one look per ability, ownership on the ground: design

**Date:** 2026-10-20. **Branch:** `visual-overhaul` (from `main` = `dominion` = `e116709`). **Source:** the 12 Oct playtest
("the game lacks visual clarity"), Tudor's notes in `Resources/gdd/gdd-ideas-2026-10-19.md` "Visual clarity", the brief
`Resources/session/briefs/2026-10-19-visual-clarity-brief.md`, and the Ask round of 20 Oct (01:20-02:20) where Tudor widened
the brief to every ability, every gun, the old UI screens and a volume slider, and approved the looks below. Tudor reviews the
result in the morning; nothing here waits on him.

## Intent

A player tells, without the HUD: who owns the ground and walls around them, which team a player is on, whether a glow is a
fresh respawn or a cast Shield, and which ability or gun just hit them. It is a prototype: clarity first, detail second.

## The style (Tudor's words, 20 Oct)

Keep **Tron's materials**: dark, glossy surfaces with thin **emissive stripes**. Replace Tron's rounded forms with **robust,
sharp shapes** like Halo and the mecha genre: hexagons, 45-degree cut corners, straight panel lines, chunky plates. The glow
lives in stripes, edges and cores, never in soft round blobs. Bloom stays as it is in the scene's volume; a stripe is bright
enough to catch a thin halo, never enough to wash a player out.

## The colour language

| Colour family | Means | Used by |
|---|---|---|
| Team colour (White, Purple, Cyan from `UiTheme`) | owned by that team | floor tiles and wall stripes near an owned zone, towers, capture rings, the **respawn bubble** (Tudor 20 Oct), mech glow stripes, shots and their trails, things a player places: Portal, Mines, Cover, Electric Fence, the team hex under the feet |
| Gold (`UiTheme.immuneBarColor`) | the Shield ultimate, nothing else | Shield shell, its armed floor ring, the immune bars that already exist |
| Electric blue (new, deeper than the cyan team) | Stun Gun and Sonic Pulse (Tudor 20 Oct) | stun bolt, arcs on a stunned player, the STUNNED label, the pulse bands |
| Silver | movement | Dash, Blink, Sprint, Zip Gun |
| Orange to yellow | fire | Flamethrower |
| Red | danger ultimates (Tudor 20 Oct: green reads as healing) | AoE Zone, Raybeam |
| White | a hit or a shot leaving | impact flashes, muzzle flashes, bullet cores |

Electric blue must read apart from the cyan team (0.15, 0.95, 1) and the white team's pale blue shots (0.93, 0.97, 1): a
deeper, more saturated blue, about (0.2, 0.45, 1). Shapes keep same-family looks apart (a small bolt vs. wide bands; a beam vs.
a ground ring).

## What changes, in order

Each part is its own commit (tests green, captures taken, reviewed). Tier 1 first; Tier 2 if the night allows; Tier 3 last.

### Tier 1

**1. Respawn bubble vs. Shield ultimate.**
- Respawn bubble: a **holographic bubble in the player's team colour**: see-through, brighter at its rim, thin horizontal
  scan-lines drifting upward, a slight flicker. Same size as today (Shield diameter x bubble scale, a hand-over, never a
  number), same build/destroy driver, still `VisibleWhenSeen.AttachToCaster`, still no collider.
- Shield: a **gold shell of hexagon plates with dark seams**, mostly opaque so the player is just visible through the seams.
  While armed, a **gold hex ring** on the floor (replaces the round cyan ring). On the trigger the plates **snap up from the
  ring** in about 0.15 s. Size unchanged (`shieldDiameter`). Colour read from `UiTheme.immuneBarColor` (the two cyan colour
  fields on `Invulnerability.prefab` are redirected; the brief wrongly put them on the `.asset`).
- Unchanged: `BlocksHit`, `StopsEnemyEffectFrom`, the `dShs`/`dShd`/`dBlk` keys, the trigger window, `ShieldWindowRule`, the
  phase protocol on the existing cast RPC.

**2. Ownership on the floor and walls.**
- Floor: dark and glossy with a faint hex grid (tile 3 m by default). A tile whose centre lies within a band of a zone's
  **live** capture radius (default: one tile past the ring) fills softly with that zone's colour as shown by
  `OwnerPaintColours.For`; neutral tiles show edge lines only; tiles outside every band stay plain. The fill fades outward.
- Walls and blocks: dark and glossy with **emissive circuit stripes**: straight lines, 45-degree turns, small square nodes,
  one stripe along the top edge so the shape reads from above. Within a zone's band the stripes take the owner's colour,
  else a dim white. Barriers (the low orange cover) turn glossy with no stripes, so cover reads as cover.
- The owner shown is the minimap's answer (ZoneKnowledge first, else the live snapshot): move `MinimapView.OwnerShown` into a
  shared helper both call. Floor and minimap always agree, including a zone whose true owner this team does not know.
- Repaint only on `BuildingManager.OwnershipChanged`, a ZoneKnowledge version change, an out-of-play change or a radius
  change, plus once at start and on a late join; never per frame.
- Defaults (Tudor: keep the defaults, he checks in the morning): tiles change on an owner flip, not while a capture is in
  progress; no pulse while under attack; nearest zone wins where bands overlap; out-of-play zones use the out-of-play paint;
  Dominion spawn areas take their team's colour like their towers.
- The arena is baked into `Game Scene` and `Dominion 2v2` (220 x 220 m and 96 x 56 m floors): change the four arena
  materials in place, no rebuild. The pattern is world-space (the cubes are non-uniformly scaled), works under Batching
  Static (no property blocks), writes depth (the fog pass darkens it for free).

**3. Mech team colours.** The purple mech is black because its team material's base colour is (0, 0, 0), not because of the
model. New per-team mech materials (never edit `PBR team 1.mat`, which the source character shares): base colour that reads
as the team, and the model's glow lines (orange on all three today) recoloured to the team colour. Model unchanged.

**4. Stun Gun, Sonic Pulse, Flamethrower.**
- Stun Gun: a **small electric-blue bolt** (a sharp, elongated faceted shape, smaller than today's 0.6 m ball) with a short
  crackle; on hit a blue burst; on the victim **electric-blue crackling arcs** for as long as they are stunned, read from the
  stun status every client already has. The STUNNED label turns electric blue.
- Sonic Pulse: two or three **electric-blue see-through arc bands** sweeping from the caster over the exact hit cone (its live
  range and angle) in about 0.2 s, then fading. Today nothing is drawn.
- Flamethrower: keep the flat fan (it shows the hit area); add short **upright flickering flames** along it, orange to
  yellow, and a few embers.

**5. Guns.** Shots keep their team colour.
- Every bullet: a **sharp elongated slug** instead of a ball, white-hot core, team-colour trail, a **small light** under
  shots the viewer can see (capped to a few at a time so SMG fire stays cheap).
- Every gun: a small sharp **muzzle flash** (white).
- Impacts per family: Baseline a white flash disc and sparks; SMG small quick sparks, no light; Burst one flash per round, the
  Bounce upgrade a ricochet chevron; Rocket a flat shockwave ring sized to its **real** blast radius plus a flash; Laser a
  scorch spark, the Mark upgrade a mark glyph, the Through Walls upgrade a flicker where the beam enters and leaves a wall.
  Today all 13 weapons and 9 projectile prefabs share one puff (`FXBulletExplosion.prefab`). Pool the flashes.

**6. UI (Tudor's extra asks).**
- Death: a small card at the top centre, **"RESPAWN IN 4"** counting down, in the current card style; it sits **under the
  shop** so a dead player sees and clicks the whole shop; no white wash; a light dark edge on the world says you are dead.
  The waiting screen (no respawn coming) gets the same card.
- Old screens move to the current card style (the round bar, break card and result card are the reference): You Win / You
  Lose, the Escape pop-up, Connection Lost, the log-saved box, the Quit buttons.
- The Escape pop-up gains a **master volume slider** (everything, the gunfire people complained about included), remembered
  between runs. No music work (Tudor turned the music off).
- One table of canvas sorting orders with a test, so a panel can't cover the shop again by accident.

### Tier 2

Dash: a straight silver streak and flat chevrons on the floor (today's cyan trail reads as the cyan team). Blink: silver
triangle shards where you left, a hex ring snapping out where you land. Zip Gun: a small harpoon head and a straight segmented
silver cable with chevrons running toward the target (today's rope is yellow, too close to gold). Portal: a flat team-colour
hex gate on the floor with a slowly turning hex inside. Mines: a flat hex puck with a blinking team light; the trigger ring
stays. Cover: a glossy panel like the walls with team-colour stripes on its edges. Electric Fence: angular pylons with jagged
team-colour arcs between them. AoE Zone: a red low hex-grid ring around the caster, its cells flickering. Raybeam: one thick
hard-edged beam, white-hot core, red sheath, a hex shrinking at the gun while it charges, a scorch flash where it ends.

### Tier 3

Sprint: short silver speed-lines behind the feet (seen by others only if the game already shares that you are sprinting;
otherwise only you). The flat team hex under each player's feet, gated by `VisibleWhenSeen`, switchable. A short tile ripple
outward when a zone flips. **Scope: nothing** (Tudor).

## Constraints

- Every game number and rule stays: damage, ranges, cone sizes, timings, **hit radii and colliders** (a visual swap never
  changes a projectile's or a placed object's collider). Looks are drawn from the numbers the gameplay reads.
- No `[PunRPC]` added, renamed or removed; every look comes from state every client already has.
- Every tunable in `UiTheme` or a new `Assets/Gameplay/Config/ArenaLook.asset`, with a plain full-sentence tooltip in play
  terms, written to the YAML and grepped.
- Tests guard rules, not Tudor's numbers (common-rules): the tile-to-zone rule, the tile colour equals `OwnerPaintColours.For`,
  one look-table entry per ability id 14-28 with the debug pings 901-903 exempt and no two sharing shape and colour family,
  the bubble and Shield differing in both, prefab guards (the pulse has a view, the stun bullet is not the built-in sphere),
  `ArenaLook` YAML fields, the bubble size as a hand-over, the sorting-order table, the volume rule. Each fails first.
- Blender for the small models (stun bolt, bullet slug, harpoon head, mine puck, fence pylon, others where a code mesh is
  worse): under 300 triangles each, FBX into `Assets/Gameplay/Art/`; Blender code may save files there (Tudor, 20 Oct).
  Check every model from **at least two angles**, in Blender and in Unity. Procedural shapes (hex shell, hex tiles, rings,
  arcs) may be code meshes (`Arena/HexPrismMesh.cs` is the model).
- Performance: frame time before and after with nine dummies and every effect up; nine-player SMG fire with lights at
  today's rate or better.
- Common-rules in full: one Editor-driving agent at a time under the lock, never the real mouse/keyboard/focus, Dev1-Dev4
  builds only, captures to `Resources/captures/2026-10-20-visual-pass/`, small commits on `visual-overhaul`, push, never
  `main`.

## Non-goals and anti-goals

Non-goals: melee and mage designs; any gameplay number; the minimap look; the HUD beyond the screens named in part 6;
replacing the mech model; music; anything for the Scope.

Anti-goals (a failure even if it works): a floor or wall bright enough to hide a player or a shot; team colour on an ability
effect that nobody owns; soft round Tron blobs; a frame-rate drop in a nine-player SMG fight; a death card that covers any
part of the shop; a look that differs between clients.

## Added 20 Oct, morning (Tudor)

**Capture towers match the arena.** Every capturable zone tower (Conquest zones and capitals, Dominion lane zones) becomes an
eight-sided faceted tower (six sides stay the Dominion spawn's mark): a dark glossy body with circuit stripes like the
walls; the crown, the base ring and the stripes glow in the owner's colour; a neutral tower stays dark with unlit lines;
contested red and the under-attack pulse stay on the crown. Size and collider unchanged. **Dominion spawn towers stay as
they are** (solid team colour, six sides, corner columns), so they never read as capturable.

## Calls made after the builder check (the session's, not Tudor's; he sees them in the report)

- **Neutral vs. the White team:** neutral stripes and tile edges are **unlit** (a slightly lighter grey line, no glow);
  owned ones glow in the owner's colour. Lit vs. unlit separates neutral from White, not brightness alone.
- **Death card while shopping:** the card is drawn under the shop and never overlaps the shop's panel: top centre while the
  shop is closed, and placed outside the shop's footprint (below it, or bottom centre above the ability bar) while it is
  open. The "capital under attack, you respawn at Tier 2" note moves onto the card.
- **The look table** is a data asset with one target-look entry per ability id 14-28 (shape, colour family, colour), the
  debug pings exempt and Scope as "no look". Effects read their colour from it as they are rebuilt (so it is wired, not a
  list beside the code); an entry whose effect is not rebuilt yet is named in the report. Tests check the table.
- **Lighting:** glossiness and reflections are controlled in the arena shaders (a reflection-strength tunable); scene
  lighting and post-processing are edited only if a capture shows the shader alone cannot keep players readable, and then in
  both scenes with the clean-scene steps.

## Left to the builder

Exact shades inside each family, Shader Graph vs. hand-written HLSL, code mesh vs. Blender for each shape, effect timings
within the stated feel, how the ownership data reaches the shaders (a small texture or global arrays) as long as the decision
lives in a tested C# rule the game calls, and the order inside a tier.
