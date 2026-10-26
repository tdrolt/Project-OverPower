using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>The shipped look table: every ability of the catalogue has one look, the three debug pings need none, the
    /// Scope draws nothing, and the respawn bubble and the Shield cannot be mistaken for each other.</summary>
    public class AbilityLookTableTests
    {
        private const string LookPath = "Assets/_Project/Shared/Config/ArenaLook.asset";
        private const string CataloguePath = "Assets/_Project/Shared/Config/AbilityCatalogue.asset";
        private const int ScopeId = 28;
        private const int ShieldId = 25;
        private static readonly int[] DebugPings = { 901, 902, 903 };

        private ArenaLook look;
        private AbilityCatalogue catalogue;

        [SetUp]
        public void Load()
        {
            look = AssetDatabase.LoadAssetAtPath<ArenaLook>(LookPath);
            catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>(CataloguePath);
            Assert.NotNull(look, LookPath);
            Assert.NotNull(catalogue, CataloguePath);
        }

        [Test]
        public void OneEntryPerCatalogueAbilityDebugPingsExempt()
        {
            List<string> problems = AbilityLookRules.Problems(
                catalogue.Abilities.Select(a => a.Id), look.AbilityLooks, new HashSet<int>(DebugPings));
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void NoTwoEntriesShareShapeAndFamily()
        {
            var seen = new HashSet<(LookShape, LookFamily)>();
            foreach (AbilityLook l in look.AbilityLooks)
            {
                if (l.Shape == LookShape.NoLook) continue;
                Assert.IsTrue(seen.Add((l.Shape, l.Family)), $"ability {l.AbilityId} repeats {l.Shape} in {l.Family}");
            }
        }

        [Test]
        public void ScopeHasNoLook()
        {
            Assert.IsTrue(look.TryGetLook(ScopeId, out AbilityLook scope));
            Assert.AreEqual(LookShape.NoLook, scope.Shape);
            Assert.AreEqual(LookFamily.None, scope.Family);
        }

        [Test]
        public void BubbleAndShieldDifferInShapeAndFamily()
        {
            Assert.IsTrue(look.TryGetLook(ShieldId, out AbilityLook shield));
            AbilityLook bubble = look.RespawnBubbleLook;
            Assert.AreNotEqual(shield.Shape, bubble.Shape);
            Assert.AreNotEqual(shield.Family, bubble.Family);
        }

        [Test]
        public void TheElectricFencesArcsAreRedDamage()
        {
            Assert.IsTrue(look.TryGetLook(26, out AbilityLook fence));
            Assert.AreEqual(LookShape.ArcPylons, fence.Shape);
            Assert.AreEqual(LookFamily.Danger, fence.Family, "the arcs hurt: red, like the Raybeam (the pylons keep the owner's team colour)");
        }

        [Test]
        public void TheHealZoneIsAGreenHexRing()
        {
            Assert.IsTrue(look.TryGetLook(27, out AbilityLook zone));
            Assert.AreEqual(LookShape.HexGridRing, zone.Shape);
            Assert.AreEqual(LookFamily.Heal, zone.Family, "it heals: the healing green, like health packs (its outer ring wears the caster's team)");
        }

        [Test]
        public void TheBubbleIsAHologramInTheTeamColour()
        {
            Assert.AreEqual(LookShape.HologramSphere, look.RespawnBubbleLook.Shape);
            Assert.AreEqual(LookFamily.Team, look.RespawnBubbleLook.Family);
        }

        [Test]
        public void TryGetLookFindsByIdNotByPositionAndFailsForAnUnknownId()
        {
            Assert.IsTrue(look.TryGetLook(ShieldId, out AbilityLook shield));
            Assert.AreEqual(ShieldId, shield.AbilityId);
            Assert.IsFalse(look.TryGetLook(-5, out _));
            Assert.IsFalse(look.TryGetLook(DebugPings[0], out _), "a debug ping has no look");
        }
    }
}
