using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The HUD's gold readout is the one string a player reads every few seconds, and the one place a
    /// machine's own culture could quietly turn "+7.7/s" into "+7,7/s" - which reads as 77, not 7.7. These run the
    /// formatter under a comma-decimal culture on purpose.</summary>
    public class GoldHudLabelTests
    {
        [Test]
        public void TheBalanceIsOnTheFirstLineAndTheIncomeOnTheSecond()
        {
            string s = ShopPricing.GoldHudLabel(1234, 7.7, 75f);
            string[] lines = s.Split('\n');
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual("Gold 1234", lines[0]);
            Assert.AreEqual("<size=75%>+7.7/s</size>", lines[1]);
        }

        [Test]
        public void TheIncomeKeepsADecimalPointUnderACommaDecimalCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                StringAssert.Contains("+7.7/s", ShopPricing.GoldHudLabel(1234, 7.7, 75f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void ZeroIncomeStillShowsOneDecimal()
        {
            StringAssert.Contains("+0.0/s", ShopPricing.GoldHudLabel(0, 0.0, 75f));
        }

        [Test]
        public void TheSizePercentIsClampedToSomethingTmpWillAccept()
        {
            StringAssert.Contains("<size=1%>", ShopPricing.GoldHudLabel(0, 0.0, 0f));
            StringAssert.Contains("<size=100%>", ShopPricing.GoldHudLabel(0, 0.0, 400f));
        }
    }
}
