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
    /// The local player's own on-screen HUD: health, armor and overheat bars, the weapon slot and the three ability
    /// slots. Built in code like TestRangePanel (every listener sits on its control's line, so nothing is wired to the
    /// wrong button). SCREEN SPACE, OWNER ONLY (created when photonView.IsMine), unlike HealthBarCanvas, the world-space
    /// bar everyone sees. Polls every value in LateUpdate, so it reads this frame's final state, rather than subscribing
    /// per bar; writes are guarded by change checks so an unmoving bar allocates nothing. The exception is
    /// overheatFill.color, written every frame while warning (to pulse); Graphic.color no-ops when set to its own value.
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

        // One Material shared by every TextMeshProUGUI this HUD builds, instead of each auto-instantiating its own the
        // moment its outline is touched (see AddLabel).
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
        private RectTransform overheatTickRect; // Repositioned live (UpdateOverheat).
        private Image ventBandImage; // The Vent window band, repositioned/recoloured live (UpdateVentBand).
        private Image armorFill;
        private RectTransform armorExtentRect; // The part of the armor track sized by capacity, not by current value.

        // The yellow immunity look is a FRAME round each bar (four thin edge Images), not a recolour and not a translucent
        // overlay (a wash over blue read grey, see UiTheme.immuneBarColor). One frame each: the HUD's health and armor bars
        // are two separate tracks (the overhead bar draws shield over health in the SAME rect). Both start inactive.
        private ImmuneFrame healthImmuneFrame;
        private ImmuneFrame armorImmuneFrame;
        private bool lastImmuneLook;

        // ---- built UI: damage numbers ---------------------------------------------------------------

        // The mark diamond shares this overlay canvas/rect; no other reference to its canvas is held past BuildUi.
        private RectTransform hitFeedbackCanvasRect;
        private GameObject silencedBanner;
        private TextMeshProUGUI goldText; // "Gold 1234" over "+7.7/s", bottom-right (BuildGoldCorner).

        // The transient toast: one pre-built label toggled on/off (BuildToast/UpdateToast), so a toast never allocates UI,
        // used by every ShowToast(string) caller (bounty, respawn tier...).
        // toastGo is the toast's OWN root, what SetActive toggles - NOT toastText.gameObject, its child: toggling the
        // child while the parent is inactive is a no-op (the toast silently never showed).
        private GameObject toastGo;
        private TextMeshProUGUI toastText;
        // Time.unscaledTime the toast should hide by; < 0 means "not currently showing". Unscaled so a debug
        // Time.timeScale change cannot freeze a stale toast on screen.
        private float toastHideAtTime = -1f;

        // A PERSISTENT label (the toast hides on a timer): shown for as long as the buff is armed or active.
        private TextMeshProUGUI overPowerLabel;

        // ---- built UI: slots -----------------------------------------------------------------------

        /// <summary>The tint target for one slot's border: four thin Image strips, one per edge, instead of one Image
        /// filling the slot rect (a translucent child never hides what is under it, so a full-rect Image always reads as
        /// a solid box). One `color` property keeps every call site a one-liner.</summary>
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

        /// <summary>The yellow "shield immunity" look for ONE HUD bar (a wash could never read yellow over the blue shield
        /// fill, see UiTheme.immuneBarColor). `edges` reuses SlotFrame (BuildFrameStrip) and `wash` is the optional faint
        /// reinforcement UNDER them (Immune Bar Wash Alpha; 0 = frame only). Both live under one root so
        /// UpdateHealthAndArmor shows/hides the whole look with a single SetActive.</summary>
        private sealed class ImmuneFrame
        {
            public readonly GameObject root;
            private readonly SlotFrame edges;
            private readonly Image wash;

            public ImmuneFrame(GameObject root, SlotFrame edges, Image wash)
            {
                this.root = root;
                this.edges = edges;
                this.wash = wash;
            }

            /// <summary>frameColor keeps ITS OWN alpha (Immune Bar Colour); washAlpha overrides the wash's alpha
            /// independently, so the two never fight over one shared alpha.</summary>
            public void Apply(Color frameColor, float washAlpha)
            {
                edges.color = frameColor;
                Color washColor = frameColor;
                washColor.a = washAlpha;
                wash.color = washColor;
            }

            public void SetShown(bool shown) => root.SetActive(shown);
        }

        /// <summary>One slot's widgets. A class so BuildSlot/SetPips mutate it in place through the arrays below.</summary>
        private sealed class SlotUi
        {
            public SlotFrame background; // The border strips - doubles as the ready/blocked/active-glow tint.
            public Image icon;
            public TextMeshProUGUI fallbackNameText;
            public Image cooldownCover;   // Null for the weapon slot - it has no cooldown sweep.
            public Transform pipRow;      // Null for the weapon slot.
            // The coloured FACE of each pip, what SetPips tints; a pip is two Images (a dark rim and the face on it).
            public readonly List<Image> pips = new List<Image>();
            // The pip roots, same order, what SetPips destroys when the charge count changes: one list that owns the
            // lifetime, rather than a parent hop that would orphan the rim.
            public readonly List<GameObject> pipRoots = new List<GameObject>();
            public TextMeshProUGUI blockReasonText; // Null for the weapon slot.

            // Ultimate slot only. A separate overlay from cooldownCover: the module's own charge pool is a trivial
            // 1-charge/0-cooldown pool that recovers the instant it is spent, so its RechargeProgress never reflects the
            // real gate; UltimateCharge.Normalised is read directly instead.
            public Image ultimateChargeFill;
            public TextMeshProUGUI readyLabel;
        }

        private SlotUi weaponSlotUi;
        private readonly SlotUi[] abilitySlotUi = new SlotUi[3];

        // Index order for abilitySlotUi and every "lastXxx" array below; Primary (the weapon) is separate, no runner slot.
        private static readonly (AbilitySlot slot, string keyLabel)[] AbilitySlotOrder =
        {
            (AbilitySlot.Attachment, "RMB"),
            (AbilitySlot.Ultimate, "SPACE"),
            (AbilitySlot.Mobility, "SHIFT"),
        };

        // ---- change-detection caches: why LateUpdate never allocates ----

        private float lastHealthFraction = -1f;
        private float lastArmorFraction = -1f;
        private float lastArmorExtentWidth = -1f;
        private float lastOverheatFraction = -1f;
        private float lastWarningThreshold01 = -1f;
        private bool lastSilenced;
        private VentOutcome lastVentOutcome = VentOutcome.None; // Detects the instant a Hit lands (UpdateVentBand).
        private float ventHitFlashHideAtTime = -1f; // Time.unscaledTime the hit flash should hide by.
        private bool lastWeaponBlocked;
        private WeaponDefinition lastWeaponDef;
        private readonly int[] lastCharges = { -1, -1, -1 };
        private readonly bool[] lastLocked = new bool[3];
        private readonly int[] lastMaxCharges = { -1, -1, -1 };
        private readonly float[] lastRecharge = { -1f, -1f, -1f };
        private readonly bool[] lastActive = { false, false, false };
        private readonly CastBlock[] lastBlock = { (CastBlock)(-1), (CastBlock)(-1), (CastBlock)(-1) };
        private float lastUltimateCharge = -1f;
        private bool lastUltimateReady;
        private bool lastUltimateRecast;
        private bool lastUltimateNone;
        private int lastGoldBalance = int.MinValue;
        private double lastGoldIncome = double.MinValue;
        private bool lastOverPowerShown;
        private bool lastOverPowerActive;
        private GameObject statusRoot;
        private TextMeshProUGUI statusText;
        private Image statusFill;
        private StatusLabel lastStatusLabel = (StatusLabel)(-1);
        private float lastStatusFill = -1f;

        /// <summary>The local player's HUD, or null before it is built.</summary>
        public static PlayerHud Local { get; private set; }

        private RectTransform panelRect;
        private static readonly Vector3[] cornerBuffer = new Vector3[4];

        /// <summary>The bars-and-slots panel in screen pixels (the HUD canvas is screen space overlay). False while the HUD is not built.</summary>
        public bool TryGetScreenRect(out Rect screenRect)
        {
            screenRect = default;
            if (panelRect == null) return false;
            panelRect.GetWorldCorners(cornerBuffer);
            screenRect = Rect.MinMaxRect(cornerBuffer[0].x, cornerBuffer[0].y, cornerBuffer[2].x, cornerBuffer[2].y);
            return true;
        }

        private void Awake()
        {
            // Every remote copy stays permanently dormant.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            // A missing theme or bar sprite must not NRE through BuildUi. A theme with no sprite makes every Filled Image
            // draw full width regardless of fillAmount, so refusing to build with one is the point.
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
            Local = this;

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
            if (Local == this) Local = null;
            if (abilityRunner != null)
                abilityRunner.SlotChanged -= HandleSlotChanged;
            if (goldWallet != null)
                goldWallet.BountyReceived -= HandleBountyReceived;

            // The one Material ApplyOutline clones for every HUD text; nothing else references it, so nothing else frees it.
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
            UpdateStatusLabel();
            UpdateHealthAndArmor();
            UpdateOverheat();
            UpdateWeaponSlot();
            UpdateAbilitySlots();
        }

        // ============================================================================================
        // OverPower (GDD p.20)
        // ============================================================================================

        /// <summary>Shows theme.overPowerActiveText while the buff is active, the fainter overPowerArmedText while only
        /// armed, and hides the label otherwise; text and colour both differ so a glance tells the states apart.
        /// The text/colour write is not gated on "active changed" alone: justShown forces it on the first shown frame,
        /// or a hidden -> armed label (active still false) would show blank.</summary>
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
        // Toast
        // ============================================================================================

        /// <summary>GoldWallet.BountyReceived handler: shows "Bounty +900" through the shared toast label. The text is set
        /// once, on the trigger frame; UpdateToast only toggles the active flag, so a bounty allocates one string.</summary>
        private void HandleBountyReceived(int amount) =>
            ShowToast($"Bounty +{amount.ToString(CultureInfo.InvariantCulture)}");

        /// <summary>Shows <paramref name="text"/> in the HUD's one transient toast label for bountyToastDurationSeconds
        /// (UiTheme; the name predates the toast serving other callers), unscaled. There is only ONE label: calling this
        /// while a toast shows replaces its text and restarts the duration, it does not queue.</summary>
        public void ShowToast(string text)
        {
            toastText.text = text;
            toastGo.SetActive(true);
            toastHideAtTime = Time.unscaledTime + theme.bountyToastDurationSeconds;
        }

        /// <summary>The "Two teams left" banner for every surviving player the instant three teams narrow to two. The
        /// same toast label; MatchDirector calls this rather than ShowToast because it is added at runtime with no
        /// Inspector to hold a UiTheme, and the theme stays private to this HUD.</summary>
        public void ShowTwoTeamsLeftBanner() => ShowToast(theme.twoTeamsLeftBannerText);

        /// <summary>The toast shown the instant MatchDirector.ReactToRoomState sees this client's own live edge: the
        /// ordinary three-team text, or the host-start two-team text.</summary>
        public void ShowMatchLiveToast(bool twoTeams) => ShowToast(twoTeams ? theme.matchLiveTwoTeamsToastText : theme.matchLiveToastText);

        private void UpdateToast()
        {
            if (toastHideAtTime < 0f || Time.unscaledTime < toastHideAtTime)
                return;

            toastGo.SetActive(false);
            toastHideAtTime = -1f;
        }

        // ============================================================================================
        // Gold
        // ============================================================================================

        /// <summary>The bottom-right readout next to the shop button, "Gold 1234" over "+7.7/s". Formatting (and the
        /// InvariantCulture rule) is ShopPricing.GoldHudLabel. Gated on balance AND income both unchanged, so an idle
        /// wallet never re-allocates a string or re-lays-out a text.</summary>
        private void UpdateGold()
        {
            if (goldWallet == null)
                return;

            // Dominion has no gold: the readout is not drawn at all.
            bool showGold = Overpower.Dominion.DominionTerritoryRules.ShowsGold(Overpower.Dominion.DominionMode.IsActive());
            if (goldText.gameObject.activeSelf != showGold)
                goldText.gameObject.SetActive(showGold);
            if (!showGold)
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

            // Read live from the same asset PlayerHealth uses, so retuning it in Play Mode moves the HUD at once.
            float maxHealth = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;

            float healthFraction = maxHealth > 0f ? Mathf.Clamp01(playerHealth.Health / maxHealth) : 0f;
            if (!Mathf.Approximately(healthFraction, lastHealthFraction))
            {
                healthFill.fillAmount = healthFraction;
                lastHealthFraction = healthFraction;
            }

            // The armor TRACK's visible width scales with capacity (BuildArmorBar says why).
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

            // Both bars turn yellow together, only while PlayerHealth's clock says the immunity (not merely the armed
            // trap) is running (ShowsImmuneLook).
            bool immune = playerHealth.ShowsImmuneLook;
            if (immune != lastImmuneLook)
            {
                lastImmuneLook = immune;
                healthImmuneFrame.Apply(theme.immuneBarColor, theme.immuneBarWashAlpha);
                armorImmuneFrame.Apply(theme.immuneBarColor, theme.immuneBarWashAlpha);
                healthImmuneFrame.SetShown(immune);
                armorImmuneFrame.SetShown(immune);
            }
        }

        private void UpdateOverheat()
        {
            if (playerOverheat == null)
                return;

            float fraction = playerOverheat.Normalised;
            bool silenced = playerOverheat.IsSilenced;

            // Deliberately NOT PlayerOverheat.IsWarning: that is backed by a threshold captured once at spawn. Reading
            // GameplayConfig every frame lets a designer retune it in Play Mode and see the colour move at once.
            float warningThreshold01 = gameplayConfig != null && gameplayConfig.OverheatMax > 0f
                ? gameplayConfig.OverheatWarningThreshold / gameplayConfig.OverheatMax
                : 0.8f;
            bool warning = !silenced && fraction >= warningThreshold01;

            // The tick mark moves with the same live threshold, so colour boundary and mark move together.
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
                // A genuine pulse BETWEEN the two overheat colours (not a dim/brighten, not a flicker): it reads as the
                // same warning getting more urgent, not a second on/off state.
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
                // The banner is specifically the "silenced by overheat, draining" cue, not the general block tint (also stun, death).
                silencedBanner.SetActive(silenced);
                lastSilenced = silenced;
            }

            UpdateVentBand(silenced);
        }

        /// <summary>The Vent band's live look and position; VentBandLookRule makes the dim/bright/hit/miss decision. Not
        /// change-guarded: the band's anchors move every frame the window is open, so there is no idle case to protect.</summary>
        private void UpdateVentBand(bool silenced)
        {
            bool windowOpen = playerOverheat.IsVentWindowOpen;
            VentOutcome outcome = playerOverheat.VentOutcome;
            VentBandLook look = VentBandLookRule.Determine(silenced, windowOpen, outcome, playerOverheat.VentEnabled);

            // The hit flash is the one look that expires on its own (UiTheme.ventHitFlashSeconds), unlike a miss which
            // lasts the silence: arm the countdown the instant Outcome first reads Hit, unscaled as the toast's is.
            if (outcome == VentOutcome.Hit && lastVentOutcome != VentOutcome.Hit)
                ventHitFlashHideAtTime = Time.unscaledTime + theme.ventHitFlashSeconds;
            lastVentOutcome = outcome;

            if (look == VentBandLook.Hit && Time.unscaledTime >= ventHitFlashHideAtTime)
                look = VentBandLook.Hidden; // flash ran its course - the bar itself already halved, that's the read

            if (look == VentBandLook.Hidden)
            {
                if (ventBandImage.gameObject.activeSelf)
                    ventBandImage.gameObject.SetActive(false);
                return;
            }

            if (!ventBandImage.gameObject.activeSelf)
                ventBandImage.gameObject.SetActive(true);

            float high = Mathf.Clamp01(playerOverheat.VentBandHighFraction);
            float low = Mathf.Clamp01(playerOverheat.VentBandLowFraction);
            RectTransform bandRt = (RectTransform)ventBandImage.transform;
            bandRt.anchorMin = new Vector2(low, 0f);
            bandRt.anchorMax = new Vector2(high, 1f);

            ventBandImage.color = look switch
            {
                VentBandLook.Bright => theme.ventBandOpenColor,
                VentBandLook.Hit => theme.ventBandHitColor,
                VentBandLook.Miss => theme.ventBandMissColor,
                _ => theme.ventBandDimColor,
            };
        }

        // ============================================================================================
        // Weapon slot (icon/name only - no cooldown sweep, no charges; the fourth slot is the weapon, not an ability).
        // ============================================================================================

        private void UpdateWeaponSlot()
        {
            if (weaponFiring == null)
                return;

            WeaponDefinition current = weaponFiring.Weapon;
            if (current != lastWeaponDef)
                RefreshWeaponSlotContent();

            // Same actor-wide rule WeaponFiring.TryFire asks (CastGate.ForActor): dead, stunned or silenced all block the
            // trigger, so the slot greys out for the same reasons as the ability slots, not overheat alone.
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

            // Most abilities have no icon yet: ApplyIconOrFallback is the one place that is decided (shared with the weapon slot).
            ApplyIconOrFallback(ui, def != null ? def.Icon : null, def != null ? def.DisplayName : "");

            int maxCharges = status != null ? status.MaxCharges : 0;
            SetPips(ui, status != null ? status.ChargesAvailable : 0, maxCharges, status != null && status.ChargesLocked);

            // Force every cached value stale so the next LateUpdate redraws this slot even if the new ability's numbers match the old one's.
            lastCharges[index] = -1;
            lastLocked[index] = status != null && status.ChargesLocked;
            lastMaxCharges[index] = maxCharges;
            lastRecharge[index] = -1f;
            lastActive[index] = !status?.IsActive ?? true;
            lastBlock[index] = (CastBlock)(-1);

            if (AbilitySlotOrder[index].slot == AbilitySlot.Ultimate)
            {
                // Force the label logic to run again for the new ultimate (or the emptied slot).
                lastUltimateCharge = -1f;
                lastUltimateReady = lastUltimateRecast = lastUltimateNone = true; // an impossible combination, so the next update always redraws
            }
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

                // Captured before either cache is overwritten: the cover's gate needs to know whether CHARGES moved this
                // frame, not just recharge (the two move independently).
                bool locked = status != null && status.ChargesLocked;
                bool chargesChanged = charges != lastCharges[i] || maxCharges != lastMaxCharges[i];
                // The lock can flip without the count moving (running dry locks at 0 charges, which the
                // count may already show), so the marks redraw on a lock change too.
                if (chargesChanged || locked != lastLocked[i])
                {
                    SetPips(ui, charges, maxCharges, locked);
                    lastCharges[i] = charges;
                    lastMaxCharges[i] = maxCharges;
                    lastLocked[i] = locked;
                }

                if (chargesChanged || !Mathf.Approximately(recharge, lastRecharge[i]))
                {
                    // charges >= maxCharges (full, or no pool at all, see IAbilityStatus.MaxCharges) is its OWN condition,
                    // not "recharge == 0": ChargePool.RechargeProgress is 0 both when full AND the instant a charge is
                    // spent (RechargeProgressIsZeroOnAFullPool), so recharge alone cannot tell "ready" from "just spent".
                    // Without it the Ultimate's trivial pool, whose progress is 0 FOREVER, would cover the READY meter.
                    ui.cooldownCover.fillAmount = charges >= maxCharges ? 0f : Mathf.Clamp01(1f - recharge);
                    lastRecharge[i] = recharge;
                }

                if (active != lastActive[i] || block != lastBlock[i])
                {
                    // Active wins over blocked: several modules are deliberately still IsActive while blocked (Flamethrower/
                    // Invulnerability through Stunned or Silenced, Dash/ZipGun through Silenced) and every ultimate reads
                    // NotReady the instant a cast spends its meter, so checking block first hid what "active" exists to
                    // show. Dead is the one reason that still wins (SlotTintRule has the survey). The reason text is hidden
                    // while active for the same cause ("not ready" under a glowing ultimate is noise).
                    ui.background.color = SlotTintRule.BorderColor(active, block, theme.slotActiveGlowColor,
                                                                    theme.slotBlockedColor, theme.slotReadyColor);
                    ui.blockReasonText.text = SlotTintRule.ShowsBlockReason(active, block) ? BlockReasonLabel(block) : "";
                    lastActive[i] = active;
                    lastBlock[i] = block;
                }

                if (slot == AbilitySlot.Ultimate)
                    UpdateUltimateMeter(ui, block, status != null);
            }
        }

        /// <summary>A translucent fill over the Ultimate slot's icon tracking UltimateCharge.Normalised, and a READY label
        /// once IsFull, the same moment Space actually casts. Independent of the slot's cooldown cover, which for an
        /// ultimate reflects only the trivial always-instant pool, never the real gate.</summary>
        private void UpdateUltimateMeter(SlotUi ui, CastBlock block, bool equipped)
        {
            if (ui.ultimateChargeFill == null)
                return; // Built only for the Ultimate slot (BuildSlot's isUltimate).

            float normalised = ultimateCharge != null ? ultimateCharge.Normalised : 0f;
            bool ready = ultimateCharge != null && ultimateCharge.IsFull;

            // "No ultimate" wins over the meter and READY/THROW; the meter keeps filling underneath.
            UltimateSlotState state = UltimateSlotRule.StateFor(equipped, ready, block == CastBlock.None);
            bool noUltimate = state == UltimateSlotState.NoUltimate;
            float shown = noUltimate ? 0f : normalised;

            if (!Mathf.Approximately(shown, lastUltimateCharge))
            {
                ui.ultimateChargeFill.fillAmount = shown;
                lastUltimateCharge = shown;
            }

            bool showReady = state == UltimateSlotState.Ready;
            bool recast = state == UltimateSlotState.Throw;

            if (showReady != lastUltimateReady || recast != lastUltimateRecast || noUltimate != lastUltimateNone)
            {
                ui.readyLabel.gameObject.SetActive(showReady || recast || noUltimate);
                ui.readyLabel.text = noUltimate ? theme.ultimateNoneText : recast ? theme.ultimateRecastText : theme.ultimateReadyText;
                lastUltimateReady = showReady;
                lastUltimateRecast = recast;
                lastUltimateNone = noUltimate;
            }

            // The fallback name sits where the label does, so it hides while a label shows (they overprinted). Set every
            // frame, not on change: a loadout swap re-enables the name via ApplyIconOrFallback and this undoes it next frame.
            ui.fallbackNameText.enabled = !ui.readyLabel.gameObject.activeSelf && !ui.icon.enabled;
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

        private void SetPips(SlotUi ui, int charges, int maxCharges, bool locked)
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
                    // The pip root IS the dark rim (Pip Size + twice the rim) with the coloured face inset in it: the rim is
                    // what the layout group measures, the face is its child, so no extra layout columns.
                    GameObject pip = new GameObject("Pip", typeof(RectTransform));
                    pip.transform.SetParent(ui.pipRow, false);
                    LayoutElement le = pip.AddComponent<LayoutElement>();
                    float outer = theme.pipSize + 2f * theme.pipOutlineWidth;
                    le.preferredWidth = outer;
                    le.preferredHeight = outer;
                    // pipRow's child control is ON (see panelLayout in BuildUi): this LayoutElement alone is the pip's size.
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
                ui.pips[i].color = locked
                    ? (i < charges ? theme.pipLockedColor : theme.pipLockedSpentColor)
                    : (i < charges ? theme.pipAvailableColor : theme.pipSpentColor);
        }

        // ============================================================================================
        // UI construction - built in code, following TestRangePanel's pattern.
        // ============================================================================================

        private void BuildUi()
        {
            GameObject canvasGo = new GameObject("Player Hud Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below both the F1 test panel (order 500) and MatchUI's win/lose/respawn panels (default overlay canvas): the
            // HUD must never cover either. overrideSorting makes that true whatever the creation order.
            canvas.overrideSorting = true;
            canvas.sortingOrder = -10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            // The match value lives on UiTheme, shared with the loadout screen. A mix of width and height (default 0.5)
            // keeps the HUD readable on ultrawide and 16:10, where matching by height alone only suited narrow windows.
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster and no EventSystem: nothing on this HUD is clickable, and a second EventSystem would warn.

            GameObject panel = new GameObject("Hud Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRect = panelRt;
            // Bottom-centre: the F1 test panel owns the top-left, so the two never overlap. Hud Bottom Offset is the
            // theme-tunable gap above the screen edge.
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0f);
            panelRt.pivot = new Vector2(0.5f, 0f);
            panelRt.anchoredPosition = new Vector2(0f, theme.hudBottomOffset);
            // Hud Scale shrinks the whole HUD ONCE here as a scale, so a designer still tunes Bar Width, Slot Width and the
            // text sizes in their own units. The pivot is bottom-centre, so the bottom edge stays Hud Bottom Offset above
            // the screen edge.
            panel.transform.localScale = Vector3.one * theme.hudScale;

            // NO background image: a slab behind the bars and slots hid the arena. The text treatment (heavier face,
            // outline, soft shadow from UiTheme's Text section) plus each bar's track and each slot's border replace it.

            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.spacing = 6f;
            panelLayout.padding = new RectOffset(
                Mathf.RoundToInt(theme.hudPanelPadding), Mathf.RoundToInt(theme.hudPanelPadding),
                Mathf.RoundToInt(theme.hudPanelPadding), Mathf.RoundToInt(theme.hudPanelPadding));
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            // ON, not off: with child control off, sizeDelta is what renders while LayoutElement.preferred* only
            // positions children and sizes the panel, two numbers that drift apart. With it ON, LayoutElement.preferred*
            // is the only number: the group writes it onto each child's sizeDelta.
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = false;
            panelLayout.childForceExpandHeight = false;

            // The panel's rect is driven by its content (padding + every bar/row), not a hand-picked sizeDelta, so a bar
            // height or slot size change on UiTheme needs no second edit.
            ContentSizeFitter panelFitter = panel.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The OverPower label: persistent, hidden until UpdateOverPower's first armed/active frame, at the top of the
            // panel (gold lives in its own corner, BuildGoldCorner).
            overPowerLabel = AddLabel(panel.transform, "", theme.bodyTextSize, FontStyles.Bold);
            LayoutElement overPowerLe = overPowerLabel.gameObject.AddComponent<LayoutElement>();
            overPowerLe.preferredWidth = theme.barWidth;
            overPowerLe.preferredHeight = theme.bodyTextSize + 8f;
            overPowerLabel.alignment = TextAlignmentOptions.Center;
            overPowerLabel.gameObject.SetActive(false);

            // Top to bottom: slots row, overheat, shield/armor, health (the approved mockup). A VerticalLayoutGroup lays
            // children out in the order they are ADDED, so build order here IS visual order.
            GameObject slotsRow = new GameObject("Slots Row", typeof(RectTransform));
            slotsRow.transform.SetParent(panel.transform, false);
            float slotsRowHeight = theme.slotIconBoxHeight + theme.slotCooldownAreaHeight;
            // Computed from the real content (the weapon slot plus one per AbilitySlotOrder, spaced by Hud Slot Spacing),
            // not read from theme.barWidth, which only matched by luck when Slot Width or the slot count changed. Hud Slot
            // Spacing's and Bar Width's tooltips give the invariant that keeps it equal to the bars' width.
            int slotCount = AbilitySlotOrder.Length + 1;
            float slotsRowWidth = slotCount * theme.slotWidth + (slotCount - 1) * theme.hudSlotSpacing;
            LayoutElement slotsRowLe = slotsRow.AddComponent<LayoutElement>();
            slotsRowLe.preferredWidth = slotsRowWidth;
            slotsRowLe.preferredHeight = slotsRowHeight;
            // panelLayout's child control (ON) turns this LayoutElement into the row's rendered size.
            HorizontalLayoutGroup slotsLayout = slotsRow.AddComponent<HorizontalLayoutGroup>();
            slotsLayout.spacing = theme.hudSlotSpacing;
            slotsLayout.childAlignment = TextAnchor.UpperCenter;
            // ON as panelLayout: each slot's LayoutElement (BuildSlot) is the one place its size lives. Force-expand OFF:
            // a runtime AddComponent does NOT run the Editor's Reset(), so childForceExpandWidth/Height default to true
            // and every slot got stretched to the row's full height (the weapon slot visibly taller).
            slotsLayout.childControlWidth = slotsLayout.childControlHeight = true;
            slotsLayout.childForceExpandWidth = slotsLayout.childForceExpandHeight = false;

            weaponSlotUi = BuildSlot(slotsRow.transform, "LMB", withCooldown: false, isUltimate: false);
            for (int i = 0; i < AbilitySlotOrder.Length; i++)
            {
                bool isUltimate = AbilitySlotOrder[i].slot == AbilitySlot.Ultimate;
                abilitySlotUi[i] = BuildSlot(slotsRow.transform, AbilitySlotOrder[i].keyLabel, withCooldown: true, isUltimate: isUltimate);
            }

            // Built LAST and parented to the row (not the panel): a child with ignoreLayout stretched over the row's rect
            // draws over the slots without any layout group counting it, so toggling it never shifts the row.
            silencedBanner = BuildSilencedBanner(slotsRow.transform);

            overheatFill = BuildBar(panel.transform, "Overheat Bar", theme.barWidth, theme.overheatBarHeight, theme.overheatColor, out Image overheatTrack);
            overheatTickRect = BuildOverheatTick(overheatTrack.transform);
            ventBandImage = BuildVentBand(overheatTrack.transform);
            armorFill = BuildArmorBar(panel.transform, out armorExtentRect);
            // armorExtentRect.parent is the armor bar's TRACK root: the frame goes there, not on the extent, so it covers
            // the WHOLE bar rather than shrinking with a part-empty capacity.
            armorImmuneFrame = BuildImmuneFrame(armorExtentRect.parent);
            healthFill = BuildBar(panel.transform, "Health Bar", theme.barWidth, theme.healthBarHeight, theme.healthColor, out _);
            healthImmuneFrame = BuildImmuneFrame(healthFill.transform.parent);

            BuildGoldCorner(canvasGo.transform);
            BuildToast(canvasGo.transform);
            BuildStatusLabel(canvasGo.transform);

            // Hold Tab for the scoreboard (D12). The publisher counts this player's own numbers into their Player
            // Properties; the panel reads everyone's. Both owner-only.
            ScoreboardPublisher.Create(gameObject, gameplayConfig);
            ScoreboardPanel.Create(transform, theme, GetComponent<PlayerInputRouter>());

            BuildHitFeedbackCanvas();
        }

        /// <summary>A SEPARATE overlay canvas for damage numbers and mark diamonds, below the HUD (-20 vs -10, so a number
        /// never draws over a slot or bar), with no GraphicRaycaster (a raycaster trap has bitten before, see ApplyTheme
        /// on HealthBarCanvas). Pools DamageNumberView.PoolSize labels, MarkIndicatorView.PoolSize diamonds and ONE for
        /// SelfMarkIndicatorView (the marked player's own diamond) up front, so popping one never Instantiates; all
        /// diamonds share the theme values so a retune moves every one.</summary>
        private void BuildHitFeedbackCanvas()
        {
            GameObject canvasGo = new GameObject("Hit Feedback Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -20;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster: nothing on this canvas is ever clickable.

            hitFeedbackCanvasRect = canvasGo.GetComponent<RectTransform>();

            var labels = new TextMeshProUGUI[DamageNumberView.PoolSize];
            for (int i = 0; i < labels.Length; i++)
            {
                TextMeshProUGUI label = AddLabel(canvasGo.transform, "", theme.damageNumberTextSize, FontStyles.Bold);
                label.enableWordWrapping = false;
                RectTransform rect = label.rectTransform;
                rect.sizeDelta = new Vector2(200f, 60f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                label.gameObject.SetActive(false);
                labels[i] = label;
            }

            DamageNumberView.Create(transform, theme, hitFeedbackCanvasRect, labels);

            var markDiamonds = new Image[MarkIndicatorView.PoolSize];
            for (int i = 0; i < markDiamonds.Length; i++)
                markDiamonds[i] = BuildMarkDiamond(canvasGo.transform, "Mark Diamond " + i);
            MarkIndicatorView.Create(transform, theme, hitFeedbackCanvasRect, markDiamonds);

            Image selfDiamond = BuildMarkDiamond(canvasGo.transform, "Self Mark Diamond");
            SelfMarkIndicatorView.Create(transform, playerHealth, theme, hitFeedbackCanvasRect, selfDiamond);
        }

        /// <summary>One mark diamond: the theme's bar sprite rotated 45 degrees, sized Mark Indicator Size, tinted Mark
        /// Colour (the colour a marked hit's damage number uses, so the two teach each other). Shared by the pooled
        /// enemy diamonds and the self one; built inactive and centre-anchored so LateUpdate repositions by anchoredPosition alone.</summary>
        private Image BuildMarkDiamond(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(theme.markIndicatorSize, theme.markIndicatorSize);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            Image image = go.AddComponent<Image>();
            image.sprite = theme.barSprite;
            image.color = theme.markColor;
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        /// <summary>The gold readout, bottom-right, directly above the "Loadout (P)" button (gold and shop are tied). It
        /// lives on THIS canvas because PlayerHud holds the GoldWallet and the change caches (UpdateGold). The two canvases
        /// line up because both use an identical (1, 0)-anchored, (1, 0)-pivoted root scaled by Hud Scale, so the gap is
        /// Gold Shop Gap at every screen size (LoadoutScreen.BuildToggleButtonCanvas, Shop Corner).</summary>
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
            // Two lines, the second smaller: the label writes its own <size> tag, so one TextMeshProUGUI serves both.
            goldRt.sizeDelta = new Vector2(theme.loadoutToggleButtonWidth, Overpower.Dominion.DominionScoreBarRules.GoldReadoutHeight(theme.bodyTextSize));
            goldText.color = theme.goldTextColor;
            goldText.alignment = TextAlignmentOptions.Right;
            goldText.enableWordWrapping = false;
        }

        /// <summary>The transient toast (ShowToast): a fixed-size label parented to the canvas, NOT Hud Panel's layout
        /// group (it must not nudge the bars when it shows), anchored top-centre clear of the Hud Panel and the F1
        /// panel's top-left. Built once, hidden until the first ShowToast. Callers toggle the ROOT (toastGo), not the label.</summary>
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

        private const float StatusHudGap = 8f; // Clear air between the label's letters and the bar under them.

        /// <summary>Your own STUNNED / SLOWED label with its thin shrinking bar (D18), a little below the screen middle,
        /// parented to the canvas like the toast so showing it never moves anything. UpdateStatusLabel is the only thing that shows it.</summary>
        private void BuildStatusLabel(Transform canvasParent)
        {
            GameObject go = new GameObject("Status Label", typeof(RectTransform));
            go.transform.SetParent(canvasParent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -theme.statusHudOffsetY);
            rt.sizeDelta = new Vector2(theme.statusHudBarSize.x, theme.bodyTextSize + 10f + StatusHudGap + theme.statusHudBarSize.y);

            TextMeshProUGUI text = AddLabel(go.transform, "", theme.bodyTextSize, FontStyles.Bold);
            RectTransform textRt = text.rectTransform;
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.anchoredPosition = Vector2.zero;
            textRt.sizeDelta = new Vector2(0f, theme.bodyTextSize + 10f);
            text.alignment = TextAlignmentOptions.Center;

            GameObject trackGo = new GameObject("Bar", typeof(RectTransform));
            trackGo.transform.SetParent(go.transform, false);
            RectTransform trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0.5f, 0f);
            trackRt.pivot = new Vector2(0.5f, 0f);
            trackRt.anchoredPosition = Vector2.zero;
            trackRt.sizeDelta = theme.statusHudBarSize;
            Image track = trackGo.AddComponent<Image>();
            track.color = theme.barTrackColor;
            track.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(trackGo.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            Image fill = fillGo.AddComponent<Image>();
            fill.sprite = theme.barSprite; // Sprite before Type - see BuildBar.
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.raycastTarget = false;

            go.SetActive(false);
            statusRoot = go;
            statusText = text;
            statusFill = fill;
        }

        /// <summary>Shows STUNNED or SLOWED (stun wins) with the bar shrinking over the time left, and hides both
        /// when neither runs. Text and colour are written only when the label changes, the fill only when it moves.</summary>
        private void UpdateStatusLabel()
        {
            if (statusEffects == null || statusRoot == null)
                return;

            statusEffects.TryGetStatusLabel(out StatusLabel label, out float remaining, out float total);

            if (label != lastStatusLabel)
            {
                lastStatusLabel = label;
                lastStatusFill = -1f;
                statusRoot.SetActive(label != StatusLabel.None);
                if (label != StatusLabel.None)
                {
                    bool stunned = label == StatusLabel.Stunned;
                    Color colour = stunned ? theme.statusStunnedColor : theme.statusSlowedColor;
                    statusText.text = stunned ? theme.statusStunnedText : theme.statusSlowedText;
                    statusText.color = colour;
                    statusFill.color = colour;
                }
            }

            if (label == StatusLabel.None)
                return;

            float amount = StatusLabelRule.Fill(remaining, total);
            if (Mathf.Abs(amount - lastStatusFill) > 0.002f)
            {
                lastStatusFill = amount;
                statusFill.fillAmount = amount;
            }
        }

        /// <summary>trackImage is handed back so a caller can add to the track (the overheat bar's warning tick).
        /// BuildArmorBar builds its own track and does not call this.</summary>
        private Image BuildBar(Transform parent, string name, float width, float height, Color fillColor, out Image trackImage)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            // panelLayout's child control is ON (BuildUi): this LayoutElement alone is the bar's size.
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
            // Sprite MUST be set before Type: a Filled Image with no sprite ignores fillAmount and always draws full width.
            fillImg.sprite = theme.barSprite;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;
            fillImg.raycastTarget = false;
            return fillImg;
        }

        /// <summary>The yellow immunity FRAME for ONE bar: four thin edge Images round the whole bar's rect (not the fill's
        /// 2px-inset rect), via BuildFrameStrip, plus an optional faint wash under them. One root, the LAST child of
        /// barRoot so it draws over the fills. Starts inactive; only UpdateHealthAndArmor shows it.</summary>
        private ImmuneFrame BuildImmuneFrame(Transform barRoot)
        {
            GameObject root = new GameObject("Immune Frame", typeof(RectTransform));
            root.transform.SetParent(barRoot, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // Built FIRST so it sits UNDER the frame edges: a non-zero Immune Bar Wash Alpha must not paint over their crisp edge.
            GameObject washGo = new GameObject("Wash", typeof(RectTransform));
            washGo.transform.SetParent(root.transform, false);
            RectTransform washRect = washGo.GetComponent<RectTransform>();
            washRect.anchorMin = Vector2.zero;
            washRect.anchorMax = Vector2.one;
            washRect.offsetMin = Vector2.zero;
            washRect.offsetMax = Vector2.zero;
            Image wash = washGo.AddComponent<Image>();
            wash.raycastTarget = false;

            float t = theme.immuneBarFrameThickness;
            Image frameTop = BuildFrameStrip(root.transform, "Frame Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, t));
            Image frameBottom = BuildFrameStrip(root.transform, "Frame Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, t));
            // Left/Right inset vertically by the thickness top and bottom, as BuildSlot's: otherwise every corner
            // carries two stacked strips.
            Image frameLeft = BuildFrameStrip(root.transform, "Frame Left", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(t, -2f * t));
            Image frameRight = BuildFrameStrip(root.transform, "Frame Right", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(t, -2f * t));

            root.SetActive(false);
            return new ImmuneFrame(root, new SlotFrame(frameTop, frameBottom, frameLeft, frameRight), wash);
        }

        /// <summary>A thin vertical mark on the overheat track showing where the warning threshold sits. A sibling of Fill
        /// added after it (draws on top), at a placeholder position; UpdateOverheat repositions it when the live threshold changes.</summary>
        private RectTransform BuildOverheatTick(Transform trackParent)
        {
            GameObject tick = new GameObject("Warning Tick", typeof(RectTransform));
            tick.transform.SetParent(trackParent, false);
            RectTransform tickRt = tick.GetComponent<RectTransform>();
            // X is a point-anchor (min == max) slid along the track by one number; Y spans the full track height.
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

        /// <summary>The Vent band: a rectangle over the stretch of the overheat track the fill drains through while the
        /// vent window is open, like BuildOverheatTick but spanning a RANGE. Parented after the tick so it draws on top
        /// (the band must read clearly at the moment that matters). Starts inactive; UpdateVentBand drives it.</summary>
        private Image BuildVentBand(Transform trackParent)
        {
            GameObject band = new GameObject("Vent Band", typeof(RectTransform));
            band.transform.SetParent(trackParent, false);
            RectTransform bandRt = band.GetComponent<RectTransform>();
            // X is a RANGE anchor (min != max, unlike the tick's point anchor): UpdateVentBand moves min/max to the live
            // band fractions. Y spans the full track height.
            bandRt.anchorMin = new Vector2(0f, 0f);
            bandRt.anchorMax = new Vector2(0f, 1f);
            bandRt.sizeDelta = Vector2.zero;
            bandRt.anchoredPosition = Vector2.zero;
            Image bandImg = band.AddComponent<Image>();
            bandImg.raycastTarget = false;
            band.SetActive(false);
            return bandImg;
        }

        /// <summary>A fixed-width track holding a "capacity extent" rectangle whose WIDTH is set live from script
        /// (UpdateHealthAndArmor), not by a layout group, which sidesteps layout-rebuild timing (a plain child's
        /// sizeDelta takes effect at once). Inside it, an ordinary fill shows current armor against its capacity. The
        /// width scales with capacity relative to max health (same hit-point scale), so an armor pool as big as max
        /// health fills the track and "+Absorb twice" reads as a visibly bigger segment, not a fuller small one.</summary>
        private Image BuildArmorBar(Transform parent, out RectTransform extentRect)
        {
            GameObject track = new GameObject("Armor Bar", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            LayoutElement le = track.AddComponent<LayoutElement>();
            le.preferredWidth = theme.barWidth;
            le.preferredHeight = theme.armorBarHeight;
            // panelLayout's child control is ON (BuildUi): this LayoutElement alone is the track's size.
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

        /// <summary>Hidden until IsSilenced; a crossed-out weapon mark built from plain rectangles (no weapon-silhouette
        /// sprite yet) so full overheat silence reads as a state with an end, not a wall (PlayerOverheat records this as
        /// the condition for the harsher "silences everything" rule). Parented to slotsRow, stretched over its rect with
        /// ignoreLayout on (BuildUi): the wash, icon and label draw over the slot boxes without shifting them.</summary>
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

            // A translucent wash across the row, behind the icon/label, so "you cannot use any of this" reads before the label does.
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
            // ON as panelLayout (BuildUi): the icon's and label's LayoutElements are the one place their size lives.
            // Force-expand OFF explicitly (slotsLayout in BuildUi says why): otherwise icon and text stretch to the row's height.
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

            // Not a layout-group child (parented to the icon): a fixed-size rect rotated in place, keeping its own sizeDelta.
            GameObject strike = new GameObject("Strike", typeof(RectTransform));
            strike.transform.SetParent(iconGo.transform, false);
            RectTransform strikeRt = strike.GetComponent<RectTransform>();
            strikeRt.anchorMin = strikeRt.anchorMax = strikeRt.pivot = new Vector2(0.5f, 0.5f);
            strikeRt.sizeDelta = new Vector2(theme.silencedStrikeWidth, theme.silencedStrikeHeight);
            strikeRt.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Image strikeImg = strike.AddComponent<Image>();
            strikeImg.color = theme.overheatSilencedColor;
            strikeImg.raycastTarget = false;

            // Body Text Size, not Small: the one HUD state that must read at a glance.
            TextMeshProUGUI text = AddLabel(content.transform, "WEAPON SILENCED", theme.bodyTextSize, FontStyles.Bold);
            LayoutElement textLe = text.gameObject.AddComponent<LayoutElement>();
            textLe.preferredHeight = 30f;
            text.color = theme.overheatSilencedColor;
            // Centred, with no pinned preferred WIDTH: a left-aligned label in a fixed box let the group centre the box
            // while the words sat at its left edge, so the banner read as off-centre. The group sizes the label to its words.
            text.alignment = TextAlignmentOptions.Center;

            row.SetActive(false);
            return row;
        }

        /// <summary>One edge strip of a border frame (SlotFrame says why four Images). Equal min/max anchors on one axis pin
        /// the strip to that edge with zero size, which sizeDelta on that axis supplies; the other axis spans the rect, so
        /// its sizeDelta stays 0.</summary>
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

        /// <summary>One weapon or ability box: a tinted background (ready/blocked/active), an icon that falls back to the
        /// display name, and (ability slots only) a charge pip row and a recharge cover sweep. isUltimate (exactly one slot)
        /// adds the charge meter: a fill plus a READY label.</summary>
        private SlotUi BuildSlot(Transform parent, string keyLabel, bool withCooldown, bool isUltimate)
        {
            var ui = new SlotUi();

            float slotHeight = withCooldown ? theme.slotIconBoxHeight + theme.slotCooldownAreaHeight : theme.slotIconBoxHeight;

            GameObject go = new GameObject("Slot " + keyLabel, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = theme.slotWidth;
            le.preferredHeight = slotHeight;
            // slotsLayout's child control is ON (BuildUi): this LayoutElement alone is the slot's size; everything inside
            // anchors relative to it.

            // The slot's border: four thin strips, each Slot Border Width thick, carrying the ready / blocked / active
            // tint UpdateAbilitySlots writes. NOT one Image filling the rect: a translucent child (Slot Fill) never hides
            // what is under it, so a full-rect Image read as a near-opaque box (the designer wants no dark opaque
            // background). raycastTarget off on all four like every HUD Graphic: a stray raycast target would block a
            // shot the day a GraphicRaycaster is added.
            Image frameTop = BuildFrameStrip(go.transform, "Frame Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, theme.slotBorderWidth));
            Image frameBottom = BuildFrameStrip(go.transform, "Frame Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, theme.slotBorderWidth));
            // Left/Right are inset vertically by Slot Border Width so they sit BETWEEN Frame Top/Bottom: otherwise every
            // corner has two stacked strips, invisible when thin but a visibly darker square once the width is raised.
            Image frameLeft = BuildFrameStrip(go.transform, "Frame Left", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(theme.slotBorderWidth, -2f * theme.slotBorderWidth));
            Image frameRight = BuildFrameStrip(go.transform, "Frame Right", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(theme.slotBorderWidth, -2f * theme.slotBorderWidth));
            ui.background = new SlotFrame(frameTop, frameBottom, frameLeft, frameRight);
            ui.background.color = theme.slotReadyColor;

            // The slot's only fill: a faint wash inset by the border width, so an icon or name has something to sit on.
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

            // The icon box is the top Slot Icon Box Height units, the only part the weapon slot has (no pips or sweep below).
            GameObject iconBox = new GameObject("Icon Box", typeof(RectTransform));
            iconBox.transform.SetParent(go.transform, false);
            RectTransform iconBoxRt = iconBox.GetComponent<RectTransform>();
            iconBoxRt.anchorMin = new Vector2(0f, 1f);
            iconBoxRt.anchorMax = new Vector2(1f, 1f);
            iconBoxRt.pivot = new Vector2(0.5f, 1f);
            iconBoxRt.anchoredPosition = Vector2.zero;
            iconBoxRt.sizeDelta = new Vector2(0f, theme.slotIconBoxHeight);

            // The icon, fallback name and (ultimate) READY label live here, under the key strip. Its own rect, not the
            // whole icon box, centres them in the SQUARE a player sees rather than in a box whose top strip is the key label.
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

            // Body Text Size: the icon box (Slot Width/Slot Icon Box Height) is sized so the longest short names fit at it.
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
                // The WHOLE icon box, not Content Box: a sweep that stopped short of the key strip would read as a drawing
                // bug. Built after Content Box (covers icon and name) and before the key strip (key stays readable).
                // Inset by Slot Border Width on all sides like Slot Fill: edge to edge it painted over the frame strips for
                // most of every cooldown, losing the frame's tint exactly while an ability recharged. The Ultimate Charge
                // Fill below stays full-square on purpose (D5).
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

                // Pip row and block-reason text sit BELOW the icon box, in the Slot Cooldown Area Height band; positions
                // derive from Slot Icon Box Height so they never drift from it.
                // 2 + Pip Row Height + 2 + Slot Reason Text Height has to stay inside Slot Cooldown Area Height, or the
                // reason line overhangs the bottom of the slot; each of those tooltips names this sum.
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
                // ON as panelLayout (BuildUi): SetPips's LayoutElement per pip is the one place its size lives. Force-expand
                // OFF explicitly (slotsLayout in BuildUi says why): otherwise every pip stretched to fill the row.
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

                // The Ultimate slot's charge meter: a translucent fill OVER the cooldown cover (built after it) plus a READY
                // label at full charge. Bound in UpdateUltimateMeter from UltimateCharge, never RechargeProgress (SlotUi).
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

                    ui.readyLabel = AddLabel(iconBox.transform, theme.ultimateReadyText, theme.smallTextSize, FontStyles.Bold);
                    RectTransform readyRt = ui.readyLabel.rectTransform;
                    readyRt.anchorMin = Vector2.zero;
                    readyRt.anchorMax = Vector2.one;
                    readyRt.offsetMin = Vector2.zero;
                    // The same inset Content Box uses, so READY lands in the middle of the visible square.
                    readyRt.offsetMax = new Vector2(0f, -theme.slotKeyRowHeight);
                    ui.readyLabel.color = theme.ultimateReadyTextColor;
                    ui.readyLabel.gameObject.SetActive(false); // UpdateUltimateMeter turns this on once IsFull.
                }
            }

            // Centred, not tucked in a corner: a full-width strip across the TOP of the icon box, so the icon/name stay
            // centred in the rest of the square. Built last so it draws over the sweep and meter. Body Text Size: the
            // longest label (SPACE) must fit.
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

        /// <summary>Same recipe as TestRangePanel.AddLabel, kept separate because the two panels have no other coupling.
        /// An instance method because it needs theme for the font, colour and outline.</summary>
        private TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            // Assign the font BEFORE touching fontSharedMaterial: .font switches it to that font asset's default
            // material, the template ApplyOutline clones from.
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

        /// <summary>Gives a text an outline via ONE Material shared by every HUD text. TMP_Text.outlineWidth/outlineColor
        /// each auto-clone fontSharedMaterial into a per-object instance the first time either is touched, so setting them
        /// per label would mean a material per label; one shared instance also batches into fewer draw calls. Built lazily
        /// from the first label's font (all share theme.font). The numbers live on UiTheme.ApplyHudTextStyle, shared with
        /// the loadout screen and the minimap.</summary>
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
