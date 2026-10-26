using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The master volume rule behind the Escape pop-up's slider: what it keeps, what it loads, and that the slider goes through it.</summary>
    public class MasterVolumeTests
    {
        [Test]
        public void Clamps01()
        {
            Assert.AreEqual(0f, MasterVolume.Clamp(-0.5f));
            Assert.AreEqual(1f, MasterVolume.Clamp(3f));
            Assert.AreEqual(0.4f, MasterVolume.Clamp(0.4f), 1e-6f);
            Assert.AreEqual(1f, MasterVolume.Clamp(float.NaN), "a broken saved value plays at full volume, never silent");
        }

        [Test]
        public void LoadsSavedOrFull()
        {
            Assert.AreEqual(1f, MasterVolume.Load((key, fallback) => fallback), "nothing saved: full volume");
            var saved = new Dictionary<string, float> { { MasterVolume.PrefsKey, 0.3f } };
            Assert.AreEqual(0.3f, MasterVolume.Load((key, fallback) => saved.TryGetValue(key, out float v) ? v : fallback), 1e-6f);
            saved[MasterVolume.PrefsKey] = 7f;
            Assert.AreEqual(1f, MasterVolume.Load((key, fallback) => saved.TryGetValue(key, out float v) ? v : fallback), "a saved value out of range is clamped");
        }

        [Test]
        public void PercentReadsWholeNumbers()
        {
            Assert.AreEqual("35%", MasterVolume.PercentText("{0}%", 0.354f));
            Assert.AreEqual("100%", MasterVolume.PercentText("{0}%", 1f));
        }

        [Test]
        public void SliderAppliesThroughRule()
        {
            const BindingFlags Handler = BindingFlags.Instance | BindingFlags.NonPublic;
            var missing = new List<string>();
            Check(missing, IlWiring.Uses(typeof(QuitConfirmPanel), "OnVolumeChanged", typeof(MasterVolume).GetMethod(nameof(MasterVolume.Apply))), "moving the slider sets the volume");
            Check(missing, IlWiring.Uses(typeof(QuitConfirmPanel), "OnVolumeReleased", typeof(MasterVolume).GetMethod(nameof(MasterVolume.Save))), "letting go remembers it");
            Check(missing, IlWiring.Uses(typeof(MasterVolume), "ApplySaved", typeof(MasterVolume).GetMethod(nameof(MasterVolume.Load))), "every start loads the saved value");

            Check(missing, IlWiring.HandsOver(typeof(QuitConfirmPanel), "BuildUi", typeof(QuitConfirmPanel).GetMethod("OnVolumeChanged", Handler)), "the slider is connected to the volume");
            Check(missing, IlWiring.HandsOver(typeof(QuitConfirmPanel), "BuildUi", typeof(QuitConfirmPanel).GetMethod("OnVolumeReleased", Handler)), "letting go of the slider is connected to the save");
            Check(missing, IlWiring.Stores(typeof(QuitConfirmPanel), "BuildUi", typeof(PointerUpRelay).GetField(nameof(PointerUpRelay.Released))), "the release is relayed");
            Assert.IsEmpty(missing);
        }

        [Test]
        public void TheSavedVolumeIsAppliedAtStart()
        {
            MethodInfo applySaved = typeof(MasterVolume).GetMethod("ApplySaved", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(applySaved);
            var hook = applySaved.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.NotNull(hook, "nothing else calls ApplySaved: without the hook the saved volume is ignored on every launch");
            Assert.AreEqual(RuntimeInitializeLoadType.BeforeSceneLoad, hook.loadType, "applied before the first scene plays a sound");
        }

        private static void Check(List<string> missing, bool wired, string what)
        {
            if (!wired) missing.Add(what);
        }
    }
}
