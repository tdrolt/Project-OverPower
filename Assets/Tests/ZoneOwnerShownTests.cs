using System.Reflection;
using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The owner a team believes a zone has: what it knows while the zone switch hides the live state, the live
    /// snapshot otherwise. Every display of zone ownership asks this one rule, so two displays can never disagree.</summary>
    public class ZoneOwnerShownTests
    {
        [Test]
        public void KnownOwnerWinsWhileFiltering()
        {
            Assert.AreEqual(2, ZoneOwnerShown.Pick(true, 2, 0));
            Assert.AreEqual(TerritoryMap.Neutral, ZoneOwnerShown.Pick(true, TerritoryMap.Neutral, 1), "a zone the team knows as neutral stays neutral even if it was lost to the enemy since");
        }

        [Test]
        public void LiveOwnerOtherwise()
        {
            Assert.AreEqual(1, ZoneOwnerShown.Pick(false, 2, 1));
            Assert.AreEqual(TerritoryMap.Neutral, ZoneOwnerShown.Pick(false, 2, TerritoryMap.Neutral));
        }

        [Test]
        public void OfReadsTheLiveSnapshotWhenNothingIsFiltering()
        {
            Assume.That(ZoneKnowledge.Instance, Is.Null, "a running ZoneKnowledge would answer from its own store");
            TerritorySnapshot snapshot = new TerritorySnapshot(3).WithCapture(1, 2, 0, 0);
            Assert.AreEqual(2, ZoneOwnerShown.Of(snapshot, 1));
            Assert.AreEqual(TerritoryMap.Neutral, ZoneOwnerShown.Of(snapshot, 0));
        }

        [Test]
        public void OfReturnsWhatTheTeamKnowsWhileFiltering()
        {
            Assume.That(ZoneKnowledge.Instance, Is.Null, "a running ZoneKnowledge would answer from its own store");
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var go = new GameObject("ZoneKnowledgeUnderTest");
            try
            {
                var knowledge = go.AddComponent<ZoneKnowledge>();
                typeof(ZoneKnowledge).GetProperty(nameof(ZoneKnowledge.Instance), all).GetSetMethod(true).Invoke(null, new object[] { knowledge });
                typeof(ZoneKnowledge).GetField("filtering", all).SetValue(knowledge, true);
                var store = (ZoneKnowledgeStore)typeof(ZoneKnowledge).GetField("store", all).GetValue(knowledge);
                store.Update(true, 0, new[] { ZoneView.Neutral, ZoneView.Neutral, new ZoneView(2, ZoneView.Neutral.Ring, 0) }, _ => false, null);

                TerritorySnapshot live = new TerritorySnapshot(3).WithCapture(1, 1, 0, 0);
                Assert.AreEqual(TerritoryMap.Neutral, ZoneOwnerShown.Of(live, 1), "zone 1 was lost to team 1 unseen: the team still believes it neutral");
                Assert.AreEqual(2, ZoneOwnerShown.Of(live, 2), "zone 2 is neutral live, but the team last saw it held by team 2");
            }
            finally
            {
                typeof(ZoneKnowledge).GetProperty(nameof(ZoneKnowledge.Instance), all).GetSetMethod(true).Invoke(null, new object[] { null });
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OfHandsTheKnowledgeAnswerToPick()
        {
            System.Type owner = typeof(ZoneOwnerShown);
            Assert.IsTrue(IlWiring.Uses(owner, nameof(ZoneOwnerShown.Of), typeof(ZoneKnowledge).GetMethod(nameof(ZoneKnowledge.TryGetDisplayed))));
            Assert.IsTrue(IlWiring.Uses(owner, nameof(ZoneOwnerShown.Of), owner.GetMethod(nameof(ZoneOwnerShown.Pick))));
        }

        [Test]
        public void TheMinimapColoursZonesAndLinksThroughTheSharedRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "RecolourOwnership",
                typeof(ZoneOwnerShown).GetMethod(nameof(ZoneOwnerShown.Of))));
            Assert.IsFalse(IlWiring.Uses(typeof(MinimapView), "RecolourOwnership",
                typeof(ZoneKnowledge).GetMethod(nameof(ZoneKnowledge.TryGetDisplayed))), "an inline copy of the rule has crept back in");
        }
    }
}
