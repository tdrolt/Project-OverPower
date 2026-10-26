using Overpower.Arena;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>The Teleport's mark: a flat hexagon ring on the floor in the movement look, on a portal while someone channels in it and at
    /// both ends of a trip on other players' screens. Built complete in one call, so a sight gate attached right after sees its renderer.</summary>
    public static class TeleportRing
    {
        public const string MarkName = "Teleport VFX (cheap, cosmetic only)";
        private const int HexSides = 6;
        private const float FloorLift = 0.03f; // keeps the ring off the floor's own surface; not a look number
        private const float FlatTopYaw = 30f; // half a side off the floor's pointy-topped hex tiles, so the ring never reads as a lit tile

        /// <summary>The ring, corners on <paramref name="radius"/> around the point, on the floor under it (at the point's own height where
        /// there is no floor). Null without a radius or an Arena Look with FX Alpha.</summary>
        public static GameObject Build(Vector3 point, float radius, Color colour, ArenaLook look)
        {
            if (radius <= 0f || look == null || look.fxAlpha == null)
                return null;
            float floorY = GroundSnap.TryFindGroundY(point, out float groundY) ? groundY : point.y;
            var mark = new GameObject(MarkName);
            mark.transform.SetPositionAndRotation(new Vector3(point.x, floorY + FloorLift, point.z), Quaternion.Euler(90f, FlatTopYaw, 0f));
            LineRenderer ring = mark.AddComponent<LineRenderer>();
            ring.sharedMaterial = look.fxAlpha;
            ring.alignment = LineAlignment.TransformZ; // with the mark turned 90 degrees on X the band lies flat (VisualTint.FillFlatCircle)
            ring.numCornerVertices = 0;
            ring.numCapVertices = 0;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.widthMultiplier = look.teleportRingWidth;
            VisualTint.FillFlatCircle(ring, radius, HexSides);
            VisualTint.SetLineColor(ring, VisualTint.WithAlpha(colour, look.teleportRingAlpha));
            return mark;
        }
    }
}
