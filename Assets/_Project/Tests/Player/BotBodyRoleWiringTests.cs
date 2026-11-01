using System.Reflection;
using NUnit.Framework;
using Overpower.Net;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// On the host, photonView.IsMine is true for the human's body and for every bot's. Each player component that means "the person
    /// at this keyboard" (devices, mouse aim, own-screen views, the death card and result panels, the own plate, F1's reset, the tower
    /// re-report) asks BodyRole.IsLocalHuman instead; what means "this client simulates the body" (the alive gate, a cast's caster)
    /// keeps IsMine or the view's owner. Each check fails with its wiring reverted.
    /// </summary>
    public class BotBodyRoleWiringTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo IsLocalHuman => typeof(BodyRole).GetMethod(nameof(BodyRole.IsLocalHuman));
        private static MethodInfo IsBot => typeof(BodyRole).GetMethod(nameof(BodyRole.IsBot));
        private static MethodInfo Getter(System.Type type, string property) => type.GetProperty(property, Any).GetGetMethod(true);

        [Test]
        public void OnlyTheLocalHumansBodyTurnsOnTheDeviceMap() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerInputRouter), "OnEnable", IsLocalHuman),
                "a bot body on the host would otherwise read the human's keyboard and mouse");

        [Test]
        public void EveryBodyThisClientControlsStillFollowsItsAliveGate()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerInputRouter), "OnEnable", Getter(typeof(PhotonView), nameof(PhotonView.IsMine))),
                "the alive gate is the controller's, a bot's included");
            Assert.IsTrue(IlWiring.HandsOver(typeof(PlayerInputRouter), "OnEnable", typeof(PlayerInputRouter).GetMethod("HandleAliveChanged", Any)));
        }

        [Test]
        public void OnlyTheLocalHumansClicksAreCheckedAgainstThePanels() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerInputRouter), "Update", IsLocalHuman));

        [Test]
        public void TheMouseAimsOnlyTheLocalHumansBody() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerAim), "Update", IsLocalHuman),
                "a bot with no aim override would otherwise turn to the host's cursor");

        [Test]
        public void ABotsMoveSkipsTheCameraTurn() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerMotor), nameof(PlayerMotor.MovementInput),
                Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.BotDriven))));

        [Test]
        public void TheCrosshairTheAimConeAndTheChargeRingAreBuiltOnlyOnTheLocalHumansBody()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(CrosshairView), "Awake", IsLocalHuman), "crosshair");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(AimConeView), "Awake", IsLocalHuman), "aim cone");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(ChargeRingView), "Awake", IsLocalHuman), "charge ring");
        }

        [Test]
        public void TheDeathCardTheResultPanelsAndTheSpectateButtonAreTheLocalHumansOnly()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), "EnsureCard", IsLocalHuman), "death card");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MatchUi), nameof(MatchUi.ShowMatchResult), IsLocalHuman), "win / lose result");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), nameof(MatchUi.ShowYouLost), IsBot), "the lose panel never covers the host's screen for a bot");
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), "EnsureSpectateView", IsLocalHuman), "spectate button");
        }

        [Test]
        public void OnlyTheLocalHumansPlateIsDrawnAsTheirOwn()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "Start", IsLocalHuman), "own bars");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "UpdateNameTagColour", IsLocalHuman), "self colour");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(PlayerNameTag), Getter(typeof(PhotonView), nameof(PhotonView.IsMine))),
                "IsMine paints every bot on the host as the host's own plate");
        }

        [Test]
        public void OnlyTheLocalHumansCooldownsAreResetFromTheTestRange() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(AbilityRunner), "OnEnable", IsLocalHuman));

        [Test]
        public void ACastIsCreditedToTheBodysOwnerBecauseTheHostSendsEveryBotsCast()
        {
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(AbilityRunner), "RPC_CastAbility", Getter(typeof(PhotonView), nameof(PhotonView.OwnerActorNr))),
                "caster actor");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(AbilityRunner), "RPC_CastAbility", Getter(typeof(PhotonView), nameof(PhotonView.Owner))),
                "caster team");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(AbilityRunner), "RPC_CastAbility", Getter(typeof(PhotonView), nameof(PhotonView.IsMine))),
                "the caster's own client is the one controlling the body");
        }

        [Test]
        public void OnlyTheLocalHumanIsRememberedInATowerForANewMaster() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BuildingCapture), "OnTriggerEnter", IsLocalHuman));

        [Test]
        public void SpectatingAsksWhetherAPlayerDroppedNotWhetherPhotonCallsThemInactive() =>
            Assert.IsTrue(IlWiring.Uses(typeof(SpectateView), "CollectLiving", typeof(Presence).GetMethod(nameof(Presence.IsDropped))));
    }
}
