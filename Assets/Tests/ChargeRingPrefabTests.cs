using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The charge ring (charge step 4) is invisible if its component or its theme goes missing from the player
    /// prefab, and nothing else would fail - so this pins the wiring, not a look value. Read-only: the prefab asset
    /// is loaded, never instantiated or saved.
    /// </summary>
    public class ChargeRingPrefabTests
    {
        private const string PlayerPrefab = "Assets/Resources/Multiplayer Player.prefab";

        private static GameObject Player()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, $"Expected the player prefab at {PlayerPrefab} - if it moved, update this path.");
            return prefab;
        }

        [Test]
        public void ThePlayerCarriesAChargeRingWithATheme()
        {
            var view = Player().GetComponent<ChargeRingView>();
            Assert.IsNotNull(view, "The player prefab has no ChargeRingView - the charge indicator draws nothing.");

            var theme = new SerializedObject(view).FindProperty("theme");
            Assert.IsNotNull(theme, "ChargeRingView.theme was renamed - update this test rather than widening access.");
            Assert.IsNotNull(theme.objectReferenceValue,
                "ChargeRingView.Theme is empty on the player prefab - the ring logs an error and draws nothing.");
        }

        [Test]
        public void TheChargeRingUsesTheSameThemeAsTheAimCone()
        {
            var ring = new SerializedObject(Player().GetComponent<ChargeRingView>()).FindProperty("theme");
            var cone = new SerializedObject(Player().GetComponent<AimConeView>()).FindProperty("theme");
            Assert.AreSame(cone.objectReferenceValue, ring.objectReferenceValue,
                "The aim cone and the charge ring point at different UiTheme assets - one of them is reading numbers nobody edits.");
        }

        [Test]
        public void TheChargeRingIsNotObservedOverTheNetwork()
        {
            // The player's PhotonView uses AutoFindAll and would absorb a second IPunObservable. PlayerNetSync is the
            // only one, and the saved list is what a build actually uses.
            var pv = Player().GetComponent<PhotonView>();
            Assert.IsNotNull(pv);
            Assert.AreEqual(1, pv.ObservedComponents.Count,
                "The player's PhotonView must observe exactly one component (PlayerNetSync).");
            Assert.IsFalse(Player().GetComponent<ChargeRingView>() is IPunObservable,
                "ChargeRingView must never implement IPunObservable.");
        }
    }
}
