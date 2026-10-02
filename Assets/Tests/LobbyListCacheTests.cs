using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    public class LobbyListCacheTests
    {
        private static RoomSnapshot Room(string name, int players = 1, int max = 12, string lobbyName = null, int? mode = 1,
            int stage = 0, string host = "Host", bool removed = false, bool open = true, bool visible = true)
        {
            var props = new Hashtable();
            if (lobbyName != null) props[LobbyKeys.Name] = lobbyName;
            if (mode.HasValue) props[LobbyKeys.Mode] = mode.Value;
            props[LobbyKeys.Stage] = stage;
            if (host != null) props[LobbyKeys.Host] = host;
            return new RoomSnapshot
            {
                Name = name, RemovedFromList = removed, IsOpen = open, IsVisible = visible,
                PlayerCount = players, MaxPlayers = max, Properties = props,
            };
        }

        private static Dictionary<string, LobbyEntry> Merged(long now, params RoomSnapshot[] rooms)
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, rooms, now);
            return cache;
        }

        [Test]
        public void ANewRoomIsAddedWithItsProperties()
        {
            var cache = Merged(5, Room("L_a", 2, 12, "Alpha", 1, 0, "Tudor"));
            var e = cache["L_a"];
            Assert.AreEqual("Alpha", e.DisplayName);
            Assert.AreEqual(1, e.ModeId);
            Assert.AreEqual("Tudor", e.HostName);
            Assert.AreEqual(2, e.PlayerCount);
            Assert.AreEqual(12, e.MaxPlayers);
            Assert.AreEqual(5, e.FirstSeen);
        }

        [Test]
        public void AnUpdateForAKnownRoomReplacesItsEntry()
        {
            var cache = Merged(5, Room("L_a", 1, 12, "Alpha"));
            LobbyListCache.Merge(cache, new[] { Room("L_a", 3, 12, "Alpha", host: "Other", stage: 2) }, 9);
            Assert.AreEqual(1, cache.Count);
            Assert.AreEqual(3, cache["L_a"].PlayerCount);
            Assert.AreEqual("Other", cache["L_a"].HostName);
            Assert.AreEqual(2, cache["L_a"].Stage);
        }

        [Test]
        public void TheFirstSeenTimeSurvivesAnUpdate()
        {
            var cache = Merged(5, Room("L_a"));
            LobbyListCache.Merge(cache, new[] { Room("L_a", 4) }, 99);
            Assert.AreEqual(5, cache["L_a"].FirstSeen);
        }

        [Test]
        public void ARemovedRoomIsDropped()
        {
            var cache = Merged(1, Room("L_a"), Room("L_b"));
            LobbyListCache.Merge(cache, new[] { Room("L_a", removed: true) }, 2);
            CollectionAssert.AreEquivalent(new[] { "L_b" }, cache.Keys);
        }

        [Test]
        public void AClosedRoomIsDropped()
        {
            var cache = Merged(1, Room("L_a"));
            LobbyListCache.Merge(cache, new[] { Room("L_a", open: false) }, 2);
            Assert.IsEmpty(cache);
        }

        [Test]
        public void AnInvisibleRoomIsDropped()
        {
            var cache = Merged(1, Room("L_a"));
            LobbyListCache.Merge(cache, new[] { Room("L_a", visible: false) }, 2);
            Assert.IsEmpty(cache);
        }

        [Test]
        public void ARoomThatIsClosedWhenFirstSeenIsNeverAdded()
        {
            Assert.IsEmpty(Merged(1, Room("L_a", open: false)));
        }

        [Test]
        public void EntriesComeOutJoinableThenInMatchThenFull()
        {
            var cache = Merged(1,
                Room("full", 12, 12, "Full"),
                Room("match", 3, 12, "Match", stage: 2),
                Room("lobby", 2, 12, "Lobby"));
            var names = LobbyListCache.Sorted(cache.Values).Select(e => e.RoomName).ToArray();
            CollectionAssert.AreEqual(new[] { "lobby", "match", "full" }, names);
        }

        [Test]
        public void NewestFirstInsideAGroup()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { Room("old") }, 1);
            LobbyListCache.Merge(cache, new[] { Room("new") }, 2);
            LobbyListCache.Merge(cache, new[] { Room("old", 5) }, 3);
            var names = LobbyListCache.Sorted(cache.Values).Select(e => e.RoomName).ToArray();
            CollectionAssert.AreEqual(new[] { "new", "old" }, names);
        }

        [Test]
        public void ARoomFromAnOldBuildGetsFallbacksInsteadOfThrowing()
        {
            var old = new RoomSnapshot { Name = "Room_1234", IsOpen = true, IsVisible = true, PlayerCount = 1, MaxPlayers = 9, Properties = new Hashtable() };
            var cache = Merged(1, old);
            Assert.AreEqual("Room_1234", cache["Room_1234"].DisplayName);
            Assert.AreEqual("?", cache["Room_1234"].ModeIdText);
        }

        [Test]
        public void ARoomWithNullPropertiesAlsoGetsFallbacks()
        {
            var bare = new RoomSnapshot { Name = "x", IsOpen = true, IsVisible = true, PlayerCount = 0, MaxPlayers = 9, Properties = null };
            Assert.AreEqual("x", Merged(1, bare)["x"].DisplayName);
        }
    }
}
