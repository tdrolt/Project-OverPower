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

        private static readonly int[] Rolls = { -5, -1, 0, 1, 2, 3, 4, 7, 19, 20 };

        private static Hashtable BotRecords(params (int actor, int build)[] bots)
        {
            var room = new Hashtable();
            foreach (var b in bots) room[BotRosterRules.PropKey(b.actor, BotRosterRules.BuildKey)] = b.build;
            return room;
        }

        [Test]
        public void TheBuildRollIsOneOfTheThree()
        {
            for (int roll = -5; roll < 20; roll++)
            {
                int build = BotSeatRules.PickBuild(roll, null);
                Assert.IsTrue(build >= 0 && build < BotSeatRules.BuildCount, roll.ToString());
            }
        }

        [Test]
        public void WithNothingTakenEveryBuildCanBePicked()
        {
            foreach (IEnumerable<int> taken in new IEnumerable<int>[] { null, new int[0] })
            {
                var seen = new HashSet<int>();
                for (int roll = 0; roll < 9; roll++) seen.Add(BotSeatRules.PickBuild(roll, taken));
                Assert.AreEqual(BotSeatRules.BuildCount, seen.Count);
            }
        }

        [Test]
        public void TheOnlyOpenBuildIsTheOneChosen()
        {
            int open = 2;
            var taken = new List<int>();
            for (int b = 0; b < BotSeatRules.BuildCount; b++)
                if (b != open) taken.Add(b);
            foreach (int roll in Rolls) Assert.AreEqual(open, BotSeatRules.PickBuild(roll, taken), roll.ToString());
        }

        [Test]
        public void ABuildAlreadyOnTheTeamIsNeverPickedAndEachOpenOneCan()
        {
            var taken = new[] { 0 };
            var seen = new HashSet<int>();
            for (int roll = -5; roll <= 20; roll++)
            {
                int build = BotSeatRules.PickBuild(roll, taken);
                Assert.AreNotEqual(0, build, roll.ToString());
                seen.Add(build);
            }
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, seen);
        }

        [Test]
        public void WhenEveryBuildIsTakenAnyBuildIsPickedAgain()
        {
            var taken = new[] { 0, 1, 2 };
            var seen = new HashSet<int>();
            for (int roll = 0; roll < 9; roll++)
            {
                int build = BotSeatRules.PickBuild(roll, taken);
                Assert.IsTrue(build >= 0 && build < BotSeatRules.BuildCount, roll.ToString());
                seen.Add(build);
            }
            Assert.AreEqual(BotSeatRules.BuildCount, seen.Count);
        }

        [Test]
        public void ABotAddedAfterAnotherOnTheSameTeamGetsADifferentBuildWhateverTheRolls()
        {
            for (int r1 = 0; r1 < 9; r1++)
                for (int r2 = 0; r2 < 9; r2++)
                    for (int r3 = 0; r3 < 9; r3++)
                    {
                        var team = new List<int>();
                        foreach (int roll in new[] { r1, r2, r3 }) team.Add(BotSeatRules.PickBuild(roll, team));
                        Assert.AreEqual(BotSeatRules.BuildCount, new HashSet<int>(team).Count, $"{r1} {r2} {r3}");
                    }
        }

        [Test]
        public void TheBuildsAlreadyOnEachTeamAreReadFromTheBotsOnly()
        {
            var seats = Seats(("sT00", 1001), ("sT01", 5), ("sT10", 1002), ("sT11", 1003), ("sS0", 1004));
            Hashtable room = BotRecords((1001, 1), (1002, 2));
            room[BotRosterRules.PropKey(1003, BotRosterRules.BuildKey)] = "not a number";
            room[BotRosterRules.PropKey(5, BotRosterRules.BuildKey)] = 0;
            room[BotRosterRules.PropKey(1004, BotRosterRules.BuildKey)] = 0;

            Dictionary<int, List<int>> byTeam = BotSeatRules.BuildsByTeam(seats, room);

            CollectionAssert.AreEqual(new[] { 1 }, byTeam[0]);
            CollectionAssert.AreEqual(new[] { 2 }, byTeam[1]);
            Assert.AreEqual(2, byTeam.Count);
        }

        [Test]
        public void NoBotsMeansNoBuildsTaken()
        {
            Assert.AreEqual(0, BotSeatRules.BuildsByTeam(Seats(("sT00", 3)), new Hashtable()).Count);
            Assert.AreEqual(0, BotSeatRules.BuildsByTeam(null, null).Count);
        }

        [Test]
        public void FillingWithBotsGivesEveryTeamThreeDifferentBuildsAroundABotAlreadySeated()
        {
            var three = new SeatLayout(new[] { 0, 1, 2 }, 3, 0);
            var seats = Seats(("sT00", 1001));
            Hashtable room = BotRecords((1001, 2));
            for (int salt = 0; salt < 9; salt++)
            {
                Dictionary<int, List<int>> byTeam = BotSeatRules.BuildsByTeam(seats, room);
                int step = salt;
                foreach (string key in BotSeatRules.FreeTeamSeats(three, seats, null))
                {
                    LobbySeatRules.TryTeamOfSeat(key, out int team);
                    if (!byTeam.TryGetValue(team, out List<int> onTeam)) byTeam[team] = onTeam = new List<int>();
                    onTeam.Add(BotSeatRules.PickBuild(step++, onTeam));
                }
                foreach (int team in three.Teams)
                    Assert.AreEqual(BotSeatRules.BuildCount, new HashSet<int>(byTeam[team]).Count, $"team {team}, salt {salt}");
                CollectionAssert.Contains(byTeam[0], 2);
            }
        }

        [Test]
        public void AddingBotsPicksTheirBuildsThroughTheTeamAwareRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LobbySeats), nameof(LobbySeats.AddBots), typeof(BotSeatRules).GetMethod(nameof(BotSeatRules.PickBuild))));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbySeats), nameof(LobbySeats.AddBots), typeof(BotSeatRules).GetMethod(nameof(BotSeatRules.BuildsByTeam))));
        }
    }
}
