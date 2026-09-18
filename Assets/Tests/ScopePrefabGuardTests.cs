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
    /// </summary>
    public class ScopePrefabGuardTests
    {
        private const string DefinitionPath = "Assets/Gameplay/Abilities/28 Scope E.asset";
        private const string PrefabPath = "Assets/Gameplay/Abilities/Scope.prefab";
        private const string CataloguePath = "Assets/Gameplay/Config/AbilityCatalogue.asset";

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
            Assert.AreEqual(800, LoadDefinition().GoldCost);
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
