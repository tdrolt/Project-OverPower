using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>When a corner closes, one health pack appears in the recess the new wall makes: on the cut's axis, midway between the
    /// barrier across the mouth and the back wall, inside play and clear of every wall, the same spot turned for each corner. Every client
    /// derives the spot and the pack's index from the cut alone, so they agree with no message. Literal values: the toy triangle and the
    /// real outline with the numbers the design started from (PhaseTwoCutGeometryTests), never the layout asset's tuning.</summary>
    public class RecessHealthPackTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int Zones = 10;
        private const float PickupReach = 1.2f; // the pad's corners sit on it, so the whole pad must fit
        private static readonly Vector3 BarrierSize = new Vector3(10.18f, 1f, 0.6f);
        private static readonly Vector3 PlankSize = new Vector3(2.609f, 5.729f, 7.181f);
        private const float PlankSpacing = 6.18f, PlankInFront = 3f;

        private static readonly Vector2 ToyCentre = new Vector2(10f, 20f);
        private static readonly List<Vector2> Toy = new List<Vector2>
        {
            new Vector2(10f, 30f), new Vector2(10f - 8.660254f, 15f), new Vector2(10f + 8.660254f, 15f),
        };

        private static Vector2 ArenaCentre => new Vector2(PhaseTwoCutGeometryTests.ArenaCentre.x, PhaseTwoCutGeometryTests.ArenaCentre.z);

        // Metres from a point to a box's footprint (centre, length along the wall, depth across it), 0 inside.
        private static float DistanceToFootprint(Vector2 point, Vector2 centre, Vector2 alongWall, Vector3 size)
        {
            Vector2 d = point - centre;
            Vector2 across = new Vector2(-alongWall.y, alongWall.x);
            float x = Mathf.Max(Mathf.Abs(Vector2.Dot(d, alongWall)) - size.x * 0.5f, 0f);
            float z = Mathf.Max(Mathf.Abs(Vector2.Dot(d, across)) - size.z * 0.5f, 0f);
            return Mathf.Sqrt(x * x + z * z);
        }

        [Test]
        public void ThePackStandsMidwayBetweenTheBarrierAndTheBackOfTheRecess()
        {
            // Cut 2 m up from (10, 20) with a 2 x 1 m recess: the mouth (and its barrier) at y 22, the back at y 23.
            PhaseTwoCutGeometry cut = PhaseTwoCutGeometry.Build(Toy, ToyCentre, Vector2.up, 2f, 2f, 1f, 0.2f);
            Assert.AreEqual(10f, cut.RecessCentre.x, 1e-4f, "on the cut's axis");
            Assert.AreEqual(22.5f, cut.RecessCentre.y, 1e-4f, "midway between the barrier and the back wall");
        }

        [Test]
        public void EachCornersPackIsInPlayOnTheFloorAndClearOfEveryWall([Values(0, 1, 2)] int team)
        {
            PhaseTwoCutGeometry cut = PhaseTwoCutGeometryTests.RealCut(team);
            Vector2 pack = cut.RecessCentre;
            Assert.IsFalse(cut.IsBehindWall(new Vector3(pack.x, 0f, pack.y)), "in play, on the open side of the wall");
            Assert.GreaterOrEqual(cut.Playable.SignedDistance(pack), PickupReach,
                                  "the whole pad is on the playable floor, clear of the recess's back and sides and every outer wall");
            Assert.GreaterOrEqual(DistanceToFootprint(pack, cut.BarrierCentre, cut.AlongWall, BarrierSize), PickupReach,
                                  "clear of the barrier across the mouth");
            (Vector2 first, Vector2 second) = cut.PlankCentres(PlankSpacing, PlankInFront);
            Assert.GreaterOrEqual(DistanceToFootprint(pack, first, cut.AlongWall, PlankSize), PickupReach, "clear of one plank");
            Assert.GreaterOrEqual(DistanceToFootprint(pack, second, cut.AlongWall, PlankSize), PickupReach, "and of the other");
            Vector2 fromCentre = pack - ArenaCentre;
            Assert.AreEqual(0f, fromCentre.x * cut.TowardClosed.y - fromCentre.y * cut.TowardClosed.x, 1e-3f,
                            "on the line from the centre toward the closed capital, the recess's own axis");
        }

        [Test]
        public void TheThreeCornersPacksAreOneSpotTurnedAboutTheCentre()
        {
            Vector2[] spot = new Vector2[3];
            for (int team = 0; team < 3; team++)
                spot[team] = PhaseTwoCutGeometryTests.RealCut(team).RecessCentre - ArenaCentre;
            Assert.Greater(spot[0].magnitude, 1f, "the pack stands away from the centre tower");
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                {
                    Assert.AreEqual(spot[a].magnitude, spot[b].magnitude, 1e-3f, $"corners {a} and {b}: as far from the centre");
                    Assert.AreEqual(120f, Mathf.Abs(Vector2.SignedAngle(spot[a], spot[b])), 0.05f, $"corners {a} and {b}: a third of a turn apart");
                }
        }

        [Test]
        public void NeitherTheOutlinesWindingNorItsFirstCornerMovesTheSpot()
        {
            Vector2 spot = PhaseTwoCutGeometry.Build(Toy, ToyCentre, Vector2.up, 2f, 2f, 1f, 0.2f).RecessCentre;
            List<Vector2> reversed = new List<Vector2>(Toy);
            reversed.Reverse();
            List<Vector2> turned = new List<Vector2> { Toy[1], Toy[2], Toy[0] };
            foreach ((List<Vector2> outline, string what) in new[] { (reversed, "the other winding"), (turned, "another first corner") })
            {
                Vector2 other = PhaseTwoCutGeometry.Build(outline, ToyCentre, Vector2.up, 2f, 2f, 1f, 0.2f).RecessCentre;
                Assert.AreEqual(spot.x, other.x, 1e-4f, what);
                Assert.AreEqual(spot.y, other.y, 1e-4f, what);
            }
        }

        [Test]
        public void APackIsInPlayWithItsZoneButTheRecessPackWhileItStands()
        {
            Assert.IsTrue(HealthPackRules.PackInPlay(false, false), "a zone's pack while its zone is in play");
            Assert.IsFalse(HealthPackRules.PackInPlay(false, true), "a zone's pack behind the cut wall: hidden, and nobody takes it");
            Assert.IsTrue(HealthPackRules.PackInPlay(true, false), "the recess pack");
            Assert.IsTrue(HealthPackRules.PackInPlay(true, true), "the recess pack, whatever its index would say as a zone");
        }

        [Test]
        public void ThePackComesOnlyWithAStandingCutAndNeverInDominion()
        {
            const int none = PhaseTwoCutRules.NoCut;
            Assert.AreEqual(-1, HealthPackRules.RecessPackIndex(false, none, none, false, Zones), "no cut, no pack");
            Assert.AreEqual(12, HealthPackRules.RecessPackIndex(false, 2, 2, true, Zones), "team 2's corner closed and its wall stands");
            Assert.AreEqual(-1, HealthPackRules.RecessPackIndex(true, 2, 2, true, Zones), "never in Dominion");
            Assert.AreEqual(-1, HealthPackRules.RecessPackIndex(false, 2, none, false, Zones), "the cut is known but this client's wall is not built yet");
            Assert.AreEqual(-1, HealthPackRules.RecessPackIndex(false, 2, 1, true, Zones), "the wall standing is another corner's");
            Assert.AreEqual(-1, HealthPackRules.RecessPackIndex(false, 2, 2, false, Zones), "a straight wall makes no recess");
            Assert.AreEqual(-1, HealthPackRules.RecessPackIndex(false, 2, 2, true, 0), "the zones are not known yet");
        }

        [Test]
        public void EachCornersPackHasItsOwnIndexAfterTheZonesPacks()
        {
            var seen = new HashSet<int>();
            for (int team = 0; team < 3; team++)
            {
                int index = HealthPackRules.RecessPackIndex(false, team, team, true, Zones);
                Assert.GreaterOrEqual(index, Zones, $"corner {team}: after every zone's pack, so it never shares a zone's room key");
                Assert.IsTrue(seen.Add(index), $"corner {team}: one pack per corner");
                for (int zone = 0; zone < Zones; zone++)
                    Assert.AreNotEqual(HealthPackManager.PackKey(zone), HealthPackManager.PackKey(index));
            }
        }

        // ---- the manager

        private GameObject host;
        private HealthPackConfig config;
        private UiTheme theme;
        private ArenaLook look;
        private Material fx;

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            look = ScriptableObject.CreateInstance<ArenaLook>();
            fx = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            look.fxAlpha = fx;
            theme.arenaLook = look;
            config = ScriptableObject.CreateInstance<HealthPackConfig>();
            var so = new SerializedObject(config);
            so.FindProperty("pickupRadius").floatValue = PickupReach;
            so.ApplyModifiedPropertiesWithoutUndo();
            host = new GameObject("RecessHealthPackTests") { hideFlags = HideFlags.HideAndDontSave };
        }

        [TearDown]
        public void TearDown()
        {
            HealthPackManager manager = host.GetComponent<HealthPackManager>();
            if (manager != null && typeof(HealthPackManager).GetField("solidMaterial", Any).GetValue(manager) is Material solid)
                Object.DestroyImmediate(solid);
            Object.DestroyImmediate(host);
            typeof(HealthPackView).GetMethod("FreeLeftovers", Any).Invoke(null, null); // edit mode runs no OnDestroy
            Object.DestroyImmediate(fx);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private HealthPackManager MakeManager()
        {
            HealthPackManager manager = host.AddComponent<HealthPackManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("theme").objectReferenceValue = theme;
            so.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        [Test]
        public void TheManagerBuildsTheRecessPackWhereTheCutSaysAndTakesItAwayWithTheCut()
        {
            HealthPackManager manager = MakeManager();
            manager.SetRecessPack(-1, Vector2.zero);
            Assert.AreEqual(-1, manager.RecessPackIndex);
            Assert.AreEqual(0, host.GetComponentsInChildren<HealthPackView>(true).Length, "no cut: no recess pack");

            manager.SetRecessPack(12, new Vector2(65.05f, 61.3f));
            Assert.AreEqual(12, manager.RecessPackIndex);
            Assert.IsTrue(manager.HasPack(12), "the pack is asked for and decided under its own index, like a zone's");
            Assert.IsTrue(manager.TryGetPack(12, out Vector3 at, out _), "drawn, so the minimap shows it");
            Assert.AreEqual(65.05f, at.x, 1e-4f);
            Assert.AreEqual(61.3f, at.z, 1e-4f);
            Assert.AreEqual(1, host.GetComponentsInChildren<HealthPackView>(true).Length, "one pack, drawn like every other");

            manager.SetRecessPack(10, new Vector2(58.2f, 49.4f));
            Assert.IsFalse(manager.HasPack(12), "another corner's cut: the old recess pack goes");
            Assert.IsTrue(manager.HasPack(10));
            Assert.AreEqual(58.2f, manager.PackCentre(10).x, 1e-4f);
            Assert.AreEqual(1, host.GetComponentsInChildren<HealthPackView>(true).Length, "still one");

            manager.SetRecessPack(-1, Vector2.zero);
            Assert.IsFalse(manager.HasPack(10), "the cut is gone (a new room): so is its pack");
            Assert.AreEqual(-1, manager.RecessPackIndex);
            Assert.AreEqual(0, host.GetComponentsInChildren<HealthPackView>(true).Length);
        }

        [Test]
        public void TheMinimapPutsThePlusOnItsPackAndMovesItWithALaterCut()
        {
            HealthPackManager manager = MakeManager();
            MinimapConfig map = ScriptableObject.CreateInstance<MinimapConfig>();
            var mapHost = new GameObject("RecessHealthPackTests Minimap", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                const float mapSize = 200f;
                MinimapView view = mapHost.AddComponent<MinimapView>();
                System.Type type = typeof(MinimapView);
                type.GetField("theme", Any).SetValue(view, theme);
                type.GetField("config", Any).SetValue(view, map);
                type.GetField("frameSize", Any).SetValue(view, new Vector2(mapSize, mapSize));
                type.GetField("packsLayer", Any).SetValue(view, mapHost.GetComponent<RectTransform>());
                MethodInfo show = type.GetMethod("ShowPackBadge", Any);
                var args = new object[] { null, true, manager, -1 };
                object firstBadge = null;
                foreach ((int index, Vector2 spot) in new[] { (12, new Vector2(65.05f, 61.3f)), (10, new Vector2(58.2f, 49.4f)) })
                {
                    manager.SetRecessPack(index, spot);
                    args[3] = index;
                    show.Invoke(view, args);
                    Assert.IsNotNull(args[0], $"pack {index}: a badge");
                    firstBadge ??= args[0];
                    Assert.AreSame(firstBadge, args[0], "the same plus, moved");
                    var root = (RectTransform)args[0].GetType().GetField("Root").GetValue(args[0]);
                    Vector2 expected = MinimapLayout.WorldToMap(manager.PackCentre(index), map.WorldCentre, map.WorldSizeMetres, mapSize);
                    Assert.AreEqual(expected.x, root.anchoredPosition.x, 1e-3f, $"pack {index}: on its pack");
                    Assert.AreEqual(expected.y, root.anchoredPosition.y, 1e-3f, $"pack {index}: on its pack");
                    Assert.IsTrue(root.gameObject.activeSelf, $"pack {index}: shown");
                }
            }
            finally
            {
                Object.DestroyImmediate(mapHost);
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void TheGameCallsTheTestedRules()
        {
            var failed = new List<string>();
            void Check(bool ok, string what) { if (!ok) failed.Add(what); }
            System.Type manager = typeof(HealthPackManager);
            MethodInfo ensure = manager.GetMethod("EnsureRecessPack", Any);
            MethodInfo inPlay = manager.GetMethod("InPlay", Any);
            Check(ensure != null && IlWiring.Uses(manager, "Update", ensure), "the manager looks for the cut every frame");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.RecessPackIndex))), "the index is the rule's");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(DominionMode).GetMethod(nameof(DominionMode.IsActive))), "Dominion is asked");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(MatchDirector).GetProperty(nameof(MatchDirector.CutTeam)).GetGetMethod()), "the room's cut");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(ArenaPhaseTwoCut).GetProperty(nameof(ArenaPhaseTwoCut.AppliedCutTeam)).GetGetMethod()), "the wall this client built");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(PhaseTwoCutGeometry).GetProperty(nameof(PhaseTwoCutGeometry.HasRecess)).GetGetMethod()), "whether it has a recess");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(BuildingManager).GetProperty(nameof(BuildingManager.ZoneCount)).GetGetMethod()), "the zones' packs come first");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", typeof(PhaseTwoCutGeometry).GetProperty(nameof(PhaseTwoCutGeometry.RecessCentre)).GetGetMethod()), "the spot is the geometry's");
            Check(IlWiring.Uses(manager, "EnsureRecessPack", manager.GetMethod(nameof(HealthPackManager.SetRecessPack), Any)), "and is handed to the builder");
            Check(inPlay != null && IlWiring.Uses(manager, "Update", inPlay), "drawing asks whether the pack is in play");
            Check(inPlay != null && IlWiring.Uses(manager, "TryRequestLocal", inPlay), "so does asking for it");
            Check(inPlay != null && IlWiring.Uses(manager, "OnPlayerPropertiesUpdate", inPlay), "and the master's decision");
            System.Type pack = manager.GetNestedType("Pack", Any);
            FieldInfo recess = pack != null ? pack.GetField("recess", Any) : null;
            Check(recess != null && IlWiring.Uses(manager, "InPlay", recess), "a recess pack is in play while its cut stands, whatever its index says as a zone");
            Check(IlWiring.Uses(manager, "InPlay", typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.PackInPlay))), "the in-play answer is the rule's");
            Check(IlWiring.Uses(manager, "InPlay", typeof(MatchDirector).GetMethod(nameof(MatchDirector.IsOutOfPlay))), "from whether the zone is out of play");
            Check(IlWiring.Uses(typeof(MinimapView), "UpdateZones", typeof(MinimapView).GetMethod("UpdateRecessPackBadge", Any)), "the minimap draws it");
            Check(IlWiring.Uses(typeof(MinimapView), "UpdateRecessPackBadge", manager.GetProperty(nameof(HealthPackManager.RecessPackIndex)).GetGetMethod()), "from the manager's recess pack");
            Check(IlWiring.Uses(typeof(MinimapView), "UpdateRecessPackBadge", typeof(MinimapView).GetMethod("ShowPackBadge", Any) ?? (MemberInfo)typeof(MinimapView)),
                  "like every other pack");
            Assert.IsEmpty(failed, string.Join("; ", failed));
        }
    }
}
