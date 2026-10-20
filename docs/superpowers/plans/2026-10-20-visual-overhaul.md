# Visual overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tron materials on hard Halo/mecha shapes, ownership on the floor and walls, one look per ability and gun, the old UI
screens restyled, the death card under the shop and a master volume slider, without changing a game number or an RPC.

**Architecture:** Pure C# rules decide every look (tested in edit mode) and the components call them; one `ArenaLook` asset
holds the new world-look numbers and the per-ability look table, `UiTheme` holds the semantic colours; the arena gets one
hand-written URP Lit-based surface shader fed by shader globals (an owner-tile texture painted only on a change), effects are
LineRenderers, particles and small code or Blender meshes, every transparent gated by sight as today.

**Tech Stack:** Unity 6000.0.70f1, URP 17.0.4 (Forward+, linear), Photon PUN 2, NUnit edit-mode tests, Blender 5.2 via MCP.

**Spec:** `docs/superpowers/specs/2026-10-20-visual-overhaul-design.md`. Architecture decided by a judge panel (three
proposals, three judging lenses, one synthesis), reproduced from "Execution" on; its working copy and the code map are in
`Resources/captures/2026-10-20-visual-pass/` (`architecture.md`, `code-map.md`).

## Global Constraints

- Every game number and rule stays; colliders, `ProjectileRadius`, hit radii and prefab root scales stay.
- No `[PunRPC]` added, renamed or removed (RpcList index-based, 45 entries); every look from state every client already has.
- Every tunable in `UiTheme` or `Assets/Gameplay/Config/ArenaLook.asset` with a full-sentence play-term tooltip, written into
  the YAML and grepped.
- Tests guard rules, not Tudor's numbers; each new test seen failing first; each wiring point proven (IL `Uses` /
  `ResultDecidesABranch` or a recorder row).
- Colour language: team colour = owned; gold = Shield only; electric blue ≈ (0.2, 0.45, 1) = Stun Gun and Sonic Pulse;
  silver = movement; orange-yellow = Flamethrower; red = AoE Zone and Raybeam; white = impacts, muzzle flashes, cores.
- Neutral stripes and tile edges are unlit lines; owned ones glow (lit vs. unlit separates neutral from the White team).
- Common-rules in full (`Resources/session/briefs/common-rules.md`): one Editor-driving agent at a time under the
  controller's lock; never the real mouse, keyboard or focus; Game Scene open and clean before every test run; no edits in
  Play Mode; Dev1-Dev4 builds only; captures to `Resources/captures/2026-10-20-visual-pass/`; branch `visual-overhaul`, never
  `main`; comment style per common-rules.
- Blender models under 300 triangles, FBX into `Assets/Gameplay/Art/`, `.blend` sources in `Assets/Gameplay/Art/Source~/`,
  every model checked from at least two angles in Blender and in Unity.

## Review Focus

1. A late joiner, a rejoin, or a Dominion round reset sees the right floor and wall colours on its first painted frame, and
   the lobby (no match) draws the arena plain: test `OwnershipSignatureTests.FirstFrameAfterRegistrationRepaints` and
   `ArenaShaderGlobalsTests.ClearOwnerTilesMarksInvalid` (task C5), plus the late-join recorder row.
2. Nothing new gives away a player hidden in the fog: no shot light, bubble, arc, pulse band, flame tongue or impact for an
   unseen player: `ShotLightRuleTests.OnlyShownShotsQualify` (C7) and an IL check that each new view goes through
   `VisibleWhenSeen` before it shows (C2, C6), plus one fog capture per task.
3. Play Mode leaves no shared `.mat`, `.asset` or prefab dirty: the after-run `git status --short` check in every task's
   capture step, and colours only through globals, runtime instances or property blocks.
4. The death card never covers the open shop at 1280x720 or 1920x1080, and shop clicks work while dead:
   `DeathCardRulesTests.NeverOverlapsTheOpenShop` with both resolutions, and a capture with the shop open (C9).
5. Switching Game Scene and Dominion 2v2, or a domain reload, leaks no runtime mesh, texture or material:
   `ShieldShellViewTests.DestroyFreesTheMesh` (C2) and `ArenaOwnershipPainterTests.DestroyFreesTheTexture` (C5), counted by
   object name.

## Execution order (controller's call; Tudor reviews in the morning)

C1 foundation → C2 bubble vs Shield → C3 mech colours → C9 death card and sorting table → C10 old screens and master volume
→ C4 arena surfaces (timebox about 3 hours) → C5 ownership paint → C6 Stun, Pulse, Flame → C7 guns A → C8 guns B → Tier 2
(Dash, Blink, Zip, Portal, Mines, Cover, Fence, AoE Zone, Raybeam) → Tier 3. Reason: the two playtest confusions and Tudor's
explicit UI asks land first and are low-risk; the arena shader is the riskiest piece and is timeboxed.

## Per-task procedure (every task)

- [ ] **Step 1: Implementer (the only Editor driver) writes the failing tests** named in the task, runs the edit-mode suite
  (`run_tests -- --mode editor --async_tests true`, Game Scene open and clean, one-line polls), records the failures.
- [ ] **Step 2: Implement** the files and signatures in the task; write new serialized fields into the YAML and grep them.
- [ ] **Step 3: Full edit-mode run**: all green (count recorded); `recompile_status` clean.
- [ ] **Step 4: Captures** with the capture kit (`Resources/captures/2026-10-20-visual-pass/kit/README.md`), HUD hidden,
  default zoom, plus the task's extra angles; each image looked at and described honestly; `git status --short` shows only
  the task's files after Play Mode.
- [ ] **Step 5: Adversarial review** (read-only reviewers: rules and correctness, look vs. the spec from the captures, tests
  that can fail and wiring); confirmed findings fixed by the implementer; one more full run after any change.
- [ ] **Step 6: Controller commits** the task's files by path (`git commit -F`, Co-Authored-By trailer), pushes
  `visual-overhaul`, and appends the task's section to `report.md`.

## Execution: the decisions

**Base:** proposal "risk", which had the best total score (8 + 8 + 6.5) and the most accurate code facts. Grafted in from "minimal": its commit order (cheap visible wins before the HLSL), the Goldberg shell, the `Source~` folder for .blend files, stun arcs seeded so every client draws the same ones, and the checker-pattern parity capture. Grafted in from "system": the grid origin at `ArenaSymmetry.centre`, a dedicated neutral line colour, and the per-surface globals. The approved looks are unchanged. Tudor's 20 Oct calls are in: Stun and Pulse are electric blue, the bubble takes the team colour, AoE Zone and Raybeam are red, and the Scope gets nothing.

**Facts I re-checked myself (20 Oct):**
- `ArenaSceneShaderIntegrityTests.cs:24,53` requires the `"Universal Render Pipeline/"` prefix.
- Every prefab in `Gameplay/Projectiles/` has 0 colliders. Hits use `ProjectileMotor.cs:9,103` (SphereCast).
- The bubble material is a per-instance field (`RespawnShield.cs:36`, destroyed at :78, `Shader.Find` at :292).
- Sorting orders: chat `MatchOrder = -1` (ChatRules.cs:58), shop -5 (LoadoutScreen.Builder.cs:385), `DominionHud.SortingOrder = -6` (:27), SpectatorResultCard 5.
- `captureRingNeutralColor = (1,1,1,0.35)` (UiTheme.cs:621). `statusStunnedColor` is the same gold as `immuneBarColor` (:248 vs :88) and is read only at StatusLabelOverhead.cs:117 and PlayerHud.cs:1125.
- Linear colour space (ProjectSettings:50). TeamSight.cs:214 converts with `.linear`. Enter Play Mode options are 0, so domain reload is on.
- `RocketBlastView` is already sized to `SplashRadius` and gated by `BlastShownAt` (:55).
- **The muzzle flash path already exists and is gated**: WeaponFiring.cs:479-480, `fired.MuzzleVfx` behind `ShotShownAt`. All 13 weapons point at `FXShot.prefab` (muzzle) and `FXBulletExplosion.prefab` (impact).
- Shots get `VisibleWhenSeen.Attach(projectile, team)` (WeaponFiring.cs:711, StunGunAbility.cs:94), and `VisibleWhenSeen.Shown` is public (:32).
- `PlayerTeamAppearance` already swaps `teamMaterials[3]` by `sharedMaterial` (:58-70). Team materials 1-3 have base (1,1,1) / **(0,0,0)** / (0,1,0.95) and an orange `_EmissionColor` (1.74,1.15,0) over `PBR_Free_EM.png`.
- `Sonic Pulse.prefab:53-54` holds 5 m / 60°; the script defaults are 4 / 90.
- `ExecuteCast` runs on every client (SonicPulseAbility.cs:107-109).
- `MatchSystems.prefab` is in both scenes, and so is `ArenaSymmetry`, which is used at runtime (BlinkAbility.cs:101).
- `HexPrismMesh` corners use sin/cos(yaw), so a corner points to +Z.
- `OwnerPaintColours.For(paint, theme, neutral, t)` takes the neutral colour as a parameter.
- `IProjectileBehaviour` has no per-frame hook, but `ProjectileMotor.Direction` is public.
- Flame fan: `AbilityVisualStructureTests:293` pins it to Particles/Unlit, and `Flamethrower Cone.prefab` is instantiated before `AttachToCaster` (FlamethrowerAbility.cs:271).

## 1. File layout (N = new, C = changed)

**Data (`Assets/scripts/Arena/`, namespace `Overpower.Arena`)**
- N `ArenaLook.cs`: `sealed class ArenaLook : ScriptableObject`.
  - Fields: every new world-look number, with a play-term tooltip.
  - `SurfaceLook[] surfaces` (floor, wall, block, barrier: base tint and smoothness).
  - Materials and meshes: `hologram`, `shieldPlates`, `fxAdditive`, `fxAlpha`, `mechTemplate`, `stunBoltMesh`, `bulletSlugMesh`.
  - `List<AbilityLook> abilityLooks`, `AbilityLook respawnBubbleLook`, `bool TryGetLook(int abilityId, out AbilityLook l)`.
  - `OnValidate` calls `ArenaShaderGlobals.Push(this)`.
- N `AbilityLook.cs`:
  - `[Serializable] struct AbilityLook { int abilityId; LookShape shape; LookFamily family; }`
  - `enum LookFamily { None, Team, Gold, ElectricBlue, Silver, Fire, Danger, White }`
  - `enum LookShape { NoLook, Streak, Shards, SpeedLines, HexGate, HarpoonCable, HexPuck, GlossyPanel, FlameFan, Bolt, ArcBands, Beam, HexShell, ArcPylons, HexGridRing, HologramSphere }`
- N `AbilityLookRules.cs`: `static List<string> Problems(IEnumerable<int> catalogueIds, IReadOnlyList<AbilityLook> table, ISet<int> exempt)`. It reports a missing id, a duplicate id, or a shared (shape, family) pair, with NoLook excluded.
- N `LookColours.cs`: `static Color For(LookFamily f, int team, UiTheme t)`.
  - Team → `ShotColorFor`, Gold → `immuneBarColor`, ElectricBlue → `statusStunnedColor`.
  - Silver, Fire (core and edge), Danger and White → new UiTheme fields.
- N `HexGrid.cs`: pointy toward +Z, as in HexPrismMesh. `Vector2Int TileAt(Vector2 xz, Vector2 origin, float size)`, `Vector2 CentreOf(Vector2Int axial, Vector2 origin, float size)`, `Vector2Int Texel(Vector2Int axial, Vector2Int min)`.
- N `FloorTileRule.cs`: pure.
  - `readonly struct ZoneBand { Vector2 centre; float radius; OwnerPaint paint; }`
  - `static OwnerPaint FloorPaint(int ownerShown, bool spawn, int spawnTeam, bool outOfPlay)`. It never pulses: `PulsesToWarning = false`, `PulseTeam = -1`. Spawn wins over ownerShown.
  - `static int ZoneFor(Vector2 tile, IReadOnlyList<ZoneBand> z, float band)`: the nearest centre among the bands that contain the tile, else -1.
  - `static float Fill(float dist, float radius, float band, float inside, float edge)`.
  - `static TileLook LookOf(Vector2 tile, IReadOnlyList<ZoneBand> z, float band, ArenaLook look, UiTheme t)`. The colour is exactly `OwnerPaintColours.For(paint, t, t.arenaNeutralLineColor, 0f)`.
- N `OwnerTileTexel.cs`: `Color32 Encode(TileLook)` and `TileLook Decode(Color32)`.
  - RGB = `colour.linear`.
  - Alpha band: 0 = plain, 64 = neutral (unlit edges), 128-255 = owned, with fill 0..1 mapped to 128..255. An owned tile at the faded band edge still glows.
- N `OwnershipSignature.cs`: `struct ZoneState { int ownerShown; bool spawn; bool outOfPlay; float radius; Vector2 centre; }` and `static bool NeedsRepaint(ZoneState[] painted, ZoneState[] now, int count)`.
- N `ArenaShaderGlobals.cs`:
  - `static ArenaGlobals From(ArenaLook)`: the hand-over, tested like `FlowNumbersOf`.
  - `static void Push(ArenaLook)`: sets `_ArenaSurfaceBase[4]`, `_ArenaSurfaceGloss[4]`, line widths, glows, reflection and `_ArenaLookReady = 1`.
  - `SetOwnerTiles(Texture2D, Vector4 grid, Vector4 min)` and `ClearOwnerTiles()`.
- N `ArenaOwnershipPainter.cs`: MonoBehaviour on **MatchSystems.prefab**, with `[SerializeField] UiTheme theme`. Its LateUpdate builds `ZoneState[]` with no allocation, and calls `Repaint()` only when `NeedsRepaint` says so.
- N `ShieldShellMesh.cs`: `const string Name = "Shield Shell"`, `Mesh Create(float seamFraction, out PlateRange[] plates)`. It builds a Goldberg shell (§4), unit diameter.
- N `ShieldShellRise.cs`: pure. `Vector3 PlateOffset(Vector3 plateCentre, float t01, float ringRadius, float ringY, float stagger)` moves each plate rigidly from the floor-ring point under it to its rest place, lowest plates first.
- N `Editor/Arena/ArenaLookEditorGlobals.cs`: `[InitializeOnLoad]` loads the asset and calls `Push`. It touches shader state only, so the Scene view shows the look without changing any scene object.

**Vision.** N `scripts/Vision/ZoneOwnerShown.cs`: `static int Pick(bool known, int knownOwner, int live)` and `static int Of(TerritorySnapshot s, int zone)`. This moves `MinimapView.OwnerShown` (:694). C `MinimapView.cs:717,722`: both call sites use `ZoneOwnerShown.Of`.

**Abilities and effects**
- C `Dominion/RespawnShield.cs` (BuildBubble 262-286):
  - The sphere stays. The material is a per-instance `new Material(look.hologram)` (existing pattern, freed at :78), and `_BaseColor` comes from N `Abilities/Visuals/RespawnBubbleLook.cs`.
  - `RespawnBubbleLook` provides `static float Diameter(float shieldDiameter, float bubbleScale)` and `static Color ColourFor(int team, UiTheme t)`. A team of -1 falls back to the theme's unknown colour.
  - `UiTheme.respawnShieldColor` is deleted once grep shows no reader.
- C `Abilities/Ultimate/InvulnerabilityAbility.cs`:
  - `ShowShield` (328-355) builds the shell. The renderer uses `look.shieldPlates`, and gold goes on through a MaterialPropertyBlock (`_BaseColor`, `_EmissionColor`). The shell is not static, so the property block is fine.
  - N `Abilities/Visuals/ShieldShellView.cs` runs the 0.15 s rise in Update and frees the mesh in OnDestroy. The existing `Destroy(activeShield, invincibleSeconds)` stays.
  - `SetRing` (222-249) uses `VisualTint.FillFlatCircle(ring, r, 6)`, which draws a hexagon. Its colour comes from `LookColours.For(Gold)`.
  - The `shieldColor` and `armedRingColor` fields are removed. This also fixes today's per-cast material leak at :338.
- N `Abilities/Visuals/StunArcsView.cs`: created beside the overhead label (PlayerStatusEffects.cs:119).
  - It owns a **child** "Arcs" GameObject with 3 LineRenderers (FX Additive, vertex colour), built before `AttachToCaster`.
  - LateUpdate toggles the child, never itself, by `StunArcsRule.Show(StatusLabel)`.
  - Zigzags are re-seeded at ~15 Hz from `PhotonNetwork.ServerTimestamp / 66 + actor`, so every client draws the same arcs.
- N `Abilities/Visuals/SonicPulseView.cs`, a component on `Sonic Pulse.prefab`:
  - `Play(Vector3 origin, Vector3 fwd, float range, float angle, int casterTeam, PhotonView caster)` draws 2-3 arc LineRenderers (FX Alpha).
  - Band radius over time: `AbilityVisualGeometry.ArcBandRadius(i, t, sweep, range)`. Points: `AbilityVisualGeometry.ArcPoint(range, angle, k, n)`.
  - It goes through `AttachToCaster` before it shows.
  - C `SonicPulseAbility`: public `ConeRange` / `ConeAngle` getters, and `ExecuteCast` calls `view.Play` on every client, with no RPC.
- C `Flamethrower Cone.prefab`: a child ParticleSystem "Flame Tongues" (vertical billboards, triangle texture, embers) on FX Additive. The fan's material is untouched. N `FlameLook.Sector(float coneAngle)` gives the emission arc.
- N `Weapons/Effects/LookTint.cs` (IProjectileBehaviour plus a public `Apply`): `[SerializeField] UiTheme theme; int abilityId;`. OnSpawned sets `LookColours.For(...)` on its renderers through a property block. Used by the stun bolt and other non-team shots.
- N `Weapons/Effects/ShotFacing.cs`: in LateUpdate, `if (ShotFacingRule.NeedsTurn(transform.forward, motor.Direction)) transform.rotation = LookRotation(dir)`. Visual only; the motor moves along `direction`.
- N `Weapons/Effects/ShotLightPool.cs` on MatchSystems.prefab: `shotLightCap` shadowless point lights, created once.
  - `ShotTeamVisuals` registers in OnSpawned and unregisters in OnExpired.
  - Each LateUpdate, `ShotLightRule.Pick(IReadOnlyList<ShotCandidate>, int cap, List<int> chosen)` chooses: only shots whose `VisibleWhenSeen.Shown` is true, nearest to the camera first.
- C `VFXManager.cs`: a pool per prefab, with `FxPoolRule.Take(int live, int cap)` deciding when to recycle the oldest. Instances are deactivated instead of `Destroy`.
- C `RocketBlastView` / `SplashShell`: restyled into a flat shockwave ring. They are already sized to the real radius.
- C `Player/PlayerTeamAppearance.cs`: Apply uses `MechMaterials.For(team, theme, look)`.
  - That returns 3 cached runtime instances of `look.mechTemplate`, DontSave, named "Mech Team N", swept at SubsystemRegistration.
  - N `Player/MechLook.cs`: `static Color Base(Color team, float mix)` and `static Color Glow(Color team, float strength)`.
  - The `teamMaterials` array is retired. `PBR team 1.mat` is never edited.

**UI**
- N `UI/UiLayers.cs`: the full table of today's orders.
  - -21 scan and bounty, -20 hit feedback, -10 HUD, spectator, warmup and shop toggle, -9 minimap, **-8 death vignette, -7 death card**, -6 Dominion HUD, -5 shop, -1 chat (match), 0 result panels, 5 spectator result, 10 match log, 100 lobby, 101 chat in the lobby room, 500 F1, 700 quit, 900 connection lost.
  - Theme-driven: scoreboard 30 and Dominion result 5.
  - Existing consts (`ChatRules.MatchOrder`, `DominionHud.SortingOrder`, `WarmupBar.SortingOrder`, `LobbyUiKit.CanvasSortingOrder`) become `= UiLayers.X`.
- N `UI/DeathCard.cs`: built with `LobbyUiKit` on its own canvas at -7 with raycasts off, plus a vignette canvas at -8. On the waiting card, only its Quit button takes raycasts.
- N `UI/DeathCardRules.cs`: `static string Text(string format, float secondsLeft)` (rounds up) and `static Rect Place(bool shopOpen, Rect shop, Rect topReserved, Vector2 screen)`. `topReserved` is the Dominion HUD band, empty outside Dominion.
- C `MatchUI.cs` (102, 283): shows the card instead of the old panels. The respawn and waiting prefab panels stay inactive and are never deleted (their fileIDs are referenced).
- N `UI/MasterVolume.cs`: `Clamp`, `Load(Func<string,float,float> get)`, `Apply` (sets `AudioListener.volume`) and a `[RuntimeInitializeOnLoadMethod]` hook. C `QuitConfirmPanel.cs` (125-180) gets a slider row that saves to PlayerPrefs only on release.

**Assets**
- `Gameplay/Config/ArenaLook.asset`.
- `Gameplay/Arena/Shaders/ArenaSurface.shader` and `ArenaHex.hlsl`.
- `Gameplay/FX/Shaders/Hologram.shader`.
- `Gameplay/FX/Materials/`: Hologram, Shield Plates (URP Lit), FX Additive and FX Alpha (URP Particles/Unlit), Mech Team (template with a greyscale glow mask).
- `Gameplay/FX/Impacts/*.prefab` and `FX Muzzle Flash.prefab`.
- `Gameplay/Art/StunBolt.fbx` and `BulletSlug.fbx`; Tier 2 FBX later.
- `Gameplay/Art/Source~/*.blend`. The `~` keeps Unity's Blender importer off them, and common-rules:29-30 allows Blender to save "where the work needs them".

## 2. Ownership data path

- **Inputs per zone:**
  - centre: `BuildingManager.TryGetZoneCentre` (:294)
  - live radius: N `BuildingManager.CaptureRadiusOf(zone)`, reading `captures` (:63) → `BuildingCapture.CaptureRadius`
  - owner shown: `ZoneOwnerShown.Of`
  - spawn: `IsSpawnZone` (:205), with `Map.CapitalTeamOf` as the spawn team
  - out of play: `MatchDirector.IsOutOfPlay` (Live.cs:96)
  - `FloorTileRule.FloorPaint` turns these into the `OwnerPaint`.
- **Transport:**
  - One `Texture2D`, RGBA32, `linear: true`, point filter, clamp, DontSave, named "Arena Owner Tiles". One texel per hex tile across the bounding box of every zone ± (largest tier radius + band + 1 tile): at most 128² in Game Scene, smaller in Dominion.
  - Globals: `_ArenaOwnerTex`, `_ArenaOwnerGrid` (origin.xz, size, valid) and `_ArenaOwnerMin`.
  - The origin is `ArenaSymmetry.centre` (found once), so the three 120° thirds paint identically. The fallback is the floor renderer's bounds centre.
  - Globals sit outside `UnityPerMaterial`, so the SRP Batcher and Batching Static both survive.
- **Shader side:**
  - From `positionWS.xz`, the shader finds the hex tile, then the texel (`LOAD_TEXTURE2D`), then decodes it.
  - With `valid == 0` (lobby, edit mode) it draws plain and never samples.
  - Walls and blocks read the tile under the pixel's xz.
- **Repaint:**
  - `NeedsRepaint` compares at most 10 structs per frame. It catches owner flips, ZoneKnowledge (shown owner and `ResetKnowledge`), out-of-play and cut radius changes, late zone registration, and a late join. The painted key starts invalid, and the first frame with `Current != null` and every zone registered paints.
  - `OwnershipChanged` (:164) and `LiveStateChanged` (Live.cs:204) also mark it dirty.
  - The texture is rewritten (one `Apply`, about 64 KB) only on a change. Capture progress is not an input.
- **No scene file changes:** the painter lives on the prefab and the four `.mat` files are shared assets.

## 3. Shaders

| Shader | How | Draws / inputs | Notes |
|---|---|---|---|
| `Overpower/Arena Surface` (Floor, Wall, Block, Barrier .mat, edited in place) | Hand-written HLSL. It includes `LitInput.hlsl`, so the CBUFFER is Lit's, and `LitForwardPass.hlsl` for the vertex shader and `InitializeInputData`. Our fragment ends in `UniversalFragmentPBR`. The pragma block is copied from `PackageCache/com.unity.render-pipelines.universal@a1907d43fdff/Shaders/Lit.shader`, including `_FORWARD_PLUS`. Passes: ForwardLit, ShadowCaster, DepthOnly and DepthNormals, all through URP includes. | `shader_feature_local _ARENA_FLOOR _ARENA_WALL _ARENA_BLOCK _ARENA_BARRIER` picks the pattern and the surface index; no per-object data. **Floor**: faint hex grid; owned tiles get fill plus glowing edges (colour × `ownedEdgeGlow`); neutral tiles get unlit lighter-grey edges. **Wall/Block**: circuit cells (straight, 45°, square node, hashed per cell) in face metres: `u = dot(posWS.xz, normalize(cross(up,n)).xz)`, `v = posWS.y`, so yawed pieces stay straight. A top-face edge line from UV0 with `fwidth`. Owned = owner colour × `stripeGlow`; otherwise dim white, unlit. **Barrier**: glossy, no stripes. Base and gloss come from `_ArenaSurfaceBase/Gloss[i]`. When `_ArenaLookReady == 0` it falls back to the material's own Lit values. | Opaque with ZWrite, so the fog pass (BeforeRenderingTransparents, rebuilt from depth) darkens it for free. Peak emission stays below ~1.5, so bloom (threshold 1) gives only a thin halo. |
| `Overpower/Hologram` (respawn bubble) | Hand-written, unlit, about 70 lines | `_BaseColor` (team), `_Rim`, `_ScanDensity`, `_ScanSpeed`, `_Flicker`, `_Intensity`, copied from ArenaLook into the per-bubble instance. Fresnel rim, upward world-Y scan lines, flicker from `_Time` plus a seed. | Transparent, **Blend One One** (order-independent: no sorting bugs overnight), Cull Off, ZWrite Off. Gated by `AttachToCaster` as today. Additive can saturate over a same-team owned floor; `_Intensity` is the tunable, judged in a capture. |
| Shield plates | URP **Lit**, opaque, **Cull Back** (`Shield Plates.mat`) | Gold base and emission set by property block | The seams are real gaps. Cull Back hides the far plates' insides, so the dark floor and the player show through: "dark seams, player just visible". |
| FX (arcs, bands, flame tongues, flashes, muzzle, sparks, trails) | URP Particles/Unlit, FX Additive / FX Alpha | Vertex colour from `LookColours` | Same pattern as Flame Cone.mat. A URP name, so it passes the integrity test. Every user is gated by sight. |

No Shader Graph: an agent cannot safely write `.shadergraph` JSON without the graph window. No `Shader.Find` for new shaders: every material hangs off ArenaLook, which UiTheme references, which the prefabs and scenes reference.

## 4. Shapes

- **Shield shell (code):** icosahedron → one midpoint subdivision (42 vertices) → dual. That gives **42 plates (30 hexagons + 12 pentagons)**, the most hex-like option. Each plate is inset by `seamFraction`. Fan triangulation gives 156 triangles in one mesh and one renderer.
  - Centred at local zero like today's sphere, scaled by `shieldDiameter`.
  - The rise moves each plate rigidly by `ShieldShellRise.PlateOffset` over `riseSeconds` (0.15), then stops.
  - Fallback if the per-plate rise misbehaves: a Y-scale rise with the bottom held on the ring (report it).
- **Armed ring:** `FillFlatCircle(…, 6)` on the existing SetRing path; no new geometry.
- **Pulse bands, stun arcs, Tier 2/3 rings:** LineRenderers. **Flames, sparks, flashes:** particles.
- **Blender FBX** (lock `-Task "Blender: <model>"`):
  - StunBolt: faceted bipyramid, about 24 triangles, smaller than 0.6 m.
  - BulletSlug: hexagonal-section slug, about 28 triangles.
  - Tier 2: harpoon head, mine puck, fence pylon. All under 300 triangles.
  - Long axis +Z, transforms applied, no materials, no colliders. Authored for the prefab root scale.
  - Each goes in as a root MeshFilter swap. Root scale, `ProjectileRadius` and the transform stay untouched.
  - **Every model is checked from 2+ angles**: in Blender with `render_viewport_to_path` (three-quarter and top), and in Unity with a `PreviewRenderUtility` render through `eval_file` (the game's 63° view and side-on). The triangle count is read in Unity. All four images are inspected and described.

## 5. Look table and tunables

| id | 14 Dash | 15 Blink | 16 Sprint | 17 Portal | 18 Zip | 19 Mines | 20 Cover | 21 Flame | 22 Stun | 23 Pulse | 24 Raybeam | 25 Shield | 26 Fence | 27 AoE | 28 Scope |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| shape | Streak | Shards | SpeedLines | HexGate | HarpoonCable | HexPuck | GlossyPanel | FlameFan | Bolt | ArcBands | Beam | HexShell | ArcPylons | HexGridRing | NoLook |
| family | Silver | Silver | Silver | Team | Silver | Team | Team | Fire | ElectricBlue | ElectricBlue | Danger | Gold | Team | Danger | None |

- `respawnBubbleLook` = (HologramSphere, Team). Ids 901-903 are exempt.
- Effects resolve `theme.arenaLook.TryGetLook(Definition.Id, out l)` and then `LookColours.For(l.family, team, theme)` when they build. An entry whose effect is not rebuilt yet is named in the report.
- **UiTheme** holds the semantic colours (one colour source):
  - team shot colours and `immuneBarColor`
  - `statusStunnedColor`: its value becomes electric blue ≈ (0.2, 0.45, 1) and its tooltip is rewritten to cover the Stun Gun and Sonic Pulse. It has two readers; no new field.
  - new `movementSilverColor`, `fireCoreColor`, `fireEdgeColor`, `dangerColor`, `flashWhiteColor`, `arenaNeutralLineColor` (lighter grey, opaque)
  - the new `arenaLook` reference
  - `bulletTintStrength`, lowered for the white-hot core
  - death card text format
- **ArenaLook** holds everything else:
  - tile size 3 m, band 1 tile, fill inside and edge, edge and stripe glow, cell, line and node sizes, top-edge width, reflection strength, `surfaces[4]`
  - hologram numbers, seam fraction, rise seconds and stagger
  - arc count and rate, pulse band count and sweep seconds, flame count
  - shot-light cap 4, range and intensity; pool caps
  - mech mix and glow
  - material and mesh refs
- Every field gets a full-sentence play-term tooltip and is written into the YAML, then grepped.

## 6. Tier 1 in commit order

Rules for every commit:
- Tests first: see the real failure, then green.
- For each wiring point, one test that fails with the wiring reverted.
- Full test run with Game Scene open and clean.
- Captures go to `Resources/captures/2026-10-20-visual-pass/`, taken in Editor Play Mode at default zoom. Two-client recorder rows only where the network matters.
- `git diff Assets/Gameplay` is checked for `.mat` files dirtied in Play Mode and for any `m_Radius`, `projectileRadius` or `m_Size` change.
- Commit locally on `visual-overhaul`.

**C1. Foundation and baseline.** ArenaLook (.cs/.asset), AbilityLook(+Rules), LookColours, UiTheme fields and `arenaLook`, ZoneOwnerShown (moved).
- `ArenaLookAssetTests.EveryFieldIsInTheYamlWithATooltip` (the UiThemeTests pattern)
- `AbilityLookTableTests`: `OneEntryPerCatalogueAbilityDebugPingsExempt` (ids from AbilityCatalogue.asset), `NoTwoEntriesShareShapeAndFamily`, `ScopeHasNoLook`, `BubbleAndShieldDifferInShapeAndFamily`
- `LookColoursTests`: `TeamIsShotColorFor`, `GoldIsImmuneBarColor`, `ElectricBlueIsTheStunnedColour` (the source, not the hue)
- `UiThemeTests.ThemePointsAtArenaLook`
- `ZoneOwnerShownTests`: `KnownOwnerWinsWhileFiltering`, `LiveOwnerOtherwise`
- IL: `IlWiring.Uses(MinimapView, "RecolourOwnership", ZoneOwnerShown.Of)`
- Before-captures of both scenes, and a baseline frame time (§7). The minimap is unchanged.

**C2. Bubble vs Shield.** Hologram shader and material, RespawnBubbleLook, ShieldShellMesh, ShieldShellRise, ShieldShellView, Shield Plates.mat, the hex ring.
- `RespawnBubbleLookTests.DiameterIsShieldDiameterTimesBubbleScale` (literal hand-over) plus IL `Uses(RespawnShield, "BuildBubble", Diameter)`
- `RespawnBubbleLookTests.ColourIsTheTeamShotColour` plus IL on `ColourFor`
- `ShieldShellMeshTests`: `FortyTwoPlatesTwelvePentagons`, `UnderThreeHundredTriangles`, `UnitBoundsCentredAtZero`
- `ShieldShellRiseTests`: `PlatesStartOnTheRingAndEndAtRest`, `LowerPlatesArriveFirst`
- `ShieldLookTests.ShellAndRingReadGold` (IL on ShowShield and SetRing → `LookColours.For`)
- `ShieldShellViewTests.DestroyFreesTheMesh` (leak count by name)
- Captures: the bubble per team, seen by an enemy and by a friend (two clients); the armed hex ring; the shell at 0.05 / 0.1 / 0.2 s from two yaws; the bubble over a same-team owned floor (re-taken after C5).

**C3. Mech colours.** Mech Team.mat (template; check `PBR_Free_EM.png` is greyscale by a `Graphics.Blit` read, and if it is not, bake a greyscale copy into `Gameplay/Art/`), MechLook, MechMaterials, PlayerTeamAppearance.
- `MechLookTests`: `BaseNeverBlackForAnyTeamColour`, `GlowIsTheTeamHue`
- `PlayerTeamAppearanceTests.NeverAssignsPbrTeam1` plus IL on `Apply` → `MechMaterials.For`
- Capture: three teams in one frame, from two yaws.

**C4. Arena surfaces, neutral only.** ArenaSurface.shader and ArenaHex.hlsl, ArenaShaderGlobals, the editor globals hook, the four `.mat` swaps (shader and keyword), **`ArenaSceneShaderIntegrityTests` widened**: URP prefix OR the allow-listed `Overpower/Arena Surface`, plus `!ShaderUtil.ShaderHasError` and `isSupported`. The custom shader is never renamed to dodge the test.
- `ArenaSceneShaderIntegrityTests.UnknownPrefixStillFails` (a fixture shader name)
- `ArenaShaderTests`: `HasDepthOnlyShadowCasterDepthNormalsPasses`, `FourArenaMaterialsUseItWithTheirKeyword`, `BarrierHasNoStripes`
- `ArenaShaderGlobalsTests.TileSizeAndReflectionHandedOver`
- `HexGridTests`: `TileAtCentreOfRoundTrips`, `NeighboursOneTileApart`, `SymmetricUnder120DegreeTurnsAboutOrigin`
- Captures: both scenes, wide and at play zoom; the walls at 240° and 312.8° and in a rotated third have straight stripes; the wall tops from above (first check UV0 per face on the real arena meshes: Game Scene uses built-in 10202 and 10203); the fog still darkens floor and walls; shadows are present.
- Frame Debugger: the SRP Batcher still batches the arena.
- **One Dev1 build: no pink.**
- **Timebox:** if C4 is not green after about 3 hours, stop, move on to C6-C10, and come back to it.

**C5. Ownership paint.** FloorTileRule, OwnerTileTexel, OwnershipSignature, `BuildingManager.CaptureRadiusOf`, ArenaOwnershipPainter on MatchSystems.prefab.
- `FloorTileRuleTests`: `TileInsideBandTakesTheZone`, `OutsideEveryBandIsPlain`, `OverlapGoesToNearestCentre`, `BandFollowsTheLiveRadius`, `FillNeverGrowsOutward` (monotonic, not values), `OutOfPlayUsesOutOfPlayPaint`, `SpawnZoneTakesItsTeamColour`, `FloorPaintNeverPulses`, `TileColourEqualsOwnerPaintColoursFor` (team, neutral and out of play, test theme)
- `OwnerTileTexelTests`: `RoundTripKeepsStateAndLinearColour`, `OwnedAtZeroFillStaysOwned`
- `OwnershipSignatureTests`: `OwnerKnowledgeRadiusOutOfPlaySpawnEachRepaint`, `NothingChangedNoRepaint`
- `ArenaOwnershipPainterPrefabTests.MatchSystemsCarriesPainterWithTheme`
- IL: `Uses(painter, "Repaint", FloorTileRule.LookOf)` and `ResultDecidesABranch(painter, "LateUpdate", NeedsRepaint)`
- Parity capture: the texture filled with a q+r checker by eval, seen from above at one spot per third, proves the HLSL cells match the C# cells.
- Recorder rows: Editor + Dev1 on one team, Dev2 on the other. Each records the painted signature hash and the minimap owners on change. A flip; a zone this team does not know (the floor equals the minimap); a late-join Dev3 is right on its first frame.
- Captures: owned, neutral and out-of-play zones in both scenes; a Dominion spawn area.

**C6. Stun, Pulse, Flame.**
- Stun: StunBolt mesh on `Stun Gun Bullet.prefab` (root MeshFilter swap) plus LookTint; a blue burst prefab as `fallbackImpactVfx`; StunArcsView; the STUNNED label goes electric blue through the field value.
- Pulse: SonicPulseView on the prefab; the getters; the ArcPoint and ArcBandRadius geometry.
- Flame: the Flame Tongues child.
- `AbilityVisualPrefabGuardTests`: `StunBulletIsNotTheBuiltInSphere` (mesh ≠ fileID 10207), `StunBulletHasNoShotTeamVisuals`, `PulseHasAView`, `FlameConeHasTongues`
- `StunArcsRuleTests.ShowOnlyWhileStunned` (Stunned beats Slowed) plus `ResultDecidesABranch(StunArcsView, "LateUpdate", Show)`
- `AbilityVisualGeometryTests.EveryArcPointPassesConeFilter` (`ConeFilter.IsWithinCone`, literal 5 m / 60°), `OuterBandEndsAtRange`
- `SonicPulseWiringTests.ExecuteCastPlaysTheViewWithLiveCone` (IL `Uses`)
- `FlameLookTests.TonguesStayInsideTheHitFan`
- Captures: the bolt in flight from two angles; the victim's arcs seen by another client (recorder: the same arc seed on both); the pulse mid-sweep; the flame from two angles.

**C7. Guns A: slug, core, muzzle, lights.**
- BulletSlug on Baseline, SMG, Burst, Burst-Bounce and the rockets; ShotFacing; `bulletTintStrength`.
- `FX Muzzle Flash.prefab` (white, sharp) repointed on the 13 weapon assets. This uses the existing gated path at WeaponFiring.cs:479, so no new code.
- ShotLightPool and ShotLightRule.
- `BulletPrefabGuardTests.WeaponBulletsUseSlugAndAddNoCollider`
- `ShotFacingRuleTests.TurnsOnlyWhenDirectionChanged` plus IL
- `ShotLightRuleTests`: `NeverMoreThanCap`, `OnlyShownShotsQualify`, `NearestToCameraFirst` plus IL `Uses(ShotLightPool, "LateUpdate", Pick)`
- Recorder: live light count per frame never above the cap, and 0 lights on enemy shots inside the fog.

**C8. Guns B: impacts and pooling.**
- Impact prefabs:
  - Baseline: a white disc and sparks.
  - SMG: small quick sparks, no light.
  - Burst: one flash per round.
  - Rocket: a flash, with the ring from the restyled RocketBlastView.
  - Laser: a scorch spark.
  - Mark: a glyph, if a hook exists.
- The 13 assets are repointed; the Stun and Zip fallbacks keep their own bursts.
- VFXManager pool.
- The Bounce ricochet chevron and the Through-Walls flicker need new hook points (bounce, wall entry). If time runs out, they go into the report.
- `WeaponImpactPrefabTests`: `NoWeaponUsesThePuff`, `EachFamilyHasItsOwnImpact`
- `FxPoolRuleTests`: `ReusesBeforeInstantiating`, `RecyclesOldestAtCap`
- `RocketBlastViewTests` (existing) stay green.

**C9. Death card and sorting table.** UiLayers, DeathCard, DeathCardRules, MatchUI routing. LoadoutScreen.cs:22 and PlayerHud.cs:821 comments are updated. **ChatRulesTests is untouched: chat stays at -1.**
- `UiLayersTests`: `DeathCardUnderShopAboveHud`, `DeathCardUnderDominionHudAndChat` (a deliberate relation), `ResultPanelsAboveShop`, `QuitAboveAllButConnectionLost`, `NoLiteralSortingOrderOutsideTheTable` (a source scan for `sortingOrder\s*=\s*-?\d` under `Assets/scripts`)
- `DeathCardRulesTests`: `CountdownRoundsUp`, `NeverOverlapsTheOpenShop`, `NeverOverlapsTheTopReservedBand`
- IL: `Uses(DeathCard, ..., Place)`
- Captures: dead with the shop closed and open, in both modes; the waiting card.

**C10. Old screens and master volume.** Win and lose panels, Escape, Connection Lost, log box and Quit buttons restyled with LobbyUiKit. The panels stay at 0, above the shop.
- `MasterVolumeTests`: `Clamps01`, `LoadsSavedOrFull`, `SliderAppliesThroughRule` (IL)
- Captures: each screen, plus Escape with the slider.

**Tier 2** (each reuses the C1 table, the C2 hex ring, the C6 arcs and bands, and the Blender pipeline; one commit each):
- Dash: silver streak and chevrons.
- Blink: shards and a hex ring.
- Zip: harpoon FBX and a segmented silver cable.
- Portal: a LineRenderer hex gate with a turning inner hex.
- Mines: puck FBX and a blinking team light.
- Cover: Arena Surface `_ARENA_WALL` instance with a team edge; not static, so a property block is fine.
- Fence: pylon FBX and jagged team arcs.
- AoE: a red LineRenderer hex-grid ring with flickering cells.
- Raybeam: one beam, white core, red sheath, a shrinking hex, a pooled scorch.

**Tier 3:** Sprint lines (others see them only if sprint state is already shared, otherwise only you); the team hex under the feet behind `VisibleWhenSeen` and an ArenaLook switch; a flip ripple through a `_ArenaRipple` global set by the painter. **Nothing for the Scope.**

## 7. Performance

- **Arena:** a material swap with no extra geometry. Per pixel it costs Lit plus about 30 ALU of hex maths plus one `Load`. Opaque, so overdraw is the same as today.
- **Shot lights:** a constant cap of 4, shadowless, short range, created once. Forward+ (`m_RenderingMode 2`) bounds the per-cluster loop.
- **Pooled impacts and flashes:** no per-impact Instantiate or Destroy (today there is one per impact). Projectiles stay Instantiate/Destroy as now; the slug costs the same as the sphere.
- **Transparents:** at most a handful of bubbles (additive, about 3.3 m); the pulse and flashes are short-lived.
- **Measurement:** `ProfilerRecorder` (main thread, render thread, batches, SetPass) in Game Scene, with nine test-range dummies, every effect up and scripted nine-shooter SMG Double Rate fire.
  - Taken at C1 (baseline) and after C4, C5 and C8, median and p95 in ms.
  - One Dev1 build measured at the end.
  - The bar: the baseline or better.

## 8. Risks and mitigations

1. **The shader-name test goes red.** It is widened in C4 with the swap, plus an error and support check.
2. **Batching Static.** The arena gets no property blocks and no object space; everything is world position, normals, UV0 and globals.
3. **SRP Batcher.** Lit's CBUFFER is used in every pass, tunables are globals, and C4 checks the Frame Debugger.
4. **Fog.** The arena is opaque with depth passes. Every transparent is gated:
   - the bubble, arcs, flame and pulse through `AttachToCaster`;
   - impacts through `ShotShownAt` / `BlastShownAt`;
   - muzzle flashes through the existing gate;
   - **lights through `Shown` every frame**, because a Light is not a Renderer and would otherwise give away a position in the fog.
5. **Renderers are collected once.** Every child is built before attach. Arcs toggle a child GameObject, never the view itself.
6. **Play Mode dirties shared `.mat` files.** Colours go only through globals, runtime instances or property blocks, and a `git diff` check runs after each run.
7. **Domain-reload leaks.** Every runtime mesh, texture and material is DontSave with a unique name ("Shield Shell", "Arena Owner Tiles", "Mech Team N"). Each is freed in OnDestroy plus a SubsystemRegistration sweep, and never collides with TowerLook's "Hex Prism".
8. **Linear colour space.** The owner texture holds `colour.linear`, and a test asserts it.
9. **The hex maths exists in both C# and HLSL.** The HLSL is a line-for-line transliteration, proven by the parity capture.
10. **Walls straddling tiles.** The colour breaks at hex edges, which reads as panel breaks; a capture judges it. The fallback is a coarser wall sample, `floor(tile / 2)`.
11. **Bounced slugs.** ShotFacing handles them.
12. **Scene freeze.** No scene edits and no `[ExecuteAlways]` on scene objects. Game Scene is checked clean before every test run.
13. **Blender importer.** `.blend` files live in `Source~`. Blender work runs under the lock.
14. **Colour clashes outside scope** (named in the report): the tower carpet and the floor fill both show ownership; the blue BLOCKED popup sits next to the gold Shield; the cyan Dash trail and gold Zip rope stay until Tier 2.
15. **Dominion HUD at -6 over the card at -7.** The `topReserved` placement avoids it, checked by capture.
16. **PlayerPrefs are shared by Dev1-4.** Harmless for a volume; save only on release.

## Open technical questions

None block Tier 1. The builder can settle every item above in the code. Three calls made here go to Tudor in the morning report, in play terms:
- The mech colours use one template material turned into three team copies at runtime, not three hand-made material files.
- The death card draws under the Dominion round bar and the chat (both draw over it), and it is placed so it doesn't collide with them.
- The waiting card keeps a Quit button.

## Additions to the tasks from the Review Focus

- C2: `ShieldShellViewTests.DestroyFreesTheMesh` (already listed) and an IL check that `RespawnShield.BuildBubble` and
  `ShowShield` call `VisibleWhenSeen.AttachToCaster` after every child renderer exists.
- C5: `OwnershipSignatureTests.FirstFrameAfterRegistrationRepaints`, `ArenaShaderGlobalsTests.ClearOwnerTilesMarksInvalid`
  (the lobby and edit mode draw plain), `ArenaOwnershipPainterTests.DestroyFreesTheTexture` (counted by name).
- C6: an IL check that `StunArcsView` and `SonicPulseView` attach through `VisibleWhenSeen` before they show.
- C7: `ShotLightRuleTests.OnlyShownShotsQualify` (already listed) is the fog guard for lights.
- C9: `DeathCardRulesTests.NeverOverlapsTheOpenShop` runs at 1280x720 and 1920x1080.
