using System.Reflection;
using NUnit.Framework;
using Overpower.Net;
using Overpower.UI;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// On the host photonView.IsMine is true for the human's body and for every bot's, so everything drawn for the person at this keyboard
    /// (HUD, minimap, shop, death card, bug key) is built only on the body BodyRole.IsLocalHuman names, and the screens that look at other
    /// players ask Presence.IsDropped, never Player.IsInactive (a bot is inactive to PUN but never gone). Each check fails with its wiring reverted.
    /// </summary>
    public class BotUiWiringTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo IsLocalHuman => typeof(BodyRole).GetMethod(nameof(BodyRole.IsLocalHuman));
        private static MethodInfo IsDropped => typeof(Presence).GetMethod(nameof(Presence.IsDropped));

        [Test]
        public void TheHudTheMinimapTheShopAndTheBugKeyAreBuiltOnlyOnTheLocalHumansBody()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerHud), "Awake", IsLocalHuman), "a bot's HUD would cover the host's screen and take over PlayerHud.Local");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(MinimapView), "Awake", IsLocalHuman), "a bot's minimap would take over MinimapView.Local");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(LoadoutScreen), "Awake", IsLocalHuman), "a bot's shop screen would register the P key");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(Overpower.Telemetry.BugMarkerKey), "Awake", IsLocalHuman), "one L press would mark a bug once per bot");
        }

        [Test]
        public void ABotsShopScreenNeverOpensItselfNorReleasesTheHumansOpenShop()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(LoadoutScreen), "Start", IsLocalHuman), "the late joiner's auto-open is the human's");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(LoadoutScreen), "OnDestroy", IsLocalHuman), "a dying bot body must not clear the static IsOpen under the human's shop");
        }

        [Test]
        public void TheDeathCardIsDrawnOnlyForTheLocalHumansBody() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DeathCard), "EnsureBuilt", IsLocalHuman));

        [Test]
        public void TheDeathCardStripeIsTheTeamOfTheBodysOwner() =>
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(DeathCard), "EnsureBuilt", typeof(PhotonView).GetProperty(nameof(PhotonView.Owner)).GetGetMethod()));

        [Test]
        public void TheEscapeMenuAndThePickYourBuildButtonFindTheLocalHumansBodyAndNoOther()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(QuitConfirmPanel), "ResolveLocalPlayer", IsLocalHuman));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "OpenTheShop", IsLocalHuman));
        }

        [Test]
        public void TheMinimapAndTheOvertimeCountAskPresenceSoABotIsNeitherMissingNorDead()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "UpdatePlayers", IsDropped));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionHud), "CountPlayers", IsDropped));
        }

        [Test]
        public void TheScoreboardNeverTellsABotFromAPlayerInAMatch()
        {
            // The BOT badge is the lobby's only: in a match nobody can tell a bot from a player.
            Assert.IsFalse(IlWiring.Uses(typeof(ScoreboardPanel), "ShowPlayer", typeof(BotRosterRules).GetMethod(nameof(BotRosterRules.IsBotActor))));
        }

        [Test]
        public void ThePlateColoursAskWhetherThePlateIsTheLocalHumansNotWhetherThisClientControlsIt()
        {
            // The rule's first argument is the answer BodyRole.IsLocalHuman gives; a bot on the host is never "yours".
            Assert.AreEqual(UnityEngine.Color.red, NameplateRules.NameColour(true, 0, 1, UnityEngine.Color.red, UnityEngine.Color.green, UnityEngine.Color.blue, UnityEngine.Color.grey));
            Assert.AreEqual(UnityEngine.Color.blue, NameplateRules.NameColour(false, 0, 1, UnityEngine.Color.red, UnityEngine.Color.green, UnityEngine.Color.blue, UnityEngine.Color.grey));
            Assert.IsTrue(NameplateRules.ShowsOwnBars(true));
            Assert.IsFalse(NameplateRules.ShowsOwnBars(false));
        }
    }
}
