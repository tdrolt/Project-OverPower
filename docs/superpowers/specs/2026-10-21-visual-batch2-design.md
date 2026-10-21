# Visual pass, batch 2: Tudor's review of the first pass - design

**Date:** 2026-10-21. **Branch:** `visual-overhaul` (from `efe758b`). **Source:** Tudor's review in chat (21 Oct, two rounds) of the
first pass (`docs/superpowers/specs/2026-10-20-visual-overhaul-design.md`, report `Resources/captures/2026-10-20-visual-pass/report.md`).
Everything in the first design stays unless this file changes it. Tudor approved this list ("Go", 21 Oct ~15:35).

## The colour language, revised

| Family | Means | Used by |
|---|---|---|
| Team colour: **Orange** (was White), Purple, Cyan | owned by / cast by that team | floor tiles, wall stripes, towers, rings, mechs, feet hexes, respawn bubble, shots and trails, placed objects, **the team accent on every ability** |
| Gold | the Shield ultimate | shell, armed ring |
| Electric blue | Stun Gun, Sonic Pulse | bolt, arcs on a stunned player, pulse arch, STUNNED label |
| **Grey with the caster's team tint** (was silver) | movement | Dash, Blink, Sprint, Zip Gun, Teleport |
| **Hot yellow-white** (was orange) | fire | Flamethrower flames, the Rocket Cursor Fire's burning ground |
| Red | damage | Raybeam, Electric Fence arcs |
| **Green** | healing | the Heal Zone (was the AoE Zone), health packs, the heal effect on a player |
| White | a hit, a shot leaving, neutral | impacts, muzzle flashes, bullet cores, the Conquest scan wave and its countdown |

**Team orange:** Tron orange (1.0, 0.45, 0.05). The team's name becomes "Orange" everywhere it is shown (lobby seats, scoreboard,
break and result cards, info pages). Neutral stays grey and unlit.

**Team accent (Tudor's idea):** the ability's own colour family says *what* it is; a thin accent in the caster's team colour says
*whose* it is, so three players of three teams fighting together stay readable. The accent is a trail, an edge or a rim, never the
main body; hits and impacts never carry it. Where it goes: Stun Gun - the bolt's crackle trail (the arcs on a victim stay blue, they
show the victim's state); Sonic Pulse - the arch's edge; Flamethrower - the outline of the floor fan; Shield - a thin ring at its
base; Raybeam - the beams' outer edges and the charge hexagon; Heal Zone - its outer ring (it heals only that team); movement - the
tint itself; placed objects and bullets already carry it (the fence arcs are red, their posts keep the team colour).

## Changes

**Looks**
- Flamethrower: the flames jet from the gun at chest height and spread over the cone; the floor fan stays as a faint hit-area marker
  with the team-colour outline; fire is hot yellow-white.
- Rocket Cursor Fire: its burning ground (`FireField`) uses floor flames (the old team-colour disc goes).
- Zip Gun harpoon about 1.5 times bigger; the stun bolt back to about the old ball's size (still bolt-shaped, brighter core).
- Bullet lights for every gun: wider and fainter, so they never read as a misplaced bullet; then a second look at the rocket.
- Electric Fence arcs red; posts keep the team colour.
- Shield shell bigger (about 2.6 m) so the mech fits inside; the respawn bubble keeps its size.
- Teleport's grey spheres become hex rings in the movement look.
- Sonic Pulse: an arch standing upright on the floor, launched from the caster across the cone to its real 5 m reach in about 0.2 s,
  its span always the cone's width at its distance (about 5.8 m at 5 m); electric blue with the team-colour edge.
- Short walls (the low barriers): dark glossy like the arena with a grey top stripe, so they still read as low cover.
- Health packs: a hexagon pad on the floor with a glowing green cross floating above it, slowly turning, dim while respawning.
- Heal effect: green plus-shaped sparks rising around any player whose health is going up, from any source (tier zones, the respawn
  zone, the Heal Zone, health packs), for as long as the healing lasts; drawn from the health every client already sees (no new
  network message), hidden in the fog like the player.
- Dominion spawn towers: no attack flicker while an enemy stands in the spawn zone (spawns cannot be taken).
- Bounty label: shown only while its tower is on your screen (no pinned label at the screen edge).
- The Conquest scan wave and its countdown text turn white.

**Rules (Tudor's game-rule changes, 21 Oct)**
- **Heal Zone** (the AoE ultimate, id 27): heals the caster and teammates inside by 14 health per second for 6 s (the old damage
  numbers turned into healing), never above full health, health only (not armour); enemies are no longer hurt; the throw to the
  cursor stays; it stacks with zone regeneration and health packs; shown as "Heal Zone" in the shop; green with the team-colour ring.
- **Dominion picks after the buy window:** the shop takes picks anywhere, any time in a round, with the same limits as the break
  (weapon depth and armour by round, one ability per slot, swaps as in the break).
- **Raybeam wind-up:** 0.4 s from pressing to firing; a red hexagon with the team-colour edge shrinks at the gun; the caster can move
  and aim during it; the beams fire at its end; a stun or death cancels it. The three hit lines draw as one merged beam.
- **Conquest recess health pack:** when the map shrinks, one health pack appears in the middle of each recess the cut creates, with
  the same heal and respawn as the other packs.

**Arena geometry**
- Overlapping wall pieces are rebuilt so no two overlap (the new shader shows overlaps): the perpendicular high walls and the
  plus-shaped small walls of the 2v2 arena, the corner piece next to the Tier 4 walls in the 3v3v3 arena; that corner piece is made
  symmetrical. The playable shape and the colliders' footprint stay the same except where the symmetry fix moves an edge.

**Small fix:** the chat's shutdown warning ("Some objects were not cleaned up ... Chat client reaper"): no helper object is made
while the game is quitting.

**GDD:** a new local copy `Resources/gdd/POP GDD - 2026-10-21.docx` with the visual pass and these rules; the 10 Oct file untouched.

## Constraints (as in the first design)

No [PunRPC] added, renamed or removed (a new cast phase on an existing RPC is allowed for the Raybeam wind-up, as the Sprint start
phase was); every look from state every client already has; tunables in `ArenaLook` / `UiTheme` with play-term tooltips, in the YAML;
tests guard rules, not numbers; colours judged in HDR (Game view or a Dev player), never on the LDR kit renders alone; the rules
sheet in full.
