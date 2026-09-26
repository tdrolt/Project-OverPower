using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The phase-two wall's shape. A toy equilateral triangle (circumradius 10, centred at (10, 20), apex up)
    /// cut 2 m past the centre toward the apex with a 2 x 1 m recess; then the real arena outline, cut toward each of
    /// the three capitals with the values the design started from (inputs of this test, not Tudor's tuning).</summary>
    public class PhaseTwoCutGeometryTests
    {
        private static readonly Vector2 ToyCentre = new Vector2(10f, 20f);
        private static readonly List<Vector2> Toy = new List<Vector2>
        {
            new Vector2(10f, 30f), new Vector2(10f - 8.660254f, 15f), new Vector2(10f + 8.660254f, 15f),
        };

        private static PhaseTwoCutGeometry ToyCut(float distance = 2f, float width = 2f, float depth = 1f) =>
            PhaseTwoCutGeometry.Build(Toy, ToyCentre, Vector2.up, distance, width, depth, wallThickness: 0.2f);

        [Test]
        public void TheCentreStaysOpenAndTheApexCloses()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.NotNull(cut);
            Assert.Greater(cut.Playable.SignedDistance(ToyCentre), 0f);
            Assert.Less(cut.Closed.SignedDistance(ToyCentre), 0f);
            Vector2 nearApex = new Vector2(10f, 26f);
            Assert.Less(cut.Playable.SignedDistance(nearApex), 0f);
            Assert.Greater(cut.Closed.SignedDistance(nearApex), 0f);
            Assert.IsTrue(cut.IsBehindWall(new Vector3(10f, 0.5f, 26f)));
            Assert.IsFalse(cut.IsBehindWall(new Vector3(10f, 0.5f, 20f)));
        }

        [Test]
        public void TheRecessIsOpenAndBesideItIsClosed()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Vector2 inRecess = new Vector2(10f, 22.5f);
            Assert.Greater(cut.Playable.SignedDistance(inRecess), 0f);
            Assert.IsFalse(cut.IsBehindWall(new Vector3(inRecess.x, 0f, inRecess.y)));
            Vector2 besideRecess = new Vector2(7f, 22.5f);
            Assert.Greater(cut.Closed.SignedDistance(besideRecess), 0f);
        }

        [Test]
        public void TheWallLineRunsFromOuterWallToOuterWallThroughTheRecess()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            ArenaBounds full = ArenaBounds.FromPolygon(Toy);
            Assert.AreEqual(6, cut.WallLine.Count);
            Assert.AreEqual(0f, full.SignedDistance(cut.WallLine[0]), 1e-4f, "starts on the outline");
            Assert.AreEqual(0f, full.SignedDistance(cut.WallLine[5]), 1e-4f, "ends on the outline");
            for (int i = 0; i < 6; i++)
            {
                float past = cut.WallLine[i].y - ToyCentre.y;
                Assert.IsTrue(Mathf.Abs(past - 2f) < 1e-4f || Mathf.Abs(past - 3f) < 1e-4f, $"point {i} is on the wall or the recess back");
            }
        }

        [Test]
        public void OneWallBoxPerSegmentFacingTheOpenSide()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.AreEqual(cut.WallLine.Count - 1, cut.WallRuns.Count);
            for (int i = 0; i < cut.WallRuns.Count; i++)
            {
                ArenaWallPlan.Run run = cut.WallRuns[i];
                Vector2 a = cut.WallLine[i], b = cut.WallLine[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x);
                Assert.AreEqual(0f, Vector2.Dot(run.InnerStart - a, normal), 1e-4f, $"run {i} starts on its segment's line");
                Assert.AreEqual(0f, Vector2.Dot(run.InnerEnd - a, normal), 1e-4f, $"run {i} ends on its segment's line");
                Vector2 middle = (a + b) * 0.5f;
                Assert.Greater(cut.Playable.SignedDistance(middle + run.Inward * 0.05f), 0f, $"run {i} faces the open side");
            }
        }

        [Test]
        public void AClockwiseOutlineCutsTheSame()
        {
            // The reviewer checked the algorithm is winding-agnostic by an independent re-run (2026-09-25): this pins
            // that same guarantee for the real test suite. Toy is listed one way round; reversing it lists the same
            // triangle the other way, so this must cut exactly the same shape.
            var reversed = new List<Vector2>(Toy);
            reversed.Reverse();
            PhaseTwoCutGeometry cut = PhaseTwoCutGeometry.Build(reversed, ToyCentre, Vector2.up, 2f, 2f, 1f, wallThickness: 0.2f);

            Assert.NotNull(cut);
            Assert.Greater(cut.Playable.SignedDistance(ToyCentre), 0f, "the centre is playable");
            Assert.Greater(cut.Closed.SignedDistance(new Vector2(10f, 26f)), 0f, "(10, 26) is closed");
            Assert.Greater(cut.Playable.SignedDistance(new Vector2(10f, 22.5f)), 0f, "the recess point is playable");
            Assert.AreEqual(5, cut.WallRuns.Count);
            for (int i = 0; i < cut.WallRuns.Count; i++)
            {
                ArenaWallPlan.Run run = cut.WallRuns[i];
                Vector2 a = cut.WallLine[i], b = cut.WallLine[i + 1];
                Vector2 middle = (a + b) * 0.5f;
                Assert.Greater(cut.Playable.SignedDistance(middle + run.Inward * 0.05f), 0f, $"run {i} faces the open side");
            }
        }

        [Test]
        public void TheBarrierStandsAcrossTheRecessMouth()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.IsTrue(cut.HasRecess);
            Assert.AreEqual(10f, cut.BarrierCentre.x, 1e-4f);
            Assert.AreEqual(22f, cut.BarrierCentre.y, 1e-4f);
            Vector3 length = Quaternion.Euler(0f, cut.BarrierYawDegrees, 0f) * Vector3.right;
            Assert.AreEqual(0f, length.z, 1e-4f, "its length runs along the wall, across the toy's cut direction (+Z)");
        }

        // Centre-Tier-III-walls Part 2, 2026-09-26 ("the zone is too empty"): the toy's cut runs straight up (+y, u =
        // Vector2.up), so BarrierCentre (10, 22) and the cut axis is the vertical line x=10 - hand-derived, not read
        // back off PlankCentres itself.
        [Test]
        public void PlankCentresMirrorAcrossTheAxisAndSitOnTheOpenSide()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            (Vector2 first, Vector2 second) = cut.PlankCentres(spacing: 0.5f, inFront: 0.3f);

            Assert.AreEqual(9.5f, first.x, 1e-4f);
            Assert.AreEqual(21.7f, first.y, 1e-4f);
            Assert.AreEqual(10.5f, second.x, 1e-4f);
            Assert.AreEqual(21.7f, second.y, 1e-4f);

            // Mirrored across the cut axis (x = 10): equally either side of it, the same distance off the wall line.
            Assert.AreEqual(20f, first.x + second.x, 1e-4f, "mirrors across the cut axis");
            Assert.AreEqual(first.y, second.y, 1e-4f);

            Assert.Greater(cut.Playable.SignedDistance(first), 0f, "on the open side of the wall line");
            Assert.Greater(cut.Playable.SignedDistance(second), 0f, "on the open side of the wall line");
        }

        [Test]
        public void NoRecessMeansAStraightWall()
        {
            PhaseTwoCutGeometry cut = ToyCut(width: 0f);
            Assert.NotNull(cut);
            Assert.IsFalse(cut.HasRecess);
            Assert.AreEqual(2, cut.WallLine.Count);
            Assert.AreEqual(1, cut.WallRuns.Count);
            Assert.Greater(cut.Closed.SignedDistance(new Vector2(10f, 22.5f)), 0f);
        }

        [Test]
        public void AWallThatMissesTheArenaOrARecessThatDoesNotFitIsRefused()
        {
            Assert.IsNull(ToyCut(distance: 20f), "past the apex: the line crosses nothing");
            Assert.IsNull(ToyCut(depth: 50f), "the recess would poke out of the arena");
            Assert.IsNull(ToyCut(width: 30f), "the recess is wider than the wall");
        }

        // ---- the real arena (the Source outline and tower positions of Game Scene, arena rebuild plan section B)

        private static readonly Vector3 ArenaCentre = new Vector3(65.05f, 0f, 53.34f);
        private static readonly List<Vector2> SourceOutline = new List<Vector2>
        {
            new Vector2(96.184f, 60.034f), new Vector2(98.998505f, 61.659004f), new Vector2(89.22851f, 78.581f),
            new Vector2(86.414f, 76.956f), new Vector2(75.451f, 95.944f), new Vector2(75.451f, 121.401f),
            new Vector2(54.649f, 121.401f), new Vector2(54.649f, 95.944f),
        };
        private static readonly Vector2[] Zone =
        {
            new Vector2(35.59f, 36.33f), new Vector2(94.51f, 36.33f), new Vector2(65.05f, 87.36f),   // Tier II 0-2
            new Vector2(65.05f, 32.90f), new Vector2(82.75f, 63.56f), new Vector2(47.35f, 63.56f),   // Tier III 3-5
            new Vector2(15.11f, 24.51f), new Vector2(114.99f, 24.51f), new Vector2(65.05f, 111.00f), // capitals 6-8
            new Vector2(65.05f, 53.34f),                                                              // centre 9
        };
        private static readonly int[][] CutZonesOfTeam = { new[] { 0, 3, 5, 6 }, new[] { 1, 3, 4, 7 }, new[] { 2, 4, 5, 8 } };

        private static PhaseTwoCutGeometry RealCut(int team)
        {
            IReadOnlyList<Vector2> full = ArenaBounds.FromSourceOutline(SourceOutline, ArenaCentre).Polygon;
            Vector2 centre = new Vector2(ArenaCentre.x, ArenaCentre.z);
            return PhaseTwoCutGeometry.Build(full, centre, Zone[6 + team] - centre, 6.3f, 19.54f, 3.25f, 0.724f);
        }

        [Test]
        public void EachCornerCutsItsOwnZonesAndKeepsTheRest([Values(0, 1, 2)] int team)
        {
            PhaseTwoCutGeometry cut = RealCut(team);
            Assert.NotNull(cut);
            for (int zone = 0; zone < Zone.Length; zone++)
            {
                bool shouldClose = System.Array.IndexOf(CutZonesOfTeam[team], zone) >= 0;
                if (shouldClose)
                    Assert.Greater(cut.Closed.SignedDistance(Zone[zone]), 0f, $"zone {zone} is behind team {team}'s wall");
                else
                    Assert.Greater(cut.Playable.SignedDistance(Zone[zone]), 0f, $"zone {zone} stays open when team {team} is cut");
            }
        }

        [Test]
        public void TheCentresWholeCaptureAreaStaysReachable([Values(0, 1, 2)] int team)
        {
            PhaseTwoCutGeometry cut = RealCut(team);
            for (int i = 0; i < 72; i++)
            {
                float radians = i * 5f * Mathf.Deg2Rad;
                Vector2 edge = Zone[9] + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 8f;
                Assert.GreaterOrEqual(cut.Playable.SignedDistance(edge), 0f, $"ring point at {i * 5} degrees");
            }
        }
    }
}
