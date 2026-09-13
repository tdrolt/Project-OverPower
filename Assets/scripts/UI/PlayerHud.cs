using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Weapons;

namespace Overpower.UI
{
    /// <summary>
    /// The local player's own on-screen HUD: health, armor and overheat bars, plus the weapon slot
    /// and the three ability slots. Built in code exactly like TestRangePanel (see its class comment
    /// for why one root built in code beats a hand-authored hierarchy: every listener sits on the
    /// same line as the control it belongs to, and nothing can be wired to the wrong button).
    ///
    /// SCREEN SPACE, OWNER ONLY - unlike HealthBarCanvas (the world-space bar over every player's
    /// head, which everyone sees and which PlayerHealth already drives). This canvas is created only
    /// when photonView.IsMine: a HUD is something you look at, and nobody needs to look at anyone
    /// else's. It replaces the screen-space "ability Selector" canvas that used to ship on this
    /// prefab from the deleted Ability UI.cs - that canvas rendered once per player instance,
    /// stacking a copy on screen for every player in the room, and was removed from the prefab in
    /// this same task after confirming no script referenced it any more.
    ///
    /// Polls every value in LateUpdate (Tudor's instruction, so the HUD always reads this frame's
    /// final state) rather than subscribing to a "health changed"-style event per bar - there is no
    /// such event today, and one dozen tiny events would outweigh reading a dozen numbers in a fixed
    /// function. Every write to a Text or Image is guarded by a change check so an unmoving bar never
    /// re-allocates a string or re-touches a Graphic 60 times a second.
    /// </summary>
    public class PlayerHud : MonoBehaviourPun
    {
        [Header("Config")]
        [SerializeField, Tooltip("Match tuning asset. Read LIVE every frame for the overheat warning " +
                 "threshold and max health - never cached at spawn - so retuning either number in " +
                 "Play Mode moves the HUD immediately instead of waiting for this player's next life. " +
                 "This is the same asset PlayerHealth and PlayerOverheat already read; the HUD holds " +
                 "its own reference only because it draws on its own schedule (LateUpdate).")]
        private GameplayConfig gameplayConfig;

        [Header("Health and armor bars [C]")]
        [SerializeField, Tooltip("Fill colour of the health bar.")]
        private Color healthColor = new Color(0.336f, 0.914f, 0.263f);

        [SerializeField, Tooltip("Fill colour of the armor bar's current charge. The armor bar's own " +
                 "WIDTH (not just its fill) grows with armor capacity, up to the full bar width at " +
                 "100 capacity - the same scale as max health - so an armor upgrade reads as a " +
                 "visibly bigger segment, not just a fuller small one.")]
        private Color armorColor = new Color(0.45f, 0.75f, 0.95f);

        [SerializeField, Tooltip("Shared dark backing colour behind every bar's fill.")]
        private Color barBackgroundColor = new Color(0f, 0f, 0f, 0.55f);

        [Header("Overheat bar [C]")]
        [SerializeField, Tooltip("Opaque track colour behind the overheat bar's fill - deliberately " +
                 "its OWN colour rather than the shared translucent Bar Background Colour, and " +
                 "deliberately lighter than every fill state (normal/warning/silenced), so the " +
                 "empty part of the bar reads as a bright, solid trough instead of blending into " +
                 "whatever is behind the HUD.")]
        private Color overheatTrackColor = new Color(0.78f, 0.78f, 0.80f, 1f);

        [SerializeField, Tooltip("Height of the overheat bar, in canvas units - taller than health/" +
                 "armor on purpose: it is the one bar a player must read at a glance mid-fight."), Range(8f, 32f)]
        private float overheatBarHeight = 18f;

        [SerializeField, Tooltip("Colour of the thin vertical tick marking exactly where the warning " +
                 "threshold sits on the track. Dark so it stays visible against the light track " +
                 "colour above and every fill colour it might be drawn over.")]
        private Color overheatTickColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);

        [SerializeField, Tooltip("Width of the warning-threshold tick mark, in canvas units."), Range(1f, 6f)]
        private float overheatTickWidth = 2f;

        [SerializeField, Tooltip("Bar colour below the warning threshold.")]
        private Color overheatNormalColor = new Color(0.60f, 0.60f, 0.66f);

        [SerializeField, Tooltip("Bar colour from the warning threshold up to the silence, pulsing - " +
                 "Tudor's condition for accepting full-silence overheat was that this warning exists, " +
                 "so it has to read as \"your mistake\", not a wall.")]
        private Color overheatWarningColor = new Color(1f, 0.65f, 0.05f);

        [SerializeField, Tooltip("Bar colour while the weapon and every ability are silenced.")]
        private Color overheatSilencedColor = new Color(0.85f, 0.16f, 0.16f);

        [SerializeField, Tooltip("How many times per second the warning colour pulses."), Range(0.5f, 8f)]
        private float warningPulseSpeed = 2.5f;

        [SerializeField, Tooltip("How far the pulse dims the warning colour at its darkest point - " +
                 "0.35 means it dips to 65% brightness and back."), Range(0f, 0.9f)]
        private float warningPulseDepth = 0.35f;

        [Header("Ability and weapon slots [C]")]
        [SerializeField, Tooltip("Slot background when the slot can be used right now.")]
        private Color slotReadyColor = new Color(0f, 0f, 0f, 0.6f);

        [SerializeField, Tooltip("Slot background when BlockFor reports anything other than None - " +
                 "dead, stunned, silenced, recharging, or an empty/not-ready slot.")]
        private Color slotBlockedColor = new Color(0.30f, 0.30f, 0.30f, 0.85f);

        [SerializeField, Tooltip("Slot background while the ability's IsActive is true - a channel, " +
                 "a dash mid-flight, sprint held.")]
        private Color slotActiveGlowColor = new Color(1f, 0.85f, 0.25f, 1f);

        [SerializeField, Tooltip("Charge pip colour when that charge is available.")]
        private Color pipAvailableColor = Color.white;

        [SerializeField, Tooltip("Charge pip colour when that charge is spent.")]
        private Color pipSpentColor = new Color(1f, 1f, 1f, 0.15f);

        [SerializeField, Tooltip("Colour of the dark cover that wipes off an ability icon as it " +
                 "recharges - fully covered the instant a charge is spent, gone the instant it returns.")]
        private Color cooldownCoverColor = new Color(0f, 0f, 0f, 0.65f);

        [Header("Ultimate charge meter [C] (Task 1.11 hook, now filled in)")]
        [SerializeField, Tooltip("Fill colour of the Ultimate slot's own charge meter - a translucent " +
                 "wash drawn over the icon, from empty to full, independent of the slot's ordinary " +
                 "recharge cover (which reflects the trivial always-instant base-class pool, not the " +
                 "real gate). Bound to UltimateCharge.Normalised.")]
        private Color ultimateChargeColor = new Color(1f, 0.85f, 0.25f, 0.45f);

        [SerializeField, Tooltip("Colour of the READY text shown over the Ultimate slot once " +
                 "UltimateCharge.IsFull is true - the moment Space actually casts something.")]
        private Color ultimateReadyTextColor = new Color(1f, 0.95f, 0.6f);

        // The one width every bar shares, so the health, overheat and armor track all line up.
        private const float BarWidth = 560f;

        // ---- component refs, read off this same player root --------------------------------------

        private PlayerHealth playerHealth;
        private PlayerOverheat playerOverheat;
        private PlayerStatusEffects statusEffects;
        private WeaponFiring weaponFiring;
        private AbilityRunner abilityRunner;
        private UltimateCharge ultimateCharge;

        // ---- built UI: bars ------------------------------------------------------------------------

        private Image healthFill;
        private Image overheatFill;
        private RectTransform overheatTickRect; // Repositioned live - see UpdateOverheat.
        private Image armorFill;
        private RectTransform armorExtentRect; // The part of the armor track sized by capacity, not by current value.
        private GameObject silencedBanner;

        // ---- built UI: slots -----------------------------------------------------------------------

        /// <summary>One slot's widgets. A class, not a struct, purely so BuildSlot/SetPips can mutate
        /// it in place through the arrays below without juggling copies back and forth.</summary>
        private sealed class SlotUi
        {
            public Image background;      // Doubles as the ready/blocked/active-glow tint.
            public Image icon;
            public TextMeshProUGUI fallbackNameText;
            public Image cooldownCover;   // Null for the weapon slot - it has no cooldown sweep.
            public Transform pipRow;      // Null for the weapon slot.
            public readonly List<Image> pips = new List<Image>();
            public TextMeshProUGUI blockReasonText; // Null for the weapon slot.

            // Ultimate slot only (Task 1.11) - null for every other slot. A separate overlay from
            // cooldownCover above: the base class's own charge pool for an ultimate module is a
            // trivial 1-charge/0-cooldown pool that recovers the instant it is spent (see the
            // addendum's "fit with 1.0"), so its RechargeProgress never reflects the real gate -
            // UltimateCharge.Normalised is read directly instead.
            public Image ultimateChargeFill;
            public TextMeshProUGUI readyLabel;
        }

        private SlotUi weaponSlotUi;
        private readonly SlotUi[] abilitySlotUi = new SlotUi[3];

        // Index order for abilitySlotUi and every "lastXxx" array below. Fixed by AbilitySlot's own
        // three ability values - Primary (the weapon) is handled separately, has no runner slot.
        private static readonly (AbilitySlot slot, string keyLabel)[] AbilitySlotOrder =
        {
            (AbilitySlot.Equipment, "RMB"),
            (AbilitySlot.Ultimate, "SPACE"),
            (AbilitySlot.Mobility, "SHIFT"),
        };

        // ---- change-detection caches - see the class comment on why LateUpdate never allocates ----

        private float lastHealthFraction = -1f;
        private float lastArmorFraction = -1f;
        private float lastArmorExtentWidth = -1f;
        private float lastOverheatFraction = -1f;
        private float lastWarningThreshold01 = -1f;
        private bool lastSilenced;
        private bool lastWeaponBlocked;
        private WeaponDefinition lastWeaponDef;
        private readonly int[] lastCharges = { -1, -1, -1 };
        private readonly int[] lastMaxCharges = { -1, -1, -1 };
        private readonly float[] lastRecharge = { -1f, -1f, -1f };
        private readonly bool[] lastActive = { false, false, false };
        private readonly CastBlock[] lastBlock = { (CastBlock)(-1), (CastBlock)(-1), (CastBlock)(-1) };
        private float lastUltimateCharge = -1f;
        private bool lastUltimateReady;

        private void Awake()
        {
            // Every remote copy of this component stays permanently dormant - see the class comment.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            playerHealth = GetComponent<PlayerHealth>();
            playerOverheat = GetComponent<PlayerOverheat>();
            statusEffects = GetComponent<PlayerStatusEffects>();
            weaponFiring = GetComponent<WeaponFiring>();
            abilityRunner = GetComponent<AbilityRunner>();
            ultimateCharge = GetComponent<UltimateCharge>();

            if (gameplayConfig == null)
                Debug.LogError($"[PlayerHud] {name}: GameplayConfig is not assigned - the overheat warning threshold and max health fall back to hardcoded numbers.");
            if (playerHealth == null || playerOverheat == null || statusEffects == null || weaponFiring == null || abilityRunner == null)
                Debug.LogError($"[PlayerHud] {name}: missing PlayerHealth/PlayerOverheat/PlayerStatusEffects/WeaponFiring/AbilityRunner on this player - the HUD cannot bind to it.");
            if (ultimateCharge == null)
                Debug.LogError($"[PlayerHud] {name}: no UltimateCharge on this player - the Ultimate slot's charge meter will read as always empty.");

            BuildUi();

            if (abilityRunner != null)
                abilityRunner.SlotChanged += HandleSlotChanged;

            RefreshWeaponSlotContent();
            for (int i = 0; i < AbilitySlotOrder.Length; i++)
                RefreshAbilitySlotContent(i);
        }

        private void OnDestroy()
        {
            if (abilityRunner != null)
                abilityRunner.SlotChanged -= HandleSlotChanged;
        }

        private void HandleSlotChanged(AbilitySlot slot)
        {
            for (int i = 0; i < AbilitySlotOrder.Length; i++)
            {
                if (AbilitySlotOrder[i].slot == slot)
                    RefreshAbilitySlotContent(i);
            }
        }

        private void LateUpdate()
        {
            UpdateHealthAndArmor();
            UpdateOverheat();
            UpdateWeaponSlot();
            UpdateAbilitySlots();
        }

        // ============================================================================================
        // Bars
        // ============================================================================================

        private void UpdateHealthAndArmor()
        {
            if (playerHealth == null)
                return;

            // Same asset PlayerHealth itself reads for this number - see the class comment on why
            // the HUD holds its own reference instead of asking PlayerHealth for one.
            float maxHealth = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;

            float healthFraction = maxHealth > 0f ? Mathf.Clamp01(playerHealth.Health / maxHealth) : 0f;
            if (!Mathf.Approximately(healthFraction, lastHealthFraction))
            {
                healthFill.fillAmount = healthFraction;
                lastHealthFraction = healthFraction;
            }

            // The armor TRACK's visible width scales with capacity relative to max health - both are
            // just "hit points" on the same 0-100-ish scale, so an armor pool as big as max health
            // fills the whole track, and level 0's starting 25 capacity is a quarter-width sliver.
            // This is what makes "+Absorb twice" read as a visibly bigger segment, not merely a
            // fuller small one (see the Armor Colour tooltip).
            float capacity = playerHealth.ArmorCapacity;
            float trackWidth = BarWidth - 4f; // matches the 2px margin baked into BuildArmorBar on each side.
            float extentWidth = maxHealth > 0f ? Mathf.Clamp01(capacity / maxHealth) * trackWidth : 0f;
            if (!Mathf.Approximately(extentWidth, lastArmorExtentWidth))
            {
                armorExtentRect.sizeDelta = new Vector2(extentWidth, armorExtentRect.sizeDelta.y);
                lastArmorExtentWidth = extentWidth;
            }

            float armorFraction = capacity > 0f ? Mathf.Clamp01(playerHealth.Armor / capacity) : 0f;
            if (!Mathf.Approximately(armorFraction, lastArmorFraction))
            {
                armorFill.fillAmount = armorFraction;
                lastArmorFraction = armorFraction;
            }
        }

        private void UpdateOverheat()
        {
            if (playerOverheat == null)
                return;

            float fraction = playerOverheat.Normalised;
            bool silenced = playerOverheat.IsSilenced;

            // Deliberately NOT PlayerOverheat.IsWarning: that property is backed by OverheatState's
            // own warningThreshold, captured once at this player's spawn. Reading GameplayConfig
            // directly here, every frame, is what lets a designer retune the threshold in Play Mode
            // and see this bar's colour change move immediately - the whole point of it being a
            // config value instead of a literal.
            float warningThreshold01 = gameplayConfig != null && gameplayConfig.OverheatMax > 0f
                ? gameplayConfig.OverheatWarningThreshold / gameplayConfig.OverheatMax
                : 0.8f;
            bool warning = !silenced && fraction >= warningThreshold01;

            // The tick mark moves with the same live threshold read above - a designer retuning it
            // in Play Mode sees both the colour boundary AND the mark on the track move together.
            if (!Mathf.Approximately(warningThreshold01, lastWarningThreshold01))
            {
                overheatTickRect.anchorMin = new Vector2(warningThreshold01, 0f);
                overheatTickRect.anchorMax = new Vector2(warningThreshold01, 1f);
                lastWarningThreshold01 = warningThreshold01;
            }

            if (!Mathf.Approximately(fraction, lastOverheatFraction))
            {
                overheatFill.fillAmount = fraction;
                lastOverheatFraction = fraction;
            }

            Color target = silenced ? overheatSilencedColor : warning ? overheatWarningColor : overheatNormalColor;
            if (warning)
            {
                // A genuine pulse (dimming the same colour), not a colour swap - reads as "this is
                // still your weapon warning you", not a second state.
                float pulse = 1f - warningPulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * warningPulseSpeed * Mathf.PI * 2f));
                float alpha = target.a;
                target *= pulse;
                target.a = alpha;
            }
            overheatFill.color = target;

            if (silenced != lastSilenced)
            {
                // The banner is specifically the "weapon silenced by overheat, draining" cue - not
                // the general block tint below, which also covers stun and death.
                silencedBanner.SetActive(silenced);
                lastSilenced = silenced;
            }
        }

        // ============================================================================================
        // Weapon slot (icon/name only - no cooldown sweep, no charges: see the addendum's "four slot
        // icons" correction, the fourth being the weapon, not a fourth ability).
        // ============================================================================================

        private void UpdateWeaponSlot()
        {
            if (weaponFiring == null)
                return;

            WeaponDefinition current = weaponFiring.Weapon;
            if (current != lastWeaponDef)
                RefreshWeaponSlotContent();

            // Same actor-wide rule WeaponFiring.TryFire itself asks (CastGate.ForActor) - dead,
            // stunned or silenced all block the trigger, so the weapon slot greys out for the same
            // three reasons the ability slots do, not overheat alone.
            bool alive = playerHealth == null || playerHealth.IsAlive;
            bool stunned = statusEffects != null && statusEffects.IsStunned;
            bool silenced = playerOverheat != null && playerOverheat.IsSilenced;
            bool blocked = CastGate.ForActor(alive, stunned, silenced) != CastBlock.None;
            if (blocked != lastWeaponBlocked)
            {
                weaponSlotUi.background.color = blocked ? slotBlockedColor : slotReadyColor;
                lastWeaponBlocked = blocked;
            }
        }

        private void RefreshWeaponSlotContent()
        {
            WeaponDefinition def = weaponFiring != null ? weaponFiring.Weapon : null;
            ApplyIconOrFallback(weaponSlotUi, def != null ? def.Icon : null, def != null ? def.DisplayName : "");
            lastWeaponDef = def;
        }

        // ============================================================================================
        // Ability slots
        // ============================================================================================

        private void RefreshAbilitySlotContent(int index)
        {
            AbilitySlot slot = AbilitySlotOrder[index].slot;
            IAbilityStatus status = abilityRunner != null ? abilityRunner.StatusFor(slot) : null;
            AbilityDefinition def = status != null ? status.Definition : null;
            SlotUi ui = abilitySlotUi[index];

            // Most abilities have no icon yet (the addendum's warning) - ApplyIconOrFallback is the
            // one place that decision is made, shared with the weapon slot above.
            ApplyIconOrFallback(ui, def != null ? def.Icon : null, def != null ? def.DisplayName : "");

            int maxCharges = status != null ? status.MaxCharges : 0;
            SetPips(ui, status != null ? status.ChargesAvailable : 0, maxCharges);

            // Force every cached value stale so the very next LateUpdate redraws this slot's sweep,
            // tint and reason text even if the new ability's numbers happen to match the old one's.
            lastCharges[index] = -1;
            lastMaxCharges[index] = maxCharges;
            lastRecharge[index] = -1f;
            lastActive[index] = !status?.IsActive ?? true;
            lastBlock[index] = (CastBlock)(-1);
        }

        private void UpdateAbilitySlots()
        {
            if (abilityRunner == null)
                return;

            for (int i = 0; i < AbilitySlotOrder.Length; i++)
            {
                AbilitySlot slot = AbilitySlotOrder[i].slot;
                SlotUi ui = abilitySlotUi[i];
                IAbilityStatus status = abilityRunner.StatusFor(slot);
                CastBlock block = abilityRunner.BlockFor(slot);

                int charges = status != null ? status.ChargesAvailable : 0;
                int maxCharges = status != null ? status.MaxCharges : 0;
                float recharge = status != null ? status.RechargeProgress : 0f;
                bool active = status != null && status.IsActive;

                if (charges != lastCharges[i] || maxCharges != lastMaxCharges[i])
                {
                    SetPips(ui, charges, maxCharges);
                    lastCharges[i] = charges;
                    lastMaxCharges[i] = maxCharges;
                }

                if (!Mathf.Approximately(recharge, lastRecharge[i]))
                {
                    // A module with no charge pool at all (Sprint spends heat, not charges) draws no
                    // sweep - see IAbilityStatus.MaxCharges' own doc comment.
                    ui.cooldownCover.fillAmount = maxCharges > 0 ? Mathf.Clamp01(1f - recharge) : 0f;
                    lastRecharge[i] = recharge;
                }

                if (active != lastActive[i] || block != lastBlock[i])
                {
                    ui.background.color = block != CastBlock.None ? slotBlockedColor
                                         : active ? slotActiveGlowColor
                                         : slotReadyColor;
                    ui.blockReasonText.text = block == CastBlock.None ? "" : BlockReasonLabel(block);
                    lastActive[i] = active;
                    lastBlock[i] = block;
                }

                if (slot == AbilitySlot.Ultimate)
                    UpdateUltimateMeter(ui);
            }
        }

        /// <summary>The Task 1.11 hook, filled in: a translucent fill over the Ultimate slot's icon
        /// tracking UltimateCharge.Normalised, and a READY label shown only once IsFull is true - the
        /// same moment Space actually casts something. Independent of the slot's ordinary cooldown
        /// cover above, which for an ultimate module reflects only the trivial always-instant
        /// base-class pool, never the real gate.</summary>
        private void UpdateUltimateMeter(SlotUi ui)
        {
            if (ui.ultimateChargeFill == null)
                return; // Built only for the Ultimate slot - see BuildSlot's isUltimate parameter.

            float normalised = ultimateCharge != null ? ultimateCharge.Normalised : 0f;
            bool ready = ultimateCharge != null && ultimateCharge.IsFull;

            if (!Mathf.Approximately(normalised, lastUltimateCharge))
            {
                ui.ultimateChargeFill.fillAmount = normalised;
                lastUltimateCharge = normalised;
            }

            if (ready != lastUltimateReady)
            {
                ui.readyLabel.gameObject.SetActive(ready);
                lastUltimateReady = ready;
            }
        }

        private static string BlockReasonLabel(CastBlock block)
        {
            switch (block)
            {
                case CastBlock.Dead: return "dead";
                case CastBlock.Stunned: return "stunned";
                case CastBlock.Silenced: return "silenced";
                case CastBlock.Recharging: return "recharging";
                case CastBlock.NotReady: return "not ready";
                default: return "";
            }
        }

        private static void ApplyIconOrFallback(SlotUi ui, Sprite icon, string displayName)
        {
            if (icon != null)
            {
                ui.icon.sprite = icon;
                ui.icon.enabled = true;
                ui.fallbackNameText.enabled = false;
                ui.fallbackNameText.text = "";
            }
            else
            {
                ui.icon.enabled = false;
                ui.fallbackNameText.enabled = true;
                ui.fallbackNameText.text = string.IsNullOrEmpty(displayName) ? "" : displayName;
            }
        }

        private void SetPips(SlotUi ui, int charges, int maxCharges)
        {
            if (ui.pipRow == null)
                return; // The weapon slot has no pip row.

            if (ui.pips.Count != maxCharges)
            {
                foreach (Image old in ui.pips)
                    Destroy(old.gameObject);
                ui.pips.Clear();

                for (int i = 0; i < maxCharges; i++)
                {
                    GameObject pip = new GameObject("Pip", typeof(RectTransform));
                    pip.transform.SetParent(ui.pipRow, false);
                    LayoutElement le = pip.AddComponent<LayoutElement>();
                    le.preferredWidth = 8f;
                    le.preferredHeight = 8f;
                    pip.GetComponent<RectTransform>().sizeDelta = new Vector2(8f, 8f); // See BuildBar's comment.
                    Image img = pip.AddComponent<Image>();
                    img.raycastTarget = false;
                    ui.pips.Add(img);
                }
            }

            for (int i = 0; i < ui.pips.Count; i++)
                ui.pips[i].color = i < charges ? pipAvailableColor : pipSpentColor;
        }

        // ============================================================================================
        // UI construction - built in code, following TestRangePanel's pattern (see its class comment).
        // ============================================================================================

        private void BuildUi()
        {
            GameObject canvasGo = new GameObject("Player Hud Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Explicit and below both the F1 test panel (TestRangePanel: order 500) and MatchUI's
            // win/lose/respawn panels (default, unordered overlay canvas) - the HUD must never sit on
            // top of either. overrideSorting makes this true regardless of sibling/creation order,
            // which an un-overridden Screen Space - Overlay canvas cannot guarantee on its own.
            canvas.overrideSorting = true;
            canvas.sortingOrder = -10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Match by HEIGHT, not TestRangePanel's default (width): this HUD is anchored to the
            // bottom edge by a fixed reference-unit offset, and matching by height keeps that offset
            // a predictable fraction of screen height on any window shape instead of shrinking
            // toward the bottom on a narrower-than-16:9 window - which is exactly the window this
            // was measured against (a small, near-square Game view during single-client testing).
            scaler.matchWidthOrHeight = 1f;
            // No GraphicRaycaster and no EventSystem: nothing on this HUD is clickable (see the class
            // comment) - adding one would just be a second, unnecessary EventSystem warning waiting
            // to happen.

            GameObject panel = new GameObject("Hud Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            // Bottom-centre: the F1 test panel owns the top-left (TestRangePanel.BuildUi), and this
            // keeps the two tools from ever overlapping. Lifted well clear of the bottom edge -
            // measured against a running client - because the chat prompt ("press Enter to chat")
            // occupies a tall band at the very bottom of the screen and would otherwise sit on top
            // of this canvas (chat's Canvas has the default sortingOrder 0, above this one's -10).
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0f);
            panelRt.pivot = new Vector2(0.5f, 0f);
            panelRt.anchoredPosition = new Vector2(0f, 260f);
            panelRt.sizeDelta = new Vector2(BarWidth, 190f);

            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.spacing = 6f;
            panelLayout.childAlignment = TextAnchor.LowerCenter;
            panelLayout.childControlWidth = false;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = false;
            panelLayout.childForceExpandHeight = false;

            armorFill = BuildArmorBar(panel.transform, out armorExtentRect);
            healthFill = BuildBar(panel.transform, "Health Bar", BarWidth, 20f, barBackgroundColor, healthColor, out _);
            overheatFill = BuildBar(panel.transform, "Overheat Bar", BarWidth, overheatBarHeight, overheatTrackColor, overheatNormalColor, out Image overheatTrack);
            overheatTickRect = BuildOverheatTick(overheatTrack.transform);

            GameObject slotsRow = new GameObject("Slots Row", typeof(RectTransform));
            slotsRow.transform.SetParent(panel.transform, false);
            LayoutElement slotsRowLe = slotsRow.AddComponent<LayoutElement>();
            slotsRowLe.preferredWidth = BarWidth;
            slotsRowLe.preferredHeight = 92f;
            // See BuildBar's comment: the outer VerticalLayoutGroup has childControl off on both
            // axes, so this row's own rect needs an explicit size - the same width as the bars
            // above it, so the slot boxes end up centred under them.
            slotsRow.GetComponent<RectTransform>().sizeDelta = new Vector2(BarWidth, 92f);
            HorizontalLayoutGroup slotsLayout = slotsRow.AddComponent<HorizontalLayoutGroup>();
            slotsLayout.spacing = 10f;
            slotsLayout.childAlignment = TextAnchor.UpperCenter;
            slotsLayout.childControlWidth = slotsLayout.childControlHeight = false;

            weaponSlotUi = BuildSlot(slotsRow.transform, "LMB", withCooldown: false, isUltimate: false);
            for (int i = 0; i < AbilitySlotOrder.Length; i++)
            {
                bool isUltimate = AbilitySlotOrder[i].slot == AbilitySlot.Ultimate;
                abilitySlotUi[i] = BuildSlot(slotsRow.transform, AbilitySlotOrder[i].keyLabel, withCooldown: true, isUltimate: isUltimate);
            }

            // Built LAST and parented to the row itself (not the panel): a plain child with
            // ignoreLayout stretched over slotsRow's own rect draws literally "over the slot row"
            // (the addendum's own words) without ever being counted by any layout group - toggling
            // it on/off with SetActive can no longer shift the row's position the way it did when
            // this lived as a separate, height-reserving sibling under Hud Panel (code review fix).
            silencedBanner = BuildSilencedBanner(slotsRow.transform);
        }

        /// <summary>trackImage is handed back so a caller can add something on top of the track
        /// itself (the overheat bar's warning tick) or give it its own colour instead of the shared
        /// Bar Background Colour (again, overheat - see its Track Colour tooltip).</summary>
        private Image BuildBar(Transform parent, string name, float width, float height, Color trackColor, Color fillColor, out Image trackImage)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            // The parent VerticalLayoutGroup has childControlWidth/Height off (each bar keeps its
            // own exact height), and with control off a LayoutGroup only reads LayoutElement's
            // preferred size to POSITION this box along the stack - it never writes that size back
            // onto the RectTransform. Set it explicitly here or the box stays at the default 100x100.
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            Image background = go.AddComponent<Image>();
            background.color = trackColor;
            background.raycastTarget = false;
            trackImage = background;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(2f, 2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.color = fillColor;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;
            fillImg.raycastTarget = false;
            return fillImg;
        }

        /// <summary>A thin vertical mark on the overheat track showing exactly where the warning
        /// threshold sits. Parented to the track (a sibling of Fill, added after it so it always
        /// draws on top of the fill) and built at a placeholder position - UpdateOverheat repositions
        /// it every frame the live threshold fraction changes, the same pattern the colour swap uses.</summary>
        private RectTransform BuildOverheatTick(Transform trackParent)
        {
            GameObject tick = new GameObject("Warning Tick", typeof(RectTransform));
            tick.transform.SetParent(trackParent, false);
            RectTransform tickRt = tick.GetComponent<RectTransform>();
            // X is a point-anchor (min == max) so it can be slid along the track by changing just
            // that one number; Y stretches the full track height so the mark spans the whole bar.
            tickRt.anchorMin = new Vector2(0.8f, 0f);
            tickRt.anchorMax = new Vector2(0.8f, 1f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.sizeDelta = new Vector2(overheatTickWidth, 0f);
            tickRt.anchoredPosition = Vector2.zero;
            Image tickImg = tick.AddComponent<Image>();
            tickImg.color = overheatTickColor;
            tickImg.raycastTarget = false;
            return tickRt;
        }

        /// <summary>The armor bar is a fixed-width track (like the other two) holding a "capacity
        /// extent" rectangle whose WIDTH is set live from script (see UpdateHealthAndArmor) rather
        /// than by any layout group - that sidesteps layout-rebuild timing entirely, since a plain
        /// child RectTransform's sizeDelta takes effect immediately. Inside that extent, an ordinary
        /// horizontal fill shows current armor against its own capacity, same as the other bars.</summary>
        private Image BuildArmorBar(Transform parent, out RectTransform extentRect)
        {
            GameObject track = new GameObject("Armor Bar", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            LayoutElement le = track.AddComponent<LayoutElement>();
            le.preferredWidth = BarWidth;
            le.preferredHeight = 12f;
            // See BuildBar's comment: childControlWidth/Height off means this has to be set directly too.
            track.GetComponent<RectTransform>().sizeDelta = new Vector2(BarWidth, 12f);
            Image background = track.AddComponent<Image>();
            background.color = barBackgroundColor;
            background.raycastTarget = false;

            GameObject extentGo = new GameObject("Capacity Extent", typeof(RectTransform));
            extentGo.transform.SetParent(track.transform, false);
            extentRect = extentGo.GetComponent<RectTransform>();
            extentRect.anchorMin = new Vector2(0f, 0f);
            extentRect.anchorMax = new Vector2(0f, 1f);
            extentRect.pivot = new Vector2(0f, 0.5f);
            extentRect.anchoredPosition = new Vector2(2f, 0f);
            extentRect.sizeDelta = new Vector2(0f, -4f); // Width set live; height = track height minus the 2px top/bottom margin.

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(extentGo.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.color = armorColor;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;
            fillImg.raycastTarget = false;
            return fillImg;
        }

        /// <summary>Hidden until IsSilenced; a crossed-out weapon mark built from plain rectangles
        /// (the project has no weapon-silhouette sprite yet) so full overheat silence reads as a
        /// state with an end, not a wall - PlayerOverheat's class comment records that this was
        /// Tudor's explicit condition for accepting the harsher "silences everything" rule.
        ///
        /// Parented to slotsRow and stretched over its full rect with ignoreLayout on (see the
        /// BuildUi call site) - a translucent wash plus the icon and label draw directly over the
        /// four slot boxes, matching the addendum's own wording ("over the slot row") instead of
        /// the height-reserving sibling row this used to be, which shifted the slots by its own
        /// height every time SetActive toggled it (code review fix).</summary>
        private GameObject BuildSilencedBanner(Transform parent)
        {
            GameObject row = new GameObject("Silenced Banner", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            RectTransform rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = Vector2.zero;
            rowRt.anchorMax = Vector2.one;
            rowRt.offsetMin = Vector2.zero;
            rowRt.offsetMax = Vector2.zero;
            LayoutElement rowLe = row.AddComponent<LayoutElement>();
            rowLe.ignoreLayout = true; // slotsRow's own HorizontalLayoutGroup must never see this as a 5th column.

            // A translucent wash across the whole row, behind the icon/label below, so "you cannot
            // use any of this right now" reads even before the eye finds the label.
            Image wash = row.AddComponent<Image>();
            wash.color = new Color(overheatSilencedColor.r, overheatSilencedColor.g, overheatSilencedColor.b, 0.35f);
            wash.raycastTarget = false;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(row.transform, false);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            HorizontalLayoutGroup layout = content.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = false;

            GameObject iconGo = new GameObject("Weapon Icon", typeof(RectTransform));
            iconGo.transform.SetParent(content.transform, false);
            LayoutElement iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 20f;
            iconLe.preferredHeight = 20f;
            iconGo.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f); // See BuildBar's comment.
            Image iconImg = iconGo.AddComponent<Image>();
            iconImg.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
            iconImg.raycastTarget = false;

            GameObject strike = new GameObject("Strike", typeof(RectTransform));
            strike.transform.SetParent(iconGo.transform, false);
            RectTransform strikeRt = strike.GetComponent<RectTransform>();
            strikeRt.anchorMin = strikeRt.anchorMax = strikeRt.pivot = new Vector2(0.5f, 0.5f);
            strikeRt.sizeDelta = new Vector2(30f, 3f);
            strikeRt.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Image strikeImg = strike.AddComponent<Image>();
            strikeImg.color = overheatSilencedColor;
            strikeImg.raycastTarget = false;

            TextMeshProUGUI text = AddLabel(content.transform, "WEAPON SILENCED", 14f, FontStyles.Bold);
            LayoutElement textLe = text.gameObject.AddComponent<LayoutElement>();
            textLe.preferredWidth = 220f;
            textLe.preferredHeight = 20f;
            text.rectTransform.sizeDelta = new Vector2(220f, 20f); // See BuildBar's comment.
            text.color = overheatSilencedColor;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            row.SetActive(false);
            return row;
        }

        /// <summary>One weapon or ability box: a tinted background (ready/blocked/active), an icon
        /// that falls back to the ability's display name when it has none, and - for the three
        /// ability slots only - a charge pip row and a recharge cover sweep. isUltimate additionally
        /// builds the Task 1.11 charge meter (a fill plus a READY label), true for exactly one of
        /// the three ability slots.</summary>
        private SlotUi BuildSlot(Transform parent, string keyLabel, bool withCooldown, bool isUltimate)
        {
            var ui = new SlotUi();

            GameObject go = new GameObject("Slot " + keyLabel, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 64f;
            le.preferredHeight = withCooldown ? 92f : 64f;
            // See BuildBar's comment: the row's HorizontalLayoutGroup has childControl off on both
            // axes, so the slot box needs its own explicit size - everything inside it (Icon Box,
            // pips, text) stretches or anchors relative to THIS rect.
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(64f, withCooldown ? 92f : 64f);

            ui.background = go.AddComponent<Image>();
            ui.background.color = slotReadyColor;

            // The icon box occupies the top 64 units - the only part that exists at all on the
            // weapon slot, which has no pip row or recharge sweep below it.
            GameObject iconBox = new GameObject("Icon Box", typeof(RectTransform));
            iconBox.transform.SetParent(go.transform, false);
            RectTransform iconBoxRt = iconBox.GetComponent<RectTransform>();
            iconBoxRt.anchorMin = new Vector2(0f, 1f);
            iconBoxRt.anchorMax = new Vector2(1f, 1f);
            iconBoxRt.pivot = new Vector2(0.5f, 1f);
            iconBoxRt.anchoredPosition = Vector2.zero;
            iconBoxRt.sizeDelta = new Vector2(0f, 64f);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(iconBox.transform, false);
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(4f, 4f);
            iconRt.offsetMax = new Vector2(-4f, -4f);
            ui.icon = iconGo.AddComponent<Image>();
            ui.icon.preserveAspect = true;
            ui.icon.raycastTarget = false;
            ui.icon.enabled = false;

            ui.fallbackNameText = AddLabel(iconBox.transform, "", 11f, FontStyles.Normal);
            RectTransform nameRt = ui.fallbackNameText.rectTransform;
            nameRt.anchorMin = Vector2.zero;
            nameRt.anchorMax = Vector2.one;
            nameRt.offsetMin = new Vector2(3f, 3f);
            nameRt.offsetMax = new Vector2(-3f, -3f);
            ui.fallbackNameText.enableWordWrapping = true;
            ui.fallbackNameText.alignment = TextAlignmentOptions.Center;
            ui.fallbackNameText.fontSize = 11f;

            if (withCooldown)
            {
                GameObject coverGo = new GameObject("Cooldown Cover", typeof(RectTransform));
                coverGo.transform.SetParent(iconBox.transform, false);
                RectTransform coverRt = coverGo.GetComponent<RectTransform>();
                coverRt.anchorMin = Vector2.zero;
                coverRt.anchorMax = Vector2.one;
                coverRt.offsetMin = Vector2.zero;
                coverRt.offsetMax = Vector2.zero;
                ui.cooldownCover = coverGo.AddComponent<Image>();
                ui.cooldownCover.color = cooldownCoverColor;
                ui.cooldownCover.type = Image.Type.Filled;
                ui.cooldownCover.fillMethod = Image.FillMethod.Vertical;
                ui.cooldownCover.fillOrigin = (int)Image.OriginVertical.Bottom;
                ui.cooldownCover.fillAmount = 0f;
                ui.cooldownCover.raycastTarget = false;

                GameObject pipRow = new GameObject("Pips", typeof(RectTransform));
                pipRow.transform.SetParent(go.transform, false);
                RectTransform pipRt = pipRow.GetComponent<RectTransform>();
                pipRt.anchorMin = new Vector2(0f, 1f);
                pipRt.anchorMax = new Vector2(1f, 1f);
                pipRt.pivot = new Vector2(0.5f, 1f);
                pipRt.anchoredPosition = new Vector2(0f, -66f);
                pipRt.sizeDelta = new Vector2(0f, 10f);
                HorizontalLayoutGroup pipLayout = pipRow.AddComponent<HorizontalLayoutGroup>();
                pipLayout.spacing = 2f;
                pipLayout.childAlignment = TextAnchor.MiddleCenter;
                pipLayout.childControlWidth = pipLayout.childControlHeight = false;
                ui.pipRow = pipRow.transform;

                ui.blockReasonText = AddLabel(go.transform, "", 9f, FontStyles.Italic);
                RectTransform reasonRt = ui.blockReasonText.rectTransform;
                reasonRt.anchorMin = new Vector2(0f, 1f);
                reasonRt.anchorMax = new Vector2(1f, 1f);
                reasonRt.pivot = new Vector2(0.5f, 1f);
                reasonRt.anchoredPosition = new Vector2(0f, -78f);
                reasonRt.sizeDelta = new Vector2(0f, 14f);
                ui.blockReasonText.alignment = TextAlignmentOptions.Center;
                ui.blockReasonText.color = overheatWarningColor;

                // TASK 1.11: the Ultimate slot's own charge meter - a translucent fill drawn OVER the
                // cooldown cover above (built after it, so it draws on top) plus a READY label shown
                // only at full charge. Bound in UpdateUltimateMeter from UltimateCharge.Normalised/
                // IsFull, never the base class's own RechargeProgress - see the SlotUi field comment
                // for why that number means nothing for an ultimate.
                if (isUltimate)
                {
                    GameObject fillGo = new GameObject("Ultimate Charge Fill", typeof(RectTransform));
                    fillGo.transform.SetParent(iconBox.transform, false);
                    RectTransform fillRt = fillGo.GetComponent<RectTransform>();
                    fillRt.anchorMin = Vector2.zero;
                    fillRt.anchorMax = Vector2.one;
                    fillRt.offsetMin = Vector2.zero;
                    fillRt.offsetMax = Vector2.zero;
                    ui.ultimateChargeFill = fillGo.AddComponent<Image>();
                    ui.ultimateChargeFill.color = ultimateChargeColor;
                    ui.ultimateChargeFill.type = Image.Type.Filled;
                    ui.ultimateChargeFill.fillMethod = Image.FillMethod.Vertical;
                    ui.ultimateChargeFill.fillOrigin = (int)Image.OriginVertical.Bottom;
                    ui.ultimateChargeFill.fillAmount = 0f;
                    ui.ultimateChargeFill.raycastTarget = false;

                    ui.readyLabel = AddLabel(iconBox.transform, "READY", 13f, FontStyles.Bold);
                    RectTransform readyRt = ui.readyLabel.rectTransform;
                    readyRt.anchorMin = Vector2.zero;
                    readyRt.anchorMax = Vector2.one;
                    readyRt.offsetMin = Vector2.zero;
                    readyRt.offsetMax = Vector2.zero;
                    ui.readyLabel.color = ultimateReadyTextColor;
                    ui.readyLabel.gameObject.SetActive(false); // UpdateUltimateMeter turns this on once IsFull.
                }
            }

            TextMeshProUGUI keyText = AddLabel(go.transform, keyLabel, 9f, FontStyles.Bold);
            RectTransform keyRt = keyText.rectTransform;
            keyRt.anchorMin = new Vector2(0f, 1f);
            keyRt.anchorMax = new Vector2(0f, 1f);
            keyRt.pivot = new Vector2(0f, 1f);
            keyRt.anchoredPosition = new Vector2(2f, -2f);
            keyRt.sizeDelta = new Vector2(36f, 12f);
            keyText.alignment = TextAlignmentOptions.TopLeft;
            keyText.fontSize = 9f;

            return ui;
        }

        /// <summary>Same recipe as TestRangePanel.AddLabel (see its class comment) - kept private to
        /// this file rather than shared, since the two panels have no other coupling and a shared
        /// utility class would be the only reason to introduce one.</summary>
        private static TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
