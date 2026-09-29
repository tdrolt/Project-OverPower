using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class HealthPackRulesTests
    {
        // ---- may take ------------------------------------------------------------------------------

        [Test]
        public void AliveHurtAvailableInPlayMayTake()
        {
            Assert.IsTrue(HealthPackRules.MayTake(alive: true, health: 60f, maxHealth: 100f, minMissing: 10f, available: true, zoneInPlay: true));
        }

        [Test]
        public void DeadMayNotTake()
        {
            Assert.IsFalse(HealthPackRules.MayTake(false, 60f, 100f, 10f, true, true));
        }

        [Test]
        public void FullHealthMayNotTake()
        {
            Assert.IsFalse(HealthPackRules.MayTake(true, 100f, 100f, 10f, true, true));
        }

        [Test]
        public void TakenPackMayNotBeTaken()
        {
            Assert.IsFalse(HealthPackRules.MayTake(true, 60f, 100f, 10f, false, true));
        }

        [Test]
        public void PackInAZoneOutOfPlayMayNotBeTaken()
        {
            Assert.IsFalse(HealthPackRules.MayTake(true, 60f, 100f, 10f, true, false));
        }

        // ---- how much it heals ---------------------------------------------------------------------

        [Test]
        public void ThirtyMissingHealsThirty()
        {
            Assert.AreEqual(30f, HealthPackRules.HealAmount(50f, 70f, 100f), 0.0001f);
        }

        [Test]
        public void EightyMissingHealsTheFullFifty()
        {
            Assert.AreEqual(50f, HealthPackRules.HealAmount(50f, 20f, 100f), 0.0001f);
        }

        [Test]
        public void AlreadyFullHealsNothing()
        {
            Assert.AreEqual(0f, HealthPackRules.HealAmount(50f, 100f, 100f), 0.0001f);
        }

        // ---- coming back ---------------------------------------------------------------------------

        [Test]
        public void NoEntryMeansAvailable()
        {
            Assert.IsTrue(HealthPackRules.IsAvailable(null, 12345));
        }

        [Test]
        public void TakenUntilTIsGoneOneMsBeforeAndBackAtT()
        {
            int[] taken = { 50000, 2, 1 };
            Assert.IsFalse(HealthPackRules.IsAvailable(taken, 49999));
            Assert.IsTrue(HealthPackRules.IsAvailable(taken, 50000));
            Assert.IsTrue(HealthPackRules.IsAvailable(taken, 50001));
        }

        [Test]
        public void ComesBackCorrectlyAcrossTheServerClockWrap()
        {
            // The clock is a wrapping int in ms: taken just before the wrap, back just after it.
            int until = int.MaxValue - 5000;
            int[] taken = { until, 2, 1 };
            Assert.IsFalse(HealthPackRules.IsAvailable(taken, int.MaxValue - 5001), "1 ms early, before the wrap");
            Assert.IsTrue(HealthPackRules.IsAvailable(taken, int.MaxValue - 5000), "exactly at the time");
            Assert.IsTrue(HealthPackRules.IsAvailable(taken, int.MinValue + 100), "now wrapped negative, 5 s after: back");
        }

        [Test]
        public void UntilPastTheWrapReadsGoneUntilItsTimeThenBack()
        {
            // Taken at int.MaxValue - 1000 for 3000 ms: until wraps to a negative number.
            int until = unchecked(int.MaxValue - 1000 + 3000);
            Assert.Less(until, 0);
            int[] taken = { until, 2, 1 };
            Assert.IsFalse(HealthPackRules.IsAvailable(taken, int.MaxValue), "still inside the window, before the wrap");
            Assert.IsFalse(HealthPackRules.IsAvailable(taken, unchecked(until - 1)), "1 ms early, after the wrap");
            Assert.IsTrue(HealthPackRules.IsAvailable(taken, until));
        }

        // ---- the master's decision -----------------------------------------------------------------

        [Test]
        public void FirstRequestOnAnAvailablePackIsGrantedAndStampsTheReturnTime()
        {
            HealthPackRules.Decision d = HealthPackRules.Decide(null, 10000, true, true, true, 3, 7, 30000);
            Assert.IsTrue(d.Granted);
            CollectionAssert.AreEqual(new[] { 40000, 3, 7 }, d.NewValue);
        }

        [Test]
        public void TwoRequestsForTheSamePackInOrderOnlyTheFirstIsGranted()
        {
            HealthPackRules.Decision first = HealthPackRules.Decide(null, 10000, true, true, true, 3, 1, 30000);
            HealthPackRules.Decision second = HealthPackRules.Decide(first.NewValue, 10000, true, true, true, 4, 1, 30000);
            Assert.IsTrue(first.Granted);
            Assert.IsFalse(second.Granted);
            Assert.IsNull(second.NewValue);
        }

        [Test]
        public void RequestIsRefusedForDeadOutOfPlayOrOutOfRange()
        {
            Assert.IsFalse(HealthPackRules.Decide(null, 0, false, true, true, 3, 1, 30000).Granted, "dead");
            Assert.IsFalse(HealthPackRules.Decide(null, 0, true, false, true, 3, 1, 30000).Granted, "out of play");
            Assert.IsFalse(HealthPackRules.Decide(null, 0, true, true, false, 3, 1, 30000).Granted, "too far away");
        }

        [Test]
        public void PackThatHasComeBackCanBeTakenAgain()
        {
            int[] old = { 40000, 3, 7 };
            Assert.IsFalse(HealthPackRules.Decide(old, 39999, true, true, true, 4, 1, 30000).Granted);
            HealthPackRules.Decision again = HealthPackRules.Decide(old, 40000, true, true, true, 4, 1, 30000);
            Assert.IsTrue(again.Granted);
            CollectionAssert.AreEqual(new[] { 70000, 4, 1 }, again.NewValue);
        }

        // ---- reading the echo ----------------------------------------------------------------------

        [Test]
        public void EchoNamingMeAndMyLatestRequestHealsOnce()
        {
            int[] mine = { 40000, 3, 7 };
            Assert.IsTrue(HealthPackRules.EchoIsMyFreshTake(mine, 3, 7, 6, 10000, 30000));
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(mine, 3, 7, 7, 10000, 30000), "already healed for request 7");
        }

        [Test]
        public void EchoForSomeoneElseOrAnOldTakeDoesNotHeal()
        {
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(new[] { 40000, 4, 7 }, 3, 7, 0, 10000, 30000), "another taker");
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(new[] { 40000, 3, 7 }, 3, 7, 0, 41000, 30000), "the take is long over");
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(null, 3, 7, 0, 10000, 30000), "no entry");
        }

        [Test]
        public void GrantedOnAnEarlierIdAfterARetryHealsOnce()
        {
            // Request 7 was granted, my retry 8 was already sent, and the echo names 7.
            int[] granted = { 40000, 3, 7 };
            Assert.IsTrue(HealthPackRules.EchoIsMyFreshTake(granted, 3, 8, 6, 10000, 30000));
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(granted, 3, 8, 7, 10000, 30000), "already healed for 7");
        }

        [Test]
        public void AnIdIHaveNotSentYetNeverHeals()
        {
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(new[] { 40000, 3, 9 }, 3, 8, 0, 10000, 30000));
        }

        [Test]
        public void AStaleValueFarInTheFutureOrWrappedDoesNotHeal()
        {
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(new[] { 10000 + 30001, 3, 7 }, 3, 7, 0, 10000, 30000), "further ahead than a take can be");
            Assert.IsTrue(HealthPackRules.EchoIsMyFreshTake(new[] { unchecked(int.MaxValue - 1000 + 30000), 3, 7 }, 3, 7, 0, int.MaxValue - 1000, 30000), "a fresh take across the wrap heals");
            Assert.IsFalse(HealthPackRules.EchoIsMyFreshTake(new[] { unchecked(int.MaxValue - 1000 + 60000), 3, 7 }, 3, 7, 0, int.MaxValue - 1000, 30000), "wrapped but 60 s ahead");
        }

        // ---- a sliver of missing health -------------------------------------------------------------

        [Test]
        public void MissingLessThanTheMinimumMayNotTakeButAtTheMinimumMay()
        {
            Assert.IsFalse(HealthPackRules.MayTake(true, 99.9f, 100f, 10f, true, true), "a sliver");
            Assert.IsFalse(HealthPackRules.MayTake(true, 90.5f, 100f, 10f, true, true), "9.5 missing");
            Assert.IsTrue(HealthPackRules.MayTake(true, 90f, 100f, 10f, true, true), "exactly 10 missing");
        }

        [Test]
        public void MinimumZeroMeansAnyDamageButNotFull()
        {
            Assert.IsTrue(HealthPackRules.MayTake(true, 99.9f, 100f, 0f, true, true));
            Assert.IsFalse(HealthPackRules.MayTake(true, 100f, 100f, 0f, true, true));
        }

        // ---- packs reset when the match goes live ---------------------------------------------------

        [Test]
        public void ATakeFromBeforeGoLiveReadsAsAvailableButOneAfterDoesNot()
        {
            const int respawn = 30000;
            int[] warmupTake = { 50000 + respawn, 3, 1 };   // taken at 50000
            int[] liveTake = { 70000 + respawn, 3, 1 };     // taken at 70000
            Assert.IsTrue(HealthPackRules.IsAvailable(warmupTake, 60000, 60000, respawn), "taken before go-live: fresh start");
            Assert.IsFalse(HealthPackRules.IsAvailable(liveTake, 80000, 60000, respawn), "taken after go-live still counts");
            Assert.IsFalse(HealthPackRules.IsAvailable(warmupTake, 55000, 0, respawn), "no go-live yet: the take counts");
        }

        // ---- asking ---------------------------------------------------------------------------------

        [Test]
        public void OneOutstandingRequestPerPackRetriesOnlyAfterTheDelay()
        {
            Assert.IsTrue(HealthPackRules.MayRequestNow(false, 0f, 0.2f), "nothing outstanding");
            Assert.IsFalse(HealthPackRules.MayRequestNow(true, 0.1f, 0.2f), "still waiting for the echo");
            Assert.IsTrue(HealthPackRules.MayRequestNow(true, 0.2f, 0.2f), "no echo for long enough: try again");
        }
    }
}
