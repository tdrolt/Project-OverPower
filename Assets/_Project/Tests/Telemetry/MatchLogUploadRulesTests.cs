using NUnit.Framework;
using Overpower.Telemetry;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Every client sends its own match log to Tudor's collector (a Google Apps Script web app) when a match ends, on every platform;
    /// desktop builds keep their files too. The send is fire and forget: plain text (no browser permission check), a time-out,
    /// failures only logged. The sent copy is anonymous: names become "Player 3", chat text is emptied, Windows user names are cut.
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
        public void TheSentCopyNamesNoPlayer()
        {
            string log = "{\"e\":\"session\",\"t\":1,\"a\":1,\"nick\":\"Tudor\",\"tm\":0}\n" +
                         "{\"e\":\"join\",\"t\":2,\"a\":2,\"tm\":1,\"nick\":\"Radu\"}\n" +
                         "{\"e\":\"console\",\"t\":3,\"msg\":\"[TEAM] Tudor team=0 isMine=True\"}\n" +
                         "{\"e\":\"marker\",\"t\":4,\"note\":\"host changed to 2 Radu\"}\n";
            string sent = MatchLogUploadRules.Anonymize(log);
            StringAssert.DoesNotContain("Tudor", sent);
            StringAssert.DoesNotContain("Radu", sent);
            StringAssert.Contains("\"nick\":\"Player 1\"", sent);
            StringAssert.Contains("\"nick\":\"Player 2\"", sent);
            StringAssert.Contains("[TEAM] Player 1 team=0", sent);
            StringAssert.Contains("host changed to 2 Player 2", sent);
        }

        [Test]
        public void TheSentCopyHasNoChatText()
        {
            string sent = MatchLogUploadRules.Anonymize("{\"e\":\"chat\",\"t\":5,\"a\":1,\"text\":\"my number is 0612345678\"}\n");
            StringAssert.Contains("\"e\":\"chat\"", sent, "the chat line stays, so the report still sees when people talked");
            StringAssert.Contains("\"text\":\"\"", sent);
            StringAssert.DoesNotContain("0612345678", sent);
        }

        [Test]
        public void TheSentCopyHasNoWindowsUserName()
        {
            string sent = MatchLogUploadRules.Anonymize("{\"e\":\"console\",\"t\":3,\"msg\":\"saved to C:\\\\Users\\\\tudor.oltean\\\\Documents\\\\OverPower\"}\n");
            StringAssert.DoesNotContain("tudor.oltean", sent);
            StringAssert.Contains("C:\\\\Users\\\\user\\\\Documents", sent);
        }

        [Test]
        public void AShortNameInsideALongerWordIsLeftAlone()
        {
            string sent = MatchLogUploadRules.Anonymize("{\"e\":\"session\",\"a\":1,\"nick\":\"Base\"}\n{\"e\":\"console\",\"msg\":\"Baseline fired\"}\n");
            StringAssert.Contains("Baseline fired", sent);
        }

        [Test]
        public void ThePayloadCarriesTheKeyTheFileNameAndTheText()
        {
            string payload = MatchLogUploadRules.Payload("k", "m_1.jsonl", "line1\nline2");
            var back = UnityEngine.JsonUtility.FromJson<MatchLogUploadRules.UploadMessage>(payload);
            Assert.AreEqual("k", back.key);
            Assert.AreEqual("m_1.jsonl", back.file);
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
        public void TheSentTextIsTheAnonymousCopy() =>
            Assert.IsTrue(IlWiring.Uses(typeof(MatchLogZip), "TryUpload", typeof(MatchLogUploadRules).GetMethod(nameof(MatchLogUploadRules.Anonymize))));

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
