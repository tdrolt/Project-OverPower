using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// A map's own minimap picture. The minimap and the team sight live on the player prefab, which names the triangle arena's MinimapConfig; a scene that
    /// plays on another map (the Dominion 2v2 lane) carries one of these with its own config, and the player's copy picks it up in Awake. A scene without
    /// one keeps the prefab's config, so Game Scene is unchanged.
    /// </summary>
    public sealed class SceneMinimapConfig : MonoBehaviour
    {
        [SerializeField, Tooltip("This scene's minimap picture and the patch of the world it shows. Replaces the one the player prefab names.")]
        private MinimapConfig config;

        public MinimapConfig Config => config;

        /// <summary>The scene's config when it has one, else the fallback (the prefab's).</summary>
        public static MinimapConfig Choose(MinimapConfig fromScene, MinimapConfig fallback) => fromScene != null ? fromScene : fallback;

        /// <summary>What the player's minimap and sight should use: this scene's config if a SceneMinimapConfig with one is in the scene.</summary>
        public static MinimapConfig Resolve(MinimapConfig fallback)
        {
            var found = FindFirstObjectByType<SceneMinimapConfig>();
            return Choose(found != null ? found.config : null, fallback);
        }
    }
}
