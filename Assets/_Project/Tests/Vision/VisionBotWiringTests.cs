using System.Reflection;
using NUnit.Framework;
using Overpower.Net;
using Overpower.Vision;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>The host controls every bot body, so a bot body is IsMine there. The sight, the fog and the hiding of enemies belong to the human at this
    /// keyboard: they ask BodyRole.IsLocalHuman, hide an enemy bot like an enemy human, and count a bot as present through Presence.</summary>
    public class VisionBotWiringTests
    {
        private static readonly MethodInfo IsLocalHuman = typeof(BodyRole).GetMethod(nameof(BodyRole.IsLocalHuman));
        private static readonly MethodInfo IsDropped = typeof(Presence).GetMethod(nameof(Presence.IsDropped));
        private static readonly MethodInfo IsMine = typeof(PhotonView).GetProperty(nameof(PhotonView.IsMine)).GetGetMethod();
        private static readonly MethodInfo IsInactive = typeof(Photon.Realtime.Player).GetProperty(nameof(Photon.Realtime.Player.IsInactive)).GetGetMethod();
        private static readonly MethodInfo OwnView = typeof(MonoBehaviourPun).GetProperty(nameof(MonoBehaviourPun.photonView)).GetGetMethod();

        [Test]
        public void OnlyTheLocalHumansBodyGetsASightPictureAndBecomesTheLocalSight()
        {
            foreach (string method in new[] { "Awake", "OnEnable", "OnDisable", "OnDestroy" })
                Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(TeamSight), method, IsLocalHuman),
                    "TeamSight." + method + " acts on whether this is the human's body, not whether the host controls it");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSight), IsMine),
                "TeamSight never asks IsMine: on the host a bot body answers yes");
        }

        [Test]
        public void TheSightsOwnBodyIsTheBodyItSitsOn_NotWhicheverBodyTheHostControls()
        {
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSight), nameof(TeamSight.CanSeePlayer), IsMine),
                "an enemy bot on the host is IsMine and must be hidden in the fog like an enemy human");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSight), "Refresh", IsMine),
                "a bot teammate's eye is not the human's own eye");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(TeamSight), nameof(TeamSight.CanSeePlayer), OwnView),
                "my own body is the one this sight sits on");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(TeamSight), "Refresh", OwnView),
                "my own eye, alive state and Scope are the ones of the body this sight sits on");
        }

        [Test]
        public void ABotCountsAsPresentWhenTheEyesAndTheScanDotsAreGathered()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(TeamSight), "Refresh", IsDropped), "a bot teammate is an eye");
            Assert.IsTrue(IlWiring.Uses(typeof(CentreScan), "CatchEnemies", IsDropped), "an enemy bot leaves a scan dot");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSight), IsInactive), "IsInactive is true for every bot");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(CentreScan), IsInactive));
        }

        [Test]
        public void AnEnemyBotIsHiddenInTheFogOnTheHostLikeAnEnemyHuman()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(EnemyVisibility), "Awake", IsLocalHuman),
                "only the human's own body skips the hiding");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(EnemyVisibility), IsMine));
            Assert.IsTrue(IlWiring.Uses(typeof(EnemyVisibility), "LateUpdate", typeof(TeamSight).GetMethod(nameof(TeamSight.CanSeePlayer))),
                "every other body, bots included, is shown only while its team is seen");
        }
    }
}
