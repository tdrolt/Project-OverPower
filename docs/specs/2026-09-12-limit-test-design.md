# Limit Test — Design Spec

**Branch:** `limit-testing` · **Written:** 2026-09-12 · **Source brief:** `Resources/loops/Limit Test/limit-test-brief.md`

The brief asks for the prototype to get as close to `POP GDD.pdf` as possible, under three
non-negotiables: **modular**, **designer-friendly** (no `.cs` file should ever need opening to
retune a value), and **it has to run networked with two clients**. Normal project stopping points
and the six refused architecture patterns are suspended for this branch.

## Decisions taken with Tudor, 2026-09-12

| Decision | Value |
|---|---|
| Build order | **Foundation → combat → match loop** |
| Verification | Quality first: compile + play-test each system before moving on |
| Window | 3 days, extendable to 5. Do not cram |
| Arena | Layer tiers onto the existing 9 towers, add a Tier-4 centre. No geometry rebuild |
| Vision system | **Out of scope.** Build laser-through-walls anyway, unflagged; Tudor rebalances |
| Kit access | Test range: live weapon/ability switcher + dummy targets |
| Time to kill | **3–4s** for easy-to-hit weapons; **2–3s** only as a conditional payoff |
| Deliverable | One brief markdown doc in `Resources/loops/Limit Test/`. Maximise build time |
| Weapon representation | **Option C** — stats on a ScriptableObject, behaviour as components on the projectile prefab |
| Cut order | Cuts land at the bottom of the Phase 2 list (phase transitions first, then the OverPower buff) |

Replaced outright, since the brief's combat revamp supersedes them (recoverable from `main`):
`Dash.cs`, `Dash with buff.cs`, `dash with projectile.cs`, `Aoe Ability.cs`, `Aoe effect.cs`.

## Verified facts this design rests on

Checked against the live Editor on 2026-09-12, not taken from planning docs.

- Unity `6000.0.70f1`, URP 17.0.4, Photon PUN 2. MPPM 1.6.3 and Input System 1.19.0 installed.
- `Multiplayer.cs` is **982** lines; `Building capture.cs` is **565**. No `.asmdef` outside Photon, no ScriptableObjects.
- **9** capture points (IDs 0–8); `BuildingManager.CathedralBuildingIDs` hardcodes 6 to team 0, 7 to team 1, 8 to team 2 as capitals. No tier concept, no gold, no shop.
- Layers: `Default, TransparentFX, Ignore Raycast, Building, Water, UI, DeadPlayer, Bullet`.
- Player capsule r=0.7, h=2.61, speed 5, so **1 unit = 1 metre**. The brief's distances (3m dash, 9m blink, 15m zip) are literal.
- Player PhotonView `observableSearch: 2` (AutoFindAll) — confirmed. Brief fact #6 is live.
- Bullet prefab: `m_CollisionDetection: 0` (Discrete), radius 0.254, mass 0.001, gravity off.
- `BuildingManager.RegisterCapture` registry exists and works. Left alone, per standing decision.

### Consequence the brief did not cover

The brief asks for **smaller and faster** bullets. Discrete Rigidbody collision at radius ~0.1 and
speed 40+ tunnels through walls and players at 60fps. Therefore **projectiles move by swept
spherecast** (kinematic, `Physics.SphereCast` along the frame's delta), not by `rb.linearVelocity`.
This is a correctness requirement, not a preference.

## 1. Data layer

### `WeaponDefinition` (ScriptableObject, 13 assets)

Shared stat block; every field visible in the Inspector.

- **Identity:** `int id` (**explicit, never the array index or filename** — brief fact #7), `string displayName`, `WeaponDefinition parent` (null = baseline; this is the upgrade tree), `Sprite icon`, `int goldCost`
- **Firing:** `float damage`, `float fireInterval`, `int projectilesPerShot`, `bool simultaneous` (false = sequential burst), `float sequentialDelay`, `float spreadDegrees`
- **Projectile:** `GameObject projectilePrefab`, `float projectileSpeed`, `float projectileRadius`, `float maxRange`
- **Accuracy cone:** `float minConeAngle`, `float maxConeAngle`, `float bloomPerShot`, `float recoveryPerSecond`, `float movingConePenalty` (GDD: standing still is 1.5x tighter, applied the frame the movement vector hits 0)
- **Overheat:** `float overheatPerShot`, `float overheatRefundOnHit`
- **Charge (optional):** `bool canCharge`, `float maxChargeSeconds`, `int chargeSteps`, `float chargeDamageMultiplier`, `float chargeRangeMultiplier`, `int chargeMaxProjectiles`
- **Feedback:** muzzle VFX, fire SFX, impact VFX

`WeaponCatalogue` (one SO) holds all 13 in an ordered list and resolves `id` to definition. The
int id is what crosses the wire; each client looks up locally (brief fact #7).

### Projectile behaviour components

On the projectile prefab, not the SO. Each owns its own serialized fields, so a field only exists
on prefabs where it means something.

| Component | Fields it owns | Used by |
|---|---|---|
| `ProjectileMotor` | (reads the WeaponDefinition) | all |
| `ExplodeOnImpact` | `splashRadius`, `splashDamage`, `falloffCurve` | rocket path |
| `ScaleDamageWithDistance` | `maxBonus` (0.5), linear muzzle to max range | rocket A |
| `DetonateAtCursor` | `speedMultiplier`, `fireFieldPrefab` | rocket B |
| `LeaveFireField` | `radius`, `damagePerSecond`, `duration` (3s) | rocket B |
| `BounceOffWalls` | `maxBounces` (3), `damagePerBounce` (0.20, additive) | burst B |
| `Pierce` | `maxTargets` (-1 = unlimited) | laser path |
| `Hitscan` | `beamVfx`, `beamDuration` | laser path |
| `IgnoreWalls` | (marker) | laser B |
| `ApplyStatusOnHit` | `StatusEffectSpec[]` | flamethrower, stun gun, mines, raybeam |

A 14th weapon is then: duplicate a prefab, swap a component, make an SO, add it to the catalogue.
No script.

### Abilities

Abilities share almost nothing, so they are **not** modelled as a shared stat block. Each is a
module prefab with one behaviour component that owns **all** of its numbers including cooldown and
charges, instantiated as a child of the player when selected. `AbilityDefinition` (SO) is a thin
catalogue entry: `int id`, `displayName`, `AbilitySlot slot`, `Sprite icon`, `int goldCost`,
`GameObject modulePrefab`. `AbilityCatalogue` resolves `id` to definition.

**Tuning rule, stated plainly because it is the deliverable:** to retune a weapon, open its
`.asset`; for its special behaviour, its projectile prefab. To retune an ability, open its module
prefab. One place each.

### Config SOs

- `GameplayConfig` — base move speed, max health, respawn base/step/cap, kill height, out-of-combat seconds (6s armor regen, 5s shop gate and the sub-35HP passive all read this one value), overheat max / decay rate / decay delay / silence threshold / warning threshold (80)
- `ArmorConfig` — absorb levels `[25, 50, 100]`, recharge levels `[6, 4, 2]`, both as arrays so a 4th tier is data
- `TerritoryConfig` — per tier: capture seconds, gold/sec, bounty. GDD values: T4 15s/8g/1200, T3 10s/10g/900, T2 15s/5g/0, T1 20s/0g/0

## 2. One damage funnel

`IDamageable.ApplyDamage(DamageInfo)`. `DamageInfo` carries source actor, amount, and flags
(`ignoresArmor`, `isOverTime`, `isSplash`, `weaponId`).

Armor absorption, the dash damage-reduction buff, the raybeam vulnerability debuff (+30%, stacking
to +60%), and burn all become modifiers **inside that one method**. This removes the existing bug
class directly: today the dash buff is applied on the bullet path and missing from the AoE path,
and every future mitigation would have diverged identically.

**Authority:** the victim is the sole authority on its own health (already true — keep it).

## 3. Status effects

One `StatusEffects` component per player. Each effect is a spec with duration and magnitude, owned
by the ability that applies it: `Burn`, `Slow`, `Stun`, `Vulnerability`, `Knockback`,
`Invulnerability`. Six equipment abilities are otherwise just copies of a timer.

Stacking rules are explicit per effect: burn refreshes, vulnerability stacks to a cap, stun takes
the longest remaining.

## 4. Networking contract

One pattern for every new networked object (portals, mines, deployable cover, fire fields,
dummies): the caster calls `PhotonNetwork.Instantiate` and passes every parameter through
**`instantiationData`**, read in `OnPhotonInstantiate`. **Never assign fields on the returned
object** — brief fact #5, and the cause of the live `AoEAbility.aoeDamage` dead-field bug.

**Ownership, decided in advance (brief fact #3):**

| State | Owner |
|---|---|
| Position, health, casts, own deployables | The player's own client |
| Territory ownership, match state, phase | Master client |
| Nothing | "The server" — Photon Cloud is a relay and never simulates |

Late-joiner state goes in Custom Properties, not unbuffered RPCs (fact #4): team, alive, weapon id,
ability ids, gold, armor tier. Transient events (a gunshot, an impact VFX) stay as RPCs.

New `IPunObservable` components go on **child** objects with their own PhotonView, never the player
root, because the root's `observableSearch: 2` would silently add them to its serialization and
start sending a second block per tick (fact #6).

**RPC discipline (fact #1):** PUN sends the *index* of a `[PunRPC]` method. Before any
multi-machine test: Refresh RPC List, and every machine builds from the same commit. Inside an RPC
body, "local" means the receiver (fact #2) — anything the sender meant travels as a parameter or
comes from `PhotonMessageInfo.Sender`.

## 5. File structure after the split

`Multiplayer.cs` (982 lines) becomes eight files, each under roughly 150 lines:

| File | Owns |
|---|---|
| `PlayerMotor` | WASD movement, sprint speed modifier, out-of-bounds return |
| `PlayerAim` | Cursor aim, cone angle, bloom accumulation and recovery |
| `PlayerHealth` | Health, armor, the `IDamageable` entry point, out-of-combat timer |
| `PlayerLifecycle` | Death, respawn scaling, alive-state replication, capital-down permanent death |
| `PlayerNetSync` | The only `IPunObservable`. Position, rotation |
| `AbilityRunner` | Four slots, cooldowns, charges, overheat gating, input dispatch |
| `MatchUI` | Win/lose/waiting/respawn panels |
| `Overheat` | Heat value, decay, silence and warning states (sprint and every weapon touch it) |

One `Overpower.Runtime` asmdef so a gameplay edit stops recompiling Photon's 218 files.

## 6. Input

Input System 1.19.0, currently installed and unused. Actions asset with the GDD's bindings:
LMB primary, RMB equipment, Space ultimate, Shift mobility, M map, P shop, Tab scoreboard.
Replaces the current state where all four abilities are bound to Space and toggled by `.enabled`
from UI buttons.

## 7. Test range

Built in Phase 0, because it is also how the damage numbers get verified rather than guessed.

- Debug panel: weapon-state dropdown (13), three slot dropdowns, reset cooldowns, damage-dealt readout
- Dummy targets: 100 HP + 25 armor, stationary and strafing, respawn on kill
- One toggle on `GameplayConfig` disables the whole thing

## 8. Number anchors

Derived from Tudor's TTK intent against 125 effective HP (100 + 25 armor). Everything else scales
from these, and every one is editable in the Inspector.

| Weapon | Damage | Interval | Nominal TTK |
|---|---|---|---|
| Baseline single-shot | 11 | 0.32s | 3.6s |
| SMG | 6 | 0.16s | 3.3s nominal; longer in practice from the cone |
| Burst (3 x 40%) | 4.4 x 3 | 0.38s/cycle | 3.6s if all three land |
| Laser (hitscan, pierces) | 16 | 0.50s | 3.9s — pays for perfect accuracy in DPS |
| Rocket, direct | 38 + 20 splash | 1.1s | 3.6s |
| **Rocket at max range (+50%)** | 57 | 1.1s | **2.4s** |
| **Shotgun, point blank (8 pellets)** | 7 x 8 | 0.90s | **2.0s** |

Conditional payoffs land in 2–3s, unconditional weapons in 3–4s, as specified.

## 9. Build order

**Phase 0 — Foundation.** asmdef, SO data layer, damage funnel, status effects, `Multiplayer.cs`
split, Input System bindings, overheat, test range.

**Phase 1 — Combat**, in cut order (later = cut first): primary weapon system, 4 path upgrades,
8 leaf choices, armor 3 levels x 2 paths, mobility (dash, blink, sprint, teleport, zip gun),
equipment (mines, deployable cover, flamethrower, stun gun, sonic pulse, raybeam), 3 ultimates.

**Phase 2 — Match loop**, in cut order: tiers + adjacency + Tier-4 centre, gold income, shop with
out-of-combat and own-territory gating plus 50% refund, ultimate charge from combat participation,
OverPower comeback buff, bounce-back bounty, phase transitions with map reduction.

**Expected casualties, recorded before starting:** phase transitions with map reduction (deep
match-state change, bottom of the list) and the OverPower mechanic (second from bottom).

## 10. Verification protocol

Per the standing rule and the brief's fact #9, for every change:

1. `unity command recompile_status` before editing — if no Editor is connected, stop and wait.
2. `unity command recompile`, poll until `completed`, confirm `failed:false` and an empty `errors` array.
3. `unity command get_console_logs` for runtime errors.
4. Enter play mode — Unity refuses it while compilation is broken, so this is a second decisive check.
5. Scene or prefab edited on disk while the Editor was open: tell Tudor to reload before playing.

The Unity console is **not** authoritative for compile errors; it retains stale messages that
`clear_console` does not purge.

## Out of scope for this branch

Vision and fog of war; jersey barriers and the two-wall-type rule; arena geometry rebuild;
meta progression, seasonal pass, matchmaking, monetization; tutorial/onboarding level;
passives (the brief removes them entirely); melee expansion; narrative and audio direction
beyond reusing existing clips.
