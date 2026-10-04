using NUnit.Framework;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: what the respawn shield blocks and when it ends.</summary>
    public class RespawnShieldRulesTests
    {
        [Test] public void TheShieldIsUpUntilItsEndTimeAndNotAfter()
        {
            Assert.IsTrue(RespawnShieldRules.IsUp(10000, 9999));
            Assert.IsFalse(RespawnShieldRules.IsUp(10000, 10000));
            Assert.IsFalse(RespawnShieldRules.IsUp(10000, 12000));
        }

        [Test] public void ZeroMeansNoShieldWasEverUp() => Assert.IsFalse(RespawnShieldRules.IsUp(0, 5));

        [Test] public void DealingDamageClearsTheShieldToZeroWhichIsDown()
        {
            int cleared = RespawnShieldRules.EndAfterDamageDealt();
            Assert.AreEqual(0, cleared);
            Assert.IsFalse(RespawnShieldRules.IsUp(cleared, 1));
        }

        [Test] public void AnUpShieldBlocksDamageAndADownOneDoesNot()
        {
            Assert.IsTrue(RespawnShieldRules.BlocksDamage(true));
            Assert.IsFalse(RespawnShieldRules.BlocksDamage(false));
        }

        [Test] public void AShieldedPlayerCountsForNoCaptureAndAnUnshieldedOneDoes()
        {
            Assert.IsFalse(RespawnShieldRules.CountsForCapture(true));
            Assert.IsTrue(RespawnShieldRules.CountsForCapture(false));
        }

        [Test] public void CastingAnAbilityWithoutHittingKeepsTheShield() => Assert.IsFalse(RespawnShieldRules.EndsOnAbilityWithoutHit);

        [Test] public void TheShieldCompareWorksAcrossTheIntWrap()
        {
            int end = unchecked(int.MaxValue - 1000 + 5000); // wrapped to negative
            Assert.IsTrue(RespawnShieldRules.IsUp(end, int.MaxValue - 500));
            Assert.IsFalse(RespawnShieldRules.IsUp(end, unchecked(end + 1)));
        }

        // ---- Task 7: the component's decisions

        [Test] public void AShieldStartedNowEndsTheGivenSecondsLaterOnTheServerClock()
        {
            Assert.AreEqual(15000, RespawnShieldRules.EndMs(5000, 10f));
            Assert.AreEqual(5800, RespawnShieldRules.EndMs(5000, 0.8f));
        }

        [Test] public void AShieldEndTimeIsNeverZeroBecauseZeroMeansNoShield()
        {
            Assert.AreNotEqual(0, RespawnShieldRules.EndMs(-10000, 10f), "now + 10 s wraps to exactly 0 here");
            Assert.IsTrue(RespawnShieldRules.IsUp(RespawnShieldRules.EndMs(-10000, 10f), -10000));
        }

        [Test] public void TheEndTimeSurvivesTheIntWrap()
        {
            int end = RespawnShieldRules.EndMs(int.MaxValue - 2000, 10f);
            Assert.Less(end, 0);
            Assert.IsTrue(RespawnShieldRules.IsUp(end, int.MaxValue));
        }

        [Test] public void OnlyAPlayerComingBackFromADeathInALiveDominionMatchGetsAShield()
        {
            Assert.IsTrue(RespawnShieldRules.StartsAfterRespawn(dominion: true, matchLive: true, diedBefore: true, freshStart: false));
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(dominion: false, matchLive: true, diedBefore: true, freshStart: false), "Conquest: never");
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(dominion: true, matchLive: false, diedBefore: true, freshStart: false), "the warm-up");
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(dominion: true, matchLive: true, diedBefore: false, freshStart: false), "a first spawn or a joiner");
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(dominion: true, matchLive: true, diedBefore: true, freshStart: true), "a round's or break's fresh start (A16)");
        }

        [Test] public void OnlyDamageActuallyDealtEndsTheShield()
        {
            Assert.IsTrue(RespawnShieldRules.ClearsOnDamageDealt(0.5f));
            Assert.IsTrue(RespawnShieldRules.ClearsOnDamageDealt(120f));
            Assert.IsFalse(RespawnShieldRules.ClearsOnDamageDealt(0f), "an ability that hit nobody reports nothing");
            Assert.IsFalse(RespawnShieldRules.ClearsOnDamageDealt(-3f));
        }

        [Test] public void AHitOnAShieldedVictimIsStoppedAndAsksForAStamp()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingHit(shieldUp: true, fromTeammate: false, lastStampMs: 0, nowMs: 5000, popupMs: 800);
            Assert.IsTrue(d.Blocked);
            Assert.IsTrue(d.WriteStamp);
        }

        [Test] public void AStampIsWrittenAtMostOncePerPopupButEveryHitIsStillStopped()
        {
            RespawnShieldRules.HitDecision soon = RespawnShieldRules.OnIncomingHit(true, false, lastStampMs: 5000, nowMs: 5799, popupMs: 800);
            Assert.IsTrue(soon.Blocked, "stopped even though no stamp is due");
            Assert.IsFalse(soon.WriteStamp);
            RespawnShieldRules.HitDecision later = RespawnShieldRules.OnIncomingHit(true, false, lastStampMs: 5000, nowMs: 5800, popupMs: 800);
            Assert.IsTrue(later.WriteStamp);
        }

        [Test] public void WithoutAShieldAHitIsNoneOfTheShieldsBusiness()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingHit(shieldUp: false, fromTeammate: false, lastStampMs: 0, nowMs: 5000, popupMs: 800);
            Assert.IsFalse(d.Blocked);
            Assert.IsFalse(d.WriteStamp);
        }

        [Test] public void ATeammatesHitIsLeftToTheOrdinaryFriendlyFireRule()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingHit(shieldUp: true, fromTeammate: true, lastStampMs: 0, nowMs: 5000, popupMs: 800);
            Assert.IsFalse(d.Blocked, "ignored as today, with no BLOCKED");
            Assert.IsFalse(d.WriteStamp);
        }

        [Test] public void TheStampSpacingSurvivesTheIntWrap()
        {
            int last = int.MaxValue - 100;
            Assert.IsFalse(RespawnShieldRules.StampDue(last, unchecked(last + 500), 800));
            Assert.IsTrue(RespawnShieldRules.StampDue(last, unchecked(last + 800), 800));
        }

        [Test] public void AStampIsNewOnlyWhenItIsNotZeroAndNotTheOneAlreadyShown()
        {
            Assert.IsTrue(RespawnShieldRules.IsNewStamp(0, 6000));
            Assert.IsTrue(RespawnShieldRules.IsNewStamp(6000, 6900));
            Assert.IsFalse(RespawnShieldRules.IsNewStamp(6000, 6000));
            Assert.IsFalse(RespawnShieldRules.IsNewStamp(6000, 0), "cleared: nothing to pop");
        }

        [Test] public void AZoneCountsAPlayerOnlyOnceTheirShieldHasRunOut()
        {
            int end = 20000;
            Assert.IsFalse(RespawnShieldRules.CountsForCapture(RespawnShieldRules.IsUp(end, 19999)));
            Assert.IsTrue(RespawnShieldRules.CountsForCapture(RespawnShieldRules.IsUp(end, 20000)));
            Assert.IsTrue(RespawnShieldRules.CountsForCapture(RespawnShieldRules.IsUp(0, 20000)), "a cleared shield counts like anyone");
        }
    }
}
