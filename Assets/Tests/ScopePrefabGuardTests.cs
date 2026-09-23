using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Data;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Scope step 2 (Tudor, 2026-09-18). Guards the RELATIONSHIPS the spec cares about, not a pinned tuning
    /// number - extraZoomOutPercent must stay retunable in the Inspector without turning this test red (Tudor:
    /// "so I can retune it in the Inspector without touching code"). Same recipe as
    /// InvulnerabilityPrefabGuardTests: the asset/prefab are loaded, never instantiated or saved.
    ///
    /// 2026-09-24 (Tudor: "remove tests that are outdated"): ScopeCostsTheSameAsEveryOtherEquipmentAbility used to
    /// pin the literal 800; rewritten to the same catalogue-relationship pattern
    /// RaybeamUltimateGuardTests.EveryUltimateCostsTheSameGold uses, since every real Equipment ability shares one
    /// price today (Mines/Deployable Cover/Flamethrower/Stun Gun/Sonic Pulse/Scope all 800, measured 2026-09-24).
    /// </summary>
    public class ScopePrefabGuardTests
    {
        private const string DefinitionPath = "Assets/Gameplay/Abilities/28 Scope E.asset";
        private const string PrefabPath = "Assets/Gameplay/Abilities/Scope.prefab";
        private const string CataloguePath = "Assets/Gameplay/Config/AbilityCatalogue.asset";

        // The shop's own cutoff (LoadoutScreen.Builder.cs:237, LoadoutScreen.cs:65, and
        // RaybeamUltimateGuardTests's own copy of this same rule) - test content, not a real ability.
        private const int FirstDebugAbilityId = 900;

        private static AbilityDefinition LoadDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(DefinitionPath);
            Assert.IsNotNull(def, DefinitionPath);
            return def;
        }

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            return prefab;
        }

        [Test]
        public void ScopeIsAnEquipmentAbility()
        {
            // The relationship the spec is built on: right mouse is the Equipment slot. Carrying Scope means
            // giving up mines, cover, raybeam and so on - that trade-off is intended, not this test's concern.
            Assert.AreEqual(AbilitySlot.Equipment, LoadDefinition().Slot);
        }

        [Test]
        public void ScopeHasNoCooldownGateLikeSprint()
        {
            // A relationship, not a pinned number: charges 0 means no pool at all - the same "no cooldown, no
            // heat cost" the spec asks for, and the same convention Sprint uses for a heat-gated ability.
            ScopeAbility module = LoadPrefab().GetComponent<ScopeAbility>();
            Assert.IsNotNull(module, PrefabPath + " has no ScopeAbility");
            Assert.AreEqual(0, module.ConfiguredCharges);
        }

        [Test]
        public void ScopeCostsTheSameAsEveryOtherEquipmentAbility()
        {
            // A relationship, not a pinned number (RaybeamUltimateGuardTests.EveryUltimateCostsTheSameGold's own
            // pattern): every real (non-debug) Equipment-slot ability costs the same gold, whatever that shared
            // price is - Tudor can retune it freely without this test going red.
            var catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>(CataloguePath);
            Assert.IsNotNull(catalogue, CataloguePath);

            var equipment = new List<AbilityDefinition>();
            foreach (AbilityDefinition ability in catalogue.ForSlot(AbilitySlot.Equipment))
                if (ability.Id < FirstDebugAbilityId)
                    equipment.Add(ability);

            Assert.IsNotEmpty(equipment, "No real (non-debug) Equipment-slot ability found in the catalogue.");
            Assert.Contains(LoadDefinition(), equipment, "Scope itself should be one of them.");

            int expected = equipment[0].GoldCost;
            foreach (AbilityDefinition ability in equipment)
                Assert.AreEqual(expected, ability.GoldCost, $"{ability.name}.goldCost");
        }

        [Test]
        public void ScopeHasNoIconYetLikeEveryOtherAbility()
        {
            Assert.IsNull(LoadDefinition().Icon);
        }

        [Test]
        public void ScopeResolvesThroughTheRealCatalogueByItsOwnId()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>(CataloguePath);
            Assert.IsNotNull(catalogue, CataloguePath);

            AbilityDefinition definition = LoadDefinition();
            Assert.AreSame(definition, catalogue.Resolve(definition.Id));
        }

        [Test]
        public void TheRealCatalogueValidatesCleanWithScopeAdded()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>(CataloguePath);
            Assert.IsNotNull(catalogue, CataloguePath);

            var problems = catalogue.Validate();
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void ThePrefabCarriesNoCollider()
        {
            Assert.IsEmpty(LoadPrefab().GetComponentsInChildren<Collider>(true), PrefabPath + " must have no collider");
        }

        [Test]
        public void ThePrefabCarriesNoPhotonView()
        {
            Assert.IsEmpty(LoadPrefab().GetComponentsInChildren<PhotonView>(true), PrefabPath + " must have no PhotonView");
        }

        [Test]
        public void ThePrefabCarriesNoIPunObservable()
        {
            Assert.IsEmpty(LoadPrefab().GetComponentsInChildren<IPunObservable>(true), PrefabPath + " must have no IPunObservable");
        }
    }
}
