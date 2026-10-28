using NUnit.Framework;
using Overpower.Match;
using Overpower.Net;
using Overpower.Web;

namespace Overpower.Tests
{
    /// <summary>
    /// In the browser every tab must be its own player: Photon refuses a second connection with the same user id in one room, and
    /// every tab of a site shares the browser's file storage. So on the web the id and the saved last match live in the tab
    /// (sessionStorage, kept across a reload of that tab for the rejoin); everywhere else they stay in the files next to the game.
    /// </summary>
    public class PlayerIdBrowserTabTests
    {
        [Test]
        public void TheBrowserKeepsTheIdPerTab() => Assert.IsTrue(PlayerIdRule.KeepsIdPerBrowserTab(webPlayer: true));

        [Test]
        public void ADesktopBuildKeepsTheIdInItsFolder() => Assert.IsFalse(PlayerIdRule.KeepsIdPerBrowserTab(webPlayer: false));

        [TestCase("get_UserId")]
        [TestCase(nameof(PlayerIdentity.LoadLastMatch))]
        [TestCase(nameof(PlayerIdentity.SaveLastMatch))]
        [TestCase(nameof(PlayerIdentity.ClearLastMatch))]
        public void TheIdentityAsksTheRuleWhereToKeepIt(string method)
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerIdentity), method, typeof(PlayerIdRule).GetMethod(nameof(PlayerIdRule.KeepsIdPerBrowserTab))),
                method + " must branch on KeepsIdPerBrowserTab");
        }

        [Test]
        public void OutsideTheBrowserTheTabStoreHoldsNothing()
        {
            BrowserTabStore.Set("overpower-test-key", "x");
            Assert.IsNull(BrowserTabStore.Get("overpower-test-key"), "the Editor and desktop builds have no tab storage");
            BrowserTabStore.Remove("overpower-test-key");
        }
    }
}
