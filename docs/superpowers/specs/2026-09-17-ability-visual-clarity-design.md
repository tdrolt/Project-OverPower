# Ability visual clarity — design

**Date:** 2026-09-17 · **Branch:** `limit-testing` · **Status:** written without Tudor (he delegated the decisions,
2026-09-17 evening). Every choice he didn't make himself is marked [C].

Tudor's request, in short:
- Mines and portals look the same; make rough primitive prefabs so you can tell them apart.
- The caging ultimate should have walls with horizontal bars, not a flat zone.
- The flamethrower shows a cylinder; it should be a cone that starts at the player.
- The grappling hook is too small and round; at least make it square.
- The rocket explosion isn't on the ground, which looks weird.
- A prototype, so rough is fine: just enough to tell them apart and read intuitively.

Markers: [T] Tudor decided · [G] GDD · [C] Claude's call (every look value is Inspector-tunable).

**This is visual only.** No damage, radius, angle, timing, collider, RPC, Room Property or `GameplayConfig.asset`
value changes. Where the visual can't honestly match the real hit shape, the mismatch is flagged below for Tudor
instead of being "fixed" in gameplay.

---

## 1. The camera everything must read from

- `CameraTracking.cs:79` offset (0, 10, −5), looking at the player: about **63° down**, **11.2 m** away at zoom 1.
  Mouse-wheel zoom is 0.5–2× (`:115`). Perspective, 60° vertical FOV (`Game Scene.unity:4725`), team yaw offset 120.
- At the 616×576 Game view that's roughly 45 px per metre near the player (computed). The visible ground is about
  14 m across.
- So: flat shapes on the floor read best. Anything tall gets foreshortened to about half its height, and a tall
  near-side wall hides what's behind it. Keep standing parts thin or see-through.

## 2. What exists (verified in code and prefabs, 2026-09-17)

| Ability | Spawned by / networking | Real gameplay shape (the numbers) | Today's visual, and why it reads badly |
|---|---|---|---|
| **Mine** (id 19) | `MineAbility.PlaceMine` → `NetworkedDeployable.Spawn` = `PhotonNetwork.Instantiate` of `Resources/Mine.prefab`, caster's client only | No collider. `OverlapSphere` at the mine: trigger **1.8 m**, blast **2.2 m**, 20 dmg + 40% slow 2 s, arms after 0.5 s, 45 s life, enemies of the owner's team (`Mine.cs:66,72,174,245`). Placed at the caster's **root**, 0.5 m above the floor (`MineAbility.cs:77`, `TestRangeSpawner.cs:106-121`) | A 0.5 m grey URP-Lit cylinder (`Mine.prefab:31,43,67`) floating 0.5 m up. Nothing on detonation |
| **Portal** (Teleport, id 17) | `TeleportAbility.PlacePortal` → same networked spawn of `Resources/Portal.prefab`; diameter travels in instantiation data | No collider. The **owner only** channels if their root is within **1.25 m** (XZ) of the centre (`TeleportAbility.cs:281-295`, `Portal.cs:37,45`); 3 s channel, 2 per player, no lifetime. Enemies can see it, nobody else can use it. Placed on real ground (`TeleportAbility.cs:169-176`) | The **same** grey Lit cylinder mesh and material (`Portal.prefab:31,43,67`), just wider (2.5 m). No team, no owner cue |
| **Electric Fence** (id 26, "the ultimate that cages") | `ElectricFenceAbility.ExecuteCast` → networked spawn of `Resources/Electric Fence.prefab` at the caster's root | No collider, **doesn't block anything**. Every client tracks each enemy's root distance from the centre and hits it (25 dmg + 50% slow) when inside the band **6 m ± 0.5 m** or when it crosses 6 m; 1 s per-target cooldown; 8 s life (`ElectricFence.cs:39,44,217-221`, `FenceCrossingState.cs:57-59`) | A flat 12 m grey disc (`ElectricFence.cs:122-123`), 0.5 m above the floor: a zone, not a fence |
| **Flamethrower** (id 21) | `FlamethrowerAbility.ExecuteCast` runs on **every client** (local coroutine, no networked object) | A true flat **cone**: target root within **7 m** of the caster's **root** and within **±22.5°** of their facing (`ConeFilter.cs:59-78`, `FlamethrowerAbility.cs:218-229,249-250`), plus a wall raycast from the muzzle; 1 s spray, 5 dps burn for 5 s | A cylinder primitive `CreatePrimitive(Cylinder)` 5.8 m wide along the whole 7 m, centred at root height (`:306-312,321-332`): wider than the real hit area at the player, half-buried, taller than the player |
| **Zip gun** (id 18, "grappling hook") | `ZipGunAbility.FireProjectile`: a **local** projectile on every client from the cast RPC; the pull is caster-only | No collider. `ProjectileMotor` sphere-casts with **0.15 m** radius (`ProjectileMotor.cs:179`, `Zip Gun.prefab` Projectile Radius), 15 m, 40 m/s; pull 25 m/s | A 0.2 m sphere (`Zip Gun Bullet.prefab:33,45`): **smaller** than its own 0.3 m hit diameter, about 9 px on screen. Remote clients get a 0.35 m sphere at the impact point for 0.3 s (`ZipGunAbility.cs:251-265`) |
| **Rocket** (weapons 2, 3, 4) | Local projectile on every client (`WeaponFiring.RPC_FireWeapon`) | `ExplodeOnImpact`: splash **3 m** sphere around the blast point, 20 dmg with linear falloff measured to the target's root (`ExplodeOnImpact.cs:169,194,257`). Rockets fly flat at muzzle height (about 1.5 m above the root; `two-client-harness.md` §7), so **every blast is about 2 m above the floor** | `ProjectileMotor.Despawn` plays the shared bullet puff at the hit point, in the air (`:259-273`). An **airburst** (range end, or the cursor rocket reaching the cursor) calls `Despawn(false)` and **shows nothing at all** (`:137,144,169`). The cursor rocket's fire field is spawned at the rocket, so its disc floats about 2 m up (`DetonateAtCursor.cs:142-143`, `FireField.cs:138,169-170`) |

**Why mines and portals look the same:** one mesh, one material, two sizes, no team or owner cue. In play they differ
completely: a mine is a **team trap** that enemies trigger; a portal is a **personal gate** that only its placer can
use.

**No gameplay collider is shared with a visual anywhere in these six**, so nothing needs separating. Guard tests
still assert that no collider appears (a primitive made with `CreatePrimitive` brings one along).

**GDD (`POP GDD.pdf`, abilities list):** the mine "becomes invisible after 1 second" (not in the prototype); the fence
surrounds the caster and slows "enemies passing through it"; the flamethrower is "continuous damage in a cone". The GDD
has no grappling hook.

## 3. Design per ability

Shared rules [C]:
- Primitives only: cube, cylinder, flat `LineRenderer` circles, and one generated fan mesh for the flame.
- Two new plain URP Unlit materials, `Ability Visual Solid` and `Ability Visual Glass` (see-through, double-sided). Lines
  reuse `Assets/Gameplay/UI/AimConeLine.mat`.
- Colour comes through `MaterialPropertyBlock` or line colours, so one material serves every team.
- **Team colour = `UiTheme.ShotColorFor(ownerTeam)`**: the same colours as each team's shots (near-white, violet,
  cyan). No `UiTheme` fields are added.
- Flat parts sit on the **real floor**, found by a short raycast down that skips bodies. Only the visual moves; the
  gameplay object stays exactly where the game put it.
- Every size is read from the gameplay component itself (`Mine.TriggerRadius`, `ElectricFence.Radius`, the
  flamethrower's own Cone Angle/Range, `ExplodeOnImpact.SplashRadius`), never retyped.

### Mine: "a small dark trap" [C]
- A **dark puck** (0.8 m × 0.12 m) with **four stubby spikes** and a **team-coloured stud** on top, lying on the floor.
- **The owner's team** also sees a thin ring at the real **Trigger Radius**. Enemies don't: they only ever saw the
  mine, and still only do.
- When it goes off, every client flashes a **blast ring** at the real **Explosion Radius** on the floor. It fades in
  0.5 s.
- Silhouette logic: small, dark and solid, the opposite of the portal.

### Portal: "a light gate on the floor" [C]
- A **see-through disc** at the real diameter, with a **bright rim**, in the owner's team colour.
- **The placer's own screen only** adds a floating **diamond on a thin stem** above the centre: "this one is yours to
  use." Teammates can't use someone else's portal, so they don't get the diamond.
- Silhouette logic: wide, light and hollow-looking, the opposite of the mine.

### Electric Fence: "a cage" [C]
- Thin **posts** round the real 6 m ring, never more than 2.5 m apart (16 posts), 1.6 m tall.
- **Three horizontal bar circles** at 0.5, 1.0 and 1.5 m, and a faint **floor band exactly as wide as Ring Thickness**:
  where the hits really land.
- Owner's team colour, see-through.
- **No colliders on bars or posts.** Colliders would block players and shots (projectile hit mask is
  Default|Building), which the fence doesn't do today.

### Flamethrower: "a cone from the player" [C]
- A **flat see-through fan** on the floor, apex under the caster's **root** (the real apex), opening to the real
  **7 m / 45°**, with a brighter outline.
- It tracks the caster's facing every physics step, exactly like the hit check. Colour: the ability's existing Vfx
  Color (orange).
- The fan is exact: every vertex is inside `ConeFilter.IsWithinCone` (tested), and its straight arc pieces stay within
  1 cm of the true arc.
- A flat fan, not a 3D cone, because the hit test ignores height. A 3D cone would rise about 3 m at its far end and
  suggest height matters.

### Zip gun: "a square hook on a rope" [C]
- The bolt's head is a **0.45 m cube** facing its flight, with a **rope** back to the shooter's muzzle while it flies.
- Where it bites, every client shows a **square anchor** (side = 2 × the existing Tether Marker Radius) and a rope from
  the caster for as long as the pull takes (distance ÷ Pull Speed).
- **Size is a visual-only scale on the prefab's Head** [C], not the 0.15 m Projectile Radius. The hit radius is a
  gameplay number, and drawn at its true 0.3 m the hook would stay too small (Tudor's complaint). Every other shot's
  mesh matches its hit diameter, so this is flagged below.
- Hook colour: one fixed amber. Tudor's earlier call keeps ability projectiles untinted (`ShotTeamVisuals.cs:13-16`).

### Rocket: "the blast is on the floor" [C]
- Every rocket blast (hit or airburst, all three rocket weapons) drops a **blast ring on the floor** straight below the
  blast, in the shooter's team colour, fading in 0.5 s.
- It listens to a new `ExplodeOnImpact.Detonated` event, so it appears exactly once per blast at the exact splash
  centre.
- **Ring size is honest:** the 3 m splash sphere cut at the height of a **standing player's root**. Falloff is
  measured to the root and reaches 0 at 3 m.
  - At muzzle height that's about **2.6 m** (computed, measured in step 6).
  - The root height (0.5 m) is read from the player's own capsule.
- The small particle puff stays where the rocket really struck (a wall face, a body), so both "where it hit" and "what
  it covers" read.
- The **cursor rocket's fire field disc** also moves down onto the floor (visual child only). Its burn sphere stays
  where it is.

### Reading rule, stated once for Tudor
- **Rings from an overlap check** (mine trigger, mine blast, fire field) mark where your **body's edge** gets caught.
- **Shapes from a distance check** (fence band, flame fan, portal disc, rocket ring) mark where your **centre** must be.

## 4. What stays byte-identical in behaviour

- **Unchanged:** `GameplayConfig.asset`, `UiTheme.asset`, `PhotonServerSettings.asset` (RpcList), every weapon asset,
  every gameplay field on every touched prefab, and every collider (there are none).
- Code edits to gameplay files are additive and visual:
  - read-only getters (`Mine.TriggerRadius/ExplosionRadius`, `ElectricFence.Radius/RingThickness`,
    `ExplodeOnImpact.SplashRadius`);
  - a `Detonated` event raised after the splash;
  - a view hook in `NetworkedDeployable` that runs after `OnPlaced`, where each view's exception is caught and logged;
  - the flamethrower's and zip gun's own cosmetic VFX code, and one tooltip corrected to match the code.
- New components never implement `IPunObservable`, and no RPC is added.
- **Proof:**
  - edit-mode guard tests pin every gameplay value on the touched prefabs at their pre-change values;
  - a Play Mode recorder measures what a practice dummy receives from each changed ability before the work and after
    it, and the two must match;
  - a two-client run checks remote clients see the same visuals in the right team colour.

## 5. Mismatches and questions for Tudor (defaults in brackets)

1. **The fence is not a cage in gameplay.** It damages and slows on crossing and blocks nothing. A cage look may read as
   "can't pass". [Bars have no colliders. Making it block is a gameplay change for you to decide.]
2. **Rocket splash on the floor is smaller than the Inspector's 3 m.** Rockets explode about 2 m up, so a standing
   player is hit out to about 2.6 m. [The ring shows the true 2.6 m. Lower rockets, or a bigger radius, would be
   gameplay changes.]
3. **The cursor rocket's fire field burns from a sphere centred about 2 m up.** The class comment calls it "burning
   ground". [The visual moves to the floor; the burn sphere is unchanged. Grounding the spawn point is a gameplay
   change.]
4. **Flamethrower Cone Range tooltip said "from the muzzle", but the code measures from the root.** [Tooltip corrected
   to match the code; the fan starts at the root. Measuring from the muzzle would add about 1.4 m of reach: gameplay.]
5. **The hook head is drawn larger (0.45 m) than what it can hit (0.3 m).** [Visual only. A bigger Projectile Radius
   would make it easier to land: gameplay.]
6. **The GDD mine turns invisible after 1 s**; the prototype's stays visible. [Unchanged.]
7. **A line between a player's two portals** would make pairs obvious, but would also show enemies where a gate leads.
   [Not drawn.]
8. **Out of scope, noticed:** `Electric Fence.prefab` and `AoE Zone.prefab` each carry two PhotonViews on the root
   (`Electric Fence.prefab:98,100`, `AoE Zone.prefab:133,177`). The AoE Zone ultimate is still a grey floating disc.
   [Untouched.]

## 6. Verification

- **Edit mode:**
  - pure geometry: fan vertices inside the real cone, post spacing, sphere-plane radius, root height, pull time, fade;
  - `GroundSnap` against real colliders in a preview scene;
  - prefab structure (parts, wiring, no colliders);
  - gameplay guard values.
- **Play Mode, single client, at 616×576, each image looked at:** before captures; the mine and portal (own view), a
  mine blast, the cage, the flame fan, a direct and an airburst rocket plus fire field, a wall hit, the hook in flight
  and on the pull. Each capture comes with measured positions, radii and colours.
- **Two clients:** B's mine, portals and cage on A's screen (no trigger ring, no diamond, B's colour). A's flame fan,
  hook, blast ring, mine and cage on B's screen (A's colour, measured). Then the regression recorder again, compared
  with the baseline.
