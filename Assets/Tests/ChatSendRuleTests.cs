using NUnit.Framework;
using Overpower.Chat;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1 Part 0: a line that could not be sent stays in the box, shows a hint and is not logged (so a retry is logged once).</summary>
    public class ChatSendRuleTests
    {
        [Test]
        public void ALineThatWentOutIsSentClearedAndLogged()
        {
            ChatSendOutcome o = ChatSendRule.Outcome("hello", canPublish: true, published: true);
            Assert.AreEqual(ChatSendOutcome.Sent, o);
            Assert.IsTrue(ChatSendRule.ClearsTheBox(o));
            Assert.IsTrue(ChatSendRule.IsLogged(o));
            Assert.IsFalse(ChatSendRule.ShowsHint(o));
        }

        [Test]
        public void ALineTheChatCannotSendNowStaysInTheBoxShowsTheHintAndIsNotLogged()
        {
            ChatSendOutcome o = ChatSendRule.Outcome("hello", canPublish: false, published: false);
            Assert.AreEqual(ChatSendOutcome.Refused, o);
            Assert.IsFalse(ChatSendRule.ClearsTheBox(o));
            Assert.IsTrue(ChatSendRule.ShowsHint(o));
            Assert.IsFalse(ChatSendRule.IsLogged(o));
        }

        [Test]
        public void APublishTheServerRefusedIsNotLoggedEither()
        {
            ChatSendOutcome o = ChatSendRule.Outcome("hello", canPublish: true, published: false);
            Assert.AreEqual(ChatSendOutcome.Refused, o);
            Assert.IsFalse(ChatSendRule.IsLogged(o));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AnEmptyLineIsClearedWithNoHintAndNoLog(string typed)
        {
            ChatSendOutcome o = ChatSendRule.Outcome(typed, canPublish: false, published: false);
            Assert.AreEqual(ChatSendOutcome.Ignored, o);
            Assert.IsTrue(ChatSendRule.ClearsTheBox(o));
            Assert.IsFalse(ChatSendRule.ShowsHint(o));
            Assert.IsFalse(ChatSendRule.IsLogged(o));
        }
    }
}
