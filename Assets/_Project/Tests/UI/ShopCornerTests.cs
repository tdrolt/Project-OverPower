using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The bottom-right shop block: its panel is as tall as what is on it in the current mode, and LoadoutScreen sizes it with that rule.</summary>
    public class ShopCornerTests
    {
        [Test]
        public void WithGoldThePanelHoldsTheButtonTheGapTheReadoutAndThePaddingTwice()
        {
            Assert.AreEqual(48f + 8f + 58f + 2f * 10f, ShopCornerRules.PanelHeight(true, 48f, 8f, 58f, 10f), 1e-4f);
        }

        [Test]
        public void WithoutGoldThePanelHoldsOnlyTheButtonAndThePaddingTwice()
        {
            Assert.AreEqual(48f + 2f * 10f, ShopCornerRules.PanelHeight(false, 48f, 8f, 58f, 10f), 1e-4f);
        }

        [Test]
        public void TheShopScreenSizesThePanelWithTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshShopCornerPanel", typeof(ShopCornerRules).GetMethod(nameof(ShopCornerRules.PanelHeight))));
        }

        [Test]
        public void TheShopScreenRefreshesThePanelEveryFrameItRuns()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "Update", typeof(LoadoutScreen).GetMethod("RefreshShopCornerPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)));
        }
    }
}
