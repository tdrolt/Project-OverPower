using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Data;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Pins AbilityDefinition.Validate: each module-prefab mistake that would otherwise fail
    /// silently in a match (a key that does nothing, a network id nobody allocated, a module that
    /// blocks its own player's shots) is reported, and a correct ability reports nothing.
    ///
    /// Private fields are seeded through reflection for the same reason CatalogueTests gives - no
    /// test-only surface on the production class.
    /// </summary>
    public class AbilityDefinitionTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var obj in created)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }

            created.Clear();
        }

        private static void SetPrivate(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Expected a private field '{name}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private AbilityDefinition NewAbility(AbilitySlot slot, GameObject modulePrefab)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            created.Add(ability);
            SetPrivate(ability, "slot", slot);
            SetPrivate(ability, "modulePrefab", modulePrefab);
            ability.name = "Test Ability";
            return ability;
        }

        private GameObject NewModulePrefab()
        {
            var go = new GameObject("Test Module");
            created.Add(go);
            go.AddComponent<DebugPingAbility>();
            return go;
        }

        [Test]
        public void ACorrectAbilityReportsNothing()
        {
            Assert.IsEmpty(NewAbility(AbilitySlot.Equipment, NewModulePrefab()).Validate());
        }

        [Test]
        public void SlotPrimaryIsReported()
        {
            List<string> problems = NewAbility(AbilitySlot.Primary, NewModulePrefab()).Validate();
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Primary", problems[0]);
        }

        [Test]
        public void AnEmptyModulePrefabIsReported()
        {
            Assert.AreEqual(1, NewAbility(AbilitySlot.Mobility, null).Validate().Count);
        }

        [Test]
        public void APrefabWithNoAbilityModuleIsReported()
        {
            var go = new GameObject("No Module");
            created.Add(go);

            List<string> problems = NewAbility(AbilitySlot.Ultimate, go).Validate();
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("AbilityModule", problems[0]);
        }

        [Test]
        public void AColliderAnywhereInThePrefabIsReported()
        {
            GameObject prefab = NewModulePrefab();
            var child = new GameObject("Child");
            child.transform.SetParent(prefab.transform);
            child.AddComponent<SphereCollider>();

            List<string> problems = NewAbility(AbilitySlot.Equipment, prefab).Validate();
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Collider", problems[0]);
        }

        [Test]
        public void APhotonViewInThePrefabIsReported()
        {
            GameObject prefab = NewModulePrefab();
            prefab.AddComponent<PhotonView>();

            List<string> problems = NewAbility(AbilitySlot.Equipment, prefab).Validate();
            Assert.IsTrue(problems.Exists(p => p.Contains("PhotonView")));
        }
    }
}
