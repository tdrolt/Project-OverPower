using System.Collections.Generic;
using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Raybeam's three hit lines drawn as ONE beam: a hard-edged sheath in the Raybeam's row of the look table spanning the outer lines'
    /// outer edges (each line is Beam Width wide, what it hits), one white-hot core straight down the middle hit line, a share of ONE line's width,
    /// that glows past white through a property block (the vertex colour clamps at 1), and one thin outline just outside the sheath in the
    /// caster's team colour. The core follows the middle line, not the sheath's middle, so it never jogs where an outer line stops.
    /// All three lie flat in the floor's plane: the outline is offset in that plane, and a camera-facing width would not match it under the
    /// tilted game camera. The shared line material is never copied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaybeamBeamView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f); // with TransformZ the width lies square to the line, in the floor's plane
        private static MaterialPropertyBlock block;
        private readonly List<Vector3> centres = new List<Vector3>(8);
        private readonly List<float> widths = new List<float>(8);
        private readonly List<Vector3> corePoints = new List<Vector3>(2);
        private readonly List<float> coreWidths = new List<float>(2);
        private readonly List<Vector3> outline = new List<Vector3>(16);

        [SerializeField, Tooltip("Where the colour (the look table's row for the ability below), Flash White and the beam numbers come " +
                 "from: Assets/_Project/Shared/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this beam: the Raybeam ability's id.")]
        private int abilityId = 24;

        [SerializeField, Tooltip("The red outer beam, as wide as the hit lines it covers.")]
        private LineRenderer sheath;

        [SerializeField, Tooltip("The white-hot middle of the beam, drawn over the sheath.")]
        private LineRenderer core;

        [SerializeField, Tooltip("The thin outline in the caster's team colour drawn just outside the sheath: it says whose beam it is.")]
        private LineRenderer edge;

        /// <summary>Draws the lines from <paramref name="lineFrom"/> to <paramref name="lineTo"/> (each Beam Width wide) as one beam laid along
        /// <paramref name="aim"/> from <paramref name="origin"/>.</summary>
        public void Draw(Vector3 origin, Vector3 aim, Vector3[] lineFrom, Vector3[] lineTo, float beamWidth, int casterTeam)
        {
            RaybeamLook.MergedSpan(origin, aim, lineFrom, lineTo, beamWidth, centres, widths);
            ArenaLook look = theme != null ? theme.arenaLook : null;
            Color danger = theme != null ? LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.Danger) : Color.red;
            Color white = theme != null ? theme.flashWhiteColor : Color.white;
            Paint(sheath, centres, widths, VisualTint.WithAlpha(danger, look != null ? look.raybeamSheathAlpha : 1f), 1f);
            float coreWidth = RaybeamLook.CoreWidth(beamWidth, look != null ? look.raybeamCoreShare : 0f);
            int middle = Mathf.Min(lineFrom.Length, lineTo.Length) / 2;
            corePoints.Clear();
            coreWidths.Clear();
            if (lineFrom.Length > 0 && lineTo.Length > 0)
            {
                corePoints.Add(lineFrom[middle]);
                corePoints.Add(lineTo[middle]);
                coreWidths.Add(coreWidth);
                coreWidths.Add(coreWidth);
            }
            Paint(core, corePoints, coreWidths, white, look != null ? look.raybeamCoreGlow : 1f);
            if (core != null && sheath != null)
                core.sortingOrder = sheath.sortingOrder + 1; // two see-through lines on one spot: sorting by distance alone flips them
            if (edge != null && look != null)
                PaintEdge(aim, look, LookColours.Accent(casterTeam, theme, danger));
        }

        // A closed outline just outside the sheath, not a wider line under it: the sheath is see-through, so an underlay would tint the beam's
        // whole body with the team colour.
        private void PaintEdge(Vector3 aim, ArenaLook look, Color accent)
        {
            RaybeamLook.MergedOutline(centres, widths, aim, look.raybeamEdgeWidth, look.raybeamEdgeGap, outline);
            LayFlat(edge);
            edge.loop = true;
            edge.positionCount = outline.Count;
            for (int k = 0; k < outline.Count; k++)
                edge.SetPosition(k, outline[k]);
            edge.widthMultiplier = 1f;
            edge.startWidth = edge.endWidth = look.raybeamEdgeWidth;
            VisualTint.SetLineColor(edge, VisualTint.WithAlpha(accent, look.raybeamEdgeAlpha));
            if (block == null)
                block = new MaterialPropertyBlock();
            edge.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(look.raybeamEdgeGlow, look.raybeamEdgeGlow, look.raybeamEdgeGlow, 1f));
            edge.SetPropertyBlock(block);
        }

        private static void LayFlat(LineRenderer line)
        {
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.transform.rotation = Flat;
        }

        private static void Paint(LineRenderer line, List<Vector3> points, List<float> lineWidths, Color colour, float glow)
        {
            if (line == null)
                return;
            LayFlat(line);
            line.positionCount = points.Count;
            for (int k = 0; k < points.Count; k++)
                line.SetPosition(k, points[k]);
            line.widthMultiplier = 1f;
            line.widthCurve = RaybeamLook.WidthCurve(points, lineWidths);
            VisualTint.SetLineColor(line, colour);
            if (block == null)
                block = new MaterialPropertyBlock();
            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(glow, glow, glow, 1f));
            line.SetPropertyBlock(block);
        }
    }
}
