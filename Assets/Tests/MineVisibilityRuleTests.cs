using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>A5 (Tudor 2026-09-17 evening: mines turn invisible to the enemy 1 s after placement, as the GDD
    /// always said). See MineVisibilityRule.</summary>
    public class MineVisibilityRuleTests
    {
        [Test]
        public void EveryoneSeesItBeforeInvisibleAfterSecondsElapses()
        {
            Assert.AreEqual(MineVisibility.Visible,
                MineVisibilityRule.For(localTeam: 0, ownerTeam: 0, secondsSincePlaced: 0.9f, invisibleAfterSeconds: 1f), "owner's own team");
            Assert.AreEqual(MineVisibility.Visible,
                MineVisibilityRule.For(localTeam: 1, ownerTeam: 0, secondsSincePlaced: 0.9f, invisibleAfterSeconds: 1f), "an enemy");
        }

        [Test]
        public void EnemiesSeeNothingOnceInvisibleAfterSecondsElapses()
        {
            Assert.AreEqual(MineVisibility.Hidden,
                MineVisibilityRule.For(localTeam: 1, ownerTeam: 0, secondsSincePlaced: 1f, invisibleAfterSeconds: 1f));
        }

        [Test]
        public void TheOwnersOwnTeamSeesAFadedGhostOnceInvisibleAfterSecondsElapses()
        {
            Assert.AreEqual(MineVisibility.Ghost,
                MineVisibilityRule.For(localTeam: 0, ownerTeam: 0, secondsSincePlaced: 1f, invisibleAfterSeconds: 1f));
        }

        [Test]
        public void NoTeamOrASpectatorCountsAsAnEnemy()
        {
            // localTeam < 0 - no team assigned yet, or a spectator - is never "the owner's own team".
            Assert.AreEqual(MineVisibility.Hidden,
                MineVisibilityRule.For(localTeam: -1, ownerTeam: 0, secondsSincePlaced: 1f, invisibleAfterSeconds: 1f));
        }

        [Test]
        public void HiddenTheInstantInvisibleAfterSecondsElapses()
        {
            // Boundary is >=, the same convention MineDetonationState.IsArmed already uses.
            Assert.AreEqual(MineVisibility.Visible,
                MineVisibilityRule.For(localTeam: 1, ownerTeam: 0, secondsSincePlaced: 0.999f, invisibleAfterSeconds: 1f));
            Assert.AreEqual(MineVisibility.Hidden,
                MineVisibilityRule.For(localTeam: 1, ownerTeam: 0, secondsSincePlaced: 1.0f, invisibleAfterSeconds: 1f));
        }
    }
}
