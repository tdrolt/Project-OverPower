using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.Telemetry;
using Overpower.Weapons;

namespace Overpower.TestRange
{
    /// <summary>A cooldown the "Reset Cooldowns" button can clear - today the local player's
    /// AbilityRunner, which refills every equipped ability's charges. Implementers register in
    /// OnEnable, unregister in OnDisable, and only for the local player: the button means "my
    /// cooldowns".</summary>
    public interface ITestRangeResettable
    {
        void ResetForTestRange();
    }

    /// <summary>File-local registry backing ITestRangeResettable, so the panel never needs to know
    /// what abilities exist.</summary>
    public static class TestRangeResetRegistry
    {
        private static readonly List<ITestRangeResettable> resettables = new List<ITestRangeResettable>();

        public static void Register(ITestRangeResettable r)
        {
            if (!resettables.Contains(r))
                resettables.Add(r);
        }

        public static void Unregister(ITestRangeResettable r) => resettables.Remove(r);
        public static void ResetAll() { foreach (var r in resettables) r.ResetForTestRange(); }
    }

    /// <summary>
    /// A live loadout switcher so one person can evaluate the whole kit alone, instead of reaching
    /// every weapon through a full match and a shop - a DESIGN instrument, not a debug menu: it is
    /// how a weapon's numbers get measured against a dummy rather than asserted on paper.
    ///
    /// Populates itself from WeaponCatalogue/AbilityCatalogue rather than a hardcoded list, so a
    /// weapon or ability asset added later appears here with no code change - see
    /// PopulateWeaponDropdown/PopulateAbilityDropdown.
    ///
    /// The UI tree is built here in code, not hand-authored in the scene, and lives entirely under
    /// one root (uiRoot): this project already lost a set of buttons to a click handler nobody
    /// wired up, because a hand-built hierarchy hides that mistake until someone clicks. Building
    /// in code puts each listener on the same line as the control it belongs to.
    ///
    /// Toggled with the raw keyboard (Keyboard.current), not an InputAction: this is a tool, not a
    /// game control, and must never appear in a future rebinding UI next to Primary/Equipment/
    /// Ultimate/Mobility.
    ///
    /// Gated entirely on GameplayConfig.TestRangeEnabled - false disables this component in Awake
    /// before it builds anything, so a designer can turn the tool off with one checkbox and leave
    /// no canvas, no key polling and no console spam in the shipping build.
    /// </summary>
    public class TestRangePanel : MonoBehaviour
    {
        [SerializeField, Tooltip("Match tuning asset. Also the on/off switch for the whole test " +
                 "range - see the class comment.")]
        private GameplayConfig gameplayConfig;

        [SerializeField, Tooltip("Armor tiers asset, read for tier 0's absorb value so the " +
                 "readout's time-to-kill target is the real 'health plus starting armor' figure, " +
                 "not a number typed into this script.")]
        private ArmorConfig armorConfig;

        [SerializeField, Tooltip("Every weapon in the game. Add an asset here and it appears in " +
                 "the dropdown with no script change.")]
        private WeaponCatalogue weaponCatalogue;

        [SerializeField, Tooltip("Every ability in the game, across all four slots. Add an asset " +
                 "here and it appears in its slot's dropdown with no script change; a slot with no " +
                 "abilities reads '(none available)'.")]
        private AbilityCatalogue abilityCatalogue;

        // Fixed once here and reused to build the three ability dropdowns identically, instead of
        // three near-duplicate blocks.
        private static readonly (string label, AbilitySlot slot)[] AbilitySlots =
        {
            ("Equipment", AbilitySlot.Equipment),
            ("Ultimate", AbilitySlot.Ultimate),
            ("Mobility", AbilitySlot.Mobility),
        };

        private GameObject uiRoot;
        private TMP_Dropdown weaponDropdown;
        private readonly TMP_Dropdown[] abilityDropdowns = new TMP_Dropdown[AbilitySlots.Length];
        private TextMeshProUGUI readoutText;
        private TextMeshProUGUI telemetryStatusText;
        private readonly List<WeaponDefinition> weaponOptions = new List<WeaponDefinition>();

        // Per ability dropdown, the ability behind each option. Option 0 is always "(none)", so an
        // option index is one more than its index in here.
        private readonly List<AbilityDefinition>[] abilityOptions =
        {
            new List<AbilityDefinition>(), new List<AbilityDefinition>(), new List<AbilityDefinition>()
        };

        private bool visible;
        private GameObject cachedLocalPlayer;
        private PlayerInputRouter suppressedRouter;

        private void Awake()
        {
            if (gameplayConfig == null)
            {
                Debug.LogError($"[TestRangePanel] {name}: GameplayConfig is not assigned - cannot tell if the test range should exist, so it is disabling itself.");
                enabled = false;
                return;
            }

            if (!gameplayConfig.TestRangeEnabled)
            {
                enabled = false; // The whole point: flip the asset's checkbox and this never wakes up.
                return;
            }

            BuildUi();
            SetVisible(false);
        }

        private void OnDisable()
        {
            suppressedRouter?.SetToolFocus(this, false);
            suppressedRouter = null;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                SetVisible(!visible);

            UpdateInputSuppression(visible);

            if (visible)
            {
                RefreshReadout();
                RefreshTelemetryStatus();
            }
        }

        private void SetVisible(bool show)
        {
            visible = show;
            uiRoot.SetActive(show);
            if (show)
            {
                RefreshWeaponSelection();
                RefreshAbilitySelection();
            }
        }

        /// <summary>Keeps exactly one router suppressed - the local player's, re-resolved every
        /// frame the panel is open so a mid-session player change is never left stuck either way.
        /// Focus is keyed by "this" (PlayerInputRouter.SetToolFocus's owner parameter) so closing
        /// this panel can never release a claim the loadout screen is still holding, and vice
        /// versa - see that method's own comment.</summary>
        private void UpdateInputSuppression(bool wantSuppressed)
        {
            PlayerInputRouter router = wantSuppressed
                ? ResolveLocalPlayer()?.GetComponentInChildren<PlayerInputRouter>(true)
                : null;

            if (router != suppressedRouter)
            {
                suppressedRouter?.SetToolFocus(this, false);
                suppressedRouter = router;
            }

            suppressedRouter?.SetToolFocus(this, true);
        }

        private GameObject ResolveLocalPlayer()
        {
            if (cachedLocalPlayer != null)
                return cachedLocalPlayer;

            if (PhotonNetwork.LocalPlayer == null)
                return null;

            PhotonView view = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            cachedLocalPlayer = view != null ? view.gameObject : null;
            return cachedLocalPlayer;
        }

        // ---- UI construction ----

        private void BuildUi()
        {
            var canvasGo = new GameObject("Test Range Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // Above match UI and chat - a designer opened this on purpose.
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            uiRoot = canvasGo;

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(16f, -16f);
            panelRt.sizeDelta = new Vector2(420f, 0f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var res = new TMP_DefaultControls.Resources();
            AddLabel(panel.transform, "TEST RANGE  (F1 closes)", 20f, FontStyles.Bold);

            AddLabel(panel.transform, "Weapon", 14f, FontStyles.Normal);
            weaponDropdown = AddDropdown(panel.transform, res);
            weaponDropdown.onValueChanged.AddListener(OnWeaponSelected);

            for (int i = 0; i < AbilitySlots.Length; i++)
            {
                AddLabel(panel.transform, AbilitySlots[i].label, 14f, FontStyles.Normal);
                abilityDropdowns[i] = AddDropdown(panel.transform, res);
                int slotIndex = i; // Captured per dropdown - the loop variable itself would be 3 by click time.
                abilityDropdowns[i].onValueChanged.AddListener(option => OnAbilitySelected(slotIndex, option));
            }

            GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = rowLayout.childForceExpandWidth = true;
            AddButton(buttonRow.transform, "Reset Cooldowns", res, OnResetCooldownsClicked);
            AddButton(buttonRow.transform, "Heal", res, OnHealClicked);
            AddButton(buttonRow.transform, "Fill Ultimate", res, OnFillUltimateClicked);
            AddButton(buttonRow.transform, "+1000 Gold", res, OnAddGoldClicked);

            GameObject armorButtonRow = new GameObject("Armor Buttons", typeof(RectTransform));
            armorButtonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup armorRowLayout = armorButtonRow.AddComponent<HorizontalLayoutGroup>();
            armorRowLayout.spacing = 8f;
            armorRowLayout.childControlWidth = armorRowLayout.childForceExpandWidth = true;
            AddButton(armorButtonRow.transform, "+Absorb", res, OnAbsorbUpgradeClicked);
            AddButton(armorButtonRow.transform, "+Recharge", res, OnRechargeUpgradeClicked);
            AddButton(armorButtonRow.transform, "Reset Armor", res, OnResetArmorClicked);

            readoutText = AddLabel(panel.transform, "", 14f, FontStyles.Normal);

            GameObject telemetryButtonRow = new GameObject("Telemetry Buttons", typeof(RectTransform));
            telemetryButtonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup telemetryRowLayout = telemetryButtonRow.AddComponent<HorizontalLayoutGroup>();
            telemetryRowLayout.spacing = 8f;
            telemetryRowLayout.childControlWidth = telemetryRowLayout.childForceExpandWidth = true;
            AddButton(telemetryButtonRow.transform, "Open telemetry folder", res, OnOpenTelemetryFolderClicked);
            AddButton(telemetryButtonRow.transform, "Drop marker", res, OnDropMarkerClicked);

            telemetryStatusText = AddLabel(panel.transform, "Telemetry: off", 14f, FontStyles.Normal);

            PopulateWeaponDropdown();
            for (int i = 0; i < AbilitySlots.Length; i++)
                PopulateAbilityDropdown(i);
        }

        private static TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static TMP_Dropdown AddDropdown(Transform parent, TMP_DefaultControls.Resources res)
        {
            GameObject go = TMP_DefaultControls.CreateDropdown(res);
            go.transform.SetParent(parent, false);
            TMP_Dropdown dropdown = go.GetComponent<TMP_Dropdown>();
            // Fix 2 (Playtest polish review): a code-built Selectable keeps Unity's default
            // Automatic navigation, so a click selects it and the scene's Input System UI module
            // maps Enter to Submit on whatever is selected - Enter also opens chat
            // (chatmanager.cs), so without this, opening chat right after picking a dropdown
            // option quietly re-submitted that dropdown instead. Same fix as LoadoutScreen's
            // buttons, applied here too since this panel builds its own controls.
            dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
            return dropdown;
        }

        private static void AddButton(Transform parent, string label, TMP_DefaultControls.Resources res,
                                       UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            go.GetComponentInChildren<TextMeshProUGUI>().text = label;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // See AddDropdown's comment.
        }

        // ---- Weapon dropdown ----

        private void PopulateWeaponDropdown()
        {
            weaponOptions.Clear();
            var labels = new List<string>();

            if (weaponCatalogue != null)
            {
                foreach (WeaponDefinition weapon in weaponCatalogue.Weapons)
                {
                    if (weapon == null)
                        continue;

                    weaponOptions.Add(weapon);
                    // Id prefix, matching the ability dropdown below (fix 5) - weapons 6 (Burst -
                    // Charge) and 12 (Laser - Charge) both have DisplayName "Charge", which read
                    // as the same weapon listed twice before the id was added to tell them apart.
                    labels.Add($"{weapon.Id} {weapon.DisplayName}");
                }
            }

            weaponDropdown.ClearOptions();
            weaponDropdown.AddOptions(labels.Count > 0 ? labels : new List<string> { "(none available)" });
            weaponDropdown.interactable = labels.Count > 0;
        }

        private void OnWeaponSelected(int index)
        {
            if (index < 0 || index >= weaponOptions.Count)
                return;

            // Through PlayerLoadout, never WeaponFiring.SetWeapon directly: that would swap the gun on
            // this screen only, and every other client would keep drawing the old one.
            PlayerLoadout loadout = ResolveLocalPlayer()?.GetComponent<PlayerLoadout>();
            if (loadout == null)
            {
                Debug.LogWarning("[TestRangePanel] no local PlayerLoadout found - cannot switch weapon.");
                return;
            }

            loadout.SetWeapon(weaponOptions[index].Id);
        }

        /// <summary>Shows the player's real current weapon as selected on every open, so a designer
        /// who switched weapons another way never sees a stale selection.</summary>
        private void RefreshWeaponSelection()
        {
            WeaponFiring firing = ResolveLocalPlayer()?.GetComponentInChildren<WeaponFiring>(true);
            if (firing == null || firing.Weapon == null)
                return;

            int index = weaponOptions.FindIndex(w => w.Id == firing.Weapon.Id);
            if (index >= 0)
                weaponDropdown.SetValueWithoutNotify(index);
        }

        // ---- Ability dropdowns ----

        /// <summary>Fills one slot's dropdown from AbilityCatalogue, led by "(none)" so a designer can
        /// empty the slot again. A slot with nothing in the catalogue reads "(none available)" and is
        /// disabled, so it reads as "nothing built yet" rather than "broken".</summary>
        private void PopulateAbilityDropdown(int slotIndex)
        {
            TMP_Dropdown dropdown = abilityDropdowns[slotIndex];
            List<AbilityDefinition> options = abilityOptions[slotIndex];
            options.Clear();
            if (abilityCatalogue != null)
                options.AddRange(abilityCatalogue.ForSlot(AbilitySlots[slotIndex].slot));

            dropdown.ClearOptions();
            if (options.Count == 0)
            {
                dropdown.AddOptions(new List<string> { "(none available)" });
                dropdown.interactable = false;
                return;
            }

            var labels = new List<string> { "(none)" };
            foreach (AbilityDefinition ability in options)
                labels.Add($"{ability.Id} {ability.DisplayName}");

            dropdown.AddOptions(labels);
            dropdown.interactable = true;
        }

        private void OnAbilitySelected(int slotIndex, int option)
        {
            List<AbilityDefinition> options = abilityOptions[slotIndex];
            if (option < 0 || option > options.Count)
                return;

            PlayerLoadout loadout = ResolveLocalPlayer()?.GetComponent<PlayerLoadout>();
            if (loadout == null)
            {
                Debug.LogWarning("[TestRangePanel] no local PlayerLoadout found - cannot equip an ability.");
                return;
            }

            int abilityId = option == 0 ? LoadoutProperties.Empty : options[option - 1].Id;
            loadout.SetAbility(AbilitySlots[slotIndex].slot, abilityId);
        }

        /// <summary>Shows what the local player really has in each slot on every open - the same
        /// reason RefreshWeaponSelection exists.</summary>
        private void RefreshAbilitySelection()
        {
            AbilityRunner runner = ResolveLocalPlayer()?.GetComponent<AbilityRunner>();
            if (runner == null)
                return;

            for (int i = 0; i < AbilitySlots.Length; i++)
            {
                if (abilityOptions[i].Count == 0)
                    continue;

                int equipped = runner.EquippedId(AbilitySlots[i].slot);
                int index = abilityOptions[i].FindIndex(a => a.Id == equipped);
                abilityDropdowns[i].SetValueWithoutNotify(index >= 0 ? index + 1 : 0);
            }
        }

        // ---- Buttons ----

        private void OnResetCooldownsClicked()
        {
            PlayerOverheat overheat = ResolveLocalPlayer()?.GetComponentInChildren<PlayerOverheat>(true);
            overheat?.Clear();
            TestRangeResetRegistry.ResetAll();
        }

        private void OnHealClicked()
        {
            PlayerHealth health = ResolveLocalPlayer()?.GetComponentInChildren<PlayerHealth>(true);
            if (health == null)
                return;

            health.ResetForRespawn();                                          // Full health, clears burn/slow/etc.
            health.SetArmorLevels(health.AbsorbLevel, health.RechargeLevel);   // Refills armor at whatever levels are already owned.
        }

        /// <summary>Task 1.11's own "F1 gets a Fill Ultimate button" [C] - instantly fills
        /// UltimateCharge so Space can be tested without farming a dummy for real.</summary>
        private void OnFillUltimateClicked()
        {
            UltimateCharge charge = ResolveLocalPlayer()?.GetComponentInChildren<UltimateCharge>(true);
            if (charge == null)
            {
                Debug.LogWarning("[TestRangePanel] no local UltimateCharge found - cannot fill the ultimate meter.");
                return;
            }

            charge.Fill();
        }

        /// <summary>Task 2.2's own "F1 gets a +1000 Gold button" [C] - so shop testing (Task 2.5)
        /// never has to wait for territory income to trickle in first.</summary>
        private void OnAddGoldClicked()
        {
            GoldWallet wallet = ResolveLocalPlayer()?.GetComponent<GoldWallet>();
            if (wallet == null)
            {
                Debug.LogWarning("[TestRangePanel] no local GoldWallet found - cannot add gold.");
                return;
            }

            wallet.Add(1000);
        }

        /// <summary>Spends one purchase on the absorb path, if the combined cap and the path's own
        /// top level both still allow it - see ArmorUpgradePath. Exercises the exact rule a future
        /// shop will use, without needing gold or a shop UI to test it.</summary>
        private void OnAbsorbUpgradeClicked() => TryUpgradeArmor(upgradeAbsorb: true);

        /// <summary>Spends one purchase on the recharge path - see OnAbsorbUpgradeClicked.</summary>
        private void OnRechargeUpgradeClicked() => TryUpgradeArmor(upgradeAbsorb: false);

        /// <summary>Spends one purchase through the shared ArmorLoadoutActions rule (Task 9a pulled
        /// this out of here so the loadout screen calls the exact same rule) and logs a refusal -
        /// the loadout screen instead disables its +Absorb/+Recharge buttons at the cap, so only
        /// this designer-facing tool needs a log line for "why didn't that do anything".</summary>
        private void TryUpgradeArmor(bool upgradeAbsorb)
        {
            PlayerHealth health = ResolveLocalPlayer()?.GetComponentInChildren<PlayerHealth>(true);
            PlayerLoadout loadout = ResolveLocalPlayer()?.GetComponent<PlayerLoadout>();

            if (!ArmorLoadoutActions.TryUpgrade(health, loadout, armorConfig, upgradeAbsorb))
                Debug.Log($"[ARMOR] {(upgradeAbsorb ? "+Absorb" : "+Recharge")} refused - at the upgrade cap.");
        }

        /// <summary>Returns both armor paths to level 0, so a designer can re-run the upgrade sweep
        /// without restarting play mode. Goes through the same shared helper as the upgrade
        /// buttons, so every other client sees the reset too.</summary>
        private void OnResetArmorClicked() =>
            ArmorLoadoutActions.Reset(ResolveLocalPlayer()?.GetComponent<PlayerLoadout>());

        /// <summary>Reveals this client's current match folder in the OS file browser. In the Editor,
        /// EditorUtility.RevealInFinder opens Explorer/Finder directly; a build has no Editor to do
        /// that, so it falls back to Application.OpenURL on a file:// URL instead, which Unity's docs
        /// confirm opens the OS's own file browser for a directory path. `persistentDataPath` on this
        /// project's own PC contains a space ("...\Project OP\Telemetry\..."), which a hand-built
        /// "file:///" + path string leaves unescaped - System.Uri.AbsoluteUri percent-encodes it (and
        /// every backslash to a forward slash) into a well-formed URI instead.</summary>
        private void OnOpenTelemetryFolderClicked()
        {
            string folder = MatchTelemetry.Instance != null ? MatchTelemetry.Instance.CurrentFolder : null;
            if (string.IsNullOrEmpty(folder))
            {
                Debug.LogWarning("[TestRangePanel] no telemetry folder yet - join a match first (or check TelemetryConfig.Enabled).");
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(folder);
#else
            Application.OpenURL(new System.Uri(folder).AbsoluteUri);
#endif
        }

        /// <summary>Task T2's own "F1 gets a Drop marker button" - logs a `marker` line with an empty
        /// note so a designer can flag "something interesting just happened" while playing, without
        /// typing anything. The report's Markers section (Task T6) shows the 30s of events around it.</summary>
        private void OnDropMarkerClicked() => MatchTelemetry.Instance?.DropMarker("");

        // ---- Readout ----

        /// <summary>The computed-versus-measured pair on the last two lines is the point of this
        /// readout: computed is what the weapon's own numbers promise, measured is what a dummy
        /// actually recorded. Disagreement past rounding means the weapon is not behaving the way
        /// its stat block claims, worth chasing even when the arithmetic "looks right" on paper.</summary>
        private void RefreshReadout()
        {
            GameObject player = ResolveLocalPlayer();
            WeaponFiring firing = player != null ? player.GetComponentInChildren<WeaponFiring>(true) : null;
            PlayerHealth health = player != null ? player.GetComponentInChildren<PlayerHealth>(true) : null;
            string armorLine = ArmorReadoutLine(health);

            if (firing == null || firing.Weapon == null)
            {
                readoutText.text = "No local weapon found - join a match to populate this readout.\n" + armorLine;
                return;
            }

            WeaponDefinition weapon = firing.Weapon;
            PlayerOverheat overheat = player.GetComponentInChildren<PlayerOverheat>(true);

            float dps = weapon.FireInterval > 0f
                ? weapon.Damage * Mathf.Max(1, weapon.ProjectilesPerShot) / weapon.FireInterval
                : 0f;

            float effectiveHp = (gameplayConfig != null ? gameplayConfig.MaxHealth : 0f) +
                                 (armorConfig != null ? armorConfig.AbsorbFor(0) : 0f);
            float computedTtk = dps > 0f ? effectiveHp / dps : Mathf.Infinity;

            string overheatLine = overheat != null
                ? $"{overheat.Normalised * 100f:0}%" +
                  (overheat.IsSilenced ? " (SILENCED)" : overheat.IsWarning ? " (warning)" : "")
                : "n/a";

            string measuredLine = DummyTarget.LastMeasuredTtkSeconds >= 0f
                ? $"{DummyTarget.LastMeasuredTtkSeconds:0.00}s after {DummyTarget.LastMeasuredHits} hits"
                : "no dummy killed yet";

            readoutText.text =
                $"Weapon: {weapon.DisplayName}\n" +
                $"Damage: {weapon.Damage:0.#}   Fire interval: {weapon.FireInterval:0.##}s\n" +
                $"Overheat: {overheatLine}\n" +
                $"Computed DPS: {dps:0.#}   Computed TTK ({effectiveHp:0} HP): {computedTtk:0.00}s\n" +
                $"Measured TTK (last dummy kill): {measuredLine}\n" +
                armorLine;
        }

        /// <summary>Format matches the design review's example: "Armor A1/R0: 50 cap, 6s" - A/R are
        /// the absorb/recharge levels, so a designer can read the upgrade state at a glance without
        /// cross-referencing ArmorConfig.</summary>
        private string ArmorReadoutLine(PlayerHealth health)
        {
            if (health == null)
                return "Armor: n/a";

            float delay = armorConfig != null ? armorConfig.RechargeSecondsFor(health.RechargeLevel) : 0f;
            return $"Armor A{health.AbsorbLevel}/R{health.RechargeLevel}: {health.ArmorCapacity:0} cap, {delay:0}s delay " +
                   $"({health.Armor:0}/{health.ArmorCapacity:0} current)";
        }

        /// <summary>"Telemetry: <lines> lines -> <folder>" while recording, or "off" before a match's
        /// file has opened (telemetry disabled, or not in a room yet) - the plan's own wording for
        /// this label.</summary>
        private void RefreshTelemetryStatus()
        {
            MatchTelemetry telemetry = MatchTelemetry.Instance;
            telemetryStatusText.text = telemetry != null && telemetry.IsRecording
                ? $"Telemetry: {telemetry.LineCount} lines -> {telemetry.CurrentFolder}"
                : "Telemetry: off";
        }
    }
}
