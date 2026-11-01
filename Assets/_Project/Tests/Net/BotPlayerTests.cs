using NUnit.Framework;
using Overpower.Net;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Tests
{
    /// <summary>
    /// The injected bot player: inactive to PUN (that is what makes the host the controller of its body), never "dropped" to the
    /// game, and its properties are set from the room mirror without a server call.
    /// </summary>
    public class BotPlayerTests
    {
        private sealed class HumanForTest : Player
        {
            public HumanForTest(int actor, bool inactive) : base("Human", actor, false) { IsInactive = inactive; }
        }

        [Test]
        public void ABotIsInactiveRemoteAndNamed()
        {
            var bot = new BotPlayer("Mira", 1001, 2);
            Assert.IsTrue(bot.IsInactive);
            Assert.IsFalse(bot.IsLocal);
            Assert.AreEqual(1001, bot.ActorNumber);
            Assert.AreEqual("Mira", bot.NickName);
            Assert.AreEqual(2, bot.Build);
        }

        [Test]
        public void ADroppedHumanIsDroppedButABotNever()
        {
            Assert.IsTrue(Presence.IsDropped(new HumanForTest(3, inactive: true)));
            Assert.IsFalse(Presence.IsDropped(new HumanForTest(3, inactive: false)));
            Assert.IsFalse(Presence.IsDropped(new BotPlayer("Mira", 1001, 0)));
            Assert.IsFalse(Presence.IsDropped(null));
        }

        [Test]
        public void MirrorMergesAndANullDeletesTheKey()
        {
            var bot = new BotPlayer("Mira", 1001, 0);
            bot.Mirror(new Hashtable { { "gold", 10 }, { "alive", true } });
            Assert.AreEqual(10, bot.CustomProperties["gold"]);
            bot.Mirror(new Hashtable { { "alive", null } });
            Assert.IsFalse(bot.CustomProperties.ContainsKey("alive"));
            Assert.AreEqual(10, bot.CustomProperties["gold"]);
        }

        [Test]
        public void ABotsPropertiesGoToTheRoomAndAHumansToTheServer()
        {
            Assert.AreEqual(PlayerProps.Route.BotRoom, PlayerProps.RouteFor(new BotPlayer("Mira", 1001, 0)));
            Assert.AreEqual(PlayerProps.Route.Server, PlayerProps.RouteFor(new HumanForTest(3, inactive: false)));
            Assert.AreEqual(PlayerProps.Route.None, PlayerProps.RouteFor(null));
        }
    }
}
