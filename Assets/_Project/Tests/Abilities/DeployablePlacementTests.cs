using NUnit.Framework;
using Overpower.Abilities;

namespace Overpower.Tests
{
    /// <summary>
    /// The tail Spawn appends to a deployable's instantiationData (timestamp, then the placer) and the fresh-start sweep's
    /// choice. The host instantiates every bot's deployable, so the placer must travel in the data, and a bot's reset must
    /// take only that bot's deployables while a match without bots sweeps exactly what it always did.
    /// </summary>
    public class DeployablePlacementTests
    {
        private const int Human = 1;
        private const int Bot = 1001;
        private const int Dropped = 5;

        [Test]
        public void AppendedDataSplitsBackIntoTheSubclassDataTheTimestampAndThePlacer()
        {
            object[] raw = DeployablePlacement.Append(new object[] { 3, 42 }, 123456, Bot, 2);

            Assert.IsTrue(DeployablePlacement.TrySplit(raw, out object[] data, out int placedMs, out bool hasPlacer, out int actor, out int team));
            CollectionAssert.AreEqual(new object[] { 3, 42 }, data, "the subclass's own indices never move");
            Assert.AreEqual(123456, placedMs);
            Assert.IsTrue(hasPlacer);
            Assert.AreEqual(Bot, actor);
            Assert.AreEqual(2, team);
        }

        [Test]
        public void NoSubclassDataSplitsBackIntoAnEmptyArrayNotNull()
        {
            object[] raw = DeployablePlacement.Append(null, 7, Human, 0);

            Assert.IsTrue(DeployablePlacement.TrySplit(raw, out object[] data, out _, out bool hasPlacer, out int actor, out _));
            Assert.IsNotNull(data);
            Assert.AreEqual(0, data.Length);
            Assert.IsTrue(hasPlacer);
            Assert.AreEqual(Human, actor);
        }

        [Test]
        public void AnUnknownTeamTravelsAsMinusOne()
        {
            object[] raw = DeployablePlacement.Append(null, 7, Bot, -1);

            DeployablePlacement.TrySplit(raw, out _, out _, out _, out _, out int team);
            Assert.AreEqual(-1, team);
        }

        [Test]
        public void DataWithOnlyATimestampHasNoPlacerSoTheOwnerFallsBackToTheSender()
        {
            Assert.IsTrue(DeployablePlacement.TrySplit(new object[] { 9, 5000 }, out object[] data, out int placedMs, out bool hasPlacer, out _, out _));
            CollectionAssert.AreEqual(new object[] { 9 }, data);
            Assert.AreEqual(5000, placedMs);
            Assert.IsFalse(hasPlacer);
        }

        [Test]
        public void SubclassWholeNumbersBeforeTheTailAreNeverReadAsThePlacer()
        {
            object[] raw = DeployablePlacement.Append(new object[] { 1, 2, 3 }, 100, Bot, 1);

            DeployablePlacement.TrySplit(raw, out object[] data, out int placedMs, out _, out int actor, out _);
            CollectionAssert.AreEqual(new object[] { 1, 2, 3 }, data);
            Assert.AreEqual(100, placedMs);
            Assert.AreEqual(Bot, actor);
        }

        [Test]
        public void MissingOrMalformedDataIsRefusedAndHandedBackWhole()
        {
            Assert.IsFalse(DeployablePlacement.TrySplit(null, out object[] fromNull, out _, out _, out _, out _));
            Assert.IsNull(fromNull);
            Assert.IsFalse(DeployablePlacement.TrySplit(new object[0], out _, out _, out _, out _, out _));

            var noTimestamp = new object[] { "portal" };
            Assert.IsFalse(DeployablePlacement.TrySplit(noTimestamp, out object[] whole, out _, out bool hasPlacer, out int actor, out _));
            Assert.AreSame(noTimestamp, whole);
            Assert.IsFalse(hasPlacer);
            Assert.AreEqual(-1, actor);
        }

        [Test]
        public void ABotsSweepTakesOnlyThatBotsDeployables()
        {
            Assert.IsTrue(DeployablePlacement.IsSweptFor(true, true, Bot, Bot, false));
            Assert.IsFalse(DeployablePlacement.IsSweptFor(true, true, Human, Bot, false), "the host human's own stay");
            Assert.IsFalse(DeployablePlacement.IsSweptFor(true, true, Bot + 1, Bot, false), "another bot's stay");
            Assert.IsFalse(DeployablePlacement.IsSweptFor(true, false, Dropped, Bot, false), "what a dropped player left stays");
        }

        [Test]
        public void TheHostHumansSweepNeverTakesABotsDeployables()
        {
            Assert.IsFalse(DeployablePlacement.IsSweptFor(true, true, Bot, Human, true));
            Assert.IsTrue(DeployablePlacement.IsSweptFor(true, true, Human, Human, true));
        }

        [Test]
        public void ACopyAnotherClientControlsIsNeverSwept()
        {
            Assert.IsFalse(DeployablePlacement.IsSweptFor(false, false, Human, Human, true));
            Assert.IsFalse(DeployablePlacement.IsSweptFor(false, true, Bot, Bot, false));
        }

        [Test]
        public void WithoutBotsTheLocalPlayersSweepTakesEverythingThisClientControlsAsBefore()
        {
            Assert.IsTrue(DeployablePlacement.IsSweptFor(true, true, Human, Human, true), "its own");
            Assert.IsTrue(DeployablePlacement.IsSweptFor(true, false, Dropped, Human, true), "what a dropped player left this client in control of");
            Assert.IsFalse(DeployablePlacement.IsSweptFor(false, false, Dropped, Human, true), "nothing it does not control");
        }
    }
}
