using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// A mech flashes white for a moment when damage lands on it, on every copy that runs the damage (PlayerHealth.ApplyDamage asks
    /// HitFlashRule and calls Flash). The whole body is lit through a property block that swaps in a plain white glow map and a white glow colour over the
    /// team material, so no material is copied and the team colour (PlayerTeamAppearance swaps sharedMaterial) is never touched. Nothing
    /// else sets a property block on the mech's renderers, so clearing the block hands each part back its own look. Sleeps (disabled)
    /// between hits. Length, brightness and the damage-over-time gap: Arena Look's Hit Flash fields; the white: UiTheme's Flash White.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitFlashView : MonoBehaviour
    {
        // URP Lit multiplies its glow colour by the glow map; the mech's map lights only its glow lines, hence the white one while flashing.
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private PlayerHealth health;
        private UiTheme theme;
        private ArenaLook look;
        private SkinnedMeshRenderer[] parts;
        private MaterialPropertyBlock block;
        private float sinceHit = float.PositiveInfinity;
        private float startedAt = float.NegativeInfinity; // kept while asleep: the gap runs from it, not from the flash's end
        private float strength;

        public bool Flashing => strength > 0f;
        public float CurrentStrength => strength;

        /// <summary>Adds the flash to <paramref name="body"/> over every mech part under it (hidden ones too: the mesh is hidden while
        /// dead), or returns null (with a warning) when the theme has no Arena Look.</summary>
        public static HitFlashView TryCreate(GameObject body, PlayerHealth health, UiTheme theme)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (body == null || look == null)
            {
                Debug.LogWarning($"[HitFlashView] {(body != null ? body.name : "?")}: no UiTheme Arena Look - this player shows no hit flash.");
                return null;
            }

            HitFlashView view = body.GetComponent<HitFlashView>();
            if (view == null)
                view = body.AddComponent<HitFlashView>();
            view.health = health;
            view.theme = theme;
            view.look = look;
            view.parts = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            view.block = new MaterialPropertyBlock();
            view.block.SetTexture(EmissionMapId, Texture2D.whiteTexture);
            view.enabled = false;
            return view;
        }

        /// <summary>Damage landed at <paramref name="now"/> (Time.time): the flash starts over at full when the rule lets it (a hit always,
        /// damage over time once its gap is up).</summary>
        public void Flash(DamageSource source, float now)
        {
            if (!HitFlashRule.StartsFlash(source, now - startedAt, look.overTimeFlashGapSeconds))
                return;
            startedAt = now;
            sinceHit = 0f;
            enabled = true;
        }

        private void LateUpdate() => Step(health != null && health.IsAlive, Time.deltaTime);

        /// <summary>One frame: whether the player is alive now and the frame's length.</summary>
        public void Step(bool victimAlive, float deltaTime)
        {
            float now = HitFlashRule.Strength(sinceHit, look.hitFlashSeconds);
            sinceHit += deltaTime;
            if (HitFlashRule.Shows(victimAlive, now))
            {
                strength = now;
                Light(now);
                return;
            }

            strength = 0f;
            sinceHit = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer part in parts)
                if (part != null)
                    part.SetPropertyBlock(null);
            enabled = false;
        }

        private void Light(float amount)
        {
            Color white = theme.flashWhiteColor;
            float k = look.hitFlashGlow * amount;
            block.SetColor(EmissionColorId, new Color(white.r * k, white.g * k, white.b * k, 1f));
            foreach (SkinnedMeshRenderer part in parts)
                if (part != null)
                    part.SetPropertyBlock(block);
        }
    }
}
