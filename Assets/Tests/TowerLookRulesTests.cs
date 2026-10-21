using NUnit.Framework;
using Overpower.Arena;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Arena rebuild step 1: columns per tier, where each column stands, and (Tudor's answer, 2026-09-19:
    /// "the capital gets 1 big column, the tier 2 two smaller columns, tier 3 and 4 columns of the same size and the
    /// same number as their tier 3") the column's own radius and the ring it stands on.</summary>
    public class TowerLookRulesTests
    {
        [Test]
        public void EachTierShowsItsOwnNumberOfColumns()
        {
            Assert.AreEqual(1, TowerLookRules.ColumnsForTier(1));
            Assert.AreEqual(2, TowerLookRules.ColumnsForTier(2));
            Assert.AreEqual(3, TowerLookRules.ColumnsForTier(3));
            Assert.AreEqual(4, TowerLookRules.ColumnsForTier(4));
        }

        [Test]
        public void AnOutOfRangeTierIsClampedNotThrown()
        {
            Assert.AreEqual(1, TowerLookRules.ColumnsForTier(0));
            Assert.AreEqual(1, TowerLookRules.ColumnsForTier(-3));
            Assert.AreEqual(4, TowerLookRules.ColumnsForTier(5));
        }

        [Test]
        public void ColumnsAreEvenlySpaced()
        {
            Assert.AreEqual(120f, TowerLookRules.ColumnYawDegrees(1, 3));
            Assert.AreEqual(240f, TowerLookRules.ColumnYawDegrees(2, 3));
            Assert.AreEqual(180f, TowerLookRules.ColumnYawDegrees(1, 2));
            Assert.AreEqual(270f, TowerLookRules.ColumnYawDegrees(3, 4));
        }

        [Test]
        public void TheFirstColumnAlwaysFacesTheFront()
        {
            for (int count = 1; count <= 4; count++)
                Assert.AreEqual(0f, TowerLookRules.ColumnYawDegrees(0, count));
        }

        // --- Tudor's answer, 2026-09-19: the capital's one column is BIG; tiers II-IV share the normal size. ---

        [Test]
        public void ColumnRadiusIsBigForTheCapitalAndNormalForEveryOtherTier()
        {
            const float normal = 0.35f;
            const float capital = 0.6f;

            Assert.AreEqual(capital, TowerLookRules.ColumnRadius(1, normal, capital));
            Assert.AreEqual(normal, TowerLookRules.ColumnRadius(2, normal, capital));
            Assert.AreEqual(normal, TowerLookRules.ColumnRadius(3, normal, capital));
            Assert.AreEqual(normal, TowerLookRules.ColumnRadius(4, normal, capital));

            // Clamped the same way ColumnsForTier is: below 1 reads as the capital (tier 1), above 4 as tier 4.
            Assert.AreEqual(capital, TowerLookRules.ColumnRadius(0, normal, capital));
            Assert.AreEqual(capital, TowerLookRules.ColumnRadius(-3, normal, capital));
            Assert.AreEqual(normal, TowerLookRules.ColumnRadius(5, normal, capital));
        }

        [Test]
        public void ColumnRingRadiusKeepsTheOuterEdgeInsideTheTowerRadius()
        {
            const float towerRadius = 2.6f;

            float normalRing = TowerLookRules.ColumnRingRadius(0.35f, towerRadius);
            float capitalRing = TowerLookRules.ColumnRingRadius(0.6f, towerRadius);

            Assert.AreEqual(2.25f, normalRing, 0.0001f);
            Assert.AreEqual(2.0f, capitalRing, 0.0001f);

            // The invariant this rule exists for: a column's outer edge (ring radius + the column's own radius)
            // never passes the tower's own collider radius, so columns never add cover over the plain round tower.
            Assert.LessOrEqual(normalRing + 0.35f, towerRadius + 0.0001f);
            Assert.LessOrEqual(capitalRing + 0.6f, towerRadius + 0.0001f);
        }

        [Test]
        public void ASpawnNeverPulsesWhileAnEnemyStandsInItOrDrainsIt()
        {
            var attacked = new CaptureRingState(CaptureRingPhase.Idle, 0f, -1, 1, -1, underAttack: true);
            var drained = new CaptureRingState(CaptureRingPhase.Draining, 0.6f, 1, 1, 2, underAttack: true);
            foreach (CaptureRingState state in new[] { attacked, drained })
            {
                OwnerPaint paint = OwnerPaint.From(TowerLookRules.Shown(state, spawn: true));
                Assert.AreEqual(OwnerPaintBase.Team, paint.Base, "a spawn keeps its owner's colour");
                Assert.AreEqual(1, paint.Team);
                Assert.IsFalse(paint.PulsesToWarning, "no attack flicker: a spawn cannot be taken");
                Assert.AreEqual(-1, paint.PulseTeam, "and no drain pulse");
                Assert.IsFalse(TowerLookRules.Shown(state, spawn: true).ShowsArc, "no capture band on a spawn");
            }

            var outOfPlay = new CaptureRingState(CaptureRingPhase.Idle, 0f, -1, -1, -1, false, outOfPlay: true);
            Assert.AreEqual(OwnerPaintBase.OutOfPlay, OwnerPaint.From(TowerLookRules.Shown(outOfPlay, spawn: true)).Base, "out of play still wins");
        }

        [Test]
        public void ACaptureTowerKeepsItsAttackAndDrainPulse()
        {
            var attacked = new CaptureRingState(CaptureRingPhase.Idle, 0f, -1, 1, -1, underAttack: true);
            Assert.IsTrue(OwnerPaint.From(TowerLookRules.Shown(attacked, spawn: false)).PulsesToWarning);
            var drained = new CaptureRingState(CaptureRingPhase.Draining, 0.6f, 1, 1, 2, underAttack: true);
            CaptureRingState shown = TowerLookRules.Shown(drained, spawn: false);
            Assert.AreEqual(2, OwnerPaint.From(shown).PulseTeam, "pulses toward the drainer");
            Assert.AreEqual(0.6f, shown.Fill01);
            Assert.AreEqual(CaptureRingPhase.Draining, shown.Phase);
        }

        [Test]
        public void EveryZoneAsksTheRuleBeforeItsRingAndTowerArePainted()
        {
            System.Reflection.MethodInfo rule = typeof(TowerLookRules).GetMethod(nameof(TowerLookRules.Shown));
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingCapture), "RefreshRingView", rule), "the ring and the tower read the same calmed state");
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingCapture), "RefreshRingView",
                typeof(BuildingManager).GetMethod(nameof(BuildingManager.IsSpawnZone))));
        }
    }
}
