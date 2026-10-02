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
        public void ARoomThatIsClosedWhenFirstSeenIsNeverAddedButItsNeighbourIs()
        {
            var cache = Merged(1, Room("L_closed", open: false), Room("L_open"));
            CollectionAssert.AreEquivalent(new[] { "L_open" }, cache.Keys);
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

        private static RoomSnapshot WithProps(IDictionary props) =>
            new RoomSnapshot { Name = "p", IsOpen = true, IsVisible = true, PlayerCount = 1, MaxPlayers = 9, Properties = props };

        [Test]
        public void IntegerPropertiesArriveAsAnyIntegerWidth()
        {
            // Photon can hand a number back as a byte, short or long depending on how it travelled
            var asByte = Merged(1, WithProps(new Hashtable { { LobbyKeys.Mode, (byte)3 }, { LobbyKeys.Stage, (byte)1 } }))["p"];
            Assert.AreEqual(3, asByte.ModeId);
            Assert.AreEqual(1, asByte.Stage);
            var asShort = Merged(1, WithProps(new Hashtable { { LobbyKeys.Mode, (short)2 } }))["p"];
            Assert.AreEqual(2, asShort.ModeId);
            var asLong = Merged(1, WithProps(new Hashtable { { LobbyKeys.Mode, 7L } }))["p"];
            Assert.AreEqual(7, asLong.ModeId);
        }

        [Test]
        public void ALongTooBigForAnIntFallsBack()
        {
            var e = Merged(1, WithProps(new Hashtable { { LobbyKeys.Mode, long.MaxValue } }))["p"];
            Assert.AreEqual(-1, e.ModeId);
        }

        [Test]
        public void ATextWhereANumberBelongsFallsBack()
        {
            var e = Merged(1, WithProps(new Hashtable { { LobbyKeys.Mode, "3" }, { LobbyKeys.Stage, "1" } }))["p"];
            Assert.AreEqual(-1, e.ModeId);
            Assert.AreEqual(0, e.Stage);
        }

        [Test]
        public void ARoomWithoutAHostShowsAQuestionMark()
        {
            var e = Merged(1, Room("L_a", host: null))["L_a"];
            Assert.AreEqual("?", e.HostName);
        }

        [Test]
        public void AnEmptyHostNameAlsoShowsAQuestionMark()
        {
            var e = Merged(1, WithProps(new Hashtable { { LobbyKeys.Host, "" } }))["p"];
            Assert.AreEqual("?", e.HostName);
        }

        [Test]
        public void TheRealPhotonHashtableReadsLikeAnyOther()
        {
            var props = new ExitGames.Client.Photon.Hashtable
            {
                { LobbyKeys.Name, "Alpha" }, { LobbyKeys.Mode, 1 }, { LobbyKeys.Stage, 2 }, { LobbyKeys.Host, "Tudor" },
            };
            var e = Merged(1, WithProps(props))["p"];
            Assert.AreEqual("Alpha", e.DisplayName);
            Assert.AreEqual(1, e.ModeId);
            Assert.AreEqual(2, e.Stage);
            Assert.AreEqual("Tudor", e.HostName);
        }

        [Test]
        public void NewestFirstUsesTheCreationStampWhenTheRoomCarriesOne()
        {
            // seen in the opposite order to how they were made: the stamp wins over first-seen
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { WithName("made-later", new Hashtable { { LobbyKeys.Created, 900 } }) }, 1);
            LobbyListCache.Merge(cache, new[] { WithName("made-earlier", new Hashtable { { LobbyKeys.Created, 100 } }) }, 2);
            var names = LobbyListCache.Sorted(cache.Values).Select(e => e.RoomName).ToArray();
            CollectionAssert.AreEqual(new[] { "made-later", "made-earlier" }, names);
        }

        [Test]
        public void ARoomWithoutAStampSortsByFirstSeenAfterStampedOnes()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { WithName("old-build", new Hashtable()) }, 5);
            LobbyListCache.Merge(cache, new[] { WithName("stamped", new Hashtable { { LobbyKeys.Created, 1 } }) }, 6);
            var names = LobbyListCache.Sorted(cache.Values).Select(e => e.RoomName).ToArray();
            CollectionAssert.AreEqual(new[] { "stamped", "old-build" }, names);
        }

        private static RoomSnapshot WithName(string name, IDictionary props) =>
            new RoomSnapshot { Name = name, IsOpen = true, IsVisible = true, PlayerCount = 1, MaxPlayers = 12, Properties = props };

        private static RoomSnapshot RoomWithFill(string name, string fill, int players = 1, int max = 9)
        {
            var props = new Hashtable { { LobbyKeys.Mode, 1 } };
            if (fill != null) props[LobbyKeys.Fill] = fill;
            return new RoomSnapshot { Name = name, IsOpen = true, IsVisible = true, PlayerCount = players, MaxPlayers = max, Properties = props };
        }

        private static readonly SeatLayout ThreeByThree = new SeatLayout(new[] { 0, 1, 2 }, 3, 3);

        [Test]
        public void TheSeatFillTextIsReadIntoTheEntry()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { RoomWithFill("a", "4/9+2") }, 1, id => ThreeByThree);
            var e = cache["a"];
            Assert.IsTrue(e.FillKnown);
            Assert.AreEqual(4, e.FilledTeamSeats);
            Assert.AreEqual(2, e.FilledSpectatorSeats);
            Assert.IsTrue(e.Layout.HasValue);
        }

        [Test]
        public void WithoutTheFillTextTheFillIsUnknown()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { RoomWithFill("a", null) }, 1, id => ThreeByThree);
            Assert.IsFalse(cache["a"].FillKnown);
        }

        [Test]
        public void TheButtonFollowsTheSeatFillWhenKnown()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            // two players only, but every team seat taken: not Join any more
            LobbyListCache.Merge(cache, new[] { RoomWithFill("full", "9/9+1", players: 2), RoomWithFill("open", "8/9+0", players: 12), RoomWithFill("none", "9/9+3", players: 2) }, 1, id => ThreeByThree);
            Assert.AreEqual(JoinAction.Spectate, LobbyListCache.ActionOf(cache["full"]));
            Assert.AreEqual(JoinAction.Join, LobbyListCache.ActionOf(cache["open"]));
            Assert.AreEqual(JoinAction.Full, LobbyListCache.ActionOf(cache["none"]));
        }

        [Test]
        public void TheButtonFallsBackToPlayerCountWithoutTheFill()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { RoomWithFill("a", null, players: 9, max: 9) }, 1, id => ThreeByThree);
            Assert.AreEqual(JoinAction.Full, LobbyListCache.ActionOf(cache["a"]));
        }

        [Test]
        public void TheButtonStillFollowsTheFillWhenTheModeIsUnknown()
        {
            var cache = new Dictionary<string, LobbyEntry>();
            LobbyListCache.Merge(cache, new[] { RoomWithFill("a", "9/9+0", players: 1, max: 12) }, 1);
            Assert.AreEqual(JoinAction.Full, LobbyListCache.ActionOf(cache["a"]));
        }
    }
}
