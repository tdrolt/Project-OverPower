# Match Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Every client writes a JSONL log of the match facts it owns.
- An Editor menu merges a match folder into `report.html` + 12 CSVs, so Tudor can balance the economy, territory and
  combat.

**Architecture:**
- **Runtime** (`Assets/scripts/Telemetry/`, namespace `Overpower.Telemetry`):
  - a pure line builder and pure rules;
  - `TelemetryConfig`;
  - `MatchTelemetry` (scene: match id, file writer, session header, master-only territory events);
  - `PlayerTelemetry` (player prefab, owner only: samples and the player's own events).
- **Gameplay code** only gains C# events and one `DamageInfo.AbilityId` field. Delete the Telemetry folder and the
  game still runs.
- **Editor** (`Assets/scripts/Editor/Telemetry/`): parse with Newtonsoft, then a pure `TelemetryAggregator` → tables,
  then `CsvReportWriter` and `HtmlReportWriter` over the same tables.

**Tech Stack:** Unity 6000.0.70f1, Photon PUN 2 (Room Properties for match id/start), Newtonsoft.Json (Editor
parsing), NUnit edit-mode tests, Chart.js from jsDelivr inside the generated HTML.

**Spec:** `docs/superpowers/specs/2026-09-16-telemetry-design.md`. Read it; its event table is the contract.

**Changes from the spec, decided while planning [C]:**
- `phase` / `elimination` events are left to Task 2.7, which adds them to `MatchTelemetry`. 2.7 runs after telemetry.
- Two presence events are added: `underAttack` start/end per zone (master), from `ZonePresenceTracker`. Built today;
  cheap and useful for balancing.
- Overheat, ultimate and heal are **observed by polling state changes in `PlayerTelemetry`** (owner, once per frame),
  instead of new gameplay events. There are fewer gameplay edits, and the state is already public.

---

## Rules for every task (each has cost hours on this project)

1. Branch `limit-testing` only; push after each task's commits. `unity command editor_status` must answer before
   editing.
2. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `editor_stop`, poll until
   `playMode: "stopped"` and `compiling: false`.
3. `recompile_status` is the only compile truth. **Tests async only:**
   `unity command run_tests -- --mode editor --async_tests true`, then poll `test_status`.
4. **Dirty scene → modal dialog → silent Editor hang.** Read `SceneManager.GetActiveScene().isDirty` and proceed only if
   False. Don't chain it with `&&` and continue anyway. To discard: `EditorSceneManager.OpenScene("Assets/Scenes/Game
   Scene.unity", OpenSceneMode.Single)`. If `editor_status` times out: stop and report.
5. **CLI:**
   - `unity command <name> -- --flag value`
   - long scripts: `unity command --timeout 240 run_script -- --file ... --entry X.Y --timeout_ms 200000`
   - eval writes `UnityEngine.Object`
   - scratch files go in the session scratchpad
     `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad`
6. **Photon:**
   - Inside an RPC, "local" is the receiver.
   - Late-joiner state goes in Custom Properties.
   - Damage/status is victim-side.
   - `ServerTimestamp` reads 0 right after connecting, so guard it.
   - `PlayerNetSync` is the only `IPunObservable`.
   - **Add no RPC** (the RpcList asset must be unchanged).
7. Never move players via `transform.position`; use `PlayerDisplacement.TeleportTo`, outside building colliders, and
   wait a frame before firing. Use `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\two-client-eval\
   shot_recorder_tpl.cs` / `fire_driver_tpl.cs` for shot measurements.
8. **Aim follows the real OS cursor while unfocused** unless the 2.6 fix (`PlayerAim.SetAimOverride`) has landed. Use
   the override for any aimed shot.
9. **Captures** (616×576) must be saved with an explicit path in the scratchpad, and looked at yourself.
10. Every designer-facing value goes on an asset with a plain `[Tooltip]`, in one home. Comments explain *why*.
    `GameplayConfig.asset` stays unchanged; the `UiTheme.asset` diff is only the new fields.
11. Commit trailer: your own `Co-Authored-By:` line. Append [C] decisions to
    `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under `## Telemetry
    (2026-09-16)`.

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/Telemetry/TelemetryLine.cs` + `Assets/Tests/TelemetryLineTests.cs` | Allocation-light JSON line builder (invariant culture, escaping) | T1 |
| `Assets/scripts/Telemetry/IncomeAttribution.cs` + tests | Split territory income per zone | T1 |
| `Assets/scripts/Telemetry/MatchClock.cs` + tests | Match seconds from server ms (wrap-safe, 0-guarded) | T1 |
| `Assets/scripts/Data/TelemetryConfig.cs` + `Assets/Gameplay/Config/TelemetryConfig.asset` | Settings | T2 |
| `Assets/scripts/Telemetry/TelemetryWriter.cs` | Buffered file writer; an IO failure disables it | T2 |
| `Assets/scripts/Telemetry/MatchTelemetry.cs` (scene) | Match id/start, file, header, join/leave/master, markers, master territory events | T2, T4 |
| `Assets/scripts/Telemetry/TuningSnapshot.cs` | Header JSON of configs and definitions | T2 |
| `Assets/scripts/Editor/Telemetry/BuildInfoWriter.cs` | Pre-build: commit hash → `Assets/Resources/BuildInfo.txt` (gitignored) | T2 |
| `Assets/scripts/TestRange/TestRangePanel.cs` (modify) | F1: Open telemetry folder, Drop marker, status line | T2 |
| `Assets/scripts/Combat/DamageInfo.cs` + its 9 construction sites (modify) | `AbilityId` | T3 |
| `WeaponFiring`, `AbilityRunner`, `PlayerStatusEffects`, `OverPowerBuff` (modify) | `Fired`, `Cast`, `StatusApplied`, `Triggered`/`Ended` events | T3 |
| `Assets/scripts/Telemetry/PlayerTelemetry.cs` (player prefab) | Samples + owner events | T3, T4 |
| `GoldWallet`, `LoadoutScreen`, `TestRangePanel` (modify) | `Credited(amount, source)`, `Spent`; purchase/refund/refused events | T4 |
| `BuildingManager`, `ZonePresenceTracker` (modify) | `CaptureProgressChanged`, `UnderAttackChanged` events | T4 |
| `Assets/scripts/Editor/Telemetry/TelemetryLog.cs` | Parse JSONL folder → typed events | T5 |
| `Assets/scripts/Editor/Telemetry/TelemetryAggregator.cs` + `Assets/Tests/TelemetryAggregatorTests.cs` + fixtures | Pure tables | T5 |
| `Assets/scripts/Editor/Telemetry/CsvReportWriter.cs` | 12 CSVs | T5 |
| `Assets/scripts/Data/BalanceTargets.cs` + asset | GDD reference lines | T6 |
| `Assets/scripts/Editor/Telemetry/HtmlReportWriter.cs`, `TelemetryMenu.cs` | HTML + menu + arena render | T6 |

**Event line format** (every line one JSON object; short keys; `t` = match seconds, `-1` before the match clock is
known):

```json
{"e":"hit","t":123.45,"a":3,"at":1,"v":2,"vt":0,"w":5,"ab":-1,"src":"Projectile","raw":11,"arm":11,"hp":0,"lethal":false,"d":7.2,"vul":0,"inv":false,"op":false}
```

The full key table is in T2 step 1 (`TelemetryKeys`). Aggregator and writers use those constants only.

---

### Task T1: Pure core (line builder, income attribution, match clock)

**Files:** create `TelemetryLine.cs`, `IncomeAttribution.cs`, `MatchClock.cs` under `Assets/scripts/Telemetry/`, and
their tests under `Assets/Tests/`.

- [ ] **Step 1: Failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    public class TelemetryLineTests
    {
        [Test]
        public void BuildsOneFlatJsonObjectWithInvariantNumbers()
        {
            var line = new TelemetryLine();
            line.Begin("hit", 12.5).Int("a", 3).Float("raw", 11.25f).Bool("lethal", false).String("src", "Projectile");
            Assert.AreEqual("{\"e\":\"hit\",\"t\":12.5,\"a\":3,\"raw\":11.25,\"lethal\":false,\"src\":\"Projectile\"}", line.End());
        }

        [Test]
        public void EscapesQuotesBackslashesAndControlCharacters()
        {
            var line = new TelemetryLine();
            line.Begin("marker", 1).String("note", "a \"b\" \\ c\nd");
            Assert.AreEqual("{\"e\":\"marker\",\"t\":1,\"note\":\"a \\\"b\\\" \\\\ c\\nd\"}", line.End());
        }

        [Test]
        public void WritesIntArrays()
        {
            var line = new TelemetryLine();
            line.Begin("sample", 0).Ints("ids", new[] { 1, -1, 25 });
            Assert.AreEqual("{\"e\":\"sample\",\"t\":0,\"ids\":[1,-1,25]}", line.End());
        }

        [Test]
        public void NonFiniteFloatsBecomeNull()
        {
            var line = new TelemetryLine();
            line.Begin("x", 0).Float("f", float.NaN);
            Assert.AreEqual("{\"e\":\"x\",\"t\":0,\"f\":null}", line.End());
        }

        [Test]
        public void TheBuilderIsReusable()
        {
            var line = new TelemetryLine();
            line.Begin("a", 1).Int("n", 1); line.End();
            line.Begin("b", 2).Int("n", 2);
            Assert.AreEqual("{\"e\":\"b\",\"t\":2,\"n\":2}", line.End());
        }
    }

    public class IncomeAttributionTests
    {
        [Test]
        public void EachOwnedZoneGetsItsTierRateDividedByPlayersPerTeam()
        {
            // Team 0 owns zone 0 (T2, 5/s) and zone 3 (T3, 10/s); 3 players per team; 5 seconds.
            int[] owners = { 0, 1, -1, 0 };
            int[] tiers = { 2, 2, 2, 3 };
            int[] tierRates = { 0, 5, 10, 8 };
            var perZone = new double[4];
            IncomeAttribution.Accumulate(0, owners, tiers, tierRates, 3, 5.0, perZone);
            Assert.AreEqual(5.0 * 5 / 3, perZone[0], 1e-9);
            Assert.AreEqual(0.0, perZone[1], 1e-9);
            Assert.AreEqual(10.0 * 5 / 3, perZone[3], 1e-9);
        }

        [Test]
        public void TheSplitSumsToTheWalletsTerritoryIncome()
        {
            int[] owners = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int[] tiers = { 2, 2, 2, 3, 3, 3, 1, 1, 1, 4 };
            int[] tierRates = { 0, 5, 10, 8 };
            var perZone = new double[10];
            IncomeAttribution.Accumulate(0, owners, tiers, tierRates, 3, 2.0, perZone);
            double sum = 0; foreach (double v in perZone) sum += v;
            double walletRate = (3 * 5 + 3 * 10 + 3 * 0 + 8) / 3.0; // GoldMath's team income ÷ players
            Assert.AreEqual(walletRate * 2.0, sum, 1e-9);
        }

        [Test]
        public void NoTeamOrZeroPlayersAddsNothing()
        {
            var perZone = new double[2];
            IncomeAttribution.Accumulate(-1, new[] { -1, -1 }, new[] { 2, 2 }, new[] { 0, 5, 10, 8 }, 3, 1, perZone);
            IncomeAttribution.Accumulate(0, new[] { 0, 0 }, new[] { 2, 2 }, new[] { 0, 5, 10, 8 }, 0, 1, perZone);
            Assert.AreEqual(0, perZone[0]); Assert.AreEqual(0, perZone[1]);
        }
    }

    public class MatchClockTests
    {
        [Test]
        public void MatchSecondsCountFromTheStartStamp() =>
            Assert.AreEqual(12.5, MatchClock.Seconds(nowMs: 112500, startMs: 100000), 1e-9);

        [Test]
        public void WorksAcrossTheServerClockWrap() =>
            Assert.AreEqual(2.0, MatchClock.Seconds(int.MinValue + 1000, int.MaxValue - 999), 1e-9);

        [Test]
        public void UnknownClockOrStartIsMinusOne()
        {
            Assert.AreEqual(-1.0, MatchClock.Seconds(0, 100000));
            Assert.AreEqual(-1.0, MatchClock.Seconds(100000, 0));
        }
    }
}
```

- [ ] **Step 2: Recompile.** Expect compile errors naming `Overpower.Telemetry` types.

- [ ] **Step 3: Implement**

```csharp
// Assets/scripts/Telemetry/TelemetryLine.cs
using System.Globalization;
using System.Text;

namespace Overpower.Telemetry
{
    /// <summary>Builds one telemetry event as a single flat JSON line. It reuses one StringBuilder, so logging
    /// dozens of events a second doesn't churn memory. Numbers are always written the same way whatever language
    /// Windows is set to (invariant culture), or a Dutch or German PC would write "12,5" and break the report.</summary>
    public sealed class TelemetryLine
    {
        private readonly StringBuilder sb = new StringBuilder(256);

        public TelemetryLine Begin(string eventName, double matchSeconds)
        {
            sb.Clear();
            sb.Append("{\"e\":");
            AppendString(eventName);
            sb.Append(",\"t\":");
            sb.Append(System.Math.Round(matchSeconds, 3).ToString("0.###", CultureInfo.InvariantCulture));
            return this;
        }

        public TelemetryLine Int(string key, int value) { Key(key); sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }

        public TelemetryLine Float(string key, float value)
        {
            Key(key);
            if (float.IsNaN(value) || float.IsInfinity(value)) sb.Append("null");
            else sb.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
            return this;
        }

        public TelemetryLine Bool(string key, bool value) { Key(key); sb.Append(value ? "true" : "false"); return this; }

        public TelemetryLine String(string key, string value) { Key(key); AppendString(value ?? ""); return this; }

        public TelemetryLine Ints(string key, int[] values)
        {
            Key(key); sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return this;
        }

        /// <summary>A raw, already-valid JSON value (used only for the session header's tuning snapshot).</summary>
        public TelemetryLine Raw(string key, string json) { Key(key); sb.Append(json); return this; }

        public string End() { sb.Append('}'); return sb.ToString(); }

        private void Key(string key) { sb.Append(','); AppendString(key); sb.Append(':'); }

        private void AppendString(string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
```

```csharp
// Assets/scripts/Telemetry/IncomeAttribution.cs
namespace Overpower.Telemetry
{
    /// <summary>Which zones a player's territory gold came from. The wallet only knows a total income per second. This
    /// splits the same number back per zone (each owned zone's tier rate ÷ players per team, as GoldMath pays it), so
    /// the report can show how much each zone earned each team.</summary>
    public static class IncomeAttribution
    {
        /// <param name="teamGoldByTier">Element 0 = Tier 1's team gold per second (the TerritoryConfig tier order).</param>
        /// <param name="perZoneGold">Added to: gold this player earned from each zone over <paramref name="seconds"/>.</param>
        public static void Accumulate(int team, int[] ownerByZone, int[] tierByZone, int[] teamGoldByTier,
                                      int playersPerTeam, double seconds, double[] perZoneGold)
        {
            if (team < 0 || playersPerTeam <= 0 || seconds <= 0) return;
            int zones = System.Math.Min(System.Math.Min(ownerByZone.Length, tierByZone.Length), perZoneGold.Length);
            for (int zone = 0; zone < zones; zone++)
            {
                if (ownerByZone[zone] != team) continue;
                int index = tierByZone[zone] - 1;
                if (index < 0 || index >= teamGoldByTier.Length) continue;
                perZoneGold[zone] += teamGoldByTier[index] * seconds / playersPerTeam;
            }
        }
    }
}
```

```csharp
// Assets/scripts/Telemetry/MatchClock.cs
namespace Overpower.Telemetry
{
    /// <summary>Seconds since the match started, from Photon's server clock: the same on every client, so
    /// the report can line up every player's log. The server clock is an int that wraps, so the subtraction is
    /// unchecked. 0 means "not known yet" (Photon reads 0 right after connecting), which returns -1.</summary>
    public static class MatchClock
    {
        public static double Seconds(int nowMs, int startMs)
        {
            if (nowMs == 0 || startMs == 0) return -1.0;
            return unchecked(nowMs - startMs) / 1000.0;
        }
    }
}
```

- [ ] **Step 4: Recompile clean; tests async.** Expect the previous total + 11, all passing.
- [ ] **Step 5: Commit + push:** `feat(telemetry): pure line builder, income attribution and match clock`.

---

### Task T2: Recording runtime (config, writer, match identity, header, markers)

**Files:**
- Create: `Assets/scripts/Data/TelemetryConfig.cs` + asset
- Create: `Assets/scripts/Telemetry/TelemetryKeys.cs`, `TelemetryWriter.cs`, `TuningSnapshot.cs`, `MatchTelemetry.cs`
- Create: `Assets/scripts/Editor/Telemetry/BuildInfoWriter.cs`
- Modify: `.gitignore` (add `/Assets/Resources/BuildInfo.txt` and its `.meta`)
- Modify: `Assets/scripts/TestRange/TestRangePanel.cs`
- Modify: `Assets/Scenes/Game Scene.unity` (a `MatchTelemetry` component on the `BuildingManager` object)

- [ ] **Step 1: `TelemetryKeys`.** One static class of `const string` for every event name and field key in the
spec's event table (e.g. `public const string Hit = "hit";` and `public const string Attacker = "a";`). The Editor
aggregator uses the same constants. Include:
  - `session`, `sample`, `goldEarned`, `purchase`, `refund`, `shopBlocked`, `shots`, `cast`, `hit`, `status`,
    `death`, `respawn`, `heal`, `overheat`, `ultimateReady`, `ultimateUsed`, `ownership`, `capture`, `bounty`,
    `underAttack`, `overpower`, `join`, `leave`, `masterChanged`, `marker`;
  - their fields.

  Add a `SchemaVersion = 1` constant.

- [ ] **Step 2: `TelemetryConfig`** (`[CreateAssetMenu]`, fields with tooltips):
  - `enabled` (true): "Write a telemetry log for every match this client plays."
  - `sampleIntervalSeconds` (5, Min 1): how often gold, position and loadout are sampled.
  - `recordPositions` (true).
  - `flushIntervalSeconds` (2, Min 0.5).
  - `folderName` ("Telemetry").

  Create the asset at `Assets/Gameplay/Config/TelemetryConfig.asset`.

- [ ] **Step 3: `TelemetryWriter`** (not a MonoBehaviour):
  - `Open(string path)` creates the directories.
  - `Write(string line)` appends to an in-memory `List<string>`.
  - `Flush()` does `File.AppendAllLines` and clears the buffer.
  - `Close()` flushes.
  - Any exception in `Open`/`Flush`: `Debug.LogError` once, then set `Disabled = true`. Every later call does nothing.
  - `LineCount` is a diagnostic property.

- [ ] **Step 4: `MatchTelemetry`** (scene component on the `BuildingManager` GameObject, `MonoBehaviourPunCallbacks`,
`public static MatchTelemetry Instance`):
  - **References:** `TelemetryConfig config`, `TerritoryConfig territoryConfig`, `GameplayConfig gameplayConfig`,
    `ArmorConfig armorConfig`, `WeaponCatalogue weapons`, `AbilityCatalogue abilities` (as serialized fields, each
    with a tooltip).
  - **Match identity:**
    - Room Properties `mId` (string, `Guid.NewGuid().ToString("N")`) and `mStart` (int ServerTimestamp).
    - `OnJoinedRoom` and `OnMasterClientSwitched`: if this client is master and `mId` is absent, write both. Wait for
      `ServerTimestamp != 0`, retrying each frame from a coroutine.
    - Every client reads them from `OnJoinedRoom` / `OnRoomPropertiesUpdate`.
    - `public double Now` → `MatchClock.Seconds(PhotonNetwork.ServerTimestamp, startMs)`.
  - **File:** once `mId` is known and this client has an actor number, open
    `Path.Combine(Application.persistentDataPath, config.folderName, $"{DateTime.Now:yyyy-MM-dd_HHmm}_{mId[..8]}",
    $"{actor}_{Sanitize(nick)}.jsonl")`. Reuse an existing folder whose name ends with `_{mId8}` if one exists, so
    every client on one PC shares one folder.
  - **Line 1, `session`:**
    - schema;
    - `mId`;
    - actor, nick, team (−1 if unknown yet);
    - is-master;
    - commit (Editor: read `.git/HEAD` → ref file, first 10 chars; build: `Resources.Load<TextAsset>("BuildInfo")`);
    - `Application.unityVersion`, `Application.platform`;
    - `Raw("tuning", TuningSnapshot.Json(...))`.
  - **`TuningSnapshot.Json`:** builds one JSON object with `JsonUtility.ToJson` of `territoryConfig`,
    `gameplayConfig`, `armorConfig`, the `OverPower` fields (they're on `GameplayConfig`), and arrays of
    `{id, name, goldCost, json}` for every weapon and ability definition in the catalogues. Wrap values with
    Newtonsoft only in the Editor aggregator; the runtime writes the raw `JsonUtility` strings as JSON values.
  - **API for recorders:** `public void Log(TelemetryLine line)`. Writes `line.End()` if enabled and the file is open;
    otherwise drops it. **Before the file opens:** keep up to 200 lines in a pending list, then flush them after the
    header.
  - **Flush:** every `flushIntervalSeconds` of unscaled time; on `OnLeftRoom`, `OnApplicationQuit` and `OnDestroy`.
  - **Room events:** `OnPlayerEnteredRoom` → `join`; `OnPlayerLeftRoom` → `leave`; `OnMasterClientSwitched` →
    `masterChanged` (actor + team where known).
  - **Markers:** `public void DropMarker(string note)` → a `marker` line with the local actor.
  - **Folder:** `public string CurrentFolder`, used by the F1 button.

- [ ] **Step 5: `BuildInfoWriter`** (`IPreprocessBuildWithReport`, `callbackOrder` 0):
  - Writes the commit hash (read `.git/HEAD` the same way) to `Assets/Resources/BuildInfo.txt` and
    `AssetDatabase.ImportAsset`s it.
  - Add both the file and its `.meta` to `.gitignore`, with a comment line.

- [ ] **Step 6: F1 panel** (`TestRangePanel`, follow its `AddButton` / `AddLabel` pattern):
  - A new button row with "Open telemetry folder" (`Application.OpenURL("file:///" + folder)`, or
    `EditorUtility.RevealInFinder` under `#if UNITY_EDITOR`) and "Drop marker" (`MatchTelemetry.Instance?.DropMarker("")`).
  - A small label: "Telemetry: <lines> lines → <folder>" (or "off").

- [ ] **Step 7: Scene wiring.**
  - Scene not dirty first.
  - Add `MatchTelemetry` to the `BuildingManager` GameObject.
  - Assign the six references.
  - Save, and read back.

- [ ] **Step 8: Verify.**
  - Recompile clean; tests pass.
  - Play mode, single client, join. The folder exists under `persistentDataPath`, and the file's first line is a
    `session` with a non-empty `tuning` that parses as JSON (check with Newtonsoft in an Editor eval).
  - Drop a marker; after 3 s the file has the marker line.
  - Stop play mode. The file is flushed, the last line is intact (valid JSON), and `LineCount` matches the file's line
    count.
  - `git status`: no `BuildInfo.txt`. Run a build only if convenient; otherwise verify `BuildInfoWriter` by calling its
    method directly and then deleting the file.

- [ ] **Step 9: Commit + push:** `feat(telemetry): match identity, log writer, session header and markers`.

---

### Task T3: Combat hooks and `PlayerTelemetry` (owner events)

**Files:**
- Modify: `Assets/scripts/Combat/DamageInfo.cs`, the 9 `new DamageInfo(` sites (`Mine.cs`, `AoeZone.cs`,
  `ElectricFence.cs`, `PlayerStatusEffects.cs`, `DummyTarget.cs`, `ExplodeOnImpact.cs`, `Hitscan.cs`, `FireField.cs`,
  `ProjectileMotor.cs`), `WeaponFiring.cs`, `AbilityRunner.cs`, `PlayerStatusEffects.cs`, `OverPowerBuff.cs`,
  `Assets/Resources/Multiplayer Player.prefab`
- Create: `Assets/scripts/Telemetry/PlayerTelemetry.cs`
- Test: `Assets/Tests/DamageInfoTests.cs` (the new field's default and the constructor order)

- [ ] **Step 1: `DamageInfo.AbilityId`.**
  - Add `public readonly int AbilityId;` (−1 when not from an ability).
  - Add a constructor parameter `int abilityId = -1` **at the end**, so existing call sites compile unchanged.
  - Set it at the ability sites: mine, AoE zone, fence, status/burn (`PlayerStatusEffects` burn ticks carry the
    applying ability id; thread it through `StatusEffectSpec` if it isn't there), and fire field (the rocket's cursor
    leaf: the weapon id stays, ability −1).
  - Read each site; report any site where the ability id isn't reachable, and what you did.
  - Test: default −1 when omitted; the value is kept when passed.

- [ ] **Step 2: Gameplay events** (owner-side raises only; each a one-line `event` + invoke):
  - `WeaponFiring.Fired(int weaponId, int projectileCount)`: raised on the **shooter's own client** when a shot is
    committed. Pellets/burst rounds are counted singly; for burst, raise once per round actually spawned.
  - `AbilityRunner.Cast(AbilitySlot slot, int abilityId)`: after `SendCast` on the caster's client.
  - `PlayerStatusEffects.StatusApplied(StatusKind kind, int sourceActor, int abilityId, float durationOrMagnitude)`:
    on the victim's owner client when applied.
  - `OverPowerBuff.Triggered()` / `Ended(string reason)`, with reason "distance" / "death".

- [ ] **Step 3: `PlayerTelemetry`** (on the player prefab root; owner only; all subscriptions in `Start`,
unsubscriptions in `OnDestroy`). Every event logs through `MatchTelemetry.Instance.Log`.

  **Owner-side events:**
  - **`hit`** from `PlayerHealth.Damaged(result, info)` on the victim's own client:
    - attacker actor and team (via `Teams.TryGetTeam`);
    - weapon, ability, source;
    - raw amount, armor absorbed, health lost, lethal;
    - distance from the attacker's `PlayerNetSync.NetworkPosition` (or their transform) to this player;
    - vulnerability > 0, invulnerable, OverPower active on the victim.
  - **`status`** from `StatusApplied`.
  - **`cast`** from `Cast`: slot, ability id, x, z.
  - **`shots`:** accumulate `Fired` per weapon id (pulls and projectiles); flush a `shots` line per weapon every sample
    interval, and on death/leave.
  - **`death`** from `PlayerHealth.Died(info)`:
    - killer actor and team, weapon, ability;
    - assists (`PlayerCombatCredit`'s ledger `AssistersSince`, same window);
    - x, z;
    - time alive since the last respawn;
    - unspent gold (`GoldWallet.Balance`);
    - loadout ids (weapon, equipment, mobility, ultimate, armor levels from `LoadoutProperties`).
  - **`respawn`** from `PlayerLifecycle.AliveChanged(true)`: x, z, time dead, and whether this was the capital-under-
    attack spawn (expose `PlayerLifecycle.LastRespawnWasUnderAttackSpawn`).
  - **`overheat`** by polling `PlayerOverheat.IsSilenced` transitions: "silenced" / "recovered", with the weapon id.
  - **`ultimateReady`** by polling `UltimateCharge.IsFull` false→true; **`ultimateUsed`** from `Cast` with slot
    Ultimate, with seconds since it became ready.
  - **`overpower`** from `Triggered`/`Ended`: zone distance, health at trigger.

  **`sample`**, every `sampleIntervalSeconds` of unscaled time:
  - balance;
  - x, z (if `recordPositions`), alive;
  - zone id standing in (`BuildingManager.TryGetZoneAt`), team;
  - weapon id, equipment, mobility, ultimate, armor absorb and recharge levels;
  - health, armor, ultimate `Normalised`, overheat `Normalised`;
  - `PhotonNetwork.GetPing()`.

- [ ] **Step 4: Prefab.** `LoadPrefabContents` → `AddComponent<PlayerTelemetry>` → `SaveAsPrefabAsset` →
`UnloadPrefabContents`; read back. `NetworkPrefabObservablesTests` must still pass (`PlayerTelemetry` isn't an
`IPunObservable`).

- [ ] **Step 5: Verify, single client, test range on.**
  1. Shoot a dummy with two weapons (`fire_driver_tpl.cs`, aim override). The file has `shots` lines with the right
     counts per weapon. **A dummy isn't a player**: log `hit` from `DummyTarget` too (same fields, victim actor −1,
     victim name "dummy"), because the test range is where Tudor tunes weapons.
  2. Cast one ability of each slot you have: `cast` lines.
  3. Get killed via `ApplyDamage` with a fake source: a `death` line with a correct loadout, then a `respawn` line.
  4. Force overheat: `overheat` silenced/recovered.
  5. Fill the ultimate: `ultimateReady`.
  6. Samples every 5 s.

  Every line parses (Newtonsoft eval over the file). Report counts per event type.

- [ ] **Step 6: Commit + push** (damage id; events; `PlayerTelemetry` + prefab).

---

### Task T4: Economy and territory hooks

**Files:** modify `GoldWallet.cs`, `LoadoutScreen.cs` (+ `ShopPricing.cs` if the reason helper lives there),
`TestRangePanel.cs`, `BuildingManager.cs`, `ZonePresenceTracker.cs`, `PlayerTelemetry.cs`, `MatchTelemetry.cs`.

- [ ] **Step 1: `GoldWallet`.**
  - Add `public enum GoldSource { Territory, Bounty, Refund, Debug, Other }`.
  - `Add(int amount)` becomes `Add(int amount, GoldSource source = GoldSource.Other)`.
  - Events `Credited(int amount, GoldSource source)` and `Spent(int amount)`, raised on the owner only.
  - Territory income crossing whole gold raises `Credited(n, Territory)`.
  - The bounty credit passes `Bounty`.
  - `LoadoutScreen`'s refunds pass `Refund`.
  - F1 +1000 passes `Debug`.

- [ ] **Step 2: Shop events** on `LoadoutScreen` (owner): `Purchased(category, itemId, price, balanceAfter)`,
`Refunded(category, amount, balanceAfter)`, `PurchaseRefused(itemId, price, PurchaseBlock reason, shortfall)`.
  - Category ∈ `weapon`, `armor`, `equipment`, `mobility`, `ultimate`.
  - Raise each at the existing spend, refund and refuse sites.
  - Include free-loadout purchases with price 0 and a `free:true` field.

- [ ] **Step 3: `PlayerTelemetry` economy.**
  - `purchase`, `refund`, `shopBlocked` lines, with the zone id standing in.
  - **`goldEarned` every sample interval:**
    - `Territory` credits summed;
    - per-zone attribution via `IncomeAttribution.Accumulate` over the same interval, using
      `BuildingManager.Current` owners, `TierByZone()`, `TerritoryConfig` tier rates and `PlayersPerTeam`;
    - write `zones` as an int array of whole gold per zone (rounded; the aggregator uses the sum, and the
      `Territory` total stays authoritative);
    - bounty, refund, debug and other totals.
  - **Check:** over a 60 s run in an owned zone, the per-zone sum is within ±1 gold per interval of the `Territory`
    credits. Report it.
  - **`heal`:** poll health increases while alive and not taking damage that frame; attribute them to the tier of the
    zone standing in; flush per interval (tier → amount).

- [ ] **Step 4: Master territory events** (`MatchTelemetry`, master only):
  - **`ownership`** from `BuildingManager.OwnershipChanged(zone, old, new, snapshot)`: zone, tier, old, new, and the
    snapshot's held-since stamp (the aggregator's dedupe key).
  - **`capture`:** add `BuildingManager.CaptureProgressChanged(int zone, CaptureProgress old, CaptureProgress new)`,
    raised in `ApplyCaptureProgressIfPresent` for each zone whose value changed (every client; `MatchTelemetry` logs
    on master only). Classify:
    - Idle → rate > 0: "started";
    - rate > 0 → rate 0 with progress: "paused";
    - paused → rate > 0: "resumed";
    - any → owned by that team: "completed";
    - Idle → rate < 0: "drainStarted";
    - rate < 0 → neutral: "neutralised";
    - rate < 0 → rate 0: "drainPaused".

    Include team, progress, and the zone's player count (`BuildingCapture` exposes a count).
  - **`bounty`:** when `SetCaptured` pays (log zone, amount, the payer team's hold seconds). Expose the paid amount via
    an event from `BuildingManager.SetCaptured`.
  - **`underAttack`:** add `ZonePresenceTracker.UnderAttackChanged(int zone, bool underAttack)`. Evaluate each zone's
    `IsUnderAttack` once per measure on the master and raise it on change; log it with the zone owner.

- [ ] **Step 5: Verify, single client, free loadout OFF in memory only.**
  1. F1 +1000 twice.
  2. Buy Rocket, reset it, buy armor, get refused in a neutral zone.
  3. Capture a T2: `capture` started/completed, `ownership`.
  4. Stand 60 s in the owned T2: `goldEarned` with the zone split.
  5. Take damage and regen: `heal`.

  Report the event counts and the ±1 gold check. Two-client events (`bounty`, `underAttack`, drains) are verified in
  2.8. Stop play mode, and prove `GameplayConfig.asset` is unchanged.

- [ ] **Step 6: Commit + push.**

---

### Task T5: Report core (parse, aggregate, CSV)

**Files:**
- Create: `Assets/scripts/Editor/Telemetry/TelemetryLog.cs`, `TelemetryAggregator.cs`, `CsvReportWriter.cs`,
  `ReportTables.cs`
- Create: `Assets/Tests/TelemetryAggregatorTests.cs`, and fixtures under `Assets/Tests/TelemetryFixtures/`
  (`match_a/1_Editor.jsonl`, `match_a/2_Player.jsonl`)
- Modify: `Packages/manifest.json` (add `"com.unity.nuget.newtonsoft-json": "3.2.1"` explicitly; it's already in the
  lock file as a dependency), `Assets/Tests/Overpower.Tests.asmdef` (add `"Newtonsoft.Json.dll"` to
  `precompiledReferences`)

- [ ] **Step 1: `TelemetryLog.Load(string folder)`.**
  - Reads every `*.jsonl` and parses each line with `JObject.Parse` inside a try.
  - Collects `TelemetryEvent { string File; string Name; double T; JObject Data; }` and `Sessions` (one per file).
  - Counts unknown event names and malformed lines.
  - Sorts all events by `T`, with `T == -1` first in file order.

- [ ] **Step 2: `ReportTables`:** plain row classes, one per CSV (the spec's table list), plus a `Header`:
  - match length;
  - players per team;
  - commits;
  - `freeLoadoutUsed` / `debugGoldUsed`;
  - markers;
  - per-player covered time range;
  - malformed/unknown counts;
  - the tuning snapshot JSON.

- [ ] **Step 3: `TelemetryAggregator.Build(TelemetryLog log, BalanceTargetsData targets = null) → ReportTables`.**
  - **Dedupe ownership:** key (zone, new owner, held-since), so two masters logging the same change count once.
  - **`gold_timeline`:** from `sample` + cumulative `goldEarned` / `purchase` / `refund`.
  - **`economy_by_minute`:** bucket `goldEarned` by `floor(t / 60)` per team. Zones held per tier from the ownership
    timeline sampled at each minute's midpoint. Gold gap = the richest team's summed balance − this team's, at the
    minute's last samples.
  - **`zone_income`:** seconds held per (zone, team) from the ownership stints (end = next change or match end), and
    gold generated = the sum of the `goldEarned` zone arrays for that team's players.
  - **`ownership`:** stints. **`captures`:** attempts from `capture` started → completed / neutralised / abandoned
    (abandoned = paused and never resumed by match end).
  - **`purchases`** / **`shop_blocked`:** straight rows. **`hits`:** raw rows.
  - **`weapons`** (all 13):
    - time equipped (from samples: the interval × samples with that weapon);
    - pulls and projectiles (from `shots`), hits;
    - accuracy = hits ÷ projectiles;
    - damage (raw), armor vs health split;
    - damage per equipped minute;
    - kills (death events whose weapon matches);
    - mean and median distance.
  - **`abilities`:** casts, damage, kills, status count and summed seconds.
  - **`players`:**
    - kills, deaths, assists;
    - damage dealt (sum of hits where they're the attacker) and taken;
    - gold earned by source, spent;
    - time alive;
    - time in own / enemy / neutral zones (from samples);
    - healing.
  - **`deaths`:** rows.

- [ ] **Step 4: Fixture + tests (write these first).**
  - **Hand-write two small log files** (~40 lines each) for a 3-minute match: 2 players on teams 0 and 1.
    - Include a duplicated `ownership` event (logged by both masters around a master change).
    - Include 3 hits from each side.
    - Include 1 purchase and 1 refund.
    - Include a T2 capture started → completed.
    - Include `goldEarned` intervals with zone arrays.
    - Include one malformed line and one unknown event.
  - **Tests assert hand-computed numbers**, one assertion group per table:
    - ownership stints count once;
    - weapon hits and accuracy;
    - player damage dealt equals the other player's damage taken;
    - zone income sums;
    - economy per minute;
    - the malformed and unknown counts in the header.

  Load the fixture path via `Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/match_a")`.

- [ ] **Step 5: `CsvReportWriter.Write(ReportTables, string folder)`.**
  - Writes 12 CSVs into `folder/csv/`.
  - Invariant culture; RFC-4180 quoting (quote fields containing a comma, quote or newline; double inner quotes).
  - Test: it writes all 12 files for the fixture, with the expected header rows and one escaped name containing a
    comma.

- [ ] **Step 6: Recompile clean; tests pass. Commit + push.**

---

### Task T6: HTML report, balance targets, menu

**Files:**
- Create: `Assets/scripts/Data/BalanceTargets.cs` + `Assets/Gameplay/Config/BalanceTargets.asset`
- Create: `Assets/scripts/Editor/Telemetry/HtmlReportWriter.cs`, `TelemetryMenu.cs`, `ArenaReportRender.cs`

- [ ] **Step 1: `BalanceTargets`** (tooltips name the GDD page):
  - `scenarioIncomePerTeam`: Losing 5, Struggling 15, Average 23, Dominant 33 (gold/s, p.37);
  - `purchaseTargetMinutes`: Primary Upgrade 1 = 6.5, Armor 1 = 10, Ultimate = 13.5, Primary Upgrade 2 = 16.5,
    Armor 2 = 20 (p.37);
  - `targetMatchSeconds` 1350 (p.36).

  The aggregator takes a plain `BalanceTargetsData` copy, so tests don't need the asset.

- [ ] **Step 2: `ArenaReportRender`.**
  - The `ArenaRender` approach (HideAndDontSave orthographic camera, never dirties the scene), framed on the arena
    bounds.
  - Returns a PNG as base64 plus the world→pixel mapping (min x, min z, metres per pixel).
  - Needs `Game Scene` open. If it isn't, the report omits the heatmaps with a note.

- [ ] **Step 3: `HtmlReportWriter.Write(ReportTables, BalanceTargetsData, arenaImage, string folder)`.**
  - One self-contained `report.html`: all table data embedded as `const DATA = {...}` (Newtonsoft serialize) and
    `<script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>`.
  - Plain CSS (system font, readable in light and dark via `prefers-color-scheme`), no other dependencies.
  - Sections exactly as the spec, Part 2 "HTML report" items 1–6:
    - header warnings (free loadout / debug gold used);
    - line charts (gold per player; team income per second stacked by tier, with scenario lines);
    - bars (zone income, weapons);
    - Gantt-like ownership rows (a canvas or div bars);
    - the team×team damage matrix as a table with shading;
    - death and position heatmaps as dots on the arena image, using the mapping;
    - the tables (sortable by clicking a header, ~20 lines of JS);
    - markers with the 30 s of events around them.
  - If Chart.js fails to load (offline), the tables still render and a note says charts need internet.

- [ ] **Step 4: `TelemetryMenu`:**
  - `OverPower/Telemetry/Build Report…`: a folder picker defaulting to the newest folder under
    `persistentDataPath/<TelemetryConfig.folderName>`. It runs `Load` → `Build` → CSV + HTML, then
    `Application.OpenURL` on the HTML and logs the paths.
  - `OverPower/Telemetry/Open Telemetry Folder`.
  - `OverPower/Telemetry/Build Report From Fixture` (for testing): builds into a temp folder from the test fixture.

- [ ] **Step 5: Verify.**
  - Build the fixture report.
  - Report the absolute path of `report.html`. The controller opens it in its own browser pane and looks, so you
    don't need a screenshot.
  - Then do a real single-client run: play 3–4 minutes doing T4's step 5 plus T3's step 5 actions and a marker.
    Build the report from that folder. Every CSV is non-empty where the run produced data. The report header shows
    "debug gold used". Spot-check 3 numbers against the log.
  - `git status` clean after the commit (no report output inside the repo).

- [ ] **Step 6: Commit + push.** Append `## Telemetry (2026-09-16)` [C] lines to the assumptions file (the event set,
  the sample interval, the dummy hits, enemies not in the logs of other clients, the CDN dependency).

---

## Self-review against the spec

| Spec item | Task |
|---|---|
| Local JSONL per client, logged once by the owner, no RPCs | T2 (writer, identity), T3/T4 (owner-only hooks) |
| Match id/start in Room Properties, 0-guard, wrap-safe | T1 `MatchClock`, T2 step 4 |
| Files under persistentDataPath/Telemetry/<date>_<mId8>/<actor>_<nick>.jsonl, shared folder on one PC | T2 step 4 |
| Session line with tuning snapshot + commit | T2 steps 4–5 |
| TelemetryConfig settings | T2 step 2 |
| F1 Open folder / Drop marker / status | T2 step 6 |
| Events: sample, goldEarned, purchase/refund, shopBlocked, shots, cast, hit (+AbilityId), status, death (+assists), respawn, heal, overheat, ultimateReady/Used, ownership, capture, bounty, overpower, join/leave/masterChanged, marker | T2–T4 (phase/elimination → 2.7, noted) |
| Income attribution pure + sum check | T1, T4 step 3 |
| Report: merge, 12 CSVs, HTML sections, arena heatmaps, BalanceTargets reference lines | T5, T6 |
| Error handling: IO disables, malformed/unknown counted, partial logs, t = −1 | T2 step 3, T5 step 1, T5 header |
| Tests: serialization, attribution, aggregator fixture with a dedupe, CSV escaping | T1, T5 |
| One-client end-to-end; two-client in 2.8 | T3–T6 verify steps; 2.8 |
