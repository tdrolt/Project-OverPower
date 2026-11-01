using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// The match with bots in it: on the host a body's owner and the person at the keyboard are two different players, so the wallet, the
    /// scoreboard and the packs read their own body's player, the team counts ask Presence instead of the raw inactive flag, and the match's
    /// edges reach every body the client controls. IL checks, as the pure rules behind them are tested on their own.
    /// </summary>
    public class BotMatchWiringTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static MethodInfo Method(System.Type type, string name) => type.GetMethod(name, Any);

        private static MethodInfo LocalPlayerGetter => typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.LocalPlayer)).GetGetMethod();

        private static readonly MethodInfo IsDropped = typeof(Presence).GetMethod(nameof(Presence.IsDropped));
        private static readonly MethodInfo SetProps = typeof(PlayerProps).GetMethod(nameof(PlayerProps.Set));

        // ---- who counts as present -------------------------------------------------------------------

        [Test]
        public void TheTeamCountsAskPresenceNotTheRawInactiveFlag()
        {
            var failed = new List<string>();
            void Check(System.Type owner, string method)
            {
                if (!IlWiring.Uses(owner, method, IsDropped)) failed.Add(owner.Name + "." + method);
            }
            Check(typeof(MatchDirector), "BuildTeamStatuses");
            Check(typeof(MatchDirector), "RecomputeWhenAGraceEnds");
            Check(typeof(MatchDirector), nameof(MatchDirector.OnPlayerLeftRoom));
            Check(typeof(MatchDirector), "CountMembers");
            Check(typeof(ZonePresenceTracker), "IsCountable");
            Assert.IsEmpty(failed, "these still read Player.IsInactive: " + string.Join(", ", failed));
        }

        [Test]
        public void ThePlayerWhoJustCameBackFromTheDeadWaitsOnlyWhenNeitherTheMasterNorItsBotPlaysThem() =>
            Assert.IsTrue(IlWiring.Uses(typeof(ZonePresenceTracker), "IsCountable", typeof(ZoneThreat).GetMethod(nameof(ZoneThreat.WaitsForRespawnSettle))));

        // ---- the edges every controlled body takes ---------------------------------------------------

        [Test]
        public void TheGoLiveFreshStartAndThePhaseChangeReachEveryBodyTheClientControls()
        {
            System.Type director = typeof(MatchDirector);
            Assert.IsTrue(IlWiring.Uses(director, "ReactToRoomState", typeof(BodyRole).GetMethod(nameof(BodyRole.ControlledBodies))));
            Assert.IsTrue(IlWiring.Uses(director, "ReactToRoomState", Method(typeof(PlayerLifecycle), "ResetForMatchStart")), "the fresh start");
            Assert.IsTrue(IlWiring.Uses(director, "ReactToRoomState", Method(typeof(PlayerLifecycle), "ReturnToSpawnForPhaseChange")), "the trip home");
        }

        [Test]
        public void TheTripHomeAndTheBannerAreDecidedByTheRules()
        {
            System.Type director = typeof(MatchDirector);
            Assert.IsTrue(IlWiring.ResultDecidesABranch(director, "ReactToRoomState",
                typeof(MatchPhaseRules).GetMethod(nameof(MatchPhaseRules.SendsHomeOnPhaseChange))), "who is sent home");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(director, "ReactToRoomState",
                typeof(MatchPhaseRules).GetMethod(nameof(MatchPhaseRules.TwoTeamsLeftReaches))), "who is told");
        }

        [Test]
        public void AnEliminatedTeamIsJudgedByTheSameRuleForTheHumanAndForEachBot()
        {
            System.Type director = typeof(MatchDirector);
            MethodInfo rule = typeof(MatchPhaseRules).GetMethod(nameof(MatchPhaseRules.TeamJustEliminated));
            MethodInfo forPlayer = Method(director, "TeamJustEliminated");
            Assert.IsTrue(IlWiring.Uses(director, "ReactToRoomState", rule), "the human's");
            Assert.IsTrue(IlWiring.Uses(director, "ReactToRoomState", forPlayer), "a bot's");
            Assert.IsTrue(IlWiring.Uses(director, "TeamJustEliminated", rule));
        }

        // ---- a body reads and writes its own player --------------------------------------------------

        [Test]
        public void TheWalletOfABodyReadsAndWritesThePlayerOfThatBody()
        {
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(GoldWallet), LocalPlayerGetter), "on the host LocalPlayer is the human, whoever's wallet this is");
            Assert.IsTrue(IlWiring.Uses(typeof(GoldWallet), "PublishBalance", SetProps), "a bot's gold goes to the room through PlayerProps");
        }

        [Test]
        public void TheScoreboardOfABodyCountsItsOwnCreditAndPublishesAsItsOwnPlayer()
        {
            System.Type publisher = typeof(ScoreboardPublisher);
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(publisher, LocalPlayerGetter));
            Assert.IsTrue(IlWiring.Uses(publisher, "Publish", SetProps));
            Assert.IsTrue(IlWiring.Uses(publisher, "OnEnable", typeof(CombatFeed).GetMethod(nameof(CombatFeed.For))), "this body's feed");
            Assert.IsTrue(IlWiring.HandsOver(publisher, "OnEnable", Method(publisher, "HandlePlayerCredit")));
            Assert.IsTrue(IlWiring.Uses(publisher, "OnDisable", typeof(CombatFeed).GetEvent(nameof(CombatFeed.Credit)).GetRemoveMethod()),
                "and leaves the same feed it joined");
        }

        [Test]
        public void TheComebackBuffOfABodyNeverAsksWhoIsAtTheKeyboard() =>
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(OverPowerBuff), LocalPlayerGetter));

        // ---- the packs --------------------------------------------------------------------------------

        [Test]
        public void TheHumansPackRequestsComeFromTheHumansBodyAndAreWrittenThroughPlayerProps()
        {
            System.Type manager = typeof(HealthPackManager);
            Assert.IsTrue(IlWiring.ResultDecidesABranch(manager, "TryRequestLocal", typeof(BodyRole).GetMethod(nameof(BodyRole.IsLocalHuman))),
                "a bot body on the host is not the body to ask for");
            Assert.IsTrue(IlWiring.Uses(manager, "TryRequestLocal", SetProps));
        }

        [Test]
        public void ABotTakesAPackThroughTheSameBodyCheckAndTheSameMasterDecisionAsAHuman()
        {
            System.Type manager = typeof(HealthPackManager);
            MethodInfo grant = Method(manager, "Grant");
            Assert.IsTrue(IlWiring.Uses(manager, nameof(HealthPackManager.TryTakeForBot), Method(manager, "MayTake")), "the body-side check");
            Assert.IsTrue(IlWiring.Uses(manager, nameof(HealthPackManager.TryTakeForBot), grant), "the bot's take");
            Assert.IsTrue(IlWiring.Uses(manager, nameof(HealthPackManager.OnPlayerPropertiesUpdate), grant), "a player's request");
            Assert.IsTrue(IlWiring.Uses(manager, "Grant", typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.Decide))), "the one decision");
        }

        [Test]
        public void ABotIsHealedWhenTheHostReadsItsTakeBackLikeAHumanIs()
        {
            System.Type manager = typeof(HealthPackManager);
            Assert.IsTrue(IlWiring.Uses(manager, nameof(HealthPackManager.OnRoomPropertiesUpdate), Method(manager, "HealBotTaker")));
            Assert.IsTrue(IlWiring.Uses(manager, "HealBotTaker", typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.EchoIsMyFreshTake))),
                "only a take this host made for the bot, once");
            Assert.IsTrue(IlWiring.Uses(manager, "HealBotTaker", typeof(PlayerLookup).GetMethod(nameof(PlayerLookup.GetPhotonViewFor))), "the taker's own body");
            Assert.IsTrue(IlWiring.Uses(manager, "HealBotTaker", Method(manager, "ApplyPack")));
        }

        // ---- the towers -------------------------------------------------------------------------------

        [Test]
        public void ATeamMayCaptureAZoneByTheSameMapRuleTheTowersApply()
        {
            System.Type buildings = typeof(BuildingManager);
            MethodInfo mayCapture = typeof(TerritoryMap).GetMethod(nameof(TerritoryMap.MayCapture), new[]
            {
                typeof(int), typeof(int), typeof(IReadOnlyDictionary<int, int>), typeof(System.Func<int, bool>), typeof(System.Func<int, bool>),
            });
            Assert.IsNotNull(mayCapture);
            Assert.IsTrue(IlWiring.Uses(buildings, nameof(BuildingManager.MayTeamCapture), mayCapture));
            Assert.IsTrue(IlWiring.Uses(buildings, nameof(BuildingManager.MayTeamCapture), typeof(ZoneThreat).GetMethod(nameof(ZoneThreat.ZoneClosesLink))),
                "an owned neighbour under attack is no way in");
            Assert.IsTrue(IlWiring.Uses(buildings, nameof(BuildingManager.MayTeamCapture), typeof(MatchDirector).GetMethod(nameof(MatchDirector.IsOutOfPlay))),
                "nor is a zone out of play");
            Assert.IsTrue(IlWiring.Uses(buildings, nameof(BuildingManager.MayTeamCapture), buildings.GetMethod(nameof(BuildingManager.IsCapturableZone))));
        }
    }
}
