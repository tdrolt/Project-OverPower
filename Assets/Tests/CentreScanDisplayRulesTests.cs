using NUnit.Framework;
using UnityEngine;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 11: the pure pieces behind the centre scan's displays - how long a dot lives and fades, who gets the
    /// scan on their maps, how the wave's world radius becomes a minimap radius, and how far the wave must travel.
    /// The numbers are the test's own.
    /// </summary>
    public class CentreScanDisplayRulesTests
    {
        // ---- dot lifetime ----

        [Test]
        public void AFreshDotIsFullyThere()
        {
            Assert.AreEqual(1f, CentreScanDisplayRules.DotAlpha(0f, 4f), 1e-4f);
        }

        [Test]
        public void ADotIsStillFullBeforeItsLastSecond()
        {
            Assert.AreEqual(1f, CentreScanDisplayRules.DotAlpha(2.9f, 4f), 1e-4f);
        }

        [Test]
        public void ADotFadesOutOverItsLastSecond()
        {
            Assert.AreEqual(0.5f, CentreScanDisplayRules.DotAlpha(3.5f, 4f), 1e-4f);
        }

        [Test]
        public void ADotIsGoneAtItsLife_AndAfter()
        {
            Assert.AreEqual(0f, CentreScanDisplayRules.DotAlpha(4f, 4f), 1e-4f);
            Assert.AreEqual(0f, CentreScanDisplayRules.DotAlpha(9f, 4f), 1e-4f);
        }

        [Test]
        public void ADotThatLivesLessThanTheFadeFadesOverItsWholeLife()
        {
            Assert.AreEqual(0.5f, CentreScanDisplayRules.DotAlpha(0.25f, 0.5f), 1e-4f);
        }

        [Test]
        public void AZeroLifeMeansNoDot()
        {
            Assert.AreEqual(0f, CentreScanDisplayRules.DotAlpha(0f, 0f), 1e-4f);
        }

        [Test]
        public void ThePoolDropsADotOnceItsLifeIsUp()
        {
            var pool = new ScanDotPool();
            pool.Add(new Vector3(1f, 0f, 2f), 10f, 5000, 3);
            pool.Prune(13.9f, 4f);
            Assert.AreEqual(1, pool.Dots.Count);
            pool.Prune(14f, 4f);
            Assert.AreEqual(0, pool.Dots.Count);
        }

        [Test]
        public void ADotStaysWhereItWasCaught()
        {
            var pool = new ScanDotPool();
            pool.Add(new Vector3(7f, 0f, -3f), 1f, 100, 2);
            pool.Prune(2f, 4f);
            Assert.AreEqual(new Vector3(7f, 0f, -3f), pool.Dots[0].Position);
            Assert.AreEqual(2, pool.Dots[0].Actor);
        }

        [Test]
        public void EachPassIsANewDot_TheSamePlayerTwiceMakesTwo()
        {
            var pool = new ScanDotPool();
            pool.Add(new Vector3(5f, 0f, 0f), 1f, 100, 3);
            pool.Add(new Vector3(60f, 0f, 0f), 2f, 200, 3);
            Assert.AreEqual(2, pool.Dots.Count);
            Assert.AreEqual(2, pool.TotalAdded);
        }

        [Test]
        public void TheTotalKeepsCountingAfterDotsExpire()
        {
            var pool = new ScanDotPool();
            pool.Add(Vector3.zero, 0f, 0, 1);
            pool.Prune(10f, 4f);
            pool.Add(Vector3.zero, 10f, 0, 1);
            Assert.AreEqual(1, pool.Dots.Count);
            Assert.AreEqual(2, pool.TotalAdded);
        }

        [Test]
        public void ThePoolNeverGrowsPastItsLimit_AndKeepsTheNewest()
        {
            var pool = new ScanDotPool();
            for (int i = 0; i < ScanDotPool.MaxDots + 10; i++)
                pool.Add(Vector3.zero, i, i, 1);
            Assert.AreEqual(ScanDotPool.MaxDots, pool.Dots.Count);
            Assert.AreEqual(ScanDotPool.MaxDots + 9, pool.Dots[pool.Dots.Count - 1].ServerMs);
        }

        // ---- who sees the scan ----

        [Test]
        public void TheHoldingTeamSeesTheScan() => Assert.IsTrue(CentreScanDisplayRules.SeesScan(2, 2));

        [Test]
        public void AnotherTeamDoesNotSeeItOnTheMaps() => Assert.IsFalse(CentreScanDisplayRules.SeesScan(1, 2));

        [Test]
        public void NobodySeesAScanOfNoHolder() => Assert.IsFalse(CentreScanDisplayRules.SeesScan(-1, -1));

        [Test]
        public void AViewerWithNoTeamYetSeesNothing() => Assert.IsFalse(CentreScanDisplayRules.SeesScan(-1, 2));

        // ---- the minimap ring ----

        [Test]
        public void TheMinimapRadiusIsTheWorldRadiusTimesMapUnitsPerMetre()
        {
            // 200 m drawn across 100 map units: half a unit per metre.
            Assert.AreEqual(20f, CentreScanDisplayRules.MinimapRadius(40f, 200f, 100f), 1e-4f);
        }

        [Test]
        public void ABiggerMapGrowsTheRingWithIt()
        {
            Assert.AreEqual(2f * CentreScanDisplayRules.MinimapRadius(40f, 200f, 100f),
                            CentreScanDisplayRules.MinimapRadius(40f, 200f, 200f), 1e-4f);
        }

        [Test]
        public void AnUnknownWorldSizeDrawsNoRing()
        {
            Assert.AreEqual(0f, CentreScanDisplayRules.MinimapRadius(40f, 0f, 100f), 1e-4f);
        }

        // ---- how far the wave must go ----

        [Test]
        public void TheWaveMustReachTheFarthestOutlinePoint()
        {
            var outline = new[] { new Vector2(10f, 0f), new Vector2(0f, 30f), new Vector2(-20f, -20f) };
            Assert.AreEqual(30f, CentreScanDisplayRules.MaxRadius(outline, Vector2.zero), 1e-4f);
        }

        [Test]
        public void TheFarthestPointIsMeasuredFromTheCentreNotFromTheOrigin()
        {
            var outline = new[] { new Vector2(100f, 100f), new Vector2(110f, 100f), new Vector2(100f, 85f) };
            Assert.AreEqual(15f, CentreScanDisplayRules.MaxRadius(outline, new Vector2(100f, 100f)), 1e-4f);
        }

        [Test]
        public void NoOutlineMeansNoRadius()
        {
            Assert.AreEqual(0f, CentreScanDisplayRules.MaxRadius(null, Vector2.zero), 1e-4f);
            Assert.AreEqual(0f, CentreScanDisplayRules.MaxRadius(new Vector2[0], Vector2.zero), 1e-4f);
        }
    }
}
