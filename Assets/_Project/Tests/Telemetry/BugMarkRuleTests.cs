using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Playtest extras P2 (2026-09-26): Ctrl+B's own admission rule.</summary>
    public class BugMarkRuleTests
    {
        private const double Cooldown = 2.0;

        [Test]
        public void TheFirstMarkThisMatchIsAlwaysAccepted()
        {
            Assert.IsTrue(BugMarkRule.CanMark(isTypingInChat: false, now: 5.0, lastMarkTime: -1, Cooldown));
        }

        [Test]
        public void AMarkIsRefusedWithinTwoSecondsOfTheLast()
        {
            Assert.IsFalse(BugMarkRule.CanMark(false, now: 6.5, lastMarkTime: 5.0, Cooldown));
        }

        [Test]
        public void AMarkExactlyAtTheCooldownEdgeIsAccepted()
        {
            Assert.IsTrue(BugMarkRule.CanMark(false, now: 7.0, lastMarkTime: 5.0, Cooldown));
        }

        [Test]
        public void AMarkIsRefusedWhileTyping()
        {
            Assert.IsFalse(BugMarkRule.CanMark(isTypingInChat: true, now: 100.0, lastMarkTime: -1, Cooldown));
        }

        [Test]
        public void TypingRefusesEvenPastTheCooldown()
        {
            Assert.IsFalse(BugMarkRule.CanMark(isTypingInChat: true, now: 20.0, lastMarkTime: 5.0, Cooldown));
        }
    }
}
