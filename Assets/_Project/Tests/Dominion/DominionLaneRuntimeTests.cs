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

        // ---- health packs: nothing is built while the room's mode is unknown (Task 11 review, item 2)

        [Test] public void NoHealthPacksAreBuiltWhileTheModeIsStillUnknown()
        {
            Assert.IsFalse(DominionRules.MayBuildHealthPacks(modeKnown: false, dominion: false, teamCount: 0), "the catalogue is not there yet: wait");
            Assert.IsFalse(DominionRules.MayBuildHealthPacks(modeKnown: false, dominion: false, teamCount: 3), "even if the team count looks like Conquest");
        }

        [Test] public void OnceTheModeIsKnownThePacksFollowTheModeRule()
        {
            Assert.IsFalse(DominionRules.MayBuildHealthPacks(modeKnown: true, dominion: true, teamCount: 2), "2v2 Dominion: none");
            Assert.IsTrue(DominionRules.MayBuildHealthPacks(modeKnown: true, dominion: true, teamCount: 3));
            Assert.IsTrue(DominionRules.MayBuildHealthPacks(modeKnown: true, dominion: false, teamCount: 3));
            Assert.IsTrue(DominionRules.MayBuildHealthPacks(modeKnown: true, dominion: false, teamCount: 2));
        }

        [Test] public void ThePackListFollowsTheThreeModeAnswersAndHoldsTheMissingTierThreeZones()
        {
            // zones 0..4: tiers 2,3,3,1,4; zone 2 already has its pack
            System.Func<int, int> tierOf = z => new[] { 2, 3, 3, 1, 4 }[z];
            System.Func<int, bool> has = z => z == 2;
            CollectionAssert.IsEmpty(Overpower.Match.HealthPackRules.ZonesToBuild(false, false, 3, 5, tierOf, has, out bool unknownSettled), "mode unknown: nothing, even for a Conquest-looking room");
            Assert.IsFalse(unknownSettled, "and ask again next frame");
            CollectionAssert.IsEmpty(Overpower.Match.HealthPackRules.ZonesToBuild(true, true, 2, 5, tierOf, has, out bool laneSettled), "2v2 Dominion: none");
            Assert.IsTrue(laneSettled, "and never any, so stop asking");
            CollectionAssert.AreEqual(new[] { 1 }, Overpower.Match.HealthPackRules.ZonesToBuild(true, false, 3, 5, tierOf, has, out bool conquestSettled), "Conquest keeps its packs");
            Assert.IsFalse(conquestSettled, "one is still to build");
            CollectionAssert.AreEqual(new[] { 1, 2 }, Overpower.Match.HealthPackRules.ZonesToBuild(true, true, 3, 5, tierOf, z => false, out _), "3v3v3 Dominion keeps them");
            CollectionAssert.IsEmpty(Overpower.Match.HealthPackRules.ZonesToBuild(true, false, 3, 5, tierOf, z => z == 1 || z == 2, out bool doneSettled));
            Assert.IsTrue(doneSettled, "every pack built and every tower registered");
            CollectionAssert.IsEmpty(Overpower.Match.HealthPackRules.ZonesToBuild(true, false, 3, 5, z => z == 4 ? 0 : tierOf(z), z => z == 1 || z == 2, out bool waitingSettled));
            Assert.IsFalse(waitingSettled, "a tower that has not registered yet may still turn out to be a pack zone");
        }

        [Test] public void TheHealthPackManagerBuildsOnlyTheZonesTheRuleNamesAndStopsAskingOnceSettled()
        {
            System.Type manager = typeof(Overpower.Match.HealthPackManager);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.FieldInfo settled = manager.GetField("packsSettled", flags);
            Assert.IsNotNull(settled);
            Assert.IsTrue(IlWiring.Uses(manager, "EnsurePacks", typeof(Overpower.Match.HealthPackRules).GetMethod(nameof(Overpower.Match.HealthPackRules.ZonesToBuild))));
            Assert.IsTrue(IlWiring.Uses(manager, "EnsurePacks", typeof(DominionMode).GetMethod(nameof(DominionMode.IsKnown))), "the mode check is asked, not assumed");
            Assert.IsTrue(IlWiring.Uses(manager, "EnsurePacks", typeof(DominionMode).GetMethod(nameof(DominionMode.IsActive))));
            Assert.IsTrue(IlWiring.Uses(manager, "EnsurePacks", typeof(DominionMode).GetMethod(nameof(DominionMode.TeamCountOfCurrentRoom))));
            Assert.IsTrue(IlWiring.Uses(manager, "EnsurePacks", settled), "settled is read first, before anything is allocated");
            Assert.IsTrue(IlWiring.Stores(manager, "EnsurePacks", settled), "and remembered");
            Assert.IsTrue(IlWiring.Stores(manager, "OnJoinedRoom", settled), "a new room is asked again");
        }
    }
}
