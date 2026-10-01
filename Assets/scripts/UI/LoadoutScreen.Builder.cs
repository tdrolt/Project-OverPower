using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.Match;

namespace Overpower.UI
{
    /// <summary>
    /// Task 2.5b review, fix 7 (split): the pure UI-CONSTRUCTION half of LoadoutScreen - every method
    /// that only builds GameObjects/components and wires their listeners to methods that live on the
    /// other half, with no purchase/refresh/hover-content logic of its own. Pulled out of
    /// LoadoutScreen.cs once that file passed 1400 lines, so a designer looking for "how is this
    /// screen laid out" and one looking for "what does clicking this actually do" each have a
    /// shorter file to read. Since Task 13 it also builds the two pages and their tabs (the weapon tree's family-and-two-upgrades rows, the
    /// armor rows and the ability columns); every field/
    /// method it touches (theme, weaponNodes, abilityCards, OnWeaponNodeClicked,
    /// RefreshHeader's own labels, etc.) still lives on the other partial, LoadoutScreen.cs - partial
    /// classes share one field list, so there is nothing to pass between the two files.
    ///
    /// What lives HERE: the weapon tree's node/column builders (BuildWeaponTreeUi,
    /// BuildDescendantColumn, BuildNodeButton), the armor section's builders (BuildArmorSection,
    /// BuildArmorRow), the ability column's builders (BuildAbilitiesUi, SlotHeading,
    /// BuildAbilityCard), and the screen's own top-level construction (BuildUi, BuildScreenCanvas -
    /// including the two pages and their tabs (Task 13), BuildColumn, BuildTitleRow, BuildHeaderRow,
    /// BuildTabRow, BuildToggleButtonCanvas) plus their shared low-level helpers (AddSectionHeader, AddButton,
    /// AddLabel, ApplyOutline, EnsureEventSystem).
    ///
    /// What stays on LoadoutScreen.cs: Awake/Update/Open/Close/Refresh and every click handler
    /// (OnWeaponNodeClicked, TryBuyArmorUpgrade, OnAbilityCardClicked, ...), every Refresh* method
    /// that repaints already-built UI from live state, and the pop-up's content and timing (LoadoutScreen.Tooltip.cs,
    /// ShopItemNumbers) - those read live asset/module data into the hover pop-up's text, a world away from building
    /// the screen's GameObjects in the first place.
    /// </summary>
    public partial class LoadoutScreen
    {
        // ============================================================================================
        // Weapon tree UI (rules from Overpower.Combat.WeaponUpgradeTree, Task 8) - construction only;
        // OnWeaponNodeClicked/RefreshWeaponTree/StyleNode (the click/repaint side) stay on the other
        // partial.
        // ============================================================================================

        /// <summary>Root centred on its own row; its direct children in a row beneath it (id order); each of THOSE
        /// children's own children in a row beneath it, side by side (Tudor, Task 13: the two upgrades of a family sit next
        /// to each other, not stacked), recursively - see BuildDescendantColumn. Today's data is exactly three tiers with two
        /// children per branch (Baseline -> {Rocket, Burst, SMG, Laser} -> two upgrades each), but nothing here assumes
        /// that shape: a branch with one child gets a row one node wide, a branch with three gets one three wide, and a
        /// fourth tier (an upgrade gaining its own child) simply adds one row further down - a designer adding weapon
        /// assets never needs a layout change here, only a new asset with the right Parent.</summary>
        private void BuildWeaponTreeUi(Transform page)
        {
            weaponNodes.Clear();
            if (weapons == null || tree.RootId < 0)
                return; // BuildWeaponTree already logged why (no catalogue, or no rootless weapon).

            WeaponDefinition rootDef = weapons.Resolve(tree.RootId);
            if (rootDef == null)
                return;

            GameObject rootRow = new GameObject("Weapon Tree Root", typeof(RectTransform));
            rootRow.transform.SetParent(page, false);
            HorizontalLayoutGroup rootLayout = rootRow.AddComponent<HorizontalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.MiddleCenter;
            rootLayout.childControlWidth = rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = rootLayout.childForceExpandHeight = false;
            weaponNodes[rootDef.Id] = BuildNodeButton(rootRow.transform, rootDef);

            // Room between the root and the four weapons for the arrows to read (Task 5b-2, loadoutTreeRowGap).
            GameObject rootGap = new GameObject("Weapon Tree Root Gap", typeof(RectTransform));
            rootGap.transform.SetParent(page, false);
            LayoutElement rootGapLe = rootGap.AddComponent<LayoutElement>();
            rootGapLe.preferredHeight = rootGapLe.minHeight = Mathf.Max(0f, theme.loadoutTreeRowGap - theme.loadoutPanelPadding * 0.5f);

            GameObject branchesRow = new GameObject("Weapon Tree Branches", typeof(RectTransform));
            branchesRow.transform.SetParent(page, false);
            HorizontalLayoutGroup branchesLayout = branchesRow.AddComponent<HorizontalLayoutGroup>();
            branchesLayout.spacing = theme.loadoutTreeColumnGap;
            branchesLayout.childAlignment = TextAnchor.UpperCenter;
            branchesLayout.childControlWidth = branchesLayout.childControlHeight = true;
            branchesLayout.childForceExpandWidth = branchesLayout.childForceExpandHeight = false;

            foreach (int childId in tree.ChildrenOf(rootDef.Id))
                BuildDescendantColumn(branchesRow.transform, childId);

            BuildTreeArrows(page);
        }

        /// <summary>Task 5b-2 (D6): one arrow per parent -> child pair of the tree data (WeaponUpgradeTree.Edges),
        /// drawn behind the nodes by a single graphic that reads the built nodes' positions - so a new weapon asset
        /// with a Parent gets its arrow with no hand placement. Never a raycast target: it must not block a node.</summary>
        private void BuildTreeArrows(Transform page)
        {
            GameObject go = new GameObject("Weapon Tree Arrows", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(page, false);
            go.transform.SetAsFirstSibling(); // Behind every node and label.
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            TreeArrowGraphic graphic = go.AddComponent<TreeArrowGraphic>();
            graphic.raycastTarget = false;
            graphic.color = theme.loadoutArrowColor;
            graphic.LineWidth = theme.loadoutArrowWidth;
            graphic.HeadSize = theme.loadoutArrowHeadSize;
            graphic.SideLaneWidth = theme.loadoutTreeColumnGap * theme.loadoutArrowSideLaneFactor;

            foreach (var (parentId, childId) in tree.Edges())
            {
                if (!weaponNodes.TryGetValue(parentId, out WeaponNodeUi parentNode) || !weaponNodes.TryGetValue(childId, out WeaponNodeUi childNode))
                    continue;
                graphic.Add(parentNode.outer.rectTransform, childNode.outer.rectTransform);
            }
        }

        /// <summary>One branch: a vertical column holding its node and, beneath it, ONE row with a nested column per child
        /// side by side, recursively - see BuildWeaponTreeUi's comment for why this copes with any child count at any
        /// depth with no code change. The node sits centred above its row of children.</summary>
        private void BuildDescendantColumn(Transform parent, int weaponId)
        {
            WeaponDefinition def = weapons.Resolve(weaponId);
            if (def == null)
                return;

            GameObject column = new GameObject($"Weapon Branch {weaponId}", typeof(RectTransform));
            column.transform.SetParent(parent, false);
            VerticalLayoutGroup layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = theme.loadoutTreeRowGap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            weaponNodes[weaponId] = BuildNodeButton(column.transform, def);

            Transform childrenRow = null;
            foreach (int childId in tree.ChildrenOf(weaponId))
            {
                if (childrenRow == null)
                {
                    GameObject row = new GameObject($"Weapon Branch {weaponId} Upgrades", typeof(RectTransform));
                    row.transform.SetParent(column.transform, false);
                    HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                    rowLayout.spacing = theme.loadoutNodeSpacing;
                    rowLayout.childAlignment = TextAnchor.UpperCenter;
                    rowLayout.childControlWidth = rowLayout.childControlHeight = true;
                    rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
                    childrenRow = row.transform;
                }
                BuildDescendantColumn(childrenRow, childId);
            }
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

            AddPopUp(go, "w:" + def.Id, () => WeaponPopUpText(def));

            return new WeaponNodeUi { button = button, outer = outer, inner = inner, label = label };
        }

        // ============================================================================================
        // Armor UI - construction only; the click handlers, TryBuyArmorUpgrade and RefreshArmor stay
        // on the other partial (see its own "Armor" section banner).
        // ============================================================================================

        /// <summary>The armor rows (Absorb, Recharge) and Reset Armor side by side, under an "Armor" heading, at the top
        /// of the Abilities &amp; Armor page. Each row is its label with its own "+" right after it; a wide gap separates
        /// the two rows so a "+" never reads as belonging to the other upgrade (Task 14b). Reset Armor sits at the right edge.</summary>
        private void BuildArmorSection(Transform page)
        {
            AddSectionHeader(page, "Armor");

            GameObject group = new GameObject("Armor Rows", typeof(RectTransform));
            group.transform.SetParent(page, false);
            HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.loadoutNodeSpacing; // Between the rows group, the spacer and Reset Armor.
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            // The two rows sit in their own group, so the wide Armor Row Gap is only between them, not around the spacer.
            GameObject rows = new GameObject("Armor Rows Inner", typeof(RectTransform));
            rows.transform.SetParent(group.transform, false);
            HorizontalLayoutGroup rowsLayout = rows.AddComponent<HorizontalLayoutGroup>();
            rowsLayout.spacing = theme.loadoutArmorRowGap;
            rowsLayout.childAlignment = TextAnchor.MiddleLeft;
            rowsLayout.childControlWidth = rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = rowsLayout.childForceExpandHeight = false;

            absorbText = BuildArmorRow(rows.transform, out absorbButton, OnAbsorbClicked, "armor:absorb", () => ArmorPopUpText(true));
            rechargeText = BuildArmorRow(rows.transform, out rechargeButton, OnRechargeClicked, "armor:recharge", () => ArmorPopUpText(false));

            // Pushes Reset Armor to the right edge, so the free width is not spread between the two rows.
            GameObject spacer = new GameObject("Armor Rows Spacer", typeof(RectTransform));
            spacer.transform.SetParent(group.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button resetArmorButton = AddButton(group.transform, "Reset Armor", OnResetArmorClicked, theme.loadoutSmallButtonWidth, theme.loadoutSmallButtonHeight);
            resetArmorLabel = resetArmorButton.GetComponentInChildren<TextMeshProUGUI>();

            // Room between the armor rows and the ability columns below them.
            GameObject gap = new GameObject("Armor Section Gap", typeof(RectTransform));
            gap.transform.SetParent(page, false);
            LayoutElement gapLe = gap.AddComponent<LayoutElement>();
            gapLe.preferredHeight = theme.loadoutSectionGap;
            gapLe.minHeight = theme.loadoutSectionGap;
        }

        private TextMeshProUGUI BuildArmorRow(Transform parent, out Button plusButton, UnityEngine.Events.UnityAction onClick,
            string popUpKey, System.Func<string> popUpText)
        {
            GameObject row = new GameObject("Armor Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.loadoutArmorPlusGap; // The "+" sits this close after its own label.
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            // Invisible, but it is what the pointer lands on between the text and the + button - so the pop-up opens
            // on the whole row, not only on the button (Task 13).
            Image hit = row.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            TextMeshProUGUI label = AddLabel(row.transform, "", theme.bodyTextSize, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            // No flexible width on the label or the row: the label is as wide as its text, so the + follows it directly.

            plusButton = AddButton(row.transform, "+", onClick, theme.loadoutStepperButtonSize, theme.loadoutStepperButtonSize, theme.loadoutStepperFontSize);

            AddPopUp(row, popUpKey, popUpText);
            return label;
        }

        // ============================================================================================
        // Abilities UI (right column) - construction only; OnAbilityCardClicked/RefreshAbilities (the
        // click/repaint side) stay on the other partial (see its own "Abilities" section banner).
        // ============================================================================================

        private void BuildAbilitiesUi(Transform page)
        {
            abilityCards.Clear();
            if (abilities == null)
                return; // Awake already logged why.

            GameObject columns = new GameObject("Ability Columns", typeof(RectTransform));
            columns.transform.SetParent(page, false);
            HorizontalLayoutGroup columnsLayout = columns.AddComponent<HorizontalLayoutGroup>();
            columnsLayout.spacing = theme.loadoutAbilityColumnGap;
            columnsLayout.childAlignment = TextAnchor.UpperCenter;
            columnsLayout.childControlWidth = columnsLayout.childControlHeight = true;
            columnsLayout.childForceExpandWidth = columnsLayout.childForceExpandHeight = false;

            // Two cards across in every column.
            float columnWidth = 2f * theme.loadoutAbilityCardWidth + theme.loadoutNodeSpacing;

            foreach (AbilitySlot slot in LoadoutAbilitySlotOrder)
            {
                Transform column = BuildColumn(columns.transform, columnWidth).transform;
                AddSectionHeader(column, SlotHeading(slot));

                // Task 2.5b: the Ultimate slot is the one slot that can read Equipped on NO card at
                // all (starts empty when the economy is on - Task 2.5a) - without this, an empty
                // ultimate column would say nothing at all about why nothing is highlighted.
                if (slot == AbilitySlot.Ultimate)
                {
                    ultimateEmptyLabel = AddLabel(column, "", theme.smallTextSize, FontStyles.Normal);
                    ultimateEmptyLabel.alignment = TextAlignmentOptions.MidlineLeft;
                    ultimateEmptyLabel.color = theme.overheatWarningColor;
                    ultimateEmptyLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                }

                List<AbilityDefinition> slotAbilities = abilities.ForSlot(slot);
                slotAbilities.RemoveAll(a => a == null || a.Id >= 900); // Debug abilities stay in F1 only.
                slotAbilities.Sort((a, b) => a.Id.CompareTo(b.Id));

                GameObject grid = new GameObject($"{slot} Ability Grid", typeof(RectTransform));
                grid.transform.SetParent(column, false);
                // Pin the width and let the group compute its own height - a GridLayoutGroup needs its own
                // rect width resolved before it can place its cards (see BuildColumn).
                LayoutElement gridLe = grid.AddComponent<LayoutElement>();
                gridLe.preferredWidth = columnWidth;
                GridLayoutGroup gridLayout = grid.AddComponent<GridLayoutGroup>();
                gridLayout.cellSize = new Vector2(theme.loadoutAbilityCardWidth, theme.loadoutAbilityCardHeight);
                gridLayout.spacing = new Vector2(theme.loadoutNodeSpacing, theme.loadoutNodeSpacing);
                gridLayout.childAlignment = TextAnchor.UpperLeft;
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = 2;

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
                case AbilitySlot.Attachment: return "Attachment — RMB";
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

            TextMeshProUGUI label = AddLabel(innerGo.transform, def.DisplayName, theme.bodyTextSize, FontStyles.Normal);
            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            AbilitySlot slot = def.Slot;
            int abilityId = def.Id;
            button.onClick.AddListener(() => OnAbilityCardClicked(slot, abilityId));

            AddPopUp(go, "a:" + def.Slot + ":" + def.Id, () => AbilityPopUpText(def));

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
            panelRect = panelRt;
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
            BuildHeaderRow(panel.transform); // Gold and the status line: above the tabs, so both pages show them.
            BuildTabRow(panel.transform);

            // One fixed-size area for both pages, so the panel never changes size when a tab is pressed.
            GameObject pages = new GameObject("Pages", typeof(RectTransform));
            pages.transform.SetParent(panel.transform, false);
            LayoutElement pagesLe = pages.AddComponent<LayoutElement>();
            pagesLe.preferredWidth = pagesLe.minWidth = theme.loadoutPageWidth;
            pagesLe.preferredHeight = pagesLe.minHeight = theme.loadoutPageHeight;

            weaponsPageRoot = BuildPage(pages.transform, "Weapons Page");
            BuildWeaponTreeUi(weaponsPageRoot.transform);
            Button resetWeaponButton = AddButton(weaponsPageRoot.transform, "Reset Weapon", OnResetWeaponClicked, theme.loadoutSmallButtonWidth, theme.loadoutSmallButtonHeight);
            resetWeaponLabel = resetWeaponButton.GetComponentInChildren<TextMeshProUGUI>();

            abilitiesPageRoot = BuildPage(pages.transform, "Abilities and Armor Page");
            BuildArmorSection(abilitiesPageRoot.transform);
            BuildAbilitiesUi(abilitiesPageRoot.transform);

            ShowPage(pageMemory.Last);
            FitPanelToCanvas();
        }

        /// <summary>One page: fills the Pages area and stacks its content top-down, centred across.</summary>
        private GameObject BuildPage(Transform parent, string pageName)
        {
            GameObject page = new GameObject(pageName, typeof(RectTransform));
            page.transform.SetParent(parent, false);
            RectTransform rt = page.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = page.AddComponent<VerticalLayoutGroup>();
            layout.spacing = theme.loadoutPanelPadding * 0.5f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return page;
        }

        /// <summary>The two tabs under the gold line: "Weapons" and "Abilities &amp; Armor". The lit one is the page
        /// on screen (ShowPage colours them).</summary>
        private void BuildTabRow(Transform parent)
        {
            GameObject row = new GameObject("Tab Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = theme.loadoutNodeSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            Button weaponsTab = AddButton(row.transform, theme.loadoutTabWeaponsText, () => ShowPage(ShopPage.Weapons), theme.loadoutTabWidth, theme.loadoutTabHeight);
            weaponsTabImage = weaponsTab.GetComponent<Image>();
            Button abilitiesTab = AddButton(row.transform, theme.loadoutTabAbilitiesArmorText, () => ShowPage(ShopPage.AbilitiesAndArmor), theme.loadoutTabWidth, theme.loadoutTabHeight);
            abilitiesTabImage = abilitiesTab.GetComponent<Image>();
        }

        private GameObject BuildColumn(Transform parent, float width)
        {
            GameObject column = new GameObject("Column", typeof(RectTransform));
            column.transform.SetParent(parent, false);
            LayoutElement le = column.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f; // A stretched heading inside must not make the column grab spare width (its cards would sit off-centre).
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
            // Stretch to the width of what it heads, so the left-aligned text starts at that section's own left edge
            // (unstretched, a layout group centres a label narrower than its column - Task 13 capture).
            header.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
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
                if (width > 0f) { le.preferredWidth = width; le.minWidth = width; } // Keeps its width when space runs short.
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
