using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Tests
{
    /// <summary>
    /// A bot's "player properties" live in the room as b{actor}.{key}, written by the host. These rules turn one into the
    /// other and plan which injected bot players a client must add or drop.
    /// </summary>
    public class BotRosterRulesTests
    {
        [Test]
        public void BotActorsStartAboveTheBase()
        {
            Assert.IsFalse(BotRosterRules.IsBotActor(1));
            Assert.IsFalse(BotRosterRules.IsBotActor(BotRosterRules.ActorBase));
            Assert.IsTrue(BotRosterRules.IsBotActor(BotRosterRules.ActorBase + 1));
        }

        [Test]
        public void APropertyKeyCarriesTheActorAndTheKey()
        {
            Assert.AreEqual("b1001.teamID", BotRosterRules.PropKey(1001, "teamID"));
            Assert.IsTrue(BotRosterRules.TryParsePropKey("b1001.teamID", out int actor, out string key));
            Assert.AreEqual(1001, actor);
            Assert.AreEqual("teamID", key);
        }

        [TestCase("bR")]
        [TestCase("bN")]
        [TestCase("bounty")]
        [TestCase("b.x")]
        [TestCase("b12x.y")]
        [TestCase("b1001.")]
        [TestCase("sT00")]
        [TestCase("b5.teamID")]
        public void OtherRoomKeysAreNotBotProperties(string roomKey)
        {
            Assert.IsFalse(BotRosterRules.TryParsePropKey(roomKey, out _, out _));
        }

        [Test]
        public void ToRoomPrefixesEveryKeyAndKeepsNullsSoTheyDeleteInTheMirror()
        {
            var room = BotRosterRules.ToRoom(1002, new Hashtable { { "gold", 40 }, { "alive", null } });
            Assert.AreEqual(2, room.Count);
            Assert.AreEqual(40, room["b1002.gold"]);
            Assert.IsTrue(room.ContainsKey("b1002.alive"));
            Assert.IsNull(room["b1002.alive"]);
        }

        [Test]
        public void SplitByBotGroupsRoomChangesPerBotAndSkipsTheRest()
        {
            var changed = new Hashtable { { "b1001.gold", 5 }, { "b1002.alive", false }, { "b1001.sb", new[] { 1 } }, { "lS", 1 }, { "bR", new[] { 1001 } } };
            Dictionary<int, Hashtable> split = BotRosterRules.SplitByBot(changed);
            Assert.AreEqual(2, split.Count);
            Assert.AreEqual(5, split[1001]["gold"]);
            Assert.IsTrue(split[1001].ContainsKey("sb"));
            Assert.AreEqual(false, split[1002]["alive"]);
        }

        [Test]
        public void MetaKeysAreNotMirroredAsPlayerProperties()
        {
            var changed = new Hashtable { { BotRosterRules.PropKey(1001, BotRosterRules.NickKey), "Mira" }, { "b1001.gold", 1 } };
            Dictionary<int, Hashtable> split = BotRosterRules.SplitByBot(changed);
            Assert.AreEqual(1, split[1001].Count);
            Assert.IsTrue(split[1001].ContainsKey("gold"));
        }

        [Test]
        public void TheRosterReadsAsAnEmptyListWhenAbsent()
        {
            CollectionAssert.IsEmpty(BotRosterRules.Roster(new Hashtable()));
            CollectionAssert.AreEqual(new[] { 1001, 1003 }, BotRosterRules.Roster(new Hashtable { { BotRosterRules.RosterKey, new[] { 1001, 1003 } } }));
        }

        [Test]
        public void TheNextActorIsNeverReused()
        {
            Assert.AreEqual(BotRosterRules.ActorBase + 1, BotRosterRules.NextActor(null));
            Assert.AreEqual(1007, BotRosterRules.NextActor(1007));
        }

        [Test]
        public void PlanAddsMissingBotsAndDropsOnesNoLongerInTheRoster()
        {
            var add = new List<int>();
            var remove = new List<int>();
            BotRosterRules.Plan(new[] { 1001, 1002 }, new[] { 1002, 1004 }, add, remove);
            CollectionAssert.AreEqual(new[] { 1001 }, add);
            CollectionAssert.AreEqual(new[] { 1004 }, remove);
        }

        [Test]
        public void RemovalTakesTheBotOutOfTheRosterAndNullsEveryOneOfItsKeys()
        {
            var room = new Hashtable
            {
                { BotRosterRules.RosterKey, new[] { 1001, 1002 } },
                { "b1001.gold", 3 }, { "b1001.~n", "Mira" }, { "b1002.gold", 9 }, { "lS", 0 },
            };
            Hashtable write = BotRosterRules.RemovalWrite(room, 1001);
            CollectionAssert.AreEqual(new[] { 1002 }, (int[])write[BotRosterRules.RosterKey]);
            Assert.IsTrue(write.ContainsKey("b1001.gold") && write["b1001.gold"] == null);
            Assert.IsTrue(write.ContainsKey("b1001.~n") && write["b1001.~n"] == null);
            Assert.IsFalse(write.ContainsKey("b1002.gold"));
            Assert.IsFalse(write.ContainsKey("lS"));
        }

        [Test]
        public void ClearAllEmptiesTheRosterAndNullsEveryBotKey()
        {
            var room = new Hashtable { { BotRosterRules.RosterKey, new[] { 1001, 1002 } }, { "b1001.gold", 3 }, { "b1002.alive", true }, { "lS", 2 } };
            Hashtable write = BotRosterRules.ClearAllWrite(room);
            CollectionAssert.IsEmpty((int[])write[BotRosterRules.RosterKey]);
            Assert.IsTrue(write.ContainsKey("b1001.gold") && write.ContainsKey("b1002.alive"));
            Assert.IsFalse(write.ContainsKey("lS"));
        }
    }
}
