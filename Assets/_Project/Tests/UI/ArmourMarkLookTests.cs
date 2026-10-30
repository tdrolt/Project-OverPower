using NUnit.Framework;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 30 Oct: the armour-upgrade marks on the shield bar should be easier to notice, in yellow, but not the gold of
    /// Invulnerability (the immune bar), so the two never read as the same thing.
    /// </summary>
    public class ArmourMarkLookTests
    {
        private static UiTheme Theme() => AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Project/Design/Settings/UiTheme.asset");

        [Test]
        public void TheMarksAreYellow()
        {
            Color mark = Theme().nameplateArmourMarkColor;
            Assert.Greater(mark.r, 0.9f);
            Assert.Greater(mark.g, 0.9f);
            Assert.Less(mark.b, 0.6f);
        }

        [Test]
        public void TheYellowIsNotInvulnerabilitysGold()
        {
            Color mark = Theme().nameplateArmourMarkColor, gold = Theme().immuneBarColor;
            Assert.Greater(Mathf.Abs(mark.g - gold.g) + Mathf.Abs(mark.b - gold.b), 0.3f, $"mark {mark} too close to gold {gold}");
        }

        [Test]
        public void TheMarksAreBiggerButStayInsideTheBar()
        {
            UiTheme theme = Theme();
            Assert.GreaterOrEqual(theme.nameplateArmourMarkSize, 2.2f, "bigger than the 29 Oct size (2)");
            Assert.LessOrEqual(theme.nameplateArmourMarkSize, theme.nameplateBarSize.y, "no taller than the bar it sits on");
            Assert.GreaterOrEqual(theme.nameplateArmourSquareLine, 0.3f, "thicker than the 29 Oct line (0.22)");
            Assert.LessOrEqual(theme.nameplateArmourChevronWidth, theme.nameplateArmourMarkSize - 2f * theme.nameplateArmourSquareLine,
                "a \"v\" still fits inside a square");
        }
    }
}
