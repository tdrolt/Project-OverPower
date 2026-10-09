using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;
using Overpower.Weapons;

namespace Overpower.UI
{
    /// <summary>
    /// The shop screen (P or the bottom-right "Loadout (P)" button; closed by P, Esc or its X): weapon upgrade tree, armor
    /// rows and ability cards on two tabs ("Weapons", "Abilities & Armor"), reopening on the page last used
    /// (ShopPageMemory). OWNER ONLY, built in code like PlayerHud (every listener sits on its button's line), dormant on
    /// remote copies. SORT ORDER: modal canvas at -5, above PlayerHud (-10) but below the F1 panel (500) and MatchUI's
    /// win/lose panels (0), which must show over an open shop; the "Loadout (P)" button has its own canvas at -10, HUD depth.
    /// INPUT: PlayerInputRouter's Shop action / ShopToggled (own emit gate, see ShopSuppressed); while open it claims tool
    /// focus (SetToolFocus), keyed by owner so it and the F1 panel can be open together. Hover pop-up: LoadoutScreen.Tooltip.cs.
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

        [SerializeField, Tooltip("Match tuning asset - Free Loadout, the sell refund rate, and the shop's own (shorter) out-of-combat timer. The same asset PlayerLoadout and GoldWallet read. Missing this fails OPEN to Free Loadout (see ShopPricing.Build) rather than silently locking every purchase.")]
        private GameplayConfig gameplayConfig;

        /// <summary>This player's gold, owner-authoritative (GoldWallet's own class comment). Read,
        /// never written directly - every spend/refund goes through TrySpend/Add so the wallet is
        /// the only thing that ever publishes the "gold" Custom Property.</summary>
        private GoldWallet goldWallet;

        /// <summary>What this player has paid per category this match, so a weapon/armor reset can refund part of it.
        /// Lives here because this is the only thing that spends through it, and it resets exactly when a fresh
        /// LoadoutScreen does (a new player object), like GoldWallet's balance.</summary>
        private readonly PurchaseLedger ledger = new PurchaseLedger();

        // ---- shop telemetry events ---------------------------------------------------------------
        // Raised at the three TrySpend sites (weapon, armor, ability), the two refund sites (weapon reset, armor reset)
        // and ShowBlockedReason; they change nothing about a click. PlayerTelemetry is the only listener.

        // Armor has no item id, only a path and a level reached on it. TelemetryKeys.ItemId documents the encoding
        // (100 + level for absorb, 200 + level for recharge) so the two paths never share a number.
        private const int ArmorAbsorbItemBase = 100;
        private const int ArmorRechargeItemBase = 200;

        /// <summary>owner, on a successful purchase: category, the item bought (for armor, the item base + level reached),
        /// the price actually charged (0 under Free Loadout), the balance right after, and whether Free Loadout paid for it.</summary>
        public event System.Action<PurchaseCategory, int, int, int, bool> Purchased;

        /// <summary>owner, on a successful weapon/armor reset: category, gold refunded, balance
        /// after. Never raised under Free Loadout (nothing was ever spent to refund) or when the
        /// refund rounds down to 0.</summary>
        public event System.Action<PurchaseCategory, int, int> Refunded;

        /// <summary>owner, on any refused click: the item id (-1 for a reset), the price checked, why it was refused, and
        /// the gold shortfall (0 unless the reason is CannotAfford).</summary>
        public event System.Action<int, int, PurchaseBlock, int> PurchaseRefused;

        /// <summary>True only while the LOCAL player's own screen is open (remote copies bail in Awake, so one writer).
        /// Reset in OnDestroy so a torn-down player object can never leave it stuck true.</summary>
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

        /// <summary>One ability card's pieces, same recipe as WeaponNodeUi; only two states (Equipped or not), so no
        /// separate styling method (RefreshAbilities).</summary>
        private sealed class AbilityCardUi
        {
            public Button button;
            public Image outer;
            public Image inner;
            public TextMeshProUGUI label;
        }

        // Keyed by (slot, id): ids are already globally unique (AbilityCatalogue), but RefreshAbilities needs the pair to
        // ask "is THIS slot's card THIS slot's equipped id" without a second lookup.
        private readonly Dictionary<(AbilitySlot slot, int id), AbilityCardUi> abilityCards =
            new Dictionary<(AbilitySlot slot, int id), AbilityCardUi>();

        // Mobility (Shift), Attachment (RMB), Ultimate (Space): left to right, not AbilitySlot's declaration order
        // (which starts with Primary, the weapon, not drawn here).
        private static readonly AbilitySlot[] LoadoutAbilitySlotOrder =
        {
            AbilitySlot.Mobility, AbilitySlot.Attachment, AbilitySlot.Ultimate
        };

        // ---- hover pop-up ---------------------------------------------------------------

        /// <summary>Turns UI pointer enter/exit/move into plain callbacks on every weapon node, ability card and armor row
        /// to drive the pop-up (LoadoutScreen.Tooltip.cs). A MonoBehaviour because the pointer handlers only work on one.</summary>
        private sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
        {
            public System.Action OnExit;
            /// <summary>Where the pointer is (screen pixels), fired on enter and every move.</summary>
            public System.Action<Vector2> OnPointerAt;
            public void OnPointerEnter(PointerEventData eventData) => OnPointerAt?.Invoke(eventData.position);
            public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
            public void OnPointerMove(PointerEventData eventData) => OnPointerAt?.Invoke(eventData.position);
        }

        // ---- pages ----------------------------------------------------------------------

        /// <summary>The page the shop reopens on: one for the whole session, shared by every LoadoutScreen (a new player
        /// object after a respawn lands on the page last used).</summary>
        private static readonly ShopPageMemory pageMemory = new ShopPageMemory();

        private RectTransform panelRect;
        private GameObject weaponsPageRoot;
        private GameObject abilitiesPageRoot;
        private Image weaponsTabImage;
        private Image abilitiesTabImage;
        private ShopPage shownPage = ShopPage.Weapons;

        /// <summary>The page on screen right now (the one whose tab is lit).</summary>
        public ShopPage CurrentPage => shownPage;

        // ---- shop header --------------------------------------------------------------

        private TextMeshProUGUI goldLabel;
        private TextMeshProUGUI statusLabel;

        // Cached against the raw gold int / block enum / out-of-combat TENTH-of-a-second, not a formatted string, so
        // Update()'s per-frame RefreshHeader builds no text when nothing changed. lastStatusText records the last string
        // drawn so the two paths never rewrite the same text.
        private bool headerInitialized;
        private int lastDisplayedGold;
        private bool lastDisplayedIsFree;
        private bool lastDisplayedIsWarmupSandbox;
        private PurchaseBlock lastDisplayedBlock;
        private int lastDisplayedTenths;
        private string lastStatusText;

        // A refused click's reason, shown in the header status line instead of the gate status until Loadout Blocked
        // Reason Duration Seconds (UiTheme) runs out (ShowBlockedReason). Expiry <= 0 means "no override active"
        // (Time.unscaledTime is never <= 0 for long, so no separate bool). Unscaled so a debug Time.timeScale change
        // cannot freeze the reason on screen.
        private string blockedReasonText = "";
        private float blockedReasonExpiryTime = -1f;
        private bool blockedReasonIsNotice; // a "Sold ..." message rides the same status line, in the normal text colour.

        /// <summary>Shown under the Ultimate heading only while that slot is empty ("Buy an ultimate"): the one slot with
        /// no card that can read Equipped at spawn, so the column would otherwise not say why nothing is highlighted.</summary>
        private TextMeshProUGUI ultimateEmptyLabel;

        // Reset-button labels, kept so Refresh can rewrite their refund preview in place (RefreshResetLabels).
        private TextMeshProUGUI resetWeaponLabel;
        private TextMeshProUGUI resetArmorLabel;

        // ---- armor --------------------------------------------------------------------------------

        private TextMeshProUGUI absorbText;
        private TextMeshProUGUI rechargeText;
        private Button absorbButton;
        private Button rechargeButton;

        // ---- screen state / built UI ---------------------------------------------------------------

        /// <summary>The modal canvas (dim + panel); SetActive(false/true) is the whole show/hide, it is never destroyed.</summary>
        private GameObject screenRoot;

        /// <summary>The always-visible "Loadout (P)" button, kept so Update() can disable it once the match is over
        /// (matching Open()'s refusal) instead of leaving a button that silently no-ops.</summary>
        private Button loadoutToggleButton;

        /// <summary>This instance's open flag, separate from the static IsOpen (what the rest of the game reads): it
        /// records whether THIS component claimed tool focus, so Close() twice (Esc, then OnDisable) never double-releases.</summary>
        private bool isOpenLocal;

        // ---- Dominion ---------------------------------------------------------------------

        /// <summary>A player who joined a Dominion match mid-round may pick once before spawning (A12): the shop opens by
        /// itself once, free, with the current round's limits, and stays pickable until they close it or the round ends;
        /// after that it is break-only. Set in Start for a fresh joiner, never a rejoiner (who keeps the round's picks).</summary>
        private bool lateJoinerPick;
        private readonly Dictionary<int, int> weaponDepth = new Dictionary<int, int>();
        private RoomManager roomManager;
        private PlayerHud playerHud;
        private bool lastDisplayedIsDominion;

        private DominionConfig DominionCfg
        {
            get
            {
                if (roomManager == null) roomManager = FindFirstObjectByType<RoomManager>();
                return roomManager != null ? roomManager.Dominion : null;
            }
        }

        // What Refresh() last drew, so Update() (while open) can tell when the weapon or armor changed from OUTSIDE this
        // screen's clicks (the F1 panel, a property echo) and catch up. Abilities need none: AbilityRunner.SlotChanged
        // fires for every equip and Refresh is subscribed to it.
        private int lastKnownWeaponId = int.MinValue;
        private int lastKnownAbsorbLevel = -1;
        private int lastKnownRechargeLevel = -1;

        // The shop gate can flip (out-of-combat timer, walking into your territory) or the balance rise (passive income)
        // with nothing clicked. A full Refresh() fires only when one of these changes; otherwise Update() still calls the
        // cheap RefreshHeader() every frame so the countdown keeps ticking.
        private int lastKnownGold = int.MinValue;
        private int lastKnownRound = -1; // Dominion: the round the open shop was drawn for (its weapon and armour limits change with it)
        private bool lastKnownBlocked;

        // One Material shared by every text this screen builds (PlayerHud.ApplyOutline says why).
        private Material loadoutTextMaterial;

        private void Awake()
        {
            // Every remote copy stays permanently dormant.
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
            // Optional: nothing here needs it besides the match-over gate.
            matchUI = GetComponent<MatchUI>();
            playerHud = GetComponent<PlayerHud>();

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

            if (inputRouter != null)
                inputRouter.ShopToggled += Toggle;
            if (abilityRunner != null)
                abilityRunner.SlotChanged += HandleAbilitySlotChanged;
        }

        private void Start()
        {
            // Owner only (Awake switched every other copy off). A joiner mid-round gets the shop once, free; a rejoiner keeps what they had.
            if (!photonView.IsMine || photonView.Owner == null || photonView.Owner.HasRejoined || !ShopPricing.DominionLive())
                return;
            DominionDirector director = DominionDirector.Instance;
            if (director == null || !DominionShopRules.LateJoinerWindowOpen(director.Stage))
                return;
            lateJoinerPick = true;
            Open();
            Debug.Log($"[SHOP] late joiner: shop opened once, free, round {director.Round} limits");
        }

        private void OnDisable()
        {
            // Covers a normal close AND the GameObject being disabled under it (leaving play mode): the tool-focus claim
            // must not outlive the component.
            if (isOpenLocal)
                Close();
        }

        private void OnDestroy()
        {
            if (inputRouter != null)
                inputRouter.ShopToggled -= Toggle;
            if (abilityRunner != null)
                abilityRunner.SlotChanged -= HandleAbilitySlotChanged;

            // MINE ONLY: every remote copy also runs OnDestroy (whenever any OTHER player leaves) though it bailed out of
            // Awake; without this guard its teardown would clear the STATIC IsOpen, and release a focus claim it never
            // made, under the local player's open screen. Also the guarantee for the owner's copy, since OnDisable does
            // not run for every teardown path.
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

            // Dominion: a late joiner's one pick ends with the round they joined in; the shop is shut outside the break.
            if (lateJoinerPick && DominionMode.IsActive() && DominionDirector.Instance != null
                && !DominionShopRules.LateJoinerWindowOpen(DominionDirector.Instance.Stage))
                lateJoinerPick = false;
            bool shopShut = ShopPricing.DominionClosed(lateJoinerPick) != PurchaseBlock.None;

            // The always-visible toggle button stops offering a loadout once the match is over, or while the shop is shut
            // (outside the break). Runs regardless of isOpenLocal: the button is clickable whether the screen is open or not.
            if (loadoutToggleButton != null)
                loadoutToggleButton.interactable = !matchOver && !shopShut;

            if (!isOpenLocal)
                return;

            // The round started (the break ended): an open shop closes the moment the stage leaves the break.
            if (shopShut)
            {
                Close();
                return;
            }

            // A raw keyboard poll, not an InputAction (as TestRangePanel's F1): Esc-closes-a-tool is a convention, not a rebindable control.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            // The match ending must close this screen though ShopSuppressed deliberately does not gate ShopToggled on it:
            // MatchUI freezes movement but nothing else stops a living player re-picking after the result is decided.
            if (matchOver)
            {
                Close();
                return;
            }

            FitPanelToCanvas();
            TickTooltip(Time.unscaledDeltaTime);

            int weaponId = CurrentWeaponId();
            int absorbLevel = playerHealth != null ? playerHealth.AbsorbLevel : -1;
            int rechargeLevel = playerHealth != null ? playerHealth.RechargeLevel : -1;

            // Gold and the gate can change with nothing on THIS screen clicked, and change every node/card's look. The
            // context is built ONCE here and passed to whichever of Refresh/RefreshHeader runs.
            int gold = goldWallet != null ? goldWallet.Balance : 0;
            ShopContext ctx = CurrentShopContext();
            bool blocked = ctx.Check(0) != PurchaseBlock.None;

            if (weaponId != lastKnownWeaponId || absorbLevel != lastKnownAbsorbLevel || rechargeLevel != lastKnownRechargeLevel
                || gold != lastKnownGold || blocked != lastKnownBlocked || CurrentRound() != lastKnownRound)
                Refresh(ctx);
            else
                RefreshHeader(ctx); // Cheap when nothing changed.
        }

        /// <summary>AbilityRunner.SlotChanged fires for every equip from any source (this screen, the F1 panel, a remote
        /// echo), so subscribing it to Refresh keeps the ability column live without the polling weapon and armor need.</summary>
        private void HandleAbilitySlotChanged(AbilitySlot slot) => Refresh();

        // ============================================================================================
        // Open / close
        // ============================================================================================

        public void Open()
        {
            if (isOpenLocal)
                return;
            // A dead player may open the shop while waiting to respawn (and an open shop stays open through a death); only
            // the match ending closes it (D4). The gates count as passed while dead (ShopRules.EffectiveInOwnTerritory,
            // applied in ShopPricing.Build; the combat gate needs none, PlayerHealth puts the clock out of combat on death).
            if (matchUI != null && matchUI.MatchOver)
                return; // The match is already decided.
            if (ShopPricing.DominionClosed(lateJoinerPick) != PurchaseBlock.None)
            {
                // Dominion outside the break: stays shut, with a short word on why (the header is not on screen to say it).
                Debug.Log($"[SHOP] open refused: NotInBreak (stage {DominionDirector.Instance?.Stage})");
                if (playerHud != null && theme != null) playerHud.ShowToast(theme.loadoutShopClosedText);
                return;
            }

            isOpenLocal = true;
            IsOpen = true;
            screenRoot.SetActive(true);
            inputRouter?.SetToolFocus(this, true);
            // The cursor is never locked in this project, so there is nothing to unlock here.
            HideTooltip(); // Reopening must not show whatever was last hovered before it closed.
            ShowPage(pageMemory.Last);
            FitPanelToCanvas();
            Refresh();
        }

        /// <summary>Scales the panel down evenly when the canvas is narrower than the panel (4:3, 5:4 windows), so
        /// nothing clips; ShopPanelScale is the rule. Cheap enough for Update, which calls it while open so a resized
        /// window follows.</summary>
        private void FitPanelToCanvas()
        {
            if (panelRect == null || screenRoot == null)
                return;
            float panelWidth = theme.loadoutPageWidth + 2f * Mathf.RoundToInt(theme.loadoutPanelPadding);
            float scale = ShopPanelScale.For(((RectTransform)screenRoot.transform).rect.width, panelWidth);
            if (!Mathf.Approximately(panelRect.localScale.x, scale))
                panelRect.localScale = new Vector3(scale, scale, 1f);
        }

        public void Close()
        {
            if (!isOpenLocal)
                return;

            isOpenLocal = false;
            IsOpen = false;
            lateJoinerPick = false; // a late joiner's one pick ends when they close the shop
            HideTooltip();
            screenRoot.SetActive(false);
            inputRouter?.SetToolFocus(this, false);

            // A reason from ShowBlockedReason must not survive a close and reopen: the gate may have changed by then, so it
            // would redraw a stale reason. Clearing headerInitialized forces RefreshHeader's first-call path on the next
            // Open(), which rewrites both labels; otherwise "the normal status hasn't changed" would skip the write.
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

        /// <summary>The fresh start at match-live forgets everything this player spent (so neither reset can refund
        /// warm-up spending) and closes the screen if open. Owner only; the guard matches every other ResetForMatchStart.</summary>
        public void ResetForMatchStart()
        {
            if (!photonView.IsMine)
                return;

            ledger.Clear();
            Close();
        }

        /// <summary>Re-reads everything this screen shows from the live player state: on Open, after every click here, and
        /// from Update()'s poll. WeaponFiring has no "loadout changed" event, so a weapon or armor change from OUTSIDE
        /// (the F1 panel, a remote echo) would sit stale until a click here. Abilities need no poll (SlotChanged).</summary>
        private void Refresh() => Refresh(CurrentShopContext());

        /// <summary>Takes a ShopContext already built this frame (Update()'s poll); the parameterless Refresh() builds the
        /// one context this whole pass needs, so no Refresh* method builds its own.</summary>
        private void Refresh(ShopContext ctx)
        {
            RefreshHeader(ctx);
            RefreshWeaponTree(ctx);
            RefreshArmor(ctx);
            RefreshAbilities(ctx);
            RefreshResetLabels();
            tooltipTextDirty = true; // A price, a level or the equipped item may have changed under an open pop-up.

            // Snapshot what was just drawn, so Update()'s poll calls back in only when something ACTUALLY changed since.
            lastKnownWeaponId = CurrentWeaponId();
            lastKnownAbsorbLevel = playerHealth != null ? playerHealth.AbsorbLevel : -1;
            lastKnownRechargeLevel = playerHealth != null ? playerHealth.RechargeLevel : -1;
            lastKnownGold = goldWallet != null ? goldWallet.Balance : 0;
            lastKnownBlocked = ctx.Check(0) != PurchaseBlock.None;
            lastKnownRound = CurrentRound();
        }

        /// <summary>The Dominion round the shop's limits are for (0 outside Dominion), so an open shop redraws when the round moves on.</summary>
        private static int CurrentRound() => DominionDirector.Instance != null ? DominionDirector.Instance.Round : 0;

        /// <summary>This player's shop gate and balance right now, built fresh each call (cheap, no allocation) rather than
        /// cached, so every caller this frame agrees even if territory or the wallet changed mid-frame.</summary>
        private ShopContext CurrentShopContext() =>
            ShopPricing.Build(gameplayConfig, playerHealth, goldWallet, photonView.Owner, transform.position,
                lifecycle == null || lifecycle.IsAlive, lateJoinerPick, DominionCfg,
                theme != null ? theme.loadoutDominionFreeText : "", theme != null ? theme.loadoutShopClosedText : "",
                theme != null ? theme.loadoutDominionLateJoinerFreeText : "");

        /// <summary>Dominion: a refused pick because the shop is shut (outside the break). True when the caller should stop.</summary>
        private bool RefuseIfClosed(ShopContext ctx, int itemId = -1)
        {
            if (ctx.Closed == PurchaseBlock.None)
                return false;
            ShowBlockedReason(ctx, ctx.Closed, 0, itemId);
            return true;
        }

        /// <summary>The most armour upgrades the + buttons offer right now: ArmorConfig's maximum, or this Dominion round's smaller allowance.</summary>
        private int ArmorCapFor(ShopContext ctx) =>
            armorConfig == null ? 0 : ctx.Limits != null ? ctx.Limits.ArmorCap(armorConfig.MaxArmorUpgrades) : armorConfig.MaxArmorUpgrades;

        /// <summary>Header row: the gold and the status line (a refused click's reason while its timer runs, else the gate's
        /// ShopContext.StatusText). Called every frame while open with the ShopContext Update() built. Every comparison is on
        /// the raw gold int, the block enum or the countdown tenth BEFORE any string is built, so an unchanged frame formats nothing.</summary>
        private void RefreshHeader(ShopContext ctx)
        {
            int gold = goldWallet != null ? goldWallet.Balance : 0;
            if (!headerInitialized || gold != lastDisplayedGold || ctx.IsDominion != lastDisplayedIsDominion)
            {
                goldLabel.text = ctx.IsDominion ? "" : ShopPricing.GoldLabel(gold); // Dominion has no gold
                lastDisplayedGold = gold;
                lastDisplayedIsDominion = ctx.IsDominion;
            }

            bool reasonActive = blockedReasonExpiryTime > 0f && Time.unscaledTime < blockedReasonExpiryTime;
            if (reasonActive)
            {
                if (blockedReasonText != lastStatusText)
                {
                    statusLabel.text = blockedReasonText;
                    statusLabel.color = blockedReasonIsNotice ? theme.textColor : theme.overheatWarningColor;
                    lastStatusText = blockedReasonText;
                }
                headerInitialized = true;
                return; // The reason's own timer owns the status line until it expires.
            }
            // The label may still show blockedReasonText, which the comparisons below do not track (they follow the NORMAL
            // status, which may not have changed underneath). "justExpired" forces one write, or a reason whose gate
            // never changed (still out of territory) would stay on screen forever.
            bool justExpired = blockedReasonExpiryTime > 0f;
            blockedReasonExpiryTime = -1f;

            PurchaseBlock block = ctx.IsFree ? PurchaseBlock.None : ctx.Check(0);
            int tenths = block == PurchaseBlock.InCombat ? Mathf.RoundToInt(ctx.SecondsUntilOutOfCombat * 10f) : 0;

            // IsWarmupSandbox joins the change check, but not because of going live: with Free Loadout ON it stays false
            // throughout and IsFree alone covers that edge (the header correctly keeps "Free (test mode)"). It matters only
            // when Free Loadout is toggled DURING the warm-up (a ScriptableObject field flipped in Play Mode): IsFree is
            // unchanged but IsWarmupSandbox flips, and is the only thing telling "Free (warm-up)" from "Free (test mode)".
            if (justExpired || !headerInitialized || ctx.IsFree != lastDisplayedIsFree
                || ctx.IsWarmupSandbox != lastDisplayedIsWarmupSandbox || block != lastDisplayedBlock || tenths != lastDisplayedTenths)
            {
                string statusText = ctx.StatusText();
                statusLabel.text = statusText;
                // A free shop's note and "all clear" read as a plain aside; a block reason borrows the HUD's overheat-warning amber.
                statusLabel.color = statusText.Length == 0 || ctx.IsFree ? theme.mutedTextColor : theme.overheatWarningColor;
                lastStatusText = statusText;
                lastDisplayedIsFree = ctx.IsFree;
                lastDisplayedIsWarmupSandbox = ctx.IsWarmupSandbox;
                lastDisplayedBlock = block;
                lastDisplayedTenths = tenths;
            }
            headerInitialized = true;
        }

        /// <summary>Tells the player why a click was refused by taking over the header status line for Loadout Blocked
        /// Reason Duration Seconds (UiTheme). Takes the SAME ctx and price the click handler checked against, so a
        /// CannotAfford reason quotes the real shortfall for the item clicked.</summary>
        private void ShowBlockedReason(ShopContext ctx, PurchaseBlock block, int price, int itemId = -1)
        {
            blockedReasonText = ctx.ReasonText(block, price);
            blockedReasonIsNotice = false;
            // Unscaled (project convention, see PlayerHud.bountyToastHideAtTime): a debug Time.timeScale change must not freeze it.
            blockedReasonExpiryTime = Time.unscaledTime + theme.loadoutBlockedReasonDurationSeconds;
            RefreshHeader(ctx); // Shows it from the same frame as the click, not one frame late.

            // `shopBlocked` telemetry. Shortfall only means anything for CannotAfford.
            int shortfall = block == PurchaseBlock.CannotAfford ? Mathf.Max(0, price - ctx.Balance) : 0;
            PurchaseRefused?.Invoke(itemId, price, block, shortfall);
        }

        /// <summary>"Sold Laser - Through Walls: +600 gold" on the header status line for a few seconds after a sale, so a
        /// refund is never a silent change in the gold number (D19).</summary>
        private void ShowSoldMessage(string text)
        {
            blockedReasonText = text;
            blockedReasonIsNotice = true;
            blockedReasonExpiryTime = Time.unscaledTime + theme.loadoutSoldMessageDurationSeconds;
        }

        /// <summary>Rewrites the Reset buttons' labels with a refund preview ("Reset Weapon (+600)"), read off the ledger
        /// and never spent, so the player sees what undoing returns before clicking.</summary>
        private void RefreshResetLabels()
        {
            double rate = gameplayConfig != null ? gameplayConfig.SellRefundRate : 0.5;
            if (resetWeaponLabel != null)
            {
                int refund = ledger.WeaponRefundPreview(rate);
                resetWeaponLabel.text = refund > 0 ? $"Reset Weapon (+{refund})" : "Reset Weapon";
            }
            if (resetArmorLabel != null)
            {
                int refund = GoldMath.Refund(ledger.ArmorSpent, rate);
                resetArmorLabel.text = refund > 0 ? $"Reset Armor (+{refund})" : "Reset Armor";
            }
        }

        // ============================================================================================
        // Weapon tree - rules from Overpower.Combat.WeaponUpgradeTree, drawn here.
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

            // How deep each weapon sits (Dominion opens the tree a level per round); computed once, from the same list the tree is built from.
            weaponDepth.Clear();
            var parentById = new Dictionary<int, int>();
            foreach (var (id, parentId) in nodes)
                parentById[id] = parentId;
            foreach (var (id, _) in nodes)
                weaponDepth[id] = DominionShopRules.WeaponDepth(id, w => parentById.TryGetValue(w, out int p) ? p : (int?)null);
            // Logged once at build, not from Refresh: a data problem does not change between opens and clicks.
            foreach (string problem in tree.Problems)
                Debug.LogError($"[LoadoutScreen] {problem}");
        }

        private void OnWeaponNodeClicked(int weaponId)
        {
            int equipped = CurrentWeaponId();
            ShopContext ctx = CurrentShopContext();
            // Guards regardless of the button's interactable flag (only Locked nodes are non-interactable, see StyleNode):
            // clicking Equipped or Owned must do nothing.
            // Dominion: every pick is free, so a weapon the round opens is pickable from anywhere (a family switch is one click).
            bool pickable = ctx.Limits != null
                ? ctx.Limits.NodeState(tree.StateOf(weaponId, equipped), DepthOf(weaponId)) == UpgradeNodeState.Selectable
                : tree.CanUpgrade(equipped, weaponId);
            if (!pickable)
                return;
            if (RefuseIfClosed(ctx, weaponId))
                return;

            // A Selectable node stays clickable while shop-blocked (StyleNode); ShowBlockedReason tells the player why in the header.
            WeaponDefinition target = weapons.Resolve(weaponId);
            int price = target != null ? target.GoldCost : 0;
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

            // A reset has no price, only the territory/combat gate (price 0 never trips CannotAfford): the same Check(0)
            // the header reads decides whether the refund is allowed.
            ShopContext ctx = CurrentShopContext();
            if (RefuseIfClosed(ctx))
                return;
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
                    WeaponDefinition sold = weapons != null ? weapons.Resolve(CurrentWeaponId()) : null;
                    int refund = ledger.SellWeapon(gameplayConfig.SellRefundRate);
                    goldWallet.Add(refund, GoldSource.Refund);
                    if (refund > 0)
                    {
                        Refunded?.Invoke(PurchaseCategory.Weapon, refund, goldWallet.Balance);
                        ShowSoldMessage(ShopRules.SoldMessage(theme.loadoutSoldWeaponFormat, sold != null ? sold.DisplayName : "weapon", refund));
                    }
                }
            }

            playerLoadout?.SetWeapon(tree.RootId);
            Refresh();
        }

        private int CurrentWeaponId() =>
            weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : tree.RootId;

        private int DepthOf(int weaponId) => weaponDepth.TryGetValue(weaponId, out int depth) ? depth : -1;

        private void RefreshWeaponTree(ShopContext ctx)
        {
            if (tree == null || weapons == null)
                return;

            int equipped = CurrentWeaponId();
            double rate = gameplayConfig != null ? gameplayConfig.SellRefundRate : 0.5;
            int refundNow = ledger.WeaponRefundPreview(rate);
            foreach (var pair in weaponNodes)
                StyleNode(pair.Value, tree.StateOf(pair.Key, equipped), weapons.Resolve(pair.Key), ctx,
                    tree.NeedsSwap(pair.Key, equipped) ? refundNow : -1);
        }

        /// <summary>The label's second line reads "Equipped"/"Owned" for a weapon already reached, else its price
        /// (ShopPricing.PriceLine), shown even under Free Loadout (which only changes whether it is charged). A Selectable
        /// node the shop gate refuses right now gets its OWN look (Loadout Shop Blocked Colour, not Locked Colour) but
        /// STAYS interactable: OnWeaponNodeClicked re-checks and shows the reason (ShowBlockedReason), and a disabled
        /// button would also stop the node being hoverable for its pop-up.</summary>
        private void StyleNode(WeaponNodeUi ui, UpgradeNodeState state, WeaponDefinition def, ShopContext ctx, int swapRefund)
        {
            // Dominion: the round decides what is open. A weapon the round has not opened is Locked and says which round does ("Round 2");
            // anything it has opened is pickable from where you stand, because every pick is free.
            string lockedText = "";
            if (ctx.Limits != null && def != null)
            {
                int depth = DepthOf(def.Id);
                if (ctx.Limits.IsRoundLocked(state, depth))
                    lockedText = ctx.Limits.LockedLabel(depth);
                state = ctx.Limits.NodeState(state, depth);
                swapRefund = -1; // nothing is sold back in a free shop
            }

            PurchaseBlock block = state == UpgradeNodeState.Selectable && def != null ? ctx.Check(def.GoldCost) : PurchaseBlock.None;
            bool shopBlocked = block != PurchaseBlock.None;

            string suffix = state == UpgradeNodeState.Equipped ? "Equipped"
                          : state == UpgradeNodeState.Owned ? "Owned"
                          : lockedText.Length > 0 ? lockedText
                          : def != null ? ctx.PriceLine(def.GoldCost, block) : "";
            // Loadout Price Line Size Percent (UiTheme) on the price/status line only: the node is small and two full-size
            // lines would not fit. A weapon on another branch says its price AND what selling back gives now (D19);
            // with nothing to sell back it keeps the normal price line.
            float sizePercent = theme.loadoutPriceLineSizePercent;
            if (state == UpgradeNodeState.Locked && swapRefund > 0 && def != null)
            {
                string swap = ShopRules.SwapLine(def.GoldCost, swapRefund, theme.loadoutSwapFormat);
                if (swap.Length > 0)
                {
                    suffix = swap;
                    sizePercent = theme.loadoutSwapLineSizePercent;
                }
            }
            ui.label.text = def != null ? $"{def.DisplayName}\n<size={sizePercent}%>{suffix}</size>" : suffix;

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

        /// <summary>Spends BEFORE upgrading, at the price of the purchase about to be made (armorConfig.UpgradeCosts
        /// [absorbLevel + rechargeLevel]: one shared "purchases so far" index for both paths). The CanUpgrade check is
        /// a second guard behind RefreshArmor's disabled button, like OnWeaponNodeClicked's.</summary>
        private void TryBuyArmorUpgrade(bool upgradeAbsorb)
        {
            if (playerHealth == null || armorConfig == null)
                return;

            ShopContext ctx = CurrentShopContext();
            int cap = ArmorCapFor(ctx);
            var path = new ArmorUpgradePath(armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel, cap);
            if (!(upgradeAbsorb ? path.CanUpgradeAbsorb : path.CanUpgradeRecharge))
                return;
            if (RefuseIfClosed(ctx))
                return;

            int price = armorConfig.CostFor(playerHealth.AbsorbLevel + playerHealth.RechargeLevel);
            bool free = ctx.IsFree;
            int chargedPrice = 0;

            // Armor has no item id: path plus level, encoded per TelemetryKeys.ItemId.
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

            ArmorLoadoutActions.TryUpgrade(playerHealth, playerLoadout, armorConfig, upgradeAbsorb, cap);

            // Read AFTER TryUpgrade so it reflects what was actually bought, not the prediction above.
            int newLevel = upgradeAbsorb ? playerHealth.AbsorbLevel : playerHealth.RechargeLevel;
            int purchasedItem = (upgradeAbsorb ? ArmorAbsorbItemBase : ArmorRechargeItemBase) + newLevel;
            Purchased?.Invoke(PurchaseCategory.Armor, purchasedItem, chargedPrice, goldWallet != null ? goldWallet.Balance : 0, free);
            Refresh();
        }

        private void OnResetArmorClicked()
        {
            // No price of its own, only the gate applies (as OnResetWeaponClicked).
            ShopContext ctx = CurrentShopContext();
            if (RefuseIfClosed(ctx))
                return;
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
                    {
                        Refunded?.Invoke(PurchaseCategory.Armor, refund, goldWallet.Balance);
                        ShowSoldMessage(ShopRules.SoldArmorMessage(theme.loadoutSoldArmorFormat, refund));
                    }
                }
            }

            ArmorLoadoutActions.Reset(playerLoadout);
            Refresh();
        }

        /// <summary>A refused upgrade DISABLES the + button, computed up front from the ArmorUpgradePath rule TryUpgrade
        /// applies, so a click that would be refused is never offered. Shows the next purchase's price (ShopPricing.
        /// PriceLine) next to a buyable path, and mutes both rows' text (not the + buttons) while shop-blocked, like the
        /// weapon tree's "visible but reads as unavailable".</summary>
        private void RefreshArmor(ShopContext ctx)
        {
            if (playerHealth == null || armorConfig == null)
                return;

            int cap = ArmorCapFor(ctx); // Dominion: this round's allowance when it is lower than the shop's own maximum
            var path = new ArmorUpgradePath(armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel, cap);
            int nextPrice = armorConfig.CostFor(playerHealth.AbsorbLevel + playerHealth.RechargeLevel);
            PurchaseBlock block = ctx.Check(nextPrice);
            bool gateBlocked = block != PurchaseBlock.None;
            string priceLine = ctx.PriceLine(nextPrice, block);

            // The limit is ONE budget shared by both rows (ArmorUpgradePath.TotalUpgrades against ArmorConfig.MaxArmorUpgrades),
            // so both rows say where the next click sits in it, or that it is spent (D5).
            bool roundLimited = cap < armorConfig.MaxArmorUpgrades;
            string limit = ArmorLimitLabel.Text(path.TotalUpgrades, cap,
                theme.loadoutArmorUpgradeFormat, roundLimited ? theme.loadoutArmorRoundMaxFormat : theme.loadoutArmorMaxFormat);
            absorbText.text = path.CanUpgradeAbsorb
                ? ArmorRow(theme.loadoutArmorAbsorbRowFormat, playerHealth.AbsorbLevel, $"{limit} ({priceLine})")
                : ArmorRow(theme.loadoutArmorAbsorbRowFormat, playerHealth.AbsorbLevel, ArmorRowFull(path.TotalUpgrades, cap, limit));
            rechargeText.text = path.CanUpgradeRecharge
                ? ArmorRow(theme.loadoutArmorRechargeRowFormat, playerHealth.RechargeLevel, $"{limit} ({priceLine})")
                : ArmorRow(theme.loadoutArmorRechargeRowFormat, playerHealth.RechargeLevel, ArmorRowFull(path.TotalUpgrades, cap, limit));
            absorbText.color = path.CanUpgradeAbsorb && gateBlocked ? theme.mutedTextColor : theme.textColor;
            rechargeText.color = path.CanUpgradeRecharge && gateBlocked ? theme.mutedTextColor : theme.textColor;

            absorbButton.interactable = path.CanUpgradeAbsorb;
            rechargeButton.interactable = path.CanUpgradeRecharge;
        }

        /// <summary>One armour row's text from its UiTheme format: {0} = this row's level, {1} = the status.</summary>
        private static string ArmorRow(string format, int level, string status) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, format, level, status);

        /// <summary>What a row says when it cannot be bought: the shared limit's "N of N (max)" if the budget is
        /// spent, otherwise that this row is at its own top level.</summary>
        private string ArmorRowFull(int bought, int max, string limit) =>
            bought >= max ? limit : theme.loadoutArmorTopLevelText;

        // ============================================================================================
        // Abilities - Mobility, Attachment, Ultimate, each a heading and a wrapping grid of cards from the catalogue.
        // No upgrade tree: any non-debug ability is pickable any time, so there is no Owned/Locked state.
        // ============================================================================================

        private void OnAbilityCardClicked(AbilitySlot slot, int abilityId)
        {
            if (playerLoadout == null || abilityRunner == null)
                return;
            int equippedId = abilityRunner.EquippedId(slot);
            if (equippedId == abilityId)
                return; // Already equipped: no-op on self-click, as OnWeaponNodeClicked.

            // ShopRules.AbilityPrice reads 0 for a first pick into an empty Mobility/Attachment slot (the free starting
            // kit: the prefab leaves those slots empty) and the card's real GoldCost otherwise; Ultimate is never free.
            AbilityDefinition def = abilities != null ? abilities.Resolve(abilityId) : null;
            int goldCost = def != null ? def.GoldCost : 0;
            bool slotIsEmpty = equippedId == LoadoutProperties.Empty;
            int price = ShopRules.AbilityPrice(slotIsEmpty, slot, goldCost);
            ShopContext ctx = CurrentShopContext();
            if (RefuseIfClosed(ctx, abilityId))
                return;
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
                // No ledger entry: abilities have no sell-back path (GDD silent on it).
                if (price > 0 && (goldWallet == null || !goldWallet.TrySpend(price)))
                    return;
                chargedPrice = price;
            }

            playerLoadout.SetAbility(slot, abilityId);
            Purchased?.Invoke(CategoryFor(slot), abilityId, chargedPrice, goldWallet != null ? goldWallet.Balance : 0, free);
            Refresh();
        }

        /// <summary>This screen's three ability slots as the telemetry PurchaseCategory. Primary never reaches here
        /// (LoadoutAbilitySlotOrder), so Attachment is an arbitrary but harmless fallback rather than throwing.</summary>
        private static PurchaseCategory CategoryFor(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Mobility: return PurchaseCategory.Mobility;
                case AbilitySlot.Ultimate: return PurchaseCategory.Ultimate;
                default: return PurchaseCategory.Attachment;
            }
        }

        /// <summary>Equipped gets the weapon tree's Equipped look and label; every other card shows its price (ShopPricing.
        /// PriceLine of ShopRules.AbilityPrice) and, while shop-blocked, its OWN look (Loadout Shop Blocked Colour) and
        /// stays interactable (see StyleNode).</summary>
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
                string suffix = equipped ? "Equipped" : ctx.PriceLine(price, block);
                ui.label.text = def != null ? $"{def.DisplayName}\n<size={theme.loadoutPriceLineSizePercent}%>{suffix}</size>" : "";
                ui.outer.color = equipped ? theme.highlightColor : Color.clear;
                ui.inner.color = shopBlocked ? theme.loadoutShopBlockedColor : theme.loadoutSelectableColor;
                ui.label.color = shopBlocked ? theme.mutedTextColor : theme.textColor;
            }

            if (ultimateEmptyLabel != null)
                ultimateEmptyLabel.text = abilityRunner.EquippedId(AbilitySlot.Ultimate) == LoadoutProperties.Empty ? "Buy an ultimate" : "";
        }

        // ============================================================================================
        // Pages - the two tabs at the top switch between the weapon tree and the abilities/armor.
        // ============================================================================================

        /// <summary>Shows one page, lights its tab and remembers the choice for the next open. Any pop-up is dropped (the
        /// item under the pointer went away). Nothing bought or priced depends on the page: both are refreshed together.</summary>
        public void ShowPage(ShopPage page)
        {
            if (weaponsPageRoot == null || abilitiesPageRoot == null)
                return;

            pageMemory.Remember(page);
            shownPage = pageMemory.Last;
            weaponsPageRoot.SetActive(shownPage == ShopPage.Weapons);
            abilitiesPageRoot.SetActive(shownPage == ShopPage.AbilitiesAndArmor);
            weaponsTabImage.color = shownPage == ShopPage.Weapons ? theme.loadoutTabActiveColor : theme.loadoutTabInactiveColor;
            abilitiesTabImage.color = shownPage == ShopPage.AbilitiesAndArmor ? theme.loadoutTabActiveColor : theme.loadoutTabInactiveColor;
            HideTooltip();
        }
    }
}
