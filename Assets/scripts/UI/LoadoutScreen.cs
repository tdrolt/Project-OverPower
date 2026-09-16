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
    public class LoadoutScreen : MonoBehaviourPun
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
        private bool lastDisplayedFreeLoadout;
        private PurchaseBlock lastDisplayedBlock;
        private int lastDisplayedTenths;
        private string lastStatusText;

        // Task 2.5b review fix 2: a refused click's own reason, shown in the header status line in
        // place of the ordinary gate status until Loadout Blocked Reason Duration Seconds (UiTheme)
        // runs out - see ShowBlockedReason. Expiry <= 0 means "no override active"; Time.time is
        // never <= 0 once the game has been running for any length of time, so this doubles as the
        // "not yet used" sentinel with no separate bool needed.
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
        }

        public void Toggle()
        {
            if (isOpenLocal)
                Close();
            else
                Open();
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

            bool reasonActive = blockedReasonExpiryTime > 0f && Time.time < blockedReasonExpiryTime;
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

            PurchaseBlock block = ctx.FreeLoadout ? PurchaseBlock.None : ctx.Check(0);
            int tenths = block == PurchaseBlock.InCombat ? Mathf.RoundToInt(ctx.SecondsUntilOutOfCombat * 10f) : 0;

            if (justExpired || !headerInitialized || ctx.FreeLoadout != lastDisplayedFreeLoadout || block != lastDisplayedBlock || tenths != lastDisplayedTenths)
            {
                string statusText = ctx.StatusText();
                statusLabel.text = statusText;
                // Free Loadout's note and "all clear" (empty string) both read as a plain aside;
                // an actual block reason borrows the overheat-warning amber so it reads as the same
                // kind of "something is stopping you" signal the HUD already uses elsewhere.
                statusLabel.color = statusText.Length == 0 || ctx.FreeLoadout ? theme.mutedTextColor : theme.overheatWarningColor;
                lastStatusText = statusText;
                lastDisplayedFreeLoadout = ctx.FreeLoadout;
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
        private void ShowBlockedReason(ShopContext ctx, PurchaseBlock block, int price)
        {
            blockedReasonText = ctx.ReasonText(block, price);
            blockedReasonExpiryTime = Time.time + theme.loadoutBlockedReasonDurationSeconds;
            RefreshHeader(ctx); // Shows it from the same frame as the click, not one frame late.
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

        /// <summary>Root centred on its own row; its direct children in a row beneath it (id order);
        /// each of THOSE children's own children stacked vertically beneath it, recursively - see
        /// BuildDescendantColumn. Today's data is exactly three tiers with two children per branch
        /// (Baseline -> {Rocket, Burst, SMG, Laser} -> two leaves each), but nothing here assumes
        /// that shape: a branch with one child gets a column one row tall, a branch with three gets
        /// one three rows tall, and a fourth tier (a leaf gaining its own child) simply stacks one
        /// row further down the same column - a designer adding weapon assets never needs a layout
        /// change here, only a new asset with the right Parent.</summary>
        private void BuildWeaponTreeUi(Transform leftColumn)
        {
            weaponNodes.Clear();
            if (weapons == null || tree.RootId < 0)
                return; // BuildWeaponTree already logged why (no catalogue, or no rootless weapon).

            WeaponDefinition rootDef = weapons.Resolve(tree.RootId);
            if (rootDef == null)
                return;

            GameObject rootRow = new GameObject("Weapon Tree Root", typeof(RectTransform));
            rootRow.transform.SetParent(leftColumn, false);
            HorizontalLayoutGroup rootLayout = rootRow.AddComponent<HorizontalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.MiddleCenter;
            rootLayout.childControlWidth = rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = rootLayout.childForceExpandHeight = false;
            weaponNodes[rootDef.Id] = BuildNodeButton(rootRow.transform, rootDef);

            GameObject branchesRow = new GameObject("Weapon Tree Branches", typeof(RectTransform));
            branchesRow.transform.SetParent(leftColumn, false);
            HorizontalLayoutGroup branchesLayout = branchesRow.AddComponent<HorizontalLayoutGroup>();
            branchesLayout.spacing = theme.loadoutNodeSpacing;
            branchesLayout.childAlignment = TextAnchor.UpperCenter;
            branchesLayout.childControlWidth = branchesLayout.childControlHeight = true;
            branchesLayout.childForceExpandWidth = branchesLayout.childForceExpandHeight = false;

            foreach (int childId in tree.ChildrenOf(rootDef.Id))
                BuildDescendantColumn(branchesRow.transform, childId);
        }

        /// <summary>One branch's own vertical column: its node, then a nested column per child,
        /// recursively - see BuildWeaponTreeUi's comment for why this copes with any child count at
        /// any depth with no code change.</summary>
        private void BuildDescendantColumn(Transform parent, int weaponId)
        {
            WeaponDefinition def = weapons.Resolve(weaponId);
            if (def == null)
                return;

            GameObject column = new GameObject($"Weapon Branch {weaponId}", typeof(RectTransform));
            column.transform.SetParent(parent, false);
            VerticalLayoutGroup layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = theme.loadoutNodeSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            weaponNodes[weaponId] = BuildNodeButton(column.transform, def);

            foreach (int childId in tree.ChildrenOf(weaponId))
                BuildDescendantColumn(column.transform, childId);
        }

        /// <summary>One weapon node: an outer Image (transparent except when Equipped, where it
        /// becomes the highlight border), an inner Image inset by Loadout Equipped Border Width
        /// holding the real state colour, and the weapon's short display name on top. Two Images
        /// rather than a UI Outline effect component - a solid Image duplicated at a shadow-style
        /// offset does not read cleanly as a border on a filled rectangle, while an inset inner
        /// Image always reads as a clean frame regardless of node size.</summary>
        private WeaponNodeUi BuildNodeButton(Transform parent, WeaponDefinition def)
        {
            GameObject go = new GameObject($"Weapon Node {def.Id}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = theme.loadoutNodeWidth;
            le.preferredHeight = theme.loadoutNodeHeight;

            Image outer = go.AddComponent<Image>();
            outer.color = Color.clear;
            outer.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            // Refresh drives every colour on this node by hand from the tree's own rules - Unity's
            // built-in transition tint would fight that on every hover/click.
            button.transition = Selectable.Transition.None;
            button.targetGraphic = outer;
            // Fix 2 (Playtest polish review): a code-built button keeps Unity's default Automatic
            // navigation, so clicking it SELECTS it, and the scene's Input System UI module maps
            // Enter to Submit on whatever is selected. Chat also opens on Enter (chatmanager.cs) -
            // without this, pressing Enter to open chat right after clicking a node quietly
            // re-clicked that node instead. None on every button this screen builds.
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject innerGo = new GameObject("Fill", typeof(RectTransform));
            innerGo.transform.SetParent(go.transform, false);
            RectTransform innerRt = innerGo.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            float b = theme.loadoutEquippedBorderWidth;
            innerRt.offsetMin = new Vector2(b, b);
            innerRt.offsetMax = new Vector2(-b, -b);
            Image inner = innerGo.AddComponent<Image>();
            inner.raycastTarget = false;

            TextMeshProUGUI label = AddLabel(innerGo.transform, def.DisplayName, theme.bodyTextSize, FontStyles.Normal);
            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            int weaponId = def.Id; // Captured per node - the field itself would be the last weapon iterated by click time.
            button.onClick.AddListener(() => OnWeaponNodeClicked(weaponId));

            HoverRelay hover = go.AddComponent<HoverRelay>();
            hover.OnEnter = () => ShowWeaponHover(def);
            hover.OnExit = ClearHover;

            return new WeaponNodeUi { button = button, outer = outer, inner = inner, label = label };
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

            if (!ctx.FreeLoadout)
            {
                PurchaseBlock block = ctx.Check(price);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, price);
                    return;
                }
                if (goldWallet == null || !goldWallet.TrySpend(price))
                    return;
                ledger.RecordWeapon(price);
            }

            playerLoadout?.SetWeapon(weaponId);
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
            if (!ctx.FreeLoadout)
            {
                PurchaseBlock block = ctx.Check(0);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, 0);
                    return;
                }
                if (goldWallet != null && gameplayConfig != null)
                    goldWallet.Add(ledger.SellWeapon(gameplayConfig.SellRefundRate));
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

        private void BuildArmorSection(Transform leftColumn)
        {
            // Extra breathing room above "Armor" (Task 9a review, 616x576 capture): the ordinary
            // item spacing between this and the Reset Weapon button above it read as the heading
            // crowding the button. An invisible spacer rather than padding on the header itself, so
            // only the gap ABOVE the heading grows, not the gap below it too.
            GameObject armorGap = new GameObject("Armor Section Gap", typeof(RectTransform));
            armorGap.transform.SetParent(leftColumn, false);
            LayoutElement armorGapLe = armorGap.AddComponent<LayoutElement>();
            armorGapLe.preferredHeight = theme.loadoutSectionGap;
            armorGapLe.minHeight = theme.loadoutSectionGap;

            AddSectionHeader(leftColumn, "Armor");

            absorbText = BuildArmorRow(leftColumn, out absorbButton, OnAbsorbClicked);
            rechargeText = BuildArmorRow(leftColumn, out rechargeButton, OnRechargeClicked);

            Button resetArmorButton = AddButton(leftColumn, "Reset Armor", OnResetArmorClicked, theme.loadoutSmallButtonWidth, theme.loadoutSmallButtonHeight);
            resetArmorLabel = resetArmorButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        private TextMeshProUGUI BuildArmorRow(Transform parent, out Button plusButton, UnityEngine.Events.UnityAction onClick)
        {
            GameObject row = new GameObject("Armor Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.loadoutNodeSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            TextMeshProUGUI label = AddLabel(row.transform, "", theme.bodyTextSize, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement labelLe = label.gameObject.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f; // Takes whatever width the fixed-size + button below does not.

            plusButton = AddButton(row.transform, "+", onClick, theme.loadoutStepperButtonSize, theme.loadoutStepperButtonSize, theme.loadoutStepperFontSize);

            return label;
        }

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

            if (!ctx.FreeLoadout)
            {
                PurchaseBlock block = ctx.Check(price);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, price);
                    return;
                }
                if (goldWallet == null || !goldWallet.TrySpend(price))
                    return;
                ledger.RecordArmor(price);
            }

            ArmorLoadoutActions.TryUpgrade(playerHealth, playerLoadout, armorConfig, upgradeAbsorb);
            Refresh();
        }

        private void OnResetArmorClicked()
        {
            // Same "no price of its own, only the gate applies" reasoning as OnResetWeaponClicked.
            ShopContext ctx = CurrentShopContext();
            if (!ctx.FreeLoadout)
            {
                PurchaseBlock block = ctx.Check(0);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, 0);
                    return;
                }
                if (goldWallet != null && gameplayConfig != null)
                    goldWallet.Add(ledger.SellArmor(gameplayConfig.SellRefundRate));
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

        private void BuildAbilitiesUi(Transform rightColumn)
        {
            abilityCards.Clear();
            if (abilities == null)
                return; // Awake already logged why.

            foreach (AbilitySlot slot in LoadoutAbilitySlotOrder)
            {
                AddSectionHeader(rightColumn, SlotHeading(slot));

                // Task 2.5b: the Ultimate slot is the one slot that can read Equipped on NO card at
                // all (starts empty when the economy is on - Task 2.5a) - without this, an empty
                // ultimate column would say nothing at all about why nothing is highlighted.
                if (slot == AbilitySlot.Ultimate)
                {
                    ultimateEmptyLabel = AddLabel(rightColumn, "", theme.smallTextSize, FontStyles.Normal);
                    ultimateEmptyLabel.alignment = TextAlignmentOptions.MidlineLeft;
                    ultimateEmptyLabel.color = theme.overheatWarningColor;
                }

                List<AbilityDefinition> slotAbilities = abilities.ForSlot(slot);
                slotAbilities.RemoveAll(a => a == null || a.Id >= 900); // Debug abilities stay in F1 only.
                slotAbilities.Sort((a, b) => a.Id.CompareTo(b.Id));

                GameObject grid = new GameObject($"{slot} Ability Grid", typeof(RectTransform));
                grid.transform.SetParent(rightColumn, false);
                // Same "pin only the width, let the group compute its own height" trick BuildColumn
                // uses - a GridLayoutGroup needs its own rect width already resolved before it can
                // work out how many cards fit per row, so this cannot be left for the outer
                // VerticalLayoutGroup to guess from the (not yet laid out) cards inside it.
                LayoutElement gridLe = grid.AddComponent<LayoutElement>();
                gridLe.preferredWidth = theme.loadoutRightColumnWidth;
                GridLayoutGroup gridLayout = grid.AddComponent<GridLayoutGroup>();
                gridLayout.cellSize = new Vector2(theme.loadoutNodeWidth, theme.loadoutNodeHeight);
                gridLayout.spacing = new Vector2(theme.loadoutNodeSpacing, theme.loadoutNodeSpacing);
                gridLayout.childAlignment = TextAnchor.UpperLeft;
                gridLayout.constraint = GridLayoutGroup.Constraint.Flexible; // Wraps to a new row once Loadout Right Column Width runs out.

                foreach (AbilityDefinition def in slotAbilities)
                    abilityCards[(slot, def.Id)] = BuildAbilityCard(grid.transform, def);
            }
        }

        /// <summary>"Mobility — Shift" etc - the brief's own wording, RMB/Space/Shift rather than
        /// AbilitySlot's own doc-comment phrasing ("Right mouse button") so the heading stays one
        /// short line at Small Text Size.</summary>
        private static string SlotHeading(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Mobility: return "Mobility — Shift";
                case AbilitySlot.Equipment: return "Equipment — RMB";
                case AbilitySlot.Ultimate: return "Ultimate — Space";
                default: return slot.ToString(); // Primary never reaches here - ForSlot(Primary) is never called.
            }
        }

        /// <summary>One ability card - same three-Image recipe as BuildNodeButton (outer border,
        /// inset inner fill, label on top), sized to GridLayoutGroup's own cell rather than a
        /// LayoutElement: the grid sets every child's size directly and ignores a child's own
        /// layout element entirely, unlike the Horizontal/VerticalLayoutGroups the weapon tree
        /// uses.</summary>
        private AbilityCardUi BuildAbilityCard(Transform parent, AbilityDefinition def)
        {
            GameObject go = new GameObject($"Ability Card {def.Id}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image outer = go.AddComponent<Image>();
            outer.color = Color.clear;
            outer.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None; // Refresh drives every colour by hand - see StyleNode's own comment.
            button.targetGraphic = outer;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // See BuildNodeButton's comment (fix 2).

            GameObject innerGo = new GameObject("Fill", typeof(RectTransform));
            innerGo.transform.SetParent(go.transform, false);
            RectTransform innerRt = innerGo.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            float b = theme.loadoutEquippedBorderWidth;
            innerRt.offsetMin = new Vector2(b, b);
            innerRt.offsetMax = new Vector2(-b, -b);
            Image inner = innerGo.AddComponent<Image>();
            inner.raycastTarget = false;

            TextMeshProUGUI label = AddLabel(innerGo.transform, def.DisplayName, theme.smallTextSize, FontStyles.Normal);
            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            AbilitySlot slot = def.Slot;
            int abilityId = def.Id;
            button.onClick.AddListener(() => OnAbilityCardClicked(slot, abilityId));

            HoverRelay hover = go.AddComponent<HoverRelay>();
            hover.OnEnter = () => ShowAbilityHover(def);
            hover.OnExit = ClearHover;

            return new AbilityCardUi { button = button, outer = outer, inner = inner, label = label };
        }

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

            if (!ctx.FreeLoadout)
            {
                PurchaseBlock block = ctx.Check(price);
                if (block != PurchaseBlock.None)
                {
                    ShowBlockedReason(ctx, block, price);
                    return;
                }
                // No ledger entry: abilities have no sell-back path (GDD silent on it), and a free
                // first pick has nothing paid to refund anyway.
                if (price > 0 && (goldWallet == null || !goldWallet.TrySpend(price)))
                    return;
            }

            playerLoadout.SetAbility(slot, abilityId);
            Refresh();
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

        // ============================================================================================
        // UI construction
        // ============================================================================================

        private void BuildUi()
        {
            // Once for both canvases below (Task 9a review, finding 4) - each used to call this
            // itself, which was harmless (EnsureEventSystem no-ops once one exists) but redundant.
            EnsureEventSystem();
            BuildScreenCanvas();
            BuildToggleButtonCanvas();
        }

        private void BuildScreenCanvas()
        {
            GameObject canvasGo = new GameObject("Loadout Screen Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -5; // See the class comment's SORT ORDER paragraph.
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            canvasGo.AddComponent<GraphicRaycaster>();
            screenRoot = canvasGo;

            GameObject dim = new GameObject("Dim", typeof(RectTransform));
            dim.transform.SetParent(canvasGo.transform, false);
            RectTransform dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            Image dimImage = dim.AddComponent<Image>();
            dimImage.color = theme.loadoutDimColor;
            dimImage.raycastTarget = true; // Blocks clicks from reaching anything behind the modal.

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            // Anchored to the TOP, not dead-centre: the panel's height grows with the weapon tree
            // and (Task 9b) the ability column, and a centred panel grew down far enough at
            // 1920x1080 to overlap the HUD sitting at the bottom of the screen. Top-anchoring keeps
            // that clearance regardless of how tall the content gets.
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 1f);
            panelRt.pivot = new Vector2(0.5f, 1f);
            panelRt.anchoredPosition = new Vector2(0f, -theme.loadoutPanelTopMargin);
            Image panelBackground = panel.AddComponent<Image>();
            // Its OWN colour, not the HUD's theme.panelColor - see Loadout Panel Colour's tooltip.
            panelBackground.color = theme.loadoutPanelColor;
            panelBackground.raycastTarget = true;

            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.spacing = theme.loadoutPanelPadding * 0.5f;
            int pad = Mathf.RoundToInt(theme.loadoutPanelPadding);
            panelLayout.padding = new RectOffset(pad, pad, pad, pad);
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = panelLayout.childForceExpandHeight = false;

            // Sized by its content, same as PlayerHud's own panel - one less pair of numbers
            // (panel width/height) that would otherwise have to be hand-kept in sync with the two
            // fixed column widths and the tree's own size below.
            ContentSizeFitter panelFitter = panel.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildTitleRow(panel.transform);
            BuildHeaderRow(panel.transform);

            GameObject contentRow = new GameObject("Content", typeof(RectTransform));
            contentRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup contentLayout = contentRow.AddComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = theme.loadoutPanelPadding;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = contentLayout.childForceExpandHeight = false;

            GameObject leftColumn = BuildColumn(contentRow.transform, theme.loadoutLeftColumnWidth);
            GameObject rightColumn = BuildColumn(contentRow.transform, theme.loadoutRightColumnWidth);
            BuildAbilitiesUi(rightColumn.transform);

            AddSectionHeader(leftColumn.transform, "Weapons");
            BuildWeaponTreeUi(leftColumn.transform);
            Button resetWeaponButton = AddButton(leftColumn.transform, "Reset Weapon", OnResetWeaponClicked, theme.loadoutSmallButtonWidth, theme.loadoutSmallButtonHeight);
            resetWeaponLabel = resetWeaponButton.GetComponentInChildren<TextMeshProUGUI>();

            BuildArmorSection(leftColumn.transform);

            // Hover-description strip. Fixed height (Loadout Description Panel Height) via
            // LayoutElement's min AND preferred, both pinned, so switching between a short weapon
            // hover and a long ability description never resizes the panel around it - see the
            // "Hover description" region for what fills it in.
            GameObject descriptionPanel = new GameObject("Description Panel", typeof(RectTransform));
            descriptionPanel.transform.SetParent(panel.transform, false);
            LayoutElement descriptionLe = descriptionPanel.AddComponent<LayoutElement>();
            descriptionLe.preferredHeight = theme.loadoutDescriptionPanelHeight;
            descriptionLe.minHeight = theme.loadoutDescriptionPanelHeight;
            Image descriptionBackground = descriptionPanel.AddComponent<Image>();
            descriptionBackground.color = theme.barTrackColor;
            descriptionBackground.raycastTarget = false;

            VerticalLayoutGroup descriptionLayout = descriptionPanel.AddComponent<VerticalLayoutGroup>();
            int descPad = Mathf.RoundToInt(theme.loadoutPanelPadding * 0.5f);
            descriptionLayout.padding = new RectOffset(descPad, descPad, descPad, descPad);
            descriptionLayout.spacing = 2f;
            descriptionLayout.childAlignment = TextAnchor.UpperLeft;
            descriptionLayout.childControlWidth = descriptionLayout.childControlHeight = true;
            descriptionLayout.childForceExpandWidth = descriptionLayout.childForceExpandHeight = false;

            hoverNameLabel = AddStretchedLabel(descriptionPanel.transform, "", theme.bodyTextSize, FontStyles.Bold);
            hoverDescriptionLabel = AddStretchedLabel(descriptionPanel.transform, "", theme.smallTextSize, FontStyles.Normal);
            hoverDescriptionLabel.color = theme.mutedTextColor;
            hoverNumbersLabel = AddStretchedLabel(descriptionPanel.transform, "", theme.smallTextSize, FontStyles.Normal);
            hoverNumbersLabel.color = theme.mutedTextColor;
            ClearHover();
        }

        private GameObject BuildColumn(Transform parent, float width)
        {
            GameObject column = new GameObject("Column", typeof(RectTransform));
            column.transform.SetParent(parent, false);
            LayoutElement le = column.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            VerticalLayoutGroup layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = theme.loadoutPanelPadding * 0.5f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return column;
        }

        private void BuildTitleRow(Transform parent)
        {
            GameObject row = new GameObject("Title Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            TextMeshProUGUI title = AddLabel(row.transform, "Loadout", theme.titleTextSize, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f; // Pushes the close button to the row's right edge.

            AddButton(row.transform, "X", Toggle, theme.loadoutStepperButtonSize, theme.loadoutStepperButtonSize, theme.loadoutStepperFontSize);
        }

        /// <summary>"Gold 1234" on the left, the shop's status line on the right (a block reason,
        /// the Free Loadout note, or nothing) - a row of its own under the title rather than folded
        /// into it, so the title row's own X-button-pushing flexibleWidth trick does not have to be
        /// redone around two more labels. Text is filled in by RefreshHeader, called from Refresh
        /// and every frame from Update() while open - see their own comments.</summary>
        private void BuildHeaderRow(Transform parent)
        {
            GameObject row = new GameObject("Header Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            goldLabel = AddLabel(row.transform, "", theme.bodyTextSize, FontStyles.Bold);
            goldLabel.alignment = TextAlignmentOptions.MidlineLeft;
            goldLabel.color = theme.goldTextColor;

            statusLabel = AddLabel(row.transform, "", theme.smallTextSize, FontStyles.Normal);
            statusLabel.alignment = TextAlignmentOptions.MidlineRight;
            LayoutElement statusLe = statusLabel.gameObject.AddComponent<LayoutElement>();
            statusLe.flexibleWidth = 1f; // Takes the rest of the row, pushing the gold label to the left edge.
        }

        /// <summary>The always-visible "Loadout (P)" button, bottom-right - clear of the HUD panel
        /// (bottom-CENTRE) and chat (bottom-left). Its own canvas at HUD depth (-10): see the class
        /// comment's SORT ORDER paragraph for why this sits with the HUD rather than the modal
        /// screen it opens.</summary>
        private void BuildToggleButtonCanvas()
        {
            GameObject canvasGo = new GameObject("Loadout Toggle Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject buttonGo = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            buttonGo.name = "Loadout Toggle Button";
            buttonGo.transform.SetParent(canvasGo.transform, false);
            RectTransform buttonRt = buttonGo.GetComponent<RectTransform>();
            buttonRt.anchorMin = buttonRt.anchorMax = buttonRt.pivot = new Vector2(1f, 0f);
            buttonRt.sizeDelta = new Vector2(theme.loadoutToggleButtonWidth, theme.loadoutToggleButtonHeight);
            buttonRt.anchoredPosition = new Vector2(-theme.loadoutToggleButtonMargin, theme.loadoutToggleButtonMargin);
            buttonGo.GetComponent<Image>().color = theme.barTrackColor;

            TextMeshProUGUI label = buttonGo.GetComponentInChildren<TextMeshProUGUI>();
            label.text = "Loadout (P)";
            if (theme.font != null)
                label.font = theme.font;
            label.fontSize = theme.bodyTextSize;
            label.color = theme.textColor;
            ApplyOutline(label);

            loadoutToggleButton = buttonGo.GetComponent<Button>();
            loadoutToggleButton.onClick.AddListener(Toggle);
            loadoutToggleButton.navigation = new Navigation { mode = Navigation.Mode.None }; // See BuildNodeButton's comment (fix 2) - this is the button fix 1/2 were both found from.
        }

        private void AddSectionHeader(Transform parent, string text)
        {
            TextMeshProUGUI header = AddLabel(parent, text, theme.smallTextSize, FontStyles.Bold);
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.color = theme.mutedTextColor;
        }

        /// <summary>width/height of 0 (the default) leaves that axis to the layout group instead of
        /// pinning it - used for the title and section headers, which should stretch to their row's
        /// own width rather than carry a fixed one. fontSize of 0 (the default) uses Body Text Size;
        /// the close X and the armor steppers pass Loadout Stepper Font Size instead, a bigger glyph
        /// sized to Loadout Stepper Button Size - see that field's own tooltip for why.</summary>
        private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width = 0f, float height = 0f, float fontSize = 0f)
        {
            GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = theme.barTrackColor;

            TextMeshProUGUI text = go.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label;
            if (theme.font != null)
                text.font = theme.font;
            text.fontSize = fontSize > 0f ? fontSize : theme.bodyTextSize;
            text.color = theme.textColor;
            ApplyOutline(text);

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // See BuildNodeButton's comment (fix 2).

            if (width > 0f || height > 0f)
            {
                LayoutElement le = go.AddComponent<LayoutElement>();
                if (width > 0f) le.preferredWidth = width;
                if (height > 0f) le.preferredHeight = height;
            }

            return button;
        }

        /// <summary>A label that stretches to fill whatever width it sits in - the same flexibleWidth
        /// trick BuildTitleRow's own title text and BuildArmorRow's own label already use, here
        /// pulled into a helper for the hover-description panel's three stacked lines, each of which
        /// needs the same "wrap to the panel's own width, not to my own text's width" behaviour.</summary>
        private TextMeshProUGUI AddStretchedLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            TextMeshProUGUI label = AddLabel(parent, text, fontSize, style);
            label.alignment = TextAlignmentOptions.TopLeft;
            LayoutElement le = label.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            return label;
        }

        /// <summary>Same recipe as PlayerHud.AddLabel - kept private to this file rather than shared
        /// for the same reason PlayerHud gives: the two have no other coupling.</summary>
        private TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            if (theme.font != null)
                tmp.font = theme.font;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = theme.textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            ApplyOutline(tmp);
            return tmp;
        }

        /// <summary>See PlayerHud.ApplyOutline's class comment for why every text this screen builds
        /// shares ONE Material instance instead of letting TMP auto-clone one per label.</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (loadoutTextMaterial == null)
            {
                loadoutTextMaterial = new Material(tmp.fontSharedMaterial);
                loadoutTextMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, theme.textOutlineWidth);
                loadoutTextMaterial.SetColor(ShaderUtilities.ID_OutlineColor, theme.textOutlineColor);
            }
            tmp.fontSharedMaterial = loadoutTextMaterial;
        }

        /// <summary>The scene already carries one EventSystem (used by TestRangePanel's dropdowns
        /// and buttons today), so this is a safety net rather than the normal path - but a screen
        /// with clickable buttons and no EventSystem in the scene would silently accept no clicks at
        /// all, which is a miserable thing to debug, so it is checked for rather than assumed.</summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
