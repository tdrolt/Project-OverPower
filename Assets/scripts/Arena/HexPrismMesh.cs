using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The six-sided prism of a Dominion spawn tower (PrismMesh at six sides): corners at 0, 60 ... 300 degrees
    /// of yaw, the first facing +Z like the first column.</summary>
    public static class HexPrismMesh
    {
        public const string MeshName = "Hex Prism";
        public const int Sides = 6;
        public const int VertexCount = Sides * 4 + 2 * (Sides + 1);

        public static Mesh Create() => PrismMesh.Create(Sides, MeshName);
    }
}
