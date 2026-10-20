using System.Collections.Generic;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>One runtime copy of the mech template per team, shared by every player of that team. The copies are DontSave and are
    /// never the template or a shared asset, so Play Mode dirties nothing; a domain reload forgets the cache, so the sweep frees any
    /// copy it left behind.</summary>
    public static class MechMaterials
    {
        public const string NamePrefix = "Mech Team ";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Dictionary<int, Material> cache = new Dictionary<int, Material>();

        /// <summary>Null for a team not known yet (-1) or a theme without an Arena Look and its mech template. The copy is re-dressed on
        /// every call, so a retuned mix or glow shows at the next spawn.</summary>
        public static Material For(int team, UiTheme theme, ArenaLook look)
        {
            if (team < 0 || theme == null || look == null || look.mechTemplate == null)
                return null;
            if (!cache.TryGetValue(team, out Material material) || material == null)
            {
                material = new Material(look.mechTemplate) { name = NamePrefix + team, hideFlags = HideFlags.DontSave };
                cache[team] = material;
            }
            Dress(material, LookColours.For(LookFamily.Team, team, theme), look);
            return material;
        }

        internal static void Dress(Material material, Color team, ArenaLook look)
        {
            Color body = MechLook.Base(team, look.mechBaseMix);
            material.SetColor(BaseColorId, body);
            material.SetColor(ColorId, body);
            // URP Lit's _EmissionColor is an HDR property, which the shader reads as it is (a plain colour property is converted to linear): hence .linear.
            material.SetColor(EmissionColorId, MechLook.Glow(team.linear, look.mechGlowStrength));
            material.EnableKeyword("_EMISSION");
        }

        internal static void Free()
        {
            foreach (Material material in cache.Values)
                if (material != null)
                    Object.DestroyImmediate(material);
            cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
            Free();
#if UNITY_EDITOR
            foreach (Material leftover in Resources.FindObjectsOfTypeAll<Material>())
                if (leftover != null && leftover.name.StartsWith(NamePrefix) && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    Object.DestroyImmediate(leftover);
#endif
        }
    }
}
