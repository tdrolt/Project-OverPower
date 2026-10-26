using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Step 0 review fix (b), 2026-09-26: the scrub ConsoleLineRule and MatchTelemetry.
    /// LogChat now share - Apply() is the pure half (AppIdTargets() needs PhotonNetwork, so it is
    /// exercised only indirectly, through ConsoleTelemetry/MatchTelemetry). Uses the fake id
    /// TEST-APP-ID-1234 only, never a real one (project rule).</summary>
    public class TelemetryScrubTests
    {
        private const string FakeAppId = "TEST-APP-ID-1234";

        [Test]
        public void EveryOccurrenceOfATargetIsReplacedAndNeverAppearsInTheResult()
        {
            string text = $"connecting with {FakeAppId} to region eu, retrying with {FakeAppId} again";
            string result = TelemetryScrub.Apply(text, new[] { FakeAppId });

            Assert.IsFalse(result.Contains(FakeAppId));
            StringAssert.Contains("<app id>", result);
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(result, "<app id>").Count);
        }

        [Test]
        public void ANullOrEmptyTextIsReturnedAsEmptyNeverNull()
        {
            Assert.AreEqual("", TelemetryScrub.Apply(null, new[] { FakeAppId }));
            Assert.AreEqual("", TelemetryScrub.Apply("", new[] { FakeAppId }));
        }

        [Test]
        public void NullOrEmptyTargetsScrubNothing()
        {
            string text = $"id {FakeAppId}";
            Assert.AreEqual(text, TelemetryScrub.Apply(text, null));
            Assert.AreEqual(text, TelemetryScrub.Apply(text, new string[0]));
        }

        [Test]
        public void AnEmptyOrNullEntryInTargetsIsSkippedRatherThanThrowing()
        {
            string text = $"id {FakeAppId}";
            string result = TelemetryScrub.Apply(text, new[] { null, "", FakeAppId });
            StringAssert.Contains("<app id>", result);
        }
    }
}
