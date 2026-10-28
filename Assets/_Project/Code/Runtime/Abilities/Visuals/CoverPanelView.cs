using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A placed cover wall wears the arena's wall material (dark, glossy, circuit stripes in the colour of the ground it stands on) with
    /// a glowing band along every edge in its owner's colour, so you see whose cover it is. The band goes through a property block: the
    /// wall material stays shared and the static arena, which never has one, draws exactly as before. Visual only; the collider is CoverWall's.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoverPanelView : MonoBehaviour, IDeployableView
    {
        private static readonly int PanelEdgeId = Shader.PropertyToID(CoverPanelLook.PropertyName);

        [SerializeField, Tooltip("Team colours and the Arena Look - Assets/_Project/Design/Settings/UiTheme.asset.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Which row of the Arena Look table colours this cover: the Deployable Cover ability's id.")]
        private int abilityId = 20;

        [SerializeField, Tooltip("The wall's visible box (the Visual child), on the arena's wall material.")]
        private Renderer panel;

        public void OnDeployablePlaced(NetworkedDeployable deployable)
        {
            if (panel == null || theme == null || theme.arenaLook == null)
                return;
            ArenaLook look = theme.arenaLook;
            Color team = LookColours.ForAbility(theme, abilityId, deployable.OwnerTeam, LookFamily.Team);
            var block = new MaterialPropertyBlock();
            panel.GetPropertyBlock(block);
            block.SetVector(PanelEdgeId, CoverPanelLook.Edge(team, look.coverEdgeGlow, look.coverEdgeWidth));
            panel.SetPropertyBlock(block);
        }
    }
}
