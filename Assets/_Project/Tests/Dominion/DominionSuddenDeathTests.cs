using System.Collections.Generic;
using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Lobby;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 8, sudden death: no respawns, who plays and who waits, damage only outside the circle, what the master writes (win, replay, nothing),
    /// where the circle is centred. Every number is a literal made up for the test, never the asset's. The components (PlayerLifecycle, DominionDirector,
    /// SuddenDeathZone) call these functions; the recorder rows in the report name the proof of each call site.
    /// </summary>
    public class DominionSuddenDeathTests
    {
        // ---- no respawns in sudden death

        [Test] public void ADeathInASuddenDeathOfALiveDominionMatchIsForGood() => Assert.IsFalse(DominionRules.RespawnAllowed(true, DominionStage.SuddenDeath));
        [Test] public void ADeathAfterTheMatchIsOverNeverRespawns() => Assert.IsFalse(DominionRules.RespawnAllowed(true, DominionStage.Over));

        [Test] public void ADeathInARoundOrABreakStillRespawns()
        {
            Assert.IsTrue(DominionRules.RespawnAllowed(true, DominionStage.Round));
            Assert.IsTrue(DominionRules.RespawnAllowed(true, DominionStage.Break));
        }

        [Test] public void OutsideALiveDominionMatchTheRespawnRuleIsNotAsked()
        {
            Assert.IsTrue(DominionRules.RespawnAllowed(false, DominionStage.SuddenDeath), "Conquest or the warm-up never has a Dominion stage that matters");
            Assert.IsTrue(DominionRules.RespawnAllowed(false, DominionStage.None));
        }

        // ---- who plays

        [Test] public void OnlyTheTiedTeamsPlaySuddenDeath()
        {
            var tied = new[] { 0, 1 };
            Assert.IsTrue(SuddenDeathRules.TeamPlays(0, tied));
            Assert.IsTrue(SuddenDeathRules.TeamPlays(1, tied));
            Assert.IsFalse(SuddenDeathRules.TeamPlays(2, tied), "a team that is not tied waits dead");
            Assert.IsFalse(SuddenDeathRules.TeamPlays(-1, tied));
            Assert.IsFalse(SuddenDeathRules.TeamPlays(0, null));
        }

        // ---- the verdict has to hold still before the master writes it (deaths a moment apart count as the same instant)

        [Test] public void AVerdictIsNotWrittenTheMomentItIsFirstSeen() =>
            Assert.IsFalse(new SuddenDeathVerdictSettle().Settled("won:1", 10f, 0.5f));

        [Test] public void AVerdictThatHoldsForTheSettleTimeIsWritten()
        {
            var settle = new SuddenDeathVerdictSettle();
            settle.Settled("won:1", 10f, 0.5f);
            Assert.IsFalse(settle.Settled("won:1", 10.25f, 0.5f), "only a quarter second");
            Assert.IsTrue(settle.Settled("won:1", 10.5f, 0.5f), "exactly the settle time");
        }

        [Test] public void ADifferentVerdictStartsTheWaitAgain()
        {
            var settle = new SuddenDeathVerdictSettle();
            settle.Settled("won:1", 10f, 0.5f);
            Assert.IsFalse(settle.Settled("replay", 10.25f, 0.5f), "the second player fell too: replay, not the win");
            Assert.IsFalse(settle.Settled("replay", 10.5f, 0.5f), "the replay has only held a quarter second");
            Assert.IsTrue(settle.Settled("replay", 10.75f, 0.5f));
        }

        [Test] public void NothingToWriteForgetsTheVerdict()
        {
            var settle = new SuddenDeathVerdictSettle();
            settle.Settled("won:1", 10f, 0.5f);
            Assert.IsFalse(settle.Settled(null, 10.25f, 0.5f));
            Assert.IsFalse(settle.Settled("won:1", 10.6f, 0.5f), "seen again from scratch, not continuously since 10");
        }

        [Test] public void AResetForgetsTheVerdict()
        {
            var settle = new SuddenDeathVerdictSettle();
            settle.Settled("replay", 10f, 0.5f);
            settle.Reset();
            Assert.IsFalse(settle.Settled("replay", 10.6f, 0.5f));
        }

        [Test] public void OnlySuddenDeathVerdictsAreSettledOn()
        {
            DominionRoomState room = SuddenDeathRoom(100000, new[] { 1, 1, 0 });
            Assert.AreEqual("sudden death won:1", DominionRoomWrites.SuddenDeathVerdictKey(Judge(room, 160000, new[] { 0, 2, 0 })));
            Assert.AreEqual("sudden death replay", DominionRoomWrites.SuddenDeathVerdictKey(Judge(room, 160000, new[] { 0, 0, 3 }, new[] { 5000, 5000, 0 })));
            Assert.IsNull(DominionRoomWrites.SuddenDeathVerdictKey(null));
            var roundEnd = new DominionRoomState { HasRound = true, Round = 1, Stage = DominionStage.Round, EndMs = 105000, Points = new[] { 1, 0, 0 }, Wins = new[] { 0, 0, 0 }, Winner = -1 };
            Assert.IsNull(DominionRoomWrites.SuddenDeathVerdictKey(DominionRoomWrites.Next(true, true, 105000, roundEnd, Cfg, Three, new[] { 3, 3, 3 })), "a round's end is never held back");
        }

        // ---- who counts as alive (a player who never died has no flag in the room)

        [Test] public void APlayerWithNoAliveFlagYetIsAlive() => Assert.IsTrue(SuddenDeathRules.CountsAsAlive(false, false));
        [Test] public void APlayerWhoseFlagSaysAliveIsAlive() => Assert.IsTrue(SuddenDeathRules.CountsAsAlive(true, true));
        [Test] public void OnlyAnExplicitDeadFlagIsDead() => Assert.IsFalse(SuddenDeathRules.CountsAsAlive(true, false));

        // ---- damage outside the circle (10 per second, a 0.5 s slice, circle radius 20 around the origin)

        private static float Damage(Vector2 at, float radius = 20f, bool on = true, bool plays = true, bool alive = true, float seconds = 0.5f) =>
            SuddenDeathRules.DamageAt(on, plays, alive, at, Vector2.zero, radius, 10f, seconds);

        [Test] public void OutsideTheCircleCostsTheRateTimesTheSeconds() => Assert.AreEqual(5f, Damage(new Vector2(30f, 0f)), 1e-4f);
        [Test] public void InsideTheCircleCostsNothing() => Assert.AreEqual(0f, Damage(new Vector2(10f, 10f)));
        [Test] public void ExactlyOnTheEdgeIsInside() => Assert.AreEqual(0f, Damage(new Vector2(20f, 0f)));
        [Test] public void OutsideCostsNothingWhenSuddenDeathIsNotOn() => Assert.AreEqual(0f, Damage(new Vector2(30f, 0f), on: false));
        [Test] public void ATeamThatIsNotPlayingTakesNoCircleDamage() => Assert.AreEqual(0f, Damage(new Vector2(30f, 0f), plays: false));
        [Test] public void ADeadPlayerTakesNoCircleDamage() => Assert.AreEqual(0f, Damage(new Vector2(30f, 0f), alive: false));
        [Test] public void NoTimePassedCostsNothing() => Assert.AreEqual(0f, Damage(new Vector2(30f, 0f), seconds: 0f));

        [Test] public void AShrinkingCircleReachesAPlayerWhoStoodStill()
        {
            var at = new Vector2(15f, 0f);
            float early = Damage(at, SuddenDeathRules.Radius(10000, 10000, 20f, 20f, 4f));
            float late = Damage(at, SuddenDeathRules.Radius(10000, 25000, 20f, 20f, 4f));
            Assert.AreEqual(0f, early, "inside while the circle is full size");
            Assert.AreEqual(5f, late, 1e-4f, "outside once it has shrunk past the player");
        }

        // ---- where the circle is centred

        [Test] public void TheCentreZoneIsTheCentreWhenTheMapHasOne()
        {
            Assert.IsTrue(SuddenDeathRules.TryCentre(new[] { new Vector3(3f, 7f, 4f) }, new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 20f) }, out Vector2 c, out float y));
            Assert.AreEqual(new Vector2(3f, 4f), c);
            Assert.AreEqual(7f, y, "the floor is the centre zone's");
        }

        [Test] public void WithoutACentreZoneTheCentreIsHalfwayBetweenTheTwoScoringZones()
        {
            Assert.IsTrue(SuddenDeathRules.TryCentre(new Vector3[0], new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 20f) }, out Vector2 c, out _));
            Assert.AreEqual(new Vector2(5f, 10f), c);
            Assert.IsTrue(SuddenDeathRules.TryCentre(null, new[] { new Vector3(-4f, 0f, 2f), new Vector3(4f, 0f, 6f) }, out Vector2 d, out _));
            Assert.AreEqual(new Vector2(0f, 4f), d);
        }

        [Test] public void TheFloorOfATwoZoneCircleIsBetweenTheTwoZones()
        {
            Assert.IsTrue(SuddenDeathRules.TryCentre(null, new[] { new Vector3(0f, 2f, 0f), new Vector3(10f, 6f, 20f) }, out _, out float y));
            Assert.AreEqual(4f, y, 1e-4f, "the average height, not whichever zone happened to come first");
        }

        [Test] public void WithNoZonesAtAllTheCircleCannotBePlaced()
        {
            Assert.IsFalse(SuddenDeathRules.TryCentre(null, null, out _, out _));
            Assert.IsFalse(SuddenDeathRules.TryCentre(new Vector3[0], new Vector3[0], out _, out _));
        }

        [Test] public void OnlyAScoringZoneInPlayShapesTheCircle()
        {
            Assert.IsTrue(SuddenDeathRules.ZoneShapesTheCircle(tier: 3, isCapital: false, outOfPlay: false));
            Assert.IsTrue(SuddenDeathRules.ZoneShapesTheCircle(tier: 4, isCapital: false, outOfPlay: false), "the centre");
            Assert.IsFalse(SuddenDeathRules.ZoneShapesTheCircle(tier: 1, isCapital: false, outOfPlay: false), "a Tier 1 zone is a spawn");
            Assert.IsFalse(SuddenDeathRules.ZoneShapesTheCircle(tier: 3, isCapital: true, outOfPlay: false), "a capital, whatever its tier");
            Assert.IsFalse(SuddenDeathRules.ZoneShapesTheCircle(tier: 3, isCapital: false, outOfPlay: true), "cut off the map");
            Assert.IsFalse(SuddenDeathRules.ZoneShapesTheCircle(tier: 0, isCapital: false, outOfPlay: false), "a tower that has not registered yet");
        }

        // ---- the circle's size follows the match size

        [Test] public void TheCircleSizesFollowTheMatchSize()
        {
            Assert.AreEqual(30f, SuddenDeathRules.StartRadiusFor(2, 30f, 60f));
            Assert.AreEqual(60f, SuddenDeathRules.StartRadiusFor(3, 30f, 60f));
            Assert.AreEqual(4f, SuddenDeathRules.FinalRadiusFor(2, 4f, 8f));
            Assert.AreEqual(8f, SuddenDeathRules.FinalRadiusFor(3, 4f, 8f));
        }

        // ---- timing: when the master may judge, and the seconds the circle has left

        [Test] public void TheMasterWaitsAfterTheStartBeforeJudging()
        {
            Assert.IsFalse(SuddenDeathRules.MayEvaluate(100000, 90000), "before the circle starts");
            Assert.IsFalse(SuddenDeathRules.MayEvaluate(100000, 100000), "at the start the flags have not come back yet");
            Assert.IsTrue(SuddenDeathRules.MayEvaluate(100000, 160000), "well after it");
        }

        [Test] public void TheJudgingTimeSurvivesTheIntWrap()
        {
            int start = unchecked(int.MaxValue - 500);
            Assert.IsFalse(SuddenDeathRules.MayEvaluate(start, start));
            Assert.IsTrue(SuddenDeathRules.MayEvaluate(start, unchecked(start + 60000)));
        }

        [Test] public void TheSecondsLeftCountDownToTheCircleStopping()
        {
            Assert.AreEqual(25f, SuddenDeathRules.SecondsUntilStopped(10000, 5000, 20f), 1e-3f, "5 s of get-ready plus the 20 s shrink");
            Assert.AreEqual(10f, SuddenDeathRules.SecondsUntilStopped(10000, 20000, 20f), 1e-3f);
            Assert.AreEqual(0f, SuddenDeathRules.SecondsUntilStopped(10000, 30000, 20f));
            Assert.AreEqual(0f, SuddenDeathRules.SecondsUntilStopped(10000, 90000, 20f));
        }

        // ---- what the master writes

        private static readonly DominionFlowNumbers Cfg = new DominionFlowNumbers
        {
            RoundsToWin = 2, MaxRounds = 3, RoundSeconds = 100f, BreakSeconds = 10f, SuddenDeathCountdownSeconds = 4f, SameInstantToleranceMs = 50,
        };
        private static readonly int[] Three = { 0, 1, 2 };

        private static DominionRoomState SuddenDeathRoom(int sdMs, int[] wins) =>
            new DominionRoomState
            {
                HasRound = true, Round = 3, Stage = DominionStage.SuddenDeath, EndMs = 0, SuddenDeathMs = sdMs,
                Points = new[] { 0, 0, 0 }, Wins = wins, Winner = -1,
            };

        /// <summary>A tally with these living counts; a team with none alive has one player who fell at the given stamp (teams with a stamp of 0 and living
        /// players have no fallen player, a team with none alive and no stamp given falls at 1000).</summary>
        private static SuddenDeathRules.Tally TallyOf(int[] alive, int[] fellAt = null)
        {
            var tally = new SuddenDeathRules.Tally(3);
            for (int team = 0; team < alive.Length; team++)
            {
                for (int i = 0; i < alive[team]; i++)
                    tally.Add(new SuddenDeathRules.Player { Team = team, Counts = true, HasAliveFlag = true, AliveFlag = true });
                if (alive[team] == 0)
                    tally.Add(new SuddenDeathRules.Player { Team = team, Counts = true, HasAliveFlag = true, AliveFlag = false, HasDeathStamp = true, DeathStampMs = fellAt != null ? fellAt[team] : 1000 });
            }
            return tally;
        }

        private static DominionWrite Judge(DominionRoomState room, int now, int[] alive, int[] fellAt = null) =>
            DominionRoomWrites.Next(true, true, now, room, Cfg, Three, new[] { 3, 3, 3 }, TallyOf(alive, fellAt));

        [Test] public void SuddenDeathStartsWithItsCircleAfterTheGetReadyCountdown()
        {
            var round3 = new DominionRoomState
            {
                HasRound = true, Round = 3, Stage = DominionStage.Round, EndMs = 500000,
                Points = new[] { 5, 5, 0 }, Wins = new[] { 1, 1, 0 }, Winner = -1,
            };
            DominionWrite w = DominionRoomWrites.Next(true, true, 500000, round3, Cfg, Three, new[] { 3, 3, 3 });
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(504000, w.Props[DominionKeys.SuddenDeathStart], "now plus the countdown");
        }

        [Test] public void TheLastTeamWithAnyoneAliveWinsAfterTheJudgingBeat()
        {
            DominionRoomState room = SuddenDeathRoom(100000, new[] { 1, 1, 0 });
            DominionWrite w = Judge(room, 160000, new[] { 0, 2, 0 });
            Assert.IsNotNull(w);
            Assert.AreEqual((int)DominionStage.Over, w.Props[DominionKeys.Stage]);
            Assert.AreEqual(1, w.Props[DominionKeys.Winner]);
            Assert.AreEqual((int)DominionStage.SuddenDeath, w.Expected[DominionKeys.Stage], "refused if the stage already moved");
            Assert.AreEqual(100000, w.Expected[DominionKeys.SuddenDeathStart], "refused if a replay already restarted it");
        }

        [Test] public void ATeamThatIsNotTiedNeverWinsSuddenDeathHoweverManyLive()
        {
            DominionWrite w = Judge(SuddenDeathRoom(100000, new[] { 1, 1, 0 }), 160000, new[] { 1, 0, 5 });
            Assert.AreEqual(0, w.Props[DominionKeys.Winner]);
        }

        [Test] public void NobodyLeftStartsSuddenDeathOverWithANewCircleStart()
        {
            DominionRoomState room = SuddenDeathRoom(100000, new[] { 1, 1, 0 });
            DominionWrite w = Judge(room, 160000, new[] { 0, 0, 3 }, new[] { 5000, 5000, 0 });
            Assert.IsNotNull(w);
            Assert.AreEqual(164000, w.Props[DominionKeys.SuddenDeathStart], "now plus the countdown: the circle is full size again");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Stage), "still sudden death");
            Assert.IsFalse(w.Props.ContainsKey(DominionKeys.Winner));
            Assert.AreEqual(100000, w.Expected[DominionKeys.SuddenDeathStart], "two masters cannot both replay");
        }

        [Test] public void WhileTwoTeamsLiveNothingIsWritten() => Assert.IsNull(Judge(SuddenDeathRoom(100000, new[] { 1, 1, 0 }), 160000, new[] { 1, 1, 0 }));
        [Test] public void NothingIsJudgedBeforeTheBeatOrWithoutTheFlags()
        {
            DominionRoomState room = SuddenDeathRoom(100000, new[] { 1, 1, 0 });
            Assert.IsNull(Judge(room, 100000, new[] { 0, 0, 0 }), "the flags are still catching up");
            Assert.IsNull(DominionRoomWrites.Next(true, true, 160000, room, Cfg, Three, new[] { 3, 3, 3 }, null), "no flags read");
        }

        [Test] public void TheRoomReadsTheCircleStart()
        {
            var props = new Hashtable { { DominionKeys.SuddenDeathStart, 12345 } };
            Assert.AreEqual(12345, DominionRoomState.Read(props).SuddenDeathMs);
            Assert.AreEqual(0, DominionRoomState.Read(new Hashtable()).SuddenDeathMs);
        }

        // ---- the edge every client reacts to

        [Test] public void SuddenDeathStartingOrReplayingIsAnEdge()
        {
            Assert.IsTrue(DominionRoomWrites.IsSuddenDeathStart(DominionStage.Round, 0, DominionStage.SuddenDeath, 5000), "the first start");
            Assert.IsTrue(DominionRoomWrites.IsSuddenDeathStart(DominionStage.SuddenDeath, 5000, DominionStage.SuddenDeath, 9000), "a replay: a new circle start");
        }

        [Test] public void NothingElseIsASuddenDeathEdge()
        {
            Assert.IsFalse(DominionRoomWrites.IsSuddenDeathStart(DominionStage.SuddenDeath, 5000, DominionStage.SuddenDeath, 5000), "same start, another key changed");
            Assert.IsFalse(DominionRoomWrites.IsSuddenDeathStart(DominionStage.SuddenDeath, 5000, DominionStage.Over, 5000));
            Assert.IsFalse(DominionRoomWrites.IsSuddenDeathStart(DominionStage.Round, 0, DominionStage.Break, 0));
            Assert.IsFalse(DominionRoomWrites.IsSuddenDeathStart(DominionStage.Round, 0, DominionStage.SuddenDeath, 0), "no circle start written yet");
        }

        // ---- a late joiner (A4)

        private static readonly SeatLayout Two = new SeatLayout(new[] { 0, 1 }, 2, 1);

        [Test] public void OnlySuddenDeathSeatsALateJoinerAsASpectatorFirst()
        {
            Assert.IsTrue(DominionRules.LateJoinerPrefersSpectatorSeat(DominionStage.SuddenDeath));
            Assert.IsFalse(DominionRules.LateJoinerPrefersSpectatorSeat(DominionStage.Round));
            Assert.IsFalse(DominionRules.LateJoinerPrefersSpectatorSeat(DominionStage.Break));
        }

        [Test] public void ALateJoinerInSuddenDeathTakesTheSpectatorSeatWhenOneIsFree()
        {
            var seats = new Dictionary<string, int> { { "sT00", 1 }, { "sT10", 2 } };
            Assert.AreEqual("sS0", LobbySeatRules.PlaceLateJoiner(Two, seats, new[] { 0, 1 }, preferSpectator: true));
            Assert.AreEqual("sT01", LobbySeatRules.PlaceLateJoiner(Two, seats, new[] { 0, 1 }), "an ordinary join takes a team seat first");
        }

        [Test] public void ALateJoinerInSuddenDeathTakesATeamSeatWhenNoSpectatorSeatIsFree()
        {
            var seats = new Dictionary<string, int> { { "sT00", 1 }, { "sT10", 2 }, { "sS0", 3 } };
            Assert.AreEqual("sT01", LobbySeatRules.PlaceLateJoiner(Two, seats, new[] { 0, 1 }, preferSpectator: true));
        }
    }
}
