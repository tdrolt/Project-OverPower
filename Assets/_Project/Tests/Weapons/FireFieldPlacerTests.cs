using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Net;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>
    /// A fire field burns for whoever fired the rocket. The host spawns a bot's field, so the object's sender is the host; the shooter travels
    /// in the field's instantiation data and every client reads it from there, falling back to the sender when the slot is missing.
    /// </summary>
    public class FireFieldPlacerTests
    {
        [Test]
        public void TheShooterInTheDataWinsOverTheHostThatSpawnedTheField()
        {
            object[] data = { 2.5f, 12f, 3f, 4, 1001 };
            Assert.AreEqual(1001, FireField.PlacerActor(data, senderActor: 1));
        }

        [Test]
        public void AHumansFieldBurnsForTheSamePlayerAsItsSender()
        {
            object[] data = { 2.5f, 12f, 3f, 4, 2 };
            Assert.AreEqual(2, FireField.PlacerActor(data, senderActor: 2));
        }

        [Test]
        public void WithoutTheShooterSlotTheSenderIsTheShooter()
        {
            Assert.AreEqual(3, FireField.PlacerActor(new object[] { 2.5f, 12f, 3f, 4 }, senderActor: 3));
            Assert.AreEqual(3, FireField.PlacerActor(null, senderActor: 3));
            Assert.AreEqual(3, FireField.PlacerActor(new object[] { 2.5f, 12f, 3f, 4, "not an actor" }, senderActor: 3));
        }

        [Test]
        public void EveryClientTakesTheShooterAndItsTeamFromTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), nameof(FireField.OnPhotonInstantiate), typeof(FireField).GetMethod(nameof(FireField.PlacerActor))),
                "the field asks who fired it");
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), nameof(FireField.OnPhotonInstantiate),
                typeof(Teams).GetMethod(nameof(Teams.TryGetTeam), new[] { typeof(int), typeof(int).MakeByRefType() })), "and burns in that player's team");
            Assert.IsFalse(IlWiring.Uses(typeof(FireField), nameof(FireField.OnPhotonInstantiate),
                typeof(Teams).GetMethod(nameof(Teams.TryGetTeam), new[] { typeof(Photon.Realtime.Player), typeof(int).MakeByRefType() })),
                "never the sender's team, which is the host's for a bot's field");
        }

        [Test]
        public void TheCursorRocketSendsItsShooterWithTheField()
        {
            MethodInfo spawn = typeof(FireField).GetMethod(nameof(FireField.Spawn));
            ParameterInfo placer = spawn.GetParameters().SingleOrDefault(p => p.Name == "placerActor");
            Assert.IsNotNull(placer, "Spawn takes the shooter");
            Assert.AreEqual(typeof(int), placer.ParameterType);
            Assert.IsTrue(IlWiring.Uses(typeof(DetonateAtCursor), nameof(DetonateAtCursor.OnExpired), spawn), "the rocket spawns its field through Spawn");
            Assert.IsTrue(IlWiring.Uses(typeof(DetonateAtCursor), nameof(DetonateAtCursor.OnExpired),
                typeof(ProjectileContext).GetProperty(nameof(ProjectileContext.ShooterActorNumber)).GetMethod), "with the shot's shooter");
        }
    }
}
