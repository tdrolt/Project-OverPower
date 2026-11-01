using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Net;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// On the host photonView.IsMine is true for its human's body and for every bot body, so the player's own components answer "whose body is
    /// this" from the body (photonView.Owner, OwnerActorNr) and keep the screen, the camera and the local-only events for the human at this
    /// keyboard (BodyRole.IsLocalHuman). Each check fails with its wiring put back to PhotonNetwork.LocalPlayer or a bare IsMine.
    /// </summary>
    public class BotBodyIdentityTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo IsLocalHuman => typeof(BodyRole).GetMethod(nameof(BodyRole.IsLocalHuman));
        private static MethodInfo SetProps => typeof(PlayerProps).GetMethod(nameof(PlayerProps.Set));
        private static MethodInfo LocalPlayer => typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.LocalPlayer)).GetGetMethod();
        private static MethodInfo ViewGetter(string property) => typeof(PhotonView).GetProperty(property).GetGetMethod();

        [Test]
        public void TheCameraAndTheResultCatchUpFollowOnlyTheHumanAtThisKeyboard()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerLifecycle), "Start", IsLocalHuman),
                "a bot body the host spawns must not take the host's camera or replay its result panel");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerLifecycle), "RespawnPlayer", IsLocalHuman), "nor its respawn toast");
        }

        [Test]
        public void ADeathAndARespawnAreJudgedForTheBodysOwnSeat()
        {
            foreach (string method in new[] { "Start", "PlayerDied", "CheckForCathedralCapture", "ArrivalStampMs", "SetAlive", "SetLastStandOut" })
                Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), method, LocalPlayer), method + " must read photonView.Owner, not the local player");

            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), "PlayerDied", ViewGetter(nameof(PhotonView.OwnerActorNr))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), "PlayerDied", ViewGetter(nameof(PhotonView.Owner))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), "CheckForCathedralCapture", ViewGetter(nameof(PhotonView.Owner))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), "ArrivalStampMs", ViewGetter(nameof(PhotonView.Owner))));
        }

        [Test]
        public void TheAliveAndLastStandFlagsAreWrittenForTheBodysOwnSeat()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "SetAlive", SetProps));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "SetLastStandOut", SetProps));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), "SetAlive", ViewGetter(nameof(PhotonView.Owner))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), "SetLastStandOut", ViewGetter(nameof(PhotonView.Owner))));
        }

        [Test]
        public void AResetDestroysOnlyTheDeployablesOfTheBodyBeingReset()
        {
            MethodInfo byActor = typeof(NetworkedDeployable).GetMethod(nameof(NetworkedDeployable.DestroyAllPlacedBy));
            foreach (string method in new[] { "ResetForMatchStart", "ResetForRoundStart", "WaitDeadForSuddenDeath" })
            {
                Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), method, byActor), method);
                Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerLifecycle), method, ViewGetter(nameof(PhotonView.OwnerActorNr))), method);
            }
        }

        [Test]
        public void ABotLeavingIsNotReadAsADrop()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerLifecycle), "OnPlayerLeftRoom", typeof(Presence).GetMethod(nameof(Presence.IsDropped))));
        }

        [Test]
        public void TheLoadoutIsPublishedForTheBodysOwnSeat()
        {
            foreach (string method in new[] { "Start", "ResetForMatchStart", "SetWeapon", "SetAbility", "SetArmorLevels" })
            {
                Assert.IsTrue(IlWiring.Uses(typeof(PlayerLoadout), method, SetProps), method);
                Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(PlayerLoadout), method, LocalPlayer), method);
            }
        }

        [Test]
        public void TheStatusLabelIsPublishedForTheBodysOwnSeatAndAPeersStatusReachesItsController()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerStatusEffects), "PublishStatusLabel", SetProps));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerStatusEffects), "RequestOnOwner", ViewGetter(nameof(PhotonView.Controller))),
                "a bot cannot receive an RPC: the host that plays it does");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(PlayerStatusEffects), "RequestOnOwner", ViewGetter(nameof(PhotonView.Owner))));
        }

        [Test]
        public void AStatusShakesAndMarksOnlyTheScreenOfTheHumanAtThisKeyboard()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerStatusEffects), "Apply", IsLocalHuman), "stunning a bot must not shake the host's camera");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerStatusEffects), "Apply", typeof(PlayerStatusEffects).GetMethod("IsLocalHumanActor", Any)),
                "a bot's Raybeam must not draw the vulnerability mark on the host's screen");
        }

        [Test]
        public void TheShootersImpactPopsAreForTheHumanAtThisKeyboardOnAnyBodyButTheirOwn()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerHealth), "ApplyDamage", IsLocalHuman),
                "the host's human hitting a bot body the host simulates still sees where the shot landed");
        }

        [Test]
        public void TheKillersScoreIsWrittenThroughTheOneWriter()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "ApplyDamage", SetProps));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "ApplyDamage", typeof(PlayerProps).GetMethod(nameof(PlayerProps.RouteFor))),
                "a bot killer's score is skipped: nothing reads it and only the host may write a bot");
        }
    }
}
