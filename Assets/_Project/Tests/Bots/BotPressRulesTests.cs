using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>A press its ability refused (a blocked Cover spot, nowhere to land a Blink) is not tried again every think.</summary>
    public class BotPressRulesTests
    {
        [Test]
        public void ARefusedPressWaitsBeforeTheNextTry()
        {
            Assert.IsFalse(BotPressRules.MayPressAgain(0.4f, 1.5f));
            Assert.IsTrue(BotPressRules.MayPressAgain(1.5f, 1.5f));
            Assert.IsTrue(BotPressRules.MayPressAgain(9f, 1.5f));
        }
    }
}
