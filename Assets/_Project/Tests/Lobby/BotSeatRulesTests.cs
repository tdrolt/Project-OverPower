using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Lobby;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Tests
{
    /// <summary>
    /// The host's Add bot / Fill with bots / remove-a-bot writes: one compare-and-swap each, so a seat never exists without its
    /// bot record and two clicks never give two bots the same number.
    /// </summary>
    public class BotSeatRulesTests
    {
        private static readonly SeatLayout Two = new SeatLayout(new[] { 0, 1 }, 2, 1);

        private static Dictionary<string, int> Seats(params (string key, int actor)[] entries)
        {
            var d = new Dictionary<string, int>();
            foreach (var e in entries) d[e.key] = e.actor;
            return d;
        }

        [Test]
        public void FreeTeamSeatsListOneTeamOrEveryTeamInSeatOrderNeverSpectators()
        {
            var seats = Seats(("sT00", 1), ("sT11", 2));
            CollectionAssert.AreEqual(new[] { "sT01" }, BotSeatRules.FreeTeamSeats(Two, seats, 0));
            CollectionAssert.AreEqual(new[] { "sT01", "sT10" }, BotSeatRules.FreeTeamSeats(Two, seats, null));
        }

        [Test]
        public void AddingBotsSeatsEachWithItsRecordAndExpectsTheSeatsEmpty()
        {
            var seats = Seats(("sT00", 1));
            SeatWrite write = BotSeatRules.AddBots(new[] { "sT01", "sT10" }, new[] { ("Mira", 0), ("Jonas", 2) }, null, new int[0], Two, seats);
            Assert.IsFalse(write.IsNone);
            Assert.AreEqual(1001, write.Props["sT01"]);
            Assert.AreEqual(1002, write.Props["sT10"]);
            CollectionAssert.AreEqual(new[] { 1001, 1002 }, (int[])write.Props[BotRosterRules.RosterKey]);
            Assert.AreEqual(1003, write.Props[BotRosterRules.NextKey]);
            Assert.AreEqual("Mira", write.Props[BotRosterRules.PropKey(1001, BotRosterRules.NickKey)]);
            Assert.AreEqual(2, write.Props[BotRosterRules.PropKey(1002, BotRosterRules.BuildKey)]);
            Assert.IsNull(write.Expected["sT01"]);
            Assert.IsNull(write.Expected["sT10"]);
            Assert.AreEqual(LobbySeatRules.LobbyBeforeStart, write.Expected[LobbyKeys.Stage]);
            Assert.IsTrue(write.Expected.ContainsKey(BotRosterRules.NextKey));
            Assert.IsNull(write.Expected[BotRosterRules.NextKey]);
        }

        [Test]
        public void AddingContinuesTheNumbersAndKeepsTheRoster()
        {
            SeatWrite write = BotSeatRules.AddBots(new[] { "sT01" }, new[] { ("Mira", 0) }, 1005, new[] { 1002 }, Two, Seats());
            Assert.AreEqual(1005, write.Props["sT01"]);
            CollectionAssert.AreEqual(new[] { 1002, 1005 }, (int[])write.Props[BotRosterRules.RosterKey]);
            Assert.AreEqual(1006, write.Props[BotRosterRules.NextKey]);
            Assert.AreEqual(1005, write.Expected[BotRosterRules.NextKey]);
        }

        [Test]
        public void ASpectatorOrTakenSeatGetsNoBot()
        {
            Assert.IsTrue(BotSeatRules.AddBots(new[] { "sS0" }, new[] { ("Mira", 0) }, null, new int[0], Two, Seats()).IsNone);
            Assert.IsTrue(BotSeatRules.AddBots(new[] { "sT00" }, new[] { ("Mira", 0) }, null, new int[0], Two, Seats(("sT00", 4))).IsNone);
            Assert.IsTrue(BotSeatRules.AddBots(new string[0], new (string, int)[0], null, new int[0], Two, Seats()).IsNone);
        }

        [Test]
        public void RemovingABotEmptiesItsSeatAndDeletesItsRecord()
        {
            var room = new Hashtable { { BotRosterRules.RosterKey, new[] { 1001, 1002 } }, { "b1001.~n", "Mira" }, { "b1001.teamID", 0 } };
            SeatWrite write = BotSeatRules.RemoveBot(1001, Two, Seats(("sT01", 1001)), room);
            Assert.IsTrue(write.Props.ContainsKey("sT01") && write.Props["sT01"] == null);
            Assert.AreEqual(1001, write.Expected["sT01"]);
            CollectionAssert.AreEqual(new[] { 1002 }, (int[])write.Props[BotRosterRules.RosterKey]);
            Assert.IsTrue(write.Props.ContainsKey("b1001.~n") && write.Props["b1001.~n"] == null);
            Assert.IsTrue(write.Props.ContainsKey("b1001.teamID"));
        }

        [Test]
        public void OnlyABotCanBeRemoved()
        {
            Assert.IsTrue(BotSeatRules.RemoveBot(3, Two, Seats(("sT01", 3)), new Hashtable()).IsNone);
        }

        [Test]
        public void ANameIsPickedFromThePoolNeverOneAlreadyInTheRoom()
        {
            var pool = new[] { "Mira", "Jonas", "Sanne" };
            var taken = new[] { "mira", "Sanne" };
            Assert.AreEqual("Jonas", BotSeatRules.PickName(pool, taken, 0));
            Assert.AreEqual("Jonas", BotSeatRules.PickName(pool, taken, 7));
        }

        [Test]
        public void AnEmptyOrUsedUpPoolStillGivesAName()
        {
            string fromFull = BotSeatRules.PickName(new[] { "Mira" }, new[] { "Mira" }, 0);
            Assert.AreEqual("Mira 2", fromFull);
            Assert.IsFalse(string.IsNullOrEmpty(BotSeatRules.PickName(new string[0], new string[0], 3)));
        }

        [Test]
        public void TheBuildRollIsOneOfTheThree()
        {
            for (int roll = -5; roll < 20; roll++)
            {
                int build = BotSeatRules.PickBuild(roll);
                Assert.IsTrue(build >= 0 && build < BotSeatRules.BuildCount, roll.ToString());
            }
        }
    }
}
