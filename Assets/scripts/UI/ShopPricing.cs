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
        /// <summary>2.7b step 5b: renamed from FreeLoadout so the name stops lying - true for the ordinary Free
        /// Loadout test mode AND for the pre-live warm-up sandbox (ShopRules.IsFree). Every shop decision already
        /// read the old field, so every purchase, reset and header follows with no per-handler change.</summary>
        public readonly bool IsFree;
        public readonly bool InOwnTerritory;
        public readonly float SecondsSinceCombat;
        public readonly float RequiredOutOfCombatSeconds;
        public readonly int Balance;

        /// <summary>2.7b step 5b: true when IsFree is true ONLY because the match isn't live yet - Free Loadout
        /// itself is off. Distinguishes the header's two free-shop reasons ("Free (warm-up)" vs "Free (test
        /// mode)"); nothing else needs to tell them apart.</summary>
        public readonly bool IsWarmupSandbox;

        public ShopContext(bool isFree, bool isWarmupSandbox, bool inOwnTerritory, float secondsSinceCombat,
                            float requiredOutOfCombatSeconds, int balance)
        {
            IsFree = isFree;
            IsWarmupSandbox = isWarmupSandbox;
            InOwnTerritory = inOwnTerritory;
            SecondsSinceCombat = secondsSinceCombat;
            RequiredOutOfCombatSeconds = requiredOutOfCombatSeconds;
            Balance = balance;
        }

        /// <summary>None outright while IsFree (GameplayConfig.FreeLoadout's own tooltip: "changes anything for
        /// free, anywhere" - or, before live, the warm-up sandbox) - every click handler still calls this rather
        /// than checking either flag itself, so there is exactly one place that decides what a free shop means
        /// for a purchase.</summary>
        public PurchaseBlock Check(int price) =>
            IsFree ? PurchaseBlock.None
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
        /// per-item, shown on the node/card itself instead), or "" once the gate holds. A free shop
        /// gets its own single note here instead of a block reason (Task 2.5b), split 2.7b step 5b
        /// between the warm-up sandbox and Free Loadout's own test mode.</summary>
        public string StatusText() => IsFree ? (IsWarmupSandbox ? "Free (warm-up)" : "Free (test mode)") : ReasonText(Check(0), 0);
    }

    /// <summary>Builds a ShopContext from live player state - the one place LoadoutScreen asks
    /// BuildingManager/Teams/GoldWallet/PlayerHealth the same questions every purchase check needs.</summary>
    public static class ShopPricing
    {
        /// <summary>2.7b step 5b: the one gatherer for ShopRules.IsFree - a missing config fails OPEN to free,
        /// same as PlayerLoadout/AbilityRunner do for their own GameplayConfig reads (a missing reference
        /// degrades to "everything free", never to "everything locked with no explanation"), and a missing
        /// MatchDirector reads as the warm-up (also free) - a test scene with no director, for instance.</summary>
        public static bool IsFreeNow(GameplayConfig config) =>
            ShopRules.IsFree(config == null || config.FreeLoadout,
                MatchDirector.Instance != null && MatchDirector.Instance.IsLive);

        public static ShopContext Build(GameplayConfig config, PlayerHealth health, GoldWallet wallet, Player owner, Vector3 position)
        {
            bool freeLoadout = config == null || config.FreeLoadout;
            bool isFree = IsFreeNow(config);
            bool isWarmupSandbox = isFree && !freeLoadout;
            bool inOwnTerritory = InOwnTerritory(owner, position);
            float secondsSinceCombat = health != null ? health.SecondsSinceCombat : 0f;
            float required = config != null ? config.ShopOutOfCombatSeconds : 0f;
            int balance = wallet != null ? wallet.Balance : 0;
            return new ShopContext(isFree, isWarmupSandbox, inOwnTerritory, secondsSinceCombat, required, balance);
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
