using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace Overpower.Tests
{
    /// <summary>The Heal Zone (the zone ultimate): each tick heals the caster and the caster's living teammates inside, never an
    /// enemy, never past full health, health only; each healed player's own client applies it, as damage is applied on the victim's. Fake
    /// players and literal numbers; the real assets only for structure.</summary>
    public class HealZoneTests
    {
        private const int Caster = 1, CasterTeam = 0;
        private const string ZonePrefabPath = "Assets/Resources/AoE Zone.prefab";

        private class FakePlayer : IDamageable
        {
            public int ActorNumber { get; set; }
            public int TeamId { get; set; }
            public bool HasLocalAuthority { get; set; } = true;
            public bool IsAlive { get; set; } = true;
            public DamageResult ApplyDamage(in DamageInfo info) => default;
        }

        private class FakeStructure : IDamageable, IStructure
        {
            public int ActorNumber { get; set; } = -1;
            public int TeamId { get; set; } = -1;
            public bool HasLocalAuthority { get; set; } = true;
            public bool IsAlive { get; set; } = true;
            public DamageResult ApplyDamage(in DamageInfo info) => default;
        }

        private static List<IDamageable> Healed(params IDamageable[] inside)
        {
            var into = new List<IDamageable> { new FakePlayer() }; // a leftover from the last tick must not survive
            HealZoneTargeting.SelectHealed(inside, Caster, CasterTeam, into);
            return into;
        }

        // ---- who a tick heals ----

        [Test]
        public void TheCasterAndTheCastersTeammatesInsideAreHealed()
        {
            var self = new FakePlayer { ActorNumber = Caster, TeamId = CasterTeam };
            var mate = new FakePlayer { ActorNumber = 5, TeamId = CasterTeam };
            CollectionAssert.AreEqual(new IDamageable[] { self, mate }, Healed(self, mate));
            Assert.IsTrue(HealZoneTargeting.Heals(Caster, CasterTeam, Caster, CasterTeam, true, true, false), "the caster");
            Assert.IsTrue(HealZoneTargeting.Heals(Caster, CasterTeam, 5, CasterTeam, true, true, false), "a teammate");
        }

        [Test]
        public void AnEnemyInsideIsNeitherHealedNorHurt()
        {
            var enemy = new FakePlayer { ActorNumber = 7, TeamId = 2 };
            CollectionAssert.IsEmpty(Healed(enemy));
            Assert.IsFalse(HealZoneTargeting.Heals(Caster, CasterTeam, 7, 2, true, true, false));
        }

        [Test]
        public void ADeadTeammateIsNotHealed()
        {
            CollectionAssert.IsEmpty(Healed(new FakePlayer { ActorNumber = 5, TeamId = CasterTeam, IsAlive = false }));
            Assert.IsFalse(HealZoneTargeting.Heals(Caster, CasterTeam, Caster, CasterTeam, false, true, false), "a dead caster either");
        }

        [Test]
        public void OnlyTheHealedPlayersOwnClientHealsThem()
        {
            var remoteMate = new FakePlayer { ActorNumber = 5, TeamId = CasterTeam, HasLocalAuthority = false };
            CollectionAssert.IsEmpty(Healed(remoteMate), "another client's copy of a teammate: their own client heals them");
            Assert.IsFalse(HealZoneTargeting.Heals(Caster, CasterTeam, 5, CasterTeam, true, false, false));
        }

        [Test]
        public void ADummyOrACoverWallIsNeverHealed()
        {
            CollectionAssert.IsEmpty(Healed(new FakeStructure(), new FakePlayer { ActorNumber = -1, TeamId = -1 }), "cover, and a practice dummy of no team");
            Assert.IsFalse(HealZoneTargeting.Heals(Caster, CasterTeam, -1, CasterTeam, true, true, true), "a structure, whatever its team");
        }

        [Test]
        public void APracticeDummyIsNeverHealedWhateverItsTeam()
        {
            // A dummy's team can be set to anything in its Inspector, so only "is it a player" keeps it out, never the team.
            CollectionAssert.IsEmpty(Healed(new FakePlayer { ActorNumber = -1, TeamId = CasterTeam }), "a practice dummy set to the caster's team");
            Assert.IsFalse(HealZoneTargeting.Heals(Caster, CasterTeam, -1, CasterTeam, true, true, false));
        }

        [Test]
        public void ACasterOfNoKnownTeamHealsOnlyThemself()
        {
            Assert.IsTrue(HealZoneTargeting.Heals(Caster, -1, Caster, -1, true, true, false));
            Assert.IsFalse(HealZoneTargeting.Heals(Caster, -1, 5, -1, true, true, false), "an unknown team never makes everyone a teammate");
        }

        // ---- how much ----

        [Test]
        public void ATickNeverHealsPastFullHealth()
        {
            Assert.AreEqual(14f, HealthPackRules.HealAmount(14f, 50f, 100f), 1e-5f, "a hurt player gets the whole tick");
            Assert.AreEqual(5f, HealthPackRules.HealAmount(14f, 95f, 100f), 1e-5f, "only what is missing");
            Assert.AreEqual(0f, HealthPackRules.HealAmount(14f, 100f, 100f), 1e-5f, "nothing at full health");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), nameof(PlayerHealth.Heal), typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.HealAmount))),
                "the heal the zone calls caps with that rule");
        }

        // ---- the game calls the rules ----

        [Test]
        public void EachTickHealsThroughTheTestedRuleAndNeverDamages()
        {
            var damageEntries = new List<MemberInfo>();
            damageEntries.Add(typeof(IDamageable).GetMethod(nameof(IDamageable.ApplyDamage)));
            foreach (MethodInfo m in typeof(PlayerHealth).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (m.Name == nameof(PlayerHealth.ApplyDamage)) damageEntries.Add(m);
            damageEntries.AddRange(typeof(DamageInfo).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            damageEntries.AddRange(typeof(PlacedEffects).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
            var damaging = new List<string>();
            foreach (MemberInfo entry in damageEntries)
                if (IlWiring.Uses(typeof(AoeZone), "ApplyTick", entry)) damaging.Add(entry.DeclaringType.Name + "." + entry.Name);

            AssertAll(
                (IlWiring.Uses(typeof(AoeZone), "ApplyTick", typeof(HealZoneTargeting).GetMethod(nameof(HealZoneTargeting.SelectHealed))), "a tick asks who it heals"),
                (IlWiring.Uses(typeof(AoeZone), "ApplyTick", typeof(PlayerHealth).GetMethod(nameof(PlayerHealth.Heal))), "and heals them through the health pack's heal (health only, capped)"),
                (damaging.Count == 0, "nobody is damaged any more (" + string.Join(", ", damaging) + ")"),
                (!IlWiring.Uses(typeof(AoeZone), "ApplyTick", typeof(MineTargeting).GetMethod(nameof(MineTargeting.SelectTargets))), "enemies are no longer picked"));
        }

        [Test]
        public void OnlyTheRulesAnswerReachesTheHeal()
        {
            // Healing the candidates instead of the rule's answer would heal an enemy standing inside on the enemy's own client.
            MethodBase tick = typeof(AoeZone).GetMethod("ApplyTick", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo candidates = typeof(AoeZone).GetField("candidateBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo healed = typeof(AoeZone).GetField("healedBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            List<int> asks = IlWiring.CallOffsets(tick, typeof(HealZoneTargeting).GetMethod(nameof(HealZoneTargeting.SelectHealed)));
            List<int> heals = IlWiring.CallOffsets(tick, typeof(PlayerHealth).GetMethod(nameof(PlayerHealth.Heal)));
            Assert.AreEqual(1, asks.Count, "one question a tick");
            int asked = asks[0];
            AssertAll(
                (IlWiring.FieldLoadOffsets(tick, candidates).TrueForAll(at => at < asked), "the candidates are not read once the rule has answered"),
                (IlWiring.FieldLoadOffsets(tick, healed).Exists(at => at > asked), "the heal loop reads the rule's answer"),
                (heals.Count > 0 && heals.TrueForAll(at => at > asked), "every heal comes after the rule has answered"));

            // The tuned number is what is handed over: the instruction right before each Heal loads Heal Per Tick.
            FieldInfo amount = typeof(AoeZone).GetField("healPerTick", BindingFlags.Instance | BindingFlags.NonPublic);
            List<int> amountLoads = IlWiring.FieldLoadOffsets(tick, amount);
            Assert.IsTrue(heals.TrueForAll(at => amountLoads.Contains(at - 5)), "each heal is Heal Per Tick, not another number");
        }

        // ---- in the fog ----

        [Test]
        public void ADiscThatDoesNothingToUsNeverShowsJustBecauseItReachesUs()
        {
            Assert.IsTrue(Overpower.Vision.ShotVisibilityRules.DiscShown(true, false, false), "seen: shown");
            Assert.IsTrue(Overpower.Vision.ShotVisibilityRules.DiscShown(true, true, false));
            Assert.IsTrue(Overpower.Vision.ShotVisibilityRules.DiscShown(false, true, true), "fire from the fog that reaches us shows");
            Assert.IsFalse(Overpower.Vision.ShotVisibilityRules.DiscShown(false, false, true), "a heal for them, reaching us from the fog, stays hidden");
            Assert.IsFalse(Overpower.Vision.ShotVisibilityRules.DiscShown(false, true, false));
        }

        [Test]
        public void TheHealZoneIsHiddenWithItsCasterAndNeverShownByReach()
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo setRadius = typeof(Overpower.Vision.VisibleWhenSeen).GetMethod(nameof(Overpower.Vision.VisibleWhenSeen.SetSeenRadius));
            MethodInfo placed = typeof(AoeZone).GetMethod("OnPlaced", Any);
            MethodInfo fieldPlaced = typeof(Overpower.Weapons.FireField).GetMethod(nameof(Overpower.Weapons.FireField.OnPhotonInstantiate));
            MethodInfo update = typeof(AoeZone).GetMethod("Update", Any);
            Assert.NotNull(update, "the zone has its Update");
            AssertAll(
                (LastArgumentIsBool(placed, setRadius, false), "the Heal Zone does nothing to an enemy, so reaching them never shows it"),
                (LastArgumentIsBool(fieldPlaced, setRadius, true), "a Fire Field burns us, so reaching us shows it"),
                (IlWiring.Uses(typeof(Overpower.Vision.TeamSight), nameof(Overpower.Vision.TeamSight.DiscShownAt),
                    typeof(Overpower.Vision.ShotVisibilityRules).GetMethod(nameof(Overpower.Vision.ShotVisibilityRules.DiscShown))), "the sight asks the tested rule"),
                (IlWiring.Uses(typeof(Overpower.Vision.VisibleWhenSeen), nameof(Overpower.Vision.VisibleWhenSeen.Apply),
                    typeof(Overpower.Vision.VisibleWhenSeen).GetField("discHurtsMyTeam", Any)), "the gate hands the disc's kind over"),
                (IlWiring.ResultDecidesABranch(typeof(AoeZone), "Update", typeof(AoeZone).GetProperty(nameof(AoeZone.IsFollowing)).GetMethod),
                    "while it follows its caster the zone is hidden with them"),
                (IlWiring.Uses(typeof(AoeZone), "Update", typeof(Overpower.Vision.VisibleWhenSeen).GetMethod(nameof(Overpower.Vision.VisibleWhenSeen.FollowCaster))),
                    "through the caster binding every ability visual uses"));
        }

        private static bool LastArgumentIsBool(MethodBase method, MethodBase target, bool value)
        {
            byte[] il = method.GetMethodBody().GetILAsByteArray();
            List<int> calls = IlWiring.CallOffsets(method, target);
            return calls.Count > 0 && calls.TrueForAll(at => at > 0 && il[at - 1] == (value ? 0x17 : 0x16));
        }

        [Test]
        public void TheOldDamageValueCarriesOverAsTheHeal()
        {
            FieldInfo heal = typeof(AoeZone).GetField("healPerTick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(heal, "the zone has a heal per tick");
            FormerlySerializedAsAttribute former = heal.GetCustomAttribute<FormerlySerializedAsAttribute>();
            Assert.IsNotNull(former, "a saved zone keeps its number");
            Assert.AreEqual("damagePerTick", former.oldName);
            Assert.IsNull(typeof(AoeZone).GetField("damagePerTick", BindingFlags.Instance | BindingFlags.NonPublic), "no damage field left");

            string yaml = File.ReadAllText(ZonePrefabPath);
            StringAssert.Contains("healPerTick:", yaml, "the zone prefab is saved with the new name");
            StringAssert.DoesNotContain("damagePerTick:", yaml);
        }

        [Test]
        public void TheShopPopUpShowsHealingNotDamage()
        {
            var def = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/_Project/Features/Abilities/Data/27 AoE Zone U.asset");
            string text = ShopItemNumbers.Ability(def);
            // The values are tuned on the prefab, so this pins which number the pop-up shows, not the number itself.
            StringAssert.IsMatch(@"heals \d+(\.\d+)? health every \d+(\.\d+)?s", text);
            StringAssert.DoesNotContain("damage every", text, "the old damage line is gone (the ultimate's meter line may still name damage)");
        }

        private static void AssertAll(params (bool ok, string what)[] checks)
        {
            var failed = new List<string>();
            foreach ((bool ok, string what) in checks)
                if (!ok) failed.Add(what);
            Assert.IsEmpty(failed, "missing: " + string.Join("; ", failed));
        }
    }
}
