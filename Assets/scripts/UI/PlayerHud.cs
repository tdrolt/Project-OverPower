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

        [SerializeField, Tooltip("Colours, text sizes and the bar sprite for this HUD.")]
        private UiTheme theme;

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

            // A missing theme (or a theme with no bar sprite) must not NRE its way through BuildUi -
            // bail out the same clean way the remote-copy check above does. A theme with no sprite is
            // exactly the Task 3 bug (every Filled Image draws full width regardless of fillAmount),
            // so refusing to build with one is the point, not just a safety net.
            if (theme == null)
            {
                Debug.LogError($"[PlayerHud] {name}: UiTheme is not assigned - the HUD cannot be built.");
                enabled = false;
                return;
            }
            if (theme.barSprite == null)
            {
                Debug.LogError($"[PlayerHud] {name}: UiTheme has no Bar Sprite - every filled bar would draw full width regardless of fillAmount, so the HUD is not built.");
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

            // The armor TRACK's visible width scales with capacity - see BuildArmorBar's class
            // comment for why.
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

            Color target = silenced ? theme.overheatSilencedColor : warning ? theme.overheatWarningColor : theme.overheatColor;
            if (warning && theme.pulseAtWarning)
            {
                // A genuine pulse (dimming the same colour), not a colour swap - reads as "this is
                // still your weapon warning you", not a second state.
                float pulse = 1f - theme.pulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * theme.pulseSpeed * Mathf.PI * 2f));
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
                weaponSlotUi.background.color = blocked ? theme.slotBlockedColor : theme.slotReadyColor;
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

                // Captured before either cache is overwritten below: the cover's own gate (next
                // block) needs to know whether CHARGES moved this frame, not just recharge - see its
                // comment for why the two can move independently.
                bool chargesChanged = charges != lastCharges[i] || maxCharges != lastMaxCharges[i];
                if (chargesChanged)
                {
                    SetPips(ui, charges, maxCharges);
                    lastCharges[i] = charges;
                    lastMaxCharges[i] = maxCharges;
                }

                if (chargesChanged || !Mathf.Approximately(recharge, lastRecharge[i]))
                {
                    // charges >= maxCharges (full, or no pool at all - IAbilityStatus.MaxCharges' own
                    // doc comment on the Sprint case) is its OWN condition, not just "recharge == 0":
                    // ChargePool.RechargeProgress reports 0 both when a pool is full AND the instant a
                    // charge is spent (pinned by RechargeProgressIsZeroOnAFullPool), so recharge alone
                    // cannot tell "ready" from "just spent". Without this gate the Ultimate slot's
                    // trivial 1-charge/0s-cooldown pool - whose RechargeProgress is 0 FOREVER, never
                    // just briefly - would show its cover permanently, fully covering the READY meter.
                    ui.cooldownCover.fillAmount = charges >= maxCharges ? 0f : Mathf.Clamp01(1f - recharge);
                    lastRecharge[i] = recharge;
                }

                if (active != lastActive[i] || block != lastBlock[i])
                {
                    ui.background.color = block != CastBlock.None ? theme.slotBlockedColor
                                         : active ? theme.slotActiveGlowColor
                                         : theme.slotReadyColor;
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
                ui.pips[i].color = i < charges ? theme.pipAvailableColor : theme.pipSpentColor;
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
            scaler.referenceResolution = theme.referenceResolution;
            // Match value now lives on UiTheme (shared with the loadout screen, Task 9) rather than
            // being hardcoded per-canvas. This used to be pinned to 1 (match by HEIGHT) because this
            // HUD is anchored to the bottom edge by a fixed reference-unit offset, and matching by
            // height keeps that offset a predictable fraction of screen height on a narrower-than-
            // 16:9 window. UiTheme's default is 0.5 instead - a mix of width and height - per spec,
            // so the HUD stays readable on ultrawide AND 16:10 too, not just narrower windows; Task 3
            // confirmed the panel is still fully on screen at 1920x1080 with this value.
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
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
            healthFill = BuildBar(panel.transform, "Health Bar", BarWidth, 20f, theme.barTrackColor, theme.healthColor, out _);
            overheatFill = BuildBar(panel.transform, "Overheat Bar", BarWidth, theme.overheatBarHeight, theme.barTrackColor, theme.overheatColor, out Image overheatTrack);
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
        /// itself - today just the overheat bar's warning tick, built by the caller right after this
        /// returns. Every bar passes the same theme.barTrackColor for trackColor; the parameter still
        /// exists because BuildArmorBar's track is built separately and needs the same colour.</summary>
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
            // Sprite MUST be set before Type - a Filled Image with no sprite ignores fillAmount and
            // always draws full width. That was the Task 3 bug: every bar changed colour correctly
            // but never visibly emptied or filled.
            fillImg.sprite = theme.barSprite;
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
            tickRt.sizeDelta = new Vector2(theme.overheatTickWidth, 0f);
            tickRt.anchoredPosition = Vector2.zero;
            Image tickImg = tick.AddComponent<Image>();
            tickImg.color = theme.overheatTickColor;
            tickImg.raycastTarget = false;
            return tickRt;
        }

        /// <summary>The armor bar is a fixed-width track (like the other two) holding a "capacity
        /// extent" rectangle whose WIDTH is set live from script (see UpdateHealthAndArmor) rather
        /// than by any layout group - that sidesteps layout-rebuild timing entirely, since a plain
        /// child RectTransform's sizeDelta takes effect immediately. Inside that extent, an ordinary
        /// horizontal fill shows current armor against its own capacity, same as the other bars.
        ///
        /// The extent's WIDTH scales with capacity relative to max health - both are just "hit
        /// points" on the same 0-100-ish scale, so an armor pool as big as max health fills the
        /// whole track, and level 0's starting 25 capacity is a quarter-width sliver. This is what
        /// makes "+Absorb twice" read as a visibly bigger segment, not merely a fuller small
        /// one.</summary>
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
            background.color = theme.barTrackColor;
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
            fillImg.color = theme.shieldColor;
            // See BuildBar's comment: sprite before type, or fillAmount is ignored.
            fillImg.sprite = theme.barSprite;
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
            wash.color = new Color(theme.overheatSilencedColor.r, theme.overheatSilencedColor.g, theme.overheatSilencedColor.b, 0.35f);
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
            iconImg.color = theme.silencedIconColor;
            iconImg.raycastTarget = false;

            GameObject strike = new GameObject("Strike", typeof(RectTransform));
            strike.transform.SetParent(iconGo.transform, false);
            RectTransform strikeRt = strike.GetComponent<RectTransform>();
            strikeRt.anchorMin = strikeRt.anchorMax = strikeRt.pivot = new Vector2(0.5f, 0.5f);
            strikeRt.sizeDelta = new Vector2(30f, 3f);
            strikeRt.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Image strikeImg = strike.AddComponent<Image>();
            strikeImg.color = theme.overheatSilencedColor;
            strikeImg.raycastTarget = false;

            TextMeshProUGUI text = AddLabel(content.transform, "WEAPON SILENCED", 14f, FontStyles.Bold);
            LayoutElement textLe = text.gameObject.AddComponent<LayoutElement>();
            textLe.preferredWidth = 220f;
            textLe.preferredHeight = 20f;
            text.rectTransform.sizeDelta = new Vector2(220f, 20f); // See BuildBar's comment.
            text.color = theme.overheatSilencedColor;
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
            ui.background.color = theme.slotReadyColor;

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
                ui.cooldownCover.color = theme.cooldownCoverColor;
                // See BuildBar's comment: sprite before type, or fillAmount is ignored.
                ui.cooldownCover.sprite = theme.barSprite;
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
                ui.blockReasonText.color = theme.overheatWarningColor;

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
                    ui.ultimateChargeFill.color = theme.ultimateChargeColor;
                    // See BuildBar's comment: sprite before type, or fillAmount is ignored.
                    ui.ultimateChargeFill.sprite = theme.barSprite;
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
                    ui.readyLabel.color = theme.ultimateReadyTextColor;
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
