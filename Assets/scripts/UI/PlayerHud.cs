using System.Collections.Generic;
using System.Globalization;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
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
    /// function. Most writes to a Text or Image are guarded by an explicit change check so an
    /// unmoving bar never re-allocates a string or re-touches a Graphic 60 times a second. The one
    /// exception is overheatFill.color, written unconditionally every frame while warning (it has to
    /// be, to pulse) - harmless because Graphic.color itself no-ops (no dirty flag, no redraw) when
    /// set to the value it already holds, the same guarantee the explicit checks below give by hand.
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

        // One Material instance shared by every TextMeshProUGUI this HUD builds - see AddLabel's
        // comment for why sharing beats letting each text auto-instantiate its own the moment its
        // outline is touched.
        private Material hudTextMaterial;

        // ---- component refs, read off this same player root --------------------------------------

        private PlayerHealth playerHealth;
        private PlayerOverheat playerOverheat;
        private PlayerStatusEffects statusEffects;
        private WeaponFiring weaponFiring;
        private AbilityRunner abilityRunner;
        private UltimateCharge ultimateCharge;
        private GoldWallet goldWallet;
        private OverPowerBuff overPowerBuff;

        // ---- built UI: bars ------------------------------------------------------------------------

        private Image healthFill;
        private Image overheatFill;
        private RectTransform overheatTickRect; // Repositioned live - see UpdateOverheat.
        private Image armorFill;
        private RectTransform armorExtentRect; // The part of the armor track sized by capacity, not by current value.
        private GameObject silencedBanner;
        private TextMeshProUGUI goldText; // "Gold 1234" over "+7.7/s", bottom-right - see BuildGoldCorner.

        // Task 2.4: the transient toast - originally just "Bounty +900", generalised (Task B3,
        // 2026-09-16) into ShowToast(string) so "Respawned at Tier 2: capital under attack" can reuse
        // the exact same label instead of a second one. A single pre-built label toggled on/off (see
        // BuildToast/UpdateToast) rather than instantiated per message, so a toast never allocates UI -
        // the same reasoning the silenced banner above already follows.
        // toastGo is the toast's OWN root (what SetActive actually toggles) - NOT toastText.gameObject,
        // which is a child of it: toggling the child while the parent stays inactive is a no-op (code
        // review fix, Task 2.4 - caught by the 616x576 capture step, which showed no toast at all
        // despite HandleBountyReceived having run).
        private GameObject toastGo;
        private TextMeshProUGUI toastText;
        // Time.unscaledTime the toast should hide by; < 0 means "not currently showing".
        // Unscaled so a debug Time.timeScale change cannot freeze a stale toast on screen forever.
        private float toastHideAtTime = -1f;

        // Task 2.6: a PERSISTENT label (unlike the toast above, which always hides itself on a
        // timer) - shown for as long as the buff is armed or active, however long that turns out
        // to be, not for a fixed duration.
        private TextMeshProUGUI overPowerLabel;

        // ---- built UI: slots -----------------------------------------------------------------------

        /// <summary>The tint target for one slot's border (HUD step 2 follow-up): four thin Image
        /// strips, one per edge, instead of a single Image filling the whole slot rect. A translucent
        /// child can never hide what is under it, so a full-rect Image at near-opaque alpha - what
        /// this used to be - always reads as a solid box no matter how faint the wash on top of it is.
        /// Wrapping the four strips behind one `color` property keeps every call site ("ui.background
        /// .color = ...") the exact one-liner it was when background was a plain Image.</summary>
        private sealed class SlotFrame
        {
            private readonly Image top, bottom, left, right;

            public SlotFrame(Image top, Image bottom, Image left, Image right)
            {
                this.top = top;
                this.bottom = bottom;
                this.left = left;
                this.right = right;
            }

            public Color color
            {
                get => top.color;
                set
                {
                    top.color = value;
                    bottom.color = value;
                    left.color = value;
                    right.color = value;
                }
            }
        }

        /// <summary>One slot's widgets. A class, not a struct, purely so BuildSlot/SetPips can mutate
        /// it in place through the arrays below without juggling copies back and forth.</summary>
        private sealed class SlotUi
        {
            public SlotFrame background; // The border strips - doubles as the ready/blocked/active-glow tint.
            public Image icon;
            public TextMeshProUGUI fallbackNameText;
            public Image cooldownCover;   // Null for the weapon slot - it has no cooldown sweep.
            public Transform pipRow;      // Null for the weapon slot.
            // The coloured FACE of each pip - what SetPips tints. Each face is a child of its own pip root
            // below, because a pip is two Images now (a dark rim and a face on top of it, HUD step 3).
            public readonly List<Image> pips = new List<Image>();
            // The pip roots, in the same order - what SetPips destroys when the charge count changes. Kept
            // separately rather than walking up from a face's parent: one list that owns the lifetime is
            // harder to get wrong than a transform.parent hop that silently orphans the rim.
            public readonly List<GameObject> pipRoots = new List<GameObject>();
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
        private int lastGoldBalance = int.MinValue;
        private double lastGoldIncome = double.MinValue;
        private bool lastOverPowerShown;
        private bool lastOverPowerActive;

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
            goldWallet = GetComponent<GoldWallet>();
            overPowerBuff = GetComponent<OverPowerBuff>();

            if (gameplayConfig == null)
                Debug.LogError($"[PlayerHud] {name}: GameplayConfig is not assigned - the overheat warning threshold and max health fall back to hardcoded numbers.");
            if (playerHealth == null || playerOverheat == null || statusEffects == null || weaponFiring == null || abilityRunner == null)
                Debug.LogError($"[PlayerHud] {name}: missing PlayerHealth/PlayerOverheat/PlayerStatusEffects/WeaponFiring/AbilityRunner on this player - the HUD cannot bind to it.");
            if (ultimateCharge == null)
                Debug.LogError($"[PlayerHud] {name}: no UltimateCharge on this player - the Ultimate slot's charge meter will read as always empty.");
            if (goldWallet == null)
                Debug.LogError($"[PlayerHud] {name}: no GoldWallet on this player - the gold readout will read as always 0.");
            if (overPowerBuff == null)
                Debug.LogWarning($"[PlayerHud] {name}: no OverPowerBuff on this player - the OVERPOWER HUD label will never show (Task 2.6 is cuttable, so this is a warning, not an error).");

            BuildUi();

            if (abilityRunner != null)
                abilityRunner.SlotChanged += HandleSlotChanged;
            if (goldWallet != null)
                goldWallet.BountyReceived += HandleBountyReceived;

            RefreshWeaponSlotContent();
            for (int i = 0; i < AbilitySlotOrder.Length; i++)
                RefreshAbilitySlotContent(i);
        }

        private void OnDestroy()
        {
            if (abilityRunner != null)
                abilityRunner.SlotChanged -= HandleSlotChanged;
            if (goldWallet != null)
                goldWallet.BountyReceived -= HandleBountyReceived;

            // The one Material ApplyOutline clones for every HUD text - nothing else references it,
            // so nothing else will clean it up.
            if (hudTextMaterial != null)
                Destroy(hudTextMaterial);
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
            UpdateGold();
            UpdateToast();
            UpdateOverPower();
            UpdateHealthAndArmor();
            UpdateOverheat();
            UpdateWeaponSlot();
            UpdateAbilitySlots();
        }

        // ============================================================================================
        // OverPower (Task 2.6, GDD p.20)
        // ============================================================================================

        /// <summary>Shows theme.overPowerActiveText while the buff is fully active, the fainter
        /// theme.overPowerArmedText while only armed, and hides the label the rest of the time -
        /// both colours and both strings distinguishing the two states so a glance tells you which
        /// one you are in, the same distinction the silenced banner's own on/off state does not
        /// need but this one does.
        ///
        /// Task 2.6 review fix: the text/colour write used to be gated ONLY on "did active change
        /// since last frame", so the very first frame the label went hidden -> armed (active never
        /// having changed from its default false) skipped that write entirely and the label showed
        /// visible but blank. justShown below forces the same write on the first shown frame
        /// regardless of whether active also happens to have changed.</summary>
        private void UpdateOverPower()
        {
            if (overPowerBuff == null)
                return;

            bool active = overPowerBuff.IsActive;
            bool armed = overPowerBuff.IsArmed;
            bool shown = active || armed;

            bool justShown = shown && !lastOverPowerShown;
            if (shown != lastOverPowerShown)
            {
                overPowerLabel.gameObject.SetActive(shown);
                lastOverPowerShown = shown;
            }
            if (!shown)
                return;

            if (active != lastOverPowerActive || justShown)
            {
                overPowerLabel.text = active ? theme.overPowerActiveText : theme.overPowerArmedText;
                overPowerLabel.color = active ? theme.overPowerActiveColor : theme.overPowerArmedColor;
                lastOverPowerActive = active;
            }
        }

        // ============================================================================================
        // Toast (Task 2.4; generalised beyond bounty payouts in Task B3, 2026-09-16)
        // ============================================================================================

        /// <summary>GoldWallet.BountyReceived handler: shows "Bounty +900" through the same transient
        /// label every other HUD toast now uses. The text is set here, once, on the trigger frame only
        /// - UpdateToast below never touches .text, just the GameObject's active flag, so a bounty
        /// allocates exactly one string no matter how long the toast stays up.</summary>
        private void HandleBountyReceived(int amount) =>
            ShowToast($"Bounty +{amount.ToString(CultureInfo.InvariantCulture)}");

        /// <summary>Shows <paramref name="text"/> in the HUD's one transient toast label for
        /// bountyToastDurationSeconds (UiTheme - the name predates this generalisation, kept rather
        /// than churned for a synonym since it was already the one home for this number), unscaled so
        /// a debug Time.timeScale change cannot freeze a stale toast on screen forever. Same behaviour
        /// the bounty payout always had; PlayerLifecycle's capital-under-attack respawn (Task B3,
        /// 2026-09-16) is the second caller. There is only ONE label: calling this while a toast is
        /// already showing replaces its text and restarts the duration, it does not queue a second one
        /// (B3 review, 2026-09-16).</summary>
        public void ShowToast(string text)
        {
            toastText.text = text;
            toastGo.SetActive(true);
            toastHideAtTime = Time.unscaledTime + theme.bountyToastDurationSeconds;
        }

        private void UpdateToast()
        {
            if (toastHideAtTime < 0f || Time.unscaledTime < toastHideAtTime)
                return;

            toastGo.SetActive(false);
            toastHideAtTime = -1f;
        }

        // ============================================================================================
        // Gold (Task 2.2)
        // ============================================================================================

        /// <summary>The bottom-right readout next to the shop button - "Gold 1234" over "+7.7/s". The formatting
        /// (and the InvariantCulture rule behind it) lives in ShopPricing.GoldHudLabel, where a test pins it.
        /// Still gated on the balance AND the income both being unchanged, so an idle wallet never re-allocates
        /// a string or re-lays-out a text sixty times a second.</summary>
        private void UpdateGold()
        {
            if (goldWallet == null)
                return;

            int balance = goldWallet.Balance;
            double income = goldWallet.IncomePerSecond;
            if (balance == lastGoldBalance && income == lastGoldIncome)
                return;

            goldText.text = ShopPricing.GoldHudLabel(balance, income, theme.goldIncomeSizePercent);
            lastGoldBalance = balance;
            lastGoldIncome = income;
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
            float trackWidth = theme.barWidth - 4f; // matches the 2px margin baked into BuildArmorBar on each side.
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

            Color target;
            if (silenced)
            {
                target = theme.overheatSilencedColor;
            }
            else if (warning)
            {
                // A genuine pulse BETWEEN the two overheat colours (not a dim/brighten of one, and
                // not a flicker) - reads as "this is still your weapon warning you, and it is getting
                // more urgent", not a second on/off state. theme.pulseDepth used to control a
                // brightness dip here; it has no meaning against a two-colour lerp, so Task 5 removed
                // it from UiTheme rather than leave a field nothing reads.
                target = theme.pulseAtWarning
                    ? Color.Lerp(theme.overheatColor, theme.overheatWarningColor, 0.5f + 0.5f * Mathf.Sin(Time.time * theme.pulseSpeed * Mathf.PI * 2f))
                    : theme.overheatWarningColor;
            }
            else
            {
                target = theme.overheatColor;
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
                    // Active wins over blocked (invulnerability rework follow-up, 2026-09-18): several
                    // modules are deliberately still IsActive while blocked (Silenced/Stunned), and every
                    // ultimate reads NotReady the instant a real cast spends its meter - checking block
                    // first hid the one thing "active" exists to show. See SlotTintRule's own class
                    // comment for the per-module survey. The reason text is hidden while active for the
                    // same cause: "not ready" under a glowing, running ultimate is noise once the meter's
                    // own fill already shows it refilling.
                    ui.background.color = SlotTintRule.BorderColor(active, block, theme.slotActiveGlowColor,
                                                                    theme.slotBlockedColor, theme.slotReadyColor);
                    ui.blockReasonText.text = SlotTintRule.ShowsBlockReason(active, block) ? BlockReasonLabel(block) : "";
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
                foreach (GameObject old in ui.pipRoots)
                    Destroy(old);
                ui.pipRoots.Clear();
                ui.pips.Clear();

                for (int i = 0; i < maxCharges; i++)
                {
                    // The pip root IS the dark rim: one Image sized Pip Size + twice the rim, with the coloured
                    // face inset inside it. Two Images per pip instead of one, and no extra layout columns - the
                    // rim is the thing the layout group measures, and the face is its child.
                    GameObject pip = new GameObject("Pip", typeof(RectTransform));
                    pip.transform.SetParent(ui.pipRow, false);
                    LayoutElement le = pip.AddComponent<LayoutElement>();
                    float outer = theme.pipSize + 2f * theme.pipOutlineWidth;
                    le.preferredWidth = outer;
                    le.preferredHeight = outer;
                    // See panelLayout's comment in BuildUi: pipRow's child control is ON, so this
                    // LayoutElement alone becomes the pip's actual rendered size.
                    Image rim = pip.AddComponent<Image>();
                    rim.color = theme.pipOutlineColor;
                    rim.raycastTarget = false;

                    GameObject faceGo = new GameObject("Face", typeof(RectTransform));
                    faceGo.transform.SetParent(pip.transform, false);
                    RectTransform faceRt = faceGo.GetComponent<RectTransform>();
                    faceRt.anchorMin = Vector2.zero;
                    faceRt.anchorMax = Vector2.one;
                    faceRt.offsetMin = new Vector2(theme.pipOutlineWidth, theme.pipOutlineWidth);
                    faceRt.offsetMax = new Vector2(-theme.pipOutlineWidth, -theme.pipOutlineWidth);
                    Image face = faceGo.AddComponent<Image>();
                    face.raycastTarget = false;

                    ui.pipRoots.Add(pip);
                    ui.pips.Add(face);
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
            // keeps the two tools from ever overlapping. Task 5 shrank the chat prompt ("press Enter
            // to chat") from a tall band down to a small corner label, so this no longer needs to
            // clear much - Hud Bottom Offset is a small, theme-tunable gap instead.
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0f);
            panelRt.pivot = new Vector2(0.5f, 0f);
            panelRt.anchoredPosition = new Vector2(0f, theme.hudBottomOffset);
            // Tudor, 2026-09-17: one 20% reduction of the whole HUD, applied ONCE here as a scale rather than by
            // re-typing every size on UiTheme at 80% - a designer still tunes Bar Width, Slot Width and the text
            // sizes in their own units, and Hud Scale is the single number that makes the group smaller or
            // bigger. The pivot above is bottom-centre, so shrinking keeps the HUD's bottom edge exactly Hud
            // Bottom Offset above the screen edge instead of floating up off it.
            panel.transform.localScale = Vector3.one * theme.hudScale;
            // Sized by the ContentSizeFitter below, not by hand - see its comment.

            // NO background image (Tudor, 2026-09-17): the near-opaque slab that used to sit behind the bars and
            // slots was the single biggest thing between a player and the arena. What replaces it is the text
            // treatment itself - a heavier face, a dark outline and a soft drop shadow, all from UiTheme's Text
            // section - plus each bar's own track and each slot's own border. One fewer Graphic here also means
            // one fewer thing in the HUD's draw batch.

            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.spacing = 6f;
            panelLayout.padding = new RectOffset(
                Mathf.RoundToInt(theme.hudPanelPadding), Mathf.RoundToInt(theme.hudPanelPadding),
                Mathf.RoundToInt(theme.hudPanelPadding), Mathf.RoundToInt(theme.hudPanelPadding));
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            // ON, not off: with child control off, each child's RectTransform.sizeDelta is what
            // actually renders while this group reads LayoutElement.preferred* only to POSITION
            // children and size the panel for ContentSizeFitter below - two numbers that have to be
            // hand-kept equal, and silently drift the moment someone edits one without the other.
            // With child control ON, LayoutElement.preferred* is the only number: this group WRITES
            // it onto each child's sizeDelta itself, so every "go.GetComponent<RectTransform>().sizeDelta
            // = ..." line that used to shadow a LayoutElement is gone (code review fix).
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = false;
            panelLayout.childForceExpandHeight = false;

            // The panel's own rect is driven by its content (padding + every bar/row below) instead
            // of a hand-picked sizeDelta - one less number to keep in sync by hand whenever a bar
            // height or slot size changes on UiTheme. childAlignment above therefore never has slack
            // to resolve either way; it is set for clarity, not because it matters here.
            ContentSizeFitter panelFitter = panel.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Task 2.6: the OverPower label - persistent (see the field's own comment), hidden
            // until UpdateOverPower's first armed/active frame, at the top of the HUD panel (HUD
            // step 4 moved the gold row that used to sit above it into its own Gold Corner, bottom-
            // right next to the shop button - see BuildGoldCorner).
            overPowerLabel = AddLabel(panel.transform, "", theme.bodyTextSize, FontStyles.Bold);
            LayoutElement overPowerLe = overPowerLabel.gameObject.AddComponent<LayoutElement>();
            overPowerLe.preferredWidth = theme.barWidth;
            overPowerLe.preferredHeight = theme.bodyTextSize + 8f;
            overPowerLabel.alignment = TextAlignmentOptions.Center;
            overPowerLabel.gameObject.SetActive(false);

            // Step 1 order - slots row, then overheat, then shield/armor, then health, top to
            // bottom - matches [T], the mocked-up layout Tudor approved after Phase 1 (the gold row
            // above is a later addition, Task 2.2, not part of that original mockup). A
            // VerticalLayoutGroup lays children top-to-bottom in the order they are ADDED
            // regardless of childAlignment (alignment only decides where leftover space goes, which
            // the ContentSizeFitter above leaves at zero anyway) - so build order here IS visual
            // order, and this comment is the one place that fact needs recording.
            GameObject slotsRow = new GameObject("Slots Row", typeof(RectTransform));
            slotsRow.transform.SetParent(panel.transform, false);
            float slotsRowHeight = theme.slotIconBoxHeight + theme.slotCooldownAreaHeight;
            // Fix 7 (Playtest polish review): this used to just read theme.barWidth, which happened
            // to equal 4 slots' worth of content only because nothing kept the two numbers in sync -
            // Slot Width or the slot count could change and this row's own preferred width would
            // silently stop matching what it actually contains. Computed from the real content
            // instead: the weapon slot plus one entry per AbilitySlotOrder, spaced by Hud Slot
            // Spacing - see that field's tooltip (and Bar Width's) for the invariant that keeps this
            // landing on the same number as the bars above it.
            int slotCount = AbilitySlotOrder.Length + 1;
            float slotsRowWidth = slotCount * theme.slotWidth + (slotCount - 1) * theme.hudSlotSpacing;
            LayoutElement slotsRowLe = slotsRow.AddComponent<LayoutElement>();
            slotsRowLe.preferredWidth = slotsRowWidth;
            slotsRowLe.preferredHeight = slotsRowHeight;
            // See panelLayout's comment above: panelLayout's own child control (ON) is what turns
            // this LayoutElement into this row's actual rendered size - no sizeDelta line needed here.
            HorizontalLayoutGroup slotsLayout = slotsRow.AddComponent<HorizontalLayoutGroup>();
            slotsLayout.spacing = theme.hudSlotSpacing;
            slotsLayout.childAlignment = TextAnchor.UpperCenter;
            // ON for the same reason as panelLayout above - this group's four slot children each
            // carry a LayoutElement (see BuildSlot) that is now the one place their size lives.
            // Force-expand OFF, same as panelLayout - a runtime AddComponent<HorizontalLayoutGroup>
            // does NOT run the Editor's Reset() (that only fires from the Add Component button), so
            // childForceExpandWidth/Height default to true, not false. Left unset here, child
            // control ON meant every slot got cross-axis stretched to the row's full height instead
            // of staying at its own LayoutElement size - the weapon slot visibly taller than the
            // ability slots below it (code review fix).
            slotsLayout.childControlWidth = slotsLayout.childControlHeight = true;
            slotsLayout.childForceExpandWidth = slotsLayout.childForceExpandHeight = false;

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

            overheatFill = BuildBar(panel.transform, "Overheat Bar", theme.barWidth, theme.overheatBarHeight, theme.overheatColor, out Image overheatTrack);
            overheatTickRect = BuildOverheatTick(overheatTrack.transform);
            armorFill = BuildArmorBar(panel.transform, out armorExtentRect);
            healthFill = BuildBar(panel.transform, "Health Bar", theme.barWidth, theme.healthBarHeight, theme.healthColor, out _);

            BuildGoldCorner(canvasGo.transform);
            BuildToast(canvasGo.transform);
        }

        /// <summary>The gold readout, bottom-right, directly above the "Loadout (P)" button (Tudor, 2026-09-17:
        /// "display the gold generation next to the shop since these systems are tied together"). It used to be
        /// the first row of Hud Panel, where it pushed the bars and slots down and had nothing to do with either.
        ///
        /// It lives on THIS canvas, not on the loadout screen's own toggle canvas, because PlayerHud is what
        /// already holds the GoldWallet and the change caches that keep an unmoving number from re-allocating a
        /// string sixty times a second - see UpdateGold. The two canvases line up because both put their content
        /// inside an identical (1, 0)-anchored, (1, 0)-pivoted root scaled by Hud Scale, so the gap between the
        /// readout and the button is Gold Shop Gap at every screen size and every scale (see
        /// LoadoutScreen.BuildToggleButtonCanvas's Shop Corner).</summary>
        private void BuildGoldCorner(Transform canvasParent)
        {
            GameObject corner = new GameObject("Gold Corner", typeof(RectTransform));
            corner.transform.SetParent(canvasParent, false);
            RectTransform cornerRt = corner.GetComponent<RectTransform>();
            cornerRt.anchorMin = cornerRt.anchorMax = cornerRt.pivot = new Vector2(1f, 0f);
            cornerRt.anchoredPosition = Vector2.zero;
            cornerRt.sizeDelta = Vector2.zero;
            corner.transform.localScale = Vector3.one * theme.hudScale;

            goldText = AddLabel(corner.transform, "", theme.bodyTextSize, FontStyles.Bold);
            RectTransform goldRt = goldText.rectTransform;
            goldRt.anchorMin = goldRt.anchorMax = goldRt.pivot = new Vector2(1f, 0f);
            // Sits on top of the button: the button's own margin, plus the button, plus the gap.
            goldRt.anchoredPosition = new Vector2(
                -theme.loadoutToggleButtonMargin,
                theme.loadoutToggleButtonMargin + theme.loadoutToggleButtonHeight + theme.goldShopGap);
            // Two lines of Body Text Size, the second one smaller - the label writes its own <size> tag, so one
            // TextMeshProUGUI serves both instead of a second one to keep in step.
            goldRt.sizeDelta = new Vector2(theme.loadoutToggleButtonWidth, 2f * theme.bodyTextSize + 10f);
            goldText.color = theme.goldTextColor;
            goldText.alignment = TextAlignmentOptions.Right;
            goldText.enableWordWrapping = false;
        }

        /// <summary>Task 2.4's transient toast (originally just "Bounty +900", generalised in Task B3,
        /// 2026-09-16 - see ShowToast): a fixed-size label parented directly to the canvas (NOT to Hud
        /// Panel's VerticalLayoutGroup - a toast is rare enough that it must not nudge the bars/slots
        /// around every time it shows or hides) and anchored top-centre, clear of both the
        /// bottom-anchored Hud Panel and TestRangePanel's own top-left corner. Built once, hidden until
        /// the first ShowToast call. Fills the toastGo/toastText fields directly rather than returning
        /// anything: callers must toggle the ROOT (toastGo), not the label's own gameObject, which
        /// stays a child of an inactive parent otherwise (see toastGo's own field comment).</summary>
        private void BuildToast(Transform canvasParent)
        {
            GameObject go = new GameObject("Toast", typeof(RectTransform));
            go.transform.SetParent(canvasParent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -theme.hudBottomOffset);
            rt.sizeDelta = new Vector2(theme.barWidth, theme.titleTextSize + 12f);

            TextMeshProUGUI text = AddLabel(go.transform, "", theme.titleTextSize, FontStyles.Bold);
            RectTransform textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            text.color = theme.bountyToastColor;
            text.alignment = TextAlignmentOptions.Center;

            go.SetActive(false); // ShowToast turns this on; UpdateToast turns it off again.
            toastGo = go;
            toastText = text;
        }

        /// <summary>trackImage is handed back so a caller can add something on top of the track
        /// itself - today just the overheat bar's warning tick, built by the caller right after this
        /// returns. Reads theme.barTrackColor directly rather than taking it as a parameter: both
        /// callers (health and overheat) pass that same colour, and BuildArmorBar builds its own
        /// track separately rather than calling this method at all, so a parameter here would only
        /// ever hold one value.</summary>
        private Image BuildBar(Transform parent, string name, float width, float height, Color fillColor, out Image trackImage)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            // See panelLayout's comment in BuildUi: its child control is ON, so this LayoutElement
            // alone becomes the bar's actual rendered size - no sizeDelta line needed here.
            Image background = go.AddComponent<Image>();
            background.color = theme.barTrackColor;
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
            le.preferredWidth = theme.barWidth;
            le.preferredHeight = theme.armorBarHeight;
            // See panelLayout's comment in BuildUi: its child control is ON, so this LayoutElement
            // alone becomes the track's actual rendered size - no sizeDelta line needed here.
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
            wash.color = new Color(theme.overheatSilencedColor.r, theme.overheatSilencedColor.g, theme.overheatSilencedColor.b, theme.silencedWashAlpha);
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
            // ON for the same reason as panelLayout in BuildUi - the icon and label below each carry
            // a LayoutElement that is now the one place their size lives, not a duplicated sizeDelta.
            // Force-expand OFF - see slotsLayout's comment in BuildUi for why a runtime
            // AddComponent needs this said explicitly (code review fix): without it the icon and
            // text both stretched to the row's full height.
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            GameObject iconGo = new GameObject("Weapon Icon", typeof(RectTransform));
            iconGo.transform.SetParent(content.transform, false);
            LayoutElement iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.preferredWidth = theme.silencedIconSize;
            iconLe.preferredHeight = theme.silencedIconSize;
            Image iconImg = iconGo.AddComponent<Image>();
            iconImg.color = theme.silencedIconColor;
            iconImg.raycastTarget = false;

            // Not a layout-group child (parented to the icon, not to Content) - a fixed-size rect
            // rotated in place, so it keeps its own explicit sizeDelta regardless of child control.
            GameObject strike = new GameObject("Strike", typeof(RectTransform));
            strike.transform.SetParent(iconGo.transform, false);
            RectTransform strikeRt = strike.GetComponent<RectTransform>();
            strikeRt.anchorMin = strikeRt.anchorMax = strikeRt.pivot = new Vector2(0.5f, 0.5f);
            strikeRt.sizeDelta = new Vector2(theme.silencedStrikeWidth, theme.silencedStrikeHeight);
            strikeRt.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Image strikeImg = strike.AddComponent<Image>();
            strikeImg.color = theme.overheatSilencedColor;
            strikeImg.raycastTarget = false;

            // Body Text Size, not Small - this is the one HUD state that must read at a glance, the
            // same reasoning Overheat Bar Height gets its own taller-than-the-rest treatment.
            TextMeshProUGUI text = AddLabel(content.transform, "WEAPON SILENCED", theme.bodyTextSize, FontStyles.Bold);
            LayoutElement textLe = text.gameObject.AddComponent<LayoutElement>();
            textLe.preferredHeight = 30f;
            text.color = theme.overheatSilencedColor;
            // Centred, and no pinned preferred WIDTH (Tudor, 2026-09-17): a left-aligned label inside a fixed
            // 260-unit box let the layout group centre the box while the words sat against its left edge, so the
            // banner read as off-centre over the slot row. The group now sizes the label to the words themselves.
            text.alignment = TextAlignmentOptions.Center;

            row.SetActive(false);
            return row;
        }

        /// <summary>One edge strip of a slot's border frame (HUD step 2 follow-up) - see SlotFrame's
        /// class comment for why the frame is four thin Images instead of one filling the whole rect.
        /// anchorMin/anchorMax stretch the strip along the edge it sits on (equal min/max on one axis
        /// pins it to that edge with zero size, which sizeDelta on that same axis then supplies); the
        /// other axis's anchors already span the full slot, so its sizeDelta stays 0.</summary>
        private Image BuildFrameStrip(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                      Vector2 pivot, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = sizeDelta;
            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>One weapon or ability box: a tinted background (ready/blocked/active), an icon
        /// that falls back to the ability's display name when it has none, and - for the three
        /// ability slots only - a charge pip row and a recharge cover sweep. isUltimate additionally
        /// builds the Task 1.11 charge meter (a fill plus a READY label), true for exactly one of
        /// the three ability slots.</summary>
        private SlotUi BuildSlot(Transform parent, string keyLabel, bool withCooldown, bool isUltimate)
        {
            var ui = new SlotUi();

            float slotHeight = withCooldown ? theme.slotIconBoxHeight + theme.slotCooldownAreaHeight : theme.slotIconBoxHeight;

            GameObject go = new GameObject("Slot " + keyLabel, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = theme.slotWidth;
            le.preferredHeight = slotHeight;
            // See panelLayout's comment in BuildUi: slotsLayout's child control is ON, so this
            // LayoutElement alone becomes the slot's actual rendered size - everything inside it
            // (Icon Box, pips, text) then stretches or anchors relative to that rect as normal.

            // The slot's border (HUD step 2 follow-up): four thin strips, one per edge, each Slot Border
            // Width thick, carrying the ready / blocked / active tint that UpdateAbilitySlots writes. NOT
            // a single Image filling the whole rect - a translucent child (Slot Fill, below) can never hide
            // what's under it, so a full-rect Image at 0.9-0.95 alpha always read as a near-opaque box no
            // matter how faint the wash on top of it was, which is the opposite of what Tudor asked for
            // ("no dark opaque background... it takes away from the visibility"). raycastTarget off on all
            // four like every other HUD Graphic - this canvas has no GraphicRaycaster, but a stray raycast
            // target here is exactly the kind of thing that later blocks a shot when someone adds one.
            Image frameTop = BuildFrameStrip(go.transform, "Frame Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, theme.slotBorderWidth));
            Image frameBottom = BuildFrameStrip(go.transform, "Frame Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, theme.slotBorderWidth));
            // Left/Right are inset vertically by Slot Border Width top and bottom (review fix, 2026-09-18) so
            // they sit BETWEEN Frame Top/Bottom instead of running corner to corner - all four strips used to
            // span the full rect on their long axis, so every corner had two strips stacked. Invisible at
            // today's thin width, but the tooltip on Slot Border Width invites raising it, and a raised width
            // would turn every corner into a visibly darker square without this.
            Image frameLeft = BuildFrameStrip(go.transform, "Frame Left", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(theme.slotBorderWidth, -2f * theme.slotBorderWidth));
            Image frameRight = BuildFrameStrip(go.transform, "Frame Right", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(theme.slotBorderWidth, -2f * theme.slotBorderWidth));
            ui.background = new SlotFrame(frameTop, frameBottom, frameLeft, frameRight);
            ui.background.color = theme.slotReadyColor;

            // The only fill a slot has left: a faint wash inset by the border width, so an icon or an ability
            // name still has something to sit on without hiding the arena behind it.
            GameObject slotFillGo = new GameObject("Slot Fill", typeof(RectTransform));
            slotFillGo.transform.SetParent(go.transform, false);
            RectTransform slotFillRt = slotFillGo.GetComponent<RectTransform>();
            slotFillRt.anchorMin = Vector2.zero;
            slotFillRt.anchorMax = Vector2.one;
            slotFillRt.offsetMin = new Vector2(theme.slotBorderWidth, theme.slotBorderWidth);
            slotFillRt.offsetMax = new Vector2(-theme.slotBorderWidth, -theme.slotBorderWidth);
            Image slotFill = slotFillGo.AddComponent<Image>();
            slotFill.color = theme.slotFillColor;
            slotFill.raycastTarget = false;

            // The icon box occupies the top Slot Icon Box Height units - the only part that exists
            // at all on the weapon slot, which has no pip row or recharge sweep below it.
            GameObject iconBox = new GameObject("Icon Box", typeof(RectTransform));
            iconBox.transform.SetParent(go.transform, false);
            RectTransform iconBoxRt = iconBox.GetComponent<RectTransform>();
            iconBoxRt.anchorMin = new Vector2(0f, 1f);
            iconBoxRt.anchorMax = new Vector2(1f, 1f);
            iconBoxRt.pivot = new Vector2(0.5f, 1f);
            iconBoxRt.anchoredPosition = Vector2.zero;
            iconBoxRt.sizeDelta = new Vector2(0f, theme.slotIconBoxHeight);

            // Everything the eye reads as "the ability" lives here, under the key strip: the icon, the fallback
            // name, and (for the ultimate) its READY label. Its own rect - rather than the whole icon box - is
            // what makes them centred in the SQUARE a player sees, instead of centred in a box whose top strip
            // is the key label (Tudor, 2026-09-17: "the text should be in the middle of the ability square").
            GameObject contentBox = new GameObject("Content Box", typeof(RectTransform));
            contentBox.transform.SetParent(iconBox.transform, false);
            RectTransform contentRt = contentBox.GetComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = new Vector2(0f, -theme.slotKeyRowHeight);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(contentBox.transform, false);
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(4f, 4f);
            iconRt.offsetMax = new Vector2(-4f, -4f);
            ui.icon = iconGo.AddComponent<Image>();
            ui.icon.preserveAspect = true;
            ui.icon.raycastTarget = false;
            ui.icon.enabled = false;

            // Body Text Size - Step 2's explicit call: the icon box is sized (Slot Width/Slot Icon
            // Box Height) so the longest short names (Raybeam, Shotgun, Baseline) fit at this size.
            ui.fallbackNameText = AddLabel(contentBox.transform, "", theme.bodyTextSize, FontStyles.Normal);
            RectTransform nameRt = ui.fallbackNameText.rectTransform;
            nameRt.anchorMin = Vector2.zero;
            nameRt.anchorMax = Vector2.one;
            nameRt.offsetMin = new Vector2(3f, 3f);
            nameRt.offsetMax = new Vector2(-3f, -3f);
            ui.fallbackNameText.enableWordWrapping = true;
            ui.fallbackNameText.alignment = TextAlignmentOptions.Center;

            if (withCooldown)
            {
                // The WHOLE icon box, not Content Box: a recharge sweep that stopped short of the key strip
                // would read as a drawing bug, not as a cooldown. It is built after Content Box (so it covers the
                // icon and name) and before the key strip below (so the key stays readable while recharging).
                // Inset by Slot Border Width on all four sides (review fix, 2026-09-18), the same as Slot Fill:
                // the cover used to run edge to edge, painting straight over the frame strips built above for
                // most of every cooldown - the dark box Tudor asked to lose came right back, and the frame's
                // own ready/blocked/active tint disappeared exactly while an ability recharged. The Ultimate
                // Charge Fill below stays full-square on purpose (D5) - only this cover is inset.
                GameObject coverGo = new GameObject("Cooldown Cover", typeof(RectTransform));
                coverGo.transform.SetParent(iconBox.transform, false);
                RectTransform coverRt = coverGo.GetComponent<RectTransform>();
                coverRt.anchorMin = Vector2.zero;
                coverRt.anchorMax = Vector2.one;
                coverRt.offsetMin = new Vector2(theme.slotBorderWidth, theme.slotBorderWidth);
                coverRt.offsetMax = new Vector2(-theme.slotBorderWidth, -theme.slotBorderWidth);
                ui.cooldownCover = coverGo.AddComponent<Image>();
                ui.cooldownCover.color = theme.cooldownCoverColor;
                // See BuildBar's comment: sprite before type, or fillAmount is ignored.
                ui.cooldownCover.sprite = theme.barSprite;
                ui.cooldownCover.type = Image.Type.Filled;
                ui.cooldownCover.fillMethod = Image.FillMethod.Vertical;
                ui.cooldownCover.fillOrigin = (int)Image.OriginVertical.Bottom;
                ui.cooldownCover.fillAmount = 0f;
                ui.cooldownCover.raycastTarget = false;

                // Pip row and block-reason text sit BELOW the icon box, in the Slot Cooldown Area Height band
                // reserved for them - positions derive from Slot Icon Box Height so they never drift out of sync
                // with it. Both heights moved onto UiTheme in HUD step 1/3: they were the last two sizes in this
                // file a designer could not reach.
                // 2 + Pip Row Height + 2 + Slot Reason Text Height has to stay inside Slot Cooldown Area Height
                // (58 today: 2 + 20 + 2 + 26 = 50). If a designer raises the pips past that, the reason line
                // starts overhanging the bottom of the slot - which is why all four numbers are on UiTheme, and
                // why each of their tooltips names this sum.
                float pipRowY = -(theme.slotIconBoxHeight + 2f);
                float reasonY = pipRowY - theme.pipRowHeight - 2f;

                GameObject pipRow = new GameObject("Pips", typeof(RectTransform));
                pipRow.transform.SetParent(go.transform, false);
                RectTransform pipRt = pipRow.GetComponent<RectTransform>();
                pipRt.anchorMin = new Vector2(0f, 1f);
                pipRt.anchorMax = new Vector2(1f, 1f);
                pipRt.pivot = new Vector2(0.5f, 1f);
                pipRt.anchoredPosition = new Vector2(0f, pipRowY);
                pipRt.sizeDelta = new Vector2(0f, theme.pipRowHeight);
                HorizontalLayoutGroup pipLayout = pipRow.AddComponent<HorizontalLayoutGroup>();
                pipLayout.spacing = theme.pipSpacing;
                pipLayout.childAlignment = TextAnchor.MiddleCenter;
                // ON for the same reason as panelLayout in BuildUi - SetPips's own LayoutElement per
                // pip is now the one place their size lives, not a duplicated sizeDelta.
                // Force-expand OFF - see slotsLayout's comment in BuildUi for why a runtime
                // AddComponent needs this said explicitly (code review fix): without it every pip
                // stretched to fill the row, so two 8x8 pips rendered as one wide bar.
                pipLayout.childControlWidth = pipLayout.childControlHeight = true;
                pipLayout.childForceExpandWidth = pipLayout.childForceExpandHeight = false;
                ui.pipRow = pipRow.transform;

                ui.blockReasonText = AddLabel(go.transform, "", theme.smallTextSize, FontStyles.Italic);
                RectTransform reasonRt = ui.blockReasonText.rectTransform;
                reasonRt.anchorMin = new Vector2(0f, 1f);
                reasonRt.anchorMax = new Vector2(1f, 1f);
                reasonRt.pivot = new Vector2(0.5f, 1f);
                reasonRt.anchoredPosition = new Vector2(0f, reasonY);
                reasonRt.sizeDelta = new Vector2(0f, theme.slotReasonTextHeight);
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

                    ui.readyLabel = AddLabel(iconBox.transform, "READY", theme.smallTextSize, FontStyles.Bold);
                    RectTransform readyRt = ui.readyLabel.rectTransform;
                    readyRt.anchorMin = Vector2.zero;
                    readyRt.anchorMax = Vector2.one;
                    readyRt.offsetMin = Vector2.zero;
                    // The same inset Content Box uses, so READY lands in the middle of the square a player sees
                    // rather than in the middle of a box whose top strip is the key label (HUD step 1).
                    readyRt.offsetMax = new Vector2(0f, -theme.slotKeyRowHeight);
                    ui.readyLabel.color = theme.ultimateReadyTextColor;
                    ui.readyLabel.gameObject.SetActive(false); // UpdateUltimateMeter turns this on once IsFull.
                }
            }

            // Tudor, 2026-09-17: centred, not tucked in a corner. A full-width strip across the TOP of the icon
            // box, so the icon/name below it stay centred in the rest of the square (Content Box above). Built
            // last inside the icon box, so it draws over the recharge sweep and the ultimate meter and the key
            // stays readable in every state. Body Text Size, as before: the longest label (SPACE) must fit.
            TextMeshProUGUI keyText = AddLabel(iconBox.transform, keyLabel, theme.bodyTextSize, FontStyles.Bold);
            RectTransform keyRt = keyText.rectTransform;
            keyRt.anchorMin = new Vector2(0f, 1f);
            keyRt.anchorMax = new Vector2(1f, 1f);
            keyRt.pivot = new Vector2(0.5f, 1f);
            keyRt.anchoredPosition = Vector2.zero;
            keyRt.sizeDelta = new Vector2(0f, theme.slotKeyRowHeight);
            keyText.alignment = TextAlignmentOptions.Center;
            keyText.enableWordWrapping = false;

            return ui;
        }

        /// <summary>Same recipe as TestRangePanel.AddLabel (see its class comment) - kept private to
        /// this file rather than shared, since the two panels have no other coupling and a shared
        /// utility class would be the only reason to introduce one. An instance method (not static,
        /// unlike before Task 5) because it now needs theme for the font/colour/outline every HUD
        /// text is built with.</summary>
        private TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            // Font must be assigned BEFORE fontSharedMaterial is touched below - assigning .font
            // switches fontSharedMaterial to that font asset's own default material, which is
            // exactly the template ApplyOutline clones from the first time it runs.
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

        /// <summary>Gives a text an outline via ONE Material instance shared by every text this HUD
        /// builds, instead of the dozen-plus near-identical instances TMP_Text would create on its
        /// own - TMP_Text.outlineWidth/outlineColor each auto-clone fontSharedMaterial into a fresh
        /// per-object instance (fontMaterial) the first time either is touched, so setting them
        /// directly on every label would mean one material per label, all with the same two numbers.
        /// Setting the shared material's shader properties once up front and handing every label the
        /// SAME instance avoids that, and lets every HUD text batch into fewer draw calls besides.
        /// Built lazily from the first label's font (all HUD labels share theme.font, so the shader
        /// this material's cloned from is the same for every text this method is ever called for).
        /// The outline, weight and shadow numbers themselves live on UiTheme.ApplyHudTextStyle (HUD
        /// step 2) - one home, shared with the loadout screen and the minimap.</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (hudTextMaterial == null)
            {
                hudTextMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(hudTextMaterial);
            }
            tmp.fontSharedMaterial = hudTextMaterial;
        }
    }
}
