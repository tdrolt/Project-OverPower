using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The floor's hexagon grid in C#. The arena shader (ArenaHex.hlsl) does the same sums line for line, so the tiles the
    /// game paints are the tiles the floor draws; the grid turns with the arena's thirds about its origin.</summary>
    public class HexGridTests
    {
        private static readonly Vector2 Origin = new Vector2(65.05f, 53.34f);
        private const float Size = 3f;

        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(1, -1),
        };

        [Test]
        public void TileAtCentreOfRoundTrips()
        {
            for (int q = -8; q <= 8; q++)
                for (int r = -8; r <= 8; r++)
                {
                    var axial = new Vector2Int(q, r);
                    Vector2 centre = HexGrid.CentreOf(axial, Origin, Size);
                    Assert.AreEqual(axial, HexGrid.TileAt(centre, Origin, Size), "centre of " + axial);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * 60f * Mathf.Deg2Rad + 0.3f;
                        Vector2 inside = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.4f * Size);
                        Assert.AreEqual(axial, HexGrid.TileAt(inside, Origin, Size), "a point inside " + axial);
                    }
                }
        }

        [Test]
        public void NeighboursOneTileApart()
        {
            var tile = new Vector2Int(3, -5);
            Vector2 centre = HexGrid.CentreOf(tile, Origin, Size);
            foreach (Vector2Int step in Neighbours)
                Assert.AreEqual(Size, Vector2.Distance(centre, HexGrid.CentreOf(tile + step, Origin, Size)), 1e-4f, "neighbour " + step);
            Assert.AreEqual(Size * Mathf.Sqrt(3f), Vector2.Distance(centre, HexGrid.CentreOf(tile + new Vector2Int(1, 1), Origin, Size)), 1e-4f,
                "the next ring is further than one tile");
        }

        [Test]
        public void SymmetricUnder120DegreeTurnsAboutOrigin()
        {
            for (int q = -10; q <= 10; q++)
                for (int r = -10; r <= 10; r++)
                {
                    Vector2 centre = HexGrid.CentreOf(new Vector2Int(q, r), Origin, Size);
                    Vector2 point = centre + new Vector2(0.21f, -0.33f) * Size;
                    foreach (float turn in new[] { 120f, 240f })
                    {
                        Vector2 turnedCentre = Turn(HexGrid.CentreOf(HexGrid.TileAt(point, Origin, Size), Origin, Size), turn);
                        Vector2 centreOfTurned = HexGrid.CentreOf(HexGrid.TileAt(Turn(point, turn), Origin, Size), Origin, Size);
                        Assert.AreEqual(0f, Vector2.Distance(turnedCentre, centreOfTurned), 1e-3f, $"tile ({q},{r}) turned {turn}");
                    }
                }
        }

        [Test]
        public void TheOriginIsATileCentreAndACornerPointsToPlusZ()
        {
            Assert.AreEqual(Vector2Int.zero, HexGrid.TileAt(Origin, Origin, Size));
            Assert.AreEqual(0f, Vector2.Distance(Origin, HexGrid.CentreOf(Vector2Int.zero, Origin, Size)), 1e-5f);
            // Across the flats it is half a tile to the edge; toward a corner it is further (half a tile / cos 30).
            float between = 0.5f * Size * 1.08f;
            Assert.AreEqual(Vector2Int.zero, HexGrid.TileAt(Origin + new Vector2(0f, between), Origin, Size), "+Z is a corner");
            Assert.AreNotEqual(Vector2Int.zero, HexGrid.TileAt(Origin + new Vector2(between, 0f), Origin, Size), "+X is a flat side");
        }

        [Test]
        public void TexelIsTheTileLessTheMinimum()
        {
            Assert.AreEqual(new Vector2Int(7, 4), HexGrid.Texel(new Vector2Int(5, -3), new Vector2Int(-2, -7)));
            Assert.AreEqual(Vector2Int.zero, HexGrid.Texel(new Vector2Int(-2, -7), new Vector2Int(-2, -7)));
        }

        private static Vector2 Turn(Vector2 point, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector2 d = point - Origin;
            return Origin + new Vector2(c * d.x - s * d.y, s * d.x + c * d.y);
        }
    }
}
