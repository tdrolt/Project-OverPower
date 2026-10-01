using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    public class SightEyesTests
    {
        static readonly SightShape Shape = new SightShape(90f, 20f, 5f);

        static SightCandidate C(int team, bool alive, bool isLocal, float x, float z) =>
            new SightCandidate(team, alive, isLocal, new Vector2(x, z), Vector2.up);

        [Test] public void Mode_SpectatingWinsOverEverything() => Assert.AreEqual(ViewerMode.Spectating, SightEyes.ModeFor(true, false));
        [Test] public void Mode_SpectatingWinsEvenIfAlive() => Assert.AreEqual(ViewerMode.Spectating, SightEyes.ModeFor(true, true));
        [Test] public void Mode_NotAliveAndNotSpectating_IsDead() => Assert.AreEqual(ViewerMode.Dead, SightEyes.ModeFor(false, false));
        [Test] public void Mode_Alive_IsAlive() => Assert.AreEqual(ViewerMode.Alive, SightEyes.ModeFor(false, true));

        [Test]
        public void Build_AliveViewer_UsesSelfAndLivingTeammatesOnly()
        {
            var list = new List<SightCandidate>
            {
                C(1, true, true, 0, 0),    // me
                C(1, true, false, 10, 0),  // teammate
                C(1, false, false, 20, 0), // dead teammate
                C(2, true, false, 30, 0),  // enemy
            };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Shape, eyes);
            Assert.AreEqual(2, eyes.Count);
            Assert.AreEqual(new Vector2(0, 0), eyes[0].Position);
            Assert.AreEqual(new Vector2(10, 0), eyes[1].Position);
        }

        [Test]
        public void Build_DeadViewer_UsesLivingTeammatesNotSelf()
        {
            // the local candidate is alive here, so only the Dead rule can exclude it
            var list = new List<SightCandidate> { C(1, true, true, 0, 0), C(1, true, false, 10, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Dead, -1, Shape, eyes);
            Assert.AreEqual(1, eyes.Count);
            Assert.AreEqual(new Vector2(10, 0), eyes[0].Position);
        }

        [Test]
        public void Build_Spectating_UsesTheWatchedTeam()
        {
            var list = new List<SightCandidate> { C(1, true, true, 0, 0), C(2, true, false, 10, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Spectating, 2, Shape, eyes);
            Assert.AreEqual(1, eyes.Count);
            Assert.AreEqual(new Vector2(10, 0), eyes[0].Position);
        }

        [Test]
        public void Build_CarriesFacingAndShape_AndClearsTheOutputFirst()
        {
            var eyes = new List<Eye> { new Eye(Vector2.one, Vector2.one, Shape) };
            var list = new List<SightCandidate> { new SightCandidate(1, true, true, new Vector2(3, 4), new Vector2(0, -1)) };
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Shape, eyes);
            Assert.AreEqual(1, eyes.Count);
            Assert.AreEqual(new Vector2(0, -1), eyes[0].Facing);
            Assert.AreEqual(Shape.ConeLength, eyes[0].Shape.ConeLength);
        }

        [Test] public void Friendly_WhileSpectating_IsTheWatchedTeam() => Assert.AreEqual(2, SightEyes.FriendlyTeam(ViewerMode.Spectating, 1, 2));
        [Test] public void Friendly_AliveOrDead_IsMyTeam()
        {
            Assert.AreEqual(1, SightEyes.FriendlyTeam(ViewerMode.Alive, 1, 2));
            Assert.AreEqual(1, SightEyes.FriendlyTeam(ViewerMode.Dead, 1, 2));
        }

        [Test]
        public void Build_EachEyeKeepsItsOwnHeight()
        {
            var list = new List<SightCandidate>
            {
                new SightCandidate(1, true, true, Vector2.zero, Vector2.up, 1.5f),
                new SightCandidate(1, true, false, Vector2.one, Vector2.up, 4.25f),
            };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Shape, eyes);
            Assert.AreEqual(1.5f, eyes[0].EyeY);
            Assert.AreEqual(4.25f, eyes[1].EyeY);
        }
    }
}
