using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    public class MinimapLinkStyleTests
    {
        [Test]
        public void BothEndsOwnedByOneTeamIsASolidLineInThatTeam()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(1, 1);
            Assert.AreEqual(MinimapLinkKind.Owned, s.Kind);
            Assert.AreEqual(1, s.Team);
        }

        [Test]
        public void OwnedToNeutralIsAWayInPointingAtTheNeutralZone()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(2, -1);
            Assert.AreEqual(MinimapLinkKind.WayIn, s.Kind);
            Assert.AreEqual(2, s.Team);
            Assert.IsTrue(s.TowardB);
        }

        [Test]
        public void NeutralToOwnedPointsTheOtherWay()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(-1, 0);
            Assert.AreEqual(MinimapLinkKind.WayIn, s.Kind);
            Assert.AreEqual(0, s.Team);
            Assert.IsFalse(s.TowardB);
        }

        [Test]
        public void TwoNeutralEndsAreAThinGreyLine()
        {
            Assert.AreEqual(MinimapLinkKind.Neutral, MinimapLinkStyle.For(-1, -1).Kind);
            Assert.AreEqual(-1, MinimapLinkStyle.For(-1, -1).Team);
        }

        [Test]
        public void EndsOwnedByDifferentTeamsAreABorder()
        {
            // [C] controller amendment 2 (2026-09-17): a border between two different teams' zones is a way in for
            // both teams, so it draws as Border (each half in its own end's colour), not the spec's thin grey line.
            MinimapLinkStyle s = MinimapLinkStyle.For(0, 1);
            Assert.AreEqual(MinimapLinkKind.Border, s.Kind);
            Assert.AreEqual(0, s.Team);
            Assert.AreEqual(1, s.TeamB);
        }

        [Test]
        public void BorderKeepsEachEndsOwnTeamInOrder()
        {
            MinimapLinkStyle s = MinimapLinkStyle.For(1, 0);
            Assert.AreEqual(MinimapLinkKind.Border, s.Kind);
            Assert.AreEqual(1, s.Team);
            Assert.AreEqual(0, s.TeamB);
        }
    }
}
