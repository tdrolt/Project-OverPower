using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.Weapons;

namespace Overpower.TestRange
{
    /// <summary>A cooldown the "Reset Cooldowns" button can clear. Nothing implements this yet -
    /// dash and mines (Phase 1) will be first - so the registry below is empty for now and only
    /// PlayerOverheat.Clear() does anything. Implementers register in OnEnable, unregister in
    /// OnDisable.</summary>
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

        [SerializeField, Tooltip("Every ability in the game, across all four slots. Empty today, " +
                 "so all three ability dropdowns read '(none available)' until a later task adds " +
                 "assets.")]
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
        private readonly List<WeaponDefinition> weaponOptions = new List<WeaponDefinition>();

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
            suppressedRouter?.SetToolFocus(false);
            suppressedRouter = null;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                SetVisible(!visible);

            UpdateInputSuppression(visible);

            if (visible)
                RefreshReadout();
        }

        private void SetVisible(bool show)
        {
            visible = show;
            uiRoot.SetActive(show);
            if (show)
                RefreshWeaponSelection();
        }

        /// <summary>Keeps exactly one router suppressed - the local player's, re-resolved every
        /// frame the panel is open so a mid-session player change is never left stuck either way.</summary>
        private void UpdateInputSuppression(bool wantSuppressed)
        {
            PlayerInputRouter router = wantSuppressed
                ? ResolveLocalPlayer()?.GetComponentInChildren<PlayerInputRouter>(true)
                : null;

            if (router != suppressedRouter)
            {
                suppressedRouter?.SetToolFocus(false);
                suppressedRouter = router;
            }

            suppressedRouter?.SetToolFocus(true);
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
            }

            GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = rowLayout.childForceExpandWidth = true;
            AddButton(buttonRow.transform, "Reset Cooldowns", res, OnResetCooldownsClicked);
            AddButton(buttonRow.transform, "Heal", res, OnHealClicked);

            readoutText = AddLabel(panel.transform, "", 14f, FontStyles.Normal);

            PopulateWeaponDropdown();
            for (int i = 0; i < AbilitySlots.Length; i++)
                PopulateAbilityDropdown(abilityDropdowns[i], AbilitySlots[i].slot);
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
            return go.GetComponent<TMP_Dropdown>();
        }

        private static void AddButton(Transform parent, string label, TMP_DefaultControls.Resources res,
                                       UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            go.GetComponentInChildren<TextMeshProUGUI>().text = label;
            go.GetComponent<Button>().onClick.AddListener(onClick);
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
                    labels.Add(weapon.DisplayName);
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

            WeaponFiring firing = ResolveLocalPlayer()?.GetComponentInChildren<WeaponFiring>(true);
            if (firing == null)
            {
                Debug.LogWarning("[TestRangePanel] no local WeaponFiring found - cannot switch weapon.");
                return;
            }

            firing.SetWeapon(weaponOptions[index].Id);
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

        /// <summary>No ability-equip API exists anywhere yet - abilities are Phase 1 territory. These
        /// dropdowns exist so the panel already reads whatever AbilityCatalogue holds; empty, they
        /// read "(none available)" and are disabled, so it reads as "nothing built yet" rather than
        /// "broken". Wiring a selection to an equip call is for whichever task adds the first
        /// ability.</summary>
        private void PopulateAbilityDropdown(TMP_Dropdown dropdown, AbilitySlot slot)
        {
            List<AbilityDefinition> options = abilityCatalogue != null
                ? abilityCatalogue.ForSlot(slot)
                : new List<AbilityDefinition>();

            dropdown.ClearOptions();
            if (options.Count == 0)
            {
                dropdown.AddOptions(new List<string> { "(none available)" });
                dropdown.interactable = false;
                return;
            }

            var labels = new List<string>();
            foreach (AbilityDefinition ability in options)
                labels.Add(ability.DisplayName);

            dropdown.AddOptions(labels);
            dropdown.interactable = true;
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

            health.ResetForRespawn();               // Full health, clears burn/slow/etc.
            health.SetArmorTier(health.ArmorTier);   // Refills armor at whatever tier is already owned.
        }

        // ---- Readout ----

        /// <summary>The computed-versus-measured pair on the last two lines is the point of this
        /// readout: computed is what the weapon's own numbers promise, measured is what a dummy
        /// actually recorded. Disagreement past rounding means the weapon is not behaving the way
        /// its stat block claims, worth chasing even when the arithmetic "looks right" on paper.</summary>
        private void RefreshReadout()
        {
            GameObject player = ResolveLocalPlayer();
            WeaponFiring firing = player != null ? player.GetComponentInChildren<WeaponFiring>(true) : null;

            if (firing == null || firing.Weapon == null)
            {
                readoutText.text = "No local weapon found - join a match to populate this readout.";
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
                $"Measured TTK (last dummy kill): {measuredLine}";
        }
    }
}
