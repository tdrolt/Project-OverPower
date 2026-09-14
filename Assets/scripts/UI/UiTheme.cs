using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Every visual value the HUD, the loadout screen, the aim cone and the overhead bars share, in one asset so
    /// readability is tuned in one place. Presentation only - no gameplay number belongs here.
    /// </summary>
    [CreateAssetMenu(menuName = "Overpower/UI Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Canvas")]
        [Tooltip("The screen size the UI is designed at. Text and panels scale from this to the real screen.")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Tooltip("0 = scale with screen width, 1 = with height, 0.5 = a mix. 0.5 keeps ultrawide and 16:10 readable.")]
        [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;

        [Header("Text")]
        [Tooltip("Font for all UI text. Leave empty to use TextMeshPro's default font.")]
        public TMP_FontAsset font;
        [Tooltip("Size of slot key labels and bar labels.")] public float smallTextSize = 20f;
        [Tooltip("Size of ability and weapon names.")] public float bodyTextSize = 24f;
        [Tooltip("Size of screen titles.")] public float titleTextSize = 34f;
        [Tooltip("Main text colour.")] public Color textColor = Color.white;
        [Tooltip("Secondary text colour (descriptions, locked items).")] public Color mutedTextColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        [Tooltip("Dark outline around text so it stays readable over bright ground.")] public Color textOutlineColor = new Color(0f, 0f, 0f, 0.9f);
        [Tooltip("Outline thickness, 0 to 1. Around 0.2 reads well without looking bold.")] [Range(0f, 1f)] public float textOutlineWidth = 0.2f;

        [Header("Panels")]
        [Tooltip("Background behind HUD groups and the loadout screen.")] public Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        [Tooltip("Border / highlight for the equipped or selected item.")] public Color highlightColor = new Color(1f, 0.78f, 0.25f, 1f);
        [Tooltip("Colour of items you cannot pick yet.")] public Color lockedColor = new Color(1f, 1f, 1f, 0.25f);
        [Tooltip("Space between a panel's background edge and the bars/slots inside it, in canvas units, on every side.")]
        public float hudPanelPadding = 16f;
        [Tooltip("Distance from the bottom of the screen to the bottom of the HUD panel, in canvas units. Kept " +
                 "small - Task 5 shrank the chat prompt so the HUD no longer needs to clear a tall band.")]
        public float hudBottomOffset = 28f;

        [Header("Bars")]
        [Tooltip("Plain white sprite every filled bar uses. Without a sprite Unity ignores the fill amount and draws the bar full.")]
        public Sprite barSprite;
        [Tooltip("Width of every HUD bar (health, armor, overheat) and the slots row beneath them, in canvas units - " +
                 "the one width they all share so the bars and slots line up.")]
        public float barWidth = 590f;
        [Tooltip("Health fill.")] public Color healthColor = new Color(0.39f, 0.8f, 0.25f, 1f);
        [Tooltip("Shield fill.")] public Color shieldColor = new Color(0.25f, 0.6f, 1f, 1f);
        [Tooltip("Overheat fill below the warning threshold.")] public Color overheatColor = new Color(0.95f, 0.62f, 0.15f, 1f);
        [Tooltip("Overheat fill at or above the warning threshold.")] public Color overheatWarningColor = new Color(1f, 0.35f, 0.1f, 1f);
        [Tooltip("Overheat fill while silenced.")] public Color overheatSilencedColor = new Color(0.9f, 0.1f, 0.1f, 1f);
        [Tooltip("Empty part of every bar.")] public Color barTrackColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        [Tooltip("Height of the health bar, in canvas units.")]
        [Range(8f, 64f)] public float healthBarHeight = 28f;
        [Tooltip("Height of the shield/armor bar, in canvas units.")]
        [Range(8f, 64f)] public float armorBarHeight = 22f;
        [Tooltip("Height of the overheat bar, in canvas units - taller than health/armor on purpose: it is the " +
                 "one bar a player must read at a glance mid-fight.")]
        [Range(8f, 64f)] public float overheatBarHeight = 32f;
        [Tooltip("Colour of the thin vertical tick marking exactly where the warning threshold sits on the " +
                 "overheat track. Light so it stays visible on the dark track and on the amber/orange fill - " +
                 "a dark tick (the original colour) read fine on the old light track but disappeared once " +
                 "Bar Track Colour went dark for the fill/track contrast.")]
        public Color overheatTickColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("Width of the overheat warning-threshold tick mark, in canvas units.")]
        [Range(1f, 6f)] public float overheatTickWidth = 2f;
        [Tooltip("Pulse the overheat bar while it is at the warning level.")] public bool pulseAtWarning = true;
        [Tooltip("Pulses per second when Pulse At Warning is on. Keep it slow - fast reads as flicker.")] public float pulseSpeed = 1.2f;

        [Header("HUD slots")]
        [Tooltip("Width of one weapon/ability slot box, in canvas units - sized so the longest short names " +
                 "(Raybeam, Shotgun, Baseline) and the longest key label (SPACE) both fit at Body Text Size.")]
        public float slotWidth = 140f;
        [Tooltip("Height of a slot's icon/name area, in canvas units - the only part the weapon slot has; the " +
                 "three ability slots add Slot Cooldown Area Height below it.")]
        public float slotIconBoxHeight = 104f;
        [Tooltip("Extra height below the icon box, in canvas units, reserved for the charge pips and the " +
                 "block-reason text on the three ability slots. The weapon slot has neither and never adds this.")]
        public float slotCooldownAreaHeight = 58f;
        [Tooltip("Slot background when the slot can be used right now.")]
        public Color slotReadyColor = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("Slot background when the slot is blocked - dead, stunned, silenced, recharging, or an " +
                 "empty/not-ready slot.")]
        public Color slotBlockedColor = new Color(0.30f, 0.30f, 0.30f, 0.85f);
        [Tooltip("Slot background while the ability is active - a channel, a dash mid-flight, sprint held.")]
        public Color slotActiveGlowColor = new Color(1f, 0.85f, 0.25f, 1f);
        [Tooltip("Charge pip colour when that charge is available.")]
        public Color pipAvailableColor = Color.white;
        [Tooltip("Charge pip colour when that charge is spent.")]
        public Color pipSpentColor = new Color(1f, 1f, 1f, 0.15f);
        [Tooltip("Colour of the dark cover that wipes off an ability icon as it recharges - fully covered the " +
                 "instant a charge is spent, gone the instant it returns.")]
        public Color cooldownCoverColor = new Color(0f, 0f, 0f, 0.65f);
        [Tooltip("Fill colour of the Ultimate slot's own charge meter - a translucent wash drawn over the icon, " +
                 "from empty to full, independent of the slot's ordinary recharge cover.")]
        public Color ultimateChargeColor = new Color(1f, 0.85f, 0.25f, 0.45f);
        [Tooltip("Colour of the READY text shown over the Ultimate slot once its charge is full.")]
        public Color ultimateReadyTextColor = new Color(1f, 0.95f, 0.6f);
        [Tooltip("Colour of the weapon-icon placeholder in the silenced banner - a plain rectangle, since the " +
                 "project has no weapon-silhouette sprite yet.")]
        public Color silencedIconColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        [Tooltip("Opacity of the translucent wash drawn behind the WEAPON SILENCED banner, on top of the slot " +
                 "row - 0 is invisible, 1 is a solid block.")]
        [Range(0f, 1f)] public float silencedWashAlpha = 0.35f;
        [Tooltip("Width and height of the square weapon-icon placeholder in the silenced banner, in canvas units.")]
        public float silencedIconSize = 28f;
        [Tooltip("Width of the diagonal strike line drawn across the silenced banner's weapon icon, in canvas units.")]
        public float silencedStrikeWidth = 38f;
        [Tooltip("Height (thickness) of the diagonal strike line, in canvas units.")]
        public float silencedStrikeHeight = 4f;

        [Header("Aim cone")]
        [Tooltip("Colour of the two lines showing where your shots can go.")] public Color coneLineColor = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("Colour of the shotgun's inner fan lines.")] public Color coneFanLineColor = new Color(1f, 1f, 1f, 0.25f);
        [Tooltip("Width of the cone lines in metres.")] public float coneLineWidth = 0.04f;
        [Tooltip("Draw an arc joining the two cone lines at bullet range, so you can see how far your shots actually go, not just which way.")]
        public bool showRangeArc = true;
        [Tooltip("Colour of the range arc.")] public Color coneArcColor = new Color(1f, 1f, 1f, 0.35f);
        [Tooltip("How many straight segments make up the range arc. More reads as a smoother curve; 24 is already smooth enough to tell from a real arc.")]
        [Range(4, 64)] public int coneArcSegments = 24;
        [Tooltip("Shared unlit material every aim-cone line and arc draw with. Keep its own colour white - each line tints itself through its own LineRenderer start/end colour above, which is what lets one material serve every line on this list.")]
        public Material coneLineMaterial;
    }
}
