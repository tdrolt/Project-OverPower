using NUnit.Framework;
using Overpower.Telemetry;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Every client sends its own match log to Tudor's collector (a Google Apps Script web app) when a match ends, on every platform;
    /// desktop builds keep their files too. The send is fire and forget: plain text (no browser permission check), a time-out,
    /// failures only logged. The sent copy keeps player names (Tudor, 29 Oct) but no chat text, and Windows user names are cut. Each
    /// copy also says which match it belongs to, so the collector puts one match's files in one folder named after its date and time.
    /// </summary>
    public class MatchLogUploadRulesTests
    {
        private const string Url = "https://script.google.com/macros/s/abc123/exec";

        // ---- when to send

        [Test]
        public void ARealBuildWithEverythingOnSends() =>
            Assert.IsTrue(MatchLogUploadRules.ShouldUpload(true, true, Url, developmentRun: false, allowDevelopmentRuns: false, hasLog: true));

        [TestCase(false, true, Url, false, false, true, TestName = "telemetry off")]
        [TestCase(true, false, Url, false, false, true, TestName = "upload off")]
        [TestCase(true, true, "", false, false, true, TestName = "no address yet")]
        [TestCase(true, true, Url, true, false, true, TestName = "a development run (Editor, Dev clients)")]
        [TestCase(true, true, Url, false, false, false, TestName = "no log to send")]
        public void NothingIsSent(bool telemetry, bool upload, string url, bool devRun, bool allowDev, bool hasLog) =>
            Assert.IsFalse(MatchLogUploadRules.ShouldUpload(telemetry, upload, url, devRun, allowDev, hasLog));

        [Test]
        public void DevelopmentRunsSendOnlyWhenAllowed() =>
            Assert.IsTrue(MatchLogUploadRules.ShouldUpload(true, true, Url, developmentRun: true, allowDevelopmentRuns: true, hasLog: true));

        [TestCase(Url, true)]
        [TestCase("http://script.google.com/macros/s/abc123/exec", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("paste the address here", false)]
        public void OnlyAnHttpsAddressIsUsable(string url, bool expected) => Assert.AreEqual(expected, MatchLogUploadRules.IsUsableUrl(url));

        [Test]
        public void ALogThatDidNotGrowIsNotSentAgain()
        {
            Assert.IsTrue(MatchLogUploadRules.SendsAgain(lastSentName: null, lastSentLength: 0, name: "a_1.jsonl", length: 10));
            Assert.IsFalse(MatchLogUploadRules.SendsAgain("a_1.jsonl", 10, "a_1.jsonl", 10));
            Assert.IsTrue(MatchLogUploadRules.SendsAgain("a_1.jsonl", 10, "a_1.jsonl", 25), "the tail after the result screen");
            Assert.IsTrue(MatchLogUploadRules.SendsAgain("a_1.jsonl", 10, "b_1.jsonl", 10), "the next match");
        }

        // ---- what is sent

        [Test]
        public void TheFileIsNamedAfterTheMatchAndThePlayerSlot() =>
            Assert.AreEqual("2026-10-28_1400_Conquest_3v3_2.jsonl", MatchLogUploadRules.RemoteFileName("2026-10-28_1400_Conquest_3v3", 2));

        [Test]
        public void TheFileNameKeepsNoPathCharacters() =>
            Assert.AreEqual("a_b_c_1.jsonl", MatchLogUploadRules.RemoteFileName("a/b\\c", 1));

        [Test]
        public void TheSentCopyKeepsPlayerNames()
        {
            string log = "{\"e\":\"session\",\"t\":1,\"a\":1,\"nick\":\"Tudor\",\"tm\":0}\n" +
                         "{\"e\":\"join\",\"t\":2,\"a\":2,\"tm\":1,\"nick\":\"Radu\"}\n" +
                         "{\"e\":\"marker\",\"t\":4,\"note\":\"host changed to 2 Radu\"}\n";
            Assert.AreEqual(log, MatchLogUploadRules.SentCopy(log));
        }

        [Test]
        public void TheSentCopyHasNoChatText()
        {
            string sent = MatchLogUploadRules.SentCopy("{\"e\":\"chat\",\"t\":5,\"a\":1,\"text\":\"my number is 0612345678\"}\n");
            StringAssert.Contains("\"e\":\"chat\"", sent, "the chat line stays, so the report still sees when people talked");
            StringAssert.Contains("\"text\":\"\"", sent);
            StringAssert.DoesNotContain("0612345678", sent);
        }

        [Test]
        public void TheReportersNoteAfterCtrlBIsKept()
        {
            string log = "{\"e\":\"chat\",\"t\":5,\"a\":1,\"text\":\"gg\"}\n" +
                         "{\"e\":\"bug\",\"t\":10.5,\"a\":1,\"tm\":0}\n" +
                         "{\"e\":\"chat\",\"t\":20,\"a\":1,\"text\":\"the wall ate my shot\"}\n" +
                         "{\"e\":\"chat\",\"t\":70.5,\"a\":1,\"text\":\"still in the window\"}\n" +
                         "{\"e\":\"chat\",\"t\":71,\"a\":1,\"text\":\"lol\"}\n";
            string sent = MatchLogUploadRules.SentCopy(log);
            StringAssert.Contains("the wall ate my shot", sent, "the note the toast asks for");
            StringAssert.Contains("still in the window", sent, "60 s after the mark, as the report reads it");
            StringAssert.DoesNotContain("gg", sent, "before the mark");
            StringAssert.DoesNotContain("lol", sent, "after the window");
        }

        [Test]
        public void TheSentCopyHasNoWindowsUserName()
        {
            string sent = MatchLogUploadRules.SentCopy("{\"e\":\"console\",\"t\":3,\"msg\":\"saved to C:\\\\Users\\\\tudor.oltean\\\\Documents\\\\OverPower\"}\n");
            StringAssert.DoesNotContain("tudor.oltean", sent);
            StringAssert.Contains("C:\\\\Users\\\\user\\\\Documents", sent);
        }

        [Test]
        public void TheMatchIdComesFromTheSessionLine()
        {
            string log = "{\"e\":\"marker\",\"t\":0,\"m\":\"not-this\"}\n" +
                         "{\"e\":\"session\",\"t\":1,\"schema\":1,\"m\":\"a1b2c3\",\"a\":1,\"nick\":\"Tudor\"}\n";
            Assert.AreEqual("a1b2c3", MatchLogUploadRules.MatchIdOf(log));
        }

        [TestCase("", TestName = "an empty log")]
        [TestCase(null, TestName = "no log")]
        [TestCase("{\"e\":\"join\",\"t\":2,\"a\":2}\n", TestName = "no session line")]
        public void NoMatchIdWithoutASessionLine(string log) => Assert.AreEqual("", MatchLogUploadRules.MatchIdOf(log));

        [Test]
        public void ThePayloadCarriesTheKeyTheFileTheMatchAndTheText()
        {
            string payload = MatchLogUploadRules.Payload("k", "m_1.jsonl", "2026-10-29_2210_Conquest-3v3v3_Tudors-lobby", "a1b2c3", "line1\nline2");
            var back = UnityEngine.JsonUtility.FromJson<MatchLogUploadRules.UploadMessage>(payload);
            Assert.AreEqual("k", back.key);
            Assert.AreEqual("m_1.jsonl", back.file);
            Assert.AreEqual("2026-10-29_2210_Conquest-3v3v3_Tudors-lobby", back.match);
            Assert.AreEqual("a1b2c3", back.matchId);
            Assert.AreEqual("line1\nline2", back.body);
        }

        [Test]
        public void ItIsSentAsPlainTextSoTheBrowserAsksNoPermission() =>
            Assert.AreEqual("text/plain;charset=utf-8", MatchLogUploadRules.ContentType);

        [TestCase(200, true)]
        [TestCase(204, true)]
        [TestCase(302, true)]
        [TestCase(0, false)]
        [TestCase(404, false)]
        [TestCase(500, false)]
        public void ARedirectCountsAsDelivered(long code, bool expected) => Assert.AreEqual(expected, MatchLogUploadRules.IsDelivered(code));

        // ---- the notice on the name screen

        [Test]
        public void TheNoticeShowsWhenTheLogWillBeSent() =>
            Assert.IsTrue(MatchLogUploadRules.ShowsNotice(true, true, Url, developmentRun: false, allowDevelopmentRuns: false));

        [Test]
        public void NoNoticeWhenNothingWillBeSent() =>
            Assert.IsFalse(MatchLogUploadRules.ShowsNotice(true, true, "", developmentRun: false, allowDevelopmentRuns: false));

        // ---- the game calls the rules

        [Test]
        public void TheMatchEndSendsTheLog() =>
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), nameof(MatchLogZip.ZipNow), typeof(MatchLogZip).GetMethod("TryUpload",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)));

        [Test]
        public void TheSendAsksTheRule() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MatchLogZip), "TryUpload", typeof(MatchLogUploadRules).GetMethod(nameof(MatchLogUploadRules.ShouldUpload))));

        [Test]
        public void TheSentTextIsTheCopyWithoutChat() =>
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), "TryUpload", typeof(MatchLogUploadRules).GetMethod(nameof(MatchLogUploadRules.SentCopy))));

        [Test]
        public void TheSendSaysWhichMatchItBelongsTo() =>
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), "TryUpload", typeof(MatchLogUploadRules).GetMethod(nameof(MatchLogUploadRules.MatchIdOf))));

        [Test]
        public void TheNoticeDoesNotCallTheDataAnonymous()
        {
            string theme = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/UiTheme.asset");
            string line = theme.Substring(theme.IndexOf("matchDataNoticeText:", System.StringComparison.Ordinal));
            line = line.Substring(0, line.IndexOf('\n'));
            StringAssert.DoesNotContain("nonymous", line, "names are sent now");
            var fresh = UnityEngine.ScriptableObject.CreateInstance<UiTheme>();
            try { StringAssert.DoesNotContain("nonymous", fresh.matchDataNoticeText); }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
        }

        [Test]
        public void TheNameScreenAsksWhetherToShowTheNotice() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(NameScreen), "BuildNameScreen", typeof(MatchLogUploadRules).GetMethod(nameof(MatchLogUploadRules.ShowsNotice))));

        [Test]
        public void TheNewSettingsAreInTheirAssetsWithATooltip()
        {
            string telemetry = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/TelemetryConfig.asset").Replace("\r\n", "\n");
            foreach (string name in new[] { "uploadEnabled", "uploadUrl", "uploadKey", "uploadTimeoutSeconds", "uploadFromDevelopmentBuilds" })
            {
                var field = typeof(Overpower.Data.TelemetryConfig).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(field, name);
                Assert.NotNull(System.Attribute.GetCustomAttribute(field, typeof(UnityEngine.TooltipAttribute)), name + " has no tooltip");
                StringAssert.Contains("\n  " + name + ":", telemetry, name + " is not in TelemetryConfig.asset");
            }
            string theme = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/UiTheme.asset").Replace("\r\n", "\n");
            StringAssert.Contains("\n  matchDataNoticeText:", theme);
            Assert.NotNull(System.Attribute.GetCustomAttribute(typeof(UiTheme).GetField("matchDataNoticeText"), typeof(UnityEngine.TooltipAttribute)));
        }
    }
}
