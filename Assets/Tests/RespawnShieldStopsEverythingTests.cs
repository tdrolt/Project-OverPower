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
            // the shield began at 10000 (dShs)
            Assert.IsTrue(RespawnShieldRules.IsFromBeforeRespawn(9999, 10000), "a mine laid before the respawn");
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(10000, 10000), "laid the instant the shield began");
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(15000, 10000), "laid after the respawn");
        }

        [Test] public void ADirectHitOrAClearedShieldIsNeverFromBeforeTheRespawn()
        {
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(0, 10000), "0 = a direct hit with nothing set up earlier");
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(9999, 0), "no shield, so no respawn to be before");
        }

        [Test] public void TheBeforeRespawnComparisonSurvivesTheIntWrap()
        {
            int started = unchecked(int.MaxValue - 500);   // just before the wrap
            Assert.IsTrue(RespawnShieldRules.IsFromBeforeRespawn(unchecked(started - 1), started));
            Assert.IsFalse(RespawnShieldRules.IsFromBeforeRespawn(unchecked(started + 1000), started), "a mine laid after the wrap");
        }

        [Test] public void TheShieldStartIsAPlayerPropertyThatIsClearedWhenALobbyIsLeft()
        {
            Assert.AreEqual("dShs", RespawnShieldRules.StartKey);
            Assert.IsTrue(MatchPropertyReset.Build().ContainsKey(RespawnShieldRules.StartKey));
        }

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

        // ------------------------------------------------------------ Task 7b review: an old mine's slow, the attacker side, the builders

        [Test] public void AStatusCarriesWhenItsMineOrFenceWasSetUp()
        {
            StatusEffectSpec slow = PlacedEffects.PlacedSlow(2f, 0.4f, 7, placedMs: 4242);
            Assert.AreEqual(4242, slow.effectPlacedMs);
            Assert.AreEqual(StatusKind.Slow, slow.kind);
            Assert.AreEqual(2f, slow.duration);
            Assert.AreEqual(0.4f, slow.magnitude);
            Assert.AreEqual(7, slow.abilityId);
            Assert.AreEqual(0, new StatusEffectSpec { kind = StatusKind.Stun }.effectPlacedMs, "a direct status has nothing set up earlier");
        }

        [Test] public void AMinesBlastCarriesWhenTheMineWasLaid()
        {
            DamageInfo info = PlacedEffects.MineBlast(30f, 3, 1, Vector3.one, 9, placedMs: 111);
            Assert.AreEqual(111, info.EffectPlacedMs);
            Assert.AreEqual(DamageSource.Splash, info.Source);
            Assert.AreEqual(30f, info.Amount);
            Assert.AreEqual(3, info.SourceActorNumber);
        }

        [Test] public void AnElectricFencesTickCarriesWhenTheFenceWasRaised()
        {
            DamageInfo info = PlacedEffects.FenceTick(12f, 3, 1, Vector3.one, 9, placedMs: 222);
            Assert.AreEqual(222, info.EffectPlacedMs);
            Assert.AreEqual(DamageSource.Zone, info.Source);
        }

        [Test] public void AnAoeZonesTickCarriesWhenTheZoneWasPlaced()
        {
            DamageInfo info = PlacedEffects.AoeZoneTick(8f, 3, 1, Vector3.one, 9, placedMs: 333);
            Assert.AreEqual(333, info.EffectPlacedMs);
            Assert.AreEqual(DamageSource.Zone, info.Source);
        }

        [Test] public void AFireFieldsBurnCarriesWhenTheFieldWasLit()
        {
            DamageInfo info = PlacedEffects.FireFieldBurn(5f, 3, 1, weaponId: 4, Vector3.one, placedMs: 444);
            Assert.AreEqual(444, info.EffectPlacedMs);
            Assert.AreEqual(DamageSource.Burn, info.Source);
            Assert.AreEqual(4, info.WeaponId);
            Assert.AreEqual(-1, info.AbilityId, "a weapon's field, as before");
        }

        [Test] public void ABurnStatusTickCarriesWhenTheBurnWasLastLit()
        {
            DamageInfo info = PlacedEffects.StatusBurn(2f, 3, 1, Vector3.one, 9, appliedMs: 555);
            Assert.AreEqual(555, info.EffectPlacedMs);
            Assert.AreEqual(DamageSource.Burn, info.Source);
            Assert.AreEqual(9, info.AbilityId);
        }

        // The attacker side, pure: PlayerStatusEffects.Apply / PlayerDisplacement.Displace on a copy of someone else ask this and nothing else.
        [Test] public void AnEnemyCopyHitByMyOwnEffectIsReportedWithWhetherItWasSetUpBeforeMyRespawn()
        {
            RespawnShieldRules.EffectReportDecision old = RespawnShieldRules.EffectReport(false, true, true, false, false, effectPlacedMs: 900, myShieldStartMs: 1000);
            Assert.IsTrue(old.Report);
            Assert.IsTrue(old.FromBeforeRespawn, "an old mine's slow");
            RespawnShieldRules.EffectReportDecision fresh = RespawnShieldRules.EffectReport(false, true, true, false, true, effectPlacedMs: 1500, myShieldStartMs: 1000);
            Assert.IsTrue(fresh.Report);
            Assert.IsFalse(fresh.FromBeforeRespawn);
            Assert.IsTrue(fresh.VictimShielded);
        }

        [Test] public void OnlyMyOwnEffectOnALivingEnemyCopyIsReported()
        {
            Assert.IsFalse(RespawnShieldRules.EffectReport(true, true, true, false, false, 0, 1000).Report, "my own body: the victim's own client has the answer");
            Assert.IsFalse(RespawnShieldRules.EffectReport(false, false, true, false, false, 0, 1000).Report, "someone else's effect");
            Assert.IsFalse(RespawnShieldRules.EffectReport(false, true, false, false, false, 0, 1000).Report, "a dead victim");
            Assert.IsFalse(RespawnShieldRules.EffectReport(false, true, true, true, false, 0, 1000).Report, "a teammate");
        }

        [Test] public void ADirectEffectOrNoShieldStartIsNeverFromBeforeTheRespawnInTheReport()
        {
            Assert.IsFalse(RespawnShieldRules.EffectReport(false, true, true, false, false, effectPlacedMs: 0, myShieldStartMs: 1000).FromBeforeRespawn);
            Assert.IsFalse(RespawnShieldRules.EffectReport(false, true, true, false, false, effectPlacedMs: 900, myShieldStartMs: 0).FromBeforeRespawn);
        }

        // ------------------------------------------------------------ the wiring: the game calls the tested rule (the method bodies are read, not run)

        // Each check names the method that must do the calling (IlWiring.Uses with a method name): a call made somewhere else in the same class does
        // not count, so moving the hand-off out of the method that matters fails the test.
        private static bool Uses(System.Type owner, string method, MemberInfo target) => IlWiring.Uses(owner, method, target);

        private static MethodInfo Builder(string name) => typeof(PlacedEffects).GetMethod(name);

        [Test] public void MineBlastsAndSlowsGoThroughTheBuilders()
        {
            System.Type mine = typeof(Overpower.Abilities.Mine);
            Assert.IsTrue(Uses(mine, "ApplyBlast", Builder(nameof(PlacedEffects.MineBlast))), "the blast's damage");
            Assert.IsTrue(Uses(mine, "ApplyBlast", Builder(nameof(PlacedEffects.PlacedSlow))), "the slow carries the mine's placement time");
        }

        [Test] public void ElectricFenceTicksAndSlowsGoThroughTheBuilders()
        {
            System.Type fence = typeof(Overpower.Abilities.ElectricFence);
            Assert.IsTrue(Uses(fence, "EvaluateTrackedTargets", Builder(nameof(PlacedEffects.FenceTick))));
            Assert.IsTrue(Uses(fence, "EvaluateTrackedTargets", Builder(nameof(PlacedEffects.PlacedSlow))));
        }

        [Test] public void AoeZoneTicksGoThroughTheBuilder() =>
            Assert.IsTrue(Uses(typeof(Overpower.Abilities.AoeZone), "ApplyTick", Builder(nameof(PlacedEffects.AoeZoneTick))));

        [Test] public void FireFieldBurnsGoThroughTheBuilder() =>
            Assert.IsTrue(Uses(typeof(Overpower.Weapons.FireField), "BurnEveryoneInside", Builder(nameof(PlacedEffects.FireFieldBurn))));

        [Test] public void TheBurnStatusTickGoesThroughTheBuilder() =>
            Assert.IsTrue(Uses(typeof(PlayerStatusEffects), "ApplyBurnDamage", Builder(nameof(PlacedEffects.StatusBurn))));

        [Test] public void AStatusOnACopyPassesItsPlacementTimeToTheAttackersShield()
        {
            FieldInfo placed = typeof(StatusEffectSpec).GetField(nameof(StatusEffectSpec.effectPlacedMs));
            Assert.IsTrue(Uses(typeof(PlayerStatusEffects), "Apply", placed), "Apply must read spec.effectPlacedMs to hand it on");
            Assert.IsTrue(Uses(typeof(PlayerStatusEffects), "Apply", typeof(RespawnShield).GetMethod(nameof(RespawnShield.NoteMyEffectOnCopy))),
                "and it is Apply that tells the shield");
        }

        // The placement-time hand-off, run for real: a mine with a placement time hits a stand-in victim, and the slow the victim receives carries that time.
        // (The IL check above only proves the builder is called; this proves the number that goes in is the mine's own.)
        private class FakeVictim : MonoBehaviour, IDamageable, IStatusReceiver
        {
            public int slowPlacedMs = int.MinValue;
            public int damagePlacedMs = int.MinValue;
            public DamageResult ApplyDamage(in DamageInfo info) { damagePlacedMs = info.EffectPlacedMs; return default; }
            public void ApplyStatus(in StatusEffectSpec spec, int sourceActor) => slowPlacedMs = spec.effectPlacedMs;
            public bool IsAlive => true;
            public int TeamId => 1;
            public int ActorNumber => 9;
            public bool HasLocalAuthority => true;
        }

        [Test] public void AMineHandsItsPlacementTimeToTheSlowAndTheDamageItGives()
        {
            var mineObject = new GameObject("Test Mine");
            var victimObject = new GameObject("Test Victim");
            try
            {
                var mine = mineObject.AddComponent<Overpower.Abilities.Mine>();
                System.Type deployable = typeof(Overpower.Abilities.NetworkedDeployable);
                deployable.GetProperty("PlacedServerTimestampMs").SetValue(mine, 424242);
                deployable.GetProperty("OwnerActor").SetValue(mine, 1);
                deployable.GetProperty("OwnerTeam").SetValue(mine, 0);
                victimObject.transform.position = new Vector3(0f, 0.5f, 0.5f);
                victimObject.AddComponent<BoxCollider>().size = Vector3.one;
                var victim = victimObject.AddComponent<FakeVictim>();
                Physics.SyncTransforms();

                int hit = (int)typeof(Overpower.Abilities.Mine).GetMethod("ApplyBlast", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(mine, new object[] { Vector3.zero });

                Assert.AreEqual(1, hit, "the stand-in enemy was caught in the blast");
                Assert.AreEqual(424242, victim.slowPlacedMs, "the slow carries when the mine was laid");
                Assert.AreEqual(424242, victim.damagePlacedMs, "and so does the damage");
            }
            finally
            {
                Object.DestroyImmediate(mineObject);
                Object.DestroyImmediate(victimObject);
            }
        }

        [Test] public void TheAttackersNoteAsksTheTestedReportRuleAndTheStartProperty()
        {
            System.Type shield = typeof(RespawnShield);
            Assert.IsTrue(Uses(shield, "NoteMyEffectOnCopy", typeof(RespawnShieldRules).GetMethod(nameof(RespawnShieldRules.EffectReport))));
            Assert.IsTrue(Uses(shield, "NoteMyEffectOnCopy", typeof(RespawnShield).GetMethod(nameof(RespawnShield.ShieldStartOf))));
        }

        [Test] public void SonicPulseUsesThePushThatKnowsWhoPushed()
        {
            MethodInfo sourceAware = typeof(PlayerDisplacement).GetMethods()
                .Single(m => m.Name == "Displace" && m.GetParameters().Length == 5);
            Assert.IsTrue(Uses(typeof(Overpower.Abilities.SonicPulseAbility), "ExecuteCast", sourceAware));
        }

        [Test] public void ThePlayersDisplacementTellsTheAttackersShieldAboutAPushOnACopy()
        {
            MethodInfo note = typeof(RespawnShield).GetMethod(nameof(RespawnShield.NoteMyEffectOnCopy));
            Assert.IsTrue(Uses(typeof(PlayerDisplacement), "Displace", note));
            Assert.IsTrue(Uses(typeof(PlayerStatusEffects), "Apply", note));
        }
    }
}
