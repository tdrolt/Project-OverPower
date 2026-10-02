using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    public class LobbyRoomRulesTests
    {
        private const string You = " (you)";
        private const string Host = " · host";

        [Test]
        public void ASeatReadsTheNameAndMarksYouAndTheHost()
        {
            Assert.AreEqual("Mara", LobbyRoomRules.SeatText("Mara", mine: false, host: false, You, Host));
            Assert.AreEqual("Tudor (you)", LobbyRoomRules.SeatText("Tudor", mine: true, host: false, You, Host));
            Assert.AreEqual("Sam · host", LobbyRoomRules.SeatText("Sam", mine: false, host: true, You, Host));
            Assert.AreEqual("Tudor (you) · host", LobbyRoomRules.SeatText("Tudor", mine: true, host: true, You, Host));
        }

        [Test]
        public void ASeatWithoutANameStillReadsSomething()
        {
            Assert.AreEqual("?", LobbyRoomRules.SeatText(null, false, false, You, Host));
            Assert.AreEqual("? (you)", LobbyRoomRules.SeatText("", true, false, You, Host));
        }

        [Test]
        public void ATeamIsCalledByItsNameOrByItsNumberPlusOne()
        {
            var names = new[] { "White", "Purple", "Cyan" };
            Assert.AreEqual("Cyan", LobbyRoomRules.TeamName(names, 2));
            Assert.AreEqual("Team 4", LobbyRoomRules.TeamName(names, 3));
            Assert.AreEqual("Team 1", LobbyRoomRules.TeamName(null, 0));
            Assert.AreEqual("Team 1", LobbyRoomRules.TeamName(new[] { "" }, 0), "an empty name is no name");
        }

        private static readonly SeatLayout Three = new SeatLayout(new[] { 0, 1, 2 }, 3, 3);
        private static readonly SeatLayout Two = new SeatLayout(new[] { 0, 1 }, 3, 3);

        [Test]
        public void TheWarmupMayEndWithNoBlockReason()
        {
            var present = new Dictionary<int, int> { { 0, 1 }, { 1, 2 }, { 2, 1 } };
            Assert.IsNull(LobbySeatRules.EndWarmupBlockReason(Three, present));
        }

        [Test]
        public void TheWarmupBlockReasonNamesTheFirstTeamWithNobodyPresent()
        {
            Assert.AreEqual(2, LobbySeatRules.EndWarmupBlockReason(Three, new Dictionary<int, int> { { 0, 1 }, { 1, 1 } }), "a missing team has none");
            Assert.AreEqual(1, LobbySeatRules.EndWarmupBlockReason(Three, new Dictionary<int, int> { { 0, 1 }, { 1, 0 }, { 2, 0 } }));
            Assert.AreEqual(0, LobbySeatRules.EndWarmupBlockReason(Three, new Dictionary<int, int>()));
        }

        [Test]
        public void TheWarmupBlockReasonOnTwoTeamsNeverNamesTeamTwo()
        {
            var present = new Dictionary<int, int> { { 0, 1 }, { 1, 1 } };
            Assert.IsNull(LobbySeatRules.EndWarmupBlockReason(Two, present));
        }

        [Test]
        public void TheWarmupBlockReasonAgreesWithMayEndWarmup()
        {
            foreach (int a in new[] { 0, 1 })
                foreach (int b in new[] { 0, 1 })
                    foreach (int c in new[] { 0, 1 })
                    {
                        var present = new Dictionary<int, int> { { 0, a }, { 1, b }, { 2, c } };
                        Assert.AreEqual(LobbySeatRules.MayEndWarmup(Three, present), LobbySeatRules.EndWarmupBlockReason(Three, present) == null);
                    }
        }
    }
}
