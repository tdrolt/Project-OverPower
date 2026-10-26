using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Net;

namespace Overpower.Tests
{
    public class LoadoutPropertiesTests
    {
        [Test]
        public void AMissingKeyReturnsTheFallback()
        {
            var props = new Dictionary<object, object>();

            int result = LoadoutProperties.ReadInt(props, LoadoutProperties.WeaponKey, fallback: 7);

            Assert.AreEqual(7, result);
        }

        [Test]
        public void AWrongTypedValueReturnsTheFallbackWithoutThrowing()
        {
            var props = new Dictionary<object, object> { { LoadoutProperties.WeaponKey, "not an int" } };

            int result = LoadoutProperties.ReadInt(props, LoadoutProperties.WeaponKey, fallback: 7);

            Assert.AreEqual(7, result);
        }

        [Test]
        public void ANullDictionaryReturnsTheFallbackWithoutThrowing()
        {
            int result = LoadoutProperties.ReadInt(null, LoadoutProperties.WeaponKey, fallback: 3);

            Assert.AreEqual(3, result);
        }

        [Test]
        public void MinusOneReadsBackAsEmpty()
        {
            var props = new Dictionary<object, object> { { LoadoutProperties.AttachmentKey, -1 } };

            int result = LoadoutProperties.ReadInt(props, LoadoutProperties.AttachmentKey, fallback: 0);

            Assert.AreEqual(LoadoutProperties.Empty, result);
        }

        [Test]
        public void KeyForMapsAttachmentUltimateAndMobility()
        {
            Assert.AreEqual(LoadoutProperties.AttachmentKey, LoadoutProperties.KeyFor(AbilitySlot.Attachment));
            Assert.AreEqual(LoadoutProperties.UltimateKey, LoadoutProperties.KeyFor(AbilitySlot.Ultimate));
            Assert.AreEqual(LoadoutProperties.MobilityKey, LoadoutProperties.KeyFor(AbilitySlot.Mobility));
        }

        [Test]
        public void KeyForPrimaryReturnsNullBecauseWeaponsUseTheirOwnKey()
        {
            Assert.IsNull(LoadoutProperties.KeyFor(AbilitySlot.Primary));
        }
    }
}
