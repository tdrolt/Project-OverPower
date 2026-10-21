using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>Colours and shapes the primitive parts of the ability visuals without touching their shared materials:
    /// a MaterialPropertyBlock for meshes, start/end colour for lines. One material asset serves every team.</summary>
    public static class VisualTint
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>A colour for Mesh.colors. Unity converts the colours of a line, trail or particle to linear for the shader, but a mesh's own
        /// vertex colours reach it as they are and the project renders in linear space: hand them over linear, or the colour shows pale.</summary>
        public static Color VertexColour(Color color) => color.linear;

        public static void SetMeshColor(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            if (renderer == null || block == null)
                return;
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(block);
        }

        public static void SetLineColor(LineRenderer line, Color color)
        {
            if (line == null)
                return;
            line.startColor = color;
            line.endColor = color;
        }

        /// <summary>A closed circle lying flat. The line's own transform is turned 90 degrees on X (its Z faces up, and
        /// LineAlignment.TransformZ lays the band flat), which makes its local XY the floor.</summary>
        public static void FillFlatCircle(LineRenderer line, float radius, int segments)
        {
            if (line == null)
                return;
            int count = Mathf.Max(3, segments);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = AbilityVisualGeometry.CirclePoint(radius, i * 360f / count);
                line.SetPosition(i, new Vector3(p.x, p.z, 0f));
            }
        }

        /// <summary>A closed circle in the line's own local XZ plane, at the line's own height - a cage bar.</summary>
        public static void FillStandingCircle(LineRenderer line, float radius, int segments)
        {
            if (line == null)
                return;
            int count = Mathf.Max(3, segments);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
                line.SetPosition(i, AbilityVisualGeometry.CirclePoint(radius, i * 360f / count));
        }
    }
}
