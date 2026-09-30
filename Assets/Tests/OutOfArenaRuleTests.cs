using NUnit.Framework;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>
    /// The safety net's decision (movement step 4). The number it is given is the signed distance to the arena
    /// outline: positive inside, negative outside. A player's radius is 0.7.
    /// </summary>
    public class OutOfArenaRuleTests
    {
        private const float Radius = 0.7f;

        [Test]
        public void StandingWellInsideRemembersTheSpot() =>
            Assert.AreEqual(OutOfArenaAction.RememberAsSafe, OutOfArenaRule.Decide(3f, Radius, true, false));

        [Test]
        public void ExactlyAPlayersWidthInsideStillCounts() =>
            Assert.AreEqual(OutOfArenaAction.RememberAsSafe, OutOfArenaRule.Decide(Radius, Radius, true, true));

        [Test]
        public void InTheAirRemembersNothing() =>
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(3f, Radius, false, true));

        [Test]
        public void HuggingOrPressedIntoAWallIsNeitherRememberedNorReturned()
        {
            // 0.70 in = touching the wall, 0.60 = walked about 0.1 m into it. Both are ordinary play.
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(0.69f, Radius, true, true));
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(0.6f, Radius, true, true));
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(0.01f, Radius, true, true));
        }

        [Test]
        public void PastTheWallsInnerFaceGoesBack() =>
            Assert.AreEqual(OutOfArenaAction.ReturnToLastSafe, OutOfArenaRule.Decide(-0.01f, Radius, true, true));

        [Test]
        public void OutsideWithNowhereRememberedDoesNothing() =>
            Assert.AreEqual(OutOfArenaAction.None, OutOfArenaRule.Decide(-5f, Radius, false, false));

        [Test]
        public void OutsideInTheAirStillGoesBack() =>
            Assert.AreEqual(OutOfArenaAction.ReturnToLastSafe, OutOfArenaRule.Decide(-5f, Radius, false, true));
    }
}
