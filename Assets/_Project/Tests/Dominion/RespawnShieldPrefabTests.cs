using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Dominion;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 7: the player prefab carries the respawn shield and it can read the Invulnerability ultimate's size (structure only,
    /// never the numbers a designer tunes).</summary>
    public class RespawnShieldPrefabTests
    {
        [Test] public void ThePlayerPrefabCarriesTheRespawnShieldWiredToTheInvulnerabilityPrefab()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Multiplayer Player.prefab");
            Assert.IsNotNull(player);
            RespawnShield shield = player.GetComponent<RespawnShield>();
            Assert.IsNotNull(shield, "the player prefab needs the RespawnShield");
            var so = new SerializedObject(shield);
            var reference = so.FindProperty("invulnerabilityReference").objectReferenceValue as InvulnerabilityAbility;
            Assert.IsNotNull(reference, "the bubble's size is read from the Invulnerability ability, never copied");
            Assert.Greater(reference.ShieldDiameter, 0f);
        }
    }
}
