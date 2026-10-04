using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 11: the small rules the lane scene needs from the game - a one-piece arena outline, a scene's own minimap picture, no health packs in 2v2.</summary>
    public class DominionLaneRuntimeTests
    {
        private static readonly Vector2[] Rectangle = { new Vector2(-10f, -5f), new Vector2(10f, -5f), new Vector2(10f, 5f), new Vector2(-10f, 5f) };

        [Test] public void AOnePieceOutlineIsUsedAsItIsNotTurnedIntoThreeThirds()
        {
            ArenaBounds bounds = ArenaBounds.FromOutline(Rectangle, new Vector3(50f, 0f, 50f), outlineIsWholeArena: true);
            Assert.AreEqual(4, bounds.Polygon.Count, "three copies would make 12 corners");
            Assert.IsTrue(bounds.Contains(new Vector3(0f, 0f, 0f), 0.5f));
            Assert.IsFalse(bounds.Contains(new Vector3(0f, 0f, 8f), 0f), "outside the rectangle is outside");
        }

        [Test] public void ATriangleArenaOutlineStillTurnsIntoThirds()
        {
            ArenaBounds bounds = ArenaBounds.FromOutline(Rectangle, Vector3.zero, outlineIsWholeArena: false);
            Assert.AreEqual(12, bounds.Polygon.Count);
        }

        [Test] public void ASceneWithItsOwnMinimapPictureUsesItAndOtherScenesKeepThePrefabs()
        {
            var scene = ScriptableObject.CreateInstance<MinimapConfig>();
            var prefab = ScriptableObject.CreateInstance<MinimapConfig>();
            try
            {
                Assert.AreSame(scene, SceneMinimapConfig.Choose(scene, prefab));
                Assert.AreSame(prefab, SceneMinimapConfig.Choose(null, prefab), "Game Scene has none, so nothing changes there");
            }
            finally { Object.DestroyImmediate(scene); Object.DestroyImmediate(prefab); }
        }

        [Test] public void TwoVsTwoDominionHasNoHealthPacksButEveryOtherRoomKeepsThem()
        {
            Assert.IsFalse(DominionRules.HasHealthPacks(dominion: true, teamCount: 2));
            Assert.IsTrue(DominionRules.HasHealthPacks(dominion: true, teamCount: 3), "3v3v3 keeps them");
            Assert.IsTrue(DominionRules.HasHealthPacks(dominion: false, teamCount: 3), "Conquest keeps them");
            Assert.IsTrue(DominionRules.HasHealthPacks(dominion: false, teamCount: 2), "a two-team Conquest lobby keeps them");
            Assert.IsTrue(DominionRules.HasHealthPacks(dominion: false, teamCount: 0), "outside a room nothing changes");
        }

        // ---- the game calls these rules (method bodies are read, not run)

        [Test] public void TheArenaBuildsItsBoundsThroughTheOutlineRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaSymmetry), "OnEnable", typeof(ArenaBounds).GetMethod(nameof(ArenaBounds.FromOutline))));

        [Test] public void TheMinimapAndTheSightAskTheSceneForItsPicture()
        {
            System.Reflection.MethodInfo resolve = typeof(SceneMinimapConfig).GetMethod(nameof(SceneMinimapConfig.Resolve));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.MinimapView), "Awake", resolve));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Vision.TeamSight), "Awake", resolve));
        }

        [Test] public void TheHealthPackManagerAsksTheTestedRuleBeforeBuildingPacks() =>
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Match.HealthPackManager), "EnsurePacks", typeof(DominionRules).GetMethod(nameof(DominionRules.HasHealthPacks))));

        [Test] public void ScenesThatAreNotTheLaneAreNotChangedByTheNewSceneComponent()
        {
            // the config component only does anything when a scene carries one: the lookup returns the fallback for null
            var prefab = ScriptableObject.CreateInstance<MinimapConfig>();
            try { Assert.AreSame(prefab, SceneMinimapConfig.Choose(null, prefab)); }
            finally { Object.DestroyImmediate(prefab); }
        }
    }
}
