using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class DamageCreditLedgerTests
    {
        [Test]
        public void RecordSumsMultipleHitsFromTheSameActor()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, 0f);
            ledger.Record(2, 15f, 1f);

            var drained = ledger.Drain();

            Assert.AreEqual(1, drained.Count);
            Assert.AreEqual(2, drained[0].actor);
            Assert.AreEqual(25f, drained[0].amount, 0.001f);
        }

        [Test]
        public void RecordKeepsSeparateActorsIndependent()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, 0f);
            ledger.Record(3, 40f, 0f);

            var drained = ledger.Drain().ToDictionary(e => e.actor, e => e.amount);

            Assert.AreEqual(10f, drained[2], 0.001f);
            Assert.AreEqual(40f, drained[3], 0.001f);
        }

        [Test]
        public void DrainResetsSumsSoASecondDrainReturnsNothing()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, 0f);

            ledger.Drain();
            var second = ledger.Drain();

            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void ZeroOrNegativeActorNumberIsIgnored()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(0, 50f, 0f);
            ledger.Record(-1, 50f, 0f);

            Assert.AreEqual(0, ledger.Drain().Count);
        }

        [Test]
        public void ZeroOrNegativeAmountIsIgnored()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 0f, 0f);
            ledger.Record(2, -5f, 0f);

            Assert.AreEqual(0, ledger.Drain().Count);
        }

        // Self-damage has no actor-vs-victim notion inside the ledger itself (see its class
        // comment) - PlayerCombatCredit is the one that compares the source actor to its own
        // owner's actor number before ever calling Record, so from the ledger's point of view a
        // "self hit" simply never arrives as a Record call. Nothing to assert here beyond that
        // documented boundary.

        [Test]
        public void AssistersSinceReturnsAnActorWhoseLastHitIsWithinTheWindow()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, now: 0f);

            var assisters = ledger.AssistersSince(now: 5f, window: 8f, excludeActor: -1).ToList();

            CollectionAssert.Contains(assisters, 2);
        }

        [Test]
        public void AssistersSinceExcludesAnActorWhoseLastHitIsOutsideTheWindow()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, now: 0f);

            var assisters = ledger.AssistersSince(now: 9f, window: 8f, excludeActor: -1).ToList();

            CollectionAssert.DoesNotContain(assisters, 2);
        }

        [Test]
        public void AssistersSinceStillSeesAnActorAfterTheirDamageWasAlreadyDrained()
        {
            // The point of keeping last-hit time separate from sum: an attacker paid out by an
            // earlier periodic flush must still count as an assister if the kill lands soon after.
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, now: 0f);
            ledger.Drain();

            var assisters = ledger.AssistersSince(now: 1f, window: 8f, excludeActor: -1).ToList();

            CollectionAssert.Contains(assisters, 2);
        }

        [Test]
        public void AssistersSinceExcludesTheKiller()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, now: 0f);
            ledger.Record(3, 5f, now: 0f);

            var assisters = ledger.AssistersSince(now: 1f, window: 8f, excludeActor: 2).ToList();

            CollectionAssert.DoesNotContain(assisters, 2);
            CollectionAssert.Contains(assisters, 3);
        }

        [Test]
        public void ClearForgetsBothSumsAndLastHitTimes()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, now: 0f);

            ledger.Clear();

            Assert.AreEqual(0, ledger.Drain().Count);
            Assert.IsFalse(ledger.AssistersSince(now: 0.5f, window: 8f, excludeActor: -1).Any());
        }

        // Mark plan step 2: PlayerCombatCredit's leading-edge LateUpdate flush (Decision 11) needs to
        // know "is there anything to send" WITHOUT draining - HasPending is that read-only question.
        [Test]
        public void HasPendingOnlyWhileUnsentDamageWaits()
        {
            var ledger = new DamageCreditLedger();
            Assert.IsFalse(ledger.HasPending);

            ledger.Record(2, 10f, now: 0f);
            Assert.IsTrue(ledger.HasPending);

            ledger.Drain();
            Assert.IsFalse(ledger.HasPending);

            ledger.Record(2, 0f, now: 1f);
            Assert.IsFalse(ledger.HasPending);
            ledger.Record(0, 5f, now: 1f);
            Assert.IsFalse(ledger.HasPending);

            ledger.Record(3, 7f, now: 2f);
            Assert.IsTrue(ledger.HasPending);
            ledger.Clear();
            Assert.IsFalse(ledger.HasPending);
        }
    }
}
