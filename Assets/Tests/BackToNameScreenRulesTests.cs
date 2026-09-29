using NUnit.Framework;
using Overpower.Match;
using Photon.Realtime;

namespace Overpower.Tests
{
    /// <summary>Task 9f (Tudor D22): the result screen leads back to the name screen; and the rejoin body choice (9e-3 review).</summary>
    public class BackToNameScreenRulesTests
    {
        // ---- the panel button

        [Test]
        public void AfterTheMatchTheButtonGoesBackToTheNameScreen() =>
            Assert.AreEqual(ResultButtonAction.BackToNameScreen, BackToNameScreenRules.ButtonAction(matchOver: true));

        [Test]
        public void DuringTheMatchTheButtonStillClosesTheGame() =>
            Assert.AreEqual(ResultButtonAction.CloseGame, BackToNameScreenRules.ButtonAction(matchOver: false));

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
