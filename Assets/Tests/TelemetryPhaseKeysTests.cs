using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T7: the runtime API's own event names/keys and the exact line shape
    /// MatchTelemetry.LogElimination/LogPhase build - through TelemetryLine directly (no Photon, no
    /// MonoBehaviour), matching the plan's "use the line builder only; no Photon needed".</summary>
    public class TelemetryPhaseKeysTests
    {
        [Test]
        public void PhaseAndEliminationEventNamesAreDistinctAndStable()
        {
            Assert.AreEqual("phase", TelemetryKeys.Phase);
            Assert.AreEqual("elimination", TelemetryKeys.Elimination);
            Assert.AreNotEqual(TelemetryKeys.Phase, TelemetryKeys.Elimination);
        }

        [Test]
        public void PhaseNumberAndTeamsRemainingKeysAreDistinctFromEveryOtherKey()
        {
            Assert.AreEqual("num", TelemetryKeys.PhaseNumber);
            Assert.AreEqual("remain", TelemetryKeys.TeamsRemaining);
            Assert.AreNotEqual(TelemetryKeys.PhaseNumber, TelemetryKeys.TeamsRemaining);
            // Neither collides with an existing identity/economy/combat key already in play.
            Assert.AreNotEqual(TelemetryKeys.Team, TelemetryKeys.PhaseNumber);
            Assert.AreNotEqual(TelemetryKeys.Zone, TelemetryKeys.TeamsRemaining);
        }

        // The two lines below are built exactly the way MatchTelemetry.LogPhase/LogElimination build
        // theirs (Begin -> Int/Ints -> Log(line.End())) - see MatchTelemetry.cs. Reproduced here with a
        // standalone TelemetryLine so the shape is pinned without needing a Photon room.

        [Test]
        public void PhaseLineShape()
        {
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Phase, 12.5);
            line.Int(TelemetryKeys.PhaseNumber, 2);
            line.Ints(TelemetryKeys.TeamsRemaining, new[] { 0, 2 });
            Assert.AreEqual("{\"e\":\"phase\",\"t\":12.5,\"num\":2,\"remain\":[0,2]}", line.End());
        }

        [Test]
        public void PhaseOneAnchorLineShape()
        {
            // MatchTelemetry's own anchor call: LogPhase(1, Array.Empty<int>()), usually before the
            // match clock is known yet (t reads -1, same sentinel `join` uses in the same window).
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Phase, -1);
            line.Int(TelemetryKeys.PhaseNumber, 1);
            line.Ints(TelemetryKeys.TeamsRemaining, System.Array.Empty<int>());
            Assert.AreEqual("{\"e\":\"phase\",\"t\":-1,\"num\":1,\"remain\":[]}", line.End());
        }

        [Test]
        public void EliminationLineShape()
        {
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Elimination, 812.25);
            line.Int(TelemetryKeys.Team, 1);
            line.Ints(TelemetryKeys.TeamsRemaining, new[] { 0, 2 });
            Assert.AreEqual("{\"e\":\"elimination\",\"t\":812.25,\"tm\":1,\"remain\":[0,2]}", line.End());
        }

        // Review fix (item 10): MatchTelemetry.LogJoinOrLeave now appends the player's own nick to
        // the `join` line only - reproduced here the same way, standalone, no Photon needed.

        [Test]
        public void JoinLineNowCarriesTheNick()
        {
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Join, -1);
            line.Int(TelemetryKeys.Actor, 3);
            line.Int(TelemetryKeys.Team, 2);
            line.String(TelemetryKeys.Nick, "Riven");
            Assert.AreEqual("{\"e\":\"join\",\"t\":-1,\"a\":3,\"tm\":2,\"nick\":\"Riven\"}", line.End());
        }

        [Test]
        public void LeaveLineShapeIsUnchangedNoNick()
        {
            var line = new TelemetryLine();
            line.Begin(TelemetryKeys.Leave, 42.0);
            line.Int(TelemetryKeys.Actor, 3);
            line.Int(TelemetryKeys.Team, 2);
            Assert.AreEqual("{\"e\":\"leave\",\"t\":42,\"a\":3,\"tm\":2}", line.End());
        }
    }
}
