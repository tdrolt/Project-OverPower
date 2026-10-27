using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The width the ability columns and the armour block share on the Abilities &amp; Armor page.</summary>
    public class ShopPageColumnsTests
    {
        [Test]
        public void AColumnIsTwoCardsAndTheSpacingBetweenThem()
        {
            Assert.AreEqual(230f, ShopPageColumns.ColumnWidth(100f, 30f), 0.001f);
        }

        [Test]
        public void TheColumnsAddUpWithOneGapBetweenEachPair()
        {
            Assert.AreEqual(3f * 230f + 2f * 50f, ShopPageColumns.Width(3, 100f, 30f, 50f), 0.001f);
            Assert.AreEqual(230f, ShopPageColumns.Width(1, 100f, 30f, 50f), 0.001f, "one column has no gap");
        }

        [Test]
        public void NoColumnsTakeNoWidth()
        {
            Assert.AreEqual(0f, ShopPageColumns.Width(0, 100f, 30f, 50f), 0.001f);
            Assert.AreEqual(0f, ShopPageColumns.Width(-2, 100f, 30f, 50f), 0.001f);
        }
    }
}
