using NUnit.Framework;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The real player prefab still lets the fog hide an enemy's whole plate: the canvas EnemyVisibility switches off is the one
    /// the bar sits on (and the plate is built on), and nothing over a player's head is legacy, stretched-bitmap UI text.</summary>
    public class NameplatePrefabTests
    {
        private const string PrefabPath = "Assets/_Project/Resources/Multiplayer Player.prefab";

        private static GameObject Prefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath + " not found");
            return prefab;
        }

        [Test]
        public void TheCanvasTheFogHidesIsTheOneTheBarSitsOn()
        {
            GameObject prefab = Prefab();
            Assert.IsNotNull(prefab.GetComponent<EnemyVisibility>(), "the fog rule must stay on the player");
            PlayerHealth health = prefab.GetComponent<PlayerHealth>();
            Assert.IsNotNull(health);
            Canvas fogCanvas = EnemyVisibility.FindOverheadCanvas(prefab.transform);
            Assert.IsNotNull(fogCanvas);
            Assert.AreSame(fogCanvas, health.OverheadCanvas);
            Assert.IsNotNull(health.OverheadBar);
            Assert.AreSame(fogCanvas, health.OverheadBar.GetComponentInParent<Canvas>(true));
            Assert.AreEqual(1, fogCanvas.GetComponentsInChildren<Canvas>(true).Length, "a nested canvas would draw outside the fog's switch");
        }

        [Test]
        public void NothingOverAPlayersHeadIsLegacyText()
        {
            Assert.AreEqual(0, Prefab().GetComponentsInChildren<UnityEngine.UI.Text>(true).Length,
                "the name is TextMeshPro, built with the plate; legacy Text draws a blurry bitmap at this scale");
        }
    }
}
