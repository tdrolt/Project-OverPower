using System.IO;
using NUnit.Framework;
using UnityEditor;
using Overpower.Data;

namespace Overpower.Tests
{
    /// <summary>Vent's two new GameplayConfig numbers, Tudor's own (ventDelay 2.0, ventWindow 0.8).
    /// Checks both the live in-memory asset AND the raw YAML on disk - the C# field initializer
    /// alone would already make the in-memory object read 2.0/0.8 even if the value were never
    /// actually hand-written into Tudor's own asset file (CODING-STANDARDS.md's "Assuming a new
    /// serialized field is saved" mistake), so the on-disk check is the one that actually proves
    /// the asset itself carries the new lines.</summary>
    public class GameplayConfigVentTests
    {
        private const string AssetPath = "Assets/Gameplay/Config/GameplayConfig.asset";

        [Test]
        public void TheLiveAssetHasTudorsVentNumbers()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameplayConfig>(AssetPath);
            Assert.IsNotNull(config, AssetPath);
            Assert.AreEqual(2.0f, config.VentDelay, 0.001f);
            Assert.AreEqual(0.8f, config.VentWindow, 0.001f);
        }

        [Test]
        public void TheAssetFileOnDiskHasBothNewLines()
        {
            string yaml = File.ReadAllText(AssetPath);
            StringAssert.Contains("ventDelay: 2", yaml);
            StringAssert.Contains("ventWindow: 0.8", yaml);
        }
    }
}
