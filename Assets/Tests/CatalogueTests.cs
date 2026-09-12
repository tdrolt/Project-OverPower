using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Pins the one rule these two catalogues exist to enforce: an id resolves to the same
    /// definition no matter where that definition sits in the list.
    ///
    /// The catalogues are seeded by writing their private serialized fields through reflection.
    /// The alternative was an internal seeding method plus an InternalsVisibleTo attribute, and
    /// reflection won because it adds no surface to the production classes at all - there is no
    /// test-only method sitting in WeaponCatalogue for someone to find later and call from real
    /// code, and nothing about the asset layer changes shape to suit its tests. The cost is that
    /// renaming a serialized field breaks these tests at run time rather than at compile time, so
    /// all of the reflection is confined to the three helpers at the top of this file.
    /// </summary>
    public class CatalogueTests
    {
        private readonly List<ScriptableObject> created = new List<ScriptableObject>();

        [TearDown]
        public void DestroyCreatedAssets()
        {
            // CreateInstance'd ScriptableObjects are not garbage collected on their own and the
            // test runner complains about the leak, so they are cleaned up per test.
            foreach (var asset in created)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- reflection helpers: the only place that knows a private field's name ----

        private static FieldInfo PrivateField(object target, string name)
        {
            var field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field,
                $"Expected a private field '{name}' on {target.GetType().Name}. If it was renamed, " +
                "update these helpers rather than widening the field's access.");

            return field;
        }

        private static void SetPrivate(object target, string name, object value) =>
            PrivateField(target, name).SetValue(target, value);

        private static T GetPrivate<T>(object target, string name) =>
            (T)PrivateField(target, name).GetValue(target);

        // ---- builders ----

        private T New<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            created.Add(asset);
            return asset;
        }

        private WeaponDefinition NewWeapon(int id, string assetName)
        {
            var weapon = New<WeaponDefinition>();
            SetPrivate(weapon, "id", id);
            weapon.name = assetName;
            return weapon;
        }

        private AbilityDefinition NewAbility(int id, string assetName, AbilitySlot slot)
        {
            var ability = New<AbilityDefinition>();
            SetPrivate(ability, "id", id);
            SetPrivate(ability, "slot", slot);
            ability.name = assetName;
            return ability;
        }

        private WeaponCatalogue NewWeaponCatalogue(params WeaponDefinition[] weapons)
        {
            var catalogue = New<WeaponCatalogue>();
            SetPrivate(catalogue, "weapons", new List<WeaponDefinition>(weapons));

            // OnEnable already ran, back when the list was empty, so the lookup it built is empty
            // too. Dropping it makes the next Resolve rebuild from the list just seeded.
            InvalidateLookup(catalogue);
            return catalogue;
        }

        private AbilityCatalogue NewAbilityCatalogue(params AbilityDefinition[] abilities)
        {
            var catalogue = New<AbilityCatalogue>();
            SetPrivate(catalogue, "abilities", new List<AbilityDefinition>(abilities));
            InvalidateLookup(catalogue);
            return catalogue;
        }

        private static void InvalidateLookup(object catalogue) =>
            SetPrivate(catalogue, "byId", null);

        // ---- WeaponCatalogue ----

        [Test]
        public void WeaponResolveReturnsTheMatchingDefinition()
        {
            var pistol = NewWeapon(1, "Pistol");
            var rifle = NewWeapon(7, "Rifle");
            var catalogue = NewWeaponCatalogue(pistol, rifle);

            Assert.AreSame(rifle, catalogue.Resolve(7));
            Assert.AreSame(pistol, catalogue.Resolve(1));
        }

        [Test]
        public void WeaponResolveOnAnUnknownIdReturnsNullAndDoesNotThrow()
        {
            // Ids arrive over the network from clients that may be stale or lying, so an unknown
            // id has to be a null the caller checks, never an exception that takes this client out.
            var catalogue = NewWeaponCatalogue(NewWeapon(1, "Pistol"));

            WeaponDefinition resolved = null;
            Assert.DoesNotThrow(() => resolved = catalogue.Resolve(999));
            Assert.IsNull(resolved);

            Assert.DoesNotThrow(() => catalogue.Resolve(-1));
            Assert.IsNull(catalogue.Resolve(-1));
        }

        [Test]
        public void WeaponResolveIsUnaffectedByListOrder()
        {
            // The test that pins the id rule. If resolution ever went through the list index,
            // reordering the list in the Inspector would silently hand every player a different
            // weapon mid-match.
            var catalogue = NewWeaponCatalogue(
                NewWeapon(1, "Pistol"),
                NewWeapon(7, "Rifle"),
                NewWeapon(12, "Rocket"));

            var before = catalogue.Resolve(7);

            GetPrivate<List<WeaponDefinition>>(catalogue, "weapons").Reverse();
            InvalidateLookup(catalogue); // force a genuine rebuild from the new order

            var after = catalogue.Resolve(7);

            Assert.AreSame(before, after);
            Assert.AreEqual("Rifle", after.name);
            Assert.AreEqual(7, after.Id);
        }

        [Test]
        public void WeaponValidateReportsDuplicateIds()
        {
            // Resolve keeps the first match, so the second weapon is silently unreachable.
            // Validate is the only thing that makes that visible.
            var catalogue = NewWeaponCatalogue(
                NewWeapon(3, "Shotgun"),
                NewWeapon(3, "ShotgunUpgrade"));

            var problems = catalogue.Validate();

            Assert.AreEqual(1, problems.Count);
            Assert.That(problems[0], Does.Contain("ShotgunUpgrade"));
            Assert.That(problems[0], Does.Contain("Shotgun"));
            Assert.That(problems[0], Does.Contain("3"));
        }

        [Test]
        public void WeaponValidateReportsNothingWhenEveryIdIsUnique()
        {
            var catalogue = NewWeaponCatalogue(
                NewWeapon(1, "Pistol"),
                NewWeapon(7, "Rifle"));

            Assert.IsEmpty(catalogue.Validate());
        }

        // ---- AbilityCatalogue ----

        [Test]
        public void AbilityResolveReturnsTheMatchingDefinition()
        {
            var dash = NewAbility(4, "Dash", AbilitySlot.Mobility);
            var mine = NewAbility(9, "Mine", AbilitySlot.Equipment);
            var catalogue = NewAbilityCatalogue(dash, mine);

            Assert.AreSame(dash, catalogue.Resolve(4));
            Assert.AreSame(mine, catalogue.Resolve(9));
        }

        [Test]
        public void AbilityResolveOnAnUnknownIdReturnsNullAndDoesNotThrow()
        {
            var catalogue = NewAbilityCatalogue(NewAbility(4, "Dash", AbilitySlot.Mobility));

            AbilityDefinition resolved = null;
            Assert.DoesNotThrow(() => resolved = catalogue.Resolve(999));
            Assert.IsNull(resolved);
        }

        [Test]
        public void AbilityResolveIsUnaffectedByListOrder()
        {
            var catalogue = NewAbilityCatalogue(
                NewAbility(4, "Dash", AbilitySlot.Mobility),
                NewAbility(7, "Turret", AbilitySlot.Ultimate),
                NewAbility(9, "Mine", AbilitySlot.Equipment));

            var before = catalogue.Resolve(7);

            GetPrivate<List<AbilityDefinition>>(catalogue, "abilities").Reverse();
            InvalidateLookup(catalogue);

            var after = catalogue.Resolve(7);

            Assert.AreSame(before, after);
            Assert.AreEqual("Turret", after.name);
            Assert.AreEqual(7, after.Id);
        }

        [Test]
        public void AbilityValidateReportsDuplicateIds()
        {
            var catalogue = NewAbilityCatalogue(
                NewAbility(5, "Shield", AbilitySlot.Equipment),
                NewAbility(5, "ShieldUpgrade", AbilitySlot.Equipment));

            var problems = catalogue.Validate();

            Assert.AreEqual(1, problems.Count);
            Assert.That(problems[0], Does.Contain("ShieldUpgrade"));
            Assert.That(problems[0], Does.Contain("Shield"));
            Assert.That(problems[0], Does.Contain("5"));
        }

        [Test]
        public void ForSlotReturnsOnlyAbilitiesInThatSlot()
        {
            var dash = NewAbility(1, "Dash", AbilitySlot.Mobility);
            var blink = NewAbility(2, "Blink", AbilitySlot.Mobility);
            var mine = NewAbility(3, "Mine", AbilitySlot.Equipment);
            var nuke = NewAbility(4, "Nuke", AbilitySlot.Ultimate);
            var catalogue = NewAbilityCatalogue(dash, blink, mine, nuke);

            var mobility = catalogue.ForSlot(AbilitySlot.Mobility);

            Assert.AreEqual(2, mobility.Count);
            Assert.Contains(dash, mobility);
            Assert.Contains(blink, mobility);
            Assert.That(mobility, Has.No.Member(mine));
            Assert.That(mobility, Has.No.Member(nuke));

            Assert.AreEqual(1, catalogue.ForSlot(AbilitySlot.Ultimate).Count);
            Assert.IsEmpty(catalogue.ForSlot(AbilitySlot.Primary));
        }
    }
}
