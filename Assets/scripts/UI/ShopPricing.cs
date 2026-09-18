using System.Globalization;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;

namespace Overpower.UI
{
    /// <summary>
    /// The loadout screen's own glue between the pure Overpower.Match.ShopRules and this player's
    /// live state (position, team, wallet, out-of-combat timer). Pulled out of LoadoutScreen
    /// (Task 2.5b, CODING-STANDARDS "keep LoadoutScreen from growing further") because five
    /// separate click handlers - weapon buy, weapon reset, armor upgrade, armor reset, ability
    /// buy - all need to ask the exact same three questions (am I in my own territory, how long
    /// out of combat, what is my balance) before they can call ShopRules.Check. Nothing here is a
    /// RULE of its own - ShopRules still owns every decision - this only gathers what a rule needs
    /// to ask about THIS player right now, once per Refresh, instead of five separate copies of the
    /// same BuildingManager/Teams/GoldWallet reads scattered through the screen.
    /// </summary>
    public readonly struct ShopContext
    {
        public readonly bool FreeLoadout;
        public readonly bool InOwnTerritory;
        public readonly float SecondsSinceCombat;
        public readonly float RequiredOutOfCombatSeconds;
        public readonly int Balance;

        public ShopContext(bool freeLoadout, bool inOwnTerritory, float secondsSinceCombat,
                            float requiredOutOfCombatSeconds, int balance)
        {
            FreeLoadout = freeLoadout;
            InOwnTerritory = inOwnTerritory;
            SecondsSinceCombat = secondsSinceCombat;
            RequiredOutOfCombatSeconds = requiredOutOfCombatSeconds;
            Balance = balance;
        }

        /// <summary>None outright when Free Loadout is on (GameplayConfig.FreeLoadout's own
        /// tooltip: "changes anything for free, anywhere") - every click handler still calls this
        /// rather than checking the flag itself, so there is exactly one place that decides what
        /// Free Loadout means for a purchase.</summary>
        public PurchaseBlock Check(int price) =>
            FreeLoadout ? PurchaseBlock.None
                        : ShopRules.Check(InOwnTerritory, SecondsSinceCombat, RequiredOutOfCombatSeconds, Balance, price);

        public float SecondsUntilOutOfCombat => ShopRules.SecondsUntilOutOfCombat(SecondsSinceCombat, RequiredOutOfCombatSeconds);

        /// <summary>Why a SPECIFIC purchase attempt is blocked, in one place (Task 2.5b review
        /// fix 2) so StatusText's generic (price-less) header question and a refused click's own
        /// item-specific reason (LoadoutScreen.ShowBlockedReason) always agree on the wording.
        /// Price is the item actually being bought - 0 for the header's own question, since
        /// CannotAfford is deliberately per-item there (shown on the node/card, not the header) -
        /// but the real price when a click's own gate check found CannotAfford, so the shortfall
        /// ("Need 700 more gold") is the real one, not always zero.</summary>
        public string ReasonText(PurchaseBlock block, int price)
        {
            switch (block)
            {
                case PurchaseBlock.NotInOwnTerritory: return "Go to a zone your team owns";
                case PurchaseBlock.InCombat: return $"Out of combat in {SecondsUntilOutOfCombat.ToString("0.0", CultureInfo.InvariantCulture)}s";
                case PurchaseBlock.CannotAfford: return $"Need {price - Balance} more gold";
                default: return "";
            }
        }

        /// <summary>The header's status line: why nothing can be bought here right now (checked with
        /// no price of its own, so CannotAfford never fires for this generic question - that is
        /// per-item, shown on the node/card itself instead), or "" once the gate holds. Free Loadout
        /// gets its own single note here instead of a block reason, matching the header's brief.</summary>
        public string StatusText() => FreeLoadout ? "Free (test mode)" : ReasonText(Check(0), 0);
    }

    /// <summary>Builds a ShopContext from live player state - the one place LoadoutScreen asks
    /// BuildingManager/Teams/GoldWallet/PlayerHealth the same questions every purchase check needs.</summary>
    public static class ShopPricing
    {
        public static ShopContext Build(GameplayConfig config, PlayerHealth health, GoldWallet wallet, Player owner, Vector3 position)
        {
            // Fails OPEN to Free Loadout on a missing config, same as PlayerLoadout/AbilityRunner do
            // for their own GameplayConfig reads - a missing reference degrades to "everything free",
            // never to "everything locked with no explanation".
            bool freeLoadout = config == null || config.FreeLoadout;
            bool inOwnTerritory = InOwnTerritory(owner, position);
            float secondsSinceCombat = health != null ? health.SecondsSinceCombat : 0f;
            float required = config != null ? config.ShopOutOfCombatSeconds : 0f;
            int balance = wallet != null ? wallet.Balance : 0;
            return new ShopContext(freeLoadout, inOwnTerritory, secondsSinceCombat, required, balance);
        }

        private static bool InOwnTerritory(Player owner, Vector3 position)
        {
            if (BuildingManager.Instance == null || BuildingManager.Instance.Current == null)
                return false;
            if (!Teams.TryGetTeam(owner, out int team))
                return false;
            if (!BuildingManager.Instance.TryGetZoneAt(position, out int zone))
                return false;
            return BuildingManager.Instance.Current.OwnerOf(zone) == team;
        }

        /// <summary>What a node/card's own price line reads - the plain number, or "Free" for
        /// anything that costs nothing right now (a 0-cost weapon root, or an ability's first free
        /// pick into an empty slot via ShopRules.AbilityPrice). Deliberately independent of Free
        /// Loadout: prices stay shown as real info even while testing for free (assignment brief),
        /// so a designer previews the real economy without it costing them anything yet.</summary>
        public static string PriceLabel(int price) => price > 0 ? price.ToString(CultureInfo.InvariantCulture) : "Free";

        /// <summary>A node/card's own price line while the shop gate specifically CANNOT afford it
        /// right now - "1200 · need 700" (Task 2.5b review fix 1), shortfall = price - balance,
        /// the same subtraction ShopContext.ReasonText's "Need N more gold" uses for the header.
        /// Every OTHER block (territory/combat, already explained by the header status line, or no
        /// block at all) just reads the plain PriceLabel - piling a second reason onto the node
        /// would repeat what the header already says.</summary>
        public static string PriceLine(int price, PurchaseBlock block, int balance) =>
            block == PurchaseBlock.CannotAfford ? $"{PriceLabel(price)} · need {price - balance}" : PriceLabel(price);

        /// <summary>"Gold 1234" - CultureInfo.InvariantCulture (Task 2.5b review fix 4), matching
        /// PlayerHud.UpdateGold's own gold formatting so the HUD and this header never disagree on
        /// a decimal/thousands separator on a non-English Windows locale.</summary>
        public static string GoldLabel(int balance) => $"Gold {balance.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>The HUD's own two-line gold readout, next to the shop button (HUD step 4): the balance on
        /// top, this second's income under it at sizePercent of the balance's size. InvariantCulture on BOTH
        /// numbers, and pinned by a test rather than by a comment: on a machine whose culture uses a comma as the
        /// decimal separator, "+7,7/s" reads as a thousands separator, i.e. as an income seventy times too big.
        ///
        /// The shop screen's own header keeps GoldLabel above - inside the shop you are reading a balance you are
        /// about to spend, not watching it tick up, and the two are never on screen at the same time (the shop's
        /// dim covers the HUD's canvas).</summary>
        public static string GoldHudLabel(int balance, double incomePerSecond, float sizePercent)
        {
            int percent = Mathf.Clamp(Mathf.RoundToInt(sizePercent), 1, 100);
            string income = incomePerSecond.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{GoldLabel(balance)}\n<size={percent.ToString(CultureInfo.InvariantCulture)}%>+{income}/s</size>";
        }
    }
}
