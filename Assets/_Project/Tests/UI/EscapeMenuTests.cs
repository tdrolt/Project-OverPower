using System.Reflection;
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 31 Oct: Escape in a match opens a menu - "MENU", a Sound section with the master volume, a line, then Leave match (red) and
    /// Close. Leave match does not leave: it opens a second card, "Leave the match? You can rejoin from the list while the match is
    /// running.", with Yes, leave and Stay.
    /// </summary>
    public class EscapeMenuTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static MethodInfo Panel(string name) => typeof(QuitConfirmPanel).GetMethod(name, Any);

        [Test]
        public void LeaveMatchInTheMenuOpensTheSecondQuestion() =>
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "OnYesClicked", Panel("ShowLeaveConfirm")));

        [Test]
        public void LeaveMatchInTheMenuDoesNotLeaveByItself() =>
            Assert.IsFalse(IlWiring.Uses(typeof(QuitConfirmPanel), "OnYesClicked", Panel("LeaveMatch")));

        [Test]
        public void OnlyTheSecondYesLeaves() =>
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "OnConfirmLeaveClicked", Panel("LeaveMatch")));

        [Test]
        public void StayGoesBackToTheGame() =>
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "OnStayClicked", Panel("Close")));

        [Test]
        public void TheMenuInAMatchHasItsOwnTitleAndARedLeaveButton()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "Open", typeof(UiTheme).GetField(nameof(UiTheme.escapeMenuMatchTitleText))), "title");
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "Open", typeof(UiTheme).GetField(nameof(UiTheme.escapeMenuLeaveButtonColor))), "red");
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "Open", typeof(UiTheme).GetField(nameof(UiTheme.escapeMenuLeaveMatchText))), "label");
        }

        [Test]
        public void TheNewWordsAndColourAreInTheThemeWithATooltip()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/UiTheme.asset").Replace("\r\n", "\n");
            foreach (string name in new[] { "escapeMenuMatchTitleText", "escapeMenuSoundHeaderText", "escapeMenuLeaveMatchText", "escapeMenuLeaveButtonColor",
                                            "leaveMatchRejoinNoteText", "leaveMatchYesText", "leaveMatchStayText" })
            {
                FieldInfo field = typeof(UiTheme).GetField(name);
                Assert.NotNull(field, name);
                var tip = (UnityEngine.TooltipAttribute)System.Attribute.GetCustomAttribute(field, typeof(UnityEngine.TooltipAttribute));
                Assert.NotNull(tip, name + " has no tooltip");
                StringAssert.Contains("\n  " + name + ":", yaml, name + " is not in UiTheme.asset");
            }
        }
    }
}
