using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Net;

namespace Overpower.Tests
{
    /// <summary>
    /// On the host a bot's body runs its abilities too, so an ability must ask whose body it is: the placer of a deployable is the
    /// casting body's owner (the instantiate's sender is the host), a body's own Player Properties go through PlayerProps, the Zip Gun
    /// refills on its own body's takedowns, and the camera, the aim outline, the shield ring's "my own" view and the portal
    /// traveller's Local belong to the human only.
    /// </summary>
    public class AbilityBotWiringTests
    {
        private static MethodInfo PlayerPropsSet => typeof(PlayerProps).GetMethod(nameof(PlayerProps.Set));
        private static MethodInfo IsLocalHuman => typeof(BodyRole).GetMethod(nameof(BodyRole.IsLocalHuman));
        private static MethodInfo OwnerActor => typeof(AbilityOwner).GetProperty(nameof(AbilityOwner.ActorNumber)).GetMethod;
        private static MethodInfo OwnerTeam => typeof(AbilityOwner).GetProperty(nameof(AbilityOwner.TeamId)).GetMethod;

        [Test]
        public void SpawnAppendsThePlacerToTheInstantiationData()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(NetworkedDeployable), nameof(NetworkedDeployable.Spawn),
                typeof(DeployablePlacement).GetMethod(nameof(DeployablePlacement.Append))));
        }

        [Test]
        public void OnPhotonInstantiateActsOnTheSplitOfThePlacementTail()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(NetworkedDeployable), nameof(NetworkedDeployable.OnPhotonInstantiate),
                typeof(DeployablePlacement).GetMethod(nameof(DeployablePlacement.TrySplit))));
        }

        [Test]
        public void TheFreshStartSweepTakesOnlyWhatIsSweptForTheGivenActor()
        {
            Assert.IsNull(typeof(NetworkedDeployable).GetMethod("DestroyAllPlacedByLocalPlayer"), "the one-body sweep is gone");
            Assert.IsNotNull(typeof(NetworkedDeployable).GetMethod(nameof(NetworkedDeployable.DestroyAllPlacedBy), new[] { typeof(int) }));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(NetworkedDeployable), nameof(NetworkedDeployable.DestroyAllPlacedBy),
                typeof(DeployablePlacement).GetMethod(nameof(DeployablePlacement.IsSweptFor))));
        }

        [TestCase(typeof(ElectricFenceAbility), "ExecuteCast")]
        [TestCase(typeof(DeployableCoverAbility), "ExecuteCast")]
        [TestCase(typeof(AoeZoneAbility), "ExecuteCast")]
        [TestCase(typeof(MineAbility), "PlaceMine")]
        [TestCase(typeof(TeleportAbility), "PlacePortal")]
        public void EveryDeployableIsPlacedForTheCastingBodysOwner(System.Type ability, string placingMethod)
        {
            Assert.IsTrue(IlWiring.Uses(ability, placingMethod, OwnerActor), "the placer's actor");
            Assert.IsTrue(IlWiring.Uses(ability, placingMethod, OwnerTeam), "the placer's team");
        }

        [Test]
        public void TheScopePublishesItsSightOnItsOwnBodysPlayer()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ScopeAbility), "SetHoldingSight", PlayerPropsSet));
        }

        [Test]
        public void TheScopeZoomsTheCameraOnlyForTheLocalHuman()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ScopeAbility), "ApplyOrRemove", IsLocalHuman));
        }

        [Test]
        public void TheHealZoneThrowIsWrittenOnTheCastersOwnPlayer()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneAbility), "ThrowOwnZone", PlayerPropsSet));
            Assert.IsTrue(IlWiring.Uses(typeof(AoeZoneAbility), "ClearOldThrow", PlayerPropsSet));
        }

        [Test]
        public void TheZipGunRefillsOnItsOwnBodysTakedownsOnly()
        {
            EventInfo takedown = typeof(CombatFeed).GetEvent(nameof(CombatFeed.Takedown));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), nameof(ZipGunAbility.OnEquip), typeof(CombatFeed).GetMethod(nameof(CombatFeed.For))));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), nameof(ZipGunAbility.OnEquip), OwnerActor), "the feed of this body's actor");
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), nameof(ZipGunAbility.OnEquip), takedown.GetAddMethod()));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), "OnDestroy", takedown.GetRemoveMethod()));
        }

        [Test]
        public void TheFlameAimOutlineIsStrongOnlyForTheLocalHuman()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(FlamethrowerAbility), "ShowVfx", IsLocalHuman));
        }

        [Test]
        public void TheShieldRingShowsAsTheViewersOwnOnlyForTheLocalHumansCast()
        {
            FieldInfo viewerIsCaster = typeof(InvulnerabilityAbility).GetField("windowViewerIsCaster", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(viewerIsCaster);
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "BeginWindow", IsLocalHuman));
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "Update", viewerIsCaster), "the ring's visibility reads it");
        }

        [Test]
        public void ThePortalFlagsAreWrittenOnTheirOwnBodysPlayer()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(TeleportAbility), "PublishReadyIfChanged", PlayerPropsSet));
            Assert.IsTrue(IlWiring.Uses(typeof(AllyPortalTraveller), "CompleteTrip", PlayerPropsSet));
        }

        [Test]
        public void ThePortalTravellersLocalIsTheHumansAndBotsAreNeverReadAsDropped()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(AllyPortalTraveller), "OnEnable", IsLocalHuman));
            Assert.IsTrue(IlWiring.Uses(typeof(AllyPortalTraveller), "FindStandingPortal",
                typeof(Presence).GetMethod(nameof(Presence.IsDropped))));
        }
    }
}
