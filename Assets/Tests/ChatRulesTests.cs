using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Chat;

namespace Overpower.Tests
{
    /// <summary>Lobby Task 11: one chat channel per lobby, and the small message each line travels as.</summary>
    public class ChatRulesTests
    {
        [Test]
        public void TheChannelIsNamedAfterTheRoom()
        {
            Assert.AreEqual("abc-123", ChatChannelRule.ChannelFor("abc-123"));
            Assert.AreNotEqual(ChatChannelRule.ChannelFor("Alpha"), ChatChannelRule.ChannelFor("Beta"), "two lobbies never share a channel");
        }

        [Test]
        public void OutsideARoomThereIsNoChannel()
        {
            Assert.IsNull(ChatChannelRule.ChannelFor(null));
            Assert.IsNull(ChatChannelRule.ChannelFor(""));
        }

        [Test]
        public void ALateMessageFromAnOldChannelIsNotAccepted()
        {
            Assert.IsTrue(ChatChannelRule.Accepts("Beta", "Beta"));
            Assert.IsFalse(ChatChannelRule.Accepts("Beta", "Alpha"), "still subscribed to Beta, an Alpha line arrives late");
            Assert.IsFalse(ChatChannelRule.Accepts(null, "Alpha"), "on the list: nothing is accepted");
            Assert.IsFalse(ChatChannelRule.Accepts("Beta", null));
        }

        [Test]
        public void AMessageSurvivesTheRoundTrip()
        {
            var sent = new ChatMessage("Tudor", 2, false, "push top zone with me");
            Assert.IsTrue(ChatMessage.TryDecode(sent.Encode(), out ChatMessage got));
            Assert.AreEqual("Tudor", got.Name);
            Assert.AreEqual(2, got.Team);
            Assert.IsFalse(got.Spectator);
            Assert.AreEqual("push top zone with me", got.Text);
        }

        [Test]
        public void ASpectatorFlagSurvivesTheRoundTrip()
        {
            Assert.IsTrue(ChatMessage.TryDecode(new ChatMessage("Kim", -1, true, "gg").Encode(), out ChatMessage got));
            Assert.IsTrue(got.Spectator);
            Assert.AreEqual(-1, got.Team);
        }

        [Test]
        public void MalformedMessagesAreIgnored()
        {
            Assert.IsFalse(ChatMessage.TryDecode(null, out _));
            Assert.IsFalse(ChatMessage.TryDecode("just a string", out _), "an old-style plain text message");
            Assert.IsFalse(ChatMessage.TryDecode(new Hashtable(), out _));
            Assert.IsFalse(ChatMessage.TryDecode(new Hashtable { { "name", "A" }, { "team", 0 }, { "spec", false } }, out _), "no text");
            Assert.IsFalse(ChatMessage.TryDecode(new Hashtable { { "name", "A" }, { "team", "zero" }, { "spec", false }, { "text", "x" } }, out _), "team not a number");
            Assert.IsFalse(ChatMessage.TryDecode(new Hashtable { { "name", 5 }, { "team", 0 }, { "spec", false }, { "text", "x" } }, out _), "name not text");
            Assert.IsFalse(ChatMessage.TryDecode(new Hashtable { { "name", "A" }, { "team", 0 }, { "spec", "no" }, { "text", "x" } }, out _), "spec not a flag");
            Assert.IsFalse(ChatMessage.TryDecode(new Hashtable { { "name", "A" }, { "team", 0 }, { "spec", false }, { "text", "   " } }, out _), "blank text");
        }

        [Test]
        public void TextAndNameAreCutAndCannotCarryMarkup()
        {
            string longText = new string('a', 500);
            var raw = new Hashtable { { "name", "<b>Tudor</b>" }, { "team", 0 }, { "spec", false }, { "text", "<color=red>hi</color>" + longText } };
            Assert.IsTrue(ChatMessage.TryDecode(raw, out ChatMessage got));
            Assert.IsFalse(got.Name.Contains("<"));
            Assert.IsFalse(got.Text.Contains("<"));
            Assert.LessOrEqual(got.Text.Length, ChatMessage.MaxTextLength);
            Assert.LessOrEqual(got.Name.Length, ChatMessage.MaxNameLength);
        }

        [Test]
        public void ASpectatorLineStartsWithTheSpectatorTag()
        {
            Assert.AreEqual("[SPEC] Kim", ChatLine.Speaker(new ChatMessage("Kim", -1, true, "x"), "[SPEC]"));
            Assert.AreEqual("Tudor", ChatLine.Speaker(new ChatMessage("Tudor", 0, false, "x"), "[SPEC]"));
        }

        [Test]
        public void ALineReadsNameThenText()
        {
            string line = ChatLine.Format(new ChatMessage("Sam", 1, false, "mine at the box"), "[SPEC]", "9C7BE0");
            Assert.AreEqual("<color=#9C7BE0><b>Sam:</b></color> mine at the box", line);
        }

        [Test]
        public void ALineNeverHoldsAFullUserId()
        {
            string id = "0123456789abcdef0123456789abcdef";
            var raw = new Hashtable { { "name", id }, { "team", 0 }, { "spec", false }, { "text", "hi" } };
            Assert.IsTrue(ChatMessage.TryDecode(raw, out ChatMessage got));
            Assert.IsFalse(ChatLine.Format(got, "[SPEC]", "FFFFFF").Contains(id), "a long 'name' is cut, so a user id sent as a name never shows whole");
            Assert.AreEqual("01234567", ChatLine.ShortId(id), "logs show 8 characters at most");
            Assert.AreEqual("", ChatLine.ShortId(null));
        }

        [Test]
        public void TheThemeAssetCarriesTheChatLook()
        {
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.IsNotNull(theme);
            Assert.AreEqual(0.38f, theme.chatPanelAlpha, 0.001f, "board 7A: a panel at about 38% opacity");
            Assert.AreEqual(27f, theme.chatTextSize, 0.001f, "board 7A: 18 px text x 1.5");
            Assert.AreEqual("Enter to type, Escape to close", theme.chatInputPlaceholder);
            Assert.AreEqual("[SPEC]", theme.chatSpectatorTag);
            Assert.Greater(theme.chatPanelSize.x, 0f);
            Assert.Greater(theme.chatLobbySize.x, 0f);
            Assert.Greater(theme.chatMaxLines, 0);
        }

        [Test]
        public void TheOldPressEnterToChatLabelIsGone()
        {
            string scene = System.IO.File.ReadAllText("Assets/Scenes/Game Scene.unity");
            StringAssert.DoesNotContain("to chat", scene, "the pixel-font hint was removed; the chat's own input box is the only hint");
            StringAssert.DoesNotContain("m_Name: chattext", scene);
            string screen = System.IO.File.ReadAllText("Assets/scripts/UI/Lobby/NameScreen.cs");
            StringAssert.DoesNotContain("chattext", screen, "nothing switches the old hint object on any more");
        }

        [Test]
        public void OnlyTheNewestLinesAreKept()
        {
            var lines = new System.Collections.Generic.List<string> { "1", "2", "3" };
            ChatLine.Trim(lines, 2);
            CollectionAssert.AreEqual(new[] { "2", "3" }, lines);
        }
    }
}
