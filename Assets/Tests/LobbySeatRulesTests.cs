using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    public class LobbySeatRulesTests
    {
        private static readonly SeatLayout Three = new SeatLayout(new[] { 0, 1, 2 }, 2, 2);
        private static readonly SeatLayout Two = new SeatLayout(new[] { 0, 1 }, 2, 1);

        private static Dictionary<string, int> Seats(params (string key, int actor)[] entries)
        {
            var d = new Dictionary<string, int>();
            foreach (var e in entries) d[e.key] = e.actor;
            return d;
        }

        [Test]
        public void SeatKeysAreBuiltFromTeamAndIndex()
        {
            Assert.AreEqual("sT12", LobbySeatRules.SeatKey(1, 2));
            Assert.AreEqual("sS0", LobbySeatRules.SpectatorSeatKey(0));
        }

        [Test]
        public void AllSeatKeysListTeamSeatsThenSpectators()
        {
            var keys = LobbySeatRules.AllSeatKeys(Two).ToList();
            CollectionAssert.AreEqual(new[] { "sT00", "sT01", "sT10", "sT11", "sS0" }, keys);
            Assert.AreEqual(5, LobbySeatRules.TotalSeats(Two));
            Assert.AreEqual(8, LobbySeatRules.TotalSeats(Three));
        }

        [Test]
        public void SeatOfFindsTheSeatOrNull()
        {
            var seats = Seats(("sT01", 5), ("sS0", 9));
            Assert.AreEqual("sT01", LobbySeatRules.SeatOf(5, seats));
            Assert.AreEqual("sS0", LobbySeatRules.SeatOf(9, seats));
            Assert.IsNull(LobbySeatRules.SeatOf(7, seats));
        }

        [Test]
        public void SeatOfIgnoresEmptyValues()
        {
            var seats = Seats(("sT00", 0), ("sT01", -1));
            Assert.IsNull(LobbySeatRules.SeatOf(0, seats));
        }

        [Test]
        public void TakingAFreeSeatWritesTheActorExpectingItEmpty()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Seats());
            Assert.IsFalse(w.IsNone);
            Assert.AreEqual(4, w.Props["sT10"]);
            Assert.IsNull(w.Expected["sT10"]);
            Assert.AreEqual(1, w.Props.Count);
            Assert.AreEqual(1, w.Expected.Count);
        }

        [Test]
        public void MovingAlsoClearsTheOldSeatExpectingTheActor()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Seats(("sT00", 4)));
            Assert.AreEqual(4, w.Props["sT10"]);
            Assert.IsNull(w.Props["sT00"]);
            Assert.IsNull(w.Expected["sT10"]);
            Assert.AreEqual(4, w.Expected["sT00"]);
        }

        [Test]
        public void ASeatTakenBySomeoneElseIsRefused()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Seats(("sT10", 6)));
            Assert.AreSame(SeatWrite.None, w);
            Assert.IsTrue(w.IsNone);
        }

        [Test]
        public void YourOwnSeatIsRefused()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Seats(("sT10", 4)));
            Assert.AreSame(SeatWrite.None, w);
        }

        [Test]
        public void LeavingClearsTheSeatExpectingTheActor()
        {
            var w = LobbySeatRules.LeaveSeat(4, Seats(("sT01", 4)));
            Assert.IsNull(w.Props["sT01"]);
            Assert.AreEqual(4, w.Expected["sT01"]);
        }

        [Test]
        public void LeavingWithoutASeatIsNone()
        {
            Assert.AreSame(SeatWrite.None, LobbySeatRules.LeaveSeat(4, Seats(("sT01", 5))));
        }

        [Test]
        public void AutoFillSendsOnePlayerToTheEmptiestTeam()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2), ("sT11", 3));
            var result = LobbySeatRules.AutoFill(Three, seats, new[] { 10 });
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(10, result["sT20"]);
        }

        [Test]
        public void AutoFillTieGoesToTheLeftMostTeam()
        {
            var result = LobbySeatRules.AutoFill(Three, Seats(), new[] { 10 });
            Assert.AreEqual(10, result["sT00"]);
        }

        [Test]
        public void AutoFillSkipsAFullTeam()
        {
            var seats = Seats(("sT00", 1), ("sT01", 2));
            var result = LobbySeatRules.AutoFill(Three, seats, new[] { 10 });
            Assert.AreEqual(10, result["sT10"]);
        }

        [Test]
        public void AutoFillUsesTheLowestFreeIndex()
        {
            // team 0 has seat 0 taken, seat 1 free; teams 1 and 2 hold one player each, so team 0 (1 filled, ties
            // left-most) gets the next player in its free seat 1
            var seats = Seats(("sT00", 1), ("sT10", 2), ("sT20", 3));
            var result = LobbySeatRules.AutoFill(Three, seats, new[] { 10 });
            Assert.AreEqual(10, result["sT01"]);
        }

        [Test]
        public void AutoFillFindsAGapBelowATakenSeat()
        {
            var seats = Seats(("sT01", 1), ("sT10", 2), ("sT20", 3));
            var result = LobbySeatRules.AutoFill(Three, seats, new[] { 10 });
            Assert.AreEqual(10, result["sT00"]);
        }

        [Test]
        public void AutoFillGoesToSpectatorSeatsWhenTeamsAreFull()
        {
            var layout = new SeatLayout(new[] { 0, 1 }, 1, 2);
            var seats = Seats(("sT00", 1), ("sT10", 2), ("sS0", 3));
            var result = LobbySeatRules.AutoFill(layout, seats, new[] { 10 });
            Assert.AreEqual(10, result["sS1"]);
        }

        [Test]
        public void AutoFillLeavesOutAnActorWhenEverythingIsFull()
        {
            var layout = new SeatLayout(new[] { 0, 1 }, 1, 1);
            var seats = Seats(("sT00", 1), ("sT10", 2), ("sS0", 3));
            var result = LobbySeatRules.AutoFill(layout, seats, new[] { 10 });
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void AutoFillPlacesSeveralInAscendingOrderWithoutSharingASeat()
        {
            var result = LobbySeatRules.AutoFill(Three, Seats(), new[] { 10, 11, 12, 13 });
            Assert.AreEqual(10, result["sT00"]);
            Assert.AreEqual(11, result["sT10"]);
            Assert.AreEqual(12, result["sT20"]);
            Assert.AreEqual(13, result["sT01"]);
            Assert.AreEqual(4, result.Values.Distinct().Count());
        }

        [Test]
        public void MayStartGameIsFalseWhenATeamStaysEmpty()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2));
            Assert.IsFalse(LobbySeatRules.MayStartGame(Three, seats, new int[0]));
        }

        [Test]
        public void MayStartGameIsTrueWhenNoRoleFillsTheLastEmptyTeam()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2));
            Assert.IsTrue(LobbySeatRules.MayStartGame(Three, seats, new[] { 10 }));
        }

        [Test]
        public void MayStartGameOnTwoTeamsNeverAsksAboutTeamTwo()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2));
            Assert.IsTrue(LobbySeatRules.MayStartGame(Two, seats, new int[0]));
        }

        [Test]
        public void MayEndWarmupNeedsEveryLayoutTeam()
        {
            var present = new Dictionary<int, int> { { 0, 2 }, { 1, 0 }, { 2, 1 } };
            Assert.IsFalse(LobbySeatRules.MayEndWarmup(Three, present));
            present[1] = 1;
            Assert.IsTrue(LobbySeatRules.MayEndWarmup(Three, present));
        }

        [Test]
        public void MayEndWarmupCountsAMissingTeamAsZero()
        {
            var present = new Dictionary<int, int> { { 0, 1 } };
            Assert.IsFalse(LobbySeatRules.MayEndWarmup(Two, present));
            present[1] = 1;
            Assert.IsTrue(LobbySeatRules.MayEndWarmup(Two, present));
        }

        [Test]
        public void LateJoinerGoesToTheEmptiestTeamInTheMatch()
        {
            var seats = Seats(("sT00", 1), ("sT01", 2), ("sT10", 3));
            Assert.AreEqual("sT11", LobbySeatRules.PlaceLateJoiner(Three, seats, new[] { 0, 1 }));
        }

        [Test]
        public void LateJoinerTieGoesLeftMost()
        {
            Assert.AreEqual("sT00", LobbySeatRules.PlaceLateJoiner(Three, Seats(), new[] { 1, 0 }));
        }

        [Test]
        public void LateJoinerNeverPicksATeamOutsideTheMatch()
        {
            // team 2 is completely empty but is not playing, so it must not be chosen
            var seats = Seats(("sT00", 1), ("sT01", 2), ("sT10", 3));
            Assert.AreEqual("sT11", LobbySeatRules.PlaceLateJoiner(Three, seats, new[] { 0, 1 }));
        }

        [Test]
        public void LateJoinerGoesToASpectatorSeatWhenTeamSeatsAreFull()
        {
            var seats = Seats(("sT00", 1), ("sT01", 2), ("sT10", 3), ("sT11", 4));
            Assert.AreEqual("sS0", LobbySeatRules.PlaceLateJoiner(Two, seats, new[] { 0, 1 }));
        }

        [Test]
        public void LateJoinerGetsNullWhenEverythingIsFull()
        {
            var seats = Seats(("sT00", 1), ("sT01", 2), ("sT10", 3), ("sT11", 4), ("sS0", 5));
            Assert.IsNull(LobbySeatRules.PlaceLateJoiner(Two, seats, new[] { 0, 1 }));
        }
    }
}
