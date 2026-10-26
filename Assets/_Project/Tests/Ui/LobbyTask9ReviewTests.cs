using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Match;
using Overpower.UI;
using Photon.Realtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Overpower.Tests
{
    /// <summary>The review of lobby Task 9 (name screen, list, create screen): what a dropped connection does, the stuck Create button, names drawn as
    /// plain text, strings and numbers moved to the theme, the apostrophe of a lobby name.</summary>
    public class LobbyTask9ReviewTests
    {
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private UiTheme theme;
        private readonly System.Collections.Generic.List<Object> made = new System.Collections.Generic.List<Object>();

        [SetUp]
        public void SetUp() => theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private T Made<T>(T o) where T : Object { made.Add(o); return o; }

        // ---- a dropped connection on the list is not a dead end ----

        [TestCase(ClientState.Disconnected, true)]
        [TestCase(ClientState.PeerCreated, true)]
        [TestCase(ClientState.ConnectedToMasterServer, false)]
        [TestCase(ClientState.ConnectingToNameServer, false)]
        [TestCase(ClientState.Disconnecting, false)]
        [TestCase(ClientState.JoinedLobby, false)]
        public void FindALobbyConnectsAgainOnlyWhenTheClientIsNotConnectedAndNotOnItsWay(ClientState state, bool connect) =>
            Assert.AreEqual(connect, LobbyScreenRules.MustConnectForList(state));

        // ---- Create is greyed until it can work ----

        [TestCase(false, true, true, "Tudor's lobby", true)]
        [TestCase(true, true, true, "Tudor's lobby", false)]   // already creating
        [TestCase(false, false, true, "Tudor's lobby", false)] // not connected to the master
        [TestCase(false, true, false, "Tudor's lobby", false)] // no mode chosen
        [TestCase(false, true, true, "   ", false)]            // no name
        [TestCase(false, true, true, null, false)]
        public void CreateIsPressableOnlyWithAConnectionAModeAndAName(bool creating, bool ready, bool hasMode, string name, bool expected) =>
            Assert.AreEqual(expected, LobbyScreenRules.CreateMayBePressed(creating, ready, hasMode, name));

        [Test]
        public void ACreateThatPhotonRefusesReportsFalseAndLeavesNothingPending()
        {
            var go = Made(new GameObject("directory"));
            LobbyDirectory directory = go.AddComponent<LobbyDirectory>();
            GameModeDefinition mode = Made(ScriptableObject.CreateInstance<GameModeDefinition>());
            typeof(GameModeDefinition).GetField("teams", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, new[] { 0, 1 });
            typeof(GameModeDefinition).GetField("seatsPerTeam", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, 3);
            LogAssert.ignoreFailingMessages = true;   // Photon logs why it refused (the test client is not on a master server)
            try
            {
                Assert.IsFalse(directory.Create("Tudor's lobby", mode), "Photon refused: not connected");
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsFalse(directory.HasPendingCreate, "a refused create leaves no pending mode behind");
        }

        // ---- names are drawn as text, never as markup ----

        [Test]
        public void TheKitDrawsPlainTextWhenAskedAndMarkupByDefault()
        {
            var kit = new LobbyUiKit(theme);
            var parent = Made(new GameObject("parent", typeof(RectTransform)));
            Assert.IsTrue(kit.Text(parent.transform, "a", "x", null, 20f, Color.white).richText, "the default is unchanged: headings use <b> and <color>");
            Assert.IsFalse(kit.Text(parent.transform, "b", "x", null, 20f, Color.white, richText: false).richText);
            kit.Dispose();
        }

        [Test]
        public void TheNameAndHostCellsOfARowAreNotRichText()
        {
            var kit = new LobbyUiKit(theme);
            var canvas = Made(new GameObject("canvas", typeof(RectTransform)));
            LobbyListPanel list = LobbyListPanel.Create(canvas.transform, kit, null);
            object row = typeof(LobbyListPanel).GetMethod("BuildRow", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(list, new object[] { "L_x" });
            FieldInfo Field(string n) => row.GetType().GetField(n);
            Assert.IsFalse(((TextMeshProUGUI)Field("Name").GetValue(row)).richText, "a lobby name typed as <b>x is shown as typed");
            Assert.IsFalse(((TextMeshProUGUI)Field("Host").GetValue(row)).richText);
            Assert.IsTrue(((TextMeshProUGUI)Field("Mode").GetValue(row)).richText, "the other cells are ours: unchanged");
            kit.Dispose();
        }

        // ---- the apostrophe of "Tudor's lobby" ----

        [Test]
        public void TheLobbyNameFontsCanDrawAnApostrophe()
        {
            foreach (TMP_FontAsset font in new[] { theme.lobbyDisplayFont, theme.lobbyBodyFont, theme.lobbyBoldFont })
            {
                Assert.NotNull(font);
                // the lobby fonts are dynamic atlases (they fill with the characters first drawn), so the question is whether the font FILE has the glyph
                Assert.NotNull(font.sourceFontFile, font.name);
                Assert.IsTrue(font.sourceFontFile.HasCharacter('\''), font.name + " has no straight apostrophe");
                Assert.IsTrue(font.sourceFontFile.HasCharacter((char)0x2019), font.name + " has no typographic apostrophe");
            }
        }

        [Test]
        public void ThePrefillKeepsTheApostropheAllTheWayIntoTheNameBox()
        {
            Assert.AreEqual("{0}'s lobby", theme.createNamePrefillFormat);
            Assert.AreEqual("Tudor's lobby", LobbyScreenRules.LobbyNamePrefill("Tudor", theme.createNamePrefillFormat, LobbyConfig.DefaultLobbyNameMaxLength));
            // the box takes any character (no letters-and-digits filter) and what it holds is what the room is created with
            var kit = new LobbyUiKit(theme);
            var parent = Made(new GameObject("parent", typeof(RectTransform)));
            TMP_InputField box = kit.InputBox(parent.transform, "n", "", 40f, 20f, 4f, TextAlignmentOptions.MidlineLeft, LobbyConfig.DefaultLobbyNameMaxLength, false, out _);
            box.text = "Tudor's lobby";
            Assert.AreEqual("Tudor's lobby", box.text);
            kit.Dispose();
        }

        // ---- strings and numbers live in the theme ----

        [Test]
        public void TheLateJoinAndJoinFailureWordsComeFromTheTheme()
        {
            Assert.AreEqual("Lobby full", theme.lobbyLateJoinFullText);
            Assert.AreEqual("Could not get a seat", theme.lobbyLateJoinNoSeatText);
            Assert.AreEqual("That lobby is full.", LobbyDirectory.JoinFailureText(ErrorCode.GameFull, theme));
            Assert.AreEqual("That lobby has closed.", LobbyDirectory.JoinFailureText(ErrorCode.GameClosed, theme));
            Assert.AreEqual("That lobby no longer exists.", LobbyDirectory.JoinFailureText(ErrorCode.GameDoesNotExist, theme));
            Assert.AreEqual("Could not join that lobby.", LobbyDirectory.JoinFailureText(ErrorCode.InternalServerError, theme));
            var other = Made(ScriptableObject.CreateInstance<UiTheme>());
            other.lobbyJoinFullText = "FULL";
            Assert.AreEqual("FULL", LobbyDirectory.JoinFailureText(ErrorCode.GameFull, other), "it reads the theme it is given");
        }

        [Test]
        public void TheLayoutNumbersThatWereInTheCodeAreInTheTheme()
        {
            Assert.AreEqual(40f, theme.lobbyListScrollSensitivity);
            Assert.AreEqual(24f, theme.lobbyListHeaderGap);
            Assert.AreEqual(6f, theme.lobbyListTitleGap);
            Assert.AreEqual(24f, theme.lobbyListPlayerGap);
            Assert.AreEqual(9f, theme.lobbyListPlayingAsGap);
            Assert.AreEqual(24f, theme.lobbyListFooterGap);
            Assert.AreEqual(12f, theme.createBlockGap);
            Assert.AreEqual(1.2f, theme.createComingSoonLabelShift);
            Assert.AreEqual(1.3f, theme.createComingSoonBoxHeight);
            Assert.AreEqual(4f, theme.createComingSoonLift);
            Assert.AreEqual(9f, theme.nameScreenTitleGap);
        }

        [Test]
        public void TheFallbacksCopyTheConfigDefaultsFromOnePlace()
        {
            var config = Made(ScriptableObject.CreateInstance<LobbyConfig>());
            Assert.AreEqual(LobbyConfig.DefaultLobbyNameMaxLength, config.LobbyNameMaxLength);
            Assert.AreEqual(LobbyConfig.DefaultNameMinLength, config.NameMinLength);
            Assert.AreEqual(LobbyConfig.DefaultNameMaxLength, config.NameMaxLength);
            Assert.AreEqual(LobbyConfig.DefaultListRedrawSeconds, config.ListRedrawSeconds);
        }

        // ---- the spectator bar's real share of the screen ----

        [Test]
        public void TheBarShareAtSixteenByNineIsItsHeightOverTheReferenceHeight() =>
            Assert.AreEqual(200f / 1080f, SpectateRules.BarShareOfScreen(200f, new Vector2(1920f, 1080f), 0.5f, 1920f, 1080f), 1e-5f);

        [Test]
        public void OnAnUltraWideScreenTheScalerMakesTheBarBiggerSoItsShareGrows()
        {
            // 21:9 (2560 x 1080): match 0.5 scales by the square root of the width ratio (1.3333), so the bar is 1.1547 times as tall on screen
            float share = SpectateRules.BarShareOfScreen(200f, new Vector2(1920f, 1080f), 0.5f, 2560f, 1080f);
            Assert.AreEqual(200f * Mathf.Sqrt(2560f / 1920f) / 1080f, share, 1e-5f);
            Assert.Greater(share, 200f / 1080f);
        }

        [Test]
        public void OnATallScreenTheShareShrinksAndAMatchOfOneIgnoresTheWidth()
        {
            float narrow = SpectateRules.BarShareOfScreen(200f, new Vector2(1920f, 1080f), 0.5f, 1080f, 1080f);
            Assert.Less(narrow, 200f / 1080f);
            Assert.AreEqual(200f / 1080f, SpectateRules.BarShareOfScreen(200f, new Vector2(1920f, 1080f), 1f, 2560f, 1080f), 1e-5f, "height-matched: the width does not matter");
            Assert.AreEqual(0f, SpectateRules.BarShareOfScreen(200f, new Vector2(1920f, 1080f), 0.5f, 0f, 0f), "no screen yet: nothing to reserve");
        }
    }
}
