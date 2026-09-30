using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Tudor, 2026-09-18: a hit marks the enemy; your next hit on them within 2 s deals +50% and uses the mark up;
    /// the hit after that marks again, endlessly. One mark per attacker per target, cashable only by whoever placed it.
    /// The ledger lives on the TARGET's own client (damage is victim-side), so "now" is always the target's clock.</summary>
    public class MarkLedgerTests
    {
        private const float Window = 2f;
        private const int Me = 2;
        private const int Other = 3;

        [Test]
        public void TheFirstHitMarks()
        {
            Assert.AreEqual(MarkOutcome.Applied, new MarkLedger().OnLandedHit(Me, 10f, Window));
        }

        [Test]
        public void TheNextHitInsideTheWindowCashesTheMark()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 10.7f, Window));
        }

        [Test]
        public void AHitExactlyAtTheWindowEdgeStillCashes()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 12f, Window));
        }

        [Test]
        public void AHitAfterTheWindowMarksAfreshInsteadOfCashing()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Me, 12.01f, Window));
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 13.5f, Window), "the fresh mark counts from 12.01");
        }

        [Test]
        public void TheHitAfterACashMarksAgainSoItAlternatesEndlessly()
        {
            var marks = new MarkLedger();
            var seen = new MarkOutcome[6];
            for (int i = 0; i < seen.Length; i++)
                seen[i] = marks.OnLandedHit(Me, 10f + 0.7f * i, Window);
            CollectionAssert.AreEqual(new[] { MarkOutcome.Applied, MarkOutcome.Cashed, MarkOutcome.Applied,
                                              MarkOutcome.Cashed, MarkOutcome.Applied, MarkOutcome.Cashed }, seen);
        }

        [Test]
        public void OnlyTheAttackerWhoPlacedTheMarkCanCashIt()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Other, 10.3f, Window), "someone else's hit places THEIR mark");
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 10.7f, Window), "and leaves mine alone");
        }

        [Test]
        public void EachAttackerKeepsTheirOwnMarkOnTheSameTarget()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            marks.OnLandedHit(Other, 10.2f, Window);
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 10.7f, Window));
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Other, 10.9f, Window));
        }

        [Test]
        public void AHitFromAWeaponThatDoesNotMarkNeitherMarksNorCashes()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.None, marks.OnLandedHit(Me, 10.5f, 0f), "X-ray, a rocket, a burn tick");
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 11f, Window), "the mark survived it");
        }

        [Test]
        public void AnUnknownAttackerNeverMarks()
        {
            var marks = new MarkLedger();
            Assert.AreEqual(MarkOutcome.None, marks.OnLandedHit(0, 10f, Window));
            Assert.AreEqual(MarkOutcome.None, marks.OnLandedHit(-1, 10f, Window));
            Assert.AreEqual(0f, marks.SecondsLeft(0, 10f));
        }

        [Test]
        public void ClearForgetsEveryMark()
        {
            // Death and respawn.
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            marks.OnLandedHit(Other, 10f, Window);
            marks.Clear();
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Me, 10.5f, Window));
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Other, 10.5f, Window));
        }

        [Test]
        public void SecondsLeftCountsDownAndIsZeroOnceCashedOrExpired()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(2f, marks.SecondsLeft(Me, 10f), 1e-4f);
            Assert.AreEqual(0.5f, marks.SecondsLeft(Me, 11.5f), 1e-4f);
            Assert.AreEqual(0f, marks.SecondsLeft(Me, 12.5f));
            Assert.AreEqual(0f, marks.SecondsLeft(Other, 10f));
            marks.OnLandedHit(Me, 13f, Window);
            marks.OnLandedHit(Me, 13.5f, Window);
            Assert.AreEqual(0f, marks.SecondsLeft(Me, 13.5f), "cashed");
        }

        [Test]
        public void OnlyACashedHitIsScaledAndNeverMoreThanOnce()
        {
            Assert.AreEqual(21f, MarkLedger.ScaledAmount(21f, MarkOutcome.None, 1.5f));
            Assert.AreEqual(21f, MarkLedger.ScaledAmount(21f, MarkOutcome.Applied, 1.5f));
            Assert.AreEqual(31.5f, MarkLedger.ScaledAmount(21f, MarkOutcome.Cashed, 1.5f), 1e-4f);
            Assert.AreEqual(0f, MarkLedger.ScaledAmount(21f, MarkOutcome.Cashed, -2f), "a negative multiplier never heals");
        }

        // ---- LongestSecondsLeft (Tudor's answer 1: the victim also sees the mark - step 5 reads this
        // through PlayerHealth.LongestMarkSecondsLeft, but the pure rule and its tests belong here). ----

        [Test]
        public void LongestSecondsLeftIsZeroWithNoMarksAtAll()
        {
            Assert.AreEqual(0f, new MarkLedger().LongestSecondsLeft(10f));
        }

        [Test]
        public void LongestSecondsLeftIsTheLaterOfTwoAttackersMarks()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);    // expires 12
            marks.OnLandedHit(Other, 10.5f, Window); // expires 12.5 - the later one
            Assert.AreEqual(1.5f, marks.LongestSecondsLeft(11f), 1e-4f);
        }

        [Test]
        public void LongestSecondsLeftIgnoresAnExpiredOrCashedMark()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window); // expires 12, never cashed
            Assert.AreEqual(0f, marks.LongestSecondsLeft(13f), "expired, though the entry is still in the ledger");

            var cashedThenNothing = new MarkLedger();
            cashedThenNothing.OnLandedHit(Me, 10f, Window);
            cashedThenNothing.OnLandedHit(Me, 10.5f, Window); // cashes it, removing the entry
            Assert.AreEqual(0f, cashedThenNothing.LongestSecondsLeft(10.5f), "cashed - nothing left to show");
        }
    }
}
