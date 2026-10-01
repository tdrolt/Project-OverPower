using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Data;
using Overpower.Net;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 8 (the Scope trade and X-Ray blind hits). Literal numbers only: no tuning value is pinned. The scoped
    /// shape itself is VisionRulesTests (Task 1); this is the wiring around it and the reveal timer.
    /// </summary>
    public class ScopeSightAndRevealTests
    {
        static readonly SightShape Normal = new SightShape(90f, 20f, 7f);
        static readonly SightShape Scoped = new SightShape(30f, 26f, 4f);

        static SightCandidate C(int team, bool alive, bool isLocal, bool scoped, float x) =>
            new SightCandidate(team, alive, isLocal, new Vector2(x, 0), Vector2.up, 0f, scoped);

        // ---- the eye list: a scoped player's eye uses the scoped shape, everyone else's the normal one

        [Test]
        public void ScopedTeammateGetsTheScopedShapeAndTheOtherTheNormalOne()
        {
            var list = new List<SightCandidate> { C(1, true, true, false, 0), C(1, true, false, true, 10) };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.AreEqual(2, eyes.Count);
            Assert.AreEqual(Normal.ConeAngleDegrees, eyes[0].Shape.ConeAngleDegrees);
            Assert.AreEqual(Normal.CircleRadius, eyes[0].Shape.CircleRadius);
            Assert.AreEqual(Scoped.ConeAngleDegrees, eyes[1].Shape.ConeAngleDegrees);
            Assert.AreEqual(Scoped.ConeLength, eyes[1].Shape.ConeLength);
            Assert.AreEqual(Scoped.CircleRadius, eyes[1].Shape.CircleRadius);
        }

        [Test]
        public void TheLocalPlayerScopedUsesTheScopedShapeToo()
        {
            var list = new List<SightCandidate> { C(1, true, true, true, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.AreEqual(Scoped.ConeLength, eyes[0].Shape.ConeLength);
        }

        [Test]
        public void TheOldBuildCallStillGivesEveryoneTheOneShape()
        {
            var list = new List<SightCandidate> { C(1, true, true, true, 0) };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Normal, eyes);
            Assert.AreEqual(Normal.ConeLength, eyes[0].Shape.ConeLength);
        }

        [Test]
        public void ADeadScopedTeammateIsStillNoEye()
        {
            var list = new List<SightCandidate> { C(1, true, true, false, 0), C(1, false, false, true, 10) };
            var eyes = new List<Eye>();
            SightEyes.Build(list, 1, ViewerMode.Alive, -1, Normal, Scoped, eyes);
            Assert.AreEqual(1, eyes.Count);
        }

        // ---- the vScp Player Property

        [Test] public void TheKeyIsVScp() => Assert.AreEqual("vScp", ScopeSightProperty.Key);
        [Test] public void AMissingKeyReadsAsNotScoped() => Assert.IsFalse(ScopeSightProperty.Read(null));
        [Test] public void TrueReadsAsScoped() => Assert.IsTrue(ScopeSightProperty.Read(true));
        [Test] public void FalseReadsAsNotScoped() => Assert.IsFalse(ScopeSightProperty.Read(false));
        [Test] public void AValueOfTheWrongTypeReadsAsNotScoped() => Assert.IsFalse(ScopeSightProperty.Read("yes"));

        [Test]
        public void NothingIsWrittenWhenTheValueDidNotChange()
        {
            Assert.IsFalse(ScopeSightProperty.ShouldPublish(false, false));
            Assert.IsFalse(ScopeSightProperty.ShouldPublish(true, true));
        }

        [Test]
        public void AChangeEitherWayIsWritten()
        {
            Assert.IsTrue(ScopeSightProperty.ShouldPublish(false, true));
            Assert.IsTrue(ScopeSightProperty.ShouldPublish(true, false));
        }

        [Test]
        public void GivingUpAMatchRemovesTheScopeFlag()
        {
            var resets = MatchPropertyReset.Build();
            Assert.IsTrue(resets.ContainsKey(ScopeSightProperty.Key));
            Assert.IsNull(resets[ScopeSightProperty.Key], "removed, so a missing key reads as not scoped");
        }

        // ---- the Scope module: holding it is what the flag follows

        GameObject go;
        ScopeAbility scope;

        [SetUp] public void MakeScope() { go = new GameObject("TestScope_Sight"); scope = go.AddComponent<ScopeAbility>(); }
        [TearDown] public void KillScope() { Object.DestroyImmediate(go); }

        [Test] public void NotHoldingBeforeAnything() => Assert.IsFalse(scope.IsHoldingSight);

        [Test]
        public void HoldingWhileAbleToActIsHoldingTheSight()
        {
            scope.OwnerTick(0.016f, held: true, canAct: true);
            Assert.IsTrue(scope.IsHoldingSight);
        }

        [Test]
        public void LettingGoEndsIt()
        {
            scope.OwnerTick(0.016f, true, true);
            scope.OwnerTick(0.016f, false, true);
            Assert.IsFalse(scope.IsHoldingSight);
        }

        [Test]
        public void HoldingWhileUnableToActIsNotHolding()
        {
            scope.OwnerTick(0.016f, true, false);
            Assert.IsFalse(scope.IsHoldingSight);
        }

        [Test]
        public void DyingEndsIt()
        {
            scope.OwnerTick(0.016f, true, true);
            scope.Interrupt(InterruptReason.Died);
            Assert.IsFalse(scope.IsHoldingSight);
        }

        [Test]
        public void RespawningEndsIt()
        {
            scope.OwnerTick(0.016f, true, true);
            scope.OnRespawned();
            Assert.IsFalse(scope.IsHoldingSight);
        }

        [Test]
        public void UnequippingEndsIt()
        {
            scope.OwnerTick(0.016f, true, true);
            scope.Interrupt(InterruptReason.Unequipped);
            Assert.IsFalse(scope.IsHoldingSight);
        }

        // ---- the reveal timer

        [Test]
        public void ARevealLastsExactlyItsSeconds()
        {
            var t = new RevealTimers();
            t.Reveal(5, 10f, 0.5f);
            Assert.IsTrue(t.IsRevealed(5, 10.2f));
            Assert.IsTrue(t.IsRevealed(5, 10.49f));
            Assert.IsFalse(t.IsRevealed(5, 10.5f));
            Assert.IsFalse(t.IsRevealed(5, 11f));
        }

        [Test] public void NeverRevealedMeansNotRevealed() => Assert.IsFalse(new RevealTimers().IsRevealed(5, 0f));

        [Test]
        public void ASecondHitExtends()
        {
            var t = new RevealTimers();
            t.Reveal(5, 10f, 0.5f);
            t.Reveal(5, 10.4f, 0.5f);
            Assert.IsTrue(t.IsRevealed(5, 10.8f));
            Assert.IsFalse(t.IsRevealed(5, 10.9f));
        }

        [Test]
        public void AShorterSecondHitDoesNotShorten()
        {
            var t = new RevealTimers();
            t.Reveal(5, 10f, 2f);
            t.Reveal(5, 10.1f, 0.5f);
            Assert.IsTrue(t.IsRevealed(5, 11.5f));
        }

        [Test]
        public void ZeroSecondsRevealsNothing()
        {
            var t = new RevealTimers();
            t.Reveal(5, 10f, 0f);
            Assert.IsFalse(t.IsRevealed(5, 10f));
        }

        [Test]
        public void ARevealIsPerPlayer()
        {
            var t = new RevealTimers();
            t.Reveal(5, 10f, 0.5f);
            Assert.IsFalse(t.IsRevealed(6, 10.1f));
        }

        [Test]
        public void ClearForgetsEveryone()
        {
            var t = new RevealTimers();
            t.Reveal(5, 10f, 5f);
            t.Clear();
            Assert.IsFalse(t.IsRevealed(5, 10.1f));
        }

        // ---- when a hit reveals

        [Test] public void AFriendlyShooterHittingAnEnemyWithARevealWeaponReveals() => Assert.IsTrue(RevealOnHitRule.ShouldReveal(true, true, 0.5f));
        [Test] public void AWeaponWithZeroNeverReveals() => Assert.IsFalse(RevealOnHitRule.ShouldReveal(true, true, 0f));
        [Test] public void AnEnemyShooterNeverRevealsToMe() => Assert.IsFalse(RevealOnHitRule.ShouldReveal(false, true, 0.5f));
        [Test] public void HittingAFriendNeverReveals() => Assert.IsFalse(RevealOnHitRule.ShouldReveal(true, false, 0.5f));
        [Test] public void ANegativeValueNeverReveals() => Assert.IsFalse(RevealOnHitRule.ShouldReveal(true, true, -1f));

        [Test]
        public void ANewWeaponDefinitionRevealsNothing()
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            try { Assert.AreEqual(0f, weapon.RevealOnHitSeconds); }
            finally { Object.DestroyImmediate(weapon); }
        }

        // ---- the wiring on the player prefab

        [Test]
        public void ThePlayerPrefabPointsTeamSightAtTheScopeModule()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Multiplayer Player.prefab");
            Assert.IsNotNull(prefab);
            var sight = prefab.GetComponent<TeamSight>();
            Assert.IsNotNull(sight);
            var field = new SerializedObject(sight).FindProperty("scopeSight");
            Assert.IsNotNull(field, "TeamSight has no scopeSight field");
            Assert.IsNotNull(field.objectReferenceValue as ScopeAbility, "scopeSight must point at the Scope prefab's ScopeAbility");
        }
    }
}
