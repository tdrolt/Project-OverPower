using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.Weapons;

namespace Overpower.UI
{
    /// <summary>
    /// The screen Tudor asked for after his first playtest: he could not select an ability at all -
    /// the only way in was the F1 developer panel, which is a design instrument, not something a
    /// player is meant to see. This is the real thing: a weapon upgrade tree, armor upgrades, and
    /// (Task 9b) ability picks, opened with P or a bottom-right "Loadout (P)" button, closed with P,
    /// Esc or its own X. It is also the future shop's shell - Phase 2 adds gold and prices as DATA
    /// (weapon/ability GoldCost fields already exist), not a UI rebuild.
    ///
    /// Built in code, in one file, exactly like PlayerHud and TestRangePanel - see PlayerHud's class
    /// comment for why: every listener sits on the same line as the button it belongs to, so nothing
    /// can end up wired to the wrong control the way a past hand-built hierarchy once was.
    ///
    /// OWNER ONLY, same as PlayerHud and AimConeView: this is a screen you look at and click for
    /// yourself, nobody needs to see another player's. Bails out in Awake for every non-owner copy.
    ///
    /// SORT ORDER: the modal canvas (dim + panel) sits at -5 - above PlayerHud's HUD (-10, so the
    /// loadout screen always reads over health/ability bars) and below both the F1 test range panel
    /// (500 - a designer who opened that on purpose must still see it on top) and MatchUI's win/
    /// lose/waiting panels (the nested "lose win manager" prefab's Canvas, sortingOrder 0, no
    /// override - a match ending must still be visible over an open loadout screen). The always-
    /// visible "Loadout (P)" button lives on its own separate canvas at -10, the same layer as the
    /// HUD it is visually part of - Task 9's brief asked this class to own that button rather than
    /// PlayerHud, but it belongs at HUD depth, not modal depth, so it does not itself sit above the
    /// F1 panel.
    ///
    /// INPUT: reuses PlayerInputRouter's existing Shop action/ShopToggled event (bound to P) rather
    /// than adding a new action - see PlayerInputRouter's ShopSuppressed comment for why ShopToggled
    /// now has its own emit gate separate from every other router event. While open this claims
    /// general tool focus the same way the F1 panel does (PlayerInputRouter.SetToolFocus), now keyed
    /// by owner so the two tools can be open at once without one closing stealing the other's claim.
    ///
    /// ABILITIES AND HOVER (Task 9b): the right column now holds a heading and a wrapping card grid
    /// per ability slot (see BuildAbilitiesUi), in place of the placeholder label Task 9a left there.
    /// Under both columns sits a fixed-height hover-description strip (see BuildScreenCanvas and the
    /// "Hover description" region) that every weapon node and ability card feeds through a HoverRelay
    /// pointer-enter/exit component - name, description and live numbers read straight off the
    /// asset/module at hover time.
    /// </summary>
    public partial class LoadoutScreen : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Colours, sizes and fonts for this screen - the same asset the HUD and aim cone use.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Every weapon in the game and its upgrade-tree parent link. Add a weapon asset with a Parent and it appears in the tree with no code change.")]
        private WeaponCatalogue weapons;

        [SerializeField, Tooltip("Every ability in the game, drawn as cards in the right-hand column. A new ability asset with Id < 900 appears with no code change; ids 901-903 are debug abilities and stay reachable only through the F1 test range panel.")]
        private AbilityCatalogue abilities;

        [SerializeField, Tooltip("Armor tiers asset - the same one the F1 panel reads, so both call the exact same upgrade rule (ArmorLoadoutActions).")]
        private ArmorConfig armorConfig;

        [SerializeField, Tooltip("Match tuning asset - Free Loadout, the sell refund rate, and the shop's own (shorter) out-of-combat timer. The same asset PlayerLoadout and GoldWallet read. Task 2.5b: missing this fails OPEN to Free Loadout (see ShopPricing.Build) rather than silently locking every purchase.")]
        private GameplayConfig gameplayConfig;

        /// <summary>This player's gold, owner-authoritative (GoldWallet's own class comment). Read,
        /// never written directly - every spend/refund goes through TrySpend/Add so the wallet is
        /// the only thing that ever publishes the "gold" Custom Property.</summary>
        private GoldWallet goldWallet;

        /// <summary>What this player has paid per category this match, so a weapon/armor reset can
        /// refund part of it (Task 2.5b Step 7). Lives here rather than a separate component - see
        /// the report's "ledger home" note - because this is the only thing that ever spends
        /// through it, and it resets exactly when a fresh LoadoutScreen does: a new player object,
        /// same as GoldWallet's own balance starts fresh only for a genuinely new player.</summary>
        private readonly PurchaseLedger ledger = new PurchaseLedger();

        // ---- shop telemetry events (Task T4) -----------------------------------------------------
        // Raised at the same five sites this screen already had before telemetry existed - the
        // three TrySpend sites (weapon, armor, ability), the two refund sites (weapon reset, armor
        // reset), and ShowBlockedReason, which every refusal already funnelled through. Nothing here
        // changes what a click actually does; PlayerTelemetry (Task T4) is the only listener.

        // Armor has no item id of its own (unlike a weapon or ability) - only a path (absorb or
        // recharge) and a level reached on that path. TelemetryKeys.ItemId documents this same
        // encoding for whoever reads the log: 100 + level for absorb, 200 + level for recharge, so
        // "absorb reaches 1" and "recharge reaches 1" are never the same number (opus review fix -
        // the level alone could not tell the two paths apart).
        private const int ArmorAbsorbItemBase = 100;
        private const int ArmorRechargeItemBase = 200;

        /// <summary>owner, on a successful purchase: category, the item bought (a weapon or ability
        /// id; for armor, ArmorAbsorbItemBase/ArmorRechargeItemBase + the level reached, see their
        /// own comment), the price actually charged (0 under Free Loadout), the balance right after,
        /// and whether Free Loadout paid for it.</summary>
        public event System.Action<PurchaseCategory, int, int, int, bool> Purchased;

        /// <summary>owner, on a successful weapon/armor reset: category, gold refunded, balance
        /// after. Never raised under Free Loadout (nothing was ever spent to refund) or when the
        /// refund rounds down to 0.</summary>
        public event System.Action<PurchaseCategory, int, int> Refunded;

        /// <summary>owner, on any refused click (weapon, armor, ability, or either reset) - the item
        /// id (-1 for a reset, which buys nothing in particular), the price that was checked, why it
        /// was refused, and the gold shortfall (0 unless the reason is CannotAfford).</summary>
        public event System.Action<int, int, PurchaseBlock, int> PurchaseRefused;

        /// <summary>True only while the LOCAL player's own screen is open - only one instance of this
        /// component ever builds anything (every remote copy bails in Awake), so there is only ever
        /// one writer. Reset in OnDestroy so a player object being torn down can never leave this
        /// stuck true for whatever spawns next.</summary>
        public static bool IsOpen { get; private set; }

        // ---- component refs, read off this same player root -----------------------------------

        private PlayerHealth playerHealth;
        private PlayerLoadout playerLoadout;
        private WeaponFiring weaponFiring;
        private PlayerInputRouter inputRouter;
        private PlayerLifecycle lifecycle;
        private MatchUI matchUI;
        private AbilityRunner abilityRunner;

        // ---- weapon tree ------------------------------------------------------------------------

        private WeaponUpgradeTree tree;

        /// <summary>One weapon node's three visual pieces - see BuildNodeButton. A class, not a
        /// struct, so Refresh can restyle one in place through the dictionary below.</summary>
        private sealed class WeaponNodeUi
        {
            public Button button;
            public Image outer; // The equipped highlight border - transparent except when Equipped.
            public Image inner; // The node's actual fill colour, inset inside outer.
            public TextMeshProUGUI label;
        }

        private readonly Dictionary<int, WeaponNodeUi> weaponNodes = new Dictionary<int, WeaponNodeUi>();

        // ---- abilities --------------------------------------------------------------------------

        /// <summary>One ability card's three visual pieces - same recipe as WeaponNodeUi (an outer
        /// border Image, an inset inner fill Image, a label), but abilities have only two states
        /// (Equipped or not) where weapons have four, so there is no separate styling method - see
        /// RefreshAbilities.</summary>
        private sealed class AbilityCardUi
        {
            public Button button;
            public Image outer;
            public Image inner;
            public TextMeshProUGUI label;
        }

        // Keyed by (slot, id) rather than id alone - unlike weapon ids, ability ids are not unique
        // WITHIN one card set only by construction (AbilityCatalogue enforces global uniqueness
        // already), but the pair is what RefreshAbilities needs to ask "is THIS slot's card THIS
        // slot's equipped id" without a second lookup.
        private readonly Dictionary<(AbilitySlot slot, int id), AbilityCardUi> abilityCards =
            new Dictionary<(AbilitySlot slot, int id), AbilityCardUi>();

        // Mobility (Shift), Equipment (RMB), Ultimate (Space) - the brief's own order, left to right
        // across the movement/utility/panic-button spectrum rather than AbilitySlot's declaration
        // order (which puts Primary - the weapon, not drawn here at all - first).
        private static readonly AbilitySlot[] LoadoutAbilitySlotOrder =
        {
            AbilitySlot.Mobility, AbilitySlot.Equipment, AbilitySlot.Ultimate
        };

        // ---- hover description ------------------------------------------------------------------

        /// <summary>Turns UI pointer enter/exit into a plain callback - added to every weapon node
        /// and ability card so hovering either one can drive the description panel below, without
        /// every node/card wiring its own EventTrigger by hand. A MonoBehaviour because
        /// IPointerEnterHandler/IPointerExitHandler only work on one.</summary>
        private sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public System.Action OnEnter;
            public System.Action OnExit;
            public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
            public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
        }

        private const string HoverHintText = "Hover an item to see what it does.";

        private TextMeshProUGUI hoverNameLabel;
        private TextMeshProUGUI hoverDescriptionLabel;
        private TextMeshProUGUI hoverNumbersLabel;

        // ---- shop header (Task 2.5b) --------------------------------------------------------------

        private TextMeshProUGUI goldLabel;
        private TextMeshProUGUI statusLabel;

        // Task 2.5b review fix 3: cached against the raw gold int / block enum / out-of-combat
        // TENTH-of-a-second, not against a formatted string, so Update()'s per-frame poll (see its
        // own comment) can call RefreshHeader every frame for cheap without building "Gold {gold}"
        // or the status text just to throw the result away when nothing changed - most frames these
        // comparisons are the only work done. lastStatusText still records the last string actually
        // drawn (from either path below) purely so the two paths never redundantly rewrite the same
        // text as one another.
        private bool headerInitialized;
        private int lastDisplayedGold;
        private bool lastDisplayedIsFree;
        private bool lastDisplayedIsWarmupSandbox;
        private PurchaseBlock lastDisplayedBlock;
        private int lastDisplayedTenths;
        private string lastStatusText;

        // Task 2.5b review fix 2: a refused click's own reason, shown in the header status line in
        // place of the ordinary gate status until Loadout Blocked Reason Duration Seconds (UiTheme)
        // runs out - see ShowBlockedReason. Expiry <= 0 means "no override active"; Time.unscaledTime
        // is never <= 0 once the game has been running for any length of time, so this doubles as
        // the "not yet used" sentinel with no separate bool needed. Unscaled (re-review fix, same
        // reasoning as PlayerHud's own bountyToastHideAtTime) so a debug Time.timeScale change
        // cannot freeze this reason on screen forever.
        private string blockedReasonText = "";
        private float blockedReasonExpiryTime = -1f;

        /// <summary>Shown under the Ultimate heading only while that slot is empty ("Buy an
        /// ultimate") - the one ability slot with no card that can ever read Equipped at spawn, so
        /// without this the column would otherwise say nothing about why nothing is highlighted.</summary>
        private TextMeshProUGUI ultimateEmptyLabel;

        // Reset-button labels, kept so Refresh can rewrite their refund preview in place - see
        // RefreshResetLabels. AddButton returns the Button; GetComponentInChildren grabs its label.
        private TextMeshProUGUI resetWeaponLabel;
        private TextMeshProUGUI resetArmorLabel;

        // ---- armor --------------------------------------------------------------------------------

        private TextMeshProUGUI absorbText;
        private TextMeshProUGUI rechargeText;
        private Button absorbButton;
        private Button rechargeButton;

        // ---- screen state / built UI ---------------------------------------------------------------

        /// <summary>The modal canvas (dim + panel) - SetActive(false/true) is the whole show/hide.
        /// Never destroyed once built, unlike PlayerHud's canvas which lives for the player's whole
        /// life anyway; this one just toggles.</summary>
        private GameObject screenRoot;

        /// <summary>The always-visible "Loadout (P)" button - kept so Update() can disable it once
        /// the match is over (Task 9b quality review), matching Open()'s own refusal instead of
        /// leaving a clickable button that silently no-ops.</summary>
        private Button loadoutToggleButton;

        /// <summary>This instance's own open/closed flag. Deliberately separate from the static
        /// IsOpen: IsOpen is what the rest of the game reads, this is what THIS component uses to
        /// know whether it already claimed tool focus, so Close() called twice (e.g. once from Esc
        /// and once from OnDisable during teardown) never double-releases.</summary>
        private bool isOpenLocal;

        // Snapshot of what Refresh() last drew, so Update() (while open) can tell when the weapon
        // or armor changed from OUTSIDE this screen's own clicks - the F1 panel's dropdown/buttons,
        // or a property echo from a remote change - and catch up (Task 9a review finding 2).
        // Abilities need no equivalent: AbilityRunner.SlotChanged already fires for every equip from
        // every source, and Refresh is already subscribed to it.
        private int lastKnownWeaponId = int.MinValue;
        private int lastKnownAbsorbLevel = -1;
        private int lastKnownRechargeLevel = -1;

        // Task 2.5b: the shop gate can flip (the out-of-combat timer running out, walking into your
        // own territory) or the balance can rise (passive territory income) with NOTHING on this
        // screen having been clicked - the same "something changed from OUTSIDE this screen" gap
        // Task 9a's review already found for the weapon/armor poll above, now extended to gold and
        // the gate. A full Refresh() only fires when one of these actually changes; otherwise
        // Update() still calls the cheap RefreshHeader() every frame so the countdown keeps ticking.
        private int lastKnownGold = int.MinValue;
        private bool lastKnownBlocked;

        // One Material instance shared by every text this screen builds - see PlayerHud.ApplyOutline
        // for why sharing beats letting TMP auto-instantiate one per label.
        private Material loadoutTextMaterial;

        private void Awake()
        {
            // Every remote copy of this component stays permanently dormant - see the class comment.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            if (theme == null)
            {
                Debug.LogError($"[LoadoutScreen] {name}: UiTheme is not assigned - the loadout screen cannot be built.");
                enabled = false;
                return;
            }

            playerHealth = GetComponent<PlayerHealth>();
            playerLoadout = GetComponent<PlayerLoadout>();
            weaponFiring = GetComponent<WeaponFiring>();
            inputRouter = GetComponent<PlayerInputRouter>();
            lifecycle = GetComponent<PlayerLifecycle>();
            abilityRunner = GetComponent<AbilityRunner>();
            goldWallet = GetComponent<GoldWallet>();
            // Optional: not every rig this component might run on has one, and there is nothing
            // this screen cannot do without it besides the match-over gate below.
            matchUI = GetComponent<MatchUI>();

            if (playerHealth == null || playerLoadout == null || weaponFiring == null || inputRouter == null || abilityRunner == null)
                Debug.LogError($"[LoadoutScreen] {name}: missing PlayerHealth/PlayerLoadout/WeaponFiring/PlayerInputRouter/AbilityRunner on this player - the loadout screen cannot apply choices.");
            if (weapons == null)
                Debug.LogError($"[LoadoutScreen] {name}: WeaponCatalogue is not assigned - the weapon tree will be empty.");
            if (abilities == null)
                Debug.LogError($"[LoadoutScreen] {name}: Ability Catalogue is not assigned - the ability columns will be empty.");
            if (armorConfig == null)
                Debug.LogError($"[LoadoutScreen] {name}: ArmorConfig is not assigned - armor upgrades will always be refused.");
            if (goldWallet == null)
                Debug.LogError($"[LoadoutScreen] {name}: no GoldWallet on this player - purchases can never spend or refund gold.");
            if (gameplayConfig == null)
                Debug.LogWarning($"[LoadoutScreen] {name}: GameplayConfig is not assigned - the shop gate fails open to Free Loadout (ShopPricing.Build).");

            BuildWeaponTree();
            BuildUi();
            screenRoot.SetActive(false);

            if (lifecycle != null)
                lifecycle.AliveChanged += HandleAliveChanged;
            if (inputRouter != null)
                inputRouter.ShopToggled += Toggle;
            if (abilityRunner != null)
                abilityRunner.SlotChanged += HandleAbilitySlotChanged;
        }

        private void OnDisable()
        {
            // Covers Esc/X/P closing the screen normally AND the GameObject being disabled out from
            // under it (e.g. leaving play mode) - either way the tool-focus claim below must not
            // outlive this component being able to act on it.
            if (isOpenLocal)
                Close();
        }

        private void OnDestroy()
        {
            if (lifecycle != null)
                lifecycle.AliveChanged -= HandleAliveChanged;
            if (inputRouter != null)
                inputRouter.ShopToggled -= Toggle;
            if (abilityRunner != null)
                abilityRunner.SlotChanged -= HandleAbilitySlotChanged;

            // MINE ONLY (Task 9a review, critical): every remote copy of this player also runs
            // OnDestroy - e.g. whenever any OTHER player leaves the room - and every remote copy
            // bailed out of Awake before ever touching IsOpen or claiming focus. Without this guard,
            // a remote player's teardown would still reach the two lines below and clear the STATIC
            // IsOpen (and release a focus claim it never made) out from under whichever OTHER
            // player's screen is the LOCAL one actually open right now.
            //
            // Belt-and-braces alongside OnDisable's own Close() for the owner's own copy - only
            // OnDisable does not run for every teardown path, so this is what actually guarantees
            // the focus claim and IsOpen never outlive the local player's own component.
            if (photonView != null && photonView.IsMine)
            {
                inputRouter?.SetToolFocus(this, false);
                IsOpen = false;
            }

            if (loadoutTextMaterial != null)
                Destroy(loadoutTextMaterial);
        }

        private void Update()
        {
            bool matchOver = matchUI != null && matchUI.MatchOver;

            // The always-visible toggle button must stop offering a loadout once the match is over
            // too (Task 9b quality review) - runs regardless of isOpenLocal below, since the button
            // is visible and clickable whether this screen is open or closed. Previously it stayed
            // interactable and Toggle()/Open() just silently refused.
            if (loadoutToggleButton != null)
                loadoutToggleButton.interactable = !matchOver;

            if (!isOpenLocal)
                return;

            // A raw keyboard poll, not an InputAction, matching TestRangePanel's own reasoning for
            // F1: Esc-closes-a-tool is a tool convention, not a rebindable gameplay control.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            // The match ending must close this screen even though ShopSuppressed deliberately does
            // not gate ShopToggled on it (Task 9a review, finding 3) - MatchUI freezes movement, but
            // nothing told this screen to stop letting a still-living player re-pick a loadout after
            // the result is already decided.
            if (matchOver)
            {
                Close();
                return;
            }

            // Refresh() otherwise only runs on Open and on this screen's OWN clicks (Task 9a review,
            // finding 2) - an equip made elsewhere while the screen is open (the F1 panel's dropdown
            // or armor buttons, or a property echo from a remote change) would sit stale here until
            // something on THIS screen was clicked. Abilities do not need this: AbilityRunner's
            // SlotChanged already fires for every equip from every source, and Refresh is already
            // subscribed to it.
            int weaponId = CurrentWeaponId();
            int absorbLevel = playerHealth != null ? playerHealth.AbsorbLevel : -1;
            int rechargeLevel = playerHealth != null ? playerHealth.RechargeLevel : -1;

            // Task 2.5b: gold can rise from passive territory income, and the gate can flip from
            // walking into your own zone or the combat timer running out - either changes what
            // every node/card should look like, with nothing on THIS screen clicked (same reasoning
            // as the weapon/armor poll above). Built ONCE here (Task 2.5b review fix 3 - this used
            // to be built again inside RefreshHeader every frame) and passed down to whichever of
            // Refresh/RefreshHeader below actually runs.
            int gold = goldWallet != null ? goldWallet.Balance : 0;
            ShopContext ctx = CurrentShopContext();
            bool blocked = ctx.Check(0) != PurchaseBlock.None;

            if (weaponId != lastKnownWeaponId || absorbLevel != lastKnownAbsorbLevel || rechargeLevel != lastKnownRechargeLevel
                || gold != lastKnownGold || blocked != lastKnownBlocked)
                Refresh(ctx);
            else
                RefreshHeader(ctx); // Still cheap even when nothing else changed - see RefreshHeader's own comment.
        }

        private void HandleAliveChanged(bool alive)
        {
            if (!alive && isOpenLocal)
                Close();
        }

        /// <summary>AbilityRunner.SlotChanged fires for every equip, from any source - this
        /// screen's own click, the F1 panel, or a remote property echo - so subscribing it straight
        /// to Refresh (rather than polling like Update() does for the weapon and armor, Task 9a
        /// review finding 2) keeps the ability column live with no extra bookkeeping.</summary>
        private void HandleAbilitySlotChanged(AbilitySlot slot) => Refresh();

        // ============================================================================================
        // Open / close
        // ============================================================================================

        public void Open()
        {
            if (isOpenLocal)
                return;
            if (lifecycle != null && !lifecycle.IsAlive)
                return; // Never open on a corpse - Verification 5 also requires staying closed through death.
            if (matchUI != null && matchUI.MatchOver)
                return; // The match is already decided - see Update()'s own MatchOver check (Task 9a review).

            isOpenLocal = true;
            IsOpen = true;
            screenRoot.SetActive(true);
            inputRouter?.SetToolFocus(this, true);
            // No Cursor.lockState/Cursor.visible call exists anywhere in this project (checked
            // before writing this) - the cursor is always free, so there is nothing to unlock here.
            ClearHover(); // Reopening must not show whatever was last hovered before it closed.
            Refresh();
        }

        public void Close()
        {
            if (!isOpenLocal)
                return;

            isOpenLocal = false;
            IsOpen = false;
            screenRoot.SetActive(false);
            inputRouter?.SetToolFocus(this, false);

            // Re-review fix: a reason shown by ShowBlockedReason must not survive being closed and
            // reopened - closing mid-window used to leave blockedReasonText/blockedReasonExpiryTime
            // armed, so reopening within the window (or even long after, since the underlying gate
            // condition can have changed by then) redrew a stale, possibly now-wrong reason. Clearing
            // headerInitialized too forces RefreshHeader's very-first-call path on the next Open(),
            // which unconditionally rewrites both labels - the same guarantee a fresh LoadoutScreen
            // gets, without which "the normal status hasn't changed since the reason interrupted it"
            // would again skip the write (the exact bug justExpired fixed for the timer-expiry case).
            blockedReasonText = "";
            blockedReasonExpiryTime = -1f;
            headerInitialized = false;
        }

        public void Toggle()
        {
            if (isOpenLocal)
                Close();
            else
                Open();
        }

        /// <summary>2.7b Decision 6: the fresh start at match-live forgets everything this player spent (so
        /// neither reset button can refund warm-up spending once the real economy starts) and closes the
        /// screen if it happened to be open at the live instant - Close() is already a no-op when it isn't.
        /// Owner only; this component already disables itself for every non-owner copy in Awake (see the class
        /// comment), but the guard matches every other ResetForMatchStart on this player.</summary>
        public void ResetForMatchStart()
        {
            if (!photonView.IsMine)
                return;

            ledger.Clear();
            Close();
        }

        /// <summary>Re-reads everything this screen shows from the live player state. Called on
        /// Open and after every click that changes something on THIS screen. Also called from
        /// Update()'s own poll (see its comment, Task 9a review finding 2) while the screen stays
        /// open, since there is no "loadout changed" event this class could subscribe to instead
        /// (WeaponFiring has none) - a weapon or armor change from OUTSIDE this screen (the F1
        /// panel's dropdown/buttons, or a property echo from a remote change) would otherwise sit
        /// stale here until something on THIS screen happened to be clicked. Abilities need no
        /// such poll: AbilityRunner.SlotChanged already fires for every equip from every source,
        /// and Refresh is already subscribed to it directly.</summary>
        private void Refresh() => Refresh(CurrentShopContext());

        /// <summary>Overload taking an already-built ShopContext (Task 2.5b review fix 3) for
        /// callers that already have one this frame (Update()'s own poll) - every other caller
        /// (Open, a click on this screen, AbilityRunner.SlotChanged) goes through the parameterless
        /// Refresh() above, which builds the one ctx this whole pass needs exactly once instead of
        /// each Refresh* method below building its own.</summary>
        private void Refresh(ShopContext ctx)
        {
            RefreshHeader(ctx);
            RefreshWeaponTree(ctx);
            RefreshArmor(ctx);
            RefreshAbilities(ctx);
            RefreshResetLabels();

            // Snapshot what was just drawn, so Update()'s poll (Task 9a review) only calls back in
            // here once something ACTUALLY changes since this Refresh, from any path - Open, a
            // click on this screen, or Update() catching an external change.
            lastKnownWeaponId = CurrentWeaponId();
            lastKnownAbsorbLevel = playerHealth != null ? playerHealth.AbsorbLevel : -1;
            lastKnownRechargeLevel = playerHealth != null ? playerHealth.RechargeLevel : -1;
            lastKnownGold = goldWallet != null ? goldWallet.Balance : 0;
            lastKnownBlocked = ctx.Check(0) != PurchaseBlock.None;
        }

        /// <summary>This player's shop gate and balance right now - built fresh each call (cheap:
        /// a couple of dictionary/property reads, no allocation) rather than cached, so every caller
        /// this frame agrees even if territory or the wallet changed mid-frame. One home for the
        /// BuildingManager/Teams/GoldWallet reads every purchase check needs - see ShopPricing's own
        /// class comment for why this was pulled out of LoadoutScreen itself.</summary>
        private ShopContext CurrentShopContext() =>
            ShopPricing.Build(gameplayConfig, playerHealth, goldWallet, photonView.Owner, transform.position);

        /// <summary>Header row: "Gold 1234" and the status line - a refused click's own reason
        /// (Task 2.5b review fix 2, see ShowBlockedReason) while its timer runs, else the shop
        /// gate's ordinary status (ShopContext.StatusText). Called every frame while open (see
        /// Update()'s own comment) with the SAME ShopContext Update() already built for this frame
        /// (fix 3 - this used to build a second one every frame, and always format "Gold {gold}"
        /// before ever comparing it to what was last drawn). Every comparison below happens on the
        /// raw gold int, the block enum, or the countdown's own tenth-of-a-second BEFORE any string
        /// is built, so a frame where nothing actually changed does no string formatting at all.</summary>
        private void RefreshHeader(ShopContext ctx)
        {
            int gold = goldWallet != null ? goldWallet.Balance : 0;
            if (!headerInitialized || gold != lastDisplayedGold)
            {
                goldLabel.text = ShopPricing.GoldLabel(gold);
                lastDisplayedGold = gold;
            }

            bool reasonActive = blockedReasonExpiryTime > 0f && Time.unscaledTime < blockedReasonExpiryTime;
            if (reasonActive)
            {
                if (blockedReasonText != lastStatusText)
                {
                    statusLabel.text = blockedReasonText;
                    statusLabel.color = theme.overheatWarningColor; // Always a block reason - never muted.
                    lastStatusText = blockedReasonText;
                }
                headerInitialized = true;
                return; // The reason's own timer owns the status line until it expires - see below.
            }
            // The reason just expired (or there never was one) - either way the label may currently
            // show blockedReasonText, which the raw comparisons below know nothing about (they only
            // track the NORMAL status's own last value, and the normal status may genuinely not have
            // changed underneath the reason). "justExpired" forces one write to replace it even when
            // the normal status equals what it was before the reason interrupted it - without this a
            // reason whose gate condition never changed (e.g. still out of territory) would get
            // stuck on screen forever once its timer ran out.
            bool justExpired = blockedReasonExpiryTime > 0f;
            blockedReasonExpiryTime = -1f;

            PurchaseBlock block = ctx.IsFree ? PurchaseBlock.None : ctx.Check(0);
            int tenths = block == PurchaseBlock.InCombat ? Mathf.RoundToInt(ctx.SecondsUntilOutOfCombat * 10f) : 0;

            // 2.7b step 5b: IsWarmupSandbox joins the change check too, so the header re-draws the moment the
            // match goes live even with Free Loadout on ("Free (test mode)" never itself changes IsFree, but
            // IsWarmupSandbox flips false at that instant and the wording underneath it is about to change too -
            // ResetForMatchStart is about to empty the loadout this same frame).
            if (justExpired || !headerInitialized || ctx.IsFree != lastDisplayedIsFree
                || ctx.IsWarmupSandbox != lastDisplayedIsWarmupSandbox || block != lastDisplayedBlock || tenths != lastDisplayedTenths)
            {
                string statusText = ctx.StatusText();
                statusLabel.text = statusText;
                // A free shop's note and "all clear" (empty string) both read as a plain aside; an
                // actual block reason borrows the overheat-warning amber so it reads as the same
                // kind of "something is stopping you" signal the HUD already uses elsewhere.
                statusLabel.color = statusText.Length == 0 || ctx.IsFree ? theme.mutedTextColor : theme.overheatWarningColor;
                lastStatusText = statusText;
                lastDisplayedIsFree = ctx.IsFree;
                lastDisplayedIsWarmupSandbox = ctx.IsWarmupSandbox;
                lastDisplayedBlock = block;
                lastDisplayedTenths = tenths;
            }
            headerInitialized = true;
        }

        /// <summary>Task 2.5b review fix 2: a click refused by the shop gate used to return silently
        /// with nothing shown anywhere - this is what actually tells the player why, by taking over
        /// the header status line for Loadout Blocked Reason Duration Seconds (UiTheme) before it
        /// reverts to the ordinary gate status on its own. Takes the SAME ctx and price the calling
        /// click handler already built its own gate check from, so a CannotAfford reason quotes the
        /// real shortfall for the item that was actually clicked.</summary>
        private void ShowBlockedReason(ShopContext ctx, PurchaseBlock block, int price, int itemId = -1)
        {
            blockedReasonText = ctx.ReasonText(block, price);
            // Unscaled (re-review fix, project convention - see PlayerHud.bountyToastHideAtTime's own
            // comment): a debug Time.timeScale change must not freeze this reason on screen forever.
            blockedReasonExpiryTime = Time.unscaledTime + theme.loadoutBlockedReasonDurationSeconds;
            RefreshHeader(ctx); // Shows it from the same frame as the click, not one frame late.

            // Task T4: `shopBlocked` telemetry. Shortfall only means anything for CannotAfford -
            // every other reason already explains itself via the reason string alone.
            int shortfall = block == PurchaseBlock.CannotAfford ? Mathf.Max(0, price - ctx.Balance) : 0;
            PurchaseRefused?.Invoke(itemId, price, block, shortfall);
        }

        /// <summary>Rewrites the Reset Weapon/Reset Armor buttons' own labels with a refund preview
        /// ("Reset Weapon (+600)") - read straight off the ledger, never spent, so hovering (or just
        /// looking at) the button tells a player what undoing costs them before they click it.</summary>
        private void RefreshResetLabels()
        {
            double rate = gameplayConfig != null ? gameplayConfig.SellRefundRate : 0.5;
            if (resetWeaponLabel != null)
            {
                int refund = GoldMath.Refund(ledger.WeaponSpent, rate);
                resetWeaponLabel.text = refund > 0 ? $"Reset Weapon (+{refund})" : "Reset Weapon";
            }
            if (resetArmorLabel != null)
            {
                int refund = GoldMath.Refund(ledger.ArmorSpent, rate);
                resetArmorLabel.text = refund > 0 ? $"Reset Armor (+{refund})" : "Reset Armor";
            }
        }

        // ============================================================================================
        // Weapon tree - rules from Overpower.Combat.WeaponUpgradeTree (Task 8), drawn here.
        // ============================================================================================

        private void BuildWeaponTree()
        {
            var nodes = new List<(int id, int parentId)>();
            if (weapons != null)
            {
                foreach (WeaponDefinition weapon in weapons.Weapons)
                {
                    if (weapon != null)
                        nodes.Add((weapon.Id, weapon.Parent != null ? weapon.Parent.Id : -1));
                }
            }

            tree = new WeaponUpgradeTree(nodes);
            // Logged once here in Awake, not from Refresh - Refresh runs on every open and every
            // click, and a data problem does not change between those.
            foreach (string problem in tree.Problems)
                Debug.LogError($"[LoadoutScreen] {problem}");
        }

        private void OnWeaponNodeClicked(int weaponId)
        {
            int equipped = CurrentWeaponId();
            // Guards regardless of the button's own interactable flag (only Locked nodes are set
            // non-interactable - see StyleNode) - clicking Equipped or Owned must simply do nothing.
            if (!tree.CanUpgrade(equipped, weaponId))
                return;

            // Task 2.5b review fix 2: a Selectable node stays clickable even while shop-blocked (see
            // StyleNode) - a refused click used to return here with nothing shown anywhere.
            // ShowBlockedReason is what actually tells the player why, in the header status line.
            WeaponDefinition target = weapons.Resolve(weaponId);
            int price = target != null ? target.GoldCost : 0;
            ShopContext ctx = CurrentShopContext();
            bool free = ctx.IsFree;
            int chargedPrice = 0;

            if (!free)
            {
                PurchaseBlock block = ctx.Check(price);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, price, weaponId);
                    return;
                }
                if (goldWallet == null || !goldWallet.TrySpend(price))
                    return;
                ledger.RecordWeapon(price);
                chargedPrice = price;
            }

            playerLoadout?.SetWeapon(weaponId);
            Purchased?.Invoke(PurchaseCategory.Weapon, weaponId, chargedPrice, goldWallet != null ? goldWallet.Balance : 0, free);
            Refresh();
        }

        private void OnResetWeaponClicked()
        {
            if (tree.RootId < 0)
                return;

            // A reset has no price of its own - only the territory/combat gate applies (price 0
            // never trips CannotAfford) - so the same Check(0) the header status line reads decides
            // whether the refund is allowed here too.
            ShopContext ctx = CurrentShopContext();
            if (!ctx.IsFree)
            {
                PurchaseBlock block = ctx.Check(0);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, 0);
                    return;
                }
                if (goldWallet != null && gameplayConfig != null)
                {
                    int refund = ledger.SellWeapon(gameplayConfig.SellRefundRate);
                    goldWallet.Add(refund, GoldSource.Refund);
                    if (refund > 0)
                        Refunded?.Invoke(PurchaseCategory.Weapon, refund, goldWallet.Balance);
                }
            }

            playerLoadout?.SetWeapon(tree.RootId);
            Refresh();
        }

        private int CurrentWeaponId() =>
            weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : tree.RootId;

        private void RefreshWeaponTree(ShopContext ctx)
        {
            if (tree == null || weapons == null)
                return;

            int equipped = CurrentWeaponId();
            foreach (var pair in weaponNodes)
                StyleNode(pair.Value, tree.StateOf(pair.Key, equipped), weapons.Resolve(pair.Key), ctx);
        }

        /// <summary>Task 2.5b: the label's second line now reads "Equipped"/"Owned" for a weapon
        /// already reached, or its price otherwise (ShopPricing.PriceLine) - shown even under Free
        /// Loadout, which only changes whether the price is actually charged, not whether it is
        /// shown (assignment brief). A Selectable node the shop gate refuses right now (out of
        /// territory/combat/gold) gets its OWN look (Loadout Shop Blocked Colour, Task 2.5b review
        /// fix 1 - this used to be painted with Locked Colour, indistinguishable from a genuinely
        /// Locked node) but STAYS interactable: OnWeaponNodeClicked re-checks the same rule and,
        /// on a refusal, shows the specific reason in the header (ShowBlockedReason, fix 2) - a
        /// disabled button would also stop this node being hoverable for the price/description
        /// panel below.</summary>
        private void StyleNode(WeaponNodeUi ui, UpgradeNodeState state, WeaponDefinition def, ShopContext ctx)
        {
            PurchaseBlock block = state == UpgradeNodeState.Selectable && def != null ? ctx.Check(def.GoldCost) : PurchaseBlock.None;
            bool shopBlocked = block != PurchaseBlock.None;

            string suffix = state == UpgradeNodeState.Equipped ? "Equipped"
                          : state == UpgradeNodeState.Owned ? "Owned"
                          : def != null ? ShopPricing.PriceLine(def.GoldCost, block, ctx.Balance) : "";
            // Loadout Price Line Size Percent (UiTheme) on the price/status line only - the node is
            // small (Loadout Node Width x Height) and two full-size lines would not both fit.
            ui.label.text = def != null ? $"{def.DisplayName}\n<size={theme.loadoutPriceLineSizePercent}%>{suffix}</size>" : suffix;

            switch (state)
            {
                case UpgradeNodeState.Equipped:
                    ui.outer.color = theme.highlightColor;
                    ui.inner.color = theme.loadoutSelectableColor;
                    ui.label.color = theme.textColor;
                    ui.button.interactable = true;
                    break;
                case UpgradeNodeState.Selectable:
                    ui.outer.color = Color.clear;
                    ui.inner.color = shopBlocked ? theme.loadoutShopBlockedColor : theme.loadoutSelectableColor;
                    ui.label.color = shopBlocked ? theme.mutedTextColor : theme.textColor;
                    ui.button.interactable = true;
                    break;
                case UpgradeNodeState.Owned:
                    ui.outer.color = Color.clear;
                    ui.inner.color = theme.loadoutOwnedColor;
                    ui.label.color = theme.mutedTextColor;
                    ui.button.interactable = true; // Clickable, but OnWeaponNodeClicked's CanUpgrade guard turns it into a no-op.
                    break;
                default: // Locked
                    ui.outer.color = Color.clear;
                    ui.inner.color = theme.lockedColor;
                    ui.label.color = theme.mutedTextColor;
                    ui.button.interactable = false;
                    break;
            }
        }

        // ============================================================================================
        // Armor - same rule the F1 panel uses, through the shared ArmorLoadoutActions helper.
        // ============================================================================================

        private void OnAbsorbClicked() => TryBuyArmorUpgrade(upgradeAbsorb: true);
        private void OnRechargeClicked() => TryBuyArmorUpgrade(upgradeAbsorb: false);

        /// <summary>Task 2.5b: spends BEFORE upgrading, from the price of the purchase about to be
        /// made (armorConfig.UpgradeCosts[absorbLevel + rechargeLevel] - one shared "how many armor
        /// purchases so far" index, absorb or recharge). The CanUpgrade check mirrors what
        /// RefreshArmor already used to disable the button, kept here too as a second guard - same
        /// belt-and-braces pattern as OnWeaponNodeClicked re-checking CanUpgrade.</summary>
        private void TryBuyArmorUpgrade(bool upgradeAbsorb)
        {
            if (playerHealth == null || armorConfig == null)
                return;

            var path = new ArmorUpgradePath(armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel);
            if (!(upgradeAbsorb ? path.CanUpgradeAbsorb : path.CanUpgradeRecharge))
                return;

            int price = armorConfig.CostFor(playerHealth.AbsorbLevel + playerHealth.RechargeLevel);
            ShopContext ctx = CurrentShopContext();
            bool free = ctx.IsFree;
            int chargedPrice = 0;

            // Task T4: armor has no item id of its own (unlike a weapon or ability), only a path
            // (absorb/recharge) and a level on that path - encoded per TelemetryKeys.ItemId's own
            // doc comment (opus review fix: the level alone, with no path, could not tell an absorb
            // upgrade apart from a recharge one that happened to reach the same level).
            int prospectiveLevel = (upgradeAbsorb ? playerHealth.AbsorbLevel : playerHealth.RechargeLevel) + 1;
            int prospectiveItem = (upgradeAbsorb ? ArmorAbsorbItemBase : ArmorRechargeItemBase) + prospectiveLevel;

            if (!free)
            {
                PurchaseBlock block = ctx.Check(price);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, price, prospectiveItem);
                    return;
                }
                if (goldWallet == null || !goldWallet.TrySpend(price))
                    return;
                ledger.RecordArmor(price);
                chargedPrice = price;
            }

            ArmorLoadoutActions.TryUpgrade(playerHealth, playerLoadout, armorConfig, upgradeAbsorb);

            // Read AFTER TryUpgrade so it reflects what was actually bought, rather than trusting
            // the prospective level computed above went through exactly as predicted.
            int newLevel = upgradeAbsorb ? playerHealth.AbsorbLevel : playerHealth.RechargeLevel;
            int purchasedItem = (upgradeAbsorb ? ArmorAbsorbItemBase : ArmorRechargeItemBase) + newLevel;
            Purchased?.Invoke(PurchaseCategory.Armor, purchasedItem, chargedPrice, goldWallet != null ? goldWallet.Balance : 0, free);
            Refresh();
        }

        private void OnResetArmorClicked()
        {
            // Same "no price of its own, only the gate applies" reasoning as OnResetWeaponClicked.
            ShopContext ctx = CurrentShopContext();
            if (!ctx.IsFree)
            {
                PurchaseBlock block = ctx.Check(0);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, 0);
                    return;
                }
                if (goldWallet != null && gameplayConfig != null)
                {
                    int refund = ledger.SellArmor(gameplayConfig.SellRefundRate);
                    goldWallet.Add(refund, GoldSource.Refund);
                    if (refund > 0)
                        Refunded?.Invoke(PurchaseCategory.Armor, refund, goldWallet.Balance);
                }
            }

            ArmorLoadoutActions.Reset(playerLoadout);
            Refresh();
        }

        /// <summary>Refused-upgrade choice: DISABLE the + button rather than a muted note, computed
        /// up front from the same ArmorUpgradePath rule TryUpgrade itself would apply, so a click
        /// that would be refused is never even offered - unlike the F1 panel, whose console log is a
        /// designer convenience this player-facing screen does not need. Task 2.5b adds the next
        /// purchase's price (ShopPricing.PriceLine - a CannotAfford shortfall included, Task 2.5b
        /// review fix 1) next to whichever path can still be bought, and mutes both rows' text (not
        /// the + buttons themselves, which the CanUpgrade check above already governs) while
        /// shop-blocked, matching the weapon tree's own "still visible, reads as unavailable" look.</summary>
        private void RefreshArmor(ShopContext ctx)
        {
            if (playerHealth == null || armorConfig == null)
                return;

            int absorbMax = Mathf.Max(0, armorConfig.AbsorbLevelCount - 1);
            int rechargeMax = Mathf.Max(0, armorConfig.RechargeLevelCount - 1);

            var path = new ArmorUpgradePath(armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel);
            int nextPrice = armorConfig.CostFor(playerHealth.AbsorbLevel + playerHealth.RechargeLevel);
            PurchaseBlock block = ctx.Check(nextPrice);
            bool gateBlocked = block != PurchaseBlock.None;
            string priceLine = ShopPricing.PriceLine(nextPrice, block, ctx.Balance);

            absorbText.text = path.CanUpgradeAbsorb
                ? $"Absorb {playerHealth.AbsorbLevel}/{absorbMax} ({priceLine})"
                : $"Absorb {playerHealth.AbsorbLevel}/{absorbMax}";
            rechargeText.text = path.CanUpgradeRecharge
                ? $"Recharge {playerHealth.RechargeLevel}/{rechargeMax} ({priceLine})"
                : $"Recharge {playerHealth.RechargeLevel}/{rechargeMax}";
            absorbText.color = path.CanUpgradeAbsorb && gateBlocked ? theme.mutedTextColor : theme.textColor;
            rechargeText.color = path.CanUpgradeRecharge && gateBlocked ? theme.mutedTextColor : theme.textColor;

            absorbButton.interactable = path.CanUpgradeAbsorb;
            rechargeButton.interactable = path.CanUpgradeRecharge;
        }

        // ============================================================================================
        // Abilities (right column) - Mobility, Equipment, Ultimate, each a heading and a wrapping
        // grid of cards straight from the catalogue. No upgrade tree here: any non-debug ability in
        // the right slot is pickable any time, so unlike weapons there is no Owned/Locked state.
        // ============================================================================================

        private void OnAbilityCardClicked(AbilitySlot slot, int abilityId)
        {
            if (playerLoadout == null || abilityRunner == null)
                return;
            int equippedId = abilityRunner.EquippedId(slot);
            if (equippedId == abilityId)
                return; // Already equipped - same no-op-on-self-click guard as OnWeaponNodeClicked.

            // Task 2.5b: ShopRules.AbilityPrice reads 0 for a first pick into an empty Mobility/
            // Equipment slot (the free starting kit - Task 2.5a leaves those slots empty on the
            // prefab) and the card's real GoldCost otherwise; the Ultimate slot is never free.
            AbilityDefinition def = abilities != null ? abilities.Resolve(abilityId) : null;
            int goldCost = def != null ? def.GoldCost : 0;
            bool slotIsEmpty = equippedId == LoadoutProperties.Empty;
            int price = ShopRules.AbilityPrice(slotIsEmpty, slot, goldCost);
            ShopContext ctx = CurrentShopContext();
            bool free = ctx.IsFree;
            int chargedPrice = 0;

            if (!free)
            {
                PurchaseBlock block = ctx.Check(price);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, price, abilityId);
                    return;
                }
                // No ledger entry: abilities have no sell-back path (GDD silent on it), and a free
                // first pick has nothing paid to refund anyway.
                if (price > 0 && (goldWallet == null || !goldWallet.TrySpend(price)))
                    return;
                chargedPrice = price;
            }

            playerLoadout.SetAbility(slot, abilityId);
            Purchased?.Invoke(CategoryFor(slot), abilityId, chargedPrice, goldWallet != null ? goldWallet.Balance : 0, free);
            Refresh();
        }

        /// <summary>Task T4: this screen's own three ability slots, as the shared PurchaseCategory
        /// telemetry uses. Primary never reaches here (this screen never builds a card for it - see
        /// LoadoutAbilitySlotOrder), so it has no real mapping; Equipment is an arbitrary but
        /// harmless fallback rather than throwing.</summary>
        private static PurchaseCategory CategoryFor(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Mobility: return PurchaseCategory.Mobility;
                case AbilitySlot.Ultimate: return PurchaseCategory.Ultimate;
                default: return PurchaseCategory.Equipment;
            }
        }

        /// <summary>Equipped gets the weapon tree's own Equipped look (highlight border) and its
        /// label reads "Equipped"; every other card shows its price (ShopPricing.PriceLine of
        /// ShopRules.AbilityPrice - "Free" for a first pick into an empty Mobility/Equipment slot,
        /// or a CannotAfford shortfall) and, while shop-blocked, its OWN look (Loadout Shop Blocked
        /// Colour, Task 2.5b review fix 1) rather than Locked Colour - see StyleNode's own comment
        /// for why this stays interactable rather than disabled.</summary>
        private void RefreshAbilities(ShopContext ctx)
        {
            if (abilityRunner == null || abilities == null)
                return;

            foreach (var pair in abilityCards)
            {
                AbilitySlot slot = pair.Key.slot;
                int cardId = pair.Key.id;
                int equippedId = abilityRunner.EquippedId(slot);
                bool equipped = equippedId == cardId;

                AbilityDefinition def = abilities.Resolve(cardId);
                int goldCost = def != null ? def.GoldCost : 0;
                int price = equipped ? 0 : ShopRules.AbilityPrice(equippedId == LoadoutProperties.Empty, slot, goldCost);
                PurchaseBlock block = equipped ? PurchaseBlock.None : ctx.Check(price);
                bool shopBlocked = block != PurchaseBlock.None;

                AbilityCardUi ui = pair.Value;
                string suffix = equipped ? "Equipped" : ShopPricing.PriceLine(price, block, ctx.Balance);
                ui.label.text = def != null ? $"{def.DisplayName}\n<size={theme.loadoutPriceLineSizePercent}%>{suffix}</size>" : "";
                ui.outer.color = equipped ? theme.highlightColor : Color.clear;
                ui.inner.color = shopBlocked ? theme.loadoutShopBlockedColor : theme.loadoutSelectableColor;
                ui.label.color = shopBlocked ? theme.mutedTextColor : theme.textColor;
            }

            if (ultimateEmptyLabel != null)
                ultimateEmptyLabel.text = abilityRunner.EquippedId(AbilitySlot.Ultimate) == LoadoutProperties.Empty ? "Buy an ultimate" : "";
        }

        // ============================================================================================
        // Hover description - one panel at the bottom of the screen, fed by whichever weapon node
        // or ability card the pointer is currently over (HoverRelay above). Numbers are always read
        // live off the asset/module at hover time, never typed text, so a designer retuning a
        // weapon or ability never has to remember to also update a description here.
        // ============================================================================================

        private void ClearHover()
        {
            hoverNameLabel.text = "";
            hoverDescriptionLabel.text = HoverHintText;
            hoverNumbersLabel.text = "";
        }

        private void ShowWeaponHover(WeaponDefinition def)
        {
            if (def == null)
            {
                ClearHover();
                return;
            }

            hoverNameLabel.text = def.DisplayName;
            hoverDescriptionLabel.text = def.Description ?? "";
            hoverNumbersLabel.text = WeaponNumbersText(def);
        }

        private static string WeaponNumbersText(WeaponDefinition def)
        {
            var sb = new StringBuilder();
            sb.Append(def.ProjectilesPerShot > 1
                ? $"Damage {Compact(def.Damage)} x{def.ProjectilesPerShot} ({Compact(def.Damage * def.ProjectilesPerShot)} total)"
                : $"Damage {Compact(def.Damage)}");

            float shotsPerSecond = def.FireInterval > 0f ? 1f / def.FireInterval : 0f;
            sb.Append($"\nFire interval {Compact(def.FireInterval)}s ({Compact(shotsPerSecond)}/s) · Range {Compact(def.MaxRange)}m");
            sb.Append($"\nOverheat {Compact(def.OverheatPerShot)}/shot");
            if (def.CanCharge)
                sb.Append(" · hold to charge");
            // Task 11b: the three lasers now wind up before they fire - worth a player reading this
            // before they equip one, the same way "hold to charge" already is above.
            if (def.WindupSeconds > 0f)
                sb.Append($" · {Compact(def.WindupSeconds)}s wind-up");

            return sb.ToString();
        }

        private void ShowAbilityHover(AbilityDefinition def)
        {
            if (def == null)
            {
                ClearHover();
                return;
            }

            hoverNameLabel.text = def.DisplayName;
            hoverDescriptionLabel.text = def.Description ?? "";
            hoverNumbersLabel.text = AbilityNumbersText(def);
        }

        /// <summary>The module prefab carries an ability's only numbers (AbilityDefinition itself is
        /// deliberately thin - see its own class comment), read through
        /// AbilityModule.ConfiguredCooldownSeconds/ConfiguredCharges rather than
        /// ChargesAvailable/RechargeProgress: those read a live runtime ChargePool, which is null on
        /// a prefab asset that was never Bind-ed to a player.</summary>
        private static string AbilityNumbersText(AbilityDefinition def)
        {
            AbilityModule prefabModule = def.ModulePrefab != null ? def.ModulePrefab.GetComponent<AbilityModule>() : null;
            if (prefabModule == null)
                return "";

            // Ultimates have 0s cooldown / 1 charge by design (AbilityModule's own defaults) -
            // readiness instead comes from the shared UltimateCharge meter (kills, assists, damage
            // dealt/taken), so "cooldown 0s" here would flatly misdescribe how they work.
            if (def.Slot == AbilitySlot.Ultimate)
                return "Charges from kills, assists, and damage dealt or taken.";

            if (prefabModule.ConfiguredCharges <= 0)
                return "No cooldown.";
            if (prefabModule.ConfiguredCharges == 1)
                return $"{Compact(prefabModule.ConfiguredCooldownSeconds)}s cooldown";

            return $"{prefabModule.ConfiguredCharges} charges, {Compact(prefabModule.ConfiguredCooldownSeconds)}s each";
        }

        /// <summary>Trims a float to at most two decimals and drops a trailing ".00" - so this panel
        /// shows "5" or "3.13", never "5.000000". CultureInfo.InvariantCulture on purpose (Task 9b
        /// quality review): the default "current culture" format uses a comma decimal separator on
        /// nl/ro Windows and others, which would render "3,13" here and, worse, inside a string
        /// already built with " · " and ", " separators of its own.</summary>
        private static string Compact(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
