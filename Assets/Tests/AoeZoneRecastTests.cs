using NUnit.Framework;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>The AoE Zone throw (D11): a cursor inside range is used as is, further is cut to the
    /// range in the same direction, height is flattened; one throw per zone; and a thrown-spot value
    /// only ever moves the zone it names.</summary>
    public class AoeZoneRecastTests
    {
        [Test]
        public void CursorThreeMetresAwayLandsThreeMetresAway()
        {
            Vector3 p = AoeZoneRecast.LandingPoint(new Vector3(10f, 1f, 20f), new Vector3(13f, 1f, 20f), 5f);
            Assert.AreEqual(13f, p.x, 0.001f);
            Assert.AreEqual(20f, p.z, 0.001f);
        }

        [Test]
        public void CursorNineMetresAwayIsCutToFiveInTheSameDirection()
        {
            Vector3 caster = new Vector3(10f, 1f, 20f);
            Vector3 p = AoeZoneRecast.LandingPoint(caster, new Vector3(10f, 1f, 29f), 5f);
            Assert.AreEqual(10f, p.x, 0.001f);
            Assert.AreEqual(25f, p.z, 0.001f);

            Vector3 diag = AoeZoneRecast.LandingPoint(Vector3.zero, new Vector3(6f, 0f, 8f), 5f);
            Assert.AreEqual(3f, diag.x, 0.001f);
            Assert.AreEqual(4f, diag.z, 0.001f);
        }

        [Test]
        public void HeightIsFlattenedToTheCastersGround()
        {
            Vector3 p = AoeZoneRecast.LandingPoint(new Vector3(0f, 2f, 0f), new Vector3(3f, 7f, 0f), 5f);
            Assert.AreEqual(2f, p.y, 0.001f);
            Vector3 far = AoeZoneRecast.LandingPoint(new Vector3(0f, 2f, 0f), new Vector3(30f, -4f, 0f), 5f);
            Assert.AreEqual(2f, far.y, 0.001f);
        }

        [Test]
        public void CursorOnTheCasterKeepsTheCasterSpot()
        {
            Vector3 p = AoeZoneRecast.LandingPoint(new Vector3(4f, 1f, 4f), new Vector3(4f, 1f, 4f), 5f);
            Assert.AreEqual(4f, p.x, 0.001f);
            Assert.AreEqual(4f, p.z, 0.001f);
        }

        [Test]
        public void RecastIsAllowedWithALiveZoneThatWasNotThrownYet()
            => Assert.IsTrue(AoeZoneRecast.MayRecast(ownZoneAlive: true, alreadyThrown: false));

        [Test]
        public void RecastIsNotAllowedAfterTheThrow()
            => Assert.IsFalse(AoeZoneRecast.MayRecast(ownZoneAlive: true, alreadyThrown: true));

        [Test]
        public void RecastIsNotAllowedWithNoZoneOrAfterItEnded()
        {
            Assert.IsFalse(AoeZoneRecast.MayRecast(ownZoneAlive: false, alreadyThrown: false));
            Assert.IsFalse(AoeZoneRecast.MayRecast(ownZoneAlive: false, alreadyThrown: true));
        }

        [Test]
        public void AThrowValueMovesOnlyTheZoneItNames()
        {
            object[] raw = AoeZoneRecast.Encode(4001, new Vector3(1f, 2f, 3f));
            Assert.IsTrue(AoeZoneRecast.TryDecode(raw, 4001, out Vector3 p));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), p);
            Assert.IsFalse(AoeZoneRecast.TryDecode(raw, 4002, out _), "a value for another zone is ignored");
        }

        [Test]
        public void MissingOrMalformedValuesAreIgnored()
        {
            Assert.IsFalse(AoeZoneRecast.TryDecode(null, 1, out _));
            Assert.IsFalse(AoeZoneRecast.TryDecode("junk", 1, out _));
            Assert.IsFalse(AoeZoneRecast.TryDecode(new object[] { 1, 2f }, 1, out _));
            Assert.IsFalse(AoeZoneRecast.TryDecode(new object[] { "a", 1f, 2f, 3f }, 1, out _));
        }
    }
}
