using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    public class LobbyListRulesTests
    {
        private static readonly SeatLayout Layout = new SeatLayout(new[] { 0, 1, 2 }, 2, 2);

        private sealed class Row
        {
            public string Name;
            public JoinAction Action;
            public LobbyStage Stage;
            public long Created;
        }

        [Test]
        public void StatusTextNamesEachStage()
        {
            Assert.AreEqual("In lobby", LobbyListRules.StatusText(LobbyStage.Lobby));
            Assert.AreEqual("Warm-up", LobbyListRules.StatusText(LobbyStage.Warmup));
            Assert.AreEqual("In match", LobbyListRules.StatusText(LobbyStage.InMatch));
        }

        [Test]
        public void JoinWhileATeamSeatIsFree()
        {
            Assert.AreEqual(JoinAction.Join, LobbyListRules.ActionFor(Layout, 5, 2));
            Assert.AreEqual(JoinAction.Join, LobbyListRules.ActionFor(Layout, 0, 0));
        }

        [Test]
        public void SpectateOnlyWhenTeamSeatsAreFullAndASpectatorSeatIsFree()
        {
            Assert.AreEqual(JoinAction.Spectate, LobbyListRules.ActionFor(Layout, 6, 1));
            Assert.AreEqual(JoinAction.Spectate, LobbyListRules.ActionFor(Layout, 6, 0));
        }

        [Test]
        public void FullWhenBothAreFull()
        {
            Assert.AreEqual(JoinAction.Full, LobbyListRules.ActionFor(Layout, 6, 2));
        }

        [Test]
        public void SortPutsJoinableThenInMatchThenFullNewestFirst()
        {
            var rows = new List<Row>
            {
                new Row { Name = "fullNew", Action = JoinAction.Full, Stage = LobbyStage.Lobby, Created = 90 },
                new Row { Name = "matchOld", Action = JoinAction.Join, Stage = LobbyStage.InMatch, Created = 10 },
                new Row { Name = "lobbyOld", Action = JoinAction.Join, Stage = LobbyStage.Lobby, Created = 20 },
                new Row { Name = "warmNew", Action = JoinAction.Spectate, Stage = LobbyStage.Warmup, Created = 50 },
                new Row { Name = "matchNew", Action = JoinAction.Spectate, Stage = LobbyStage.InMatch, Created = 60 },
                new Row { Name = "fullOld", Action = JoinAction.Full, Stage = LobbyStage.InMatch, Created = 5 },
            };
            var sorted = LobbyListRules.Sort(rows, r => r.Action, r => r.Stage, r => r.Created);
            CollectionAssert.AreEqual(
                new[] { "warmNew", "lobbyOld", "matchNew", "matchOld", "fullNew", "fullOld" },
                sorted.Select(r => r.Name).ToArray());
        }
    }
}
