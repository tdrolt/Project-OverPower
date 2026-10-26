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
        public void TheShotSoundSource_IsCentredWithinACoupleOfMetres_AndFullyThreeDFromAFewMetresOut()
        {
            // Your own muzzle sits well under a metre to your side; fully 3D there would put it almost wholly in one
            // ear, so the sound stays centred within a couple of metres and only then swings out to its side. The
            // curve's time axis is the fraction of the source's max distance.
            AudioSource source = Source();
            AnimationCurve blend = source.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
            Assert.AreEqual(0f, blend.Evaluate(0f), 1e-3f, "at the shooter's own spot the sound should be centred");
            Assert.AreEqual(0f, blend.Evaluate(2f / source.maxDistance), 1e-3f, "still centred a couple of metres out");
            Assert.AreEqual(1f, blend.Evaluate(4f / source.maxDistance), 1e-3f, "fully 3D from a few metres out");
            Assert.AreEqual(1f, blend.Evaluate(1f), 1e-3f, "fully 3D far out");
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
