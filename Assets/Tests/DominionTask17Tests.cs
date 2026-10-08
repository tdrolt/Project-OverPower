using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.UI;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 17 (Tudor A51-A53): the respawn shield drops the moment its owner leaves their own spawn area, the score bars in the
    /// bottom-right corner fill by the leader's points, and Conquest's centre scan wave is yellow while the enemy dots it leaves keep their colour.
    /// Rules are pure; the wiring tests read the components' method bodies (IlWiring), so putting an inline copy back fails them.</summary>
    public class DominionTask17Tests
    {
        private static MethodInfo Method(System.Type type, string name) => type.GetMethod(name);

        // ---------------------------------------------------------------- A51: the shield drops on leaving the spawn

        [Test] public void AShieldedPlayerInsideTheirSpawnKeepsTheShield() =>
            Assert.IsFalse(RespawnShieldRules.DropsOnLeavingSpawn(shieldUp: true, inOwnSpawn: true));

        [Test] public void AShieldedPlayerOutsideTheirSpawnLosesTheShield() =>
            Assert.IsTrue(RespawnShieldRules.DropsOnLeavingSpawn(shieldUp: true, inOwnSpawn: false));

        [Test] public void WithNoShieldThereIsNothingToDropWhereverThePlayerStands()
        {
            Assert.IsFalse(RespawnShieldRules.DropsOnLeavingSpawn(shieldUp: false, inOwnSpawn: false));
            Assert.IsFalse(RespawnShieldRules.DropsOnLeavingSpawn(shieldUp: false, inOwnSpawn: true));
        }

        [Test] public void AnEnemyTeamsSpawnAreaDoesNotCountAsYourOwnSpawn()
        {
            var go = new GameObject("test spawn area");
            try
            {
                var area = go.AddComponent<SpawnHealArea>();
                area.Configure(1, SpawnHealArea.AreaShape.Circle, new Vector2(10f, 10f), 5f);
                SpawnHealArea.Register(area); // edit-mode tests do not run OnEnable
                Assert.IsTrue(SpawnHealArea.Contains(1, Vector3.zero), "team 1 stands in its own area");
                Assert.IsFalse(SpawnHealArea.Contains(0, Vector3.zero), "team 0 standing in team 1's area is not in its own spawn");
                Assert.IsFalse(DominionHealRules.InOwnSpawn(inRegisteredArea: false, teamCount: 3, zoneIsOwnCapital: false), "an enemy's capital circle is not yours either");
                Assert.IsTrue(RespawnShieldRules.DropsOnLeavingSpawn(true, SpawnHealArea.Contains(0, Vector3.zero)), "so the shield of team 0 drops there");
            }
            finally
            {
                SpawnHealArea.Unregister(go.GetComponent<SpawnHealArea>());
                Object.DestroyImmediate(go);
            }
        }

        private sealed class WatchRig
        {
            public bool up = true, inside = true;
            public int asked, drops;
            public readonly ShieldSpawnWatch watch;
            public WatchRig()
            {
                watch = new ShieldSpawnWatch(() => up, () => { asked++; return inside; }, () => { drops++; up = false; });
            }
        }

        [Test] public void TheWatchKeepsAShieldWhileTheOwnerStaysInsideAndDropsItOnTheFirstFrameOutside()
        {
            var rig = new WatchRig();
            Assert.IsFalse(rig.watch.Tick());
            Assert.AreEqual(0, rig.drops);
            rig.inside = false;
            Assert.IsTrue(rig.watch.Tick(), "the first frame outside");
            Assert.AreEqual(1, rig.drops);
        }

        [Test] public void AShieldThatDroppedStaysDroppedWhenTheOwnerWalksBackIn()
        {
            var rig = new WatchRig { inside = false };
            rig.watch.Tick();
            rig.inside = true;
            Assert.IsFalse(rig.watch.Tick());
            rig.inside = false;
            Assert.IsFalse(rig.watch.Tick(), "and leaving again drops nothing more");
            Assert.AreEqual(1, rig.drops);
        }

        [Test] public void TheWatchDoesNotEvenLookForTheSpawnWhileNoShieldIsUp()
        {
            var rig = new WatchRig { up = false, inside = false };
            Assert.IsFalse(rig.watch.Tick());
            Assert.AreEqual(0, rig.asked, "no shield, no per-frame area search for every player");
            Assert.AreEqual(0, rig.drops);
        }

        [Test] public void TheShieldComponentDropsItThroughTheWatchOverTheOwnSpawnRule()
        {
            System.Type shield = typeof(RespawnShield);
            Assert.IsTrue(IlWiring.Uses(shield, "Awake", typeof(ShieldSpawnWatch).GetConstructors()[0]), "the component builds the watch");
            Assert.IsTrue(IlWiring.Uses(shield, "Update", Method(typeof(ShieldSpawnWatch), nameof(ShieldSpawnWatch.Tick))), "and ticks it every frame");
            Assert.IsTrue(IlWiring.Uses(shield, "Awake", Method(typeof(SpawnHealArea), nameof(SpawnHealArea.InOwnSpawn))), "the place asked is the spawn heal's own area");
            Assert.IsTrue(IlWiring.Uses(shield, "Awake", Method(typeof(RespawnShield), nameof(RespawnShield.ClearShield))), "dropping it is the existing clear path");
            Assert.IsTrue(IlWiring.Uses(typeof(ShieldSpawnWatch), "Tick", Method(typeof(RespawnShieldRules), nameof(RespawnShieldRules.DropsOnLeavingSpawn))), "the watch asks the tested rule");
        }

        // ---------------------------------------------------------------- A52: the score bars

        [Test] public void TheLeadingTeamsBarIsFullAndTheOthersFillInProportionToTheLeader()
        {
            float[] fills = DominionScoreBarRules.Fills(new[] { 0, 1 }, new[] { 300, 100, 0 });
            Assert.AreEqual(1f, fills[0], 1e-5f);
            Assert.AreEqual(1f / 3f, fills[1], 1e-5f);
        }

        [Test] public void ThreeTeamsFillByTheirShareOfTheLeader()
        {
            float[] fills = DominionScoreBarRules.Fills(new[] { 0, 1, 2 }, new[] { 20, 80, 40 });
            Assert.AreEqual(0.25f, fills[0], 1e-5f);
            Assert.AreEqual(1f, fills[1], 1e-5f);
            Assert.AreEqual(0.5f, fills[2], 1e-5f);
        }

        [Test] public void WhenEveryoneIsOnZeroEveryBarIsEmpty()
        {
            float[] fills = DominionScoreBarRules.Fills(new[] { 0, 1, 2 }, new[] { 0, 0, 0 });
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, fills);
        }

        [Test] public void TeamsTiedForTheLeadAreBothFull()
        {
            float[] fills = DominionScoreBarRules.Fills(new[] { 0, 1, 2 }, new[] { 50, 50, 10 });
            Assert.AreEqual(1f, fills[0], 1e-5f);
            Assert.AreEqual(1f, fills[1], 1e-5f);
            Assert.AreEqual(0.2f, fills[2], 1e-5f);
        }

        [Test] public void OnlyTheTeamsOfTheMatchCountForTheLeader()
        {
            // a 2v2 room keeps team 2's slot in the array; whatever sits there must not become the leader
            float[] fills = DominionScoreBarRules.Fills(new[] { 0, 1 }, new[] { 10, 20, 999 });
            Assert.AreEqual(2, fills.Length);
            Assert.AreEqual(0.5f, fills[0], 1e-5f);
            Assert.AreEqual(1f, fills[1], 1e-5f);
            Assert.AreEqual(20, DominionScoreBarRules.Leader(new[] { 0, 1 }, new[] { 10, 20, 999 }));
        }

        [Test] public void AMissingPointsEntryReadsAsZeroAndNeverThrows()
        {
            Assert.DoesNotThrow(() => DominionScoreBarRules.Fills(new[] { 0, 1, 2 }, new[] { 5 }));
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f }, DominionScoreBarRules.Fills(new[] { 0, 1, 2 }, new[] { 5 }));
            CollectionAssert.AreEqual(new[] { 0f, 0f }, DominionScoreBarRules.Fills(new[] { 0, 1 }, null));
        }

        [Test] public void FillNeverLeavesZeroToOne()
        {
            Assert.AreEqual(0f, DominionScoreBarRules.Fill(-5, 10));
            Assert.AreEqual(1f, DominionScoreBarRules.Fill(30, 10), "points above the leader cannot happen, but the bar still stops at full");
            Assert.AreEqual(0f, DominionScoreBarRules.Fill(0, 0));
        }

        [Test] public void TheBarsShowDuringARoundAndOvertimeOnlyAndAreHiddenInTheBreak()
        {
            Assert.IsTrue(DominionScoreBarRules.ShownIn(DominionStage.Round));
            Assert.IsTrue(DominionScoreBarRules.ShownIn(DominionStage.Overtime));
            Assert.IsFalse(DominionScoreBarRules.ShownIn(DominionStage.Break));
            Assert.IsFalse(DominionScoreBarRules.ShownIn(DominionStage.SuddenDeath));
            Assert.IsFalse(DominionScoreBarRules.ShownIn(DominionStage.Over));
            Assert.IsFalse(DominionScoreBarRules.ShownIn(DominionStage.None));
        }

        [Test] public void TheBarsSitAboveTheGoldReadoutAndTheLoadoutButton()
        {
            // button margin 24, button 48 high, gap 8, body text 24 (two lines + 10), scale 0.8 -> the readout's top is (24+48+8+58) * 0.8 above the corner
            Assert.AreEqual(110.4f, DominionScoreBarRules.GoldReadoutTop(24f, 48f, 8f, 24f, 0.8f), 1e-3f);
            Assert.AreEqual(138f, DominionScoreBarRules.GoldReadoutTop(24f, 48f, 8f, 24f, 1f), 1e-3f, "unscaled");
        }

        [Test] public void TheHudHostShowsTheBarsOnlyForTheTestedStagesAndHandsThemThePoints()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "Update", Method(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.ShownIn))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "Update", Method(typeof(ScoreBars), nameof(ScoreBars.Refresh))));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "HideBars", Method(typeof(ScoreBars), nameof(ScoreBars.SetVisible))), "the break, the result and a left room hide them");
        }

        [Test] public void TheBarsFillThroughTheTestedRuleAndSitWhereTheRuleSays()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Refresh", Method(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.Fills))));
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Build", Method(typeof(DominionScoreBarRules), nameof(DominionScoreBarRules.GoldReadoutTop))));
        }

        // ---------------------------------------------------------------- A53: the scan wave is yellow, the dots keep their colour

        private static VisionConfig RealVision() => UnityEditor.AssetDatabase.LoadAssetAtPath<VisionConfig>("Assets/Gameplay/Config/VisionConfig.asset");

        [Test] public void TheScanWaveReadsYellowOnTheRealConfig()
        {
            VisionConfig vision = RealVision();
            Assert.IsNotNull(vision);
            Color.RGBToHSV(vision.ScanWaveColour, out float hue, out float saturation, out float value);
            Assert.That(hue, Is.InRange(0.10f, 0.20f), "a yellow hue (36 to 72 degrees), not the old red");
            Assert.That(saturation, Is.GreaterThan(0.5f));
            Assert.That(value, Is.GreaterThan(0.6f));
        }

        [Test] public void TheWaveAndTheMinimapDotsReadSeparateColourFields()
        {
            VisionConfig vision = ScriptableObject.CreateInstance<VisionConfig>();
            try
            {
                var so = new UnityEditor.SerializedObject(vision);
                Color dots = vision.MinimapEnemyColour;
                so.FindProperty("scanWaveColour").colorValue = Color.blue;
                so.ApplyModifiedProperties();
                Assert.AreEqual(Color.blue, vision.ScanWaveColour);
                Assert.AreEqual(dots, vision.MinimapEnemyColour, "recolouring the wave leaves the dots alone");
                so.FindProperty("minimapEnemyColour").colorValue = Color.green;
                so.ApplyModifiedProperties();
                Assert.AreEqual(Color.blue, vision.ScanWaveColour, "and the other way round");
            }
            finally { Object.DestroyImmediate(vision); }
        }

        [Test] public void TheRealConfigsDotsAreNotTheWavesColour()
        {
            VisionConfig vision = RealVision();
            Assert.AreNotEqual(vision.ScanWaveColour, vision.MinimapEnemyColour, "the wave is yellow, the enemy dots it leaves keep their own colour");
        }

        [Test] public void TheWorldWaveUsesTheWaveColourAndTheMinimapDotsUseTheDotColour()
        {
            MethodInfo wave = typeof(VisionConfig).GetProperty(nameof(VisionConfig.ScanWaveColour)).GetGetMethod();
            MethodInfo dot = typeof(VisionConfig).GetProperty(nameof(VisionConfig.MinimapEnemyColour)).GetGetMethod();
            Assert.IsTrue(IlWiring.Uses(typeof(CentreScanWaveView), wave), "the world ring");
            Assert.IsFalse(IlWiring.Uses(typeof(CentreScanWaveView), dot), "and never the dots' colour");
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "UpdateScan", wave), "the minimap ring");
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "UpdateScan", dot), "the minimap dots");
        }
    }
}
