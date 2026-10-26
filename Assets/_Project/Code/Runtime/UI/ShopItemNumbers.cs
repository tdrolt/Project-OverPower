using System.Text;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Weapons;

namespace Overpower.UI
{
    /// <summary>The numbers block of the shop pop-up for a weapon or an ability, read live off the asset (and, for
    /// the extras, off the projectile / module prefab) when the pop-up opens - never typed text, so retuning an
    /// asset retunes the pop-up.</summary>
    public static class ShopItemNumbers
    {
        private static string N(float value) => ShopNumberFormat.Compact(value);

        /// <summary>A weapon's numbers: damage, fire interval, range, overheat, then whatever special its projectile
        /// carries (blast, bounces, piercing, wall-piercing, distance bonus, mark, charge, wind-up).</summary>
        public static string Weapon(WeaponDefinition def)
        {
            if (def == null)
                return "";

            var sb = new StringBuilder();
            sb.Append(def.ProjectilesPerShot > 1
                ? $"Damage {N(def.Damage)} x{def.ProjectilesPerShot} ({N(def.Damage * def.ProjectilesPerShot)} total)"
                : $"Damage {N(def.Damage)}");

            float shotsPerSecond = def.FireInterval > 0f ? 1f / def.FireInterval : 0f;
            sb.Append($"\nFire interval {N(def.FireInterval)}s ({N(shotsPerSecond)}/s) · Range {N(def.MaxRange)}m");
            sb.Append($"\nOverheat {N(def.OverheatPerShot)}/shot");
            if (def.CanCharge)
                sb.Append(" · hold to charge");
            // The three lasers wind up before they fire - worth a player reading this before they equip one.
            if (def.WindupSeconds > 0f)
                sb.Append($" · {N(def.WindupSeconds)}s wind-up");
            if (def.MarkWindowSeconds > 0f)
                sb.Append($" · mark: next hit within {N(def.MarkWindowSeconds)}s +{N((def.MarkedDamageMultiplier - 1f) * 100f)}%");

            AppendProjectileSpecials(sb, def);
            return sb.ToString();
        }

        /// <summary>What the weapon's projectile prefab adds: a blast, bounces, piercing, seeing through walls, a bonus
        /// that grows with distance. A prefab never spawned has all these components' serialized values, so this reads
        /// the asset directly - the same "prefab, not a live object" rule AbilityModule.ShopStatsText follows.</summary>
        private static void AppendProjectileSpecials(StringBuilder sb, WeaponDefinition def)
        {
            GameObject prefab = def.ProjectilePrefab;
            if (prefab == null)
                return;

            ExplodeOnImpact blast = prefab.GetComponent<ExplodeOnImpact>();
            if (blast != null)
                sb.Append($"\nBlast {N(blast.SplashDamage)} damage, radius {N(blast.SplashRadius)}m");

            BounceOffWalls bounce = prefab.GetComponent<BounceOffWalls>();
            if (bounce != null)
                sb.Append($"\nBounces {bounce.MaxBounces} times, +{N(bounce.DamagePerBounce * 100f)}% damage per bounce");

            ScaleDamageWithDistance scaling = prefab.GetComponent<ScaleDamageWithDistance>();
            if (scaling != null)
                sb.Append($"\nUp to +{N(scaling.MaxBonus * 100f)}% damage at full range");

            Pierce pierce = prefab.GetComponent<Pierce>();
            if (pierce != null)
                sb.Append(pierce.MaxTargets == BeamResolver.Unlimited ? "\nPierces every enemy in line" : $"\nPierces up to {pierce.MaxTargets} enemies");

            if (prefab.GetComponent<IgnoreWalls>() != null)
                sb.Append("\nPasses through walls");
        }

        /// <summary>An ability's numbers: how often it can be used, then its own (range, damage, duration, radius...) -
        /// the module prefab carries them (AbilityDefinition itself is deliberately thin). Read through the module's
        /// Configured* values and ShopStatsText, never the live charge pool, which a prefab asset never bound to a
        /// player does not have.</summary>
        public static string Ability(AbilityDefinition def)
        {
            if (def == null)
                return "";
            AbilityModule module = def.ModulePrefab != null ? def.ModulePrefab.GetComponent<AbilityModule>() : null;
            if (module == null)
                return "";

            // Ultimates have 0s cooldown / 1 charge by design (AbilityModule's own defaults) - readiness instead comes
            // from the shared UltimateCharge meter (kills, assists, damage dealt/taken), so "cooldown 0s" would
            // flatly misdescribe how they work.
            string timing;
            if (def.Slot == AbilitySlot.Ultimate)
                timing = "Charges from kills, assists, and damage dealt or taken.";
            else if (module.ConfiguredCharges <= 0)
                timing = "No cooldown.";
            else if (module.ConfiguredCharges == 1)
                timing = $"{N(module.ConfiguredCooldownSeconds)}s cooldown";
            else
                timing = $"{module.ConfiguredCharges} charges, {N(module.ConfiguredCooldownSeconds)}s cooldown each";

            return ShopNumberFormat.Lines(timing, module.ShopStatsText());
        }
    }
}
