using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Data;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Rework step 5 (Tudor, 2026-09-18): the Raybeam moves from Equipment to Ultimate, gated by the
    /// ultimate meter alone like every other ultimate, and costs the same gold as the rest. Pins the
    /// slot move and the two cross-ultimate relationships as a live read of the real catalogue asset -
    /// AssetDatabase + SerializedObject, read-only, the same recipe as AbilityVisualPrefabGuardTests
    /// and InvulnerabilityPrefabGuardTests.
    ///
    /// DEBUG PING IS EXCLUDED BY Id >= 900 - NOT A MAGIC NUMBER HERE, BUT THE PROJECT'S OWN
    /// DOCUMENTED RULE FOR "IS THIS A REAL ABILITY". The shop already draws this exact line:
    /// LoadoutScreen.Builder.cs:237 (`slotAbilities.RemoveAll(a => a == null || a.Id >= 900); //
    /// Debug abilities stay in F1 only.`) and LoadoutScreen.cs:65's own tooltip ("ids 901-903 are
    /// debug abilities and stay reachable only through the F1 test range panel"). Matching that
    /// rule here means this test and the shop agree on what "a real ultimate" is, and a future
    /// debug ability dropped in at 904 is excluded by both without anyone having to remember a
    /// second rule. 902 Debug Ping U sits in the live catalogue as a real Ultimate-slot entry with
    /// goldCost 0 - it is test content used to prove the ability framework, not a real ultimate.
    /// </summary>
    public class RaybeamUltimateGuardTests
    {
        private const string RaybeamPath = "Assets/Gameplay/Abilities/24 Raybeam E.asset";
        private const string CataloguePath = "Assets/Gameplay/Config/AbilityCatalogue.asset";

        // The shop's own cutoff (LoadoutScreen.Builder.cs:237, LoadoutScreen.cs:65) - see this
        // class's own comment for why the same number belongs here too.
        private const int FirstDebugAbilityId = 900;

        private static AbilityDefinition LoadRaybeam()
        {
            var definition = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(RaybeamPath);
            Assert.IsNotNull(definition, RaybeamPath);
            return definition;
        }

        private static AbilityCatalogue LoadCatalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>(CataloguePath);
            Assert.IsNotNull(catalogue, CataloguePath);
            return catalogue;
        }

        /// <summary>Test content, not a real ultimate - see this class's own comment.</summary>
        private static bool IsDebugAbility(AbilityDefinition ability) => ability.Id >= FirstDebugAbilityId;

        private static List<AbilityDefinition> RealUltimates()
        {
            var result = new List<AbilityDefinition>();
            foreach (AbilityDefinition ability in LoadCatalogue().ForSlot(AbilitySlot.Ultimate))
            {
                if (!IsDebugAbility(ability))
                    result.Add(ability);
            }

            return result;
        }

        [Test]
        public void RaybeamIsAnUltimate()
        {
            Assert.AreEqual(AbilitySlot.Ultimate, LoadRaybeam().Slot);
        }

        [Test]
        public void EveryUltimateIsGatedByTheMeterAloneAndNotAlsoByACooldown()
        {
            List<AbilityDefinition> ultimates = RealUltimates();
            Assert.IsNotEmpty(ultimates, "No real (non-debug) Ultimate-slot ability found in the catalogue.");

            foreach (AbilityDefinition ability in ultimates)
            {
                AbilityModule module = ability.ModulePrefab.GetComponent<AbilityModule>();
                Assert.IsNotNull(module, $"{ability.name}'s module prefab has no AbilityModule");
                Assert.AreEqual(0f, module.ConfiguredCooldownSeconds, $"{ability.name}.cooldownSeconds");
                Assert.AreEqual(1, module.ConfiguredCharges, $"{ability.name}.charges");
            }
        }

        [Test]
        public void EveryUltimateCostsTheSameGold()
        {
            // A relationship, not a number, so Tudor can retune every ultimate's price together
            // without this test going red (decision 12, Tudor, 2026-09-18).
            List<AbilityDefinition> ultimates = RealUltimates();
            Assert.IsNotEmpty(ultimates, "No real (non-debug) Ultimate-slot ability found in the catalogue.");

            int expected = ultimates[0].GoldCost;
            foreach (AbilityDefinition ability in ultimates)
                Assert.AreEqual(expected, ability.GoldCost, $"{ability.name}.goldCost");
        }

        [Test]
        public void TheCatalogueStillValidates()
        {
            CollectionAssert.IsEmpty(LoadCatalogue().Validate());
        }
    }
}
