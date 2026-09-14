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

        [Header("Bars")]
        [Tooltip("Plain white sprite every filled bar uses. Without a sprite Unity ignores the fill amount and draws the bar full.")]
        public Sprite barSprite;
        [Tooltip("Health fill.")] public Color healthColor = new Color(0.39f, 0.8f, 0.25f, 1f);
        [Tooltip("Shield fill.")] public Color shieldColor = new Color(0.25f, 0.6f, 1f, 1f);
        [Tooltip("Overheat fill below the warning threshold.")] public Color overheatColor = new Color(0.95f, 0.62f, 0.15f, 1f);
        [Tooltip("Overheat fill at or above the warning threshold.")] public Color overheatWarningColor = new Color(1f, 0.35f, 0.1f, 1f);
        [Tooltip("Overheat fill while silenced.")] public Color overheatSilencedColor = new Color(0.9f, 0.1f, 0.1f, 1f);
        [Tooltip("Empty part of every bar.")] public Color barTrackColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        [Tooltip("Height of the overheat bar, in canvas units - taller than health/armor on purpose: it is the " +
                 "one bar a player must read at a glance mid-fight.")]
        [Range(8f, 32f)] public float overheatBarHeight = 18f;
        [Tooltip("Colour of the thin vertical tick marking exactly where the warning threshold sits on the " +
                 "overheat track. Dark so it stays visible against the track colour and every fill colour it " +
                 "might be drawn over.")]
        public Color overheatTickColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        [Tooltip("Width of the overheat warning-threshold tick mark, in canvas units.")]
        [Range(1f, 6f)] public float overheatTickWidth = 2f;
        [Tooltip("Pulse the overheat bar while it is at the warning level.")] public bool pulseAtWarning = true;
        [Tooltip("Pulses per second when Pulse At Warning is on. Keep it slow - fast reads as flicker.")] public float pulseSpeed = 1.2f;
        [Tooltip("How far the warning pulse dims the overheat colour at its darkest point - 0.35 means it dips " +
                 "to 65% brightness and back.")]
        [Range(0f, 0.9f)] public float pulseDepth = 0.35f;

        [Header("HUD slots")]
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

        [Header("Aim cone")]
        [Tooltip("Colour of the two lines showing where your shots can go.")] public Color coneLineColor = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("Colour of the shotgun's inner fan lines.")] public Color coneFanLineColor = new Color(1f, 1f, 1f, 0.25f);
        [Tooltip("Width of the cone lines in metres.")] public float coneLineWidth = 0.04f;
    }
}
