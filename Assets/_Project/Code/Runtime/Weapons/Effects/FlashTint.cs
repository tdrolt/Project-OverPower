using Overpower.UI;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// Paints a gun's flash (an impact, the muzzle flash, the ricochet chevron) in the theme's Flash White times the Arena Look's Gun
    /// Flash Glow, through a property block so the shared FX material never changes. Above 1 the glow is what lets Bloom halo a hit past
    /// the arena's own stripes. Applied once per copy as VFXManager makes it; the pool reuses the copy, so nothing is set per flash.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlashTint : MonoBehaviour
    {
        [SerializeField, Tooltip("Where the flash's white (Flash White) and how much brighter it is drawn (its Arena Look's Gun Flash Glow) " +
                 "come from: Assets/Gameplay/Config/UiTheme.asset.")]
        private UiTheme theme;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static Color ColourFor(Color white, float glow) => new Color(white.r * glow, white.g * glow, white.b * glow, white.a);

        private void Awake() => Apply();

        /// <summary>False, leaving the authored white, when there is no theme.</summary>
        public bool Apply()
        {
            if (theme == null)
                return false;
            float glow = theme.arenaLook != null ? theme.arenaLook.gunFlashGlow : 1f;
            Color colour = ColourFor(theme.flashWhiteColor, glow);
            var block = new MaterialPropertyBlock();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                r.SetPropertyBlock(block);
            }
            return true;
        }
    }
}
