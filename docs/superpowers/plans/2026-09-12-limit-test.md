# Limit Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring Project OverPower as close to `POP GDD.pdf` as possible on the `limit-testing`
branch, with every gameplay value editable from the Unity Inspector and the game still running
networked with two clients.

**Architecture:** A ScriptableObject data layer holds weapon stats and catalogue entries; projectile
and ability *behaviour* lives as small components on prefabs, so a new weapon is an asset edit
rather than a script. All damage flows through one `IDamageable.ApplyDamage(DamageInfo)` funnel so
armor, buffs and debuffs are applied in exactly one place. `Multiplayer.cs` (982 lines) splits into
eight focused components. Pure gameplay maths lives in plain C# classes with edit-mode tests;
networked behaviour is verified by play-testing.

**Tech Stack:** Unity 6000.0.70f1, URP 17.0.4, Photon PUN 2, Unity Test Framework 1.6.0,
Input System 1.19.0, Multiplayer Play Mode 1.6.3. Driven from the CLI via `unity command …`.

**Spec:** `docs/specs/2026-09-12-limit-test-design.md`. Read it before Task 0.1.

---

## Standing rules for every task

These are not optional and they come from `Resources/loops/Limit Test/limit-test-brief.md`.

1. **Before editing anything:** `unity command recompile_status`. If it errors or reports no
   connected Editor, **stop and ask Tudor to open the project**. Do not edit and verify later.
   Checking the OS process list is not a valid test — the Hub and the Editor are separate processes.
2. **After editing:** `unity command recompile`, poll `unity command recompile_status` until
   `status:"completed"`, and confirm `failed:false` with an empty `errors` array. The Unity console
   is **not** authoritative — it retains stale compiler messages that `clear_console` does not purge.
3. **Then:** `unity command run_tests -- --mode EditMode` and confirm zero failures.
4. **PUN RPC list is index-based.** PUN sends the *index* of a `[PunRPC]` method, not its name.
   Adding or reordering RPCs between two builds makes calls silently mis-dispatch. Never reorder
   existing `[PunRPC]` methods; add new ones at the end of their file.
5. **Inside an RPC body, "local" means the receiver.** `PhotonNetwork.LocalPlayer`,
   `Input.mousePosition`, `Camera.main` and `transform.position` all resolve on the machine that
   *received* the call. Anything the sender meant travels as a parameter or comes from
   `PhotonMessageInfo.Sender`.
6. **`PhotonNetwork.Instantiate` returns only the caller's copy.** Assigning fields on the returned
   object does not reach other clients. Pass everything through `instantiationData` and read it in
   `OnPhotonInstantiate`.
7. **Never add an `IPunObservable` to the player prefab root.** Its PhotonView uses
   `observableSearch: 2` (AutoFindAll) and PUN's search includes children, so a new observable is
   silently added to the root's serialization and starts sending a second block per tick.
8. **Every ScriptableObject gets an explicit `id` field.** Never the array index or the filename —
   reordering a list would silently re-map everyone's data.
9. **Never mutate a ScriptableObject at runtime.** It persists in the Editor, silently does not in a
   build, and is one shared instance per process. Copy to a per-player runtime struct on spawn.
10. **Commit after every task** with a message saying what a designer can now change.
11. **If a scene or prefab was edited on disk while the Editor was open,** tell Tudor to reload it
    before playing — Unity may hold a stale in-memory copy that overwrites the file on save.

---

## File structure

### New — data layer

| Path | Responsibility |
|---|---|
| `Assets/scripts/Overpower.Runtime.asmdef` | One assembly for gameplay code so edits stop recompiling Photon's 218 files |
| `Assets/scripts/Data/WeaponDefinition.cs` | SO: the stat block every weapon shares |
| `Assets/scripts/Data/WeaponCatalogue.cs` | SO: all 13 weapons, resolves `int id` to definition |
| `Assets/scripts/Data/AbilityDefinition.cs` | SO: catalogue entry for one ability (id, slot, icon, cost, module prefab) |
| `Assets/scripts/Data/AbilityCatalogue.cs` | SO: all abilities, resolves `int id` to definition |
| `Assets/scripts/Data/GameplayConfig.cs` | SO: global tunables (speed, health, respawn, overheat, out-of-combat) |
| `Assets/scripts/Data/ArmorConfig.cs` | SO: absorb and recharge levels as arrays |
| `Assets/scripts/Data/TerritoryConfig.cs` | SO: per-tier capture time, gold/sec, bounty |

### New — pure logic (edit-mode tested, no UnityEngine dependency beyond maths types)

| Path | Responsibility |
|---|---|
| `Assets/scripts/Combat/DamageInfo.cs` | `DamageInfo`, `DamageResult`, `DamageSource`, `IDamageable` |
| `Assets/scripts/Combat/DamageResolver.cs` | The one place damage maths happens |
| `Assets/scripts/Combat/StatusEffectState.cs` | Burn/slow/stun/vulnerability/invulnerability, stacking rules |
| `Assets/scripts/Combat/OverheatState.cs` | Heat, decay delay, decay rate, silence and warning |
| `Assets/scripts/Combat/AimConeState.cs` | Cone angle, bloom per shot, recovery, standing-still bonus |
| `Assets/scripts/Combat/ChargePool.cs` | Charge-based cooldowns (dash 2 charges, mines 2 charges) |
| `Assets/scripts/Combat/ArmorState.cs` | Absorption, break, out-of-combat recharge |
| `Assets/scripts/Territory/AdjacencyGraph.cs` | Which zones a team may capture given what it holds |

### New — player components (the `Multiplayer.cs` split)

| Path | Responsibility |
|---|---|
| `Assets/scripts/Player/PlayerMotor.cs` | WASD movement, speed modifiers, out-of-bounds return |
| `Assets/scripts/Player/PlayerAim.cs` | Cursor aim, owns `AimConeState` |
| `Assets/scripts/Player/PlayerHealth.cs` | Implements `IDamageable`; health, armor, out-of-combat timer |
| `Assets/scripts/Player/PlayerLifecycle.cs` | Death, respawn scaling, alive replication, capital-down death |
| `Assets/scripts/Player/PlayerNetSync.cs` | The only `IPunObservable` — position and rotation |
| `Assets/scripts/Player/PlayerOverheat.cs` | Owns `OverheatState`, drives the HUD bar |
| `Assets/scripts/Player/PlayerStatusEffects.cs` | Owns `StatusEffectState`, applies slow/stun to the motor |
| `Assets/scripts/Player/AbilityRunner.cs` | Four slots, cooldowns, charges, overheat gating |
| `Assets/scripts/Player/PlayerLoadout.cs` | Current weapon id and ability ids; replicates via Custom Properties |
| `Assets/scripts/Player/MatchUI.cs` | Win/lose/waiting/respawn panels |

### New — weapons and projectiles

| Path | Responsibility |
|---|---|
| `Assets/scripts/Weapons/WeaponFiring.cs` | Reads the active `WeaponDefinition`, spawns shots, spends overheat |
| `Assets/scripts/Weapons/ProjectileMotor.cs` | Swept-spherecast movement, range budget, hit dispatch |
| `Assets/scripts/Weapons/ProjectileContext.cs` | Per-projectile runtime data from `instantiationData` |
| `Assets/scripts/Weapons/Effects/*.cs` | One file per behaviour: explode, pierce, bounce, distance-scaling, detonate-at-cursor, fire field, hitscan, ignore-walls, apply-status |

### New — test range and tests

| Path | Responsibility |
|---|---|
| `Assets/scripts/TestRange/DummyTarget.cs` | 100 HP + 25 armor practice target, respawns |
| `Assets/scripts/TestRange/TestRangePanel.cs` | Live weapon/ability switcher, reset cooldowns, damage readout |
| `Assets/Tests/Overpower.Tests.asmdef` | Edit-mode test assembly |
| `Assets/Tests/*.cs` | One test file per pure-logic class |

### Modified

| Path | Change |
|---|---|
| `Assets/scripts/Player/Multiplayer.cs` | Emptied out across Tasks 0.8–0.11, then deleted |
| `Assets/scripts/Player/Building capture.cs` | Tier, adjacency, gold (Phase 2) |
| `Assets/scripts/BuildingManager.cs` | Tier-aware ownership; `RegisterCapture` left alone |
| `Assets/scripts/RoomManager.cs` | Spawns loadout-aware players |
| `Assets/Resources/Multiplayer Player.prefab` | Component swap; HUD bars added |

### Deleted (recoverable from `main`)

`Dash.cs`, `Dash with buff.cs`, `dash with projectile.cs`, `Aoe Ability.cs`, `Aoe effect.cs`,
`Ability UI.cs`, `bullet.cs`, and after Task 0.11, `Multiplayer.cs`. The two orphaned
`Multiplayer Player 1/2.prefab` files stay until a three-team render check passes.

---

# Phase 0 — Foundation

## Task 0.1: Assemblies and a working test harness

Nothing else can be verified until `unity command run_tests` works, so this is first.

**Files:**
- Create: `Assets/scripts/Overpower.Runtime.asmdef`
- Create: `Assets/Tests/Overpower.Tests.asmdef`
- Create: `Assets/Tests/HarnessTest.cs`

- [ ] **Step 1: Confirm the Editor is connected**

Run: `unity command recompile_status`
Expected: a row containing `{"status":"completed","failed":false,"errors":[]}`. If it errors, stop
and ask Tudor to open the project.

- [ ] **Step 2: Create the runtime assembly definition**

`Assets/scripts/Overpower.Runtime.asmdef`:

```json
{
  "name": "Overpower.Runtime",
  "rootNamespace": "Overpower",
  "references": [
    "PhotonUnityNetworking",
    "PhotonRealtime",
    "PhotonChat",
    "Unity.InputSystem",
    "Unity.TextMeshPro",
    "Unity.RenderPipelines.Universal.Runtime"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 3: Recompile and read the errors — expect failures, and fix them**

Run: `unity command recompile`, then poll `unity command recompile_status`.

An asmdef is the single most likely step in this plan to break the build, because scripts outside
`Assets/scripts/` that referenced these classes now cannot see them, and `Assembly-CSharp` scripts
are compiled *after* this assembly. Expected failures and their fixes:

- `The type or namespace name 'X' could not be found` in a file under `Assets/TutorialInfo/` or
  another folder — those scripts are unrelated to gameplay; leave them alone, they do not reference
  gameplay types.
- A missing reference to a Photon assembly — add the exact assembly name to `references`. Get the
  real names with: `unity command find_assets -- --filter "t:asmdef"`.
- If `Unity.RenderPipelines.Universal.Runtime` is not resolvable, remove it; nothing in
  `Assets/scripts/` needs URP types today.

Do not proceed until `failed:false` with an empty `errors` array.

- [ ] **Step 4: Create the test assembly**

`Assets/Tests/Overpower.Tests.asmdef`:

```json
{
  "name": "Overpower.Tests",
  "rootNamespace": "Overpower.Tests",
  "references": [
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "Overpower.Runtime",
    "PhotonUnityNetworking",
    "PhotonRealtime"
  ],
  "includePlatforms": [ "Editor" ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [ "nunit.framework.dll" ],
  "autoReferenced": false,
  "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 5: Write a harness test that proves the runner works**

`Assets/Tests/HarnessTest.cs`:

```csharp
using NUnit.Framework;

namespace Overpower.Tests
{
    public class HarnessTest
    {
        [Test]
        public void TestRunnerIsWired()
        {
            Assert.AreEqual(4, 2 + 2);
        }
    }
}
```

- [ ] **Step 6: Recompile, then run the tests**

Run: `unity command recompile`, poll until completed with no errors, then
`unity command run_tests -- --mode EditMode`
Expected: 1 test, 1 passed, 0 failed. If `run_tests` reports no tests found, the asmdef's
`defineConstraints` or `precompiledReferences` are wrong — fix before continuing.

- [ ] **Step 7: Commit**

```bash
git add Assets/scripts/Overpower.Runtime.asmdef Assets/Tests
git commit -m "build: gameplay assembly and an edit-mode test harness"
```

---

## Task 0.2: The damage funnel

This is the single most important contract in the plan. Every later task depends on these exact
type and member names, so they are given in full.

**Files:**
- Create: `Assets/scripts/Combat/DamageInfo.cs`
- Create: `Assets/scripts/Combat/DamageResolver.cs`
- Create: `Assets/Tests/DamageResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/DamageResolverTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class DamageResolverTests
    {
        [Test]
        public void ArmorAbsorbsFirst()
        {
            var r = DamageResolver.Resolve(10f, false, health: 100f, armor: 25f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(10f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(0f, r.HealthLost, 0.001f);
            Assert.IsFalse(r.ArmorBroke);
        }

        [Test]
        public void DamageSpillsIntoHealthWhenArmorBreaks()
        {
            var r = DamageResolver.Resolve(40f, false, health: 100f, armor: 25f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(25f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(15f, r.HealthLost, 0.001f);
            Assert.IsTrue(r.ArmorBroke);
        }

        [Test]
        public void IgnoresArmorSkipsTheArmorPool()
        {
            var r = DamageResolver.Resolve(10f, true, health: 100f, armor: 25f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(0f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(10f, r.HealthLost, 0.001f);
        }

        [Test]
        public void VulnerabilityAppliesBeforeArmor()
        {
            // +60% on 20 damage is 32, which breaks 25 armor and spills 7.
            var r = DamageResolver.Resolve(20f, false, health: 100f, armor: 25f,
                                           vulnerability: 0.6f, reduction: 0f);
            Assert.AreEqual(25f, r.ArmorAbsorbed, 0.001f);
            Assert.AreEqual(7f, r.HealthLost, 0.001f);
        }

        [Test]
        public void ReductionAppliesBeforeArmor()
        {
            var r = DamageResolver.Resolve(40f, false, health: 100f, armor: 0f,
                                           vulnerability: 0f, reduction: 0.5f);
            Assert.AreEqual(20f, r.HealthLost, 0.001f);
        }

        [Test]
        public void VulnerabilityAndReductionMultiplyRatherThanCancel()
        {
            // 100 * 1.5 * 0.5 = 75
            var r = DamageResolver.Resolve(100f, true, health: 200f, armor: 0f,
                                           vulnerability: 0.5f, reduction: 0.5f);
            Assert.AreEqual(75f, r.HealthLost, 0.001f);
        }

        [Test]
        public void LethalWhenHealthReachesZero()
        {
            var r = DamageResolver.Resolve(100f, true, health: 100f, armor: 0f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.IsTrue(r.Lethal);
            Assert.AreEqual(100f, r.HealthLost, 0.001f);
        }

        [Test]
        public void OverkillDoesNotReportMoreHealthLostThanTheTargetHad()
        {
            var r = DamageResolver.Resolve(500f, true, health: 30f, armor: 0f,
                                           vulnerability: 0f, reduction: 0f);
            Assert.AreEqual(30f, r.HealthLost, 0.001f);
            Assert.IsTrue(r.Lethal);
        }

        [Test]
        public void FullReductionDealsNothingAndIsNotLethal()
        {
            var r = DamageResolver.Resolve(999f, true, health: 1f, armor: 0f,
                                           vulnerability: 0f, reduction: 1f);
            Assert.AreEqual(0f, r.HealthLost, 0.001f);
            Assert.IsFalse(r.Lethal);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `unity command recompile` then `unity command run_tests -- --mode EditMode`
Expected: compile failure naming `DamageResolver` as not found. That failure is the test failing.

- [ ] **Step 3: Write the contracts**

`Assets/scripts/Combat/DamageInfo.cs`:

```csharp
using UnityEngine;

namespace Overpower.Combat
{
    public enum DamageSource { Projectile, Splash, Burn, Zone, Contact }

    /// <summary>
    /// Everything the damage funnel needs to resolve one hit. Immutable on purpose: a hit is a
    /// fact, and letting callers mutate it mid-flight is how the two divergent damage paths this
    /// replaces came about.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;

        /// <summary>
        /// ActorNumber of whoever fired. Inside an RPC this MUST come from
        /// PhotonMessageInfo.Sender, never PhotonNetwork.LocalPlayer, which resolves to the
        /// receiver. Getting this wrong gives kill credit to the victim.
        /// </summary>
        public readonly int SourceActorNumber;

        public readonly int SourceTeamId;
        public readonly int WeaponId;          // -1 when the damage did not come from a weapon
        public readonly DamageSource Source;
        public readonly bool IgnoresArmor;
        public readonly Vector3 HitPoint;

        public DamageInfo(float amount, int sourceActorNumber, int sourceTeamId, int weaponId,
                          DamageSource source, bool ignoresArmor, Vector3 hitPoint)
        {
            Amount = amount;
            SourceActorNumber = sourceActorNumber;
            SourceTeamId = sourceTeamId;
            WeaponId = weaponId;
            Source = source;
            IgnoresArmor = ignoresArmor;
            HitPoint = hitPoint;
        }
    }

    public readonly struct DamageResult
    {
        public readonly float ArmorAbsorbed;
        public readonly float HealthLost;
        public readonly bool ArmorBroke;
        public readonly bool Lethal;

        public DamageResult(float armorAbsorbed, float healthLost, bool armorBroke, bool lethal)
        {
            ArmorAbsorbed = armorAbsorbed;
            HealthLost = healthLost;
            ArmorBroke = armorBroke;
            Lethal = lethal;
        }

        public float Total => ArmorAbsorbed + HealthLost;
    }

    public interface IDamageable
    {
        /// <summary>
        /// Call only on the victim's own client. The victim is the sole authority on its health;
        /// every other client learns the result through serialization.
        /// </summary>
        DamageResult ApplyDamage(in DamageInfo info);

        bool IsAlive { get; }
        int TeamId { get; }
        int ActorNumber { get; }
    }
}
```

`Assets/scripts/Combat/DamageResolver.cs`:

```csharp
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The only place in the project where damage maths happens. Before this existed there were
    /// two copies of the sequence and they had already diverged: the dash damage-reduction buff
    /// was applied on the bullet path and missing from the AoE path.
    ///
    /// Order of operations, fixed and tested:
    ///   1. multiply by (1 + vulnerability)
    ///   2. multiply by (1 - reduction)
    ///   3. armor absorbs what it can, unless the hit ignores armor
    ///   4. the remainder comes off health, clamped so overkill is not reported
    /// </summary>
    public static class DamageResolver
    {
        public static DamageResult Resolve(float amount, bool ignoresArmor, float health,
                                           float armor, float vulnerability, float reduction)
        {
            float final = Mathf.Max(0f, amount)
                        * (1f + Mathf.Max(0f, vulnerability))
                        * (1f - Mathf.Clamp01(reduction));

            float absorbed = 0f;
            if (!ignoresArmor && armor > 0f)
            {
                absorbed = Mathf.Min(armor, final);
                final -= absorbed;
            }

            float healthLost = Mathf.Min(Mathf.Max(0f, health), final);
            bool armorBroke = !ignoresArmor && armor > 0f && absorbed >= armor - 0.0001f;
            bool lethal = health - healthLost <= 0.0001f && healthLost > 0f;

            return new DamageResult(absorbed, healthLost, armorBroke, lethal);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `unity command recompile`, poll until clean, then `unity command run_tests -- --mode EditMode`
Expected: 10 tests, 10 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Combat Assets/Tests/DamageResolverTests.cs
git commit -m "feat(combat): one damage funnel with armor, vulnerability and reduction"
```

---

## Task 0.3: Status effects

**Files:**
- Create: `Assets/scripts/Combat/StatusEffectState.cs`
- Create: `Assets/Tests/StatusEffectStateTests.cs`

**Behaviour contract, to be implemented exactly:**

```csharp
namespace Overpower.Combat
{
    public enum StatusKind { Burn, Slow, Stun, Vulnerability, Invulnerability }

    public enum StackRule { Refresh, StackToCap, LongestWins }

    [System.Serializable]
    public struct StatusEffectSpec
    {
        public StatusKind kind;
        public float duration;
        public float magnitude;   // Slow: 0..1 speed loss. Vulnerability: 0..1 extra damage.
                                  // Burn: damage per second. Stun/Invulnerability: ignored.
    }

    public sealed class StatusEffectState
    {
        public void Apply(in StatusEffectSpec spec);
        public void Tick(float deltaTime);
        public void ClearAll();
        public bool IsActive(StatusKind kind);
        public float Magnitude(StatusKind kind);   // summed for StackToCap, else the active one
        public float Remaining(StatusKind kind);
        public int StackCount(StatusKind kind);
    }
}
```

**Stacking rules, per kind — these are design decisions, not implementation detail:**

| Kind | Rule | Cap | Reason |
|---|---|---|---|
| `Burn` | `Refresh` | — | The flamethrower's 5 dmg/s for 5s restarts rather than doubling; otherwise two taps from one player delete a target |
| `Slow` | `StackToCap` | 0.60 | Mines plus electric fence should compound, but a fully immobilised player cannot play |
| `Stun` | `LongestWins` | — | Two stuns landing together must not add to 4.5s of helplessness |
| `Vulnerability` | `StackToCap` | 0.60 | The brief states it explicitly: one raybeam is +30%, two or three is +60% |
| `Invulnerability` | `LongestWins` | — | Same reason as stun |

The caps live on `GameplayConfig` (Task 0.4) as `slowCap` and `vulnerabilityCap`, passed into the
constructor — not hardcoded, because they are balance values.

- [ ] **Step 1: Write the failing tests**

Cover, one test each: a single slow reduces speed by its magnitude; two slows sum; three slows clamp
at the cap; burn refreshes rather than stacking so `StackCount` stays 1 and `Remaining` resets to
the full duration; a 1.0s stun and a 2.5s stun applied together leave 2.5s remaining, not 3.5s;
vulnerability stacks 0.3 + 0.3 to 0.6 and a third stack stays at 0.6; `Tick` past a duration
deactivates the effect and `Magnitude` returns 0; `ClearAll` deactivates everything (used on death);
ticking with no effects active does not throw.

- [ ] **Step 2: Run to verify they fail**

Run: `unity command recompile` then `unity command run_tests -- --mode EditMode`
Expected: compile failure naming `StatusEffectState`.

- [ ] **Step 3: Implement `StatusEffectState`**

Internally a small list of live entries per kind. `Tick` decrements remaining time and removes
expired entries. Burn does **not** deal damage here — it returns damage-due through
`float ConsumeBurnDamage(float deltaTime)` so the caller routes it through `DamageResolver` with
`ignoresArmor: false` and `DamageSource.Burn`, keeping the single-funnel rule intact.

- [ ] **Step 4: Run to verify they pass**

Expected: all tests pass, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Combat/StatusEffectState.cs Assets/Tests/StatusEffectStateTests.cs
git commit -m "feat(combat): status effects with per-kind stacking rules"
```

---

## Task 0.4: Config ScriptableObjects and armor state

**Files:**
- Create: `Assets/scripts/Data/GameplayConfig.cs`
- Create: `Assets/scripts/Data/ArmorConfig.cs`
- Create: `Assets/scripts/Combat/ArmorState.cs`
- Create: `Assets/Tests/ArmorStateTests.cs`
- Create assets: `Assets/Gameplay/Config/GameplayConfig.asset`, `Assets/Gameplay/Config/ArmorConfig.asset`

**`GameplayConfig` fields and their values.** Every one `[SerializeField]` with a `[Tooltip]`
saying what it does in plain language, because this asset is the designer-facing surface.

| Field | Value | Source |
|---|---|---|
| `baseMoveSpeed` | 5 | Existing prefab value |
| `maxHealth` | 100 | GDD |
| `respawnBaseSeconds` | 5 | Existing, from playtest note 2.3 |
| `respawnPerDeathSeconds` | 1 | Existing |
| `respawnMaxSeconds` | 10 | Existing |
| `killHeight` | -10 | Existing |
| `outOfCombatSeconds` | 6 | GDD armor regen; one value serves armor regen, the shop gate and the sub-35HP passive |
| `shopOutOfCombatSeconds` | 5 | GDD purchasing rule |
| `overheatMax` | 100 | GDD |
| `overheatDecayDelay` | 1.5 | GDD |
| `overheatDecayPerSecond` | 25 | GDD |
| `overheatWarningThreshold` | 80 | Tudor's stated condition for accepting full-silence overheat |
| `slowCap` | 0.60 | **Claude's number** — not in the GDD or brief |
| `vulnerabilityCap` | 0.60 | Brief, raybeam section |
| `testRangeEnabled` | true | The one toggle that disables the whole test range |

**`ArmorConfig`:** `absorbLevels = [25, 50, 100]` and `rechargeSeconds = [6, 4, 2]`, both arrays so
a fourth tier is data rather than code. GDD, Health & Armor section.

**`ArmorState` contract:**

```csharp
public sealed class ArmorState
{
    public ArmorState(float capacity, float rechargeDelaySeconds);
    public float Current { get; }
    public float Capacity { get; }
    public bool IsBroken { get; }
    public void SetTier(float capacity, float rechargeDelaySeconds);  // refills to the new capacity
    public float Absorb(float amount);      // returns how much it took
    public void Tick(float deltaTime, float secondsSinceCombat);
    public void Clear();                    // on death
}
```

Recharge is **instant to full** once `secondsSinceCombat >= rechargeDelaySeconds`, matching the
GDD's "armor regenerates after 6 seconds out of combat". A gradual refill would be a different
design and is not what is written.

- [ ] **Step 1: Write the failing tests**

Cover: absorbing less than capacity leaves the remainder; absorbing more returns only the capacity
and sets `IsBroken`; ticking with `secondsSinceCombat` below the delay does not refill; reaching the
delay refills to full and clears `IsBroken`; `SetTier` to a larger capacity refills to the new value
(buying an upgrade should not leave you on old armor); `SetTier` to a smaller capacity clamps
`Current`; `Clear` empties it.

- [ ] **Step 2: Run to verify they fail**

- [ ] **Step 3: Implement the two SOs and `ArmorState`**

Both SOs get `[CreateAssetMenu(menuName = "OverPower/Gameplay Config")]` and
`"OverPower/Armor Config"` so Tudor can make them from the right-click menu.

- [ ] **Step 4: Create the two assets in the Editor**

Run: `unity command create_asset -- --type GameplayConfig --path "Assets/Gameplay/Config/GameplayConfig.asset"`
then the same for `ArmorConfig`. Set every field to the table above with
`unity command set_serialized_field`. Verify with `unity command get_serialized_fields` that the
values actually landed — creating an asset and assuming its defaults stuck is how a silent
misconfiguration starts.

- [ ] **Step 5: Run the tests**

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/scripts/Data Assets/scripts/Combat/ArmorState.cs Assets/Tests/ArmorStateTests.cs Assets/Gameplay
git commit -m "feat(data): gameplay and armor config assets, armor absorption state

Tudor can now change max health, move speed, respawn scaling, overheat rates
and the three armor tiers from Assets/Gameplay/Config without opening a script."
```

---

## Task 0.5: Overheat state

**Files:**
- Create: `Assets/scripts/Combat/OverheatState.cs`
- Create: `Assets/Tests/OverheatStateTests.cs`

**Contract:**

```csharp
public sealed class OverheatState
{
    public OverheatState(float max, float decayDelay, float decayPerSecond, float warningThreshold);
    public float Heat { get; }
    public float Normalised { get; }        // 0..1, for the HUD bar
    public bool IsSilenced { get; }
    public bool IsWarning { get; }          // Heat >= warningThreshold && !IsSilenced
    public bool CanAct => !IsSilenced;
    public void Add(float amount);          // a shot, or a second of sprinting
    public void Refund(float amount);       // the laser's half-cost refund on a connecting shot
    public void Tick(float deltaTime);
    public void Clear();                    // on death
}
```

**The rule that matters, from the GDD:** at `Heat >= max` the player is **silenced until the bar
reaches 0**, not until it drops below max. Decay starts `decayDelay` seconds after the last
addition and runs at `decayPerSecond`. With the configured 100 / 1.5s / 25 per second that is up to
**4 seconds of helplessness**, which Tudor accepted explicitly on condition that the warning state
at 80 exists. Both behaviours are therefore tested, not incidental.

Overheat silences the primary weapon **and all abilities** — Tudor's decision, taken against a
recommendation of weapon-only lockout, because the GDD says "silenced".

- [ ] **Step 1: Write the failing tests**

Cover: adding below max does not silence; reaching exactly max silences; heat at 99 after decay is
not silenced; once silenced, dropping to 50 is **still silenced**; once silenced, reaching 0 clears
it; decay does not start before `decayDelay` elapses; adding heat resets the decay delay; `Refund`
cannot push heat below 0; `IsWarning` is true at 80 and false at 79; `IsWarning` is false while
silenced (the warning's job is to precede the silence, not accompany it); `Clear` zeroes heat and
un-silences.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `OverheatState`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Combat/OverheatState.cs Assets/Tests/OverheatStateTests.cs
git commit -m "feat(combat): overheat with silence-until-zero and an 80-heat warning state"
```

---

## Task 0.6: Aim cone

**Files:**
- Create: `Assets/scripts/Combat/AimConeState.cs`
- Create: `Assets/Tests/AimConeStateTests.cs`

**Contract:**

```csharp
public sealed class AimConeState
{
    public AimConeState(float minAngle, float maxAngle, float bloomPerShot,
                        float recoveryPerSecond, float standingStillMultiplier);
    public float CurrentAngle { get; }             // before the standing-still bonus
    public float EffectiveAngle { get; }           // after it
    public void RegisterShot();
    public void Tick(float deltaTime, bool isMoving);
    public float SampleOffsetDegrees(System.Random rng);  // random within EffectiveAngle
    public void Reset();
}
```

GDD: standing still gives a **1.5x accuracy increase**, taking effect the moment the movement vector
hits 0 (so strafing is a real decision). Implemented as `EffectiveAngle = CurrentAngle / 1.5` when
not moving. `standingStillMultiplier` is configurable per weapon, because the GDD gives the semi-auto
better standing accuracy than the SMG.

**Random spread is Tudor's decision, taken against a recommendation** of deterministic twin-ray
spread. The recorded watch-for is: *do players complain about missing shots they felt they aimed
correctly?* `SampleOffsetDegrees` takes an injected `Random` so the tests are deterministic.

- [ ] **Step 1: Write the failing tests**

Cover: a fresh cone sits at `minAngle`; one shot adds exactly `bloomPerShot`; repeated shots clamp
at `maxAngle`; `Tick` while moving recovers at `recoveryPerSecond` and never below `minAngle`;
`EffectiveAngle` equals `CurrentAngle / 1.5` when not moving and `CurrentAngle` when moving;
`SampleOffsetDegrees` with a seeded `Random` stays within `±EffectiveAngle / 2` over 1000 samples;
`Reset` returns to `minAngle`.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `AimConeState`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Combat/AimConeState.cs Assets/Tests/AimConeStateTests.cs
git commit -m "feat(combat): aim cone with bloom, recovery and a standing-still bonus"
```

---

## Task 0.7: Charge pools

**Files:**
- Create: `Assets/scripts/Combat/ChargePool.cs`
- Create: `Assets/Tests/ChargePoolTests.cs`

Dash has 2 charges on a 5s cooldown and mines have 2 charges on 10s each, so charge-based cooldowns
are shared machinery rather than per-ability code.

**Contract:**

```csharp
public sealed class ChargePool
{
    public ChargePool(int maxCharges, float rechargeSeconds);
    public int Available { get; }
    public int MaxCharges { get; }
    public float RechargeProgress { get; }   // 0..1 toward the next charge, for the HUD
    public bool TryConsume();
    public void Tick(float deltaTime);
    public void RefillAll();                 // zip gun's reset-on-takedown, and respawn
    public void SetMaxCharges(int max);
}
```

Charges recharge **one at a time, sequentially** — spending both dashes starts a 5s timer for the
first and another 5s for the second, rather than both returning together. This is the conventional
behaviour and it is what makes 2 charges a resource rather than a double jump.

- [ ] **Step 1: Write the failing tests**

Cover: a new pool is full; `TryConsume` decrements and returns true; consuming an empty pool returns
false and does not go negative; one charge returns after exactly `rechargeSeconds`; spending two and
ticking `rechargeSeconds` returns exactly one, not two; ticking `2 * rechargeSeconds` returns both;
a full pool does not exceed `maxCharges`; `RefillAll` fills instantly and clears partial progress;
`SetMaxCharges` upward grants the new charges, downward clamps.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `ChargePool`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Combat/ChargePool.cs Assets/Tests/ChargePoolTests.cs
git commit -m "feat(combat): sequential charge-based cooldowns"
```

---

## Task 0.8: Weapon and ability data assets

**Files:**
- Create: `Assets/scripts/Data/WeaponDefinition.cs`
- Create: `Assets/scripts/Data/WeaponCatalogue.cs`
- Create: `Assets/scripts/Data/AbilityDefinition.cs`
- Create: `Assets/scripts/Data/AbilityCatalogue.cs`
- Create: `Assets/Tests/CatalogueTests.cs`

Fields exactly as listed in the spec's section 1. Every field needs a `[Tooltip]`. Group them with
`[Header]` into Identity / Firing / Projectile / Accuracy / Overheat / Charge / Feedback so the
Inspector is readable — this asset is the product as far as requirement 2 is concerned.

**The id rule, which is a correctness requirement:** `int id` is explicit and hand-assigned.
Catalogue lookup is `id → definition` through a dictionary built in `OnEnable`, never
`list[index]`. The brief's fact #7 is the reason: reordering a list would silently re-map every
player's weapon.

- [ ] **Step 1: Write the failing tests**

Cover: a catalogue with duplicate ids reports them through `Validate()` rather than silently
resolving to the first; `Resolve(id)` returns the matching definition; `Resolve` on an unknown id
returns null and does not throw; `Resolve` is unaffected by reordering the backing list (build the
catalogue, resolve id 7, reverse the list, resolve id 7, assert the same object).

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement the four types**

Include an `OnValidate` that logs an error naming both assets when two share an id, so the mistake
surfaces in the Editor rather than in a playtest.

- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Data Assets/Tests/CatalogueTests.cs
git commit -m "feat(data): weapon and ability definitions with explicit networked ids"
```

---

## Task 0.9: Split `Multiplayer.cs` — health, armor and the damage entry point

The split happens over Tasks 0.9–0.12. Take it one component at a time and **play-test between
each** — this is the riskiest stretch of the plan, because `Multiplayer.cs` owns movement, aim,
ability dispatch, serialization, damage, death, respawn and the win/lose UI, and everything in the
scene references it.

**Files:**
- Create: `Assets/scripts/Player/PlayerHealth.cs`
- Modify: `Assets/scripts/Player/Multiplayer.cs` (remove health, armor and damage)
- Modify: `Assets/Resources/Multiplayer Player.prefab` (add the component)

`PlayerHealth` implements `IDamageable`, owns `ArmorState` and `StatusEffectState`, tracks
`secondsSinceCombat` (reset on dealing **or** taking damage — Tudor's definition of out of combat),
and ticks burn damage through `DamageResolver`. It serializes health and armor through
`PlayerNetSync` (Task 0.11), not through its own `IPunObservable` — brief fact #7.

- [ ] **Step 1: Read the current damage path before touching it**

Read `Multiplayer.cs:347-466` (`OnCollisionEnter`, `TakeDamage`, `ApplyAoEDamage`). Note that the
victim is already the sole authority and that the `IsMine` guard at the top of `OnCollisionEnter`
is what fixed the "health bar fights itself" bug. **Preserve that guard.**

- [ ] **Step 2: Write `PlayerHealth`** with the public surface:

```csharp
public float Health { get; }
public float Armor { get; }
public int ArmorTier { get; }          // 0..2 index into ArmorConfig
public float SecondsSinceCombat { get; }
public bool IsOutOfCombat { get; }
public event System.Action<DamageResult, DamageInfo> Damaged;
public event System.Action<DamageInfo> Died;
public void SetArmorTier(int tier);
public void ResetForRespawn();
public void NoteDealtDamage();         // called by the shooter to reset its own combat timer
```

- [ ] **Step 3: Move the damage path over**

Delete `TakeDamage` and `ApplyAoEDamage` from `Multiplayer.cs`. Both callers (the bullet collision
and the AoE coroutine) now build a `DamageInfo` and call `ApplyDamage`. **This is the task that
fixes the divergence** — the dash reduction buff now applies to both paths because there is only
one path.

- [ ] **Step 4: Add the component to the player prefab**

Use `unity command add_component`, not hand-edited YAML. Then `unity command save_prefab_contents`.

- [ ] **Step 5: Verify**

`unity command recompile` → clean. `unity command run_tests` → pass. Then **enter play mode** and
confirm no console errors: `unity command editor_play`, `unity command get_console_logs`,
`unity command editor_stop`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(player): health and armor move out of Multiplayer.cs

Both damage paths now go through one funnel, so the dash damage-reduction buff
applies to AoE damage as well as bullets. It did not before."
```

---

## Task 0.10: Split — motor, aim and overheat components

**Files:**
- Create: `Assets/scripts/Player/PlayerMotor.cs`, `PlayerAim.cs`, `PlayerOverheat.cs`, `PlayerStatusEffects.cs`
- Modify: `Assets/scripts/Player/Multiplayer.cs`, `Assets/Resources/Multiplayer Player.prefab`

`PlayerMotor` owns WASD movement and exposes `AddSpeedMultiplier(object key, float multiplier)` /
`RemoveSpeedMultiplier(object key)` so sprint (+50%) and slow statuses compose instead of
overwriting each other. **This is the shape of bug 1.2** — `RespawnPlayer` hardcoded
`movementSpeed = 10f` against a prefab value of 5, and a keyed multiplier stack cannot reproduce it.

`PlayerAim` owns `AimConeState` and exposes `Vector3 GetShotDirection(System.Random rng)`.
`PlayerOverheat` owns `OverheatState`. `PlayerStatusEffects` owns `StatusEffectState` and pushes
slow into the motor and stun into the ability runner.

- [ ] **Step 1: Move movement and rotation** out of `Multiplayer.cs:194-277` into `PlayerMotor`,
      keeping the `FixedUpdate` + `MovePosition` approach (changing physics integration mid-refactor
      would conflate two problems).
- [ ] **Step 2: Move `UpdateRotationFromMouse` (`Multiplayer.cs:219-268`)** into `PlayerAim`.
- [ ] **Step 3: Add the four components** to the prefab via `unity command add_component`.
- [ ] **Step 4: Verify** — recompile clean, tests pass, play mode with no console errors, and
      **confirm by playing that WASD and mouse aim still work**. A refactor that compiles and does
      not move is a failed refactor.
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(player): movement, aim, overheat and statuses become components

Sprint and slow now compose through a keyed speed-multiplier stack instead of
assigning movementSpeed, which is the bug shape that gave a speed buff on respawn."
```

---

## Task 0.11: Split — lifecycle, net sync and match UI, then delete `Multiplayer.cs`

**Files:**
- Create: `Assets/scripts/Player/PlayerLifecycle.cs`, `PlayerNetSync.cs`, `MatchUI.cs`
- Delete: `Assets/scripts/Player/Multiplayer.cs`

`PlayerNetSync` is the **only** `IPunObservable` and it stays on the player root's existing
PhotonView (it replaces `Multiplayer`'s, so the observable count does not change). Every other new
component is deliberately not observable — brief fact #6.

`PlayerLifecycle` keeps, unchanged in behaviour: the respawn scaling (5s + 1s per death, capped at
10s), the collider-off-and-kinematic-while-dead fix for bug 2.5, the `killHeight` out-of-bounds
return for 2.7, the alive-state Custom Property, the capital-down permanent-death branch, and the
name-tag colour recompute for 2.11. **Each of those is a fixed bug; re-breaking one is worse than
not doing the refactor.** List them in the commit message so the review can check each.

- [ ] **Step 1: Inventory the fixed bugs** listed above against `Resources/loops/bug-log.md` and
      write them as a checklist in the task notes before moving any code.
- [ ] **Step 2: Move death, respawn and alive replication** (`Multiplayer.cs:467-573, 701-872`).
- [ ] **Step 3: Move the RPCs** (`Multiplayer.cs:601-700, 876-930`) into `MatchUI` and
      `PlayerLifecycle`, **preserving their relative order** — PUN dispatches by index.
- [ ] **Step 4: Move `OnPhotonSerializeView`** (`Multiplayer.cs:330-346`) into `PlayerNetSync`.
- [ ] **Step 5: Delete `Multiplayer.cs`** and fix every reference the compiler reports.
- [ ] **Step 6: Refresh the RPC list** — Unity menu `Window > Photon Unity Networking > Refresh RPC List`
      via `unity command menu`. Without this, RPC indices are stale and calls mis-dispatch.
- [ ] **Step 7: Verify with two clients.** Recompile clean, tests pass, then a **two-client play
      session** via Multiplayer Play Mode: both players spawn, move, shoot, take damage, die,
      respawn with the scaled timer, and the win panel appears. Check each bug on the Step 1
      checklist by hand.
- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(player): delete Multiplayer.cs, 982 lines into eight components

Verified still working after the split, each previously fixed and individually
re-checked: respawn timer scaling, dead players keeping no hitbox, out-of-bounds
return, alive state reaching late joiners, capital-down permanent death, and
name-tag colours recomputing when either team property arrives."
```

---

## Task 0.12: Input System bindings

**Files:**
- Create: `Assets/Gameplay/Input/OverpowerControls.inputactions`
- Create: `Assets/scripts/Player/PlayerInputRouter.cs`
- Modify: `Assets/scripts/Player/AbilityRunner.cs`

Replaces the current state where all four abilities are bound to Space and toggled by `.enabled`
from UI buttons.

| Action | Binding | Type |
|---|---|---|
| `Move` | WASD | Value, Vector2 |
| `Primary` | Left mouse | Button, with hold for charge weapons |
| `Equipment` | Right mouse | Button |
| `Ultimate` | Space | Button |
| `Mobility` | Left Shift | Button, with hold for sprint |
| `Map` | M | Button |
| `Shop` | P | Button |
| `Scoreboard` | Tab | Button |

- [ ] **Step 1: Create the actions asset** with `unity command create_asset`, then populate it.
- [ ] **Step 2: Write `PlayerInputRouter`** which reads the asset and raises events the
      `AbilityRunner` subscribes to. It must **gate all input on `IsAlive`** — bug 2.10 was
      abilities being usable while dead, and the gate belongs at the input layer where it cannot be
      forgotten per-ability.
- [ ] **Step 3: Gate chat** — when a `TMP_InputField` has focus, suppress gameplay input. This is a
      known live issue (you can currently shoot while typing).
- [ ] **Step 4: Verify** — play-test each binding individually and confirm the chat gate works.
- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(input): GDD keybinds through the Input System

LMB primary, RMB equipment, Space ultimate, Shift mobility, M/P/Tab for UI.
All gameplay input is gated on being alive and on chat not having focus."
```

---

## Task 0.13: Projectile system

**Files:**
- Create: `Assets/scripts/Weapons/ProjectileMotor.cs`, `ProjectileContext.cs`, `RangeBudget.cs`
- Create: `Assets/scripts/Weapons/WeaponFiring.cs`
- Create: `Assets/Tests/RangeBudgetTests.cs`

**The correctness requirement:** projectiles move by **swept spherecast**, not `rb.linearVelocity`.
Each frame, cast a sphere of `projectileRadius` from the current position along
`direction * speed * deltaTime` and move to the hit point or the full step. The existing bullets are
Discrete-collision Rigidbodies at radius 0.254 and speed 25; the brief asks for smaller and faster,
which makes tunnelling certain rather than likely.

**Every projectile has a range cap and a lifetime.** Today bullets have neither, so **every missed
shot is a permanent Rigidbody on all nine clients** — the cause of the round-2 freezes. `RangeBudget`
tracks distance travelled against `maxRange` and is the pure-logic piece worth testing.

Projectiles are spawned with `PhotonNetwork.Instantiate` and every parameter (weapon id, shooter
actor number, shooter team, direction, charge level) travels in `instantiationData`, read in
`OnPhotonInstantiate`. **Never assign fields on the returned object** — that is brief fact #5 and
the live `AoEAbility.aoeDamage` bug.

- [ ] **Step 1: Write `RangeBudgetTests`** — a budget consumed below `maxRange` stays alive; one
      step past it expires; the expiry point is clamped to exactly `maxRange` so a
      damage-scales-with-distance weapon cannot exceed its cap; a zero `maxRange` expires immediately
      without dividing by zero.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement `RangeBudget`, `ProjectileContext`, `ProjectileMotor`, `WeaponFiring`.**
      `WeaponFiring` reads the active `WeaponDefinition`, checks `PlayerOverheat.CanAct`, applies
      `fireInterval`, spends `overheatPerShot`, and spawns `projectilesPerShot` either simultaneously
      with `spreadDegrees` (shotgun) or sequentially with `sequentialDelay` (burst).
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "feat(weapons): swept-spherecast projectiles with a range cap

Projectiles no longer tunnel at high speed and no longer live forever when they
miss. Every missed bullet used to be a permanent Rigidbody on every client."
```

---

## Task 0.14: Test range

**Files:**
- Create: `Assets/scripts/TestRange/DummyTarget.cs`, `TestRangePanel.cs`
- Create: `Assets/Gameplay/Prefabs/DummyTarget.prefab`
- Modify: `Assets/Scenes/Game Scene.unity`

Built before the content, because it is how the damage numbers get verified rather than asserted.

- [ ] **Step 1: `DummyTarget`** implements `IDamageable` with 100 HP + 25 armor from `ArmorConfig`,
      shows a floating damage number per hit, logs `[TTK] <weapon> killed in <seconds>s` on death,
      and respawns after 2s. Two variants: stationary, and strafing left-right at the player's base
      speed.
- [ ] **Step 2: `TestRangePanel`** — a Canvas with a weapon dropdown populated from
      `WeaponCatalogue`, three ability dropdowns from `AbilityCatalogue` filtered by slot, a reset-
      cooldowns button, and a readout of damage dealt and measured TTK. Hidden entirely when
      `GameplayConfig.testRangeEnabled` is false.
- [ ] **Step 3: Place dummies and the panel in the scene** via `unity command create_gameobject` and
      `instantiate_prefab`. Dummies go near team 0's spawn so they are reachable immediately.
- [ ] **Step 4: Verify** — play, shoot a dummy, and confirm the logged TTK for the baseline weapon is
      **3.6s ± 0.3s**. If it is not, the baseline numbers are wrong and must be corrected before any
      other weapon is built on top of them.
- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "feat(testrange): dummy targets and a live loadout switcher

Lets one person evaluate every weapon and ability without a second client, and
measures time-to-kill so the damage numbers are checked rather than assumed."
```

---

## Task 0.15: Baseline weapon end-to-end

**Files:**
- Create: `Assets/Gameplay/Weapons/01 Baseline.asset`
- Create: `Assets/Gameplay/Projectiles/Baseline Bullet.prefab`

The first real content, and the reference every other weapon's numbers are relative to.

| Field | Value | Whose number |
|---|---|---|
| `id` | 1 | Claude |
| `damage` | 11 | **Claude**, derived from Tudor's 3–4s TTK intent |
| `fireInterval` | 0.32 | **Claude**, same derivation |
| `projectileSpeed` | 55 | **Claude** — brief says "smaller and faster" than the current 25 |
| `projectileRadius` | 0.10 | **Claude** — brief says smaller than the current 0.254 |
| `maxRange` | 30 | **Claude** |
| `minConeAngle` | 1.5 | **Claude** |
| `maxConeAngle` | 7 | **Claude** |
| `bloomPerShot` | 0.8 | **Claude** |
| `recoveryPerSecond` | 6 | **Claude** |
| `standingStillMultiplier` | 1.5 | GDD |
| `overheatPerShot` | 7 | **Claude** — 14 shots to silence, roughly 4.5s of sustained fire |

- [ ] **Step 1: Create the projectile prefab** — sphere, radius 0.10, `ProjectileMotor`, on the
      `Bullet` layer, registered for Photon instantiation.
- [ ] **Step 2: Create the weapon asset** with the table above via `unity command create_asset` and
      `set_serialized_field`, then confirm with `get_serialized_fields`.
- [ ] **Step 3: Add it to `WeaponCatalogue` and set it as the default loadout weapon.**
- [ ] **Step 4: Verify in the test range** — measured TTK on a 100+25 dummy is **3.6s ± 0.3s**;
      standing still visibly tightens the cone; 14 rapid shots silence the player; silence clears
      about 4s later.
- [ ] **Step 5: Verify with two clients** — both players can shoot each other, damage applies once
      (not twice), and the health bar does not oscillate.
- [ ] **Step 6: Commit.**

```bash
git add -A
git commit -m "feat(weapons): the baseline weapon, fully data-driven

Every number is on Assets/Gameplay/Weapons/01 Baseline.asset. Measured TTK is
3.6s against 100 HP + 25 armor, matching the 3-4s intent for easy-to-hit weapons."
```

**Phase 0 ends here. Checkpoint with Tudor before starting Phase 1:** the foundation is the part he
asked to be done properly, and it is also the part he will be judged on for "can I read a file I did
not write".

---

# Phase 1 — Combat

Ordered so that cuts land at the bottom. Every number below marked **[C]** is Claude's invention and
goes in the provenance doc; **[T]** is Tudor's from the brief; **[G]** is from the GDD.

## Task 1.1: Rocket launcher path and its two leaves

**Files:**
- Create: `Assets/Gameplay/Weapons/02 Rocket.asset`, `03 Rocket - Distance.asset`, `04 Rocket - Cursor Fire.asset`
- Create: `Assets/Gameplay/Projectiles/Rocket.prefab`, `Rocket Cursor.prefab`, `Fire Field.prefab`
- Create: `Assets/scripts/Weapons/Effects/ExplodeOnImpact.cs`, `ScaleDamageWithDistance.cs`, `DetonateAtCursor.cs`, `LeaveFireField.cs`

**The aim-cone reading matters here.** Tudor's clarification: "precision only with good positioning"
is *not* a separate mechanic — it is a large `maxConeAngle`, a high `bloomPerShot` and a slow
`recoveryPerSecond`. The rocket is accurate only once you have stood still long enough for the cone
to settle, and firing throws it wide again. Do not write special-case accuracy code for it.

| Field | `02 Rocket` | `03 Distance` | `04 Cursor Fire` | Whose |
|---|---|---|---|---|
| `id` | 2 | 3 | 4 | [C] |
| `damage` (direct) | 38 | 38 | 30 | [C] |
| `splashDamage` | 20 | 20 | 22 | [C] |
| `splashRadius` | 3.0 | 3.0 | 3.0 | [C] |
| `fireInterval` | 1.1 | 1.1 | 1.1 | [C] |
| `projectileSpeed` | 28 | 28 | 18 | [C] (the brief says leaf B also reduces speed [T]) |
| `projectileRadius` | 0.22 | 0.22 | 0.22 | [C] |
| `maxRange` | 32 | 32 | 32 | [C] |
| `minConeAngle` | 1.0 | 1.0 | 1.0 | [C] |
| `maxConeAngle` | 14 | 14 | 14 | [C], expressing [T]'s intent |
| `bloomPerShot` | 6.0 | 6.0 | 6.0 | [C] |
| `recoveryPerSecond` | 3.5 | 3.5 | 3.5 | [C] |
| `overheatPerShot` | 22 | 22 | 22 | [C] |
| `maxBonus` (distance) | — | 0.50 | — | [T] |
| `fireDuration` | — | — | 3.0 | [T] |
| `fireDamagePerSecond` | — | — | 12 | [C] |
| `fireFieldRadius` | — | — | 2.5 | [C] |

TTK check: 125 / 38 = 3.3 rockets x 1.1s = **3.6s** on direct hits. At max range with leaf A,
38 x 1.5 = 57, so 2.2 rockets x 1.1s = **2.4s** — inside Tudor's 2–3s conditional band.

- [ ] **Step 1: `ExplodeOnImpact`** — on hit, `Physics.OverlapSphere(splashRadius)`, one `DamageInfo`
      per `IDamageable` found with `DamageSource.Splash`, scaled by `falloffCurve` over distance.
      Only the projectile owner runs this and only the victim applies it, so nine clients do not each
      deal the damage. **This is bug 2.B's shape** — the AoE coroutine ran on every client.
- [ ] **Step 2: `ScaleDamageWithDistance`** — linear from +0% at the muzzle to `maxBonus` at
      `maxRange`, reading distance from `RangeBudget`. Tudor's clarification says linear explicitly.
- [ ] **Step 3: `DetonateAtCursor`** — travel toward the cursor ground point, detonate on arrival.
      **If it strikes a player or wall before reaching that point, detonate there instead** — Tudor's
      clarification, and without it a rocket passes through people.
- [ ] **Step 4: `LeaveFireField`** — spawn a networked fire field via `PhotonNetwork.Instantiate` with
      radius, dps and duration in `instantiationData`. Ticks damage once per second on the **field
      owner's** client only, routed through each victim's `ApplyDamage`.
- [ ] **Step 5: Create the three assets and three prefabs**, add to `WeaponCatalogue`.
- [ ] **Step 6: Verify in the test range** — direct-hit TTK 3.6s ±0.3s; max-range leaf-A TTK
      2.4s ±0.3s; standing still for 4s visibly tightens the cone and one shot widens it; the fire
      field damages a dummy standing in it for 3 seconds and then disappears.
- [ ] **Step 7: Verify with two clients** — splash damages a remote player exactly once.
- [ ] **Step 8: Commit.**

## Task 1.2: Burst path and its two leaves

**Files:**
- Create: `Assets/Gameplay/Weapons/05 Burst.asset`, `06 Burst - Charge.asset`, `07 Burst - Bounce.asset`
- Create: `Assets/Gameplay/Projectiles/Burst Round.prefab`, `Burst Round Bouncing.prefab`
- Create: `Assets/scripts/Weapons/Effects/BounceOffWalls.cs`

Tudor's clarification: the burst is **sequential, not simultaneous** — a short burst, so landing all
three is a skill outcome worth 120% of one baseline bullet. This is the difference from the shotgun,
which fires everything at once.

| Field | `05 Burst` | `06 Charge` | `07 Bounce` | Whose |
|---|---|---|---|---|
| `id` | 5 | 6 | 7 | [C] |
| `damage` per projectile | 4.4 | 4.4 | 4.4 | [T] — 40% of the baseline's 11 |
| `projectilesPerShot` | 3 | 3 (to 5 charged) | 3 | [T] |
| `simultaneous` | false | false | false | [T] |
| `sequentialDelay` | 0.07 | 0.07 | 0.07 | [C] |
| `fireInterval` | 0.38 | 0.38 | 0.38 | [C] |
| `projectileSpeed` | 58 | 58 | 58 | [C] |
| `projectileRadius` | 0.09 | 0.09 | 0.09 | [C] |
| `maxRange` | 28 | 28 | 28 | [C] |
| `minConeAngle` | 1.8 | 1.8 | 1.8 | [C] |
| `maxConeAngle` | 9 | 9 | 9 | [C] |
| `bloomPerShot` | 0.7 | 0.7 | 0.7 | [C] |
| `recoveryPerSecond` | 7 | 7 | 7 | [C] |
| `overheatPerShot` | 9 per trigger pull | 12 | 9 | [C] |
| `maxChargeSeconds` | — | 0.35 | — | [C] |
| `chargeMaxProjectiles` | — | 5 | — | [T] |
| `chargeSteps` | — | 2 (3→4→5) | — | [T] |
| `chargeDamageMultiplier` | — | 1.5 | — | [C] |
| `maxBounces` | — | — | 3 | [T] |
| `damagePerBounce` | — | — | 0.20 additive | [T] |

**`overheatPerShot` is per trigger pull, not per projectile.** State this in the field's tooltip —
it is the kind of ambiguity that makes a designer think a value is broken.

TTK checks: all three land, 13.2 per 0.38s = **3.6s**, matching the baseline exactly — the burst
trades consistency for the same DPS, which is the point. Full charge with all five landing:
5 x 6.6 = 33 per 0.73s cycle = **2.8s** [C, targeting Tudor's 2–3s band for "full burst on burst
charge path"]. Three-bounce hits: 4.4 x 1.6 x 3 = 21 per 0.38s = **2.3s**.

- [ ] **Step 1: `BounceOffWalls`** — on a wall hit, reflect the direction about the hit normal,
      increment the bounce count, add `damagePerBounce` additively, expire after `maxBounces`.
      Bounces consume range budget, so bouncing does not extend the weapon's reach.
- [ ] **Step 2: Extend `WeaponFiring` for hold-to-charge** — charge level maps to projectile count
      through `chargeSteps`, so a partial charge gives 4. Tudor's clarification.
- [ ] **Step 3: Create the assets and prefabs**, add to the catalogue.
- [ ] **Step 4: Verify** — burst TTK 3.6s ±0.3s with all three landing; partial charge fires 4;
      full charge fires 5; a bounced round's damage number visibly increases per bounce.
- [ ] **Step 5: Commit.**

## Task 1.3: SMG path and its two leaves

**Files:**
- Create: `Assets/Gameplay/Weapons/08 SMG.asset`, `09 SMG - Double Rate.asset`, `10 SMG - Shotgun.asset`
- Create: `Assets/Gameplay/Projectiles/SMG Round.prefab`, `Pellet.prefab`

Tudor's clarification on the shotgun: "needs to get extremely close" is a **short `maxRange` plus the
spread** — at point blank every pellet lands, which is where the one-shot potential comes from; past
a few metres most of them miss. No special-case code.

| Field | `08 SMG` | `09 Double Rate` | `10 Shotgun` | Whose |
|---|---|---|---|---|
| `id` | 8 | 9 | 10 | [C] |
| `damage` | 6 | 4 | 7 per pellet | [C] |
| `fireInterval` | 0.16 | 0.08 | 0.90 | [C] |
| `projectilesPerShot` | 1 | 1 | 8 | [C] |
| `simultaneous` | — | — | true | [T] |
| `spreadDegrees` | — | — | 11 | [C] |
| `projectileSpeed` | 52 | 52 | 45 | [C] |
| `projectileRadius` | 0.08 | 0.08 | 0.07 | [C] |
| `maxRange` | 22 | 20 | 9 | [C] |
| `minConeAngle` | 2.5 | 4.0 | 3.0 | [C] |
| `maxConeAngle` | 12 | 20 | 8 | [C] |
| `bloomPerShot` | 1.1 | 1.6 | 2.5 | [C] |
| `recoveryPerSecond` | 8 | 9 | 6 | [C] |
| `overheatPerShot` | 3.5 | 2.2 | 18 | [C] |

TTK: SMG 125/6 = 20.8 shots x 0.16 = **3.3s** nominal, longer in practice because of the cone.
Double rate **2.5s nominal but only at close range** — the 20-degree cone is the condition, which is
what makes a run-and-gun weapon a positioning weapon. Shotgun point blank, all 8 pellets:
56 per 0.90s = **2.0s**, the low end of Tudor's conditional band, reached only inside 9 metres.

- [ ] **Step 1: Extend `WeaponFiring` for simultaneous multi-projectile spread** — `spreadDegrees`
      distributes pellets evenly around the aim direction with a small random jitter, all in one frame.
- [ ] **Step 2: Create the three assets and two prefabs**, add to the catalogue.
- [ ] **Step 3: Verify** — SMG TTK 3.3s ±0.3s; shotgun at 1m kills in ~2s and at 8m barely connects;
      the double-rate cone is visibly wider.
- [ ] **Step 4: Commit.**

## Task 1.4: Laser path and its two leaves

**Files:**
- Create: `Assets/Gameplay/Weapons/11 Laser.asset`, `12 Laser - Charge.asset`, `13 Laser - Through Walls.asset`
- Create: `Assets/scripts/Weapons/Effects/Hitscan.cs`, `Pierce.cs`, `IgnoreWalls.cs`

Hitscan — instant, no travel time — per Tudor's clarification and the GDD. The GDD's overheat rule
for the laser applies: **double overheat per shot, half refunded if the shot connects.** That is what
`overheatRefundOnHit` exists for.

| Field | `11 Laser` | `12 Charge` | `13 Through Walls` | Whose |
|---|---|---|---|---|
| `id` | 11 | 12 | 13 | [C] |
| `damage` | 16 | 16 | 16 | [C] |
| `fireInterval` | 0.50 | 0.50 | 0.50 | [C] |
| `maxRange` | 26 | 26 (41 charged) | 26 | [C] |
| `minConeAngle` | 0.8 | 0.8 | 0.8 | [C] |
| `maxConeAngle` | 6 | 6 | 6 | [C] |
| `bloomPerShot` | 1.5 | 1.5 | 1.5 | [C] |
| `recoveryPerSecond` | 5 | 5 | 5 | [C] |
| `overheatPerShot` | 14 | 14 | 14 | [C], doubling the baseline per [G] |
| `overheatRefundOnHit` | 7 | 7 | 7 | [G] |
| `maxChargeSeconds` | — | 0.45 | — | [C] |
| `chargeDamageMultiplier` | — | 2.2 | — | [C] |
| `chargeRangeMultiplier` | — | 1.6 | — | [C] |
| `pierceMaxTargets` | -1 (unlimited) | -1 | -1 | [T] |

TTK: 125/16 = 7.8 shots x 0.50s = **3.9s** — the laser pays for perfect accuracy and piercing with
DPS. Charged: 35.2 per 0.95s cycle = **3.4s** plus 60% more range, so charging buys reach and a
slight DPS gain rather than a damage spike.

- [ ] **Step 1: `Hitscan`** — `Physics.RaycastAll` along the aim direction up to `maxRange`, sorted by
      distance, one `DamageInfo` per `IDamageable` in the line. Draw the beam VFX for
      `beamDuration` on every client via an RPC so it is visible, with the direction as a
      **parameter** — inside an RPC, `Camera.main` and `transform.forward` resolve on the receiver.
- [ ] **Step 2: `Pierce`** — `maxTargets` of -1 means unlimited; a positive value stops after N.
- [ ] **Step 3: `IgnoreWalls`** — excludes the `Building` layer from the raycast mask.
      **Build it without a warning in the UI**, per Tudor's instruction; he will rebalance.
- [ ] **Step 4: Create the three assets**, add to the catalogue.
- [ ] **Step 5: Verify** — laser TTK 3.9s ±0.3s; two dummies in a line both take damage from one
      shot; a connecting shot costs 7 net overheat and a miss costs 14; the wall-piercing leaf
      damages a dummy behind a wall.
- [ ] **Step 6: Commit.**

## Task 1.5: Armor upgrades

**Files:**
- Modify: `Assets/scripts/Player/PlayerHealth.cs`
- Create: `Assets/scripts/Player/ArmorUpgradePath.cs`

GDD: absorb levels 25 / 50 / 100, recharge levels 6s / 4s / 2s, upgradeable up to three times, with
a choice each time between the absorption path and the recharge path.

**A GDD discrepancy to record, not to silently resolve:** the text says armor can be upgraded "up to
three times" and lists three levels, while the balancing sheet prices only two armor upgrades
(1400 and 1800 gold). Implemented as **level 1 is the starting armor and there are two purchasable
upgrades**, which is the reading the cost sheet supports. A third upgrade costs **2200 [C]** and is
present but disabled by a bool on `GameplayConfig`, so Tudor can turn it on without a code change.

Armor tier replicates through a Custom Property, not an RPC — a late joiner needs to know how much
armor you have (brief fact #4).

- [ ] **Step 1: Write `ArmorUpgradePath`** tracking absorb level and recharge level independently,
      clamped to the array lengths in `ArmorConfig`.
- [ ] **Step 2: Write edit-mode tests** — two absorb upgrades give 100 capacity and 6s recharge; two
      recharge upgrades give 25 capacity and 2s recharge; one of each gives 50 and 4s; a third
      upgrade is refused when the config disables it; buying an upgrade refills armor to the new
      capacity immediately.
- [ ] **Step 3: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 4: Wire it to the test range panel** so armor tier is selectable without a shop.
- [ ] **Step 5: Verify with two clients** that a remote player's armor tier is correct including for
      a mid-match joiner.
- [ ] **Step 6: Commit.**

## Task 1.6: Mobility — dash, blink, sprint

**Files:**
- Create: `Assets/scripts/Abilities/Mobility/DashAbility.cs`, `BlinkAbility.cs`, `SprintAbility.cs`
- Create: `Assets/Gameplay/Abilities/Dash.prefab`, `Blink.prefab`, `Sprint.prefab`

| Ability | Field | Value | Whose |
|---|---|---|---|
| Dash | `cooldownSeconds` | 5 | [T] |
| | `charges` | 2 | [T] |
| | `distance` | 3 | [T] |
| | `travelSpeed` | 18 | [C] |
| | `damageReduction` | 0 | [C] — see the note below |
| Blink | `cooldownSeconds` | 12 | [T] |
| | `range` | 9 | [T] |
| | `destinationCheckRadius` | 0.75 | [C] |
| Sprint | `speedMultiplier` | 1.5 | [T] |
| | `overheatPerSecond` | 18 | [C] |

**The dash damage-reduction buff is set to 0 and recorded as a removal.** The current code gives the
dash a damage-reduction buff; Tudor's revamp spec for dash lists only cooldown, charges and distance.
The field stays on the component so turning it back on is a number, not a code change. Tudor needs to
know this, because it silently changes how survivable a dash-in is.

**Blink needs a destination-validity check or players will blink inside geometry** — Tudor's own
clarification. Sphere-cast `destinationCheckRadius` at the target point; if it is blocked, walk the
destination back toward the player until it is clear.

**Sprint is the interaction Tudor called the most interesting in the list:** it spends the weapon's
resource, so sprinting costs you the ability to shoot, and it ends by itself when overheat silences
you. At 18 overheat per second against a 100 maximum, that is about **5.5 seconds of sprint** from
cold. Sprint must call the same `OverheatState.Add` the weapons use — not its own timer.

- [ ] **Step 1: Implement the three abilities** as module prefabs, all numbers `[SerializeField]`.
- [ ] **Step 2: Dash travels over time** through `PlayerMotor` rather than teleporting, and is
      **cancelled on death** — bug 2.10 was a running dash coroutine overwriting the respawn position
      afterwards, returning the player to where they died.
- [ ] **Step 3: Verify in the test range** — dash covers 3m and has 2 charges recharging one at a
      time; blink covers 9m instantly and cannot land inside a wall; sprint is 50% faster and
      silences the player after about 5.5s of holding it.
- [ ] **Step 4: Verify with two clients** that remote dashes and blinks look correct, then **die
      mid-dash and confirm you respawn at the spawn point**, not where you died.
- [ ] **Step 5: Commit.**

## Task 1.7: Mobility — teleport portals and zip gun

**Files:**
- Create: `Assets/scripts/Abilities/Mobility/TeleportAbility.cs`, `ZipGunAbility.cs`
- Create: `Assets/Gameplay/Abilities/Teleport.prefab`, `Zip Gun.prefab`, `Assets/Gameplay/Prefabs/Portal.prefab`

The first genuinely new **networked object type**, so the `instantiationData` pattern gets its first
real test here.

| Ability | Field | Value | Whose |
|---|---|---|---|
| Teleport | `portalRadius` | 2.5 (diameter per [T]: a 2.5m circle) | [T] |
| | `placementRange` | 5 | [T] |
| | `maxPortals` | 2 | [T] |
| | `channelSeconds` | 3 | [T] |
| | `gateCooldownSeconds` | 10 | [T] |
| | `personalOnly` | true | [T] |
| Zip gun | `range` | 15 | [T] |
| | `cooldownSeconds` | 15 | [T] |
| | `resetsOnTakedown` | true | [T] |
| | `pullSpeed` | 25 | [C] |
| | `projectileSpeed` | 40 | [C] |

**Portals are personal** — only the player who placed them can travel through. Tudor's clarification.
So the trigger check compares the entering player's actor number against the portal's owner, which
arrives in `instantiationData`.

**Takedown = a kill or an assist** — Tudor's clarification. The zip gun subscribes to the damage
funnel's kill/assist signal rather than counting kills itself.

- [ ] **Step 1: `Portal.prefab`** — a networked object owned by its placer, carrying owner actor
      number, radius and its paired portal's view id in `instantiationData`. Placing a third portal
      destroys the oldest.
- [ ] **Step 2: `TeleportAbility`** — the 3-second channel is interrupted by leaving the circle, and
      the 10s gate cooldown starts after a successful travel.
- [ ] **Step 3: `ZipGunAbility`** — a projectile that on hitting a wall **or a player** pulls the
      **shooter** toward it. Pull direction must be computed from the shooter's position as a
      parameter, not from `transform.position` inside an RPC.
- [ ] **Step 4: Verify with two clients** — a remote player's portals are visible but not usable by
      you; travelling works in both directions; the zip gun pulls you and not the target; a takedown
      resets its cooldown; **a late joiner sees existing portals** (this is the late-joiner test from
      brief fact #4 — if portals are RPC-only they will not appear).
- [ ] **Step 5: Commit.**

## Task 1.8: Equipment — mines and deployable cover

**Files:**
- Create: `Assets/scripts/Abilities/Equipment/MineAbility.cs`, `DeployableCoverAbility.cs`
- Create: `Assets/Gameplay/Abilities/Mines.prefab`, `Deployable Cover.prefab`
- Create: `Assets/Gameplay/Prefabs/Mine.prefab`, `Cover Wall.prefab`

| Ability | Field | Value | Whose |
|---|---|---|---|
| Mines | `damage` | 20 | [T] |
| | `charges` | 2 | [T] |
| | `cooldownSeconds` | 10 per charge | [T] |
| | `slowMagnitude` | 0.40 | **[C]** |
| | `slowSeconds` | 2.0 | **[C]** |
| | `triggerRadius` | 1.8 | **[C]** |
| | `explosionRadius` | 2.2 | **[C]** |
| | `armDelaySeconds` | 0.5 | **[C]** |
| | `persistSeconds` | 45 | **[C]** |
| Cover | `durationSeconds` | 10 | [T] |
| | `hitPoints` | 100 | [T] |
| | `cooldownSeconds` | 20 | [T] |
| | `width` | 3.0 | **[C]** |
| | `height` | 2.0 | **[C]** |
| | `placementDistance` | 3.0 | **[C]** |

**Cover blocks projectiles in both directions, including the caster's own** — Tudor's decision of
2026-09-12. It is cover to fight around, not a shield to shoot out of. The 6-second indestructible
variant considered that day was reverted, because the hit points are the point of the ability: they
turn it into a decision for the enemy — spend damage removing the wall, or spend it on the player
behind it.

The cover wall therefore implements `IDamageable` with 100 HP and goes through the same damage funnel
as a player. That is the payoff for having one funnel.

- [ ] **Step 1: `Mine.prefab`** — proximity trigger on enemies only, arm delay so you cannot
      instantly detonate it at your feet, expires after `persistSeconds` so a match does not
      accumulate mines forever.
- [ ] **Step 2: `Cover Wall.prefab`** — a collider on the `Building` layer so existing
      wall-blocking logic applies to it for free, plus `IDamageable`. Destroyed at 0 HP or at
      `durationSeconds`, whichever comes first.
- [ ] **Step 3: Verify** — two mines placeable, each on its own 10s timer; a dummy walking over one
      takes 20 and is slowed; your own bullets stop at your own cover; 100 damage destroys it before
      the 10s expiry.
- [ ] **Step 4: Verify with two clients** — a remote player's mine damages you once, not twice, and
      cover blocks a remote player's shots.
- [ ] **Step 5: Commit.**

## Task 1.9: Equipment — flamethrower and stun gun

**Files:**
- Create: `Assets/scripts/Abilities/Equipment/FlamethrowerAbility.cs`, `StunGunAbility.cs`
- Create: `Assets/Gameplay/Abilities/Flamethrower.prefab`, `Stun Gun.prefab`

| Ability | Field | Value | Whose |
|---|---|---|---|
| Flamethrower | `burnDamagePerSecond` | 5 | [T] |
| | `burnSeconds` | 5 | [T] |
| | `cooldownSeconds` | 13 | [T] |
| | `coneAngle` | 45 | **[C]** |
| | `coneRange` | 7 | **[C]** |
| | `spraySeconds` | 1.0 | **[C]** |
| Stun gun | `stunSeconds` | 2.5 | [T] |
| | `range` | 8 | [T] |
| | `cooldownSeconds` | 17 | [T] |
| | `projectileSpeed` | 16 | **[C]** |
| | `projectileRadius` | 0.45 | **[C]** |
| | `damage` | 0 | **[C]** — pure utility |

**The flamethrower applies the burn on contact and then the burn ticks on its own** — it does not
require holding the target in the cone. Tudor's clarification, and it is the difference between a
usable ability and an unusable one in a top-down game.

Burn goes through `StatusEffectState` with the `Refresh` stack rule, and its damage is routed through
`DamageResolver` so armor still absorbs it.

- [ ] **Step 1: Implement both**, stun applying `StatusKind.Stun` which `PlayerStatusEffects` feeds to
      the motor and the ability runner.
- [ ] **Step 2: Verify** — a dummy caught in the cone burns for 25 total over 5 seconds even after
      leaving the cone; re-applying refreshes rather than doubling; a stunned dummy cannot move for
      2.5s.
- [ ] **Step 3: Verify with two clients** that a stun on a remote player actually stops their input,
      which means the stun must be applied on the **victim's** client.
- [ ] **Step 4: Commit.**

## Task 1.10: Equipment — sonic pulse and raybeam

**Files:**
- Create: `Assets/scripts/Abilities/Equipment/SonicPulseAbility.cs`, `RaybeamAbility.cs`
- Create: `Assets/Gameplay/Abilities/Sonic Pulse.prefab`, `Raybeam.prefab`

| Ability | Field | Value | Whose |
|---|---|---|---|
| Sonic pulse | `knockbackDistance` | 5 | [T] |
| | `coneRange` | 4 | [T] |
| | `collisionStunSeconds` | 2.0 | [T] |
| | `cooldownSeconds` | 8 | [T] |
| | `coneAngle` | 90 | **[C]** |
| | `knockbackSpeed` | 14 | **[C]** |
| | `damage` | 0 | **[C]** |
| Raybeam | `beams` | 3 | [T] |
| | `vulnerabilityPerBeam` | 0.30 | [T] |
| | `vulnerabilityCap` | 0.60 | [T] |
| | `vulnerabilitySeconds` | 4 | [T] |
| | `cooldownSeconds` | 20 | [T] |
| | `beamRange` | 12 | **[C]** |
| | `beamWidth` | 0.35 | **[C]** |
| | `damage` | 0 | **[C]** |

**Sonic pulse stuns enemies only.** A teammate caught in a collision is not stunned, so this cannot
be used to grief your own team — Tudor's clarification.

**The raybeam's third beam is redundancy against a partial miss, not extra damage.** At the
convergence point all three land, so the skill is in placing the cursor at the right distance. One
beam is +30%, two or three is +60%. Implemented through `StatusKind.Vulnerability` with the
`StackToCap` rule, which Task 0.3 already built and tested.

- [ ] **Step 1: Implement both.** Knockback moves the **victim** on the victim's own client, driven by
      an RPC carrying the direction and distance as parameters.
- [ ] **Step 2: Collision detection during knockback** — if the victim's sweep hits a wall or another
      **enemy**, stun both for 2s.
- [ ] **Step 3: Verify** — a dummy pushed into a wall is stunned; two dummies colliding are both
      stunned; a teammate is not; three beams on one dummy show +60% and not +90% damage taken.
- [ ] **Step 4: Commit.**

## Task 1.11: Ultimates

**Files:**
- Create: `Assets/scripts/Abilities/Ultimate/ElectricFenceAbility.cs`, `AoeZoneAbility.cs`, `InvulnerabilityAbility.cs`
- Create: `Assets/Gameplay/Abilities/Electric Fence.prefab`, `AoE Zone.prefab`, `Invulnerability.prefab`
- Create: `Assets/scripts/Player/UltimateCharge.cs`

Three ultimates, **possibly one fewer than a real choice needs** — Tudor's own note, worth watching in
the first playtest. The multi-dash mark was cut on 2026-09-11 because it scaled damage with the
number of dashes a target had been hit with, and dashes here are pure movement that cannot hit
anything.

The GDD specifies almost nothing numerically for these, so nearly every value is Claude's.

| Ability | Field | Value | Whose |
|---|---|---|---|
| Electric fence | `radius` | 6 | **[C]** |
| | `ringThickness` | 1.0 | **[C]** |
| | `damagePerPass` | 25 | **[C]** |
| | `perTargetCooldown` | 1.0 | **[C]** |
| | `slowMagnitude` | 0.50 | **[C]** |
| | `slowSeconds` | 1.5 | **[C]** |
| | `durationSeconds` | 8 | **[C]** |
| AoE zone | `durationSeconds` | 6 | [G] |
| | `tickSeconds` | 1.0 | [G] |
| | `radius` | 5 | **[C]** |
| | `damagePerTick` | 14 | **[C]** — 84 over the full duration |
| Invulnerability | `durationSeconds` | 2.5 | **[C]** |

**Invulnerability is a panic button, not an engage tool** — you survive the burst but cannot act
during it, because the caster is stunned for the duration. Tudor's clarification. Implemented as
`StatusKind.Invulnerability` plus `StatusKind.Stun` on the caster, both for the same duration.

**`UltimateCharge`** implements the GDD's "resource meter generated through active combat
participation (damage dealt, damage taken, kills/assists)". All Claude's numbers:

| Field | Value |
|---|---|
| `maxCharge` | 1000 |
| `chargePerDamageDealt` | 1.0 per point |
| `chargePerDamageTaken` | 0.5 per point |
| `chargePerKill` | 150 |
| `chargePerAssist` | 75 |

At these rates, killing two opponents outright charges roughly 55% of an ultimate, which puts the
GDD's intended ~13.5 minute first ultimate in reach without making it automatic.

- [ ] **Step 1: Implement `UltimateCharge`** with edit-mode tests for each accrual source and the cap.
- [ ] **Step 2: Implement the three ultimates.** The AoE zone replaces the deleted `Aoe Ability.cs`
      and must **not** reproduce its two bugs: `ApplyDamageOverTime` logged every tick (70 log lines
      per cast on every client, which caused the round-2 freezes), and the damage coroutine ran on
      every client so each one called `PhotonNetwork.Destroy`. Tick on the owner only; no per-tick
      logging.
- [ ] **Step 3: Verify** — the AoE zone deals 14 per second for 6 seconds and nothing after; the
      fence damages a dummy crossing it at most once per second; invulnerability prevents all damage
      and also prevents acting.
- [ ] **Step 4: Verify with two clients** and check the console line count before and after a cast
      using the `cursor` value from `unity command console` — a cast must not produce dozens of log
      lines.
- [ ] **Step 5: Commit.**

## Task 1.12: HUD

**Files:**
- Modify: `Assets/Resources/Multiplayer Player.prefab`
- Create: `Assets/scripts/UI/PlayerHud.cs`

Bars and cooldowns for everything built above: health, armor (as a separate segment above health),
overheat with a visible **warning state at 80** — which was Tudor's stated condition for accepting
full-silence overheat, so it is a requirement and not polish — four slot icons with cooldown sweeps
and charge pips, and the ultimate charge meter.

- [ ] **Step 1: Build the HUD** on the player prefab's existing canvas, reusing the health slider
      already there.
- [ ] **Step 2: Verify** every bar against the test range, especially that the overheat bar turns the
      warning colour at 80 and reads as the player's mistake rather than a wall.
- [ ] **Step 3: Commit.**

---

# Phase 2 — Match loop

## Task 2.1: Territory tiers, adjacency and the Tier-4 centre

**Files:**
- Modify: `Assets/scripts/Player/Building capture.cs`, `Assets/scripts/BuildingManager.cs`
- Create: `Assets/scripts/Territory/AdjacencyGraph.cs`, `Assets/scripts/Data/TerritoryConfig.cs`
- Create: `Assets/Gameplay/Config/TerritoryConfig.asset`
- Modify: `Assets/Scenes/Game Scene.unity` (a tenth capture point)

Tudor's choice: **layer tiers onto the existing nine towers and add a centre.** No geometry rebuild.
Towers 6, 7 and 8 are already hardcoded as the three capitals in
`BuildingManager.CathedralBuildingIDs`; the remaining six split into three Tier 2 and three Tier 3 by
position, and a new tower with `buildingID: 9` becomes the Tier 4 centre.

`TerritoryConfig`, all GDD numbers:

| Tier | Capture seconds | Gold/sec | Capture bounty |
|---|---|---|---|
| 1 — Capital | 20 | 0 | 0 |
| 2 — Transition | 15 | 5 | 0 |
| 3 — Flanking | 10 | 10 | 900 |
| 4 — Centre | 15 | 8 | 1200 |

Adjacency is a **serialized list of zone ids per zone**, editable in the Inspector — Tudor's
requirement. `AdjacencyGraph` answers "may team T capture zone Z", which is true when T controls at
least one zone adjacent to Z.

- [ ] **Step 1: Write `AdjacencyGraph` tests** — a team holding an adjacent zone may capture; a team
      holding nothing adjacent may not; a team already owning the zone may not recapture it (this is
      bug 2.9's second cause: `CompleteCapture` never checked whether the tower was already yours);
      a capital is always capturable by its owning team even with nothing adjacent, so a team that
      loses everything is not permanently locked out.
- [ ] **Step 2: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 3: Add `tier` and `adjacentZoneIds` to `BuildingCapture`** and drive `captureThreshold`
      from `TerritoryConfig` by tier instead of the current single shared value.
- [ ] **Step 4: Expose `captureProgress`** — it is currently private with no readers, so **nothing in
      the game shows a capture happening.** Add a readable property and a world-space progress bar.
      Also make the capture sound positional rather than broadcast map-wide.
- [ ] **Step 5: Add tower 9** to the scene at the map centre via `unity command instantiate_prefab`,
      wire it into `BuildingManager.TowerDictionary`, and set the adjacency lists for all ten zones.
- [ ] **Step 6: Verify with two clients**, including that a mid-match joiner receives all ten
      ownership states (the existing `AllBuffered` path already does this — confirm it still does).
- [ ] **Step 7: Commit.**

## Task 2.2: Gold income

**Files:**
- Create: `Assets/scripts/Economy/GoldWallet.cs`, `Assets/scripts/Economy/IncomeTicker.cs`

Gold is per player and accrues from the team's held territories. The **master client** owns income
accrual, because territory ownership is master-authoritative; each player's gold total lives in their
own Custom Properties so a late joiner and the shop both see it.

- [ ] **Step 1: Write `GoldWallet` tests** — income sums correctly across held tiers; a team holding
      one Tier 3 and one Tier 2 earns 15/sec, matching the GDD's "struggling" row; spending more than
      you hold is refused; a 50% refund returns exactly half, rounded down.
- [ ] **Step 2: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 3: `IncomeTicker`** accrues once per second on the master and writes each player's total
      to their Custom Properties, rather than every client computing its own and drifting.
- [ ] **Step 4: Verify with two clients** that both see the same gold totals and that a joiner gets
      the right figure.
- [ ] **Step 5: Commit.**

## Task 2.3: Shop

**Files:**
- Create: `Assets/scripts/Economy/ShopPanel.cs`, `Assets/scripts/Economy/PurchaseGate.cs`

GDD purchasing rules: the player must be **in a captured territory** and **out of combat for at least
5 seconds**; upgrades can be undone for a **50% refund**. Bound to P.

Costs, all from the GDD balancing sheet: primary upgrade 1 = 1200, primary upgrade 2 = 1600,
armor upgrade 1 = 1400, armor upgrade 2 = 1800, ultimate = 1550. Equipment and mobility re-picks are
not priced in the sheet — **800 each [C]**, low enough that adapting to a matchup is viable and high
enough that it is not free. Passives are priced at 2200 in the sheet but **there are no passives in
this prototype**, so that line is unimplemented by design.

- [ ] **Step 1: Write `PurchaseGate` tests** — refused while in combat; refused outside your own
      territory; allowed when both conditions hold; the out-of-combat clock uses the same
      `SecondsSinceCombat` that armor regen uses, so the two cannot disagree.
- [ ] **Step 2: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 3: Build `ShopPanel`** — the weapon upgrade tree drawn from `WeaponCatalogue.parent`
      links, so adding a weapon asset makes it appear in the shop with no UI work.
- [ ] **Step 4: Verify with two clients** — buying an upgrade changes your weapon and the other
      client sees the change (weapon id replicates through Custom Properties); selling refunds half.
- [ ] **Step 5: Commit.**

## Task 2.4: OverPower comeback mechanic

**Files:**
- Create: `Assets/scripts/Match/OverPowerBuff.cs`

The GDD's rule: if a player is attacked by **both** enemy teams within a 3-second interval near a
territory they control, then whenever they drop under 35 HP they instantly regenerate their shield
and gain **+10% on three of the highest parameters** of their primary ability while the overheat
mechanic is nullified. Moving more than **15 metres** from that territory expires the buff instantly,
triggered or not.

This is a comeback mechanic meant to reward outnumbered defence and reduce third-party bullying. It
is second from the bottom of the cut list.

**One ambiguity to resolve explicitly rather than guess silently:** "three of the highest parameters"
is not well defined for a weapon whose stats are on an SO. Implemented as **damage, fire rate and
range**, each +10%, recorded as Claude's interpretation [C]. Tudor should confirm this is what he
meant.

- [ ] **Step 1: Write tests** for the trigger condition — damage from two different enemy teams
      within 3s arms it; damage from one team twice does not; being more than 15m from an owned
      territory disarms it; dropping below 35 HP while armed fires it once.
- [ ] **Step 2: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 3: Verify with three clients if possible**, or by simulating a second attacking team
      from the test range if only two are available.
- [ ] **Step 4: Commit.**

## Task 2.5: Bounce-back bounty

**Files:**
- Modify: `Assets/scripts/Player/Building capture.cs`, `Assets/scripts/Economy/GoldWallet.cs`

GDD: a team holding a Tier 3 or Tier 4 territory for more than **5 minutes uninterrupted** earns a
bounty on it; when an enemy team captures it they receive that bounty instantly. Bounty values come
from `TerritoryConfig` (900 for Tier 3, 1200 for Tier 4).

- [ ] **Step 1: Write tests** — a hold of 4:59 pays nothing; 5:01 pays the tier's bounty; the timer
      resets when ownership changes; a contested-but-not-captured zone does not reset it.
- [ ] **Step 2: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 3: Verify with two clients.**
- [ ] **Step 4: Commit.**

## Task 2.6: Phase transitions

**Files:**
- Create: `Assets/scripts/Match/MatchPhase.cs`

**This is the task expected to be cut.** It is last deliberately.

GDD: Phase 1 with three teams alive, where a team is eliminated only when their capital is captured
**and** all their members are eliminated afterwards, creating a last-stand window in which they can
reclaim their capital or capture an enemy one. On the first elimination, the dead team's capital and
connected route are removed, the map becomes a two-lane trapezoid, all Tier 3 territories go neutral
and every team returns to their capital. Phase 2 drops the last-stand rule entirely: the remaining
enemy team is eliminated on losing their capital.

The existing code already has the last-stand half of this: `RPC_HandleDeathMaster` implements
capital-down permanent death, which is the cause of the round-1 "players sometimes invisible" bug
that turned out to be working as designed. What is missing is the phase transition and map reduction.

**If this task is reached, cut the map reduction first and keep the phase rule change.** Removing
geometry is the expensive half and the least important to the match feeling different; the rule
change is what alters how Phase 2 plays.

- [ ] **Step 1: Write tests** for the phase state machine — three teams alive is Phase 1; one team
      eliminated moves to Phase 2; in Phase 2 losing a capital eliminates immediately without a last
      stand; a team that captures an enemy capital during its own last stand adopts it as its new
      capital.
- [ ] **Step 2: Run to verify they fail, implement, run to verify they pass.**
- [ ] **Step 3: Verify with three clients.** Two clients cannot exercise a three-team phase
      transition, so if only two are available, say so in the findings rather than claiming it works.
- [ ] **Step 4: Commit.**

---

# Final task: the deliverable

**Files:**
- Create: `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\findings.md`

One document, kept brief per Tudor's instruction, containing:

1. **The numbers provenance table** — every value, where it lives, and whether it is Tudor's, the
   GDD's, or Claude's. This is the thing he said he cannot recover by playing the game, so it is not
   optional and not summarisable.
2. **Feasibility verdict per ability** — built, built with a caveat, or not built and why.
3. **Answers to his five judging questions**, including the honest ones: what did it do that he
   would not have approved of, and which three files should he pick to test whether he can read code
   he did not write.
4. **Every conflict between "build more" and "keep it clean"** and which was chosen, as the brief
   requires.
5. **What is safe to playtest and what is not**, since a test with 2–3 people follows the window.

---

## Self-review against the spec

Checked after writing, per the writing-plans skill.

**Spec coverage.** Every numbered spec section maps to at least one task: §1 data layer → Tasks 0.4,
0.8; §2 damage funnel → 0.2; §3 status effects → 0.3; §4 networking contract → standing rules plus
Tasks 0.9–0.11, 1.7, 1.8; §5 file split → 0.9–0.11; §6 input → 0.12; §7 test range → 0.14;
§8 number anchors → 0.15 and all of Phase 1; §9 build order → the phase structure; §10 verification
→ the standing rules.

**Gaps found and closed while reviewing:** the spec listed `AdjacencyGraph` under pure logic but no
task created it until Phase 2 — moved its tests into Task 2.1 explicitly. The spec mentioned an
`ApplyStatusOnHit` projectile component with no owning task — it is created in Task 1.9 with the
flamethrower, the first weapon that needs it. `UltimateCharge` appears in the spec's Phase 2 list but
is built in Task 1.11, because the ultimates cannot be tested without it; noted there rather than
left inconsistent.

**Type consistency.** `DamageInfo`, `DamageResult`, `IDamageable`, `StatusEffectSpec`,
`StatusEffectState`, `OverheatState`, `AimConeState`, `ChargePool`, `ArmorState` and `RangeBudget`
are used with the same member names in every later task as they are defined in Tasks 0.2–0.13.
`PlayerHealth.NoteDealtDamage` is the only cross-component call introduced late; it is defined in
Task 0.9's public surface.

**Known deviation from the skill, recorded deliberately.** Full implementation code is inlined only
for the shared contracts, where drift between tasks is fatal. Content tasks carry exact number tables
and behaviour descriptions instead. **This trades plan fidelity for build time** — the conflict the
brief asks to have named rather than silently resolved. The risk accepted is that a subagent
interprets a behaviour description differently than it would have been written; the per-task review
between subagents is the control.
