using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Overpower.Data;

namespace Overpower.UI
{
    /// <summary>
    /// Task 2.5b review, fix 7 (split): the pure UI-CONSTRUCTION half of LoadoutScreen - every method
    /// that only builds GameObjects/components and wires their listeners to methods that live on the
    /// other half, with no purchase/refresh/hover-content logic of its own. Pulled out of
    /// LoadoutScreen.cs once that file passed 1400 lines, so a designer looking for "how is this
    /// screen laid out" and one looking for "what does clicking this actually do" each have a
    /// shorter file to read. A plain code move: nothing here changed behaviour, and every field/
    /// method it touches (theme, weaponNodes, abilityCards, OnWeaponNodeClicked, ShowWeaponHover,
    /// RefreshHeader's own labels, etc.) still lives on the other partial, LoadoutScreen.cs - partial
    /// classes share one field list, so there is nothing to pass between the two files.
    ///
    /// What lives HERE: the weapon tree's node/column builders (BuildWeaponTreeUi,
    /// BuildDescendantColumn, BuildNodeButton), the armor section's builders (BuildArmorSection,
    /// BuildArmorRow), the ability column's builders (BuildAbilitiesUi, SlotHeading,
    /// BuildAbilityCard), and the screen's own top-level construction (BuildUi, BuildScreenCanvas -
    /// including the hover-description panel's labels, BuildColumn, BuildTitleRow, BuildHeaderRow,
    /// BuildToggleButtonCanvas) plus their shared low-level helpers (AddSectionHeader, AddButton,
    /// AddStretchedLabel, AddLabel, ApplyOutline, EnsureEventSystem).
    ///
    /// What stays on LoadoutScreen.cs: Awake/Update/Open/Close/Refresh and every click handler
    /// (OnWeaponNodeClicked, TryBuyArmorUpgrade, OnAbilityCardClicked, ...), every Refresh* method
    /// that repaints already-built UI from live state, and the hover-CONTENT methods (ShowWeaponHover,
    /// WeaponNumbersText, ShowAbilityHover, AbilityNumbersText, Compact) - those read live
    /// asset/module data into the hover strip's text, which is a world away from building the strip's
    /// GameObjects in the first place (that part - BuildScreenCanvas's "Hover description" region -
    /// is the one piece of construction that stayed textually inside BuildScreenCanvas rather than
    /// becoming its own method, so it moved along with it).
    /// </summary>
    public partial class LoadoutScreen
    {
        // ============================================================================================
        // Weapon tree UI (rules from Overpower.Combat.WeaponUpgradeTree, Task 8) - construction only;
        // OnWeaponNodeClicked/RefreshWeaponTree/StyleNode (the click/repaint side) stay on the other
        // partial.
        // ============================================================================================

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

        // ============================================================================================
        // Armor UI - construction only; the click handlers, TryBuyArmorUpgrade and RefreshArmor stay
        // on the other partial (see its own "Armor" section banner).
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

        // ============================================================================================
        // Abilities UI (right column) - construction only; OnAbilityCardClicked/RefreshAbilities (the
        // click/repaint side) stay on the other partial (see its own "Abilities" section banner).
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

        // ============================================================================================
        // UI construction - the screen's own top-level assembly, plus the shared low-level helpers
        // every builder method above (and here) uses.
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
            // Its OWN colour - see Loadout Panel Colour's tooltip. The HUD has no panel behind it at all any more
            // (HUD step 2); this modal still does, because it deliberately hides the world behind it.
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

            // The bottom-right corner group, scaled by the same Hud Scale as the HUD panel (Tudor, 2026-09-17).
            // A wrapper rather than a scale on the button itself, because HUD step 4 hangs the gold readout above
            // this button from PlayerHud's own canvas with an identical wrapper: two roots with the same anchor,
            // the same pivot and the same scale stay aligned at any screen size, where two independently scaled
            // children would drift apart the moment either size changed.
            GameObject corner = new GameObject("Shop Corner", typeof(RectTransform));
            corner.transform.SetParent(canvasGo.transform, false);
            RectTransform cornerRt = corner.GetComponent<RectTransform>();
            cornerRt.anchorMin = cornerRt.anchorMax = cornerRt.pivot = new Vector2(1f, 0f);
            cornerRt.anchoredPosition = Vector2.zero;
            cornerRt.sizeDelta = Vector2.zero;
            corner.transform.localScale = Vector3.one * theme.hudScale;

            GameObject buttonGo = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            buttonGo.name = "Loadout Toggle Button";
            buttonGo.transform.SetParent(corner.transform, false);
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
        /// shares ONE Material instance instead of letting TMP auto-clone one per label. The outline,
        /// weight and shadow numbers live on UiTheme.ApplyHudTextStyle (HUD step 2).</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (loadoutTextMaterial == null)
            {
                loadoutTextMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(loadoutTextMaterial);
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
