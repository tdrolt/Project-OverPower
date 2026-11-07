using System.Reflection;
using NUnit.Framework;
using Overpower.Bots;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;
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
        public void TheXOnABotsSeatShowsByTheRuleAndItsClickTakesTheBotOut()
        {
            MethodInfo rule = M(typeof(LobbyRoomRules), nameof(LobbyRoomRules.ShowsRemoveBot));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyRoomPanel), "Redraw", rule));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(LobbyRoomPanel), "OnRemoveBotClicked", rule));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyRoomPanel), "OnRemoveBotClicked", M(typeof(LobbySeats), nameof(LobbySeats.RemoveBot))));
        }

        [Test]
        public void AClickOnASeatAsksForItEvenOnABotsSeat()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyRoomPanel), "OnSeatClicked", M(typeof(LobbySeats), nameof(LobbySeats.TryTake))));
            Assert.IsFalse(IlWiring.Uses(typeof(LobbyRoomPanel), "OnSeatClicked", M(typeof(LobbySeats), nameof(LobbySeats.RemoveBot))), "only the x takes a bot out");
        }

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
        public void TheNewMasterClaimsABotBodyBeforeRemovingIt()
        {
            MethodInfo claim = typeof(Photon.Pun.PhotonView).GetProperty(nameof(Photon.Pun.PhotonView.ControllerActorNr)).GetSetMethod();
            MethodInfo destroy = typeof(Photon.Pun.PhotonNetwork).GetMethod(nameof(Photon.Pun.PhotonNetwork.Destroy), new[] { typeof(Photon.Pun.PhotonView) });
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(BotRoster), "TakeAllBotsOut", claim), "a dropped host still controls the bodies here");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(BotRoster), "TakeAllBotsOut", destroy));
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
        public void TheBrainPutsItsZoneFirstAndKeepsACalmPaceThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseGoal", M(typeof(BotFocusRules), nameof(BotFocusRules.ShouldEngage))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "RecentMemory", M(typeof(BotFocusRules), nameof(BotFocusRules.ChaseMemory))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "InSpawnOffLimits", M(typeof(BotSpawnRules), nameof(BotSpawnRules.OffLimits))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "PickTarget", M(typeof(BotBrain), "LeftAlone")), "an enemy in its own spawn is no target");
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseGoal", M(typeof(BotPaceRules), nameof(BotPaceRules.KeepGoal))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "PauseNowAndThen", M(typeof(BotPaceRules), nameof(BotPaceRules.Pause))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "FightPoint", M(typeof(BotPaceRules), nameof(BotPaceRules.Pause))), "a strafe may stand still");
        }

        [Test]
        public void TheBrainPlaysWithItsTeamThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Think", M(typeof(BotTeamRules), nameof(BotTeamRules.Outnumbered))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Think", M(typeof(BotTeamRules), nameof(BotTeamRules.Stance))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "TeammateFights", M(typeof(BotTeamRules), nameof(BotTeamRules.FightingNearTeammate))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "PickTarget", M(typeof(BotBrain), "TeammatesFightTarget")), "no target of its own: it joins a teammate's fight");
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ReadScan", M(typeof(BotTeamRules), nameof(BotTeamRules.ScanShowsUs))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ReadScan", M(typeof(BotTeamRules), nameof(BotTeamRules.ScanSweptFrom))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ReadScan", M(typeof(CentreScanRules), nameof(CentreScanRules.FrontSwept))));
        }

        [Test]
        public void ARefusedPressIsRetriedOnlyAfterTheWait() =>
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Ready", M(typeof(BotPressRules), nameof(BotPressRules.MayPressAgain))));

        [Test]
        public void EachBuildsAbilitiesAreUsedByItsRules()
        {
            foreach (string rule in new[] { "Flame", "Zip", "ZipEscape", "Fence", "Cover", "Sprint", "HealZone", "Scope", "Blink", "Raybeam" })
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseAbilities", M(typeof(BotAbilityRules), rule)), rule);
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ScoutAhead", M(typeof(BotAbilityRules), nameof(BotAbilityRules.Scout))));
        }

        [Test]
        public void TheWalkableMapIsBuiltFromTheRulesSources() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotNavMesh), nameof(BotNavMesh.Build), M(typeof(BotPathRules), nameof(BotPathRules.IsWalkSource))));

        [Test]
        public void TheBrainFinishesTeamsThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ReadLastStands", M(typeof(BotHuntRules), nameof(BotHuntRules.LastStand))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "IsHunted", M(typeof(BotHuntRules), nameof(BotHuntRules.Hunted))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Think", M(typeof(BotBrain), "ReadLastStands")));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "PickTarget", M(typeof(BotBrain), "IsHunted")), "a team in its last stand is a target at any distance");
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseGoal", M(typeof(BotBrain), "IsHunted")), "...and is chased");
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotPushRules), nameof(BotPushRules.Exposed))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotPushRules), nameof(BotPushRules.PushAnyway))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotPushRules), nameof(BotPushRules.Forced))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotPushRules), nameof(BotPushRules.KeepTaking))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotBrain), "CaptureDone")));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "CaptureDone", M(typeof(BotPushRules), nameof(BotPushRules.CaptureDone01))));
        }

        [Test]
        public void TheBrainSpreadsOutThroughTheRulesInBothModes()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotSpreadRules), nameof(BotSpreadRules.Spreading))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", M(typeof(BotSpreadRules), nameof(BotSpreadRules.PushedBack))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "DominionGoal", M(typeof(BotSpreadRules), nameof(BotSpreadRules.Spreading))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotBrain), "HoldWhileSpreading", M(typeof(BotSpreadRules), nameof(BotSpreadRules.HoldZone))));
            foreach (string mode in new[] { "ConquestGoal", "DominionGoal" })
            {
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), mode, M(typeof(BotBrain), "HoldWhileSpreading")), mode);
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), mode, M(typeof(BotBrain), "CountTeammatesHeading")), mode);
            }
        }

        [Test]
        public void TheBrainTakesFreeZonesFirstThroughTheRules()
        {
            MethodInfo free = M(typeof(BotCaptureRules), nameof(BotCaptureRules.FreeZone));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ConquestGoal", free));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "DominionGoal", free));
            foreach (string field in new[] { nameof(ConquestZone.EnemyOnIt), nameof(ConquestZone.Standing) })
            {
                Assert.IsTrue(IlWiring.Stores(typeof(BotBrain), "ConquestGoal", typeof(ConquestZone).GetField(field)), field);
                Assert.IsTrue(IlWiring.Stores(typeof(BotBrain), "DominionGoal", typeof(DominionZone).GetField(field)), field);
            }
            foreach (string mode in new[] { "ConquestGoal", "DominionGoal" })
            {
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), mode, typeof(BotConfig).GetProperty(nameof(BotConfig.FreeZoneEnemySeconds)).GetGetMethod()), mode);
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), mode, typeof(BotConfig).GetProperty(nameof(BotConfig.FreeZoneSpreadMetres)).GetGetMethod()), mode);
            }
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotBrain), "ChooseGoal", M(typeof(BotCaptureRules), nameof(BotCaptureRules.EngageOnFreeZone))),
                "on its way to a free zone it fights only an enemy that hit it or is in weapon range");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotBrain), "ChooseGoal", M(typeof(BotCaptureRules), nameof(BotCaptureRules.StayOnZone))),
                "on a free zone it shoots from the zone");
            FieldInfo objectiveFree = typeof(BotBrain).GetField("objectiveFree", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string method in new[] { "PauseNowAndThen", "ScoutAhead" })
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), method, objectiveFree), method + ": no stop on the way to a free zone");
        }

        [Test]
        public void TheLaserBotSupportsAPartnerThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChoosePartner", M(typeof(BotSupportRules), nameof(BotSupportRules.PickPartner))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "SupportPoint", M(typeof(BotSupportRules), nameof(BotSupportRules.FollowPoint))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotBrain), "WaitingToWalkOut", M(typeof(BotSupportRules), nameof(BotSupportRules.WaitForTeammate))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseGoal", M(typeof(BotBrain), "WaitingToWalkOut")));
            foreach (string mode in new[] { "ConquestGoal", "DominionGoal" })
            {
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), mode, M(typeof(BotBrain), "ChoosePartner")), mode);
                Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), mode, M(typeof(BotBrain), "SupportPoint")), mode);
            }
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BotBrain), "ScoutAhead", M(typeof(BotAbilityRules), nameof(BotAbilityRules.ScoutDue))));
        }

        [Test]
        public void AHurtBotUsesATeammatesHealZoneThroughTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "TryHealZone", M(typeof(BotHealZoneRules), nameof(BotHealZoneRules.WalkIn))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "TryHealZone", M(typeof(BotDirector), nameof(BotDirector.HealZones))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ChooseGoal", M(typeof(BotBrain), "TryHealZone")));
        }

        [Test]
        public void SpawnsAreShotBackIntoAndKeptOutOfThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "LeftAlone", M(typeof(BotSpawnRules), nameof(BotSpawnRules.LeaveAlone))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "OffLimits", M(typeof(BotSpawnRules), nameof(BotSpawnRules.KeepOut))));
        }

        [Test]
        public void RoutesComeFromTwoMapsAndTightBendsAreTakenThroughTheRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotNavMesh), nameof(BotNavMesh.TryRoute), M(typeof(BotPathRules), nameof(BotPathRules.TakeWide))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotNavMesh), nameof(BotNavMesh.TryRoute), M(typeof(BotPathRules), nameof(BotPathRules.RouteMetres))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotNavMesh), nameof(BotNavMesh.Centred), M(typeof(BotPathRules), nameof(BotPathRules.GapMiddle))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotNavMesh), nameof(BotNavMesh.Centred), M(typeof(BotPathRules), nameof(BotPathRules.Across))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Steer", M(typeof(BotNavMesh), nameof(BotNavMesh.TryRoute))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Steer", M(typeof(BotPathRules), nameof(BotPathRules.CornerSpeed))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Steer", M(typeof(BotBrain), "CentreBends")));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "CentreBends", M(typeof(BotNavMesh), nameof(BotNavMesh.Centred))));
        }

        [Test]
        public void EachBotThinksShopsAndReadsTheMatchOnItsOwnBeat()
        {
            MethodInfo roll = typeof(UnityEngine.Random).GetProperty(nameof(UnityEngine.Random.value)).GetGetMethod();
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(BotBrain), "Init", roll), "a random phase per bot");
            foreach (string field in new[] { "nextThinkAt", "nextShopAt", "nextLastStandReadAt" })
                Assert.IsTrue(IlWiring.Stores(typeof(BotBrain), "Init", typeof(BotBrain).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)), field);
        }

        [Test]
        public void TheBotsBuysAndPicksGoIntoItsMatchLog()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "Logged",
                M(typeof(Overpower.Telemetry.PlayerTelemetry), nameof(Overpower.Telemetry.PlayerTelemetry.HandlePurchased))));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ShopConquest", M(typeof(BotBrain), "Logged")));
            Assert.IsTrue(IlWiring.Uses(typeof(BotBrain), "ShopDominion", M(typeof(BotBrain), "Logged")));
        }

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
