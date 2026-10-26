using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Net;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 7b, Part 0: the fixes from the Task 7 review. A self-hit on a shielded player is stopped without a BLOCKED, a rejoiner's
    /// first respawn gets the shield, the capture roster's hold-out is a rule over two lists, and the shield's two properties are cleared on leave.</summary>
    public class RespawnShieldReviewFixTests
    {
        private const RespawnShieldRules.Origin Enemy = RespawnShieldRules.Origin.Enemy;
        private const RespawnShieldRules.Origin Teammate = RespawnShieldRules.Origin.Teammate;
        private const RespawnShieldRules.Origin Self = RespawnShieldRules.Origin.Self;
        private static readonly System.Func<int, bool> NobodyDead = p => false;

        [Test] public void ASelfHitOnAShieldedPlayerIsStoppedSilentlyWithNoBlockedPopup()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingHit(true, Self, lastStampMs: 0, nowMs: 5000, popupMs: 800);
            Assert.IsTrue(d.Blocked, "a shielded player still cannot hurt themselves");
            Assert.IsFalse(d.WriteStamp, "but nobody sees BLOCKED for it");
        }

        [Test] public void AnOriginIsSelfFirstThenTeammateThenEnemy()
        {
            Assert.AreEqual(Self, RespawnShieldRules.OriginOf(true, true));
            Assert.AreEqual(Teammate, RespawnShieldRules.OriginOf(false, true));
            Assert.AreEqual(Enemy, RespawnShieldRules.OriginOf(false, false), "an unknown team counts as an enemy, as for damage");
        }

        [Test] public void APlayerWhoLeftWhileDeadGetsTheShieldOnTheirRejoinRespawn()
        {
            Assert.IsTrue(RespawnShieldRules.StartsAfterRespawn(dominion: true, matchLive: true, diedBefore: false, freshStart: false, afterRejoin: true));
        }

        [Test] public void ARejoinRespawnStillGetsNoShieldInConquestTheWarmUpOrAFreshStart()
        {
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(false, true, false, false, afterRejoin: true), "Conquest");
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(true, false, false, false, afterRejoin: true), "warm-up");
            Assert.IsFalse(RespawnShieldRules.StartsAfterRespawn(true, true, false, true, afterRejoin: true), "a round's or break's fresh start");
        }

        [Test] public void AShieldedPlayerOnTheRosterIsHeldOutAndAnUnshieldedOneStays()
        {
            var counted = new List<int> { 1, 2, 3 };
            var held = new List<int>();
            RespawnShieldRules.SortRoster(counted, held, p => p == 2, NobodyDead);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, counted);
            CollectionAssert.AreEqual(new[] { 2 }, held);
        }

        [Test] public void AHeldOutPlayerWhoseShieldIsDownComesBack()
        {
            var counted = new List<int> { 1 };
            var held = new List<int> { 2 };
            RespawnShieldRules.SortRoster(counted, held, p => false, NobodyDead);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, counted);
            Assert.IsEmpty(held);
        }

        [Test] public void AHeldOutPlayerWhoDiedIsDroppedAndNotCounted()
        {
            var counted = new List<int>();
            var held = new List<int> { 2 };
            RespawnShieldRules.SortRoster(counted, held, p => false, p => p == 2);
            Assert.IsEmpty(counted, "they re-enter through the trigger when they respawn");
            Assert.IsEmpty(held);
        }

        [Test] public void AHeldOutPlayerWhoIsStillShieldedStaysHeld()
        {
            var counted = new List<int>();
            var held = new List<int> { 2 };
            RespawnShieldRules.SortRoster(counted, held, p => true, NobodyDead);
            Assert.IsEmpty(counted);
            CollectionAssert.AreEqual(new[] { 2 }, held);
        }

        [Test] public void LeavingAMatchClearsBothShieldProperties()
        {
            Dictionary<string, object> resets = MatchPropertyReset.Build();
            Assert.IsTrue(resets.ContainsKey(RespawnShieldRules.ShieldKey), "dShd");
            Assert.IsTrue(resets.ContainsKey(RespawnShieldRules.BlockedKey), "dBlk");
            Assert.IsNull(resets[RespawnShieldRules.ShieldKey], "null removes the key");
            Assert.IsNull(resets[RespawnShieldRules.BlockedKey]);
        }
    }
}
