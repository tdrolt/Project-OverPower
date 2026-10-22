using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// One Raybeam line: a hard-edged sheath in the Raybeam's row of the look table, exactly Beam Width wide (what the beam hits), a
    /// white-hot core inside it that glows past white through a property block (the vertex colour clamps at 1), and a thin outline just
    /// outside the sheath in the caster's team colour that says whose beam it is. The shared line material is never copied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaybeamBeamView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock block;
        private Vector3[] corners;

        [SerializeField, Tooltip("Where the colour (the look table's row for the ability below), Flash White and the beam numbers come " +
                 "from: Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this beam: the Raybeam ability's id.")]
        private int abilityId = 24;

        [SerializeField, Tooltip("The red outer beam, as wide as what the beam hits.")]
        private LineRenderer sheath;

        [SerializeField, Tooltip("The white-hot middle of the beam, drawn over the sheath.")]
        private LineRenderer core;

        [SerializeField, Tooltip("The thin outline in the caster's team colour drawn just outside the sheath: it says whose beam it is.")]
        private LineRenderer edge;

        public void Draw(Vector3 from, Vector3 to, float beamWidth, int casterTeam)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            Color danger = theme != null ? LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.Danger) : Color.red;
            Color white = theme != null ? theme.flashWhiteColor : Color.white;
            Paint(sheath, from, to, beamWidth, VisualTint.WithAlpha(danger, look != null ? look.raybeamSheathAlpha : 1f), 1f);
            Paint(core, from, to, RaybeamLook.CoreWidth(beamWidth, look != null ? look.raybeamCoreShare : 0f), white,
                  look != null ? look.raybeamCoreGlow : 1f);
            if (core != null && sheath != null)
                core.sortingOrder = sheath.sortingOrder + 1; // two see-through lines on one spot: sorting by distance alone flips them
            if (edge != null && look != null)
                PaintEdge(from, to, beamWidth, look, LookColours.Accent(casterTeam, theme, danger));
        }

        // A closed outline just outside the sheath, not a wider line under it: the sheath is see-through, so an underlay would tint the beam's
        // whole body with the team colour.
        private void PaintEdge(Vector3 from, Vector3 to, float beamWidth, ArenaLook look, Color accent)
        {
            corners ??= new Vector3[4];
            RaybeamLook.EdgeOutline(from, to, beamWidth, look.raybeamEdgeWidth, look.raybeamEdgeGap, corners);
            edge.useWorldSpace = true;
            edge.loop = true;
            edge.positionCount = corners.Length;
            edge.SetPositions(corners);
            edge.startWidth = edge.endWidth = look.raybeamEdgeWidth;
            VisualTint.SetLineColor(edge, VisualTint.WithAlpha(accent, look.raybeamEdgeAlpha));
            if (block == null)
                block = new MaterialPropertyBlock();
            edge.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(look.raybeamEdgeGlow, look.raybeamEdgeGlow, look.raybeamEdgeGlow, 1f));
            edge.SetPropertyBlock(block);
        }

        private static void Paint(LineRenderer line, Vector3 from, Vector3 to, float width, Color colour, float glow)
        {
            if (line == null)
                return;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = line.endWidth = width;
            VisualTint.SetLineColor(line, colour);
            if (block == null)
                block = new MaterialPropertyBlock();
            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(glow, glow, glow, 1f));
            line.SetPropertyBlock(block);
        }
    }
}
