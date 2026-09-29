using System;
using NUnit.Framework;
using Overpower.Match;
using Photon.Realtime;

namespace Overpower.Tests
{
    /// <summary>Task 9e (Tudor D21): the id a player keeps between runs, when a rejoin is on offer, which
    /// disconnects count as "connection lost", and how an inactive (dropped) actor counts in the match rules.</summary>
    public class RejoinRulesTests
    {
        // ---- the id

        [Test]
        public void TheIdIsCreatedWhenNoneIsSaved()
        {
            string id = PlayerIdRule.Resolve(null, () => "fresh-id", out bool created);
            Assert.AreEqual("fresh-id", id);
            Assert.IsTrue(created, "a new id must be reported as created so the caller saves it");
        }

        [Test]
        public void TheSavedIdIsReusedAndNothingNewIsCreated()
        {
            int calls = 0;
            string id = PlayerIdRule.Resolve("saved-id", () => { calls++; return "other"; }, out bool created);
            Assert.AreEqual("saved-id", id);
            Assert.IsFalse(created);
            Assert.AreEqual(0, calls, "a saved id is never replaced");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AnEmptySavedIdCountsAsMissing(string saved)
        {
            string id = PlayerIdRule.Resolve(saved, () => "fresh-id", out bool created);
            Assert.AreEqual("fresh-id", id);
            Assert.IsTrue(created);
        }

        [Test]
        public void TheIdIsNeverEmptyEvenIfTheGeneratorFails()
        {
            Assert.IsNotEmpty(PlayerIdRule.Resolve(null, () => "", out _));
            Assert.IsNotEmpty(PlayerIdRule.Resolve(null, () => null, out _));
        }

        [Test]
        public void TheSavedIdIsTrimmed()
        {
            Assert.AreEqual("abc", PlayerIdRule.Resolve("  abc\n", () => "x", out _));
        }

        [Test]
        public void TwoNewIdsDiffer()
        {
            Assert.AreNotEqual(PlayerIdRule.NewId(), PlayerIdRule.NewId());
        }

        [Test]
        public void TheLoggedFormOfAnIdIsAtMostEightCharacters()
        {
            Assert.AreEqual("12345678", PlayerIdRule.ForLog("1234567890abcdef"));
            Assert.AreEqual("abc", PlayerIdRule.ForLog("abc"));
            Assert.AreEqual("", PlayerIdRule.ForLog(null));
        }

        // ---- offering a rejoin

        private const long Second = 1000L;

        [Test]
        public void ARejoinIsOfferedForAMatchSavedSixtySecondsAgoWithA120SecondWindow()
        {
            Assert.IsTrue(RejoinRules.IsOffered(Record("Room_1234", "u1", 1_000_000), "u1", 1_000_000 + 60 * Second, 120));
        }

        [Test]
        public void NoRejoinIsOfferedForAMatchSaved130SecondsAgo()
        {
            Assert.IsFalse(RejoinRules.IsOffered(Record("Room_1234", "u1", 1_000_000), "u1", 1_000_000 + 130 * Second, 120));
        }

        [Test]
        public void NoRejoinIsOfferedWhenNothingIsSaved()
        {
            Assert.IsFalse(RejoinRules.IsOffered(null, "u1", 5_000_000, 120));
        }

        [Test]
        public void NoRejoinIsOfferedWhenTheRecordHasNoRoom()
        {
            Assert.IsFalse(RejoinRules.IsOffered(Record("", "u1", 1_000_000), "u1", 1_000_000 + 10 * Second, 120));
        }

        [Test]
        public void NoRejoinIsOfferedWhenTheRecordBelongsToAnotherId()
        {
            Assert.IsFalse(RejoinRules.IsOffered(Record("Room_1234", "u1", 1_000_000), "u2", 1_000_000 + 10 * Second, 120));
        }

        [Test]
        public void TheWindowEndsExactlyAtTheTtl()
        {
            var record = Record("Room_1234", "u1", 1_000_000);
            Assert.IsTrue(RejoinRules.IsOffered(record, "u1", 1_000_000 + 120 * Second - 1, 120));
            Assert.IsFalse(RejoinRules.IsOffered(record, "u1", 1_000_000 + 120 * Second, 120));
        }

        [Test]
        public void ARecordFromTheFutureIsNotOffered()
        {
            Assert.IsFalse(RejoinRules.IsOffered(Record("Room_1234", "u1", 9_000_000), "u1", 1_000_000, 120));
        }

        [Test]
        public void ANonPositiveWindowNeverOffersARejoin()
        {
            Assert.IsFalse(RejoinRules.IsOffered(Record("Room_1234", "u1", 1_000_000), "u1", 1_000_000, 0));
        }

        [Test]
        public void TheWindowInSecondsBecomesPhotonsMilliseconds()
        {
            Assert.AreEqual(120_000, RejoinRules.PlayerTtlMs(120f));
            Assert.AreEqual(0, RejoinRules.PlayerTtlMs(0f));
            Assert.AreEqual(0, RejoinRules.PlayerTtlMs(-5f));
        }

        // ---- the saved record

        [Test]
        public void ARecordSurvivesBeingWrittenAndRead()
        {
            var original = Record("Room_4321", "user-xyz", 1234567890123L);
            original.Nick = "Radu";
            Assert.IsTrue(RejoinRecord.TryParse(original.Serialize(), out RejoinRecord back));
            Assert.AreEqual("Room_4321", back.RoomName);
            Assert.AreEqual("user-xyz", back.UserId);
            Assert.AreEqual("Radu", back.Nick);
            Assert.AreEqual(1234567890123L, back.SavedAtMs);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json at all")]
        [TestCase("{}")]
        public void AnUnreadableRecordIsNoRecord(string text)
        {
            Assert.IsFalse(RejoinRecord.TryParse(text, out RejoinRecord record));
            Assert.IsNull(record);
        }

        // ---- which disconnects are "connection lost"

        [TestCase(DisconnectCause.ClientTimeout, true)]
        [TestCase(DisconnectCause.ServerTimeout, true)]
        [TestCase(DisconnectCause.Exception, true)]
        [TestCase(DisconnectCause.DisconnectByServerLogic, true)]
        [TestCase(DisconnectCause.DisconnectByServerReasonUnknown, true)]
        [TestCase(DisconnectCause.DisconnectByClientLogic, false)]
        [TestCase(DisconnectCause.ApplicationQuit, false)]
        [TestCase(DisconnectCause.None, false)]
        public void OnlyAnUnwantedDisconnectFromARoomShowsThePanel(DisconnectCause cause, bool expected)
        {
            Assert.AreEqual(expected, RejoinRules.IsConnectionLoss(cause, wasInRoom: true));
        }

        [Test]
        public void ADisconnectWhileNotInARoomNeverShowsThePanel()
        {
            Assert.IsFalse(RejoinRules.IsConnectionLoss(DisconnectCause.ClientTimeout, wasInRoom: false));
        }

        [TestCase(ClientState.Disconnecting, true)]
        [TestCase(ClientState.Disconnected, true)]
        [TestCase(ClientState.Joined, true)]
        [TestCase(ClientState.DisconnectingFromGameServer, false)]
        [TestCase(ClientState.Leaving, false)]
        public void OnlyALeaveThatHeadsBackToTheMasterIsADeliberateLeave(ClientState state, bool isDisconnect)
        {
            Assert.AreEqual(isDisconnect, RejoinRules.LeaveIsADisconnect(state));
        }

        // ---- the scoreboard tally a rejoined player continues

        [Test]
        public void ARestoredTallyContinuesFromTheRoomsNumbers()
        {
            var tally = new ScoreTally();
            tally.Restore(new[] { 3, 2, 1, 450, 5 });
            Assert.AreEqual(3, tally.Kills);
            Assert.AreEqual(2, tally.Deaths);
            Assert.AreEqual(1, tally.Assists);
            Assert.AreEqual(450, tally.DamageRounded);
            Assert.AreEqual(5, tally.Captures);
            tally.AddKill();
            Assert.AreEqual(4, tally.ToArray()[0]);
        }

        [Test]
        public void ARestoreFromNothingOrAShortValueLeavesZeros()
        {
            var tally = new ScoreTally();
            tally.Restore(null);
            Assert.AreEqual(0, tally.Kills);
            tally.Restore(new[] { 2 });
            Assert.AreEqual(2, tally.Kills);
            Assert.AreEqual(0, tally.Deaths);
            Assert.AreEqual(0, tally.DamageRounded);
            tally.Restore(new[] { -4, -1, -1, -9, -2 });
            Assert.AreEqual(new[] { 0, 0, 0, 0, 0 }, tally.ToArray());
        }

        [Test]
        public void OnlyAnActiveJoinerRefusalIsRetriedAndOnlyForAWhile()
        {
            Assert.IsTrue(RejoinRules.ShouldRetryRejoin(ErrorCode.JoinFailedFoundActiveJoiner, 0, 15));
            Assert.IsTrue(RejoinRules.ShouldRetryRejoin(ErrorCode.JoinFailedFoundActiveJoiner, 14, 15));
            Assert.IsFalse(RejoinRules.ShouldRetryRejoin(ErrorCode.JoinFailedFoundActiveJoiner, 15, 15));
            Assert.IsFalse(RejoinRules.ShouldRetryRejoin(ErrorCode.GameDoesNotExist, 0, 15));
            Assert.IsFalse(RejoinRules.ShouldRetryRejoin(ErrorCode.JoinFailedWithRejoinerNotFound, 0, 15));
        }

        // ---- request counters (a fresh process starts its counters at 0 while the room still holds the old ones)

        [Test]
        public void ACounterNeverGoesBelowWhatTheRoomAlreadyHolds()
        {
            Assert.AreEqual(7, RejoinRules.SeedCounter(0, 7));
            Assert.AreEqual(9, RejoinRules.SeedCounter(9, 7));
            Assert.AreEqual(0, RejoinRules.SeedCounter(0, -3));
        }

        // ---- an inactive actor in the match rules

        [Test]
        public void AnInactiveActorIsNotPresent()
        {
            Assert.IsFalse(PresenceRules.IsPresent(inactive: true));
            Assert.IsTrue(PresenceRules.IsPresent(inactive: false));
        }

        [Test]
        public void AnInactiveActorIsNeverAlive()
        {
            Assert.IsFalse(PresenceRules.CountsAsAlive(inactive: true, aliveProperty: true));
            Assert.IsFalse(PresenceRules.CountsAsAlive(inactive: true, aliveProperty: null));
        }

        [Test]
        public void AnActiveActorIsAliveUnlessTheAliveFlagSaysOtherwise()
        {
            Assert.IsTrue(PresenceRules.CountsAsAlive(inactive: false, aliveProperty: null));
            Assert.IsTrue(PresenceRules.CountsAsAlive(inactive: false, aliveProperty: true));
            Assert.IsFalse(PresenceRules.CountsAsAlive(inactive: false, aliveProperty: false));
        }

        [Test]
        public void AnInactiveMemberCountsAsDeadForTheTeamStatus()
        {
            Assert.IsTrue(PresenceRules.CountsAsDead(inactive: true, waiting: false, aliveProperty: true));
            Assert.IsTrue(PresenceRules.CountsAsDead(inactive: true, waiting: false, aliveProperty: null));
        }

        [Test]
        public void AnActiveMemberIsDeadWhenWaitingOrNotAlive()
        {
            Assert.IsFalse(PresenceRules.CountsAsDead(inactive: false, waiting: false, aliveProperty: true));
            Assert.IsFalse(PresenceRules.CountsAsDead(inactive: false, waiting: false, aliveProperty: null));
            Assert.IsTrue(PresenceRules.CountsAsDead(inactive: false, waiting: true, aliveProperty: true));
            Assert.IsTrue(PresenceRules.CountsAsDead(inactive: false, waiting: false, aliveProperty: false));
        }

        [Test]
        public void ATeamWhoseLastLivingMemberDropsIsAllDeadSoTheLastStandRulesApply()
        {
            // A team with no base, two members, one already dead (waiting) and one whose connection just dropped.
            int dead = 0;
            if (PresenceRules.CountsAsDead(inactive: false, waiting: true, aliveProperty: false)) dead++;
            if (PresenceRules.CountsAsDead(inactive: true, waiting: false, aliveProperty: true)) dead++;
            var team = new TeamStatus
            {
                TeamId = 1, InMatch = true, Members = 2, MembersDead = dead, MembersOutForLastStand = 1,
                HoldsOwnCapital = false, HoldsAnyCapitalInPlay = false,
            };
            var other = new TeamStatus { TeamId = 0, InMatch = true, Members = 1, HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true };
            MatchPhaseResult result = MatchPhaseRules.Recompute(new System.Collections.Generic.List<int>(), new[] { other, team });
            CollectionAssert.Contains(result.Eliminated, 1);
        }

        [Test]
        public void ATeamWithABaseIsNotKnockedOutJustBecauseItsOnlyMemberDropped()
        {
            var team = new TeamStatus
            {
                TeamId = 1, InMatch = true, Members = 1,
                MembersDead = PresenceRules.CountsAsDead(inactive: true, waiting: false, aliveProperty: true) ? 1 : 0,
                HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true,
            };
            var other = new TeamStatus { TeamId = 0, InMatch = true, Members = 1, HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true };
            MatchPhaseResult result = MatchPhaseRules.Recompute(new System.Collections.Generic.List<int>(), new[] { other, team });
            CollectionAssert.DoesNotContain(result.Eliminated, 1);
        }

        // ---- the lobby: an inactive member is not a player who is here

        [Test]
        public void TheLobbyCountSkipsInactiveActors()
        {
            Assert.IsFalse(PresenceRules.CountsInTheLobby(inactive: true));
            Assert.IsTrue(PresenceRules.CountsInTheLobby(inactive: false));
        }

        // ---- a rejoined player with no body yet

        [Test]
        public void AFallbackBodyIsSpawnedOnlyWhenNoneArrivedInTime()
        {
            Assert.IsFalse(RejoinRules.NeedsFallbackBody(hasBody: true, secondsWaited: 10f, limitSeconds: 3f));
            Assert.IsFalse(RejoinRules.NeedsFallbackBody(hasBody: false, secondsWaited: 1f, limitSeconds: 3f));
            Assert.IsTrue(RejoinRules.NeedsFallbackBody(hasBody: false, secondsWaited: 3f, limitSeconds: 3f));
        }

        // ---- 9e-2 fix 2: a normal join refused because the old place is still held

        [Test]
        public void ARefusalBecauseOurOldPlaceIsHeldRejoinsTheSavedRoom()
        {
            Assert.AreEqual(JoinRefusalAction.RejoinSavedRoom, RejoinRules.OnJoinRefused(ErrorCode.JoinFailedFoundInactiveJoiner, savedMatchOffered: true));
        }

        [Test]
        public void ARefusalBecauseOurOldPlaceIsHeldWithNoSavedRoomShowsAMessage()
        {
            Assert.AreEqual(JoinRefusalAction.ShowMessage, RejoinRules.OnJoinRefused(ErrorCode.JoinFailedFoundInactiveJoiner, savedMatchOffered: false));
        }

        [Test]
        public void AnyOtherRefusalIsLeftToTheNormalPath()
        {
            Assert.AreEqual(JoinRefusalAction.None, RejoinRules.OnJoinRefused(ErrorCode.GameFull, true));
            Assert.AreEqual(JoinRefusalAction.None, RejoinRules.OnJoinRefused(ErrorCode.NoRandomMatchFound, false));
        }

        // ---- 9e-2 fix 1: what is reset when a match is given up

        [Test]
        public void GivingUpAMatchResetsEveryMatchPropertyTheRoomHeld()
        {
            var resets = Overpower.Net.MatchPropertyReset.Build();
            foreach (string key in new[] { "alive", "lastStand", "lastStandAt", "gold", "armorAbsorbLvl", "armorRechargeLvl",
                                           "sb", "hpReq", "tpUse", "tpRdy", "st", "aozT",
                                           "weaponId", "equipmentId", "ultimateId", "mobilityId" })
                Assert.IsTrue(resets.ContainsKey(key), "missing key " + key);
        }

        [Test]
        public void TheResetValuesAreTheOnesOfAFreshPlayer()
        {
            var resets = Overpower.Net.MatchPropertyReset.Build();
            Assert.AreEqual(true, resets["alive"]);
            Assert.AreEqual(false, resets["lastStand"]);
            Assert.AreEqual(0, resets["armorAbsorbLvl"]);
            Assert.IsNull(resets["gold"], "gold is removed, not written as 0: the next match reads it as 'never set'");
            Assert.IsNull(resets["sb"]);
            Assert.IsNull(resets["weaponId"]);
            Assert.IsFalse(resets.ContainsKey("teamID"), "the team is re-picked on the next join, not reset here");
        }

        // ---- 9e-2 fix 3: placement counters after a rejoin

        [Test]
        public void ANewMineCounterContinuesAboveTheSurvivingOnes()
        {
            Assert.AreEqual(8, Overpower.Combat.DeployablePruning.NextSeq(0, new[] { 3, 7, 5 }));
            Assert.AreEqual(10, Overpower.Combat.DeployablePruning.NextSeq(10, new[] { 3, 7, 5 }));
            Assert.AreEqual(0, Overpower.Combat.DeployablePruning.NextSeq(0, new int[0]));
        }

        // ---- 9e-2 D-a: a dropped member counts as dead only after the grace

        [Test]
        public void ADroppedMemberDoesNotCountAsDeadInsideTheGrace()
        {
            Assert.IsFalse(PresenceRules.CountsAsDead(true, false, true, inactiveSeconds: 4f, graceSeconds: 10f));
            Assert.IsTrue(PresenceRules.CountsAsDead(true, false, true, inactiveSeconds: 10f, graceSeconds: 10f));
            Assert.IsTrue(PresenceRules.CountsAsDead(true, false, true, inactiveSeconds: 30f, graceSeconds: 10f));
        }

        [Test]
        public void TheGraceNeverHidesARealDeath()
        {
            Assert.IsTrue(PresenceRules.CountsAsDead(true, true, true, inactiveSeconds: 1f, graceSeconds: 10f));
            Assert.IsTrue(PresenceRules.CountsAsDead(false, false, false, inactiveSeconds: 0f, graceSeconds: 10f));
        }

        [Test]
        public void ANoGraceMeansDeadAtOnce()
        {
            Assert.IsTrue(PresenceRules.CountsAsDead(true, false, true, inactiveSeconds: 0f, graceSeconds: 0f));
        }

        [Test]
        public void ATeamWhoseLastMemberDroppedIsNotKnockedOutInsideTheGraceButIsAfter()
        {
            System.Func<float, MatchPhaseResult> run = secondsAway =>
            {
                bool dead = PresenceRules.CountsAsDead(true, false, true, secondsAway, 10f);
                var team = new TeamStatus { TeamId = 1, InMatch = true, Members = 1, MembersDead = dead ? 1 : 0,
                                            HoldsOwnCapital = false, HoldsAnyCapitalInPlay = false };
                var other = new TeamStatus { TeamId = 0, InMatch = true, Members = 1, HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true };
                return MatchPhaseRules.Recompute(new System.Collections.Generic.List<int>(), new[] { other, team });
            };
            CollectionAssert.DoesNotContain(run(5f).Eliminated, 1);
            CollectionAssert.Contains(run(11f).Eliminated, 1);
        }

        // ---- 9e-2 minor: an id file that belongs to another folder or machine is not reused

        [Test]
        public void ATaggedIdIsReusedOnlyWhereItWasMade()
        {
            string tagHere = PlayerIdRule.Tag("PC-A", @"C:\Games\A");
            string file = PlayerIdRule.Compose("abc123", tagHere);
            Assert.AreEqual("abc123", PlayerIdRule.ResolveTagged(file, tagHere, () => "new", out bool created));
            Assert.IsFalse(created);

            string tagCopy = PlayerIdRule.Tag("PC-A", @"C:\Games\A - Copy");
            Assert.AreNotEqual(tagHere, tagCopy);
            Assert.AreEqual("new", PlayerIdRule.ResolveTagged(file, tagCopy, () => "new", out created));
            Assert.IsTrue(created);

            Assert.AreNotEqual(tagHere, PlayerIdRule.Tag("PC-B", @"C:\Games\A"));
        }

        [Test]
        public void ALegacyUntaggedIdIsAdoptedNotReplaced()
        {
            Assert.AreEqual("abc123", PlayerIdRule.ResolveTagged("abc123", "tag", () => "new", out bool created));
            Assert.IsTrue(created, "reported as created so the caller rewrites the file with its tag");
        }

        [Test]
        public void ATaggedIdIsNeverEmptyAndTheTagIsStable()
        {
            Assert.AreEqual(PlayerIdRule.Tag("m", "f"), PlayerIdRule.Tag("m", "f"));
            Assert.IsNotEmpty(PlayerIdRule.ResolveTagged(null, "tag", () => "", out _));
        }

        private static RejoinRecord Record(string room, string userId, long savedAtMs) =>
            new RejoinRecord { RoomName = room, UserId = userId, Nick = "Nick", SavedAtMs = savedAtMs };
    }
}
