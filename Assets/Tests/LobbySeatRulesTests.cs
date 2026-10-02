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
            Assert.AreEqual("sT01", LobbySeatRules.SeatOf(5, Two, seats));
            Assert.AreEqual("sS0", LobbySeatRules.SeatOf(9, Two, seats));
            Assert.IsNull(LobbySeatRules.SeatOf(7, Two, seats));
        }

        [Test]
        public void SeatOfIgnoresEmptyValues()
        {
            var seats = Seats(("sT00", 0), ("sT01", -1));
            Assert.IsNull(LobbySeatRules.SeatOf(0, Two, seats));
        }

        [Test]
        public void TakingAFreeSeatWritesTheActorExpectingItEmpty()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Two, Seats());
            Assert.IsFalse(w.IsNone);
            Assert.AreEqual(4, w.Props["sT10"]);
            Assert.IsNull(w.Expected["sT10"]);
            Assert.AreEqual(1, w.Props.Count);
            Assert.AreEqual(2, w.Expected.Count, "the seat, and the stage still being the lobby");
        }

        [Test]
        public void MovingAlsoClearsTheOldSeatExpectingTheActor()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Two, Seats(("sT00", 4)));
            Assert.AreEqual(4, w.Props["sT10"]);
            Assert.IsNull(w.Props["sT00"]);
            Assert.IsNull(w.Expected["sT10"]);
            Assert.AreEqual(4, w.Expected["sT00"]);
        }

        [Test]
        public void ASeatTakenBySomeoneElseIsRefused()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Two, Seats(("sT10", 6)));
            Assert.AreSame(SeatWrite.None, w);
            Assert.IsTrue(w.IsNone);
        }

        [Test]
        public void YourOwnSeatIsRefused()
        {
            var w = LobbySeatRules.TakeSeat(4, "sT10", Two, Seats(("sT10", 4)));
            Assert.AreSame(SeatWrite.None, w);
        }

        [Test]
        public void LeavingClearsTheSeatExpectingTheActor()
        {
            var w = LobbySeatRules.LeaveSeat(4, Two, Seats(("sT01", 4)));
            Assert.IsNull(w.Props["sT01"]);
            Assert.AreEqual(4, w.Expected["sT01"]);
        }

        [Test]
        public void EverySeatWriteExpectsTheStageStillToBeTheLobby()
        {
            // Seats change only before Start: once the stage is 1 the room refuses any seat write (Task 3 review).
            var take = LobbySeatRules.TakeSeat(4, "sT10", Two, Seats());
            Assert.AreEqual(0, take.Expected[LobbyKeys.Stage]);
            var move = LobbySeatRules.TakeSeat(4, "sT10", Two, Seats(("sT00", 4)));
            Assert.AreEqual(0, move.Expected[LobbyKeys.Stage]);
            var leave = LobbySeatRules.LeaveSeat(4, Two, Seats(("sT01", 4)));
            Assert.AreEqual(0, leave.Expected[LobbyKeys.Stage]);
            Assert.IsFalse(take.Props.ContainsKey(LobbyKeys.Stage), "the expectation is not a write");
        }

        [Test]
        public void ClearingSeatsExpectsEachSeatToStillBeThatActorsAndTheStageToBeTheLobby()
        {
            var w = LobbySeatRules.ClearSeats(Seats(("sT00", 4), ("sS0", 9)));
            Assert.IsNull(w.Props["sT00"]);
            Assert.IsNull(w.Props["sS0"]);
            Assert.AreEqual(2, w.Props.Count);
            Assert.AreEqual(4, w.Expected["sT00"]);
            Assert.AreEqual(9, w.Expected["sS0"]);
            Assert.AreEqual(0, w.Expected[LobbyKeys.Stage]);
        }

        [Test]
        public void ClearingNoSeatsIsNone()
        {
            Assert.AreSame(SeatWrite.None, LobbySeatRules.ClearSeats(Seats()));
        }

        // ---- Start (lobby Task 4) ----

        [Test]
        public void StartWritesTheAutoFilledSeatsAndMovesTheStageToWarmup()
        {
            // team 0 has one player, teams 1 and 2 none: No role actors 7 and 8 go to team 1 and team 2
            var seats = Seats(("sT00", 3));
            var w = LobbySeatRules.StartWrite(Three, seats, new[] { 7, 8 });
            Assert.IsFalse(w.IsNone);
            Assert.AreEqual(7, w.Props["sT10"]);
            Assert.AreEqual(8, w.Props["sT20"]);
            Assert.AreEqual(1, w.Props[LobbyKeys.Stage]);
            Assert.AreEqual(3, w.Props.Count);
        }

        [Test]
        public void StartExpectsEverySeatOfTheLayoutAtItsCurrentValueAndTheStageToBeTheLobby()
        {
            var seats = Seats(("sT00", 3), ("sS0", 9));
            var w = LobbySeatRules.StartWrite(Three, seats, new[] { 7 });
            foreach (string key in LobbySeatRules.AllSeatKeys(Three))
            {
                Assert.IsTrue(w.Expected.ContainsKey(key), key + " is expected");
                if (key == "sT00") Assert.AreEqual(3, w.Expected[key]);
                else if (key == "sS0") Assert.AreEqual(9, w.Expected[key]);
                else Assert.IsNull(w.Expected[key], key + " is empty");
            }
            Assert.AreEqual(0, w.Expected[LobbyKeys.Stage]);
            Assert.AreEqual(LobbySeatRules.TotalSeats(Three) + 1, w.Expected.Count);
        }

        [Test]
        public void StartWithNobodyInNoRoleWritesOnlyTheStage()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2), ("sT20", 3));
            var w = LobbySeatRules.StartWrite(Three, seats, new int[0]);
            Assert.AreEqual(1, w.Props.Count);
            Assert.AreEqual(1, w.Props[LobbyKeys.Stage]);
        }

        [Test]
        public void StartSortsTheNoRolePlayersAndNeverTouchesTakenSeats()
        {
            var seats = Seats(("sT00", 1));
            var w = LobbySeatRules.StartWrite(Three, seats, new[] { 9, 5 });
            Assert.AreEqual(5, w.Props["sT10"], "the lower actor is placed first");
            Assert.AreEqual(9, w.Props["sT20"]);
            Assert.IsFalse(w.Props.ContainsKey("sT00"));
        }

        [Test]
        public void ASeatKeyNamesItsTeamOrSpectating()
        {
            Assert.IsTrue(LobbySeatRules.TryTeamOfSeat("sT00", out int t0)); Assert.AreEqual(0, t0);
            Assert.IsTrue(LobbySeatRules.TryTeamOfSeat("sT21", out int t2)); Assert.AreEqual(2, t2);
            Assert.IsFalse(LobbySeatRules.TryTeamOfSeat("sS0", out _));
            Assert.IsFalse(LobbySeatRules.TryTeamOfSeat(null, out _));
            Assert.IsFalse(LobbySeatRules.TryTeamOfSeat("lS", out _));
            Assert.IsTrue(LobbySeatRules.IsSpectatorSeat("sS1"));
            Assert.IsFalse(LobbySeatRules.IsSpectatorSeat("sT01"));
            Assert.IsFalse(LobbySeatRules.IsSpectatorSeat(null));
        }

        [Test]
        public void LeavingWithoutASeatIsNone()
        {
            Assert.AreSame(SeatWrite.None, LobbySeatRules.LeaveSeat(4, Two, Seats(("sT01", 5))));
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
        public void StartBlockReasonIsNullWhenTheGameMayStart()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2));
            Assert.IsNull(LobbySeatRules.StartBlockReason(Three, seats, new[] { 10 }), "No role fills the last empty team");
            Assert.IsNull(LobbySeatRules.StartBlockReason(Three, Seats(("sT00", 1), ("sT10", 2), ("sT20", 3)), new int[0]));
        }

        [Test]
        public void StartBlockReasonNamesTheTeamThatWouldStayEmpty()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2));
            Assert.AreEqual(2, LobbySeatRules.StartBlockReason(Three, seats, new int[0]));
            Assert.AreEqual(0, LobbySeatRules.StartBlockReason(Three, Seats(("sT10", 2), ("sT20", 3)), new int[0]));
            Assert.AreEqual(1, LobbySeatRules.StartBlockReason(Three, Seats(("sT00", 1), ("sT20", 3)), new int[0]));
        }

        [Test]
        public void StartBlockReasonNamesTheFirstEmptyTeamWhenSeveralStayEmpty()
        {
            Assert.AreEqual(0, LobbySeatRules.StartBlockReason(Three, Seats(), new int[0]));
            Assert.AreEqual(1, LobbySeatRules.StartBlockReason(Three, Seats(("sT00", 1)), new int[0]));
        }

        [Test]
        public void StartBlockReasonOnTwoTeamsNeverNamesTeamTwo()
        {
            var seats = Seats(("sT00", 1), ("sT10", 2));
            Assert.IsNull(LobbySeatRules.StartBlockReason(Two, seats, new int[0]));
            Assert.AreEqual(1, LobbySeatRules.StartBlockReason(Two, Seats(("sT00", 1)), new int[0]));
        }

        [Test]
        public void StartBlockReasonAgreesWithMayStartGame()
        {
            var cases = new[]
            {
                Seats(), Seats(("sT00", 1)), Seats(("sT00", 1), ("sT10", 2)), Seats(("sT00", 1), ("sT10", 2), ("sT20", 3)), Seats(("sS0", 5)),
            };
            foreach (var seats in cases)
                foreach (var noRole in new[] { new int[0], new[] { 9 }, new[] { 9, 10 }, new[] { 9, 10, 11 } })
                    Assert.AreEqual(LobbySeatRules.MayStartGame(Three, seats, noRole), LobbySeatRules.StartBlockReason(Three, seats, noRole) == null);
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
            // team 0 has 1 filled, team 1 has none: "first team with room" would say team 0 (sT01), the emptiest is team 1 (sT10)
            var seats = Seats(("sT00", 1));
            Assert.AreEqual("sT10", LobbySeatRules.PlaceLateJoiner(Three, seats, new[] { 0, 1 }));
        }

        [Test]
        public void LateJoinerPicksALaterTeamWhenItIsTheEmptiest()
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

        [Test]
        public void RoomPropertiesThatLookLikeSeatsNeverReadAsSeats()
        {
            // lS (stage) = 2 and mMode = 3 live in the same dictionary: actors 2 and 3 are still in No role
            var seats = Seats(("sT00", 1), ("lS", 2), ("mMode", 3));
            Assert.IsNull(LobbySeatRules.SeatOf(2, Two, seats));
            Assert.IsNull(LobbySeatRules.SeatOf(3, Two, seats));
            Assert.AreEqual("sT00", LobbySeatRules.SeatOf(1, Two, seats));
        }

        [Test]
        public void TakeSeatNeverTouchesANonSeatKey()
        {
            var seats = Seats(("lS", 2), ("mMode", 3));
            var w = LobbySeatRules.TakeSeat(2, "sT10", Two, seats);
            Assert.AreEqual(1, w.Props.Count);
            Assert.IsFalse(w.Props.ContainsKey("lS"));
            Assert.AreEqual(0, w.Expected["lS"], "only the stage expectation (0), never the room's stage 2 read as a seat");
            Assert.AreEqual(2, w.Expected.Count);
            Assert.AreSame(SeatWrite.None, LobbySeatRules.LeaveSeat(3, Two, seats));
        }

        [Test]
        public void AutoFillSortsItsOwnCopyOfTheNoRoleActors()
        {
            var ascending = LobbySeatRules.AutoFill(Three, Seats(), new[] { 10, 11, 12, 13 });
            var descendingInput = new[] { 13, 12, 11, 10 };
            var descending = LobbySeatRules.AutoFill(Three, Seats(), descendingInput);
            CollectionAssert.AreEquivalent(ascending, descending);
            Assert.AreEqual(13, descendingInput[0], "the caller's list must not be reordered");
        }

        [Test]
        public void FillTextOfAnEmptyLobby()
        {
            // Three: 3 teams x 2 seats = 6 team seats, 2 spectator seats
            Assert.AreEqual("0/6+0", LobbySeatRules.FillText(Three, Seats()));
        }

        [Test]
        public void FillTextCountsAFullTeamSeatSet()
        {
            var seats = Seats(("sT00", 1), ("sT01", 2), ("sT10", 3), ("sT11", 4), ("sT20", 5), ("sT21", 6));
            Assert.AreEqual("6/6+0", LobbySeatRules.FillText(Three, seats));
        }

        [Test]
        public void FillTextCountsSpectatorsSeparately()
        {
            var seats = Seats(("sT00", 1), ("sS0", 2), ("sS1", 3));
            Assert.AreEqual("1/6+2", LobbySeatRules.FillText(Three, seats));
        }

        [Test]
        public void FillTextIgnoresNonSeatKeysAndEmptySeats()
        {
            var seats = Seats(("sT00", 1), ("sT01", 0), ("lS", 2), ("mMode", 3), ("sT50", 4));
            Assert.AreEqual("1/6+0", LobbySeatRules.FillText(Three, seats));
        }

        [Test]
        public void FillTextRoundTripsThroughTryParseFill()
        {
            Assert.IsTrue(LobbySeatRules.TryParseFill("4/9+2", out int team, out int teamSeats, out int spec));
            Assert.AreEqual(4, team);
            Assert.AreEqual(9, teamSeats);
            Assert.AreEqual(2, spec);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("4/9")]
        [TestCase("a/b+c")]
        [TestCase("4-9+2")]
        public void TryParseFillRefusesAnythingElse(string text)
        {
            Assert.IsFalse(LobbySeatRules.TryParseFill(text, out _, out _, out _));
        }

        // ---- late joiners and the sweep after Start (lobby Task 7) ----

        [TestCase(1)]
        [TestCase(2)]
        public void LateJoinWriteExpectsTheSeatEmptyAndTheStageItSaw(int stage)
        {
            var w = LobbySeatRules.LateJoinWrite(7, "sT10", stage);
            Assert.AreEqual(7, w.Props["sT10"]);
            Assert.AreEqual(1, w.Props.Count, "only the seat is written");
            Assert.IsTrue(w.Expected.ContainsKey("sT10"));
            Assert.IsNull(w.Expected["sT10"], "expected empty");
            Assert.AreEqual(stage, w.Expected[LobbyKeys.Stage], "the stage must not have moved");
            Assert.AreEqual(2, w.Expected.Count);
        }

        private static Dictionary<int, SeatHolderPresence> Presence(params (int actor, SeatHolderPresence state)[] entries)
        {
            var d = new Dictionary<int, SeatHolderPresence>();
            foreach (var e in entries) d[e.actor] = e.state;
            return d;
        }

        [Test]
        public void AnInactiveHolderAtStageZeroIsCleared()
        {
            var writes = LobbySeatRules.SweepWrites(Seats(("sT00", 4)), Presence((4, SeatHolderPresence.Inactive)), 0);
            Assert.AreEqual(1, writes.Count);
            Assert.IsNull(writes[0].Props["sT00"]);
            Assert.AreEqual(4, writes[0].Expected["sT00"]);
            Assert.AreEqual(0, writes[0].Expected[LobbyKeys.Stage]);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void AnInactiveHolderAfterStartKeepsTheSeat(int stage)
        {
            var writes = LobbySeatRules.SweepWrites(Seats(("sT00", 4), ("sS0", 5)),
                Presence((4, SeatHolderPresence.Inactive), (5, SeatHolderPresence.Inactive)), stage);
            Assert.AreEqual(0, writes.Count);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AHolderWhoLeftForGoodIsClearedAtAnyStageWithoutAStageExpectation(int stage)
        {
            var writes = LobbySeatRules.SweepWrites(Seats(("sT01", 6)), Presence((6, SeatHolderPresence.LeftForGood)), stage);
            Assert.AreEqual(1, writes.Count);
            Assert.IsNull(writes[0].Props["sT01"]);
            Assert.AreEqual(6, writes[0].Expected["sT01"], "expected to still be that actor");
            Assert.IsFalse(writes[0].Expected.ContainsKey(LobbyKeys.Stage), "no stage expectation for a quit");
        }

        [Test]
        public void AHolderMissingFromThePresenceListLeftForGood()
        {
            var writes = LobbySeatRules.SweepWrites(Seats(("sT01", 6)), Presence(), 1);
            Assert.AreEqual(1, writes.Count);
            Assert.IsNull(writes[0].Props["sT01"]);
        }

        [Test]
        public void APresentHolderIsNeverCleared()
        {
            var writes = LobbySeatRules.SweepWrites(Seats(("sT00", 4)), Presence((4, SeatHolderPresence.Present)), 0);
            Assert.AreEqual(0, writes.Count);
        }

        [Test]
        public void AQuitAndADropAtStageZeroAreTwoWrites()
        {
            var writes = LobbySeatRules.SweepWrites(Seats(("sT00", 4), ("sT10", 5), ("sT11", 6)),
                Presence((4, SeatHolderPresence.Inactive), (5, SeatHolderPresence.LeftForGood), (6, SeatHolderPresence.Present)), 0);
            Assert.AreEqual(2, writes.Count);
            int forGood = writes[0].Expected.ContainsKey(LobbyKeys.Stage) ? 1 : 0;
            var quit = writes[forGood];
            var drop = writes[1 - forGood];
            Assert.IsTrue(quit.Props.ContainsKey("sT10") && quit.Props.Count == 1 && !quit.Expected.ContainsKey(LobbyKeys.Stage));
            Assert.IsTrue(drop.Props.ContainsKey("sT00") && drop.Props.Count == 1 && drop.Expected[LobbyKeys.Stage].Equals(0));
        }

        [Test]
        public void TeamsForALateJoinerAreTheFixedOnesOnceTheMatchFixedThemElseTheLayouts()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, LobbySeatRules.TeamsForLateJoin(Three, null, null));
            CollectionAssert.AreEqual(new[] { 0, 1 }, LobbySeatRules.TeamsForLateJoin(Three, new[] { 0, 1 }, null));
        }

        [Test]
        public void ALateJoinerInAThreeTeamLayoutNeverGetsTeamTwoWhenTwoTeamsPlay()
        {
            // an empty team 2 and fuller teams 0 and 1: the emptiest playing team still wins
            var seats = Seats(("sT00", 1), ("sT10", 2));
            string seat = LobbySeatRules.PlaceLateJoiner(Three, seats, LobbySeatRules.TeamsForLateJoin(Three, new[] { 0, 1 }, null));
            Assert.IsTrue(LobbySeatRules.TryTeamOfSeat(seat, out int team));
            Assert.AreNotEqual(2, team);
        }

        [Test]
        public void AKnockedOutTeamIsNeverOfferedToALateJoiner()
        {
            CollectionAssert.AreEqual(new[] { 0, 2 }, LobbySeatRules.TeamsForLateJoin(Three, new[] { 0, 1, 2 }, new[] { 1 }));
            // before the match fixed its teams (the warm-up) nobody can be knocked out, but a stale list still never offers its teams
            CollectionAssert.AreEqual(new[] { 1, 2 }, LobbySeatRules.TeamsForLateJoin(Three, null, new[] { 0 }));
        }

        [Test]
        public void AKnockedOutTeamThatIsTheEmptiestIsSkippedByThePlacement()
        {
            // team 1 is knocked out and empty, teams 0 and 2 have a player each: the joiner must sit on 0 or 2, never on 1
            var seats = Seats(("sT00", 1), ("sT20", 3));
            string seat = LobbySeatRules.PlaceLateJoiner(Three, seats, LobbySeatRules.TeamsForLateJoin(Three, new[] { 0, 1, 2 }, new[] { 1 }));
            Assert.IsTrue(LobbySeatRules.TryTeamOfSeat(seat, out int team));
            Assert.AreNotEqual(1, team);
        }

        // ---- a seatless player in a running game (Task 7 review)

        [TestCase(true, 1, true, null, true)]    // in the room, started, a layout, no seat: needs one
        [TestCase(true, 1, false, null, false)]  // the layout has not arrived yet: nobody can say they have no seat
        [TestCase(true, 0, true, null, false)]   // still the lobby
        [TestCase(true, 1, true, "sT00", false)] // has a seat
        [TestCase(false, 1, true, null, false)]  // not in a room
        public void AGameRunningWithoutMySeatNeedsTheLayoutToo(bool inRoom, int stage, bool hasLayout, string seat, bool expected) =>
            Assert.AreEqual(expected, LobbySeatRules.GameRunningWithoutSeat(inRoom, stage, hasLayout, seat));

        // ---- the same write is not sent twice

        [Test]
        public void TheSameWriteSentTwiceInOneInstantGoesOnce()
        {
            var gate = new RepeatWriteGate(1f);
            Assert.IsTrue(gate.ShouldSend("sT00=3", 10f));
            Assert.IsFalse(gate.ShouldSend("sT00=3", 10f));
            Assert.IsFalse(gate.ShouldSend("sT00=3", 10.9f));
        }

        [Test]
        public void ADifferentWriteOrALaterRetryGoesThrough()
        {
            var gate = new RepeatWriteGate(1f);
            Assert.IsTrue(gate.ShouldSend("sT00=3", 10f));
            Assert.IsTrue(gate.ShouldSend("sT10=4", 10f), "another write");
            Assert.IsTrue(gate.ShouldSend("sT10=4", 11.5f), "the same one after the window: a lost write is retried");
        }

        [Test]
        public void AWriteSignatureNamesEveryKeyAndValueInOrder()
        {
            var a = new SeatWrite(new Dictionary<string, object> { { "sT10", null }, { "sT00", null } }, new Dictionary<string, object> { { "sT10", 4 }, { "sT00", 3 } });
            var b = new SeatWrite(new Dictionary<string, object> { { "sT00", null }, { "sT10", null } }, new Dictionary<string, object> { { "sT00", 3 }, { "sT10", 4 } });
            var c = new SeatWrite(new Dictionary<string, object> { { "sT00", null } }, new Dictionary<string, object> { { "sT00", 3 } });
            Assert.AreEqual(a.Signature(), b.Signature(), "order does not matter");
            Assert.AreNotEqual(a.Signature(), c.Signature());
        }
    }
}
