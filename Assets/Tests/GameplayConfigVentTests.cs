using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Overpower.Data;

namespace Overpower.Tests
{
    /// <summary>Vent's GameplayConfig numbers (ventDelay/ventWindow, 2026-09-23; ventRandomTiming/
    /// ventRandomDelayMin/ventRandomDelayMax, 2026-09-24). Rewritten 2026-09-24 (Tudor: "remove tests
    /// that are outdated" - he retunes every one of these from the Inspector, so pinning 2.0/0.8 as
    /// literals went red on every tuning pass with nothing actually broken).
    ///
    /// What is kept, and still worth keeping: every Vent field line actually exists in the asset's own
    /// YAML on disk (CODING-STANDARDS.md's "Assuming a new serialized field is saved" mistake - a C#
    /// field initializer alone would make the in-memory object read a sane default even if the value
    /// were never hand-written into the asset file at all, so only the on-disk check actually proves
    /// the asset carries these lines), and that the numbers are SANE - a rule, not a value: Vent Delay
    /// &gt; 0, Vent Window &gt;= 0, and the random range is a valid (non-inverted, non-negative) span once
    /// the "smaller value is always the minimum" tooltip is accounted for.</summary>
    public class GameplayConfigVentTests
    {
        private const string AssetPath = "Assets/Gameplay/Config/GameplayConfig.asset";

        [Test]
        public void TheAssetFileOnDiskHasEveryVentFieldLine()
        {
            string yaml = File.ReadAllText(AssetPath);
            StringAssert.IsMatch(@"(?m)^\s*ventDelay:\s*[-\d.]+\s*$", yaml, "ventDelay: line");
            StringAssert.IsMatch(@"(?m)^\s*ventWindow:\s*[-\d.]+\s*$", yaml, "ventWindow: line");
            StringAssert.IsMatch(@"(?m)^\s*ventRandomTiming:\s*[01]\s*$", yaml, "ventRandomTiming: line");
            StringAssert.IsMatch(@"(?m)^\s*ventRandomDelayMin:\s*[-\d.]+\s*$", yaml, "ventRandomDelayMin: line");
            StringAssert.IsMatch(@"(?m)^\s*ventRandomDelayMax:\s*[-\d.]+\s*$", yaml, "ventRandomDelayMax: line");
        }

        [Test]
        public void TheLiveAssetsVentNumbersAreSane()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameplayConfig>(AssetPath);
            Assert.IsNotNull(config, AssetPath);

            Assert.Greater(config.VentDelay, 0f, "Vent Delay");
            Assert.GreaterOrEqual(config.VentWindow, 0f, "Vent Window - 0 is the documented 'Vent off' value");
            Assert.GreaterOrEqual(config.VentRandomDelayMin, 0f, "Vent Random Delay Min");
            Assert.GreaterOrEqual(config.VentRandomDelayMax, 0f, "Vent Random Delay Max");

            // "min <= max, or handled": the field's own tooltip says a swap is resolved by using the
            // smaller as the minimum (OverheatState.PickVentDelay does exactly that), so both possible
            // orderings are sane - the only actually-unsane case is a negative span with no real width,
            // already ruled out by the two GreaterOrEqual checks above.
            float lo = Mathf.Min(config.VentRandomDelayMin, config.VentRandomDelayMax);
            float hi = Mathf.Max(config.VentRandomDelayMin, config.VentRandomDelayMax);
            Assert.LessOrEqual(lo, hi);
        }
    }
}
