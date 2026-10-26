using System.Collections;
using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    public class LobbySeatsTests
    {
        private static readonly SeatLayout Layout = new SeatLayout(new[] { 0, 1 }, 2, 1);

        [Test]
        public void SeatKeysBecomeSeatsAndOtherKeysAreIgnored()
        {
            var props = new Hashtable { { "sT00", 3 }, { "sS0", 5 }, { "lS", 2 }, { "mMode", 3 }, { "sT20", 7 }, { 42, 9 } };
            var seats = LobbySeats.SeatsFrom(props, Layout);
            Assert.AreEqual(2, seats.Count);
            Assert.AreEqual(3, seats["sT00"]);
            Assert.AreEqual(5, seats["sS0"]);
        }

        [Test]
        public void NonIntAndNonPositiveValuesCountAsEmpty()
        {
            var props = new Hashtable { { "sT00", null }, { "sT01", 0 }, { "sT10", -4 }, { "sT11", "7" }, { "sS0", 2.5f } };
            Assert.AreEqual(0, LobbySeats.SeatsFrom(props, Layout).Count);
        }

        [Test]
        public void AByteOrShortValueIsStillAnActor()
        {
            var props = new Hashtable { { "sT00", (byte)4 }, { "sT01", (short)6 } };
            var seats = LobbySeats.SeatsFrom(props, Layout);
            Assert.AreEqual(4, seats["sT00"]);
            Assert.AreEqual(6, seats["sT01"]);
        }

        [Test]
        public void ANullTableGivesNoSeats()
        {
            Assert.AreEqual(0, LobbySeats.SeatsFrom(null, Layout).Count);
        }
    }
}
