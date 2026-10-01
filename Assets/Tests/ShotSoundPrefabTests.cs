using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 7 (Tudor): gun sounds come from where they happen - full volume near you, fading to silent further
    /// out, from that side. AudioManager.Play3D spawns the prefab below, which is the one home for the hearing
    /// distances. Guards the RULE (fully 3D, a fade that actually spans a distance), never Tudor's 15 m / 35 m, which
    /// he retunes on the prefab.
    /// </summary>
    public class ShotSoundPrefabTests
    {
        private const string PrefabPath = "Assets/Prefabs/prefabs/AudioSourcePrefab.prefab";

        private static AudioSource Source()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            var source = prefab.GetComponent<AudioSource>();
            Assert.IsNotNull(source, "the shot-sound prefab has no AudioSource");
            return source;
        }

        [Test]
        public void TheShotSoundSource_IsFullyThreeD()
        {
            Assert.AreEqual(1f, Source().spatialBlend, 1e-5f);
        }

        [Test]
        public void TheShotSoundFade_SpansADistance_AndIsEven()
        {
            AudioSource source = Source();
            Assert.AreEqual(AudioRolloffMode.Linear, source.rolloffMode);
            Assert.Greater(source.minDistance, 0f);
            Assert.Greater(source.maxDistance, source.minDistance);
        }

        [Test]
        public void TheShotSoundSource_DoesNotBendWithMovement()
        {
            Assert.AreEqual(0f, Source().dopplerLevel, 1e-5f);
        }
    }
}
