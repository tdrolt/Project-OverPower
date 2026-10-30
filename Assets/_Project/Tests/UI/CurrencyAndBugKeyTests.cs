using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 30 Oct: the currency is called credits wherever a player reads it (designer tooltips and code names keep "gold"),
    /// and the bug mark is plain L instead of Ctrl+B.
    /// </summary>
    public class CurrencyAndBugKeyTests
    {
        private static readonly Regex Gold = new Regex("gold", RegexOptions.IgnoreCase);

        // The text a player reads in a YAML asset: every value, and the wrapped lines under it; never a field name.
        private static string[] ValuesOf(string path) =>
            File.ReadAllLines(path)
                .Select(line => Regex.Match(line, @"^\s+(?:- )?(?:[A-Za-z_]+: )?(.*)$").Groups[1].Value)
                .ToArray();

        [TestCase("Assets/_Project/Design/Settings/UiTheme.asset")]
        [TestCase("Assets/_Project/Design/Settings/HowToPlayPages.asset")]
        [TestCase("Assets/_Project/Design/Modes/Conquest 3v3.asset")]
        [TestCase("Assets/_Project/Design/Modes/Conquest 3v3v3.asset")]
        [TestCase("Assets/_Project/Design/Modes/Dominion 2v2.asset")]
        [TestCase("Assets/_Project/Design/Modes/Dominion 3v3v3.asset")]
        public void NoPlayerTextSaysGold(string path)
        {
            string[] hits = ValuesOf(path).Where(v => Gold.IsMatch(v)).ToArray();
            Assert.IsEmpty(hits, string.Join(" | ", hits));
        }

        [Test]
        public void TheThemesOwnDefaultsSayCredits()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                foreach (FieldInfo field in typeof(UiTheme).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType == typeof(string))
                        Assert.IsFalse(Gold.IsMatch((string)field.GetValue(theme) ?? ""), field.Name);
                    else if (field.FieldType == typeof(string[]))
                        foreach (string text in (string[])field.GetValue(theme) ?? new string[0])
                            Assert.IsFalse(Gold.IsMatch(text ?? ""), field.Name);
                }
            }
            finally { Object.DestroyImmediate(theme); }
        }

        [Test]
        public void TheWalletSaysCredits() => Assert.AreEqual("Credits 1200", ShopPricing.GoldLabel(1200));

        [Test]
        public void TheBugMarkIsPlainL()
        {
            var config = AssetDatabase.LoadAssetAtPath<TelemetryConfig>("Assets/_Project/Design/Settings/TelemetryConfig.asset");
            Assert.AreEqual(UnityEngine.InputSystem.Key.L, config.BugMarkKey);
            Assert.IsFalse(config.BugMarkNeedsCtrl);
        }
    }
}
