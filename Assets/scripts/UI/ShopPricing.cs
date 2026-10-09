using System.Globalization;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;

namespace Overpower.UI
{
    /// <summary>
    /// The loadout screen's glue between the pure Overpower.Match.ShopRules and this player's live state (position,
    /// team, wallet, out-of-combat timer). Five click handlers (weapon buy/reset, armor upgrade/reset, ability buy)
    /// ask the same three questions (own territory? how long out of combat? balance?) before ShopRules.Check, so
    /// they are gathered once per Refresh here. Nothing here is a RULE; ShopRules owns every decision.
    /// </summary>
    public readonly struct ShopContext
    {
        /// <summary>True for the ordinary Free Loadout test mode AND for the pre-live warm-up sandbox
        /// (ShopRules.IsFree); every purchase, reset and header decision follows this one flag.</summary>
        public readonly bool IsFree;
        public readonly bool InOwnTerritory;
        public readonly float SecondsSinceCombat;
        public readonly float RequiredOutOfCombatSeconds;
        public readonly int Balance;

        /// <summary>True when IsFree is true ONLY because the match isn't live yet (Free Loadout itself is off).
        /// Tells the header's two free-shop reasons apart ("Free (warm-up)" vs "Free (test mode)").</summary>
        public readonly bool IsWarmupSandbox;

        /// <summary>NotInBreak while a live Dominion match is outside the break (the shop takes no picks), else None.</summary>
        public readonly PurchaseBlock Closed;
        /// <summary>What this round opens; null in Conquest and in the warm-up.</summary>
        public readonly DominionShopLimits Limits;
        public bool IsDominion => Limits != null;
        private readonly string dominionFreeText;
        private readonly string closedText;

        public ShopContext(bool isFree, bool isWarmupSandbox, bool inOwnTerritory, float secondsSinceCombat,
                            float requiredOutOfCombatSeconds, int balance,
                            PurchaseBlock closed = PurchaseBlock.None, DominionShopLimits limits = null,
                            string dominionFreeText = "", string closedText = "")
        {
            Closed = closed;
            Limits = limits;
            this.dominionFreeText = dominionFreeText;
            this.closedText = closedText;
            IsFree = isFree;
            IsWarmupSandbox = isWarmupSandbox;
            InOwnTerritory = inOwnTerritory;
            SecondsSinceCombat = secondsSinceCombat;
            RequiredOutOfCombatSeconds = requiredOutOfCombatSeconds;
            Balance = balance;
        }

        /// <summary>None outright while IsFree (Free Loadout, or the pre-live warm-up sandbox). Every click handler
        /// calls this rather than checking a flag itself, so one place decides what a free shop means for a purchase.</summary>
        public PurchaseBlock Check(int price) =>
            Closed != PurchaseBlock.None ? Closed // Dominion outside the break: nothing can be picked, however free it is
            : IsFree ? PurchaseBlock.None
                     : ShopRules.Check(InOwnTerritory, SecondsSinceCombat, RequiredOutOfCombatSeconds, Balance, price);

        /// <summary>What a node/card's price line reads under this gate: "Free" for everything in Dominion (there is no gold), else the usual line.</summary>
        public string PriceLine(int price, PurchaseBlock block = PurchaseBlock.None) =>
            IsDominion ? ShopPricing.PriceLabel(0) : ShopPricing.PriceLine(price, block, Balance);

        public float SecondsUntilOutOfCombat => ShopRules.SecondsUntilOutOfCombat(SecondsSinceCombat, RequiredOutOfCombatSeconds);

        /// <summary>Why a SPECIFIC purchase attempt is blocked, in one place so StatusText's price-less header
        /// question and a refused click's item-specific reason (LoadoutScreen.ShowBlockedReason) agree on the
        /// wording. Price is 0 for the header's question (CannotAfford is per-item, shown on the node/card), the
        /// real price when a click found CannotAfford, so the shortfall ("Need 700 more gold") is the real one.</summary>
        public string ReasonText(PurchaseBlock block, int price)
        {
            switch (block)
            {
                case PurchaseBlock.NotInOwnTerritory: return "Go to a zone your team owns";
                case PurchaseBlock.InCombat: return $"Out of combat in {SecondsUntilOutOfCombat.ToString("0.0", CultureInfo.InvariantCulture)}s";
                case PurchaseBlock.CannotAfford: return $"Need {price - Balance} more gold";
                case PurchaseBlock.NotInBreak: return string.IsNullOrEmpty(closedText) ? "The shop opens in the break" : closedText;
                default: return "";
            }
        }

        /// <summary>The header's status line: why nothing can be bought here right now (checked with no price, so
        /// CannotAfford never fires; that is per-item), or "" once the gate holds. A free shop gets its own note
        /// instead of a block reason, split between the warm-up sandbox and Free Loadout's test mode.</summary>
        public string StatusText() =>
            Closed != PurchaseBlock.None ? ReasonText(Closed, 0)
            : IsDominion ? (string.IsNullOrEmpty(dominionFreeText) ? "Free (break)" : dominionFreeText)
            : IsFree ? (IsWarmupSandbox ? "Free (warm-up)" : "Free (test mode)") : ReasonText(Check(0), 0);
    }

    /// <summary>Builds a ShopContext from live player state - the one place LoadoutScreen asks
    /// BuildingManager/Teams/GoldWallet/PlayerHealth the same questions every purchase check needs.</summary>
    public static class ShopPricing
    {
        /// <summary>The one gatherer for ShopRules.IsFree. A missing config fails OPEN to free, as PlayerLoadout and
        /// AbilityRunner do (never "everything locked with no explanation"); a missing MatchDirector reads as the
        /// warm-up, also free (a test scene with no director).</summary>
        public static bool IsFreeNow(GameplayConfig config) =>
            ShopRules.IsFree(config == null || config.FreeLoadout,
                MatchDirector.Instance != null && MatchDirector.Instance.IsLive,
                DominionMode.IsActive());

        /// <summary>Whether a reset hands out the prefab's starting ultimate (ShopRules.StartingUltimateHandedOut, with the live inputs).</summary>
        public static bool StartingUltimateHandedOut(GameplayConfig config) =>
            ShopRules.StartingUltimateHandedOut(config == null || config.FreeLoadout,
                MatchDirector.Instance != null && MatchDirector.Instance.IsLive,
                DominionMode.IsActive());

        /// <summary>True in a live Dominion match (not the warm-up, where the shop is the free sandbox and the break rule does not apply).</summary>
        public static bool DominionLive() => DominionMode.IsLive();

        /// <summary>Why the shop takes no picks right now (NotInBreak), or None. The stage comes from the room via DominionDirector.</summary>
        public static PurchaseBlock DominionClosed(bool lateJoinerPickOpen)
        {
            DominionDirector director = DominionDirector.Instance;
            DominionStage stage = director != null ? director.Stage : DominionStage.None;
            return DominionShopRules.PickBlock(DominionLive(), stage, lateJoinerPickOpen);
        }

        /// <summary>What the current round opens, or null outside a live Dominion match / without a DominionConfig. The round is the one
        /// coming up during a break (dRnd already names it), the one being played otherwise.</summary>
        public static DominionShopLimits DominionLimits(DominionConfig dominion)
        {
            if (dominion == null || !DominionLive()) return null;
            DominionDirector director = DominionDirector.Instance;
            int round = director != null ? director.Round : 1;
            // Asked every frame while the shop is open: kept per round (and per config), not built anew each time.
            if (cachedLimits == null || cachedLimitsRound != round || cachedLimitsConfig != dominion)
            {
                cachedLimits = new DominionShopLimits(round, dominion.WeaponDepthByRound, dominion.ArmorUpgradesByRound, dominion.LockedTierLabelFormat);
                cachedLimitsRound = round;
                cachedLimitsConfig = dominion;
            }
            return cachedLimits;
        }

        private static DominionShopLimits cachedLimits;
        private static int cachedLimitsRound;
        private static DominionConfig cachedLimitsConfig;

        public static ShopContext Build(GameplayConfig config, PlayerHealth health, GoldWallet wallet, Player owner, Vector3 position, bool isAlive,
                                        bool lateJoinerPickOpen = false, DominionConfig dominion = null, string dominionFreeText = "", string closedText = "",
                                        string lateJoinerFreeText = "")
        {
            bool freeLoadout = config == null || config.FreeLoadout;
            bool isFree = IsFreeNow(config);
            bool isWarmupSandbox = isFree && !freeLoadout && !DominionLive();
            // (D4) While dead the territory gate counts as passed (respawn is at home). The combat gate needs no override: PlayerHealth puts the clock at "out of combat" on death.
            bool inOwnTerritory = ShopRules.EffectiveInOwnTerritory(isAlive, InOwnTerritory(owner, position));
            float secondsSinceCombat = health != null ? health.SecondsSinceCombat : 0f;
            float required = config != null ? config.ShopOutOfCombatSeconds : 0f;
            int balance = wallet != null ? wallet.Balance : 0;
            return new ShopContext(isFree, isWarmupSandbox, inOwnTerritory, secondsSinceCombat, required, balance,
                DominionClosed(lateJoinerPickOpen), DominionLimits(dominion), HeaderFreeText(lateJoinerPickOpen, dominionFreeText, lateJoinerFreeText), closedText);
        }

        /// <summary>The header's free note: the late joiner's own wording while their one pick is open mid-round, the break's otherwise.</summary>
        private static string HeaderFreeText(bool lateJoinerPickOpen, string breakText, string lateJoinerText)
        {
            DominionDirector director = DominionDirector.Instance;
            DominionStage stage = director != null ? director.Stage : DominionStage.None;
            return DominionShopRules.HeaderFreeText(stage, lateJoinerPickOpen, breakText, lateJoinerText);
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

        /// <summary>What a node/card's price line reads: the plain number, or "Free" for anything costing nothing
        /// right now (a 0-cost weapon root, an ability's first free pick via ShopRules.AbilityPrice). Deliberately
        /// independent of Free Loadout: prices stay shown while testing for free, so a designer previews the real economy.</summary>
        public static string PriceLabel(int price) => price > 0 ? price.ToString(CultureInfo.InvariantCulture) : "Free";

        /// <summary>A node/card's price line while the gate specifically CANNOT afford it: "1200 · need 700"
        /// (shortfall = price - balance, as ShopContext.ReasonText). Every OTHER block is already explained by the
        /// header status line, so those read the plain PriceLabel.</summary>
        public static string PriceLine(int price, PurchaseBlock block, int balance) =>
            block == PurchaseBlock.CannotAfford ? $"{PriceLabel(price)} · need {price - balance}" : PriceLabel(price);

        /// <summary>"Gold 1234" in InvariantCulture, matching PlayerHud.UpdateGold, so the HUD and this header never
        /// disagree on a decimal/thousands separator on a non-English Windows locale.</summary>
        public static string GoldLabel(int balance) => $"Gold {balance.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>The HUD's two-line gold readout next to the shop button: the balance on top, this second's income
        /// under it at sizePercent of the balance's size. InvariantCulture on BOTH numbers, pinned by a test: with a
        /// comma decimal separator "+7,7/s" reads as a thousands separator, an income seventy times too big. The shop
        /// header keeps GoldLabel; the two are never on screen together (the shop's dim covers the HUD canvas).</summary>
        public static string GoldHudLabel(int balance, double incomePerSecond, float sizePercent)
        {
            int percent = Mathf.Clamp(Mathf.RoundToInt(sizePercent), 1, 100);
            string income = incomePerSecond.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{GoldLabel(balance)}\n<size={percent.ToString(CultureInfo.InvariantCulture)}%>+{income}/s</size>";
        }
    }
}
