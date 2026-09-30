using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Playtest extras P6 (2026-09-26): EscapeOwnershipRule - see its own class comment for
    /// why "this frame OR last frame" is the rule, not just "this frame".</summary>
    public class EscapeOwnershipRuleTests
    {
        [Test]
        public void NothingOpenAnywhereBelongsToNobody()
        {
            Assert.IsFalse(EscapeOwnershipRule.BelongsToShopOrChat(false, false, false, false));
        }

        [Test]
        public void ShopOpenThisFrameBelongsToTheShop()
        {
            Assert.IsTrue(EscapeOwnershipRule.BelongsToShopOrChat(true, false, false, false));
        }

        [Test]
        public void ChatOpenThisFrameBelongsToChat()
        {
            Assert.IsTrue(EscapeOwnershipRule.BelongsToShopOrChat(false, false, true, false));
        }

        [Test]
        public void ShopClosedItselfLastFrameStillBelongsToTheShop()
        {
            // LoadoutScreen's own Update() ran first this frame, saw Escape, and already closed
            // itself - by the time QuitConfirmPanel looks, IsOpen already reads false THIS frame.
            Assert.IsTrue(EscapeOwnershipRule.BelongsToShopOrChat(false, true, false, false));
        }

        [Test]
        public void ChatClosedItselfLastFrameStillBelongsToChat()
        {
            Assert.IsTrue(EscapeOwnershipRule.BelongsToShopOrChat(false, false, false, true));
        }

        [Test]
        public void NeitherOpenNowNorLastFrameBelongsToTheGame()
        {
            Assert.IsFalse(EscapeOwnershipRule.BelongsToShopOrChat(false, false, false, false));
        }
    }
}
