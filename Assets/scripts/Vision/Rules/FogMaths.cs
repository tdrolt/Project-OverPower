using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>The map between the world and the sight picture, in C# (the fog shader does the same sum): for debugging
    /// and for the minimap.</summary>
    public static class FogMaths
    {
        /// <summary>Where a world point is in the sight picture: (0,0) at the rect's lower corner, (1,1) at the upper one.
        /// rect = (min x, min z, size x, size z), the world square the picture covers; height is ignored.</summary>
        public static Vector2 WorldToSightUv(Vector3 world, Vector4 rect)
        {
            return new Vector2((world.x - rect.x) / rect.z, (world.z - rect.y) / rect.w);
        }

        /// <summary>True when the point's x and z are inside the rect (edges count). A rect with no size holds nothing.</summary>
        public static bool IsInsideRect(Vector3 world, Vector4 rect)
        {
            if (rect.z <= 0f || rect.w <= 0f)
                return false;
            Vector2 uv = WorldToSightUv(world, rect);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }
    }
}
