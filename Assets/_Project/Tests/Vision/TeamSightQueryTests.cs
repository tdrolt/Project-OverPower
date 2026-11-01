using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Net;
using Overpower.Vision;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A bot's team asks what it sees through TeamSightQuery, with no TeamSight and no local player: the same eyes and wall test as the
    /// human's sight, for any team, and everything seen when the fog is off.</summary>
    public class TeamSightQueryTests
    {
        private static readonly SightShape Normal = new SightShape(90f, 20f, 5f);
        private static readonly SightShape Scoped = new SightShape(10f, 40f, 2f);

        private VisionConfig config;

        [TearDown]
        public void TearDown()
        {
            TeamSightQuery.Configure(null, 0f, 0f, 0f);
            if (config != null)
                Object.DestroyImmediate(config);
        }

        private VisionConfig MakeConfig(bool fogEnabled)
        {
            config = ScriptableObject.CreateInstance<VisionConfig>();
            var serialized = new SerializedObject(config);
            serialized.FindProperty("fogEnabled").boolValue = fogEnabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static SightCandidate C(int team, bool alive, float x, float z, bool scoped = false) =>
            new SightCandidate(team, alive, false, new Vector2(x, z), Vector2.up, 0f, scoped);

        [Test]
        public void WithoutAConfigEverythingIsSeen()
        {
            TeamSightQuery.Configure(null, 0f, 0f, 0f);
            Assert.IsTrue(TeamSightQuery.CanTeamSee(0, new Vector3(100f, 0f, 100f)));
            Assert.IsTrue(TeamSightQuery.CanTeamSeeBody(0, null));
        }

        [Test]
        public void WithTheFogOffEveryTeamSeesEveryPointAndEveryBody()
        {
            TeamSightQuery.Configure(MakeConfig(false), 10f, 40f, -2f);
            foreach (int team in new[] { -1, 0, 1, 2 })
            {
                Assert.IsTrue(TeamSightQuery.CanTeamSee(team, new Vector3(500f, 3f, -500f)), "team " + team + " sees a far point");
                Assert.IsTrue(TeamSightQuery.CanTeamSeeBody(team, null), "team " + team + " sees a body that is not there");
            }
        }

        [Test]
        public void WithTheFogOnAndNoOneInTheRoomATeamSeesNothing()
        {
            Assert.IsNull(Photon.Pun.PhotonNetwork.CurrentRoom, "the edit-mode tests are not in a room");
            TeamSightQuery.Configure(MakeConfig(true), 10f, 40f, -2f);
            Assert.IsFalse(TeamSightQuery.CanTeamSee(0, Vector3.zero), "no eyes, no sight");
            Assert.IsFalse(TeamSightQuery.CanTeamSee(-1, Vector3.zero), "a team that has not arrived has no eyes either");
        }

        [Test]
        public void ABodyThatIsNotThereIsNotHiddenEvenWithTheFogOn()
        {
            TeamSightQuery.Configure(MakeConfig(true), 10f, 40f, -2f);
            Assert.IsTrue(TeamSightQuery.CanTeamSeeBody(0, null));
        }

        [Test]
        public void EveryLivingMemberOfTheTeamIsAnEyeWhoeverItIs()
        {
            var candidates = new List<SightCandidate> { C(1, true, 0, 0), C(1, true, 10, 0), C(1, false, 20, 0), C(2, true, 30, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(candidates, 1, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.AreEqual(2, eyes.Count, "a bot team's eyes are its living bodies, no body of the team is 'the local one'");
            Assert.AreEqual(new Vector2(0, 0), eyes[0].Position);
            Assert.AreEqual(new Vector2(10, 0), eyes[1].Position);
        }

        [Test]
        public void AnotherTeamsEyesAreNotMine()
        {
            var candidates = new List<SightCandidate> { C(1, true, 0, 0), C(2, true, 10, 0), C(0, true, 20, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(candidates, 2, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.AreEqual(1, eyes.Count);
            Assert.AreEqual(new Vector2(10, 0), eyes[0].Position);
        }

        [Test]
        public void ATeamThatHasNotArrivedHasNoEyes()
        {
            var candidates = new List<SightCandidate> { C(-1, true, 0, 0), C(1, true, 10, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(candidates, -1, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.IsEmpty(eyes, "with no team asked about and nobody local, nobody is an eye");
        }

        [Test]
        public void AScopedTeammateSeesWithTheScopedShape()
        {
            var candidates = new List<SightCandidate> { C(1, true, 0, 0, scoped: true), C(1, true, 10, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(candidates, 1, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.AreEqual(Scoped.ConeLength, eyes[0].Shape.ConeLength);
            Assert.AreEqual(Normal.ConeLength, eyes[1].Shape.ConeLength);
        }

        [Test]
        public void TheScopeShapeIsTheNarrowLongConeWithASmallerCircle()
        {
            SightShape scoped = VisionRules.ShapeFor(90f, 22f, 7f, true, 20f, 40f, -3f);
            Assert.AreEqual(20f, scoped.ConeAngleDegrees);
            Assert.AreEqual(40f, scoped.ConeLength);
            Assert.AreEqual(4f, scoped.CircleRadius, 1e-5f);
        }

        [Test]
        public void TheQueryAsksTheSameRulesAsTheHumansSight()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(TeamSightQuery), nameof(TeamSightQuery.CanTeamSee),
                typeof(VisionRules).GetMethod(nameof(VisionRules.TeamSeesWithEye))), "any eye in shape with a clear line");
            Assert.IsTrue(IlWiring.Uses(typeof(TeamSightQuery), "EyesOf", typeof(VisionRules).GetMethod(nameof(VisionRules.ShapeFor))),
                "the normal and the scoped shape come from the config and the Scope's numbers");
            Assert.IsTrue(IlWiring.Uses(typeof(TeamSightQuery), "EyesOf",
                typeof(SightEyes).GetMethod(nameof(SightEyes.Build), new[]
                {
                    typeof(IReadOnlyList<SightCandidate>), typeof(int), typeof(ViewerMode), typeof(int), typeof(SightShape), typeof(SightShape), typeof(List<Eye>),
                })), "the eyes are built by the same rule");
            Assert.IsTrue(IlWiring.Uses(typeof(TeamSightQuery), "ClearLine",
                typeof(ShotVisibilityRules).GetMethod(nameof(ShotVisibilityRules.TargetHeight))), "the wall test aims at the same height");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(TeamSightQuery), "ClearLine",
                typeof(Physics).GetMethod(nameof(Physics.Linecast), new[] { typeof(Vector3), typeof(Vector3), typeof(int), typeof(QueryTriggerInteraction) })),
                "a Linecast on the Building layer");
        }

        [Test]
        public void TheQueryCountsAPlayerAsAliveByThePresenceRuleAndNeverAsTheLocalOne()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(TeamSightQuery), "Gather", typeof(Presence).GetMethod(nameof(Presence.IsDropped))),
                "a bot is inactive to Photon but never dropped");
            var inactive = typeof(Photon.Realtime.Player).GetProperty(nameof(Photon.Realtime.Player.IsInactive)).GetGetMethod();
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSightQuery), inactive), "IsInactive is only read through Presence");
            var isMine = typeof(PhotonView).GetProperty(nameof(PhotonView.IsMine)).GetGetMethod();
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSightQuery), isMine), "the host controls every bot, so IsMine says nothing about whose eyes");
        }

        [Test]
        public void TheQueryDoesNotNeedTheHumansSight()
        {
            var local = typeof(TeamSight).GetProperty(nameof(TeamSight.Local)).GetGetMethod();
            Assert.IsFalse(IlWiring.Uses(typeof(TeamSightQuery), local), "a bot team's sight does not depend on TeamSight.Local");
            var localPlayer = typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.LocalPlayer)).GetGetMethod();
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(TeamSightQuery), localPlayer), "nor on the person at this keyboard");
        }
    }
}
