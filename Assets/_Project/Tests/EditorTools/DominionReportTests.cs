using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// A Dominion match read back from the master's round notes (written by DominionMarkerNotes, so the two can never drift apart), and each map's
    /// own facts for the report: zone names, spawns, the closed corner of a two-team Conquest, and the patch its picture shows.
    /// </summary>
    public class DominionReportTests
    {
        private static readonly int[] TwoTeams = { 0, 1 };
        private static readonly int[] ThreeTeams = { 0, 1, 2 };

        private static DominionRoundTimeline Read(params (double T, string Note)[] notes) => DominionRoundTimeline.From(notes);

        // ---------------------------------------------------------------- rounds

        [Test] public void ARoundIsReadWithItsStartEndWinnerAndPoints()
        {
            DominionRoundTimeline timeline = Read(
                (20.0, DominionMarkerNotes.BreakStart(1)),
                (40.0, DominionMarkerNotes.RoundStart(1)),
                (100.0, DominionMarkerNotes.RoundEnd(1, 1, TwoTeams, new[] { 120, 340 })));
            Assert.AreEqual(1, timeline.Rounds.Count);
            DominionRound round = timeline.Rounds[0];
            Assert.AreEqual(1, round.Number);
            Assert.AreEqual(20.0, round.BreakStartT);
            Assert.AreEqual(40.0, round.StartT);
            Assert.AreEqual(100.0, round.EndT);
            Assert.AreEqual(DominionRoundTimeline.Won, round.Outcome);
            CollectionAssert.AreEqual(new[] { 1 }, round.Winners);
            Assert.AreEqual(120, round.Points[0]);
            Assert.AreEqual(340, round.Points[1]);
        }

        [Test] public void ATiedACutShortAndASharedRoundAreToldApart()
        {
            DominionRoundTimeline timeline = Read(
                (10.0, DominionMarkerNotes.RoundStart(1)),
                (50.0, DominionMarkerNotes.RoundEnd(1, -1, TwoTeams, new[] { 60, 60 })),
                (60.0, DominionMarkerNotes.RoundStart(2)),
                (90.0, DominionMarkerNotes.OvertimeStart(2, TwoTeams)),
                (110.0, DominionMarkerNotes.RoundEndShared(2, TwoTeams, TwoTeams, new[] { 75, 80 })),
                (120.0, DominionMarkerNotes.RoundStart(3)),
                (130.0, DominionMarkerNotes.RoundCutShort(3, TwoTeams, new[] { 5, 0 })));
            Assert.AreEqual(DominionRoundTimeline.Tied, timeline.Rounds[0].Outcome);
            Assert.IsEmpty(timeline.Rounds[0].Winners);
            Assert.AreEqual(DominionRoundTimeline.Shared, timeline.Rounds[1].Outcome);
            CollectionAssert.AreEqual(TwoTeams, timeline.Rounds[1].Winners);
            Assert.AreEqual(90.0, timeline.Rounds[1].OvertimeStartT);
            CollectionAssert.AreEqual(TwoTeams, timeline.Rounds[1].OvertimeTeams);
            Assert.AreEqual(80, timeline.Rounds[1].Points[1]);
            Assert.AreEqual(DominionRoundTimeline.CutShort, timeline.Rounds[2].Outcome);
            Assert.AreEqual(5, timeline.Rounds[2].Points[0]);
        }

        [Test] public void TheMatchsWinnerAndWinsAreRead()
        {
            DominionRoundTimeline timeline = Read((300.0, DominionMarkerNotes.MatchOver(2, ThreeTeams, new[] { 1, 0, 2 })));
            Assert.AreEqual(300.0, timeline.OverT);
            Assert.AreEqual(2, timeline.Winner);
            Assert.AreEqual(1, timeline.Wins[0]);
            Assert.AreEqual(0, timeline.Wins[1]);
            Assert.AreEqual(2, timeline.Wins[2]);
        }

        [Test] public void BountiesAndCentrePayoutsAreRead()
        {
            DominionRoundTimeline timeline = Read(
                (70.0, DominionMarkerNotes.Bounty(1, 0, 45)),
                (80.0, DominionMarkerNotes.CentrePayout(2, 75)),
                (90.0, DominionMarkerNotes.CentrePayout(-1, 75)));
            Assert.AreEqual(1, timeline.Bounties.Count);
            Assert.AreEqual(1, timeline.Bounties[0].Zone);
            Assert.AreEqual(0, timeline.Bounties[0].Team);
            Assert.AreEqual(45, timeline.Bounties[0].Points);
            Assert.AreEqual(2, timeline.CentrePayouts.Count);
            Assert.AreEqual(2, timeline.CentrePayouts[0].Team);
            Assert.AreEqual(75, timeline.CentrePayouts[0].Points);
            Assert.AreEqual(-1, timeline.CentrePayouts[1].Team, "paid nobody");
        }

        [Test] public void TheSameNoteFromTwoMastersCountsOnce()
        {
            string bounty = DominionMarkerNotes.Bounty(0, 1, 30);
            Assert.AreEqual(1, Read((70.0, bounty), (70.0, bounty)).Bounties.Count);
        }

        [Test] public void NotesThatAreNotDominionsAreIgnored()
        {
            DominionRoundTimeline timeline = Read((5.0, "seat taken: sS1"), (6.0, "round 1 start"), (7.0, null), (8.0, "dominion round x start"));
            Assert.IsEmpty(timeline.Rounds);
            Assert.AreEqual(-1, timeline.Winner);
        }

        [Test] public void NotesOutOfOrderAreReadInTimeOrder()
        {
            DominionRoundTimeline timeline = Read(
                (200.0, DominionMarkerNotes.RoundStart(2)),
                (40.0, DominionMarkerNotes.RoundStart(1)));
            Assert.AreEqual(1, timeline.Rounds[0].Number);
            Assert.AreEqual(2, timeline.Rounds[1].Number);
        }

        // ---------------------------------------------------------------- sudden death

        [Test] public void TheMatchsOwnSuddenDeathIsRead()
        {
            DominionRoundTimeline timeline = Read(
                (400.0, DominionMarkerNotes.SuddenDeathStart(TwoTeams)),
                (430.0, DominionMarkerNotes.SuddenDeathReplay(TwoTeams)),
                (470.0, DominionMarkerNotes.MatchOver(0, TwoTeams, new[] { 1, 1 })));
            Assert.IsNotNull(timeline.SuddenDeath);
            Assert.AreEqual(400.0, timeline.SuddenDeath.StartT);
            Assert.AreEqual(1, timeline.SuddenDeath.Replays);
            Assert.AreEqual(470.0, timeline.SuddenDeath.EndT);
            Assert.AreEqual(0, timeline.SuddenDeath.Winner);
            Assert.AreEqual(0, timeline.SuddenDeath.ForRound);
            Assert.AreEqual("sudden death", timeline.HowEnded(2));
        }

        [Test] public void ARoundsOwnSuddenDeathBelongsToThatRound()
        {
            DominionRoundTimeline timeline = Read(
                (10.0, DominionMarkerNotes.RoundStart(3)),
                (50.0, DominionMarkerNotes.RoundToSuddenDeath(3, TwoTeams, TwoTeams, new[] { 90, 95 })),
                (51.0, DominionMarkerNotes.SuddenDeathStart(TwoTeams)),
                (80.0, DominionMarkerNotes.RoundWonInSuddenDeath(3, 1, TwoTeams, new[] { 90, 95 })));
            DominionRound round = timeline.Rounds[0];
            Assert.AreEqual(DominionRoundTimeline.WonInSuddenDeath, round.Outcome);
            CollectionAssert.AreEqual(new[] { 1 }, round.Winners);
            Assert.AreEqual(50.0, round.EndT);
            Assert.AreEqual(95, round.Points[1]);
            Assert.AreEqual(3, timeline.SuddenDeath.ForRound);
            Assert.AreEqual(80.0, timeline.SuddenDeath.EndT);
            Assert.AreEqual(1, timeline.SuddenDeath.Winner);
        }

        // ---------------------------------------------------------------- how it ended

        [Test] public void TwoRoundWinsEndIt()
        {
            DominionRoundTimeline timeline = Read(
                (10.0, DominionMarkerNotes.RoundStart(1)), (50.0, DominionMarkerNotes.RoundEnd(1, 1, TwoTeams, new[] { 1, 9 })),
                (60.0, DominionMarkerNotes.RoundStart(2)), (99.0, DominionMarkerNotes.RoundEnd(2, 1, TwoTeams, new[] { 2, 8 })),
                (99.0, DominionMarkerNotes.MatchOver(1, TwoTeams, new[] { 0, 2 })));
            Assert.AreEqual("two round wins", timeline.HowEnded(2));
            Assert.AreEqual("3 round wins", Read((99.0, DominionMarkerNotes.MatchOver(1, TwoTeams, new[] { 0, 3 }))).HowEnded(3));
        }

        [Test] public void ALeadAfterTheLastRoundEndsIt()
        {
            DominionRoundTimeline timeline = Read(
                (10.0, DominionMarkerNotes.RoundStart(1)), (50.0, DominionMarkerNotes.RoundEnd(1, 0, TwoTeams, new[] { 9, 1 })),
                (60.0, DominionMarkerNotes.RoundStart(2)), (99.0, DominionMarkerNotes.RoundEnd(2, -1, TwoTeams, new[] { 4, 4 })),
                (110.0, DominionMarkerNotes.RoundStart(3)), (150.0, DominionMarkerNotes.RoundEnd(3, -1, TwoTeams, new[] { 3, 3 })),
                (150.0, DominionMarkerNotes.MatchOver(0, TwoTeams, new[] { 1, 0 })));
            Assert.AreEqual("lead after round 3", timeline.HowEnded(2));
        }

        [Test] public void OthersLeavingEndsIt()
        {
            DominionRoundTimeline timeline = Read(
                (10.0, DominionMarkerNotes.RoundStart(1)), (30.0, DominionMarkerNotes.RoundCutShort(1, TwoTeams, new[] { 2, 0 })),
                (30.0, DominionMarkerNotes.MatchOver(0, TwoTeams, new[] { 0, 0 })));
            Assert.AreEqual("others left", timeline.HowEnded(2));
        }

        [Test] public void NoWinnerNoEnding() => Assert.AreEqual("", Read((10.0, DominionMarkerNotes.RoundStart(1))).HowEnded(2));

        // ---------------------------------------------------------------- the maps

        [Test] public void TheLanesZonesHaveTheLanesNames()
        {
            Assert.AreEqual("Top zone", ReportMaps.ZoneName(ReportMap.Lane, 0));
            Assert.AreEqual("Bottom zone", ReportMaps.ZoneName(ReportMap.Lane, 1));
            Assert.AreEqual(ArenaNames.TeamName(0) + " spawn", ReportMaps.ZoneName(ReportMap.Lane, 2));
            Assert.AreEqual(ArenaNames.TeamName(1) + " spawn", ReportMaps.ZoneName(ReportMap.Lane, 3));
            Assert.AreEqual("Zone 6", ReportMaps.ZoneName(ReportMap.Lane, 6));
            Assert.AreEqual(4, ReportMaps.ZoneCount(ReportMap.Lane));
        }

        [Test] public void TheLanesZoneNumbersAreTheLaneBuildersOwn()
        {
            Assert.AreEqual("Top zone", ReportMaps.ZoneName(ReportMap.Lane, Overpower.EditorTools.DominionLaneBuilder.TopZone));
            Assert.AreEqual("Bottom zone", ReportMaps.ZoneName(ReportMap.Lane, Overpower.EditorTools.DominionLaneBuilder.BottomZone));
            Assert.IsTrue(ReportMaps.IsSpawn(ReportMap.Lane, Overpower.EditorTools.DominionLaneBuilder.OrangeSpawnZone));
            Assert.IsTrue(ReportMaps.IsSpawn(ReportMap.Lane, Overpower.EditorTools.DominionLaneBuilder.PurpleSpawnZone));
        }

        [Test] public void EachMapsPictureIsItsSavedMinimap()
        {
            Assert.AreEqual(Overpower.EditorTools.DominionLaneBuilder.MinimapConfigPath, ReportMaps.MinimapConfigPath(ReportMap.Lane));
            Assert.AreEqual(Overpower.EditorTools.MinimapBaker.ConfigPath, ReportMaps.MinimapConfigPath(ReportMap.Triangle));
        }

        [Test] public void TheTriangleArenaKeepsItsNames()
        {
            for (int zone = 0; zone < 10; zone++)
                Assert.AreEqual(ArenaNames.ZoneName(zone), ReportMaps.ZoneName(ReportMap.Triangle, zone));
            Assert.AreEqual(10, ReportMaps.ZoneCount(ReportMap.Triangle));
        }

        [Test] public void SpawnsAreTheCapitals()
        {
            foreach (int zone in new[] { 6, 7, 8 }) Assert.IsTrue(ReportMaps.IsSpawn(ReportMap.Triangle, zone), "zone " + zone);
            foreach (int zone in new[] { 0, 3, 9 }) Assert.IsFalse(ReportMaps.IsSpawn(ReportMap.Triangle, zone), "zone " + zone);
            Assert.IsFalse(ReportMaps.IsSpawn(ReportMap.Lane, 0));
        }

        [Test] public void AClosedCornerIsItsCapitalItsTierTwoAndBothTierThreesBesideIt()
        {
            CollectionAssert.AreEqual(new[] { 2, 4, 5, 8 }, ReportMaps.ClosedZones(2));
            CollectionAssert.AreEqual(new[] { 0, 3, 5, 6 }, ReportMaps.ClosedZones(0));
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 7 }, ReportMaps.ClosedZones(1));
            Assert.IsEmpty(ReportMaps.ClosedZones(-1));
        }

        [Test] public void TheLaneIsShownAsItsWholePictureAndTheTriangleInItsOwnFrame()
        {
            var picture = new MapFrame(20f, 30f, 90f, 40f);
            MapFrame lane = ReportMaps.FrameFor(ReportMap.Lane, picture);
            Assert.AreEqual(20f, lane.MinX);
            Assert.AreEqual(40f, lane.Depth);
            MapFrame triangle = ReportMaps.FrameFor(ReportMap.Triangle, picture);
            Assert.AreEqual(ReportMaps.TriangleFrame.Width, triangle.Width);
            Assert.AreEqual(ReportMaps.TriangleFrame.MinZ, triangle.MinZ);
        }

        [Test] public void APictureKeepsTheSameScaleOnBothAxes()
        {
            Assert.AreEqual(400, ReportMaps.PixelsUp(new MapFrame(0f, 0f, 100f, 50f), 800));
            Assert.AreEqual(800, ReportMaps.PixelsUp(new MapFrame(0f, 0f, 100f, 100f), 800));
        }

        [Test] public void ImageUpIsPlusZ()
        {
            var frame = new MapFrame(10f, 20f, 100f, 50f);
            (float px, float py) = ReportMaps.WorldToPixel(frame, 800, 10f, 20f);
            Assert.AreEqual(0f, px, 0.01f);
            Assert.AreEqual(400f, py, 0.01f, "the smallest z is the bottom row");
            (px, py) = ReportMaps.WorldToPixel(frame, 800, 110f, 70f);
            Assert.AreEqual(800f, px, 0.01f);
            Assert.AreEqual(0f, py, 0.01f);
            (px, py) = ReportMaps.WorldToPixel(frame, 800, 35f, 45f);
            Assert.AreEqual(200f, px, 0.01f);
            Assert.AreEqual(200f, py, 0.01f);
        }

        [Test] public void TheTriangleFrameMatchesTheOldRendersMapping()
        {
            (float px, float py) = ReportMaps.WorldToPixel(ReportMaps.TriangleFrame, 1024, 5f, 5f);
            (float oldPx, float oldPy) = ArenaReportRender.WorldToPixel(5f, 5f);
            Assert.AreEqual(oldPx, px, 0.01f);
            Assert.AreEqual(oldPy, py, 0.01f);
        }
    }
}
