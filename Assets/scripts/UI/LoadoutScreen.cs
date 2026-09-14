using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Combat;
using Overpower.Data;
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
    /// TASK 9B HOOKS: rightColumnContent (an empty column the same width as the weapon tree's own,
    /// sitting where ability picks belong) and descriptionPanelContent (an empty, zero-height strip
    /// under both columns, where a hovered item's live-numbers description will go) exist now and
    /// are deliberately left empty - see Awake and BuildScreenCanvas.
    /// </summary>
    public class LoadoutScreen : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Colours, sizes and fonts for this screen - the same asset the HUD and aim cone use.")]
        private UiTheme theme;

        [SerializeField, Tooltip("Every weapon in the game and its upgrade-tree parent link. Add a weapon asset with a Parent and it appears in the tree with no code change.")]
        private WeaponCatalogue weapons;

        [SerializeField, Tooltip("Every ability in the game. Not drawn yet (Task 9b fills in the right-hand column) but wired here now so that task is a content change, not a wiring change.")]
        private AbilityCatalogue abilities;

        [SerializeField, Tooltip("Armor tiers asset - the same one the F1 panel reads, so both call the exact same upgrade rule (ArmorLoadoutActions).")]
        private ArmorConfig armorConfig;

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

        /// <summary>Task 9b's right-hand column container - see the class comment.</summary>
        private Transform rightColumnContent;

        /// <summary>Task 9b's hover-description strip - see the class comment.</summary>
        private Transform descriptionPanelContent;

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
            // Optional: not every rig this component might run on has one, and there is nothing
            // this screen cannot do without it besides the match-over gate below.
            matchUI = GetComponent<MatchUI>();

            if (playerHealth == null || playerLoadout == null || weaponFiring == null || inputRouter == null)
                Debug.LogError($"[LoadoutScreen] {name}: missing PlayerHealth/PlayerLoadout/WeaponFiring/PlayerInputRouter on this player - the loadout screen cannot apply choices.");
            if (weapons == null)
                Debug.LogError($"[LoadoutScreen] {name}: WeaponCatalogue is not assigned - the weapon tree will be empty.");
            if (armorConfig == null)
                Debug.LogError($"[LoadoutScreen] {name}: ArmorConfig is not assigned - armor upgrades will always be refused.");

            BuildWeaponTree();
            BuildUi();
            screenRoot.SetActive(false);

            if (lifecycle != null)
                lifecycle.AliveChanged += HandleAliveChanged;
            if (inputRouter != null)
                inputRouter.ShopToggled += Toggle;
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
            if (matchUI != null && matchUI.MatchOver)
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
            if (weaponId != lastKnownWeaponId || absorbLevel != lastKnownAbsorbLevel || rechargeLevel != lastKnownRechargeLevel)
                Refresh();
        }

        private void HandleAliveChanged(bool alive)
        {
            if (!alive && isOpenLocal)
                Close();
        }

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
        /// Open and after every click that changes something - there is no "loadout changed" event
        /// to subscribe to instead (WeaponFiring has none, and while the screen is open only this
        /// owner's own clicks can change anything anyway, so polling every frame would answer a
        /// question that never changes between clicks).</summary>
        private void Refresh()
        {
            RefreshWeaponTree();
            RefreshArmor();

            // Snapshot what was just drawn, so Update()'s poll (Task 9a review) only calls back in
            // here once something ACTUALLY changes since this Refresh, from any path - Open, a
            // click on this screen, or Update() catching an external change.
            lastKnownWeaponId = CurrentWeaponId();
            lastKnownAbsorbLevel = playerHealth != null ? playerHealth.AbsorbLevel : -1;
            lastKnownRechargeLevel = playerHealth != null ? playerHealth.RechargeLevel : -1;
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

            return new WeaponNodeUi { button = button, outer = outer, inner = inner, label = label };
        }

        private void OnWeaponNodeClicked(int weaponId)
        {
            int equipped = CurrentWeaponId();
            // Guards regardless of the button's own interactable flag (only Locked nodes are set
            // non-interactable - see StyleNode) - clicking Equipped or Owned must simply do nothing.
            if (!tree.CanUpgrade(equipped, weaponId))
                return;

            playerLoadout?.SetWeapon(weaponId);
            Refresh();
        }

        private void OnResetWeaponClicked()
        {
            if (tree.RootId < 0)
                return;

            playerLoadout?.SetWeapon(tree.RootId);
            Refresh();
        }

        private int CurrentWeaponId() =>
            weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : tree.RootId;

        private void RefreshWeaponTree()
        {
            if (tree == null)
                return;

            int equipped = CurrentWeaponId();
            foreach (var pair in weaponNodes)
                StyleNode(pair.Value, tree.StateOf(pair.Key, equipped));
        }

        private void StyleNode(WeaponNodeUi ui, UpgradeNodeState state)
        {
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
                    ui.inner.color = theme.loadoutSelectableColor;
                    ui.label.color = theme.textColor;
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
            AddSectionHeader(leftColumn, "Armor");

            absorbText = BuildArmorRow(leftColumn, out absorbButton, OnAbsorbClicked);
            rechargeText = BuildArmorRow(leftColumn, out rechargeButton, OnRechargeClicked);

            AddButton(leftColumn, "Reset Armor", OnResetArmorClicked, theme.loadoutSmallButtonWidth, theme.loadoutSmallButtonHeight);
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

            plusButton = AddButton(row.transform, "+", onClick, theme.loadoutStepperButtonSize, theme.loadoutStepperButtonSize);

            return label;
        }

        private void OnAbsorbClicked()
        {
            ArmorLoadoutActions.TryUpgrade(playerHealth, playerLoadout, armorConfig, upgradeAbsorb: true);
            Refresh();
        }

        private void OnRechargeClicked()
        {
            ArmorLoadoutActions.TryUpgrade(playerHealth, playerLoadout, armorConfig, upgradeAbsorb: false);
            Refresh();
        }

        private void OnResetArmorClicked()
        {
            ArmorLoadoutActions.Reset(playerLoadout);
            Refresh();
        }

        /// <summary>Refused-upgrade choice: DISABLE the + button rather than a muted note, computed
        /// up front from the same ArmorUpgradePath rule TryUpgrade itself would apply, so a click
        /// that would be refused is never even offered - unlike the F1 panel, whose console log is a
        /// designer convenience this player-facing screen does not need.</summary>
        private void RefreshArmor()
        {
            if (playerHealth == null || armorConfig == null)
                return;

            int absorbMax = Mathf.Max(0, armorConfig.AbsorbLevelCount - 1);
            int rechargeMax = Mathf.Max(0, armorConfig.RechargeLevelCount - 1);
            absorbText.text = $"Absorb {playerHealth.AbsorbLevel}/{absorbMax}";
            rechargeText.text = $"Recharge {playerHealth.RechargeLevel}/{rechargeMax}";

            var path = new ArmorUpgradePath(armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel);
            absorbButton.interactable = path.CanUpgradeAbsorb;
            rechargeButton.interactable = path.CanUpgradeRecharge;
        }

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
            panelBackground.color = theme.panelColor;
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

            GameObject contentRow = new GameObject("Content", typeof(RectTransform));
            contentRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup contentLayout = contentRow.AddComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = theme.loadoutPanelPadding;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = contentLayout.childForceExpandHeight = false;

            GameObject leftColumn = BuildColumn(contentRow.transform, theme.loadoutLeftColumnWidth);
            GameObject rightColumn = BuildColumn(contentRow.transform, theme.loadoutRightColumnWidth);
            rightColumnContent = rightColumn.transform;
            // Task 9b's own hook - a placeholder so an empty column does not look broken in the
            // meantime; 9b replaces this label with real ability cards.
            TextMeshProUGUI abilitiesPlaceholder = AddLabel(rightColumn.transform, "Abilities (Task 9b)", theme.smallTextSize, FontStyles.Italic);
            abilitiesPlaceholder.color = theme.mutedTextColor;

            AddSectionHeader(leftColumn.transform, "Weapons");
            BuildWeaponTreeUi(leftColumn.transform);
            AddButton(leftColumn.transform, "Reset Weapon", OnResetWeaponClicked, theme.loadoutSmallButtonWidth, theme.loadoutSmallButtonHeight);

            BuildArmorSection(leftColumn.transform);

            // Task 9b's hover-description hook - present, empty, and zero-height (no LayoutElement
            // means the layout group gives it no size) until that task gives it content.
            GameObject descriptionPanel = new GameObject("Description Panel", typeof(RectTransform));
            descriptionPanel.transform.SetParent(panel.transform, false);
            descriptionPanelContent = descriptionPanel.transform;
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

            AddButton(row.transform, "X", Toggle, theme.loadoutStepperButtonSize, theme.loadoutStepperButtonSize);
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

            buttonGo.GetComponent<Button>().onClick.AddListener(Toggle);
        }

        private void AddSectionHeader(Transform parent, string text)
        {
            TextMeshProUGUI header = AddLabel(parent, text, theme.smallTextSize, FontStyles.Bold);
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.color = theme.mutedTextColor;
        }

        /// <summary>width/height of 0 (the default) leaves that axis to the layout group instead of
        /// pinning it - used for the title and section headers, which should stretch to their row's
        /// own width rather than carry a fixed one.</summary>
        private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width = 0f, float height = 0f)
        {
            GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = theme.barTrackColor;

            TextMeshProUGUI text = go.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label;
            if (theme.font != null)
                text.font = theme.font;
            text.fontSize = theme.bodyTextSize;
            text.color = theme.textColor;
            ApplyOutline(text);

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            if (width > 0f || height > 0f)
            {
                LayoutElement le = go.AddComponent<LayoutElement>();
                if (width > 0f) le.preferredWidth = width;
                if (height > 0f) le.preferredHeight = height;
            }

            return button;
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
