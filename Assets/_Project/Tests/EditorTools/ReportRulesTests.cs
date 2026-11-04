using NUnit.Framework;
using Overpower.EditorTools.Telemetry;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// The match report's decisions: which mode a match was (the logs, else the folder's name, else the log itself), what each mode shows, how a
    /// number is written, and what the page leaves out (it stays in the CSV files).
    /// </summary>
    public class ReportRulesTests
    {
        // ---------------------------------------------------------------- the mode

        [Test] public void EveryModeIsReadFromItsName()
        {
            Assert.AreEqual(ReportMode.Conquest3v3v3, ReportModes.FromName("Conquest 3v3v3"));
            Assert.AreEqual(ReportMode.Conquest3v3, ReportModes.FromName("Conquest 3v3"));
            Assert.AreEqual(ReportMode.Dominion3v3v3, ReportModes.FromName("Dominion 3v3v3"));
            Assert.AreEqual(ReportMode.Dominion2v2, ReportModes.FromName("Dominion 2v2"));
        }

        [Test] public void ANameIsReadWhateverItsCaseAndSeparators()
        {
            Assert.AreEqual(ReportMode.Dominion2v2, ReportModes.FromName("Dominion-2v2"));
            Assert.AreEqual(ReportMode.Dominion2v2, ReportModes.FromName("  dominion   2V2 "));
            Assert.AreEqual(ReportMode.Conquest3v3, ReportModes.FromName("conquest_3v3"));
        }

        [Test] public void AnyOtherTextNamesNoMode()
        {
            foreach (string text in new[] { null, "", "   ", "Conquest", "3v3", "Dominion 3v3", "Conquest 3v3v3v3", "Match" })
                Assert.IsNull(ReportModes.FromName(text), text);
        }

        [Test] public void TheModeIsReadFromTheMatchFoldersName()
        {
            Assert.AreEqual(ReportMode.Dominion2v2, ReportModes.FromFolderName("2026-11-01_2346_Dominion-2v2_jjjjs-lobby"));
            Assert.AreEqual(ReportMode.Conquest3v3, ReportModes.FromFolderName("2026-10-03_2250_Conquest-3v3_Tudors-lobby (2)"));
            Assert.IsNull(ReportModes.FromFolderName("match_a"));
            Assert.IsNull(ReportModes.FromFolderName(null));
        }

        [Test] public void ALobbyNamedLikeAModeNeverHidesTheFoldersMode() =>
            Assert.AreEqual(ReportMode.Dominion3v3v3, ReportModes.FromFolderName("2026-01-02_0304_Dominion-3v3v3_Conquest-3v3"));

        [Test] public void TheLogsOwnModeWinsOverTheFoldersName() =>
            Assert.AreEqual(ReportMode.Dominion3v3v3, ReportModes.Resolve(new[] { "", "Dominion 3v3v3" }, "2026-01-02_0304_Conquest-3v3_x", false, 2));

        [Test] public void WithoutAModeInTheLogsTheFolderSaysIt() =>
            Assert.AreEqual(ReportMode.Dominion2v2, ReportModes.Resolve(new[] { "", null }, "2026-01-02_0304_Dominion-2v2_x", false, 3));

        [Test] public void DominionRoundNotesMakeAnUnnamedMatchDominion()
        {
            Assert.AreEqual(ReportMode.Dominion2v2, ReportModes.Resolve(null, "old-folder", true, 2));
            Assert.AreEqual(ReportMode.Dominion3v3v3, ReportModes.Resolve(null, "old-folder", true, 3));
        }

        [Test] public void AMatchThatNamesNoModeIsReadAsConquestThreeTeams()
        {
            Assert.AreEqual(ReportMode.Conquest3v3v3, ReportModes.Resolve(null, null, false, 2));
            Assert.AreEqual(ReportMode.Conquest3v3v3, ReportModes.Resolve(new string[0], "match_a", false, 3));
        }

        [Test] public void OnlyTheTwoVersusTwoPlaysOnTheLane()
        {
            Assert.AreEqual(ReportMap.Lane, ReportModes.MapOf(ReportMode.Dominion2v2));
            Assert.AreEqual(ReportMap.Triangle, ReportModes.MapOf(ReportMode.Dominion3v3v3));
            Assert.AreEqual(ReportMap.Triangle, ReportModes.MapOf(ReportMode.Conquest3v3));
            Assert.AreEqual(ReportMap.Triangle, ReportModes.MapOf(ReportMode.Conquest3v3v3));
        }

        [Test] public void OnlyConquestThreeTeamsHasPhaseTabsAndTheGddTargets()
        {
            foreach (ReportMode mode in new[] { ReportMode.Conquest3v3, ReportMode.Dominion3v3v3, ReportMode.Dominion2v2 })
            {
                Assert.IsFalse(ReportModes.HasPhaseTabs(mode), mode.ToString());
                Assert.IsFalse(ReportModes.ShowsTargets(mode), mode.ToString());
            }
            Assert.IsTrue(ReportModes.HasPhaseTabs(ReportMode.Conquest3v3v3));
            Assert.IsTrue(ReportModes.ShowsTargets(ReportMode.Conquest3v3v3));
        }

        [Test] public void OverPowerIsLeftOutWithTwoTeams()
        {
            Assert.IsFalse(ReportModes.ShowsOverPower(ReportMode.Conquest3v3));
            Assert.IsFalse(ReportModes.ShowsOverPower(ReportMode.Dominion2v2));
            Assert.IsTrue(ReportModes.ShowsOverPower(ReportMode.Conquest3v3v3));
        }

        [Test] public void GoldIsConquestsAndTheCentrePaysOnlyInDominionThreeTeams()
        {
            Assert.IsTrue(ReportModes.ShowsGold(ReportMode.Conquest3v3));
            Assert.IsFalse(ReportModes.ShowsGold(ReportMode.Dominion2v2));
            Assert.IsTrue(ReportModes.HasCentrePayouts(ReportMode.Dominion3v3v3));
            Assert.IsFalse(ReportModes.HasCentrePayouts(ReportMode.Dominion2v2));
            Assert.IsFalse(ReportModes.HasCentrePayouts(ReportMode.Conquest3v3v3));
        }

        [Test] public void EachModeNamesItsTeamsAndSize()
        {
            CollectionAssert.AreEqual(new[] { 0, 1 }, ReportModes.Teams(ReportMode.Dominion2v2));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ReportModes.Teams(ReportMode.Dominion3v3v3));
            Assert.AreEqual(2, ReportModes.DefaultTeamSize(ReportMode.Dominion2v2));
            Assert.AreEqual(3, ReportModes.DefaultTeamSize(ReportMode.Conquest3v3));
            Assert.AreEqual("Dominion 2v2", ReportModes.DisplayName(ReportMode.Dominion2v2));
            Assert.AreEqual("conquest-3v3v3", ReportModes.Key(ReportMode.Conquest3v3v3));
        }

        // ---------------------------------------------------------------- numbers

        [Test] public void MatchTimeCountsFromGoingLive()
        {
            Assert.AreEqual("0:00", ReportFormat.Clock(12.0, 12.0));
            Assert.AreEqual("1:05", ReportFormat.Clock(77.0, 12.0));
            Assert.AreEqual("12:00", ReportFormat.Clock(732.0, 12.0));
        }

        [Test] public void MatchTimeShowsWholeSecondsGone()
        {
            Assert.AreEqual("0:59", ReportFormat.Clock(59.9, 0.0));
            Assert.AreEqual("1:00", ReportFormat.Clock(60.0, 0.0));
        }

        [Test] public void TheWarmUpReadsAsMinusTime()
        {
            Assert.AreEqual("-0:05", ReportFormat.Clock(7.0, 12.0));
            Assert.AreEqual("-0:01", ReportFormat.Clock(11.7, 12.0));
            Assert.AreEqual("-1:30", ReportFormat.Clock(0.0, 90.0));
        }

        [Test] public void ALengthUnderAMinuteIsSeconds()
        {
            Assert.AreEqual("45 s", ReportFormat.Length(45.0));
            Assert.AreEqual("45 s", ReportFormat.Length(44.6));
            Assert.AreEqual("0 s", ReportFormat.Length(-3.0));
        }

        [Test] public void ALengthFromAMinuteOnIsMinutesAndSeconds()
        {
            Assert.AreEqual("1:00", ReportFormat.Length(59.6));
            Assert.AreEqual("1:20", ReportFormat.Length(80.2));
            Assert.AreEqual("23:04", ReportFormat.Length(1384.0));
        }

        [Test] public void AShareIsAWholePercentage()
        {
            Assert.AreEqual("38%", ReportFormat.Percent(0.375));
            Assert.AreEqual("0%", ReportFormat.Percent(0.0));
            Assert.AreEqual("100%", ReportFormat.Percent(1.0));
            Assert.AreEqual("", ReportFormat.Percent(double.NaN));
        }

        [Test] public void DamageAndPointsAreWholeNumbers()
        {
            Assert.AreEqual("13", ReportFormat.Whole(12.5));
            Assert.AreEqual("12", ReportFormat.Whole(12.4));
            Assert.AreEqual("0", ReportFormat.Whole(0.2));
            Assert.AreEqual("", ReportFormat.Whole(double.PositiveInfinity));
        }

        [Test] public void NumbersReadTheSameOnAGermanPc()
        {
            System.Globalization.CultureInfo before = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.AreEqual("1:20", ReportFormat.Length(80.0));
                Assert.AreEqual("13", ReportFormat.Whole(12.5));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = before;
            }
        }

        [Test] public void EachBotBuildHasItsName()
        {
            Assert.AreEqual("Shotgun", ReportFormat.BotBuildName(Overpower.Data.BotConfig.Shotgun));
            Assert.AreEqual("Burst", ReportFormat.BotBuildName(Overpower.Data.BotConfig.Burst));
            Assert.AreEqual("Laser", ReportFormat.BotBuildName(Overpower.Data.BotConfig.Laser));
            Assert.AreEqual("", ReportFormat.BotBuildName(-1));
            Assert.AreEqual("", ReportFormat.BotBuildName(7));
        }

        [Test] public void TheHeaderCountsHumansAndBots()
        {
            Assert.AreEqual("4 players: 0 humans, 4 bots", ReportFormat.PlayerCount(0, 4));
            Assert.AreEqual("1 player: 1 human, 0 bots", ReportFormat.PlayerCount(1, 0));
            Assert.AreEqual("3 players: 2 humans, 1 bot", ReportFormat.PlayerCount(2, 1));
        }

        // ---------------------------------------------------------------- what the page leaves out

        [Test] public void TheLobbysOwnNotesAreLeftOut()
        {
            foreach (string note in new[]
                     {
                         LobbyMarkerNotes.SeatTaken("sS1"), LobbyMarkerNotes.SeatLeft("o2"), LobbyMarkerNotes.BotSeen(1001, "Rex"), LobbyMarkerNotes.SpectatorSeen(4),
                         LobbyMarkerNotes.LobbyCreated("Kim's lobby", "Conquest 3v3"), LobbyMarkerNotes.StartGame, LobbyMarkerNotes.EndWarmup,
                         "countdown start", "countdown cancelled", "two-team lobby on", "two-team lobby off", LobbyMarkerNotes.EarlyLinesDropped(12),
                     })
                Assert.IsTrue(ReportVisibility.HidesMarker(note), note);
        }

        [Test] public void TheRoundStoryIsLeftToTheResultSection()
        {
            Assert.IsTrue(ReportVisibility.HidesMarker(Overpower.Dominion.DominionMarkerNotes.RoundStart(2)));
            Assert.IsTrue(ReportVisibility.HidesMarker(Overpower.Dominion.DominionMarkerNotes.Bounty(1, 0, 40)));
        }

        [Test] public void AMarkerSomebodyDroppedStays()
        {
            foreach (string note in new[] { "", "the shop froze here", LobbyMarkerNotes.HostChanged(3, "Kim"), "countdown starts soon" })
                Assert.IsFalse(ReportVisibility.HidesMarker(note), note);
            Assert.IsFalse(ReportVisibility.HidesMarker(null));
        }

        [Test] public void TheHiddenStatsAreTheAgreedOnes()
        {
            foreach (string stat in new[]
                     {
                         "matchId", "commits", "tuning", "coverage", "logCoverage", "skippedLines", "console", "actor", "itemId",
                         "pulls", "projectiles", "splashHits", "armorDamage", "healthDamage", "meanDistance", "medianDistance",
                         "statusCount", "statusSeconds", "marksPlaced", "goldDebug", "goldOther", "goldRefund",
                     })
            {
                Assert.IsTrue(ReportVisibility.HidesStat(stat), stat);
                CollectionAssert.Contains(ReportVisibility.HiddenStats(), stat);
            }
        }

        [Test] public void WhatADesignerReadsAMatchByStays()
        {
            foreach (string stat in new[] { "kills", "deaths", "damageDealt", "accuracy", "marksCashed", "goldTerritory", "goldBounty", "healing", null })
                Assert.IsFalse(ReportVisibility.HidesStat(stat), stat ?? "null");
        }
    }
}
