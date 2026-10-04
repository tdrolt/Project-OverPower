using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Dominion;
using Overpower.Net;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 7b (Tudor's A24-A26): the respawn shield also stops enemy stuns, slows and pushes, any hit on an enemy ends it, and effects set
    /// up before the respawn do not; plus the Task 7 review's wiring tests. The rules are pure; the wiring tests drive the real
    /// PlayerStatusEffects / PlayerDisplacement over a stand-in shield, so taking the shield's question out of Apply or Displace fails them.
    /// </summary>
    public class RespawnShieldStopsEverythingTests
    {
        private const RespawnShieldRules.Origin Enemy = RespawnShieldRules.Origin.Enemy;
        private const RespawnShieldRules.Origin Teammate = RespawnShieldRules.Origin.Teammate;
        private const RespawnShieldRules.Origin Self = RespawnShieldRules.Origin.Self;

        // ------------------------------------------------------------ A24: the victim side (pure rule)

        [Test] public void AnEnemysStunSlowOrPushOnAShieldedPlayerIsStoppedAndAsksForAStamp()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingEffect(true, Enemy, lastStampMs: 0, nowMs: 5000, popupMs: 800);
            Assert.IsTrue(d.Blocked);
            Assert.IsTrue(d.WriteStamp);
        }

        [Test] public void OwnAndTeammateEffectsAreNeverStoppedByTheShield()
        {
            Assert.IsFalse(RespawnShieldRules.OnIncomingEffect(true, Self, 0, 5000, 800).Blocked, "a shielded player's own dash, sprint or pulse back-push");
            Assert.IsFalse(RespawnShieldRules.OnIncomingEffect(true, Teammate, 0, 5000, 800).Blocked, "a teammate's help");
            Assert.IsFalse(RespawnShieldRules.OnIncomingEffect(true, Self, 0, 5000, 800).WriteStamp);
        }

        [Test] public void WithNoShieldAnEnemysEffectLandsAsToday()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingEffect(false, Enemy, 0, 5000, 800);
            Assert.IsFalse(d.Blocked);
            Assert.IsFalse(d.WriteStamp);
        }

        [Test] public void AnEffectIsStoppedEvenWhenNoStampIsDueYet()
        {
            RespawnShieldRules.HitDecision d = RespawnShieldRules.OnIncomingEffect(true, Enemy, lastStampMs: 5000, nowMs: 5100, popupMs: 800);
            Assert.IsTrue(d.Blocked);
            Assert.IsFalse(d.WriteStamp);
        }

        // ------------------------------------------------------------ A25 / A26: the attacker side (pure rule)

        [Test] public void AShieldedAttackerHittingAnUnshieldedEnemyLosesItsShield() =>
            Assert.IsTrue(RespawnShieldRules.EndsOnEnemyAffected(attackerShieldUp: true, victimShieldUp: false, fromBeforeRespawn: false));

        [Test] public void AnEffectOnAnEnemyWhoseOwnShieldStoppedItDoesNotEndTheAttackersShield() =>
            Assert.IsFalse(RespawnShieldRules.EndsOnEnemyAffected(true, victimShieldUp: true, false));

        [Test] public void AnEffectSetUpBeforeTheRespawnDoesNotEndTheNewShield() =>
            Assert.IsFalse(RespawnShieldRules.EndsOnEnemyAffected(true, false, fromBeforeRespawn: true));

        [Test] public void WithNoShieldThereIsNothingToEnd() =>
            Assert.IsFalse(RespawnShieldRules.EndsOnEnemyAffected(attackerShieldUp: false, victimShieldUp: false, fromBeforeRespawn: false));

        [Test] public void AnEffectIsFromBeforeTheRespawnWhenItWasSetUpBeforeTheShieldBegan()
        {
            // shield ends at 20000 and lasts 10 s, so it began at 10000
            Assert.IsTrue(RespawnShieldRules.IsFromBeforeRespawn(9999, 20000, 10f), "a mine laid before the respawn");
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(10000, 20000, 10f), "laid the instant the shield began");
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(15000, 20000, 10f), "laid after the respawn");
        }

        [Test] public void ADirectHitOrAClearedShieldIsNeverFromBeforeTheRespawn()
        {
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(0, 20000, 10f), "0 = a direct hit with nothing set up earlier");
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(9999, 0, 10f), "no shield, so no respawn to be before");
        }

        [Test] public void TheBeforeRespawnComparisonSurvivesTheIntWrap()
        {
            int end = unchecked(int.MaxValue - 2000 + 10000);   // wrapped to negative
            int started = unchecked(end - 10000);               // just before the wrap
            Assert.IsTrue(RespawnShieldRules.IsFromBeforeRespawn(unchecked(started - 1), end, 10f));
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(unchecked(started + 1), end, 10f));
        }

        // ------------------------------------------------------------ the rejoin respawn (Task 7 review, default A28)

        // ------------------------------------------------------------ the capture roster hold-out (Task 7 review)

        private static readonly System.Func<int, bool> NobodyDead = p => false;

        // ------------------------------------------------------------ the judge: one stamp spacing for hits, statuses and pushes

        private sealed class JudgeRig
        {
            public bool up = true;
            public int now = 5000;
            public readonly List<int> stamps = new List<int>();
            public readonly ShieldJudge judge;
            public JudgeRig() { judge = new ShieldJudge(() => up, () => now, () => 800, stamps.Add); }
        }

        [Test] public void AStoppedEnemyEffectStampsBlockedOnceAndAHitInsideThePopupDoesNotStampAgain()
        {
            var rig = new JudgeRig();
            Assert.IsTrue(rig.judge.JudgeEffect(Enemy));
            rig.now = 5300;
            Assert.IsTrue(rig.judge.JudgeHit(Enemy), "stopped");
            CollectionAssert.AreEqual(new[] { 5000 }, rig.stamps, "one stamp for the stun and the hit that followed it");
            rig.now = 5800;
            Assert.IsTrue(rig.judge.JudgeEffect(Enemy));
            CollectionAssert.AreEqual(new[] { 5000, 5800 }, rig.stamps);
        }

        [Test] public void ASelfHitIsStoppedByTheJudgeWithNoStampAndAnOwnEffectIsNotStopped()
        {
            var rig = new JudgeRig();
            Assert.IsTrue(rig.judge.JudgeHit(Self));
            Assert.IsFalse(rig.judge.JudgeEffect(Self));
            Assert.IsFalse(rig.judge.JudgeEffect(Teammate));
            Assert.IsFalse(rig.judge.JudgeHit(Teammate));
            Assert.IsEmpty(rig.stamps);
        }

        [Test] public void WithoutAShieldTheJudgeStopsNothing()
        {
            var rig = new JudgeRig { up = false };
            Assert.IsFalse(rig.judge.JudgeHit(Enemy));
            Assert.IsFalse(rig.judge.JudgeEffect(Enemy));
            Assert.IsEmpty(rig.stamps);
        }

        [Test] public void WithoutAServerClockTheEffectIsStillStoppedButNothingIsStamped()
        {
            var rig = new JudgeRig { now = 0 };
            Assert.IsTrue(rig.judge.JudgeEffect(Enemy));
            Assert.IsEmpty(rig.stamps);
        }

        // ------------------------------------------------------------ wiring: the components ask the shield

        /// <summary>A stand-in for the respawn shield on a test rig: says "stopped" or "not stopped" and remembers who it was asked about, so a
        /// wiring test can prove a component asks the shield before an enemy's status or push lands.</summary>
        private class TestEffectShield : MonoBehaviour, IEffectShield
        {
            public bool stops;
            public int askedWith = int.MinValue;
            public int asked;

            public bool StopsEnemyEffectFrom(int sourceActor)
            {
                asked++;
                askedWith = sourceActor;
                return stops;
            }
        }

        private GameObject go;

        [TearDown] public void TearDown() { if (go != null) Object.DestroyImmediate(go); }

        private static void MakeMine(GameObject target)
        {
            PhotonView view = target.AddComponent<PhotonView>();
            typeof(PhotonView).GetProperty("IsMine").GetSetMethod(true).Invoke(view, new object[] { true });
        }

        private static void CallAwake(Component c) =>
            c.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(c, null);

        [Test] public void AShieldedPlayersStatusEffectsRefuseAnEnemysStunAndAskTheShieldWhoSentIt()
        {
            go = new GameObject("Victim");
            MakeMine(go);
            var shield = go.AddComponent<TestEffectShield>();
            shield.stops = true;
            var status = go.AddComponent<PlayerStatusEffects>();
            CallAwake(status);

            status.Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = 2.5f, abilityId = -1 }, 7);

            Assert.IsFalse(status.IsStunned, "an enemy's stun does not land while the shield is up");
            Assert.AreEqual(7, shield.askedWith, "the shield is told who sent it");
        }

        [Test] public void AShieldedPlayersStatusEffectsRefuseAnEnemysSlowToo()
        {
            go = new GameObject("Victim");
            MakeMine(go);
            go.AddComponent<TestEffectShield>().stops = true;
            var status = go.AddComponent<PlayerStatusEffects>();
            CallAwake(status);

            status.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.4f, abilityId = -1 }, 7);

            Assert.AreEqual(0f, status.Remaining(StatusKind.Slow), 1e-5f);
        }

        [Test] public void WhenTheShieldDoesNotStopItAStunLandsAsToday()
        {
            go = new GameObject("Victim");
            MakeMine(go);
            var shield = go.AddComponent<TestEffectShield>();
            shield.stops = false;
            var status = go.AddComponent<PlayerStatusEffects>();
            CallAwake(status);

            status.Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = 2.5f, abilityId = -1 }, 7);

            Assert.IsTrue(status.IsStunned);
            Assert.AreEqual(1, shield.asked);
        }

        [Test] public void APlayerWithNoRespawnShieldComponentTakesStatusesAsToday()
        {
            go = new GameObject("Victim");
            MakeMine(go);
            var status = go.AddComponent<PlayerStatusEffects>();
            CallAwake(status);

            status.Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = 2.5f, abilityId = -1 }, 7);

            Assert.IsTrue(status.IsStunned, "Conquest and the test range have no shield on the player");
        }

        private static bool Moving(PlayerDisplacement d) =>
            typeof(PlayerDisplacement).GetField("activeKind", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(d) != null;

        [Test] public void AShieldedPlayerIsNotPushedByAnEnemysSonicPulse()
        {
            go = new GameObject("Victim");
            MakeMine(go);
            var shield = go.AddComponent<TestEffectShield>();
            shield.stops = true;
            var displacement = go.AddComponent<PlayerDisplacement>();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // Awake logs the missing capsule / rigidbody / motor of this bare rig
            CallAwake(displacement);

            bool ended = false;
            displacement.Displace(Vector3.forward, 5f, 20f, e => ended = true, 7);

            Assert.IsFalse(Moving(displacement), "no push started");
            Assert.IsFalse(ended, "nothing started, so nothing ends");
            Assert.AreEqual(7, shield.askedWith);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test] public void AnUnshieldedPlayerIsPushedAsToday()
        {
            go = new GameObject("Victim");
            MakeMine(go);
            var shield = go.AddComponent<TestEffectShield>();
            shield.stops = false;
            var displacement = go.AddComponent<PlayerDisplacement>();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            CallAwake(displacement);

            displacement.Displace(Vector3.forward, 5f, 20f, e => { }, 7);

            Assert.IsTrue(Moving(displacement));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------ Task 7 review: the properties are cleared on leave

        // ------------------------------------------------------------ the credit message carries "this ends the shield"

        [Test] public void AHitFromBeforeTheRespawnDoesNotMarkTheCreditAsEndingTheShield()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, 0f, cashedMark: false, endsShield: false);
            Assert.IsFalse(ledger.Drain().Single().endsShield);
        }

        [Test] public void OneHitAfterTheRespawnInTheSameReportMakesTheCreditEndTheShield()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, 0f, endsShield: false);
            ledger.Record(2, 5f, 0.1f, endsShield: true);
            Assert.IsTrue(ledger.Drain().Single().endsShield);
        }

        [Test] public void AnOrdinaryHitEndsTheShieldByDefaultAndTheFlagResetsAfterADrain()
        {
            var ledger = new DamageCreditLedger();
            ledger.Record(2, 10f, 0f);
            Assert.IsTrue(ledger.Drain().Single().endsShield, "a direct hit has nothing set up earlier");
            ledger.Record(2, 10f, 1f, endsShield: false);
            Assert.IsFalse(ledger.Drain().Single().endsShield, "the earlier true did not stick");
        }

        [Test] public void ADamageInfoCarriesWhenItsEffectWasSetUpAndKeepsItWhenScaled()
        {
            var info = new DamageInfo(10f, 1, 0, -1, DamageSource.Splash, false, Vector3.zero, effectPlacedMs: 12345);
            Assert.AreEqual(12345, info.EffectPlacedMs);
            Assert.AreEqual(12345, info.WithAmount(15f).EffectPlacedMs);
            Assert.AreEqual(0, new DamageInfo(10f, 1, 0, -1, DamageSource.Projectile, false, Vector3.zero).EffectPlacedMs, "a direct hit");
        }
    }
}
