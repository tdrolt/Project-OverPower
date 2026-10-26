using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.EditorTools;
using Overpower.Lobby;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The free spawn slot pick (a body never appears inside another one), its wiring into RoomManager.SpawnPointFor, and the Editor
    /// builder that makes the slot points under a spawn point.
    /// </summary>
    public class SpawnSlotFreePickTests
    {
        private static readonly Vector3[] Line = { new Vector3(-2f, 0f, 0f), Vector3.zero, new Vector3(2f, 0f, 0f) };
        private static readonly Vector3[] Nobody = new Vector3[0];

        [Test] public void OwnSlotWhenNobodyStandsOnIt() => Assert.AreEqual(2, SpawnSlotRules.FreeSlotFor(Line, Nobody, 1.5f, 2));

        [Test] public void OwnSlotEvenWhenAnEarlierSlotIsFree() =>
            Assert.AreEqual(2, SpawnSlotRules.FreeSlotFor(Line, new[] { new Vector3(0.2f, 0f, 0.1f) }, 1.5f, 2),
                "teammates spawning at the same moment cannot see each other yet: each must keep its own slot");

        [Test] public void FirstFreeSlotWhenOwnIsTaken() =>
            Assert.AreEqual(1, SpawnSlotRules.FreeSlotFor(Line, new[] { new Vector3(2.3f, 0f, 0.4f), new Vector3(-2f, 0f, 0f) }, 1.5f, 2));

        [Test] public void OwnSlotWhenEverySlotIsTaken() =>
            Assert.AreEqual(1, SpawnSlotRules.FreeSlotFor(Line, new[] { new Vector3(-2f, 0f, 0f), Vector3.zero, new Vector3(2f, 0f, 0f) }, 1.5f, 1));

        [Test] public void OnlySomeoneInsideTheRadiusTakesASlot()
        {
            Assert.AreEqual(0, SpawnSlotRules.FreeSlotFor(Line, new[] { new Vector3(0.1f, 0f, 0f), new Vector3(-2f, 0f, 1.6f) }, 1.5f, 1), "1.6 m away is outside 1.5 m");
            Assert.AreEqual(2, SpawnSlotRules.FreeSlotFor(Line, new[] { new Vector3(0.1f, 0f, 0f), new Vector3(-2f, 0f, 1.4f) }, 1.5f, 1), "1.4 m away is inside 1.5 m");
        }

        [Test] public void HeightDoesNotCount() =>
            Assert.AreEqual(1, SpawnSlotRules.FreeSlotFor(Line, new[] { new Vector3(-2f, 3f, 0f) }, 1.5f, 0), "someone 3 m above slot 0 still stands on it");

        [Test] public void NobodyListedOrZeroRadiusKeepsOwnSlot()
        {
            Assert.AreEqual(1, SpawnSlotRules.FreeSlotFor(Line, null, 1.5f, 1));
            Assert.AreEqual(1, SpawnSlotRules.FreeSlotFor(Line, new[] { Vector3.zero }, 0f, 1), "radius 0 = slots are never checked");
        }

        [Test] public void SpawnPointForAsksTheFreeSlotRuleWithTheConfiguredRadius()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "SpawnPointFor", typeof(SpawnSlotRules).GetMethod(nameof(SpawnSlotRules.FreeSlotFor))));
            Assert.IsTrue(IlWiring.Uses(typeof(RoomManager), "SpawnPointFor",
                typeof(GameplayConfig).GetProperty(nameof(GameplayConfig.SpawnSlotClearRadius)).GetGetMethod()), "the radius is the config's");
        }

        // ---------------------------------------------------------------- the builder

        [Test] public void PlaceSlotsMakesOneChildPerRowFacingLikeThePointAndRunsAgainCleanly()
        {
            var home = new GameObject("home");
            home.transform.SetPositionAndRotation(new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 120f, 0f));
            var foreign = new GameObject("Marker");
            foreign.transform.SetParent(home.transform, false);
            try
            {
                ArenaSpawnSlotBuilder.PlaceSlots(home.transform, Line);
                ArenaSpawnSlotBuilder.PlaceSlots(home.transform, Line);
                List<Transform> slots = home.transform.Cast<Transform>().Where(c => c.name.StartsWith(ArenaSpawnSlotBuilder.SlotPrefix)).ToList();
                Assert.AreEqual(Line.Length, slots.Count, "a second run replaces its own slots instead of adding more");
                for (int i = 0; i < Line.Length; i++)
                {
                    Transform slot = home.transform.GetChild(i);
                    StringAssert.StartsWith(ArenaSpawnSlotBuilder.SlotPrefix, slot.name, "the slots come first: SpawnPointFor picks a child by its index");
                    Assert.Less(Vector3.Distance(Line[i], slot.localPosition), 1e-4f);
                    Assert.Less(Quaternion.Angle(home.transform.rotation, slot.rotation), 0.01f, "a slot faces like its point");
                }
                Assert.IsNotNull(home.transform.Find("Marker"), "a child the builder did not make is left alone");
            }
            finally { Object.DestroyImmediate(home); }
        }

        [Test] public void BuildPrimitiveArenaPlacesTheSpawnSlotsFromTheLayout()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaPrimitiveBuilder), "BuildAll", typeof(ArenaSpawnSlotBuilder).GetMethod(nameof(ArenaSpawnSlotBuilder.PlaceAll))));
            Assert.IsTrue(IlWiring.Uses(typeof(ArenaSpawnSlotBuilder), "PlaceAll", typeof(ArenaSpawnSlotBuilder).GetMethod(nameof(ArenaSpawnSlotBuilder.PlaceSlots))));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(ArenaSpawnSlotBuilder), "PlaceAll",
                typeof(ArenaLayout).GetProperty(nameof(ArenaLayout.SpawnSlotOffsets)).GetGetMethod()), "the offsets are the layout asset's");
        }
    }
}
