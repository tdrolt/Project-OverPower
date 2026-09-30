using NUnit.Framework;
using Overpower.Match;
using Photon.Realtime;

namespace Overpower.Tests
{
    /// <summary>Task 9f (Tudor D22): the result screen leads back to the name screen; and the rejoin body choice (9e-3 review).</summary>
    public class BackToNameScreenRulesTests
    {
        // ---- the panel button (decided by the match PHASE: a knocked-out player's lose panel shows mid-match)

        [Test]
        public void WhenTheMatchIsReallyOverTheButtonGoesBackToTheNameScreen() =>
            Assert.AreEqual(ResultButtonAction.BackToNameScreen, BackToNameScreenRules.ButtonAction(MatchPhase.Over));

        [TestCase(MatchPhase.Warmup)]
        [TestCase(MatchPhase.ThreeTeams)]
        [TestCase(MatchPhase.TwoTeams)]
        public void WhileTheMatchStillRunsTheButtonStillClosesTheGame(MatchPhase phase) =>
            Assert.AreEqual(ResultButtonAction.CloseGame, BackToNameScreenRules.ButtonAction(phase));

        // ---- giving up on the wait

        [Test]
        public void StillInARoomAfterTheTimeoutMeansDisconnectBeforeTheReload() =>
            Assert.IsTrue(BackToNameScreenRules.MustDisconnectBeforeReload(ClientState.Joined));

        [Test]
        public void ConnectingHalfwayAfterTheTimeoutMeansDisconnectBeforeTheReload() =>
            Assert.IsTrue(BackToNameScreenRules.MustDisconnectBeforeReload(ClientState.ConnectingToMasterServer));

        [Test]
        public void OnTheMasterServerNoDisconnectIsNeeded() =>
            Assert.IsFalse(BackToNameScreenRules.MustDisconnectBeforeReload(ClientState.ConnectedToMasterServer));

        // ---- the zip after the leave

        [Test]
        public void TheLiveActorNumberIsUsedWhileInARoom() =>
            Assert.AreEqual(3, Overpower.Telemetry.MatchLogZipRule.ResolveActor(3, 5));

        [TestCase(-1)]
        [TestCase(0)]
        public void AfterTheLeaveTheRememberedActorNumberIsUsed(int live) =>
            Assert.AreEqual(5, Overpower.Telemetry.MatchLogZipRule.ResolveActor(live, 5));

        [Test]
        public void AnEmptyNickFallsBackToTheRememberedOne() =>
            Assert.AreEqual("Radu", Overpower.Telemetry.MatchLogZipRule.ResolveNick("", "Radu"));

        [Test]
        public void AnActorSeenInTheRoomIsStillThereAfterTheLeave()
        {
            int remembered = Overpower.Telemetry.MatchLogZipRule.ResolveActor(4, -1); // the zip at the button press stores this
            Assert.AreEqual(4, remembered);
            Assert.AreEqual(4, Overpower.Telemetry.MatchLogZipRule.ResolveActor(-1, remembered));
        }

        [Test]
        public void ALiveNickIsKept() =>
            Assert.AreEqual("Live", Overpower.Telemetry.MatchLogZipRule.ResolveNick("Live", "Radu"));

        // ---- waiting for the leave

        [Test]
        public void TheSceneIsRebuiltOnceBackOnTheMasterServer() =>
            Assert.AreEqual(ReturnStep.ReloadScene, BackToNameScreenRules.NextStep(ClientState.ConnectedToMasterServer));

        [TestCase(ClientState.Leaving)]
        [TestCase(ClientState.DisconnectingFromGameServer)]
        [TestCase(ClientState.ConnectingToMasterServer)]
        [TestCase(ClientState.Joined)]
        public void WhileTheLeaveIsStillRunningItWaits(ClientState state) =>
            Assert.AreEqual(ReturnStep.Wait, BackToNameScreenRules.NextStep(state));

        [TestCase(ClientState.Disconnected)]
        [TestCase(ClientState.PeerCreated)]
        public void IfTheConnectionIsGoneItReconnects(ClientState state) =>
            Assert.AreEqual(ReturnStep.Reconnect, BackToNameScreenRules.NextStep(state));

        // ---- which own body a rejoining player keeps

        [Test]
        public void TheBodyThisClientSpawnedItselfIsKeptNotTheLastRegisteredOne() =>
            Assert.AreEqual(12, BackToNameScreenRules.BodyToKeep(new[] { 7, 12 }, spawnedByThisClient: 12, lookupPointsAt: 7));

        [Test]
        public void WithNoSpawnOfItsOwnTheOneTheLookupPointsAtIsKept() =>
            Assert.AreEqual(7, BackToNameScreenRules.BodyToKeep(new[] { 7, 12 }, spawnedByThisClient: -1, lookupPointsAt: 7));

        [Test]
        public void ASpawnedBodyThatIsGoneFallsBackToTheLookup() =>
            Assert.AreEqual(7, BackToNameScreenRules.BodyToKeep(new[] { 7, 12 }, spawnedByThisClient: 99, lookupPointsAt: 7));

        [Test]
        public void WhenNeitherIsThereTheOldestBodyIsKept() =>
            Assert.AreEqual(5, BackToNameScreenRules.BodyToKeep(new[] { 9, 5 }, spawnedByThisClient: -1, lookupPointsAt: -1));

        [Test]
        public void NoBodiesMeansNothingToKeep() =>
            Assert.AreEqual(-1, BackToNameScreenRules.BodyToKeep(new int[0], spawnedByThisClient: 3, lookupPointsAt: 3));
    }
}
