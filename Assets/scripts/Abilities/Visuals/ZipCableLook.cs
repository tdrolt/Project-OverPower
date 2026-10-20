using Overpower.Arena;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Zip Gun's cable, shared by the bolt in flight and the pull: a straight line drawn with the look's cable material, whose texture
    /// is a thin segment then a chevron, repeated every Zip Chevron Spacing metres from the shooter's end. Scrolling that texture runs the
    /// chevrons toward the target. The colour and the scroll go on through a property block, so the material file never changes while
    /// playing; the cable material is a plain Unlit because the particle shader ignores a texture offset.
    /// </summary>
    public static class ZipCableLook
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>The texture's shift along the cable at this moment, in tiles, kept in (-1, 0]. A falling value moves each chevron away
        /// from the shooter's end, at <paramref name="speed"/> metres per second.</summary>
        public static float Scroll(float seconds, float speed, float spacing)
        {
            if (spacing <= 0f)
                return 0f;
            float tiles = seconds * speed / spacing;
            float within = tiles - Mathf.Floor(tiles);
            return within > 0f ? -within : 0f;
        }

        /// <summary>Makes <paramref name="line"/> the cable: the look's material, width and pattern size. Positions stay the caller's.</summary>
        public static void Configure(LineRenderer line, ArenaLook look)
        {
            if (line == null || look == null)
                return;
            if (look.zipCable != null)
                line.sharedMaterial = look.zipCable;
            line.widthMultiplier = look.zipCableWidth;
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.textureMode = LineTextureMode.Tile;
            line.textureScale = new Vector2(1f / Mathf.Max(0.05f, look.zipChevronSpacing), 1f);
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        public static void Paint(LineRenderer line, MaterialPropertyBlock block, Color colour, float scroll)
        {
            if (line == null || block == null)
                return;
            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetVector(BaseMapStId, new Vector4(1f, 1f, scroll, 0f));
            line.SetPropertyBlock(block);
        }
    }
}
