using System;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The master volume (the Escape pop-up's slider): AudioListener.volume, which every AudioSource in the game plays through (gunfire, impacts,
    /// towers, music; none ignores the listener volume). Remembered in PlayerPrefs, written only when the slider is let go (each write hits the
    /// registry on Windows), and applied before the first scene loads. PlayerPrefs are shared by every copy of the game on one PC, Dev1-4 included.
    /// </summary>
    public static class MasterVolume
    {
        public const string PrefsKey = "masterVolume";
        public const float Full = 1f;

        /// <summary>0..1; a broken value (NaN) plays at full volume rather than silently.</summary>
        public static float Clamp(float volume) => float.IsNaN(volume) ? Full : Mathf.Clamp01(volume);

        /// <summary>The saved volume, or full volume when nothing is saved. get is PlayerPrefs.GetFloat in the game.</summary>
        public static float Load(Func<string, float, float> get) => Clamp(get(PrefsKey, Full));

        public static string PercentText(string format, float volume)
        {
            int percent = Mathf.RoundToInt(Clamp(volume) * 100f);
            try { return string.Format(format ?? "", percent); }
            catch (FormatException) { return percent + "%"; }
        }

        public static void Apply(float volume) => AudioListener.volume = Clamp(volume);

        public static void Save(float volume)
        {
            PlayerPrefs.SetFloat(PrefsKey, Clamp(volume));
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySaved() => Apply(Load(PlayerPrefs.GetFloat));
    }
}
