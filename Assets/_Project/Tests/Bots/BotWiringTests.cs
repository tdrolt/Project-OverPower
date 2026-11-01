using System.Reflection;
using NUnit.Framework;
using Overpower.Bots;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Net;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The bots are wired into the game: the lobby buttons, the roster, the body's hand-over to its bot, the host's spawn, the brain's
    /// decisions through the tested rules, and the assets (the prefab's BotSeat, BotConfig with its three builds, RoomManager's reference).
    /// Structure only: BotConfig's numbers are Tudor's.
    /// </summary>
    public class BotWiringTests
    {
        private static MethodInfo M(System.Type t, string name) =>
            t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        [Test]
        public void TheHostsClickOnABotSeatAsksTheRule() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(LobbyRoomPanel), "OnSeatClicked", M(typeof(LobbyRoomRules), nameof(LobbyRoomRules.ClickRemovesBot))));

        [Test]
        public void TheSeatsWriteBotsThroughTheirRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LobbySeats), nameof(LobbySeats.AddBots), M(typeof(BotSeatRules), nameof(BotSeatRules.AddBots))));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbySeats), nameof(LobbySeats.AddBots), M(typeof(BotSeatRules), nameof(BotSeatRules.PickName))));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbySeats), nameof(LobbySeats.RemoveBot), M(typeof(BotSeatRules), nameof(BotSeatRules.RemoveBot))));
        }

        [Test]
        public void TheRosterPlansFromTheRoomAndTheHostSpawnsEveryBotSeat()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotRoster), "Reconcile", M(typeof(BotRosterRules), nameof(BotRosterRules.Plan))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotRoster), "Reconcile", M(typeof(BotRosterRules), nameof(BotRosterRules.SplitByBot))));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyStart), "SpawnBots", M(typeof(RoomManager), nameof(RoomManager.SpawnBotOnTeam))));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyStart), "ReactToStart", M(typeof(LobbyStart), "SpawnBots")));
        }

        [Test]
        public void ABodyIsHandedToItsBotOnlyWhenItsDataNamesOne() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotSeat), "Awake", M(typeof(BotSeat), nameof(BotSeat.TryReadActor))));

        [Test]
        public void ABotsPropertiesAreRoutedByTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerProps), nameof(PlayerProps.Set), M(typeof(PlayerProps), nameof(PlayerProps.RouteFor))));

        [Test]
        public void TheBrainDecidesThroughTheTestedRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "PickTarget", M(typeof(BotTargetRules), nameof(BotTargetRules.Pick))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Think", M(typeof(BotHealthRules), nameof(BotHealthRules.Stance))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "AimAndFire", M(typeof(BotFireRules), nameof(BotFireRules.ShouldFire))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "AimAndFire", M(typeof(BotAimRules), nameof(BotAimRules.ErrorMetres))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "AimAndFire", M(typeof(BotAimRules), nameof(BotAimRules.TurnToward))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotBrain), "Vent", M(typeof(BotVentRules), nameof(BotVentRules.PressNow))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "FightPoint", M(typeof(BotRangeRules), nameof(BotRangeRules.Decide))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(ConquestGoalRules), nameof(ConquestGoalRules.Choose))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "DominionGoal", M(typeof(DominionGoalRules), nameof(DominionGoalRules.Choose))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ShopConquest", M(typeof(BotShopRules), nameof(BotShopRules.MostExpensiveAffordable))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ShopDominion", M(typeof(BotShopRules), nameof(BotShopRules.DominionTarget))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Steer", M(typeof(BotPathRules), nameof(BotPathRules.NextCorner))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Steer", M(typeof(BotPathRules), nameof(BotPathRules.IsStuck))));
        }

        [Test]
        public void EachBuildsAbilitiesAreUsedByItsRules()
        {
            foreach (string rule in new[] { "Flame", "Zip", "Fence", "Cover", "Sprint", "HealZone", "Scope", "Blink", "Raybeam" })
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseAbilities", M(typeof(BotAbilityRules), rule)), rule);
        }

        [Test]
        public void TheWalkableMapIsBuiltFromTheRulesSources() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotNavMesh), nameof(BotNavMesh.Build), M(typeof(BotPathRules), nameof(BotPathRules.IsWalkSource))));

        [Test]
        public void ThePlayerPrefabCarriesTheBotSeat()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Design/Resources/Multiplayer Player.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<BotSeat>());
            var order = typeof(BotSeat).GetCustomAttribute<DefaultExecutionOrder>();
            Assert.IsNotNull(order);
            Assert.Less(order.order, 0, "it runs before every other Awake of the body");
        }

        [Test]
        public void BotConfigHasThreeCompleteBuildsAndRoomManagerReadsIt()
        {
            var config = AssetDatabase.LoadAssetAtPath<BotConfig>("Assets/_Project/Design/Settings/BotConfig.asset");
            Assert.IsNotNull(config);
            Assert.AreEqual(3, config.Builds.Length);
            foreach (BotConfig.Build b in config.Builds)
            {
                Assert.IsNotNull(b.FamilyWeapon, b.Name);
                Assert.IsNotNull(b.UpgradeWeapon, b.Name);
                Assert.AreSame(b.FamilyWeapon, b.UpgradeWeapon.Parent, b.Name + ": the upgrade is the family's");
                Assert.AreEqual(AbilitySlot.Attachment, b.Attachment.Slot, b.Name);
                Assert.AreEqual(AbilitySlot.Mobility, b.Movement.Slot, b.Name);
                Assert.AreEqual(AbilitySlot.Ultimate, b.Ultimate.Slot, b.Name);
                Assert.LessOrEqual(b.FightRangeMin, b.FightRangeMax, b.Name);
            }
            Assert.IsNotEmpty(config.Names);

            var systems = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Match/Prefabs/Match Systems.prefab");
            Assert.AreSame(config, systems.GetComponentInChildren<RoomManager>(true).BotSettings);
        }

        [Test]
        public void EveryBotConfigNumberHasAPlayTermsTooltipAndIsInTheAsset()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/BotConfig.asset").Replace("\r\n", "\n");
            foreach (FieldInfo field in typeof(BotConfig).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.GetCustomAttribute<SerializeField>() == null) continue;
                StringAssert.Contains("\n  " + field.Name + ":", yaml, field.Name + " is written into the asset");
                if (field.Name == "builds") continue;
                Assert.IsNotNull(field.GetCustomAttribute<TooltipAttribute>(), field.Name + " has a tooltip");
            }
        }
    }
}
