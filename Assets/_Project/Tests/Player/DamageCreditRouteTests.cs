using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Net;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>Where a victim's credit for an attacker goes when the attacker may be a bot the host plays (a bot cannot receive an RPC), how the
    /// receiving body tells a bot's own credit from a bot victim's credit for a human, and that the credit lands on the attacker's own feed.</summary>
    public class DamageCreditRouteTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo Credit(string name) => typeof(PlayerCombatCredit).GetMethod(name, Any);
        private static MethodInfo ViewGetter(string property) => typeof(PhotonView).GetProperty(property).GetGetMethod();

        [Test]
        public void AHumanAttackersCreditGoesToTheirOwnClientWhoeverSendsIt()
        {
            Assert.AreEqual(PlayerCombatCredit.CreditRoute.AttackersClient, PlayerCombatCredit.RouteFor(false, false));
            Assert.AreEqual(PlayerCombatCredit.CreditRoute.AttackersClient, PlayerCombatCredit.RouteFor(false, true), "the host's bot victim credits a human too");
        }

        [Test]
        public void ABotAttackersCreditStaysOnTheHostOrTravelsThroughTheBotsBodyToIt()
        {
            Assert.AreEqual(PlayerCombatCredit.CreditRoute.BotBodyHere, PlayerCombatCredit.RouteFor(true, true), "the host plays the bot: no message");
            Assert.AreEqual(PlayerCombatCredit.CreditRoute.BotBodyOnHost, PlayerCombatCredit.RouteFor(true, false), "a human victim's client tells the host");
        }

        [Test]
        public void OnlyACreditMessageTheBotsControllerDidNotSendIsTheBotsOwn()
        {
            Assert.IsTrue(PlayerCombatCredit.IsCreditForThisBot(true, false), "a victim's client sent it through the bot's body");
            Assert.IsFalse(PlayerCombatCredit.IsCreditForThisBot(true, true), "the host sent it: the bot is the victim crediting a human");
            Assert.IsFalse(PlayerCombatCredit.IsCreditForThisBot(false, false), "a human's body is never a bot's credit");
            Assert.IsFalse(PlayerCombatCredit.IsCreditForThisBot(false, true));
        }

        [Test]
        public void TheCreditMessageBranchesOnTheTestedRuleAndChecksTheSenderAgainstTheController()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerCombatCredit), "RPC_DamageCredit", Credit(nameof(PlayerCombatCredit.IsCreditForThisBot))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "RPC_DamageCredit", typeof(BodyRole).GetMethod(nameof(BodyRole.IsBot))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(PlayerCombatCredit), "RPC_DamageCredit", ViewGetter(nameof(PhotonView.Controller))),
                "the host sends a bot victim's credit, so the anti-spoof check is the controller");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(PlayerCombatCredit), "RPC_DamageCredit", ViewGetter(nameof(PhotonView.Owner))),
                "the owner of a bot victim is the bot, which never sends anything");
        }

        [Test]
        public void SendingCreditAsksTheTestedRoute()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "SendCredit", Credit(nameof(PlayerCombatCredit.RouteFor))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "SendCredit", Credit("Credit")), "the host credits its own bot without a message");
        }

        [Test]
        public void ACreditLandsOnTheAttackersOwnFeed()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "Credit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.For))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "Credit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseDamageDealt))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "Credit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseEnemyAffected))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "Credit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseCredit))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "Credit", typeof(CombatFeed).GetMethod(nameof(CombatFeed.RaiseTakedown))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerCombatCredit), "Credit", typeof(PlayerHealth).GetMethod(nameof(PlayerHealth.NoteDealtDamage))),
                "dealing damage keeps the attacker's own body in combat");
        }

        [Test]
        public void TheUltimateMeterListensToItsOwnBodysFeed()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(UltimateCharge), "OnEnable", typeof(CombatFeed).GetMethod(nameof(CombatFeed.For))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(UltimateCharge), "OnEnable", ViewGetter(nameof(PhotonView.OwnerActorNr))),
                "the feed of this body's actor, not the local player's");
            Assert.IsTrue(IlWiring.HandsOver(typeof(UltimateCharge), "OnEnable", typeof(UltimateCharge).GetMethod("HandleDamageDealt", Any)));
            Assert.IsTrue(IlWiring.HandsOver(typeof(UltimateCharge), "OnEnable", typeof(UltimateCharge).GetMethod("HandleTakedown", Any)));
            Assert.IsTrue(IlWiring.HandsOver(typeof(UltimateCharge), "OnDisable", typeof(UltimateCharge).GetMethod("HandleDamageDealt", Any)),
                "and leaves the same feed");
        }
    }
}
