using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// One Raybeam line: a hard-edged sheath in the Raybeam's row of the look table, exactly Beam Width wide (what the beam hits), and a
    /// white-hot core inside it that glows past white through a property block (the vertex colour clamps at 1). The shared line material
    /// is never copied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaybeamBeamView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock block;

        [SerializeField, Tooltip("Where the colour (the look table's row for the ability below), Flash White and the beam numbers come " +
                 "from: Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this beam: the Raybeam ability's id.")]
        private int abilityId = 24;

        [SerializeField, Tooltip("The red outer beam, as wide as what the beam hits.")]
        private LineRenderer sheath;

        [SerializeField, Tooltip("The white-hot middle of the beam, drawn over the sheath.")]
        private LineRenderer core;

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
