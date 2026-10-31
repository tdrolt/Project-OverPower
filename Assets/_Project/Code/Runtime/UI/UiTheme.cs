using System.Collections.Generic;
using Overpower.Arena;
using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Every visual value the HUD, the loadout screen, the aim cone and the overhead bars share, in one asset so
    /// readability is tuned in one place; also every shot's trail/core tint (ShotTeamVisuals), a laser's wind-up
    /// warning line and fired beam (Hitscan), and the reference resolution/match value that ThemedCanvasScaler
    /// applies to scene-built canvases that are not PlayerHud's (chat). Presentation only - no gameplay number here.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/UI Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Canvas")]
        [Tooltip("The screen size the UI is designed at. Text and panels scale from this to the real screen.")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Tooltip("0 = scale with screen width, 1 = with height, 0.5 = a mix. 0.5 keeps ultrawide and 16:10 readable.")]
        [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;
        [Tooltip("How big the whole HUD is drawn, as a fraction of the sizes below: 1 is full size, 0.8 is 20% " +
                 "smaller. Applied ONCE, as a scale on the HUD panel and on the gold/shop block in the bottom-right " +
                 "corner - every other size on this asset stays in its own units, so a designer tunes Bar Width or " +
                 "Slot Width normally and this one number makes the whole group bigger or smaller. It does NOT scale " +
                 "the minimap, the toast, the match panels, the chat or the F1 test range panel, which each sit on " +
                 "their own.")]
        [Range(0.5f, 1.5f)] public float hudScale = 0.8f;

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
        [Tooltip("How much thicker every glyph is drawn, 0 to 1 (TextMeshPro's Face Dilate). This is the weight " +
                 "control for the font the project already uses - there is no second, bolder font asset - and it " +
                 "was raised in HUD step 2 because the dark panel that used to sit behind the HUD is gone. Around " +
                 "0.08 reads as a firmer version of the same letters; past ~0.2 they start to close up.")]
        [Range(0f, 0.5f)] public float hudTextFaceDilate = 0.08f;
        [Tooltip("Colour of the soft shadow dropped under every UI text, so a word keeps its shape over the " +
                 "arena's bright sand without a panel behind it. Alpha 0 turns the shadow off.")]
        public Color hudTextShadowColor = new Color(0f, 0f, 0f, 0.75f);
        [Tooltip("How far the shadow is offset from the text, in font units (x right, y up - so a negative y " +
                 "drops it below the letters, which is what reads as a shadow rather than a halo).")]
        public Vector2 hudTextShadowOffset = new Vector2(0.5f, -0.5f);
        [Tooltip("How blurred the shadow's edge is, 0 to 1. Soft enough not to read as a second, offset copy of " +
                 "the text; hard enough to still darken the ground under it.")]
        [Range(0f, 1f)] public float hudTextShadowSoftness = 0.25f;
        [Tooltip("How far the shadow spreads outward from the glyph before it fades, 0 to 1. Together with " +
                 "Softness this is what makes the shadow a pool under the word rather than an outline of it.")]
        [Range(0f, 1f)] public float hudTextShadowDilate = 0.1f;

        [Header("Panels")]
        [Tooltip("Border / highlight for the equipped or selected item.")] public Color highlightColor = new Color(1f, 0.78f, 0.25f, 1f);
        [Tooltip("Colour of items you cannot pick yet - a dark, mostly-opaque fill with muted text on top (Muted Text Colour), not a near-transparent wash: at low alpha over a translucent panel this read as barely-there rather than clearly locked.")]
        public Color lockedColor = new Color(0.09f, 0.09f, 0.10f, 0.92f);
        [Tooltip("Space between a panel's background edge and the bars/slots inside it, in canvas units, on every side.")]
        public float hudPanelPadding = 16f;
        [Tooltip("Distance from the bottom of the screen to the bottom of the HUD panel, in canvas units. Kept small: " +
                 "the chat prompt is small, so the HUD does not need to clear a tall band.")]
        public float hudBottomOffset = 28f;

        [Header("Bars")]
        [Tooltip("Plain white sprite every filled bar uses. Without a sprite Unity ignores the fill amount and draws the bar full.")]
        public Sprite barSprite;
        [Tooltip("Width of every HUD bar (health, armor, overheat), in canvas units. The slots row beneath them does " +
                 "not read this directly - its own preferred width is computed from Slot Width and Hud Slot Spacing " +
                 "below (4 slots: the weapon plus the three ability slots - see PlayerHud.BuildUi). Keep 4 x Slot " +
                 "Width + 3 x Hud Slot Spacing equal to this number, or the slot row will no longer line up under the " +
                 "bars above it.")]
        public float barWidth = 590f;
        [Tooltip("Health fill.")] public Color healthColor = new Color(0.39f, 0.8f, 0.25f, 1f);
        [Tooltip("Shield fill.")] public Color shieldColor = new Color(0.25f, 0.6f, 1f, 1f);
        [Tooltip("FRAME colour drawn around a bar - both HUD bars (health, armor) and the shared bar over your " +
                 "head, on every screen - while the Invulnerability shield's immunity is running (the seconds " +
                 "after it triggers, not while it is only armed). Carry-over C, the controller's decision (evidence: " +
                 "captures/immune-overlay-alpha-montage.png): a translucent WASH over the whole bar could never read " +
                 "yellow over the blue shield fill - measured across alpha 0.45-0.8, the overhead bar's shield part " +
                 "stayed a flat grey-beige/khaki, and on the HUD armour bar the FILLED part read PALER than the " +
                 "EMPTY part (backwards - fuller looked LESS full). Tudor's words: \"an overlay so it doesn't mess " +
                 "with the shield\" - a frame round the bar's own outside edge never sits over the blue/green fills " +
                 "at all, so it can run at full alpha (1) and the fills underneath are simply never touched. See " +
                 "Immune Bar Wash Alpha below for an optional faint reinforcement UNDER the frame, off by default.")]
        public Color immuneBarColor = new Color(1f, 0.86f, 0.1f, 1f);
        [Tooltip("Thickness of the yellow frame drawn around a HUD bar (health, armour) while Immune Bar Colour's " +
                 "look is showing, in HUD canvas units (same unit as Slot Border Width). Four thin edge Images " +
                 "built in code round the bar's own rect (PlayerHud.BuildImmuneFrame - reuses BuildFrameStrip, the " +
                 "same recipe a slot's own border already uses) and drawn OVER the fills, not a recolour of either.")]
        public float immuneBarFrameThickness = 4f;
        [Tooltip("Thickness of the yellow frame drawn around the bar over a player's head while the look is " +
                 "showing, in THAT bar's own local RectTransform units (its \"Bar\" rect is 15 x 3 of these, scaled " +
                 "x0.05 to world metres by HealthBarCanvas's own transform - Multiplayer Player.prefab). Chosen by " +
                 "capture, not calculation (Rule 6): 0.6 reads clearly yellow at ordinary combat range in the game " +
                 "camera without swallowing the bar's own fills underneath - see PlayerHealth.EnsureImmuneFrame and " +
                 "carry-over C's Play Mode captures for the comparison this was picked from.")]
        public float immuneOverheadFrameThickness = 0.6f;
        [Range(0f, 1f)]
        [Tooltip("Alpha of an optional faint WASH covering the whole bar in Immune Bar Colour, UNDER the frame, " +
                 "while the look is showing. 0.15 by default: a faint wash that reinforces the frame; 0 = frame " +
                 "only. Keep any non-zero value low: a wash strong enough to read on its own recreates the exact flattening effect that " +
                 "moved this look from a wash to a frame in the first place (see Immune Bar Colour's own tooltip) - " +
                 "it is offered only as a subtle reinforcement, never the primary signal.")]
        public float immuneBarWashAlpha = 0.15f;
        [Tooltip("Overheat fill below the warning threshold.")] public Color overheatColor = new Color(0.95f, 0.62f, 0.15f, 1f);
        [Tooltip("Overheat fill at or above the warning threshold.")] public Color overheatWarningColor = new Color(1f, 0.35f, 0.1f, 1f);
        [Tooltip("Overheat fill while silenced.")] public Color overheatSilencedColor = new Color(0.9f, 0.1f, 0.1f, 1f);
        [Tooltip("Empty part of every bar.")] public Color barTrackColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        [Tooltip("Height of the health bar, in canvas units.")]
        [Range(8f, 64f)] public float healthBarHeight = 28f;
        [Tooltip("Height of the shield/armor bar, in canvas units.")]
        [Range(8f, 64f)] public float armorBarHeight = 22f;
        [Tooltip("How many hit points lie between two thin divider lines on every health and armour bar, on your own HUD and on " +
                 "the bar over every head. At 25, a 100 health bar is cut into four steps and a 50 armour bar into two. 0 draws no lines.")]
        [Min(0f)] public float barSegmentSize = 25f;
        [Tooltip("Colour of the thin divider lines on every health and armour bar. Dark, so each line reads as a small gap in the fill.")]
        public Color barSegmentLineColor = new Color(0.04f, 0.05f, 0.07f, 0.9f);
        [Tooltip("Width of one divider line on your own HUD's health and armour bars, in canvas units.")]
        [Range(0.5f, 6f)] public float hudBarSegmentLineWidth = 2f;
        [Tooltip("Size of the \"current/max\" number written in the middle of your own HUD's health and armour bars (for example " +
                 "35/125), in canvas units. Keep it a little under the armour bar's height so it fits inside the bar.")]
        public float hudBarNumberSize = 19f;
        [Tooltip("Colour of the \"current/max\" number on your own HUD's health and armour bars. Its dark outline and shadow come " +
                 "from the HUD's text style, so it reads over the fill and the empty part alike.")]
        public Color hudBarNumberColor = Color.white;
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

        [Header("Vent (the overheat sweetspot)")]
        [Tooltip("Colour of the Vent band on the overheat track before its window has opened - dim, so it reads " +
                 "as 'coming up' rather than 'press now'. Only shown while silenced and this silence's Vent " +
                 "attempt is still unspent.")]
        public Color ventBandDimColor = new Color(1f, 1f, 1f, 0.28f);
        [Tooltip("Colour of the Vent band while its window is open - bright, the one moment R actually does " +
                 "something. Distinct from every other bar colour so it reads as 'act now' at a glance.")]
        public Color ventBandOpenColor = new Color(1f, 0.95f, 0.35f, 0.95f);
        [Tooltip("Colour the Vent band flashes the instant R lands inside the window, before it disappears - " +
                 "the bar itself visibly dropping by half is the main feedback, this is just the extra beat " +
                 "that says 'that was you'.")]
        public Color ventBandHitColor = new Color(0.4f, 1f, 0.55f, 1f);
        [Tooltip("How long the hit flash (Vent Band Hit Colour) stays up before the band disappears, in " +
                 "seconds. Short on purpose - the bar dropping by half is the read, not the flash.")]
        public float ventHitFlashSeconds = 0.25f;
        [Tooltip("Colour the Vent band turns on a miss (pressed early, pressed late, or the window passed with " +
                 "nothing pressed) - stays this colour for the rest of the silence, so a player who pressed early " +
                 "knows at once rather than wondering if it is still coming. Opaque dark grey on purpose: the earlier " +
                 "translucent mid-grey sat over the red silenced fill and read pinkish, almost the same as the dim " +
                 "'not yet' look (white at 0.28 alpha over red also reads pink) - a player who pressed early could " +
                 "not tell they had missed. Opaque so it reads as 'closed' regardless of what is under it.")]
        public Color ventBandMissColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        [Header("HUD slots")]
        [Tooltip("Width of one weapon/ability slot box, in canvas units - sized so the longest short names " +
                 "(Raybeam, Shotgun, Baseline) and the longest key label (SPACE) both fit at Body Text Size. " +
                 "The slots row's own width is 4 of these plus 3 gaps of Hud Slot Spacing below - see Bar " +
                 "Width's tooltip for why that total is kept equal to it.")]
        public float slotWidth = 140f;
        [Tooltip("Horizontal gap between adjacent HUD slots (the weapon slot and the three ability slots), in canvas " +
                 "units. It is the one home for that gap, read by both the layout group's spacing and the row's own " +
                 "preferred-width calculation, so the two can never disagree.")]
        public float hudSlotSpacing = 10f;
        [Tooltip("Height of a slot's icon/name area, in canvas units - the only part the weapon slot has; the " +
                 "three ability slots add Slot Cooldown Area Height below it.")]
        public float slotIconBoxHeight = 104f;
        [Tooltip("Extra height below the icon box, in canvas units, reserved for the charge pips and the " +
                 "block-reason text on the three ability slots. The weapon slot has neither and never adds this.")]
        public float slotCooldownAreaHeight = 58f;
        [Tooltip("Height of the key strip (LMB / RMB / SPACE / SHIFT) across the top of a slot, in canvas units. The " +
                 "icon and the ability name centre themselves in whatever is left of the icon box below it, so both " +
                 "read as centred in the square at once - which one shared rect could never do.")]
        public float slotKeyRowHeight = 30f;
        [Tooltip("Height of the block-reason line (\"recharging\", \"stunned\") under a slot's charge pips, in " +
                 "canvas units. Together with the pip row it has to fit inside Slot Cooldown Area Height above.")]
        public float slotReasonTextHeight = 26f;
        [Tooltip("Thickness of the border drawn around a weapon/ability slot, in canvas units. The border is what " +
                 "carries the ready / blocked / active colour now: There is no dark box behind the abilities, so the " +
                 "slot is a thin frame over a faint wash instead of a filled square. Raise it if the state colour is " +
                 "hard to see at a glance.")]
        public float slotBorderWidth = 3f;
        [Tooltip("The faint wash inside a slot's border (HUD step 2) - just enough to keep an icon or an ability " +
                 "name readable over the arena's bright sand, low enough to see the ground through. Raise the " +
                 "alpha if names are hard to read; drop it to 0 for a frame with nothing inside it at all.")]
        public Color slotFillColor = new Color(0f, 0f, 0f, 0.22f);
        [Tooltip("Slot BORDER colour when the slot can be used right now (HUD step 2 - it used to be the whole " +
                 "square's fill). Dark and near-opaque: a thin dark line is the most legible frame on the arena's " +
                 "bright sand.")]
        public Color slotReadyColor = new Color(0.05f, 0.05f, 0.07f, 0.9f);
        [Tooltip("Slot BORDER colour when the slot is blocked - dead, stunned, silenced, recharging, or an " +
                 "empty/not-ready slot. Brighter than it was as a full-square fill (HUD step 2): three canvas " +
                 "units of mid-grey has to work harder than a hundred and forty did.")]
        public Color slotBlockedColor = new Color(0.45f, 0.45f, 0.45f, 0.95f);

        [Tooltip("Colour of a teleport portal's disc, rim, diamond and stem while its pair cannot be used (the owner's " +
                 "charge is recharging). Only the colour is used; each part keeps its own opacity.")]
        public Color portalCooldownColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        [Tooltip("Slot BORDER colour while the ability is active - a channel, a dash mid-flight, sprint held. " +
                 "Unchanged by HUD step 2: at full alpha it already reads as a lit frame.")]
        public Color slotActiveGlowColor = new Color(1f, 0.85f, 0.25f, 1f);
        [Tooltip("Charge pip colour when that charge is available.")]
        public Color pipAvailableColor = Color.white;
        [Tooltip("Charge pip colour when that charge is spent.")]
        public Color pipSpentColor = new Color(1f, 1f, 1f, 0.15f);
        [Tooltip("Charge pip colour for a charge that is available while the ability is locked after running out " +
                 "of charges (Dash: locked until enough have refilled). Red, so the player sees it cannot be used yet.")]
        public Color pipLockedColor = new Color(0.9f, 0.15f, 0.15f, 1f);
        [Tooltip("Charge pip colour for a spent charge while the ability is locked - the same red, dimmer, so the " +
                 "count still reads.")]
        public Color pipLockedSpentColor = new Color(0.9f, 0.15f, 0.15f, 0.35f);
        [Tooltip("Width and height of one charge pip, in canvas units. Bigger pips read better: at 14 a pip is a " +
                 "tenth of a 140-unit slot, where 8 was a seventeenth. The pips sit in a row Pip Row Height tall, " +
                 "which has to be at least this plus twice Pip Outline Width.")]
        public float pipSize = 14f;
        [Tooltip("Gap between two charge pips, in canvas units. Wide enough to count them at a glance without " +
                 "the row running past the slot's edge.")]
        public float pipSpacing = 4f;
        [Tooltip("Height of the charge pip row under a slot's icon, in canvas units. Must be at least Pip Size " +
                 "plus twice Pip Outline Width, or the pips are squashed. Together with Slot Reason Text Height " +
                 "it has to fit inside Slot Cooldown Area Height.")]
        public float pipRowHeight = 20f;
        [Tooltip("Thickness of the dark rim around each charge pip, in canvas units - the same trick the " +
                 "minimap's markers use. Without the HUD's old dark panel behind it, a plain white pip on the " +
                 "arena's bright sand has nothing to read against.")]
        public float pipOutlineWidth = 2f;
        [Tooltip("Colour of that rim. Dark and near-opaque, so it works under both the available and the spent " +
                 "pip colour above.")]
        public Color pipOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        [Tooltip("Colour of the dark cover that wipes off an ability icon as it recharges - fully covered the " +
                 "instant a charge is spent, gone the instant it returns.")]
        public Color cooldownCoverColor = new Color(0f, 0f, 0f, 0.65f);
        [Tooltip("Fill colour of the Ultimate slot's own charge meter - a translucent wash drawn over the icon, " +
                 "from empty to full, independent of the slot's ordinary recharge cover.")]
        public Color ultimateChargeColor = new Color(1f, 0.85f, 0.25f, 0.45f);
        [Tooltip("Colour of the READY text shown over the Ultimate slot once its charge is full.")]
        public Color ultimateReadyTextColor = new Color(1f, 0.95f, 0.6f);
        [Tooltip("Text shown over the Ultimate slot while the meter is empty but the key still does something - " +
                 "the Heal Zone can be thrown to the cursor once. Same colour as the READY text.")]
        public string ultimateRecastText = "THROW";
        [Tooltip("Text shown over the Ultimate slot once its meter is full.")]
        public string ultimateReadyText = "READY";
        [Tooltip("Text shown over the Ultimate slot while no ultimate is equipped (the meter keeps filling underneath).")]
        public string ultimateNoneText = "No ultimate";
        [Header("Status labels (STUNNED / SLOWED)")]
        [Tooltip("Text shown over a stunned player's head, and on your own HUD when it is you. Stun is the stronger effect: it always wins over slowed.")]
        public string statusStunnedText = "STUNNED";
        [Tooltip("Text shown over a slowed player's head, and on your own HUD when it is you (only while not stunned).")]
        public string statusSlowedText = "SLOWED";
        [Tooltip("The electric blue of the Stun Gun and the Sonic Pulse: the stun bolt, the arcs on a stunned player and the pulse bands, " +
                 "and also the STUNNED label with the thin bar shrinking under it. A deeper blue than the cyan team, " +
                 "and never the gold of the Shield, so a stun is told apart from both.")]
        public Color statusStunnedColor = new Color(0.2f, 0.45f, 1f, 1f);
        [Tooltip("Colour of the SLOWED label and the thin bar shrinking under it - a sandy tan, the colour of being stuck in the mud, that no team, shot or ability uses, so a slow is never taken for the stun's electric blue.")]
        public Color statusSlowedColor = new Color(0.78f, 0.62f, 0.4f, 1f);
        [Tooltip("Your own HUD label: how far below the middle of the screen it sits, in canvas units.")]
        public float statusHudOffsetY = 170f;
        [Tooltip("Your own HUD bar: width and height, in canvas units. Sits directly under the label.")]
        public Vector2 statusHudBarSize = new Vector2(220f, 8f);
        [Tooltip("Size of the STUNNED / SLOWED label over a player's plate, in overhead-canvas units (the name on the plate is Nameplate Name Size).")]
        public float nameplateStatusTextSize = 3.2f;
        [Tooltip("Gap between the top of a player's plate and the STUNNED / SLOWED countdown bar above it, in overhead-canvas units. " +
                 "The shield's \"Wasted\" text keeps the same gap above the label.")]
        public float nameplateStatusGap = 0.6f;
        [Tooltip("The STUNNED / SLOWED countdown bar above a player's plate: width and height, in overhead-canvas units.")]
        public Vector2 nameplateStatusBarSize = new Vector2(16f, 0.6f);
        [Tooltip("Gap between that countdown bar and the STUNNED / SLOWED label sitting above it, in overhead-canvas units.")]
        public float nameplateStatusLabelGap = 0.3f;

        [Header("Nameplate (name, health and armour over every player)")]
        [Tooltip("Font of the name on every player's plate, their STUNNED / SLOWED label and the shield's \"Wasted\" text. " +
                 "Leave empty to use the UI font above.")]
        public TMP_FontAsset nameplateFont;
        [Tooltip("Size of the player's name on their plate, in overhead-canvas units (the units of the plate's bar, Nameplate Bar Size).")]
        public float nameplateNameSize = 3.6f;
        [Tooltip("Width and height of the health-and-armour bar on every plate, in overhead-canvas units. Every bar is this wide " +
                 "whatever the player's name, so health compares at a glance.")]
        public Vector2 nameplateBarSize = new Vector2(16f, 2.4f);
        [Tooltip("Empty space between the plate's edge and the name and bar inside it, in overhead-canvas units: left and right, then top and bottom.")]
        public Vector2 nameplatePadding = new Vector2(0.9f, 0.7f);
        [Tooltip("Gap between the name and the bar under it, in overhead-canvas units.")]
        public float nameplateRowGap = 0.5f;
        [Tooltip("The widest a plate grows for a long name, in overhead-canvas units. A longer name is cut short with three dots; " +
                 "the plate is never narrower than its bar.")]
        public float nameplateMaxWidth = 30f;
        [Tooltip("Height of the plate's bottom edge above the player's overhead point, in overhead-canvas units (negative is lower). " +
                 "When a player dies their bar goes and the plate shrinks to the name, which stays where it was.")]
        public float nameplateBottomY = -1.6f;
        [Tooltip("Thickness of the team-coloured edge round every plate, in overhead-canvas units. 0 turns the edge off.")]
        public float nameplateEdgeThickness = 0.35f;
        [Tooltip("Colour of the plate behind the name and bar: dark and mostly opaque, so the name and bar read over every floor colour.")]
        public Color nameplateBackingColor = new Color(0.04f, 0.05f, 0.07f, 0.78f);
        [Tooltip("Colour of your own name on your plate.")]
        public Color nameplateSelfNameColor = Color.white;
        [Tooltip("Colour of a teammate's name on their plate.")]
        public Color nameplateTeammateNameColor = Color.green;
        [Tooltip("Colour of an enemy's name on their plate.")]
        public Color nameplateEnemyNameColor = Color.red;
        [Tooltip("Colour of a name while the teams are still arriving, for a moment after someone joins: grey rather than a guess.")]
        public Color nameplateUnknownNameColor = Color.grey;
        [Tooltip("Width of one divider line on the health-and-armour bar over every head, in overhead-canvas units. The lines fall " +
                 "every Bar Segment Size hit points: health's under the shield, armour's on the shield where it is filled.")]
        public float nameplateSegmentLineWidth = 0.15f;
        [Tooltip("Size of the armour-upgrade mark on the plate (the outer Absorb square, or the room a Recharge \"v\" stack takes), in " +
                 "overhead-canvas units. The mark sits in the middle of the shield bar, so keep it no taller than the bar (Nameplate Bar Size).")]
        public float nameplateArmourMarkSize = 2f;
        [Tooltip("How big the second Absorb square is next to the first: it sits inside it, centred (0.5 = half as wide).")]
        [Range(0.1f, 0.95f)] public float nameplateArmourNestedScale = 0.5f;
        [Tooltip("Gap between the shield bar and the HP bar on your own plate, in overhead-canvas units. Your own plate shows no name " +
                 "(the scoreboard has it): shield on top, HP below, like the bars under the ability slots.")]
        public float nameplateOwnRowGap = 0.3f;
        [Tooltip("Thickness of an Absorb square's outline on the plate, in overhead-canvas units.")]
        public float nameplateArmourSquareLine = 0.25f;
        [Tooltip("Width of one Recharge \"v\" on the plate, in overhead-canvas units; it is half as tall as it is wide. Keep it " +
                 "inside an Absorb square (Armour Mark Size less two outlines), since a \"v\" sits in a square when both are bought.")]
        public float nameplateArmourChevronWidth = 1.8f;
        [Tooltip("Thickness of a Recharge \"v\" stroke on the plate, in overhead-canvas units.")]
        public float nameplateArmourChevronLine = 0.3f;
        [Tooltip("Height between the two stacked \"v\"s of the second Recharge level, in overhead-canvas units.")]
        public float nameplateArmourChevronStep = 0.65f;
        [Tooltip("Colour of the armour-upgrade marks on every plate (Absorb squares and Recharge \"v\"s). They sit on the shield bar " +
                 "and show only while the shield holds any armour: a light yellow reads on the shield's blue and stays apart from Invulnerability's gold.")]
        public Color nameplateArmourMarkColor = new Color(1f, 1f, 0.45f, 1f);
        [Header("Shield wasted text (Invulnerability ultimate)")]
        [Tooltip("Text that pops over a player's head when their armed shield window ran out and nobody hit them.")]
        public string shieldWastedText = "Wasted";
        [Tooltip("Colour of the shield's \"Wasted\" text.")]
        public Color shieldWastedColor = new Color(0.75f, 0.78f, 0.85f, 1f);
        [Tooltip("How long the shield's \"Wasted\" text stays over the player's head, in seconds.")]
        public float shieldWastedSeconds = 1.2f;
        [Tooltip("Size of the \"Wasted\" text, in overhead-canvas units (the name on the plate is Nameplate Name Size). " +
                 "It sits above the STUNNED / SLOWED label's place, Nameplate Status Gap higher.")]
        public float shieldWastedTextSize = 3.4f;
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

        [Header("Loadout screen")]
        [Tooltip("Colour of the full-screen wash behind the loadout panel - dims the game world so the panel reads as a modal screen.")]
        public Color loadoutDimColor = new Color(0f, 0f, 0f, 0.75f);
        [Tooltip("Background behind the loadout panel itself - its OWN colour, separate from Panel Colour (the HUD's), because a HUD panel sits over solid HUD chrome while this one sits over the game world: at Panel Colour's own opacity the world, and a player's overhead health/shield bar, showed straight through the middle of it. Kept nearly opaque (~0.96) so nothing behind the modal panel is visible through it.")]
        public Color loadoutPanelColor = new Color(0.05f, 0.05f, 0.07f, 0.96f);
        [Tooltip("Space between the loadout panel's background edge and its title/columns, in canvas units, on every side.")]
        public float loadoutPanelPadding = 20f;
        [Tooltip("Extra vertical gap between the armour paths and the Mobility, Attachment and Ultimate columns below them on the Abilities & Armor page, on top of the normal item spacing, in canvas units.")]
        public float loadoutSectionGap = 16f;
        [Tooltip("Gap from the top of the screen to the top of the loadout panel, in canvas units. The panel is anchored to the TOP of the screen rather than dead-centre so a tall tree/armor column never grows down into the HUD, which sits at the bottom.")]
        public float loadoutPanelTopMargin = 24f;
        [Tooltip("Width of the shop's content area under the tabs, in canvas units - the same on both pages, so the panel never changes size when you switch tabs. Wide enough for the four weapon-tree columns (Loadout Node Width and Loadout Tree Column Gap) and for the three ability columns (Loadout Ability Card Width, two cards across).")]
        public float loadoutPageWidth = 1700f;
        [Tooltip("Height of the shop's content area under the tabs, in canvas units - the same on both pages. Must fit the weapon tree with its Reset Weapon button and the Abilities & Armor page's tallest column.")]
        public float loadoutPageHeight = 560f;
        [Tooltip("Width of one weapon node button in the upgrade tree, in canvas units. The weapons page has the whole content area to itself, so the nodes are big enough to read at a glance; eight upgrades sit side by side (two under each of the four families), so eight of these plus the gaps must fit Loadout Page Width.")]
        public float loadoutNodeWidth = 180f;
        [Tooltip("Height of one weapon node button in the upgrade tree, in canvas units.")]
        public float loadoutNodeHeight = 96f;
        [Tooltip("Width of one ability card on the Abilities & Armor page, in canvas units. Two cards fit across each of the three columns, and the armour block above is as wide as the three columns, so the armour paths start on the first card's left edge.")]
        public float loadoutAbilityCardWidth = 240f;
        [Tooltip("Height of one ability card on the Abilities & Armor page, in canvas units.")]
        public float loadoutAbilityCardHeight = 88f;
        [Tooltip("Width of one armour path (Absorb, Recharge) on the Abilities & Armor page, in canvas units. A path holds its picture, its name and next price, its steps and its + button, from left to right; the path and Reset Armor beside it must fit the width of the ability columns below them.")]
        [UnityEngine.Serialization.FormerlySerializedAs("loadoutArmorCardWidth")]
        public float loadoutArmorPathWidth = 780f;
        [Tooltip("Height of one armour path, in canvas units. The two paths stand one above the other, so the armour section is twice this plus Loadout Armor Path Gap; the whole Abilities & Armor page must still fit Loadout Page Height.")]
        public float loadoutArmorPathHeight = 64f;
        [Tooltip("Width of an armour path's text (its name and level, then the next upgrade and its price), between its picture and its steps, in canvas units. Too narrow and the price line wraps.")]
        public float loadoutArmorPathTextWidth = 340f;
        [Tooltip("Side of the square picture on an armour path, in canvas units. Keep it a little smaller than Loadout Armor Path Height.")]
        public float loadoutArmorPathIconSize = 48f;
        [Tooltip("The heading above the two armour paths in the shop.")]
        public string loadoutArmorHeadingText = "Armor — two upgrade paths";
        [Tooltip("Side of one step marker on an armour path (one per level, from the starting level to the top), in canvas units. Drawn as a diamond.")]
        public float loadoutArmorStepSize = 20f;
        [Tooltip("Distance from one step marker's centre to the next along an armour path, in canvas units. Also the width each step's value below it may take.")]
        public float loadoutArmorStepSpacing = 90f;
        [Tooltip("Thickness of the line joining the step markers of an armour path, in canvas units.")]
        public float loadoutArmorStepLineWidth = 4f;
        [Tooltip("Size of the value under each step marker (how much it soaks, how soon it refills), in canvas units.")]
        public float loadoutArmorStepLabelSize = 15f;
        [Tooltip("The value under each Absorb step marker. {0} = how much damage that level soaks.")]
        public string loadoutArmorAbsorbStepFormat = "{0}";
        [Tooltip("The value under each Recharge step marker. {0} = how many seconds out of combat that level waits before the armour refills.")]
        public string loadoutArmorRechargeStepFormat = "{0}s";
        [Tooltip("Colour of an armour step you already have: its marker is filled with it and the line leading to it is lit with it.")]
        public Color loadoutArmorStepReachedColor = new Color(0.25f, 0.6f, 1f, 1f);
        [Tooltip("Ring colour of the armour step the path's + buys next. The marker stays hollow until it is bought.")]
        public Color loadoutArmorStepNextColor = new Color(0.9f, 0.93f, 1f, 1f);
        [Tooltip("Ring and line colour of an armour step you could still reach later with the upgrades you have left.")]
        public Color loadoutArmorStepLaterColor = new Color(0.42f, 0.42f, 0.47f, 1f);
        [Tooltip("Ring and line colour of an armour step you cannot reach with the upgrades left (both paths share one budget, and in Dominion each round allows only so many). Keep it faint.")]
        public Color loadoutArmorStepOutOfReachColor = new Color(0.24f, 0.24f, 0.27f, 0.6f);
        [Tooltip("Side of the square picture on an ability card in the shop, in canvas units. It sits at the card's left edge with the name and price to its right, so keep it a little smaller than Loadout Ability Card Height.")]
        public float loadoutCardIconSize = 60f;
        [Tooltip("Side of the square picture on a weapon in the shop's tree, in canvas units. It sits at the node's left edge with the name and price to its right; a bigger picture leaves less room for the text (Loadout Node Width).")]
        public float loadoutNodeIconSize = 48f;
        [Tooltip("Gap between a shop card's left edge and its picture, and between the picture and the text, in canvas units.")]
        public float loadoutCardIconGap = 8f;
        [Tooltip("Tint on an item's picture in the shop while the item cannot be picked right now (locked until the break, blocked by the shop, already passed on the weapon tree, or not open yet). White leaves the picture as drawn; grey darkens it and a lower alpha fades it into the card, so a bright picture never makes an item look pickable when it is not. Alpha alone barely shows (the UI blends in linear colour), so darken with grey first.")]
        public Color loadoutIconUnavailableTint = new Color(0.5f, 0.5f, 0.5f, 0.75f);
        [Tooltip("Picture on the Absorb armour path in the shop. The armour upgrades have no item asset of their own, so their pictures live here.")]
        public Sprite loadoutArmorAbsorbIcon;
        [Tooltip("Picture on the Recharge armour path in the shop. The armour upgrades have no item asset of their own, so their pictures live here.")]
        public Sprite loadoutArmorRechargeIcon;
        [Tooltip("Horizontal space between the Mobility, Attachment and Ultimate columns on the Abilities & Armor page, in canvas units. The armour block above spans the same columns, so this also widens it.")]
        public float loadoutAbilityColumnGap = 60f;
        [Tooltip("Gap between the two upgrades of a weapon family (side by side), between ability cards in a column's grid (both directions), and between the armour paths and Reset Armor, in canvas units.")]
        public float loadoutNodeSpacing = 20f;
        [Tooltip("Gap between an armour path's steps (Absorb, Recharge) and its own + button inside the path, in canvas units.")]
        public float loadoutArmorPlusGap = 12f;
        [Tooltip("Vertical gap between the Absorb path (above) and the Recharge path (below) on the Abilities & Armor page, in canvas units. Enough to read as two separate tracks.")]
        [UnityEngine.Serialization.FormerlySerializedAs("loadoutArmorRowGap")]
        public float loadoutArmorPathGap = 8f;
        [Tooltip("Thickness of the highlight border drawn around the equipped weapon node or ability card, in canvas units.")]
        public float loadoutEquippedBorderWidth = 4f;
        [Tooltip("Width and height of a small square icon button - the close X and the armor +Absorb/+Recharge steppers - in canvas units. Raised from an original 44: a bigger button gives the bigger glyph below more room to stay legible.")]
        public float loadoutStepperButtonSize = 56f;
        [Tooltip("Font size for the glyph inside a stepper button (the close X, the armor + buttons), in canvas units - bigger than Body Text Size on purpose: at Body Text Size a '+' drawn this small read as a flat dash rather than a clear plus.")]
        public float loadoutStepperFontSize = 32f;
        [Tooltip("Width of a labelled loadout button - Reset Weapon, Reset Armor - in canvas units.")]
        public float loadoutSmallButtonWidth = 170f;
        [Tooltip("Height of a labelled loadout button - Reset Weapon, Reset Armor - in canvas units.")]
        public float loadoutSmallButtonHeight = 42f;
        [Tooltip("Node fill for a weapon you can upgrade into right now.")]
        public Color loadoutSelectableColor = new Color(0.16f, 0.16f, 0.19f, 0.95f);
        [Tooltip("Node fill for a weapon you already passed through on the way to your current one.")]
        public Color loadoutOwnedColor = new Color(0.30f, 0.30f, 0.33f, 0.85f);
        [Tooltip("Node/card fill for a Selectable item the shop gate refuses right now (out of territory, in combat, or short of gold). Deliberately its OWN colour, distinct from Locked Colour: a shop-blocked item is still reachable (leave the zone, wait out combat, earn the gold) where a tree-Locked item genuinely cannot be picked yet, and the two used to be painted identically. Also distinct from the HUD's Slot Blocked Colour, which marks a HUD ability slot that cannot fire right now (dead/stunned/recharging) - a different question with its own look.")]
        public Color loadoutShopBlockedColor = new Color(0.22f, 0.14f, 0.05f, 0.92f);
        [Tooltip("Font size percentage (of Body/Small Text Size) for the price/status line under a weapon node or ability card's name - e.g. \"<size=70%>\".")]
        [Range(10f, 100f)] public float loadoutPriceLineSizePercent = 70f;
        [Tooltip("Seconds a refused click's reason (\"Need 700 more gold\", \"Out of combat in 2.4s\"...) stays shown in the header status line before it reverts to the ordinary gate status. A click on a shop-blocked item used to do nothing visible at all.")]
        public float loadoutBlockedReasonDurationSeconds = 2f;
        [Tooltip("Second line on a weapon node that can only be reached by selling your current weapon path first. {0} = the new weapon's price, {1} = the gold selling back gives right now (the same number the Reset Weapon button shows). Not shown when there is nothing to sell back.")]
        public string loadoutSwapFormat = "Swap {0} (sell back +{1})";
        [Tooltip("Font size percentage for that swap line (smaller than Loadout Price Line Size Percent so it fits the node).")]
        [Range(10f, 100f)] public float loadoutSwapLineSizePercent = 55f;
        [Tooltip("Message after selling weapons with Reset Weapon. {0} = weapon name, {1} = gold refunded.")]
        public string loadoutSoldWeaponFormat = "Sold {0}: +{1} credits";
        [Tooltip("Message after selling armour upgrades with Reset Armor. {0} = gold refunded.")]
        public string loadoutSoldArmorFormat = "Sold armour upgrades: +{0} credits";
        [Tooltip("Seconds a 'Sold ...' message stays on the shop's status line.")]
        public float loadoutSoldMessageDurationSeconds = 3f;
        [Tooltip("Seconds the pointer must rest on a weapon node, ability card or armour row before its pop-up (name, what it does, numbers) appears next to the cursor. Moving off hides it at once; moving to another item restarts the wait.")]
        public float loadoutTooltipDelaySeconds = 0.5f;
        [Tooltip("Widest a shop pop-up gets, in canvas units; longer text wraps onto more lines.")]
        public float loadoutTooltipMaxWidth = 400f;
        [Tooltip("Colour of the numbers block (damage, cooldown, ...) in a shop pop-up, under the name and the one-line description.")]
        public Color loadoutTooltipNumbersColor = new Color(0.72f, 0.74f, 0.80f, 1f);
        [Tooltip("Background of the shop tooltip.")]
        public Color loadoutTooltipBackgroundColor = new Color(0.12f, 0.12f, 0.145f, 1f);
        [Tooltip("Where the tooltip's top-left corner sits relative to the cursor, in canvas units (x right, y up: a negative y is below the cursor).")]
        public Vector2 loadoutTooltipOffset = new Vector2(16f, -18f);
        [Tooltip("Gap between the cursor and the tooltip when it flips to the other side of the cursor near a screen edge, in canvas units.")]
        public float loadoutTooltipFlipGap = 8f;
        [Tooltip("How far beside its column an arrow bows out when it must run down past another node in the same column, as a fraction of Loadout Tree Column Gap. The shop's tree draws its upgrades side by side, so no arrow uses this today; it is kept for a deeper tree.")]
        [Range(0.1f, 1f)] public float loadoutArrowSideLaneFactor = 0.8f;
        [Tooltip("Armour row wording while upgrades remain. {0} = the number of the next upgrade, {1} = the limit (ArmorConfig Max Armor Upgrades).")]
        public string loadoutArmorUpgradeFormat = "Upgrade {0} of {1}";
        [Tooltip("Armour row wording once the limit is reached. {0} = the limit.")]
        public string loadoutArmorMaxFormat = "{0} of {0} (max)";
        [Tooltip("Armour row wording in Dominion once this round's allowance is used up (the shop's own maximum is higher; later rounds open more). {0} = this round's allowance.")]
        public string loadoutArmorRoundMaxFormat = "{0} of {0} this round";
        [Tooltip("The shop header's note in a Dominion break: picks are free, no gold involved, and anything can be changed until the round starts.")]
        public string loadoutDominionFreeText = "Free (break)";
        [Tooltip("What the shop says when it is asked to open (P, or the Loadout button) in a Dominion match while no round or break is on (the moment before the first break, or once the match is over), and what its header says if it is somehow open then. Shown as a short toast over the game when the shop stays shut.")]
        public string loadoutShopClosedText = "The shop is closed";
        [Tooltip("In a Dominion round, what a locked shop item says and the reason shown when one is clicked: the gun, an ability slot already filled, and Reset Armor stay as they are until the next break.")]
        public string loadoutLockedUntilBreakText = "Locked until the break";
        [Tooltip("The shop header's note while a Dominion round is being played: what you already have stays until the next break, and only empty ability slots can still be filled.")]
        public string loadoutDominionRoundText = "Round on: only empty slots can be filled";
        [Tooltip("Armour row wording for a row that is at its own top level with upgrades still left in the shared limit.")]
        public string loadoutArmorTopLevelText = "top level";
        [Tooltip("The Absorb armour path's text. {0} = its level, {1} = the status text (which upgrade is next and its price, or max), drawn smaller (Loadout Price Line Size Percent) like an ability card's price. Keep the line break after the name.")]
        [TextArea(2, 3)] public string loadoutArmorAbsorbRowFormat = "Absorb lv {0}\n{1}";
        [Tooltip("The Recharge armour path's text. {0} = its level, {1} = the status text (which upgrade is next and its price, or max), drawn smaller (Loadout Price Line Size Percent) like an ability card's price. Keep the line break after the name.")]
        [TextArea(2, 3)] public string loadoutArmorRechargeRowFormat = "Recharge lv {0}\n{1}";
        [Tooltip("Colour of the arrows from each weapon to the upgrades it opens in the shop's weapon tree (neutral grey).")]
        public Color loadoutArrowColor = new Color(0.42f, 0.42f, 0.47f, 1f);
        [Tooltip("Thickness of those arrows, in canvas units.")]
        public float loadoutArrowWidth = 3f;
        [Tooltip("Length of the arrowhead at the upgrade end of each tree arrow, in canvas units.")]
        public float loadoutArrowHeadSize = 12f;
        [Tooltip("Vertical space between one weapon-tree row and the next, in canvas units - room for the arrows to read.")]
        public float loadoutTreeRowGap = 90f;
        [Tooltip("Horizontal space between the four weapon families (each family is its node with its two upgrades side by side beneath it), in canvas units. The whole tree must fit Loadout Page Width.")]
        public float loadoutTreeColumnGap = 50f;
        [Tooltip("Text on the shop's first tab (the weapon tree page).")]
        public string loadoutTabWeaponsText = "Weapons";
        [Tooltip("Text on the shop's second tab (the armour rows and the Mobility, Attachment and Ultimate cards).")]
        public string loadoutTabAbilitiesArmorText = "Abilities & Armor";
        [Tooltip("Width of one shop tab, in canvas units.")]
        public float loadoutTabWidth = 300f;
        [Tooltip("Height of one shop tab, in canvas units.")]
        public float loadoutTabHeight = 46f;
        [Tooltip("Fill of the tab of the page you are on.")]
        public Color loadoutTabActiveColor = new Color(0.30f, 0.30f, 0.36f, 1f);
        [Tooltip("Fill of the tab of the page you are not on.")]
        public Color loadoutTabInactiveColor = new Color(0.13f, 0.13f, 0.16f, 1f);
        [Tooltip("Pop-up name of the Absorb armour row.")]
        public string loadoutArmorAbsorbTipName = "Absorb armor";
        [Tooltip("Pop-up line saying what the Absorb armour path does.")]
        public string loadoutArmorAbsorbTipText = "Armor that soaks damage before your health takes any.";
        [Tooltip("Pop-up numbers: the Absorb level you have. {0} = level, {1} = how much damage it soaks.")]
        public string loadoutArmorAbsorbNowFormat = "Now: level {0}, soaks {1} damage";
        [Tooltip("Pop-up numbers: what the next Absorb upgrade gives. {0} = level, {1} = how much damage it soaks, {2} = the price.")]
        public string loadoutArmorAbsorbNextFormat = "Next: level {0}, soaks {1} damage ({2})";
        [Tooltip("Pop-up name of the Recharge armour row.")]
        public string loadoutArmorRechargeTipName = "Recharge armor";
        [Tooltip("Pop-up line saying what the Recharge armour path does.")]
        public string loadoutArmorRechargeTipText = "Your armor starts refilling sooner after a fight.";
        [Tooltip("Pop-up numbers: the Recharge level you have. {0} = level, {1} = seconds out of combat before it refills.")]
        public string loadoutArmorRechargeNowFormat = "Now: level {0}, refills after {1}s out of combat";
        [Tooltip("Pop-up numbers: what the next Recharge upgrade gives. {0} = level, {1} = seconds out of combat before it refills, {2} = the price.")]
        public string loadoutArmorRechargeNextFormat = "Next: level {0}, refills after {1}s out of combat ({2})";
        [Tooltip("Pop-up numbers line for an armour row that has no upgrade left (limit reached or top level).")]
        public string loadoutArmorNoNextText = "Next: no upgrade left";
        [Tooltip("Width of the always-visible 'Loadout (P)' button bottom-right of the HUD, in canvas units.")]
        public float loadoutToggleButtonWidth = 190f;
        [Tooltip("Height of the always-visible 'Loadout (P)' button bottom-right of the HUD, in canvas units.")]
        public float loadoutToggleButtonHeight = 48f;
        [Tooltip("Distance from the bottom-right screen corner to the 'Loadout (P)' button, in canvas units, on both axes.")]
        public float loadoutToggleButtonMargin = 24f;

        [Header("Shots")]
        [Tooltip("Each team's colour, index = team id (0/1/2) - Teams.TryGetTeam's own numbering, read through ShotColorFor " +
                 "below rather than indexed directly so an out-of-range id falls back safely. It is that team's colour " +
                 "wherever one is drawn: shot trails and bullet cores, the respawn bubble, the minimap and scoreboard, and " +
                 "the mech's body and glow, so changing one recolours all of them. Keep it bright enough to read against the arena's dark walls.")]
        public Color[] teamShotColors = new Color[3]
        {
            new Color(1f, 0.45f, 0.05f, 1f),  // team 0 - orange
            new Color(0.68f, 0.32f, 1f, 1f),  // team 1 - violet
            new Color(0.15f, 0.95f, 1f, 1f),  // team 2 - cyan
        };
        [Tooltip("Trail/tint colour used when the shooter's team could not be resolved - Teams.TryGetTeam returned " +
                 "-1. Should never actually appear in a real match; only a debug/test-range shot fired with no team " +
                 "assigned reads this. Deliberately dimmer AND lower alpha than every real team colour - both the " +
                 "trail's fade (reads straight off this colour's alpha) and the core's glow (ShotTeamVisuals.TintCore " +
                 "scales emission by this same alpha) key off it, so an unresolved shot reads as a faint, washed-out " +
                 "ghost rather than a fourth team colour.")]
        public Color unknownTeamShotColor = new Color(0.55f, 0.55f, 0.58f, 0.5f);
        [Tooltip("How many seconds a shot's trail keeps fading behind it after the bullet itself is gone. Raised from " +
                 "an original 0.25 - at the game's normal camera zoom a shot crosses almost the whole visible frame " +
                 "in under half a second, and 0.25 left too little of the tail actually drawn to compare colours by.")]
        public float trailTime = 0.35f;
        [Tooltip("Trail width where it meets the bullet, in metres. Raised from an original 0.09 - too thin a ribbon " +
                 "to read its colour at a glance at the game's normal camera zoom.")]
        public float trailStartWidth = 0.15f;
        [Tooltip("Trail width at its fading tail end, in metres - thinner than Trail Start Width so the " +
                 "trail reads as tapering off rather than a solid ribbon.")]
        public float trailEndWidth = 0.02f;
        [Tooltip("Shared unlit material every shot trail renders with - Assets/_Project/Features/UI/Shot Trail.mat, built the " +
                 "same way as the Aim Cone Line Material (URP Particles/Unlit, alpha transparent, no shadows). Its " +
                 "own colour stays white: every trail tints itself through TrailRenderer.colorGradient, which is what " +
                 "lets one material serve every team.")]
        public Material trailMaterial;
        [Tooltip("How much of the team colour the bullet's core takes, in its colour and its glow: 0 is a white-hot core (Flash White), 1 is the full " +
                 "team colour. Keep it low so the core reads white-hot and the team shows in the trail and the light under the shot; high " +
                 "values turn the whole shot into one team-coloured blob.")]
        [Range(0f, 1f)] public float bulletTintStrength = 0.2f;
        [Tooltip("How brightly the bullet's white-hot core glows (its colour is Flash White with Bullet Tint Strength of the team " +
                 "colour). Keep it well above 1: the scene's Bloom only halos what is brighter than 1, and the halo is what makes " +
                 "the core read as a hot shot instead of a flat-lit pellet.")]
        public float bulletEmission = 5f;

        /// <summary>The trail/tint colour for a shot fired by teamId; Unknown Team Shot Colour for an unresolved
        /// (-1) or out-of-range id - the same fail-open reading ShooterTeamId carries in the rest of the weapons code.</summary>
        public Color ShotColorFor(int teamId) => TeamPalette.Of(teamShotColors, teamId, unknownTeamShotColor);

        // Not serialized - built lazily per bucket, then reused. Every client simulates every projectile, so
        // ShotTeamVisuals must not allocate a Gradient per shot (see GradientFor).
        [System.NonSerialized] private Dictionary<int, Gradient> cachedShotGradients;

        /// <summary>Drops the cached Gradients so an Inspector edit of Team Shot Colors (or the unknown-team
        /// fallback) shows at once in the Editor instead of after a domain reload.</summary>
        private void OnValidate() => cachedShotGradients = null;

        /// <summary>ShotColorFor(teamId) as the two-key fade-to-transparent Gradient a shot's TrailRenderer wants,
        /// cached by resolved bucket (0/1/2, -1 for every unresolved id) so equal teams share one object.</summary>
        public Gradient GradientFor(int teamId)
        {
            int bucket = TeamPalette.Holds(teamShotColors, teamId) ? teamId : -1;

            if (cachedShotGradients == null)
                cachedShotGradients = new Dictionary<int, Gradient>();

            if (cachedShotGradients.TryGetValue(bucket, out Gradient cached))
                return cached;

            Color color = ShotColorFor(teamId);
            var gradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) },
            };
            cachedShotGradients[bucket] = gradient;
            return gradient;
        }

        [Header("Lasers")]
        [Tooltip("Width of the wind-up warning line the instant the trigger is pulled, in metres. Grows to Laser " +
                 "Warning End Width over the wind-up, so the line visibly thickens as the beam gets closer to firing " +
                 "- part of the laser telegraph (see IgnoreWalls.cs).")]
        public float laserWarningStartWidth = 0.03f;
        [Tooltip("Width of the wind-up warning line right before it fires, in metres.")]
        public float laserWarningEndWidth = 0.14f;
        [Tooltip("Opacity of the wind-up warning line the instant the trigger is pulled, 0-1. Kept low so " +
                 "the very start of a wind-up reads as a faint hint rather than an alarm.")]
        [Range(0f, 1f)] public float laserWarningStartAlpha = 0.15f;
        [Tooltip("Opacity of the wind-up warning line right before it fires, 0-1. Higher than the start so " +
                 "the line is unmistakable by the moment the beam actually lands.")]
        [Range(0f, 1f)] public float laserWarningEndAlpha = 0.9f;
        [Tooltip("Shared unlit material the wind-up warning line renders with - reuses Shot Trail's or Aim " +
                 "Cone Line's own material (URP Particles/Unlit, vertex colour) rather than adding a third " +
                 "near-identical one. Every warning line tints itself through its own LineRenderer start/end " +
                 "colour, which is what lets one material serve every team.")]
        public Material laserWarningMaterial;
        [Tooltip("Width of a fired laser beam's visible line, in metres - the line every client draws the " +
                 "instant a laser's wind-up ends (or immediately, for a laser with no wind-up).")]
        public float laserBeamWidth = 0.14f;
        [Tooltip("Multiplies the beam line's team colour before it is drawn, so the beam itself reads as a " +
                 "bright, glowing line rather than a flat-lit one at a glance - the same idea as Bullet " +
                 "Emission above, applied to the laser's own LineRenderer colour instead of a property " +
                 "block. 1 = no boost over the plain team colour.")]
        public float laserBeamEmission = 1.6f;
        [Tooltip("Seconds a fired beam stays on screen, fading to transparent over this time, before it " +
                 "disappears - replaces Hitscan's own former Beam Duration field. One home for this number " +
                 "so all three laser leaves read consistently instead of being tuned per-prefab.")]
        public float laserBeamLingerSeconds = 0.18f;

        [Header("Gold")]
        [Tooltip("Colour of the HUD gold readout (\"Gold 1234  +7.7/s\") - a warm amber, the same " +
                 "family as Highlight Colour/Ultimate Charge Colour, so gold reads as a reward the " +
                 "same way the ultimate-ready glow does, not just another stat.")]
        public Color goldTextColor = new Color(1f, 0.82f, 0.2f, 1f);
        [Tooltip("Gap between the top of the 'Loadout (P)' button and the gold readout sitting above it, in canvas " +
                 "units. Gold and the shop are the same system, so the readout sits up against the button that spends " +
                 "it, out of the ability bar.")]
        public float goldShopGap = 8f;
        [Tooltip("Font size of the income line (\"+7.7/s\") as a percentage of the balance line above it. The " +
                 "balance is the number you act on; the income is context, so it is deliberately smaller - the " +
                 "same relationship Loadout Price Line Size Percent gives a shop item's price.")]
        [Range(30f, 100f)] public float goldIncomeSizePercent = 75f;

        [Header("Bounty toast")]
        [Tooltip("Text colour of the transient \"Bounty +900\" toast shown when a capture pays your team a bounty - " +
                 "the same warm amber family as Gold Text Colour, so a bounty reads as an emphatic version of the " +
                 "same gold reward rather than an unrelated alert colour.")]
        public Color bountyToastColor = new Color(1f, 0.82f, 0.2f, 1f);
        [Tooltip("Seconds the bounty toast stays on screen before it hides itself again.")]
        public float bountyToastDurationSeconds = 3f;

        [Header("Capital under attack")]
        [Tooltip("Shown on the death card, under the countdown, while a player waits to respawn and their capital is currently " +
                 "under attack, so their coming respawn will land at their Tier 2 zone instead of the capital.")]
        public string capitalUnderAttackRespawnNote = "Your capital is under attack - you will respawn at your Tier 2 zone";
        [Tooltip("HUD toast shown right after a player respawns at their capital's Tier 2 zone because the " +
                 "capital was under attack. Uses the same toast label and duration as the bounty payout " +
                 "(Bounty Toast Duration Seconds above).")]
        public string capitalUnderAttackRespawnToast = "Respawned at Tier 2: capital under attack";
        [Tooltip("Text size of the capital-under-attack note on the death card, in reference pixels (the HUD's 1920x1080 grid). " +
                 "The card is as wide as Death Card Width, so a larger size takes more lines.")]
        public float capitalUnderAttackNoteFontSize = 24f;
        [Tooltip("Text colour of the capital-under-attack note on the death card.")]
        public Color capitalUnderAttackNoteColor = Color.white;
        [Tooltip("The strip drawn behind the capital-under-attack note inside the death card, a shade darker than the card, " +
                 "so the warning reads as its own line and not as part of the countdown.")]
        public Color capitalUnderAttackNoteBackingColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("Phase transition")]
        [Tooltip("HUD toast shown to every surviving player the instant the match narrows from three teams to two " +
                 "(the last stand is the same with two teams - a team with no base can't respawn and is out once " +
                 "every member is dead, unless it retakes or takes a base). Uses the same toast label and duration as " +
                 "the bounty payout (Bounty Toast Duration Seconds above). Keep it to two lines (about 52 " +
                 "characters): the toast label grows upward and clips at the top of the screen.")]
        public string twoTeamsLeftBannerText = "Two teams left: lose your base and you can't respawn";

        [Tooltip("The waiting card a dead player sees while their team holds no base and nobody on it can respawn. " +
                 "Line breaks and runs of spaces read as one space: the card shows it as one line that wraps.")]
        public string waitingPanelText = "Waiting for team to \n\ncapture Base territory ";

        [Header("Capture ring")]
        [Tooltip("Material every capture ring line draws with. Keep it unlit, transparent, vertex-coloured and its own " +
                 "colour white: each ring tints itself per team. Points at the Aim Cone Line material, which is exactly that.")]
        public Material captureRingMaterial;
        [Tooltip("Thickness of the thin circle on the ground marking the edge of every capture zone, in metres. Its " +
                 "outer edge sits exactly on the zone's Capture Radius.")]
        public float captureRingOutlineWidth = 0.15f;
        [Tooltip("Thickness of the progress band drawn just inside the edge while a zone is being captured or drained, " +
                 "in metres.")]
        public float captureRingArcWidth = 0.45f;
        [Tooltip("Gap between the edge circle and the progress band, in metres.")]
        public float captureRingArcGap = 0.1f;
        [Tooltip("How far above the ground the ring floats, in metres. Just enough never to flicker into the ground; " +
                 "raise it if parts of a ring disappear on uneven ground.")]
        public float captureRingHeightOffset = 0.06f;
        [Tooltip("Edge colour of a zone nobody owns: a dim white (GDD p.45, neutral territories use a dim white). Owned " +
                 "zones use their team colour.")]
        public Color captureRingNeutralColor = new Color(1f, 1f, 1f, 0.35f);
        [Tooltip("Blinks per second of a paused progress band (the capture is contested, its link is under attack, or " +
                 "a drain is on hold). Keep it slower than the pulse below, so a pause reads differently from an attack.")]
        public float captureRingPausedBlinkSpeed = 0.7f;
        [Tooltip("Brightest opacity (0-1) of a paused progress band while it blinks. Lower than a moving band, so a " +
                 "pause reads as 'on hold'. The minimap's progress rings blink the same way.")]
        [Range(0f, 1f)] public float captureRingPausedOpacity = 0.6f;
        [Tooltip("Colour an owned zone's edge pulses to while an enemy is inside (under attack), even before anything " +
                 "drains. The minimap bubble's outline pulses to it too.")]
        public Color captureRingWarningColor = new Color(1f, 0.2f, 0.15f, 1f);
        [Tooltip("Pulses per second of a zone's edge while it is under attack, or being drained (a drain pulses in the " +
                 "draining team's colour instead).")]
        public float captureRingPulseSpeed = 1.6f;
        [Tooltip("How dark a pulsing zone edge and tower crown get at the moment they swap colour (0 = black, 1 = no dip, a hard blink). " +
                 "The owner's colour fades down, then the warning red (or the draining team's colour) fades up, so the two colours never " +
                 "mix into a third: purple and red mixed read as pink, orange and red as a muddy red.")]
        [Range(0f, 1f)] public float captureRingPulseDip = 0.35f;
        [Tooltip("How many straight pieces make up each ring. More reads as a smoother circle; 96 is smooth at every zoom.")]
        [Range(16, 256)] public int captureRingSegments = 96;
        [Tooltip("The dark loop behind a capture's progress band, so how full it is reads like a loading bar.")]
        public Color captureRingTrackColor = new Color(0f, 0f, 0f, 0.4f);

        [Header("Minimap")]
        [Tooltip("On: the corner minimap sits in the top-left corner, with Dominion's score bars to its right (between it and the timer) " +
                 "and its lines under it. Off: the top-right corner, everything mirrored back.")]
        public bool minimapOnLeft;
        [Tooltip("Size (bounding diameter) of the triangular minimap in its corner, in canvas units.")]
        public float minimapCornerSize = 340f;
        [Tooltip("Gap between the corner minimap's frame and the top and side screen edges, in canvas units.")]
        public float minimapCornerMargin = 24f;
        [Tooltip("Diameter of the large map shown in the middle of the screen while M is toggled on, in canvas units. " +
                 "Everything on it (bubbles, lines, labels) scales up from the corner sizes by the same amount. Shrinks " +
                 "to fit above Large Bottom Clearance if this would otherwise overlap it.")]
        public float minimapLargeSize = 860f;
        [Tooltip("Height of the strip left clear at the bottom of the screen for the HUD's ability bar while the " +
                 "large map (M) is open, in canvas units. The large map's diameter shrinks below Large Size if it " +
                 "would otherwise overlap this strip, and the map centres itself in whatever space remains above it. " +
                 "It is low because the HUD is 20% smaller (Hud Scale) and the gold row left it.")]
        public float minimapLargeBottomClearance = 300f;
        [Tooltip("Width of the dark anti-aliased band framing the triangular minimap's edge, in canvas units at the " +
                 "corner size (it scales up with everything else on the large map). Also used as the corner map's " +
                 "inset from the screen edges.")]
        public float minimapFrameWidth = 5f;
        [Tooltip("Colour of the minimap's frame (and its background if the baked arena image is missing).")]
        public Color minimapFrameColor = new Color(0.06f, 0.06f, 0.08f, 0.9f);
        [Tooltip("Tint and opacity of the baked arena picture under the minimap. Lower the alpha or darken it so the " +
                 "bubbles and lines stand out more.")]
        public Color minimapBackgroundTint = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("Bubble diameter of a Tier 1 zone (capital) on the corner minimap, in canvas units. The GDD draws " +
                 "capitals largest.")]
        public float minimapBubbleDiameterTier1 = 36f;
        [Tooltip("Bubble diameter of a Tier 2 zone on the corner minimap, in canvas units (small in the GDD).")]
        public float minimapBubbleDiameterTier2 = 24f;
        [Tooltip("Bubble diameter of a Tier 3 zone on the corner minimap, in canvas units (small in the GDD). Kept small so the health pack cross in its recess does not touch the bubble.")]
        public float minimapBubbleDiameterTier3 = 18f;
        [Tooltip("Bubble diameter of the Tier 4 centre zone on the corner minimap, in canvas units (medium in the GDD).")]
        public float minimapBubbleDiameterTier4 = 30f;
        [Tooltip("Width of the dark outline around each zone bubble, in canvas units. It pulses to Capture Ring Warning " +
                 "Colour while the zone is under attack.")]
        public float minimapBubbleOutlineWidth = 3f;
        [Tooltip("Colour of a zone bubble's outline when nothing is attacking it.")]
        public Color minimapBubbleOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        [Tooltip("Fill of a neutral zone's bubble, and colour of a link nobody owns.")]
        public Color minimapNeutralColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        [Tooltip("Font size of the I / II / III / IV label on each bubble, in canvas units.")]
        public float minimapLabelSize = 16f;
        [Tooltip("Thickness of the capture progress ring around a bubble, in canvas units. It fills and blinks like " +
                 "the ring on the ground.")]
        public float minimapProgressRingWidth = 4f;
        [Tooltip("Width of a link one team owns both ends of, a way-in link with its arrowhead, or each half of a " +
                 "Border link (two different teams' zones), in canvas units.")]
        public float minimapOwnedLinkWidth = 4f;
        [Tooltip("Width of a link nobody owns (a thin grey line), in canvas units.")]
        public float minimapNeutralLinkWidth = 2f;
        [Tooltip("Thickness of the centre scan's red ring on the minimap, in canvas units.")]
        public float minimapScanRingWidth = 2.5f;
        [Tooltip("The text of the countdown above the centre tower. {0} is the whole seconds left until the next centre scan, " +
                 "e.g. \"Scan {0}\" reads \"Scan 12\". Its height and on/off are on the Vision Config; its size is below.")]
        public string scanCountdownFormat = "Scan {0}";
        [Tooltip("Text size of the countdown above the centre tower, in reference pixels (1080p). It is a HUD label, so it keeps " +
                 "the same size on screen however far the camera is zoomed out. Bigger reads better but covers more of the view.")]
        [Min(1f)] public float scanCountdownFontSize = 28f;
        [Tooltip("Text size of the second countdown, the one fixed under the corner minimap, in reference pixels (1080p). Same words " +
                 "and colour as the one above the tower; it is always on screen while there is a scan, so it can stay smaller.")]
        [Min(1f)] public float scanMinimapCountdownFontSize = 14.3f;
        [Tooltip("The yellow text floating above a tower that carries a bounty in Dominion. {0} is the points the team that takes the zone will get, " +
                 "e.g. \"BOUNTY {0}\" reads \"BOUNTY 150\".")]
        public string bountyLabelFormat = "BOUNTY {0}";
        [Tooltip("The same label in Conquest, where the bounty is gold. {0} is the gold each player of the team that takes the zone will get.")]
        public string bountyLabelGoldFormat = "BOUNTY {0} CREDITS";
        [Tooltip("The text that pops above a tower when its bounty is paid in Dominion. {0} is the points paid, e.g. \"+{0}\" reads \"+150\".")]
        public string bountyPopFormat = "+{0}";
        [Tooltip("The same pop in Conquest. {0} is the gold each player of the team got.")]
        public string bountyPopGoldFormat = "+{0} CREDITS";
        [Tooltip("The colour of the bounty label and of the pop when it is paid. A warm yellow that stands out from the team colours.")]
        public Color bountyLabelColour = new Color(1f, 0.85f, 0.1f, 1f);
        [Tooltip("Text size of the bounty label above a tower, in reference pixels (1080p). It is a HUD label, so it keeps the same size on screen however far the camera is zoomed out.")]
        [Min(1f)] public float bountyLabelFontSize = 30f;
        [Tooltip("Text size of the pop when a bounty is paid, in reference pixels (1080p). Bigger than the label so the payout is noticed.")]
        [Min(1f)] public float bountyPopFontSize = 44f;
        [Tooltip("How high above the middle of the tower's foot the bounty label is pinned, in metres, before its pixel lift below. About 1.5 m puts it at the " +
                 "tower's base, where the game camera looks, so it stays on screen with the tower.")]
        [Min(0f)] public float bountyLabelAnchorHeight = 1.5f;
        [Tooltip("How far the bounty label floats above its anchor point, in reference pixels (1080p). Pixels rather than metres: the game camera is only " +
                 "a few metres above the towers, so a height in metres would swing wildly with the distance.")]
        [Min(0f)] public float bountyLabelLift = 56f;
        [Tooltip("How far the pop rises above the label while it fades, in reference pixels (1080p).")]
        [Min(0f)] public float bountyPopRise = 110f;
        [Tooltip("How long the pop lasts from appearing until it is gone, in seconds.")]
        [Min(0.1f)] public float bountyPopSeconds = 2.5f;
        [Tooltip("Size of a way-in arrowhead, in canvas units. It points from a team's zone toward the neutral zone next to it.")]
        public float minimapArrowheadSize = 12f;
        [Tooltip("Size of your own arrow on the minimap, in canvas units. It points where you face.")]
        public float minimapOwnMarkerSize = 18f;
        [Tooltip("Size of the little health pack cross on a bubble's upper right edge, in canvas units. Its colours (green ready, grey taken) come from the Health Pack Config.")]
        public float minimapPackBadgeSize = 12f;
        [Tooltip("How thick the arms of that cross are, as a fraction of its size (0.3 = a third).")]
        [Range(0.1f, 0.6f)] public float minimapPackBadgeBarFraction = 0.34f;
        [Tooltip("Colour of your own arrow on the minimap.")]
        public Color minimapOwnMarkerColor = new Color(1f, 0.85f, 0.25f, 1f);
        [Tooltip("Diameter of a teammate's dot on the minimap, in canvas units. Enemies aren't shown.")]
        public float minimapTeammateDotSize = 10f;
        [Tooltip("Colour of a teammate's dot on the minimap.")]
        public Color minimapTeammateDotColor = new Color(0.45f, 1f, 0.45f, 1f);
        [Tooltip("How solid the small corner map is, 0 is invisible and 1 is fully opaque (0.8 makes it 20% more " +
                 "see-through). It multiplies everything on the map at once - the picture, the bubbles, the links and " +
                 "the markers.")]
        [Range(0f, 1f)] public float minimapCornerOpacity = 0.8f;
        [Tooltip("How solid the large map is while M is held open, 0 to 1. Full opacity suits it: you opened it on " +
                 "purpose, so nothing is hiding behind it that you would rather be looking at.")]
        [Range(0f, 1f)] public float minimapLargeOpacity = 1f;
        [Tooltip("How much opacity the large map gives up while you are moving, 0 to 1, so you can still see where " +
                 "you are running. Subtracted from Large Opacity above; the corner map never dims for movement.")]
        [Range(0f, 1f)] public float minimapLargeMovingOpacityDrop = 0.3f;
        [Tooltip("Seconds a full fade from invisible to solid takes. The map fades between its opacity states " +
                 "rather than snapping, so starting and stopping reads as a change of state, not a flicker. 0 " +
                 "turns the fade off.")]
        public float minimapOpacityFadeSeconds = 0.25f;
        [Tooltip("How fast you have to be going, in metres per second, before the large map counts you as " +
                 "MOVING. The base move speed is 5, so 1 is a fifth of walking pace. It is deliberately higher " +
                 "than Moving Exit Speed below - see that field.")]
        public float minimapMovingEnterSpeed = 1f;
        [Tooltip("How slow you have to be going, in metres per second, before the large map counts you as " +
                 "STOPPED. Deliberately lower than Moving Enter Speed: between the two the map keeps whatever " +
                 "state it already had, so a player drifting around one single threshold cannot make it strobe.")]
        public float minimapMovingExitSpeed = 0.35f;
        [Tooltip("Seconds of smoothing on the measured speed before it is compared with the two speeds above. " +
                 "Frame-to-frame position deltas are noisy enough on their own to tip a threshold back and " +
                 "forth. 0 uses the raw per-frame speed.")]
        public float minimapSpeedSmoothingSeconds = 0.15f;

        /// <summary>The corner-map bubble diameter for a zone tier (1 capital ... 4 centre).</summary>
        public float MinimapBubbleDiameter(int tier) => tier switch
        {
            1 => minimapBubbleDiameterTier1,
            3 => minimapBubbleDiameterTier3,
            4 => minimapBubbleDiameterTier4,
            _ => minimapBubbleDiameterTier2,
        };

        [Header("OverPower")]
        [Tooltip("Text shown in the HUD's OverPower label while the buff is fully ACTIVE.")]
        public string overPowerActiveText = "OVERPOWER";
        [Tooltip("Text shown in the HUD's OverPower label while the buff is only ARMED - hit by both " +
                 "enemy teams within the window, near your own territory, but not yet triggered.")]
        public string overPowerArmedText = "OverPower ready";
        [Tooltip("Text colour of the HUD label while the comeback buff is fully ACTIVE (shield just " +
                 "refilled, damage/fire rate/range boosted, overheat nullified). A hot, urgent colour " +
                 "of its own - distinct from the overheat/bounty families - so the one moment the " +
                 "buff is actually live reads as a clearly different state from everything else the " +
                 "HUD already shows in red/amber.")]
        public Color overPowerActiveColor = new Color(1f, 0.25f, 0.55f, 1f);
        [Tooltip("Text colour of the fainter hint shown while the buff is only ARMED - two enemy " +
                 "teams have hit you within the window, near your own territory, but you have not " +
                 "yet dropped under the health threshold. Same hue as Active, low alpha, so it reads " +
                 "as a preview of the same state rather than an unrelated colour.")]
        public Color overPowerArmedColor = new Color(1f, 0.25f, 0.55f, 0.45f);

        [Header("Aim cone")]
        [Tooltip("Draw the aim cone: two lines from your gun showing how far and how wide your shots can go. A designer switch, set " +
                 "here only and never offered in the game's menus, so players learn their guns' cones by playing. Off by default; " +
                 "the crosshair shows the spread either way.")]
        public bool showAimCone = false;
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

        [Header("Charge ring")]
        [Tooltip("Show the ring at your own feet while you hold a charging weapon's trigger. Off draws nothing at " +
                 "all; the weapon still charges exactly the same.")]
        public bool showChargeRing = true;
        [Tooltip("Material the charge ring draws with. Keep it unlit, transparent, vertex-coloured and its own " +
                 "colour white - the ring tints itself through each line's colour. Points at the Aim Cone Line " +
                 "material, which is exactly that and is what the capture ring uses too.")]
        public Material chargeRingMaterial;
        [Tooltip("Distance from the player's centre to the middle of the charge ring, in metres. The player's own " +
                 "body is 0.7 m across, so anything below about 0.8 draws inside their feet.")]
        public float chargeRingRadius = 1.15f;
        [Tooltip("Thickness of the charge ring's band, in metres.")]
        public float chargeRingWidth = 0.22f;
        [Tooltip("How far above the floor the charge ring floats, in metres. Just enough never to flicker into the " +
                 "ground; raise it if parts of the ring disappear on a slope.")]
        public float chargeRingHeightOffset = 0.06f;
        [Tooltip("How many straight pieces make up the charge ring. More reads as a smoother circle.")]
        [Range(16, 256)] public int chargeRingSegments = 64;
        [Tooltip("The dark loop behind the charge ring's fill, so how full it is reads like a loading bar. Without " +
                 "it a part-filled band on open ground reads as a stray arc rather than a meter.")]
        public Color chargeRingTrackColor = new Color(0f, 0f, 0f, 0.45f);
        [Tooltip("Colour of the charge ring's fill while it is still filling.")]
        public Color chargeRingFillColor = new Color(1f, 0.82f, 0.2f, 0.85f);
        [Tooltip("Colour the charge ring's fill switches to the moment the charge is full, so the ceiling is " +
                 "unmistakable without having to judge a closed circle by eye.")]
        public Color chargeRingFullColor = new Color(1f, 0.95f, 0.6f, 1f);
        [Tooltip("Colour of the ticks marking where the next round is earned. A weapon whose charge has no steps " +
                 "shows no ticks at all.")]
        public Color chargeRingStepTickColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("How far a step tick reaches across the ring, in metres - centred on the ring, so a little more " +
                 "than Charge Ring Width makes it read as a notch cut through the band.")]
        public float chargeRingStepTickLength = 0.3f;
        [Tooltip("Thickness of a step tick, in metres.")]
        public float chargeRingStepTickWidth = 0.05f;

        [Header("Crosshair")]
        [Tooltip("Colour of the crosshair's small centre hexagon at the point you aim at. Kept the same for every team, so it never " +
                 "reads as an ally or an enemy marker.")]
        public Color crosshairColor = new Color(0.85f, 1f, 1f, 0.95f);
        [Tooltip("Colour of the thin dark edge drawn around every part of the crosshair, so it stays readable on a bright floor " +
                 "as well as a dark one.")]
        public Color crosshairOutlineColor = new Color(0f, 0f, 0f, 0.8f);
        [Tooltip("How thick that dark edge is, in canvas units. 0 draws no edge.")]
        public float crosshairOutlineWidth = 1.5f;
        [Tooltip("Width of the small hexagon at the exact point you aim at, corner to corner, in canvas units.")]
        public float crosshairCentreSize = 9f;
        [Tooltip("Line thickness of that centre hexagon, in canvas units.")]
        public float crosshairCentreLineWidth = 2f;
        [Tooltip("Line thickness of the spread hexagon around the centre, in canvas units. It stays this thick however wide the " +
                 "hexagon opens.")]
        public float crosshairRingLineWidth = 2.5f;
        [Tooltip("Gap from the centre to the inside of the spread hexagon's sides when the gun would fire dead straight, in canvas " +
                 "units. Keep it larger than half the Charge Hex Size, or the spread hexagon sits on the charge hexagon.")]
        public float crosshairRestRadius = 20f;
        [Tooltip("How far the spread hexagon's sides move out for every degree a shot can stray from the aim, in canvas units. The " +
                 "stray comes from the gun itself (its spread, its bloom from firing, moving or standing still), so a bigger number " +
                 "makes the same bloom read larger on screen.")]
        public float crosshairUnitsPerDegree = 5f;
        [Tooltip("The farthest the spread hexagon's sides ever move out from the centre, in canvas units, however wide the gun's " +
                 "spread is.")]
        public float crosshairMaxRadius = 120f;
        [Tooltip("How quickly the spread hexagon follows a change in spread, in seconds: about how long it takes to get most of the " +
                 "way. 0 snaps it at once; a little smooths a shot's bloom so the opening reads as a movement.")]
        public float crosshairRadiusSmoothSeconds = 0.05f;
        [Tooltip("How fast the spread hexagon turns while you are not shooting, in degrees per second (clockwise). 0 holds it still " +
                 "until you fire.")]
        public float crosshairSpinIdleDegreesPerSecond = 30f;
        [Tooltip("How much faster the spread hexagon turns for every shot per second you are firing, in degrees per second. A gun " +
                 "that fires ten times a second turns ten times this much faster than at rest.")]
        public float crosshairSpinDegreesPerShot = 45f;
        [Tooltip("The fastest the spread hexagon ever turns, in degrees per second, however fast the gun fires.")]
        public float crosshairSpinMaxDegreesPerSecond = 600f;
        [Tooltip("Over about how many seconds the spin counts your shots, which is also about how long it takes to slow back " +
                 "down after you stop. Shorter makes every single shot kick the spin; longer makes it build up and ease off slowly.")]
        public float crosshairShotRateWindowSeconds = 0.5f;
        [Tooltip("Colour of the spread hexagon with no heat on the overheat meter.")]
        public Color crosshairHeatColdColor = new Color(1f, 1f, 1f, 0.95f);
        [Tooltip("Colour of the spread hexagon a third of the way to Heat Full At on the overheat meter.")]
        public Color crosshairHeatWarmColor = new Color(1f, 0.9f, 0.2f, 0.95f);
        [Tooltip("Colour of the spread hexagon two thirds of the way to Heat Full At on the overheat meter.")]
        public Color crosshairHeatHotColor = new Color(1f, 0.55f, 0.1f, 0.95f);
        [Tooltip("Colour of the spread hexagon from Heat Full At upward, and all the while you are overheated and cannot fire.")]
        public Color crosshairHeatFullColor = new Color(1f, 0.12f, 0.08f, 1f);
        [Tooltip("How full the overheat meter is, from 0 to 1, when the spread hexagon turns fully red. 0.8 matches the HUD bar's " +
                 "warning at 80 of 100 heat; lower warns earlier.")]
        [Range(0f, 1f)] public float crosshairHeatFullAt = 0.8f;
        [Tooltip("The crosshair's colour while the vent window is open, the moment pressing R vents: it replaces the overheated red so the " +
                 "window reads where the player is looking.")]
        public Color crosshairVentWindowColor = new Color(0.25f, 1f, 0.35f, 1f);
        [Tooltip("Width of the hexagon around the centre that fills while you charge a charging gun, corner to corner, in canvas units.")]
        public float crosshairChargeHexSize = 30f;
        [Tooltip("Line thickness of the charge hexagon, in canvas units.")]
        public float crosshairChargeLineWidth = 3f;
        [Tooltip("Colour of the faint full charge hexagon behind the fill, so how full it is reads like a meter.")]
        public Color crosshairChargeTrackColor = new Color(1f, 1f, 1f, 0.18f);
        [Tooltip("Colour of the charge hexagon's fill while it is still filling.")]
        public Color crosshairChargeFillColor = new Color(1f, 0.82f, 0.2f, 0.95f);
        [Tooltip("Colour the charge hexagon switches to the moment the charge is full.")]
        public Color crosshairChargeFullColor = new Color(1f, 0.95f, 0.6f, 1f);

        [Header("Debug log (F1)")]
        // These four are in SCREEN PIXELS, not canvas units, and deliberately so: the debug log is IMGUI, which
        // has no CanvasScaler to scale anything for it. Everything else on this asset is in canvas units.
        [Tooltip("Width of the F1 debug log panel, in SCREEN PIXELS (the log is IMGUI - it has no canvas, so it " +
                 "does not scale with the rest of the UI). It shrinks on a screen too narrow to hold it.")]
        public float debugLogWidthPixels = 420f;
        [Tooltip("The tallest the F1 debug log may get, as a fraction of the screen height. It is shortened " +
                 "further if there is not that much room left under the minimap.")]
        [Range(0.1f, 1f)] public float debugLogMaxHeightFraction = 0.45f;
        [Tooltip("Gap between the F1 debug log and the edges of the screen, in SCREEN PIXELS.")]
        public float debugLogScreenMarginPixels = 8f;
        [Tooltip("Gap between the bottom of the corner minimap and the top of the F1 debug log, in SCREEN PIXELS. The " +
                 "gap is measured against the CORNER map's reserved space even while the large map is open, so the " +
                 "log does not jump every time someone presses M.")]
        public float debugLogGapBelowMinimapPixels = 6f;

        [Header("Match start (2.7b)")]
        [Tooltip("Only the colour the out-of-play paint path would use on a zone's ring and minimap bubble - and " +
                 "nothing shows that path in play any more: an out-of-play zone (a host start's third capital, or any " +
                 "zone the phase-two cut closes) disappears entirely - tower, ring and minimap bubble - instead of " +
                 "being painted this colour. Kept for whichever paint path might use it again; not currently seen.")]
        public Color outOfPlayZoneColor = new Color(0.12f, 0.12f, 0.12f, 0.45f);
        [Header("Phase two cut (a team knocked out)")]
        [Tooltip("The minimap's shade over the part of the arena a knockout closed (behind the phase-two wall). Dark and " +
                 "mostly opaque, so it reads as gone, not as ground you can take.")]
        public Color minimapCutAreaColor = new Color(0.05f, 0.05f, 0.06f, 0.75f);
        [Tooltip("The phase-two wall's line on the minimap.")]
        public Color minimapCutWallColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        [Tooltip("How thick the phase-two wall's line is drawn on the minimap, in the same canvas units as the link " +
                 "widths (the real wall is under a metre - about one unit, too thin to see).")]
        public float minimapCutWallWidth = 3f;
        [Tooltip("The first line of the warm-up bar while the countdown counts down to going live. {0} is the whole seconds left, on this " +
                 "client's own synced server clock - it MUST stay in the text, or the number never shows. The countdown's " +
                 "own length is GameplayConfig > Match Start Countdown Seconds.")]
        public string matchCountdownText = "MATCH STARTS IN {0}";
        [Tooltip("Gap from the top of the screen to the top of the warm-up bar, in reference pixels. It must clear " +
                 "a two-line toast above it (the longest a warm-up can raise is the 'capital under attack' respawn " +
                 "toast), or the two run into each other.")]
        public float warmupTopOffset = 130f;
        [Tooltip("Toast shown the instant the match goes live with all three teams - the ordinary case.")]
        public string matchLiveToastText = "The match is live! Zones, credits, loadouts and respawn timers are reset.";
        [Tooltip("Toast shown the instant a host-started match goes live with two teams - lose your base and your " +
                 "team can't respawn until it retakes one. Two lines at most (about 52 characters), as the bounty " +
                 "toast.")]
        public string matchLiveTwoTeamsToastText = "Two teams: lose your base and you can't respawn";

        [Header("Damage numbers")]
        [Tooltip("Pop a number beside an enemy each time your damage lands on them. Off hides them; nothing else about " +
                 "combat changes.")]
        public bool showDamageNumbers = true;
        [Tooltip("Canvas units, at the number's settled (post-pop) size.")]
        public float damageNumberTextSize = 30f;
        [Tooltip("An ordinary hit's number.")]
        public Color damageNumberColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("A hit that used up your mark (+50% damage) - both the bigger number and the mark diamond over " +
                 "an enemy you've marked share this colour. Keep it apart from the team colours, the reds and the golds: " +
                 "a hot pink says 'marked', never 'owned by a team'.")]
        public Color markColor = new Color(1f, 0.25f, 0.7f, 1f);
        [Tooltip("How much bigger a marked hit's number is than an ordinary one, for its whole life.")]
        public float damageNumberMarkedScale = 1.4f;
        [Tooltip("How long the number takes to rise and fade away once it starts moving - it stays put and solid " +
                 "for Damage Number Hold Seconds first, then over this many seconds it rises smoothly the whole " +
                 "way (see Damage Number Rise) while fading out starting at Damage Number Fade Start, ending " +
                 "fully gone.")]
        public float damageNumberLifetimeSeconds = 0.8f;
        [Tooltip("How long the pop (the number starting oversized and settling down) takes, in seconds.")]
        public float damageNumberPopSeconds = 0.12f;
        [Tooltip("How big a number starts, as a multiple of its settled size.")]
        public float damageNumberPopScale = 1.5f;
        [Tooltip("Tudor's override on the Mark plan: one live number per enemy, not one per hit - every new hit on " +
                 "the same enemy ADDS to its number, re-pops it and restarts this clock. This is how long the number " +
                 "stays fully solid (no rise, no fade) after the LAST hit that touched it before it starts rising and " +
                 "fading over Damage Number Lifetime Seconds above. While you keep damaging one enemy its number just " +
                 "keeps counting up in place.")]
        public float damageNumberHoldSeconds = 0.6f;
        [Tooltip("Canvas units a number floats up over its post-hold life.")]
        public float damageNumberRise = 60f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the post-hold life before a number starts to fade.")]
        public float damageNumberFadeStart = 0.55f;
        [Tooltip("Metres above a target where its number is anchored when there's no fresher local impact point to use " +
                 "instead (a burn tick, a fire field, a mine - anything not simulated on the shooter's own screen, or " +
                 "an impact older than about a second). The bar over a head sits at about 3.")]
        public float damageNumberAnchorHeight = 2f;
        [Tooltip("Canvas units the number is nudged from its anchor point - positive x to the right - so it sits " +
                 "beside the impact rather than exactly on top of it.")]
        public Vector2 damageNumberScreenOffset = new Vector2(50f, 0f);
        [Tooltip("Text shown at the impact when your own shot is blocked by the enemy's shield. " +
                 "Shooter-side only - your own screen's best guess from the enemy's replicated shield look, not a " +
                 "message from them, so it can lag or miss right at the edges of the window.")]
        public string blockedText = "Blocked";
        [Tooltip("Colour of the Blocked text above - drawn at the ordinary Damage Number Text Size, not a smaller " +
                 "one. Grey on purpose: it is a guess, not a confirmed hit.")]
        public Color blockedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        [Tooltip("The word that pops over a player whose Dominion respawn shield just stopped a hit, seen by everyone who can see that player. " +
                 "Separate from Blocked Text above, which belongs to the Invulnerability ultimate and stays as it is.")]
        public string respawnShieldBlockedText = "BLOCKED";
        [Tooltip("Colour of the respawn shield's BLOCKED word. A blue on purpose, so it never reads as the grey Invulnerability Blocked or the gold Shield.")]
        public Color respawnShieldBlockedColor = new Color(0.3098f, 0.6392f, 1f, 1f);

        [Header("Dominion sudden death")]
        [Tooltip("The red of the sudden-death circle's edge and of everything outside it, in the world and on the minimap (#E5484D).")]
        public Color suddenDeathColor = new Color(0.898f, 0.2824f, 0.302f, 1f);
        [Tooltip("How strongly the red covers the ground outside the circle in the world, from 0 (not at all) to 1 (solid). Low enough to still see the arena through it.")]
        [Range(0f, 1f)] public float suddenDeathOutsideAlpha = 0.3f;
        [Tooltip("How strongly the red covers everything outside the circle on the minimap, from 0 (not at all) to 1 (solid).")]
        [Range(0f, 1f)] public float suddenDeathMinimapOutsideAlpha = 0.45f;
        [Tooltip("How wide the circle's edge line is on the ground, in metres.")]
        public float suddenDeathRingWidthMetres = 0.6f;
        [Tooltip("How thick the circle's edge line is on the minimap, in canvas units.")]
        public float suddenDeathMinimapRingWidth = 2.5f;

        // ------------------------------------------------------------------------------------------------
        // Dominion HUD. Sizes are in reference pixels (the boards' pixels x 1.5 at 1920 x 1080), like the lobby screens.
        // ------------------------------------------------------------------------------------------------
        [Header("Dominion HUD: team colours")]
        [Tooltip("Each team's colour in the Dominion HUD (the fill of its score bar, its round-win dots, its name on the break card), in team order: orange, purple, cyan.")]
        public Color[] dominionTeamColors = { new Color(1f, 0.45f, 0.05f, 1f), new Color(0.42f, 0.247f, 0.749f, 1f), new Color(0.169f, 0.722f, 0.769f, 1f) };
        [Tooltip("Each team's lighter colour for writing its name or number on a dark card (a plain purple is too dark to read there), in team order: orange, purple, cyan.")]
        public Color[] dominionTeamTextColors = { new Color(1f, 0.5f, 0.15f, 1f), new Color(0.612f, 0.482f, 0.878f, 1f), new Color(0.31f, 0.827f, 0.871f, 1f) };
        [Tooltip("The gold of the Dominion HUD's accents: the points of the centre payout, the break countdown.")]
        public Color dominionGoldColor = new Color(0.91f, 0.725f, 0.192f, 1f);
        [Tooltip("The dim grey of an empty round-win dot on the break card and of the small labels on the Dominion cards (the score rows tint theirs with the team's colour).")]
        public Color dominionDimColor = new Color(0.557f, 0.545f, 0.522f, 1f);
        [Tooltip("The soft light grey of the small headings on the Dominion HUD and cards (ROUND 2 OF 3, ROUND 1).")]
        public Color dominionMutedColor = new Color(0.725f, 0.714f, 0.682f, 1f);

        [Header("Dominion HUD: round clock (top centre of the screen during a round)")]
        [Tooltip("How far the round clock sits below the top edge of the screen.")]
        public float dominionBarTop = 30f;
        [Tooltip("The dark box behind the round clock.")]
        public Color dominionBarFill = new Color(0.059f, 0.063f, 0.078f, 0.75f);
        [Tooltip("The rounded corners of the round clock's box.")]
        public float dominionBarRadius = 12f;
        [Tooltip("Height of the round clock's box.")]
        public float dominionBarHeight = 80f;
        [Tooltip("The slightly darker fill inside the round clock's box.")]
        public Color dominionBarClockFill = new Color(0f, 0f, 0f, 0.25f);
        [Tooltip("Width of the round clock's box in a 2v2 match.")]
        public float dominionBarClockWidth = 190f;
        [Tooltip("Width of the round clock's box in a 3v3v3 match.")]
        public float dominionBar3ClockWidth = 200f;
        [Tooltip("Thickness of the team-colour stripe along the top of the death card.")]
        public float dominionBarEdgeThickness = 6f;
        [Tooltip("Size of the round clock in a 2v2 match.")]
        public float dominionClockSize = 36f;
        [Tooltip("Size of the round clock in a 3v3v3 match.")]
        public float dominionClock3Size = 34f;
        [Tooltip("Size of the small heading over the clock (ROUND 2 OF 3).")]
        public float dominionRoundLabelSize = 18f;
        [Tooltip("Extra space between the letters of the small headings, in reference pixels.")]
        public float dominionLabelSpacing = 3f;
        [Tooltip("Width and height of one round-win dot, after each team's score bar and on the break card.")]
        public float dominionDotSize = 18f;
        [Tooltip("Space between two round-win dots.")]
        public float dominionDotGap = 9f;
        [Tooltip("Thickness of the ring of an empty round-win dot.")]
        public float dominionDotRing = 3f;
        [Tooltip("How strongly the empty round-win dots after a team's score bar show that team's colour, from 0 (not at all) to 1 (as strong as " +
                 "its name). The tint says whose row it is, which matters in sudden death, when only the dots are left on screen.")]
        [Range(0f, 1f)]
        public float dominionEmptyDotTeamAlpha = 0.5f;
        [Tooltip("What the small heading over the round clock says. {0} = the round, {1} = how many rounds there are.")]
        public string dominionRoundLabelFormat = "ROUND {0} OF {1}";
        [Tooltip("What the round clock says once the rounds are over and sudden death is on (there is no clock then).")]
        public string dominionBarSuddenText = "SUDDEN DEATH";
        [Tooltip("Size of the words in the clock slot during sudden death.")]
        public float dominionBarSuddenSize = 30f;
        [Tooltip("What the small heading over the round clock says during overtime, in place of the round number. The clock under it then counts the overtime's own minute down.")]
        public string dominionBarOvertimeText = "OVERTIME";
        [Tooltip("What flashes under the round clock, in the paid team's colour, when the centre pays out. {0} = the points, {1} = the team's name in capitals.")]
        public string dominionFlashFormat = "+{0} {1}";
        [Tooltip("Size of the centre payout flash.")]
        public float dominionFlashSize = 36f;
        [Tooltip("How long the centre payout flash stays up, in seconds.")]
        public float dominionFlashSeconds = 2.5f;
        [Tooltip("How long the centre payout flash takes to fade out at the end of its time, in seconds.")]
        [Min(0.01f)] public float dominionFlashFadeSeconds = 0.5f;
        [Tooltip("Gap between the bottom of the round clock and the centre payout flash under it.")]
        public float dominionFlashGap = 6f;
        [Tooltip("How far below the top of the clock's box the 'ROUND 2 OF 3' label sits, as a share of the box's height (0.16 = a sixth).")]
        [Range(0f, 0.5f)] public float dominionRoundLabelTopShare = 0.1f;

        [Header("Dominion HUD: break card (between rounds)")]
        [Tooltip("How far the break card sits below the top edge of the screen. It sits high so the arena stays in view; the shop (P) opens on top of it.")]
        public float dominionBreakTop = 195f;
        [Tooltip("The dark box of the break card and the sudden-death banner.")]
        public Color dominionCardFill = new Color(0.059f, 0.063f, 0.078f, 0.85f);
        [Tooltip("The rounded corners of the break card. Also the corners of the death card, the YOU WIN / YOU LOSE card and the pop-ups.")]
        public float dominionCardRadius = 15f;
        [Tooltip("Empty space inside the break card, left and right, then top and bottom. The saved-log box uses half of it.")]
        public Vector2 dominionCardPadding = new Vector2(42f, 36f);
        [Tooltip("Space between the lines of the break card.")]
        public float dominionCardGap = 18f;
        [Tooltip("Width of the break card.")]
        public float dominionBreakWidth = 860f;
        [Tooltip("Size of the small heading at the top of the break card (ROUND 1).")]
        public float dominionBreakHeaderSize = 19.5f;
        [Tooltip("Size of the big line of the break card (PURPLE WINS).")]
        public float dominionBreakHeadlineSize = 66f;
        [Tooltip("Size of the two teams' points on the break card.")]
        public float dominionBreakPointsSize = 45f;
        [Tooltip("Size of the small words on the break card (points, Round wins).")]
        public float dominionBreakSmallSize = 22f;
        [Tooltip("Size of the line saying what the next round opens.")]
        public float dominionBreakOpensSize = 24f;
        [Tooltip("Size of the PICK YOUR BUILD button's words. Also the words of every pop-up button: Escape, Connection lost, the saved-log box, the death card's Quit, YOU WIN / YOU LOSE.")]
        public float dominionBreakButtonSize = 30f;
        [Tooltip("Height of the PICK YOUR BUILD button. Also the height of every pop-up button: Escape, Connection lost, the saved-log box, the death card's Quit, YOU WIN / YOU LOSE.")]
        public float dominionBreakButtonHeight = 72f;
        [Tooltip("Space left and right of the words inside the PICK YOUR BUILD button. Also inside the pop-ups' buttons (Escape, Connection lost, the saved-log box, the death card's Quit).")]
        public float dominionBreakButtonPadding = 42f;
        [Tooltip("Size of the countdown line at the bottom of the break card (ROUND 2 STARTS IN 14).")]
        public float dominionBreakCountdownSize = 33f;
        [Tooltip("Size of the countdown line in the card for the last seconds of the break, when it grows (the number of seconds is Break Countdown Seconds in the Dominion Config).")]
        public float dominionBreakBigSize = 84f;
        [Tooltip("The small heading of the break card. {0} = the round that just ended (or the round about to start, before round 1).")]
        public string dominionBreakHeaderFormat = "ROUND {0}";
        [Tooltip("The big line of the break card when one team won the round. {0} = the team's name in capitals.")]
        public string dominionBreakWinsFormat = "{0} WINS";
        [Tooltip("The big line of the break card when the round was tied (nobody gets a round win).")]
        public string dominionBreakTiedText = "TIED";
        [Tooltip("The big line of the break card when an overtime ran out and the round was shared.")]
        public string dominionBreakSharedText = "SHARED ROUND";
        [Tooltip("The line under the shared-round headline. {0} = the names of the teams that share the round, each in its colour (ORANGE and PURPLE, or ORANGE, PURPLE and CYAN).")]
        public string dominionBreakSharedLineFormat = "{0} each get a round win";
        [Tooltip("The small line under \"<TEAM> WINS\" on the break card when the round was shared at match point and its sudden death decided it.")]
        public string dominionBreakSuddenDeathLine = "won the round in sudden death";
        [Tooltip("Font size of the line that names the teams sharing the round (also the line that says a round was won in sudden death).")]
        public float dominionBreakSharedLineSize = 28f;
        [Tooltip("What sits between team names in the break card's lists (ORANGE, PURPLE) - also between the teams on match point.")]
        public string dominionBreakNamesSeparator = ", ";
        [Tooltip("What sits before the last team name in the shared-round line (ORANGE and PURPLE).")]
        public string dominionBreakNamesLast = " and ";
        [Tooltip("What sits between the teams' round wins in the match score on the break card (ORANGE 1 - 1 PURPLE).")]
        public string dominionBreakScoreDash = " - ";
        [Tooltip("Font size of the match score on the break card (shown from round 2 on).")]
        public float dominionBreakScoreSize = 45f;
        [Tooltip("The line for teams one round win away from the match. {0} = their names in their colours, joined by the Names Separator.")]
        public string dominionBreakMatchPointFormat = "MATCH POINT: {0}";
        [Tooltip("Font size of the match point line.")]
        public float dominionBreakMatchPointSize = 30f;
        [Tooltip("How long, in seconds, the newest round-win dot of each team that shared the round pulses when the break card appears.")]
        public float dominionBreakPulseSeconds = 0.6f;
        [Tooltip("How much bigger than normal that dot grows at the height of its pulse (1 = no pulse, 2 = twice the size).")]
        public float dominionBreakPulseScale = 1.7f;
        [Tooltip("The big line of the break before round 1, when there is no result yet.")]
        public string dominionBreakFirstText = "GET READY";
        [Tooltip("The word between the two teams' points on the break card.")]
        public string dominionBreakPointsLabel = "points";
        [Tooltip("Gap between the teams' points on the break card.")]
        public float dominionBreakPointsGap = 28f;
        [Tooltip("Size of the small word 'points' on the break card (between the two scores in 2v2, under the three scores in 3v3v3).")]
        public float dominionBreakPointsLabelSize = 24f;
        [Tooltip("Gap between the round-win dots and the labels around them on the break card.")]
        public float dominionBreakWinsGap = 8f;
        [Tooltip("The small mark between one team's round-win dots and the next team's.")]
        public string dominionBreakWinsSeparator = "·";
        [Tooltip("How thick the thin line is that divides the result of the round from what the next round opens, on the break card.")]
        public float dominionBreakDividerThickness = 2f;
        [Tooltip("The label in front of the round-win dots on the break card.")]
        public string dominionBreakWinsLabel = "Round wins";
        [Tooltip("The button that points to the shop on the break card. The shop opens on P; clicking this opens it too.")]
        public string dominionBreakPickText = "PICK YOUR BUILD (P)";
        [Tooltip("The small countdown line at the bottom of the break card. {0} = the round that starts next, {1} = seconds left.")]
        public string dominionBreakStartsFormat = "ROUND {0} STARTS IN {1}";
        [Tooltip("The big countdown for the last seconds of the break. {0} = the round that starts next, {1} = seconds left.")]
        public string dominionBreakBigFormat = "Round {0} starts in {1}…";
        [Tooltip("The line saying what the next round opens in the shop. {0} = the round, {1} = what it opens (the next lines).")]
        public string dominionOpensFormat = "Round {0} opens: {1}";
        [Tooltip("What the break before round 1 says instead: round 1 opens no weapon or armour tier, only the free abilities.")]
        public string dominionOpensFirstText = "Round 1: pick a <b>movement ability</b>, an <b>attachment</b> and an <b>ultimate</b>";
        [Tooltip("What a round opens when it makes the shop's weapon tree one step deeper (a weapon family).")]
        public string dominionOpensWeaponFamily = "a weapon family";
        [Tooltip("What a round opens when it makes the weapon tree two or more steps deep (an upgrade of the family).")]
        public string dominionOpensWeaponUpgrade = "a weapon upgrade";
        [Tooltip("What a round opens when it allows exactly one more armour upgrade.")]
        public string dominionOpensArmorOne = "one armor upgrade";
        [Tooltip("What a round opens when it allows several more armour upgrades. {0} = how many.")]
        public string dominionOpensArmorMore = "{0} armor upgrades";
        [Tooltip("The word that joins two things a round opens.")]
        public string dominionOpensAnd = " and ";
        [Tooltip("What a round says when it opens nothing new in the shop.")]
        public string dominionOpensNothing = "nothing new";

        [Header("Under the minimap: all lines")]
        [Tooltip("Gap between the bottom of the corner minimap's frame (the triangle, or the lane map's rectangle) and the line under it (the scan countdown in Conquest, the centre " +
                 "countdown and the circle countdown in Dominion), in canvas units. Smaller tucks the line closer to the map.")]
        public float minimapLinesGap = 6f;
        [Tooltip("Font of the lines under the corner minimap. Leave it empty to use the HUD font (Text > Font), the one the minimap's own " +
                 "labels and the ability bar use; set the display font here to match Dominion's round bar instead.")]
        public TMP_FontAsset minimapLinesFont;

        [Header("Dominion HUD: under the minimap")]
        [Tooltip("Size of the centre countdown line (IV zone +200 in 12s). The gap under the map and the font of these lines are set above, under 'Under the minimap: all lines'.")]
        public float dominionCentreSize = 19.5f;
        [Tooltip("Size of the line saying who holds the centre.")]
        public float dominionCentreHolderSize = 13.65f;
        [Tooltip("The centre countdown under the minimap in a 3v3v3 match, read as \"IV zone +200 in 12 seconds\". {0} = the points the centre pays, " +
                 "{1} = seconds to the payout. The tags colour the points gold. IV is the name the minimap's bubble gives the centre zone.")]
        public string dominionCentreFormat = "IV zone <color=#E8B931>+{0}</color> in {1} seconds";
        [Tooltip("Who holds the centre. {0} = the team's name.")]
        public string dominionCentreHoldsFormat = "{0} holds it";
        [Tooltip("What the line says when nobody holds the centre.")]
        public string dominionCentreNobodyText = "Nobody holds it";
        [Tooltip("The line under the minimap while the sudden-death circle is still shrinking. {0} = the time left (m:ss).")]
        public string dominionShrinkFormat = "CIRCLE SHRINKS · {0}";
        [Tooltip("Size of the circle countdown line.")]
        public float dominionShrinkSize = 27f;

        [Header("Dominion HUD: score bars (top right, left of the minimap, during a round and overtime)")]
        [Tooltip("How long each team's score bar is, not counting the little box with its number in front of it.")]
        public float dominionScoreBarWidth = 255f;
        [Tooltip("How tall each team's score bar is.")]
        public float dominionScoreBarHeight = 33f;
        [Tooltip("The gap between one team's score bar and the next.")]
        public float dominionScoreBarGap = 9f;
        [Tooltip("The width of the little box in front of each bar that holds the team's points as a number.")]
        public float dominionScoreBarNumberWidth = 75f;
        [Tooltip("The size of the points number in that box. Small, so it never fights the round clock.")]
        public float dominionScoreBarNumberSize = 24f;
        [Tooltip("How far below the top edge of the screen the first score bar starts.")]
        public float dominionScoreBarsTop = 30f;
        [Tooltip("The gap between the score bars' right end (the last round-win dot) and the left edge of the corner minimap.")]
        public float dominionScoreBarsMinimapGap = 24f;
        [Tooltip("The gap between the end of a team's score bar and its round-win dots. The dots' size and spacing are the round clock's Dominion Dot fields.")]
        public float dominionScoreDotsGap = 9f;
        [Tooltip("The dark colour behind each score bar and behind its number. The team's colour fills it from the left.")]
        public Color dominionScoreBarTrackColor = new Color(0.059f, 0.063f, 0.078f, 0.75f);

        [Header("Dominion HUD: sudden-death banner")]
        [Tooltip("How far the banner sits below the top edge of the screen (under the round bar).")]
        public float dominionBannerTop = 175f;
        [Tooltip("How many seconds the full banner stays up after sudden death starts; then it shrinks to the small line under the round bar for the rest of the match. A restarted sudden death shows the full banner again.")]
        public float dominionBannerSeconds = 8f;
        [Tooltip("Width of the full sudden-death banner.")]
        public float dominionBannerWidth = 900f;
        [Tooltip("The big words of the sudden-death banner.")]
        public string dominionBannerTitle = "SUDDEN DEATH";
        [Tooltip("The rules line under the banner's big words.")]
        public string dominionBannerRules = "No respawns · stay inside the circle · last team standing wins";
        [Tooltip("Size of the banner's big words.")]
        public float dominionBannerTitleSize = 51f;
        [Tooltip("Size of the banner's rules line.")]
        public float dominionBannerRulesSize = 21f;
        [Tooltip("Size of the small SUDDEN DEATH line that stays under the round bar after the banner.")]
        public float dominionBannerSmallSize = 27f;
        [Tooltip("Extra height of the full banner beyond its two lines of writing (the room above, between and below them).")]
        public float dominionBannerExtraHeight = 30f;
        [Tooltip("How far below the top of the full banner its big words start.")]
        public float dominionBannerTitleTop = 12f;
        [Tooltip("Extra height of the small SUDDEN DEATH line's box beyond its writing.")]
        public float dominionBannerSmallExtraHeight = 12f;

        [Header("Dominion HUD: result")]
        [Tooltip("The small heading of the result card. {0} = the match size (2v2 or 3v3v3).")]
        public string dominionResultModeFormat = "DOMINION {0}";
        [Tooltip("The big line of the result card. {0} = the winner's name in capitals, {1} = the round wins (2–1, or 2–1–0 with three teams).")]
        public string dominionResultHeadlineFormat = "{0} WINS {1}";
        [Tooltip("The big line when the match was settled by sudden death. {0} = the winner's name in capitals.")]
        public string dominionResultSuddenFormat = "{0} WINS IN SUDDEN DEATH";
        [Tooltip("What goes between the round wins in the big line.")]
        public string dominionResultScoreSeparator = "–";
        [Tooltip("The heading of a column of the result table. {0} = the round.")]
        public string dominionResultRoundFormat = "Round {0}";
        [Tooltip("The small note under the result table.")]
        public string dominionResultNote = "The winner of each round in bold. Tab still shows kills and damage.";
        [Tooltip("Width of the result card.")]
        public float dominionResultWidth = 960f;
        [Tooltip("How far above the middle of the screen the result card sits, in reference pixels (the HUD's 1920x1080 grid). Raised so the card " +
                 "never covers the 'your match log is saved' box in the bottom left. Lower it and the two overlap.")]
        [Min(0f)] public float dominionResultRaise = 150f;
        [Tooltip("Size of the big line of the result card.")]
        public float dominionResultHeadlineSize = 84f;
        [Tooltip("Size of the small heading of the result card.")]
        public float dominionResultModeSize = 19.5f;
        [Tooltip("Size of the writing in the result table.")]
        public float dominionResultTableSize = 24f;
        [Tooltip("Height of one row of the result table.")]
        public float dominionResultRowHeight = 42f;
        [Tooltip("Width of the team-name column of the result table.")]
        public float dominionResultNameWidth = 180f;
        [Tooltip("Width of one round's column of the result table.")]
        public float dominionResultCellWidth = 150f;
        [Tooltip("Gap between the cells of a row of the result table.")]
        public float dominionResultRowGap = 16f;
        [Tooltip("Size of the small note under the result table.")]
        public float dominionResultNoteSize = 21f;
        [Tooltip("Which sorting order the result card draws at: above the match panels, under the saved-log box.")]
        public int dominionResultSortingOrder = UiLayers.DominionResultDefault;

        [Header("Mark")]
        [Tooltip("Canvas units, the mark diamond's width and height (both the shooter's own diamond over an " +
                 "enemy, and the marked player's own diamond over their own head - Tudor's answer 1). Its colour " +
                 "is Mark Colour above, shared with a marked hit's own damage number so the two teach each other.")]
        public float markIndicatorSize = 22f;
        [Tooltip("Metres above a player the mark diamond sits at - just above the bar over their head. The same " +
                 "value anchors both diamonds: the shooter's own, over the enemy they marked, and the marked " +
                 "player's own, over their own head (Tudor's answer 1) - one number, so retuning it moves both.")]
        public float markIndicatorAnchorHeight = 3.6f;
        [Tooltip("Pulses per second while a mark is live; 0 = steady (no pulsing at all, just the plain fade as " +
                 "the mark runs out).")]
        public float markIndicatorPulseSpeed = 2.5f;
        [Range(0f, 1f)]
        [Tooltip("How faint the diamond gets as the mark runs low, or at the pulse's own dimmest point; 1 turns " +
                 "off the fade and the pulse both (always full strength while the mark is live at all).")]
        public float markIndicatorMinAlpha = 0.35f;
        [Tooltip("How many sharp red spikes ring an enemy your Raybeam has left vulnerable. The ring sits at the same spot as the Mark gun's " +
                 "pink diamond and leaves a hole for it, so an enemy carrying both shows the diamond inside the spikes. Its colour is Danger " +
                 "Colour, the Raybeam's own red; it fades and pulses like the diamond.")]
        [Min(3)]
        public int raybeamMarkSpikeCount = 8;
        [Tooltip("Canvas units, how far each red Raybeam spike reaches out from the ring it stands on. Longer reads spikier and bigger.")]
        public float raybeamMarkSpikeLength = 15f;
        [Range(0.1f, 1f)]
        [Tooltip("How broad each Raybeam spike's base is, as a share of its slice of the ring: 1 makes the bases touch in a closed star, " +
                 "lower makes thinner, sharper spikes with gaps between them.")]
        public float raybeamMarkSpikeWidth = 0.75f;
        [Tooltip("Canvas units of clear space between the points of the pink mark diamond and the bases of the red Raybeam spikes, so " +
                 "on an enemy carrying both marks the two never touch. The ring's hole grows with Mark Indicator Size by itself.")]
        public float raybeamMarkGap = 2f;

        [Header("Towers (arena rebuild)")]
        [Tooltip("Colour of a tower's crown, base ring and column caps while nobody owns its zone. Owned towers use their team's " +
                 "colour (Team Shot Colors), a capital cut from a two-team match uses Out Of Play Zone Colour, and an " +
                 "attacked or drained tower pulses exactly like its ring on the ground. Keep it a plain mid grey: " +
                 "grey and white mean nobody, so a neutral tower never reads as owned.")]
        public Color towerNeutralColor = new Color(0.42f, 0.42f, 0.42f, 1f);
        [Tooltip("How dark a Dominion spawn tower's body is against its team colour: the body is painted with the " +
                 "team colour times this, so the spawn keeps a shaded silhouette under its glowing lid, base and " +
                 "columns. 1 would make the body as flat and bright as the rest. Capture towers do not use it: their " +
                 "body is the dark arena wall look (Arena Look, Tower Body).")]
        [Range(0f, 1f)] public float towerBodyShade = 0.8f;
        [Tooltip("How brightly an OWNED tower's crown, base ring and column caps glow in the owner's colour (on a " +
                 "Dominion spawn, its columns too). 1 = flat colour, no glow; 1.3 = soft; 1.5 = clear glow; 1.75+ = " +
                 "strong. On a capture tower only the colour's brightest part is pushed this far, so purple stays the " +
                 "violet of its stripes; on a Dominion spawn, past about 1.75 violet starts turning lilac. Neutral and " +
                 "out-of-play towers never glow, so a glow means owned. The body never glows.")]
        [Range(1f, 2.5f)] public float towerOwnerGlow = 1.5f;

        [Header("Spawn towers (Dominion)")]
        [Tooltip("Half-width of a spawn tower's six-sided body, measured to its corners, in metres. Today's round capital body is 2. It is cut down to the tower's solid footprint (2.6 m), so the body can never be wider than what players can already hit and stand against.")]
        [Min(0.5f)] public float spawnTowerBodyRadius = 2f;
        [Tooltip("Half-width of a spawn tower's six-sided base, measured to its corners, in metres. Today's round capital base is 2.6, the full footprint, and it is never allowed past it.")]
        [Min(0.5f)] public float spawnTowerPlinthRadius = 2.6f;
        [Tooltip("Half-width of a spawn tower's six-sided lid, measured to its corners, in metres. Today's round capital lid is 2.35. It is never allowed past the tower's footprint (2.6 m).")]
        [Min(0.5f)] public float spawnTowerLidRadius = 2.35f;
        [Tooltip("How thick a spawn tower's lid is, in metres. The lid sits on top of the body, so a thicker lid makes the tower taller (the columns stay as tall as on every other tower). Today's capital lid is 0.35.")]
        [Min(0.05f)] public float spawnTowerLidHeight = 0.35f;
        [Tooltip("How many columns a spawn tower has, from 1 to 6. 6 puts one on every corner of the body; fewer are spread evenly round it, starting at the front.")]
        [Range(1, 6)] public int spawnTowerColumnCount = 6;
        [Tooltip("Width of each spawn tower column, in metres (half-width). The column cap is drawn wider in the same proportion as on the zone towers. A column is pulled in when its cap would stick out past the tower's footprint (2.6 m).")]
        [Min(0.05f)] public float spawnTowerColumnRadius = 0.35f;

        [Header("Playtest extras")]
        [Tooltip("HUD toast shown on Ctrl+B - 'a bug just happened' (BugMarkerKey): a screenshot is " +
                 "saved next to this client's own telemetry log, and its own `bug` line is written. " +
                 "Uses the same transient toast label every other HUD toast shares (PlayerHud.ShowToast).")]
        public string bugMarkedText = "Bug marked - type what happened in chat";
        [Tooltip("Escape's pop-up question outside a match on a desktop build (the name screen, the lobby list, a lobby before " +
                 "its start). Yes closes the game. The browser build never offers it: a web page cannot close itself.")]
        public string quitPromptText = "Close the game?";
        [Tooltip("The question on the second card, after Leave match in the menu during a match (from the warm-up on). Yes, leave " +
                 "takes this player back to the lobby list; the match carries on for everyone else and the seat is freed.")]
        public string leaveMatchPromptText = "Leave the match?";
        [Tooltip("Escape's pop-up title in the browser outside a match, where there is nothing to quit: the pop-up then only " +
                 "holds the master volume and a close button.")]
        public string escapeMenuTitleText = "Sound";
        [Tooltip("The close button of that browser pop-up and of the menu in a match (same effect as pressing Escape again).")]
        public string escapeMenuCloseText = "CLOSE";
        [Tooltip("Title of the menu Escape opens during a match (from the warm-up on): the Sound section, a line, then Leave match and Close.")]
        public string escapeMenuMatchTitleText = "MENU";
        [Tooltip("Heading of the Sound section above the master volume in the menu during a match.")]
        public string escapeMenuSoundHeaderText = "SOUND";
        [Tooltip("The menu's leave button during a match. It does not leave at once: it opens the second card (Leave Match Prompt Text and " +
                 "Leave Match Rejoin Note Text) with Yes, leave and Stay.")]
        public string escapeMenuLeaveMatchText = "LEAVE MATCH";
        [Tooltip("Fill colour of the menu's Leave match button, so it reads as the one that takes you out.")]
        public Color escapeMenuLeaveButtonColor = new Color(0.78f, 0.2f, 0.2f, 1f);
        [Tooltip("The line under \"Leave the match?\" on the second card: what a player who leaves can still do.")]
        public string leaveMatchRejoinNoteText = "You can rejoin from the list while the match is running.";
        [Tooltip("The second card's button that really leaves: back to the lobby list, the match carries on for everyone else.")]
        public string leaveMatchYesText = "YES, LEAVE";
        [Tooltip("The second card's button that closes both cards and goes back to the match (same effect as pressing Escape).")]
        public string leaveMatchStayText = "STAY";
        [Tooltip("One muted line under the name screen's buttons, shown only when this build sends match logs " +
                 "(TelemetryConfig > Upload has an address): it tells players that match data, with their player name, is collected.")]
        public string matchDataNoticeText = "Match data, including your player name, is collected to help improve the game.";
        [Tooltip("Escape pop-up's confirm button, for both questions: it closes the game, or during a match leaves it.")]
        public string quitYesText = "Yes";
        [Tooltip("QuitConfirmPanel's cancel button - same effect as pressing Escape again.")]
        public string quitNoText = "No";
        [Tooltip("MatchLogZip's saved-log overlay, shown once this client's own match log has been " +
                 "zipped (the win/lose panel, and again on quit if that had not already happened). " +
                 "{0} is filled in with the match log folder's full path (inside the Match logs folder) and {1} with the zip file's full path. " +
                 "Each new line of the text is a new line in the box.")]
        [TextArea(2, 5)]
        public string matchLogSavedText = "Your match log is saved.\nSaved in: {0}\nSend this zip to Tudor: {1}";
        [Tooltip("MatchLogZip's saved-log overlay button - Application.OpenURL of the match folder.")]
        public string openLogFolderText = "Open folder";
        [Tooltip("Where the saved-log overlay sits: how far from the left edge and from the bottom edge of the screen, in reference pixels. " +
                 "The bottom left keeps it clear of the YOU WIN / YOU LOSE title and the result button.")]
        public Vector2 matchLogSavedOffset = new Vector2(24f, 70f);
        [Tooltip("Width of the saved-log overlay, in reference pixels. Narrow enough to stay clear of the ability slots.")]
        public float matchLogSavedWidth = 640f;

        [Header("Scoreboard (hold Tab)")]
        [Tooltip("Width of the Tab scoreboard, in reference pixels (the HUD's 1920x1080 grid).")]
        public float scoreboardWidth = 1100f;
        [Tooltip("Height of every scoreboard line: a team's title, the column headings and each player.")]
        public float scoreboardRowHeight = 42f;
        [Tooltip("Text size of a player's line on the scoreboard.")]
        public float scoreboardTextSize = 26f;
        [Tooltip("Text size of the scoreboard's title, its column headings and each team's name.")]
        public float scoreboardHeadingTextSize = 28f;
        [Tooltip("Colour of the dark slab behind the scoreboard. Keep it fairly see-through so the arena still shows.")]
        public Color scoreboardPanelColor = new Color(0.04f, 0.04f, 0.06f, 0.88f);
        [Tooltip("Colour of the strip behind YOUR OWN line, so you find yourself at a glance.")]
        public Color scoreboardSelfRowColor = new Color(1f, 1f, 1f, 0.16f);
        [Tooltip("How many times a second the scoreboard re-reads everyone's numbers while Tab is held. It does " +
                 "not read anything while closed.")]
        [Min(1f)] public float scoreboardRefreshesPerSecond = 5f;
        [Tooltip("Where the scoreboard's six columns (name, kills, deaths, assists, damage, zones) start and end, as " +
                 "fractions of its width from the left: 7 numbers, column 1 runs from the first to the second, " +
                 "column 2 from the second to the third, and so on. Keep them rising.")]
        public float[] scoreboardColumnEdges = { 0.02f, 0.42f, 0.53f, 0.64f, 0.75f, 0.88f, 0.99f };
        [Tooltip("Draw order of the scoreboard against other screen panels: higher is on top. The HUD is at -10.")]
        public int scoreboardSortingOrder = UiLayers.ScoreboardDefault;
        [Tooltip("Empty space, in reference pixels, between the scoreboard's left and right edges and its text.")]
        public int scoreboardPaddingHorizontal = 12;
        [Tooltip("Empty space, in reference pixels, above and below the scoreboard's lines.")]
        public int scoreboardPaddingVertical = 11;
        [Tooltip("Gap, in reference pixels, between two scoreboard lines.")]
        public float scoreboardLineSpacing = 2f;
        [Tooltip("How much bigger the scoreboard's title is than its headings, in text size points.")]
        public float scoreboardTitleExtraSize = 6f;
        [Tooltip("The scoreboard's title.")]
        public string scoreboardTitleText = "Scoreboard";
        [Tooltip("The column headings, in order: player name, kills, deaths, assists, damage dealt, zones captured.")]
        public string[] scoreboardColumnTexts = { "Player", "Kills", "Deaths", "Assists", "Damage", "Zones" };
        [Tooltip("Each team's name on the scoreboard, in team order (Orange, Purple, Cyan). Each one is drawn in that " +
                 "team's Team Shot Colour.")]
        public string[] scoreboardTeamNames = { "Orange", "Purple", "Cyan" };
        [Tooltip("Shown for a team the scoreboard cannot name (a player whose team has not arrived yet).")]
        public string scoreboardUnknownTeamText = "Joining";

        [Header("Connection lost and rejoin")]
        [Tooltip("Title of the panel that appears when your connection to the match drops (ConnectionLostPanel).")]
        public string connectionLostTitle = "Connection lost";
        [Tooltip("The line under the title: what Rejoin does for you.")]
        public string connectionLostBody = "Rejoin to come back to the same match as the same player - same team, credits and loadout. You respawn as after a death.";
        [Tooltip("The button that reconnects and puts you back in the same match.")]
        public string connectionLostRejoinButton = "Rejoin";
        [Tooltip("The button that gives up your place and goes back to the name screen to join a new match.")]
        public string connectionLostLeaveButton = "Leave";
        [Tooltip("Shown on the panel while the reconnect is running.")]
        public string rejoinWorkingText = "Reconnecting...";
        [Tooltip("Shown when the rejoin cannot happen: the match is over, or the two minutes ran out and your place was given up.")]
        public string rejoinFailedText = "The match has ended, or your place in it was given up. You can join a new match.";
        [Tooltip("Shown when a new join is refused because your own dropped place is still held in a match that cannot be rejoined from here.")]
        public string rejoinPlaceHeldText = "You still hold a place in a match you dropped out of, but it could not be reached. Try again in a minute, or join a new match once it is given up.";
        [Tooltip("The button on the failure message that goes back to the name screen.")]
        public string rejoinFailedOkButton = "OK";
        [Tooltip("The name screen's button, shown while a match you dropped out of is still holding your place.")]
        public string rejoinMatchButton = "Rejoin your match";
        [Tooltip("Label of the result screen's button once the match is really over: it leads back to the lobby list.")]
        [UnityEngine.Serialization.FormerlySerializedAs("resultButtonMainMenu")]
        public string resultButtonLobbyList = "Back to the lobby list";
        [Tooltip("Label of the same button while the match is still running (a knocked-out player's lose screen, the waiting card, " +
                 "the death card): it takes this player back to the lobby list and the match carries on for the others.")]
        public string resultButtonLeaveMatch = "Leave match";
        [Tooltip("Width of the connection lost panel, in reference pixels.")]
        public float connectionLostPanelWidth = 640f;
        [Tooltip("Colour of the connection lost title.")]
        public Color connectionLostTitleColor = new Color(1f, 0.55f, 0.35f, 1f);
        [Tooltip("Text size of the connection lost title, in reference pixels.")]
        [Min(8f)] public float connectionLostTitleSize = 34f;
        [Tooltip("Text size of the line under the title, in reference pixels.")]
        [Min(8f)] public float connectionLostBodySize = 22f;
        [Tooltip("Size of the name screen's Rejoin your match button, in reference pixels. Wider than the panel's buttons: the " +
                 "name screen's font is wide and would wrap the label.")]
        public Vector2 rejoinMatchButtonSize = new Vector2(420f, 52f);
        [Tooltip("Distance from the top of the name box to the bottom of the Rejoin your match button, in reference pixels.")]
        public float rejoinButtonGapAboveNameBox = 16f;

        /// <summary>Writes this theme's outline, weight and drop-shadow onto one shared TextMeshPro material -
        /// the one home for those seven numbers, called by PlayerHud, the loadout screen and the minimap, which
        /// each build one material for every label they own (see PlayerHud.ApplyOutline for why one shared
        /// material beats letting TMP clone one per label).
        ///
        /// Trap: setting _UnderlayColor and friends does nothing until UNDERLAY_ON is enabled on the material,
        /// so the shadow silently never appears.</summary>
        public void ApplyHudTextStyle(Material material)
        {
            if (material == null)
                return;

            material.SetFloat(ShaderUtilities.ID_OutlineWidth, textOutlineWidth);
            material.SetColor(ShaderUtilities.ID_OutlineColor, textOutlineColor);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, hudTextFaceDilate);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, hudTextShadowColor);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, hudTextShadowOffset.x);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, hudTextShadowOffset.y);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, hudTextShadowSoftness);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, hudTextShadowDilate);
            if (hudTextShadowColor.a > 0f)
                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            else
                material.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        }

        [Header("Spectate")]
        [Tooltip("The button on a knocked-out player's lose screen that starts watching a living player.")]
        public string spectateButton = "Spectate";
        [Tooltip("The same button once you are watching: it moves to the next living player.")]
        public string spectateNextButton = "Next";
        [Tooltip("While watching, the lose panel shrinks to a strip holding Next and Quit. Its size, in reference pixels.")]
        public Vector2 spectateStripSize = new Vector2(460f, 70f);
        [Tooltip("Height of the strip's bottom edge above the bottom of the screen, in reference pixels.")]
        public float spectateStripBottom = 120f;
        [Tooltip("Distance of the Next and Quit buttons from the strip's centre, sideways, in reference pixels.")]
        public float spectateButtonOffset = 115f;

        [Header("Spectator bar")]
        [Tooltip("The bar at the bottom of a spectator seat's screen: who they watch and the keys. Fill colour of the bar.")]
        public Color spectatorBarColor = new Color(0.06f, 0.063f, 0.078f, 0.88f);
        [Tooltip("Empty space inside the bar, in reference pixels: left and right, then top and bottom.")]
        public Vector2 spectatorBarPadding = new Vector2(36f, 20f);
        [Tooltip("Gap between the bar's parts (who is watched, each key, Leave), in reference pixels.")]
        public float spectatorBarGap = 42f;
        [Tooltip("Distance from the bottom of the screen to the bottom of the bar, in reference pixels.")]
        public float spectatorBarBottom = 28f;
        [Tooltip("Text size of the small SPECTATING caption, in reference pixels.")]
        public float spectatorBarCaptionSize = 18f;
        [Tooltip("Colour of the SPECTATING caption.")]
        public Color spectatorBarCaptionColor = new Color(0.557f, 0.545f, 0.522f, 1f);
        [Tooltip("Text size of the watched player's name (and team), in reference pixels.")]
        public float spectatorBarNameSize = 36f;
        [Tooltip("Text size of each key's description (Previous, Next, Whole map, Zoom), in reference pixels.")]
        public float spectatorBarKeyTextSize = 22f;
        [Tooltip("Text size of the key caps (Q, E, Space, Wheel), in reference pixels.")]
        public float spectatorBarKeyCapSize = 22f;
        [Tooltip("Colour of a key cap's outline.")]
        public Color spectatorBarKeyCapColor = new Color(0.557f, 0.545f, 0.522f, 1f);
        [Tooltip("Thickness of a key cap's and the Leave button's outline, in reference pixels.")]
        public float spectatorBarOutline = 2f;
        [Tooltip("Empty space inside a key cap, left and right, then top and bottom, in reference pixels.")]
        public Vector2 spectatorBarKeyCapPadding = new Vector2(15f, 6f);
        [Tooltip("Size of the Leave button, in reference pixels.")]
        public Vector2 spectatorBarLeaveSize = new Vector2(120f, 60f);
        [Tooltip("Outline colour of the Leave button.")]
        public Color spectatorBarLeaveOutlineColor = new Color(0.29f, 0.302f, 0.341f, 1f);
        [Tooltip("The small caption above the name.")]
        public string spectatorBarCaption = "SPECTATING";
        [Tooltip("Shown instead of a player's name while the whole map is in view.")]
        public string spectatorBarWholeMapName = "Whole map";
        [Tooltip("The four keys' caps, in order: previous player, next player, whole map, zoom.")]
        public string[] spectatorBarKeyCaps = { "Q", "E", "Space", "Wheel" };
        [Tooltip("What each of the four keys does, in the same order.")]
        public string[] spectatorBarKeyTexts = { "Previous", "Next", "Whole map", "Zoom" };
        [Tooltip("The Leave button's label (back to the lobby list).")]
        public string spectatorBarLeaveText = "Leave";
        [Tooltip("The whole-map view (Space): how much of the screen's height the arena fills, from 0.3 to 1. 0.88 = 88%, the arena centred.")]
        public float spectatorWholeMapFill = 0.88f;
        [Tooltip("The angle a spectator sees the arena from, in degrees turned round the map (0 = the camera behind the bottom edge, looking up the map). It is the same whoever is watched.")]
        public float spectatorViewAngle = 0f;
        [Tooltip("The result card a spectator sees when the match ends (a spectator has no body, so no YOU WIN / YOU LOSE panel): the title, with {0} replaced by the winning team's name (team names: Scoreboard team names). The button under it reads 'Back to the lobby list' (Result button) and leads there.")]
        public string spectatorResultTitle = "{0} wins the match";
        [Tooltip("Text size of the spectator result card's title, in reference pixels.")]
        public float spectatorResultTitleSize = 56f;
        [Tooltip("Fill colour of the spectator result card. Also the fill of the death card, the YOU WIN / YOU LOSE card and the pop-ups (Escape, Connection lost, the saved-log box).")]
        public Color spectatorResultCardColor = new Color(0.06f, 0.063f, 0.078f, 0.92f);
        [Tooltip("Empty space inside the spectator result card, left and right, then top and bottom, in reference pixels. Also inside the YOU WIN / YOU LOSE card, the Escape pop-up and the Connection lost box.")]
        public Vector2 spectatorResultCardPadding = new Vector2(64f, 36f);
        [Tooltip("How far the spectator result card sits below the top of the screen, in reference pixels.")]
        public float spectatorResultCardTop = 150f;
        [Tooltip("Space between the title and the button on a result card (the spectator's match result), in reference pixels. Also between the lines of the YOU WIN / YOU LOSE card, the Escape pop-up and the Connection lost box.")]
        public float spectatorResultCardGap = 24f;
        [Tooltip("Letter spacing of the small heading above a result card's big line (and the Dominion break card's heading), as a share of the heading's size.")]
        public float resultHeadingSpacingShare = 0.23f;
        [Tooltip("How small a long big line on a fixed-width result card may shrink, as a share of its normal size (0.4 = down to 40%), before it is cut off.")]
        [Range(0.1f, 1f)] public float resultTitleMinSizeShare = 0.4f;

        [Header("Lobby screens")]
        [Tooltip("The font of the big letters on the lobby screens (title, buttons, headings): Oswald.")]
        public TMP_FontAsset lobbyDisplayFont;
        [Tooltip("The font of the ordinary text on the lobby screens: Public Sans regular.")]
        public TMP_FontAsset lobbyBodyFont;
        [Tooltip("The font of the bold text on the lobby screens (labels, lobby names, buttons): Public Sans bold.")]
        public TMP_FontAsset lobbyBoldFont;
        [Tooltip("Background of the name screen and the lobby list, and the card of the create screen.")]
        public Color lobbyDarkColor = new Color(0.102f, 0.11f, 0.133f, 1f);
        [Tooltip("Background behind the create screen's card.")]
        public Color lobbyBackdropColor = new Color(0.059f, 0.063f, 0.078f, 1f);
        [Tooltip("Fill of the name box, the lobby name box, the mode buttons and a lobby row.")]
        public Color lobbyPanelColor = new Color(0.149f, 0.161f, 0.196f, 1f);
        [Tooltip("Fill of a lobby row that is full.")]
        public Color lobbyRowFullColor = new Color(0.125f, 0.133f, 0.165f, 1f);
        [Tooltip("Outline of the boxes and of the Cancel button.")]
        public Color lobbyBorderColor = new Color(0.29f, 0.302f, 0.341f, 1f);
        [Tooltip("Outline of the create screen's card and of the team size buttons that are not chosen.")]
        public Color lobbyCardBorderColor = new Color(0.227f, 0.239f, 0.278f, 1f);
        [Tooltip("The main buttons (Find a lobby, Create lobby, Create) and the chosen mode and team size.")]
        public Color lobbyPurpleColor = new Color(0.42f, 0.247f, 0.749f, 1f);
        [Tooltip("The small PROJECT caption, the How to play button, the Rejoin outline and the In lobby status.")]
        public Color lobbyCyanColor = new Color(0.169f, 0.722f, 0.769f, 1f);
        [Tooltip("The main text colour, the title letters and the Join buttons.")]
        public Color lobbyOffWhiteColor = new Color(0.957f, 0.949f, 0.929f, 1f);
        [Tooltip("Labels and the second line of text.")]
        public Color lobbyMutedColor = new Color(0.725f, 0.714f, 0.682f, 1f);
        [Tooltip("Hints, column headings and text of a full lobby.")]
        public Color lobbyDimColor = new Color(0.557f, 0.545f, 0.522f, 1f);
        [Tooltip("The In match status.")]
        public Color lobbyYellowColor = new Color(0.91f, 0.725f, 0.192f, 1f);
        [Tooltip("Fill of a button that cannot be pressed (a full lobby's Full button, Find a lobby with a short name).")]
        public Color lobbyDisabledButtonColor = new Color(0.2f, 0.212f, 0.247f, 1f);
        [Tooltip("Text on the light buttons (Join, Spectate, How to play).")]
        public Color lobbyDarkTextColor = new Color(0.102f, 0.11f, 0.133f, 1f);
        [Tooltip("The short line that tells a lobby could not be joined or created.")]
        public Color lobbyErrorColor = new Color(0.91f, 0.725f, 0.192f, 1f);
        [Tooltip("The orange outline round the OVERPOWER letters.")]
        public Color lobbyTitleOutlineColor = new Color(0.949f, 0.549f, 0.157f, 1f);
        [Tooltip("How thick the orange outline round OVERPOWER is, 0 to 1. Around 0.1 is thin, 0.2 is bold.")]
        public float lobbyTitleOutlineWidth = 0.09f;
        [Tooltip("The material the OVERPOWER title is drawn with: the Oswald font's material with its outline switched on. The outline colour and thickness above are applied on top; this material makes sure the outline shader is in the game build.")]
        public Material lobbyTitleMaterial;
        [Tooltip("Fill of the team size that is chosen on the create screen (a see-through purple).")]
        public Color lobbyCreateSelectedFill = new Color(0.42f, 0.247f, 0.749f, 0.251f);
        [Tooltip("How round the corners of boxes and buttons are, in reference pixels.")]
        public float lobbyCornerRadius = 9f;
        [Tooltip("How round the corners of the create screen's card are, in reference pixels.")]
        public float lobbyCardRadius = 15f;
        [Tooltip("Thickness of the outline of boxes and buttons, in reference pixels.")]
        public float lobbyBorderWidth = 1.5f;
        [Tooltip("Thickness of the outline of the chosen team size, in reference pixels.")]
        public float lobbyChosenBorderWidth = 3f;
        [Tooltip("Extra space between the letters of the small capital labels (YOUR NAME, LOBBY, MODE), in reference pixels.")]
        public float lobbyLabelSpacing = 1.5f;
        [Tooltip("Extra space between the letters of the small cyan PROJECT caption, in reference pixels.")]
        public float lobbyKickerSpacing = 6f;
        [Tooltip("Extra space between the letters of the big purple buttons, in reference pixels.")]
        public float lobbyButtonSpacing = 1.5f;
        [Tooltip("Width of the name screen's column, in reference pixels.")]
        public float nameScreenWidth = 720f;
        [Tooltip("Space between the title, the name box and the buttons on the name screen, in reference pixels.")]
        public float nameScreenGap = 42f;
        [Tooltip("Space between the name box's label, the box and its hint, in reference pixels.")]
        public float nameScreenInnerGap = 12f;
        [Tooltip("Text size of PROJECT above the title, in reference pixels.")]
        public float nameScreenKickerSize = 21f;
        [Tooltip("Text size of OVERPOWER, in reference pixels.")]
        public float nameScreenTitleSize = 144f;
        [Tooltip("Text size of YOUR NAME, in reference pixels.")]
        public float nameScreenLabelSize = 19.5f;
        [Tooltip("Height of the name box, in reference pixels.")]
        public float nameScreenFieldHeight = 84f;
        [Tooltip("Text size of the typed name, in reference pixels.")]
        public float nameScreenFieldTextSize = 33f;
        [Tooltip("Text size of the hint under the name box, in reference pixels.")]
        public float nameScreenHintSize = 19.5f;
        [Tooltip("Height of Find a lobby, in reference pixels.")]
        public float nameScreenFindHeight = 90f;
        [Tooltip("Text size of Find a lobby, in reference pixels.")]
        public float nameScreenFindTextSize = 36f;
        [Tooltip("Height of Rejoin your match, in reference pixels.")]
        public float nameScreenRejoinHeight = 72f;
        [Tooltip("Text size of Rejoin your match, in reference pixels.")]
        public float nameScreenRejoinTextSize = 24f;
        [Tooltip("Empty space round the lobby list screen: left and right, then top and bottom, in reference pixels.")]
        public Vector2 lobbyListPadding = new Vector2(96f, 72f);
        [Tooltip("Space between the heading, the table and the bottom row of the lobby list, in reference pixels.")]
        public float lobbyListGap = 36f;
        [Tooltip("Text size of PROJECT OVERPOWER above the list heading, in reference pixels.")]
        public float lobbyListKickerSize = 19.5f;
        [Tooltip("Extra space between the letters of that caption, in reference pixels.")]
        public float lobbyListKickerSpacing = 4.5f;
        [Tooltip("Text size of the Lobbies heading, in reference pixels.")]
        public float lobbyListTitleSize = 72f;
        [Tooltip("Text size of Playing as <name>, in reference pixels.")]
        public float lobbyListPlayingAsSize = 22.5f;
        [Tooltip("Height of the Create lobby button, in reference pixels.")]
        public float lobbyListCreateHeight = 72f;
        [Tooltip("Empty space left and right inside the Create lobby button, in reference pixels.")]
        public float lobbyListCreatePadding = 42f;
        [Tooltip("Text size of the Create lobby button, in reference pixels.")]
        public float lobbyListCreateTextSize = 30f;
        [Tooltip("Text size of the column headings, in reference pixels.")]
        public float lobbyListHeadSize = 19.5f;
        [Tooltip("How the table's width is shared between the Lobby, Mode, Players, Status and Host columns.")]
        public float[] lobbyListColumnWeights = { 3f, 2f, 1.4f, 1.6f, 1.4f };
        [Tooltip("Width of the Join / Spectate / Full column, in reference pixels.")]
        public float lobbyListActionWidth = 180f;
        [Tooltip("Space between the table's columns, in reference pixels.")]
        public float lobbyListColumnGap = 24f;
        [Tooltip("Empty space left and right inside a lobby row, in reference pixels.")]
        public float lobbyListRowPadding = 30f;
        [Tooltip("Height of a lobby row, in reference pixels.")]
        public float lobbyListRowHeight = 96f;
        [Tooltip("Space between two lobby rows, in reference pixels.")]
        public float lobbyListRowGap = 12f;
        [Tooltip("Text size of a lobby row, in reference pixels.")]
        public float lobbyListRowTextSize = 25.5f;
        [Tooltip("Text size of the +1 spec part of the players text, in reference pixels.")]
        public float lobbyListRowSpecSize = 21f;
        [Tooltip("Height of a row's Join / Spectate / Full button, in reference pixels.")]
        public float lobbyListRowButtonHeight = 60f;
        [Tooltip("Text size of that button, in reference pixels.")]
        public float lobbyListRowButtonTextSize = 22.5f;
        [Tooltip("Height of the How to play button, in reference pixels.")]
        public float lobbyListHowToHeight = 72f;
        [Tooltip("Empty space left and right inside the How to play button, in reference pixels.")]
        public float lobbyListHowToPadding = 33f;
        [Tooltip("Text size of the How to play button, in reference pixels.")]
        public float lobbyListHowToTextSize = 24f;
        [Tooltip("Text size of the line at the bottom right of the list, in reference pixels.")]
        public float lobbyListHintSize = 21f;
        [Tooltip("Width of the create screen's card, in reference pixels.")]
        public float createCardWidth = 1140f;
        [Tooltip("Empty space inside the create screen's card, in reference pixels.")]
        public float createCardPadding = 60f;
        [Tooltip("Space between the create screen's parts, in reference pixels.")]
        public float createCardGap = 39f;
        [Tooltip("Text size of Create a lobby, in reference pixels.")]
        public float createTitleSize = 54f;
        [Tooltip("Text size of LOBBY NAME, MODE and TEAMS, in reference pixels.")]
        public float createLabelSize = 19.5f;
        [Tooltip("Height of the lobby name box, in reference pixels.")]
        public float createFieldHeight = 72f;
        [Tooltip("Empty space left and right inside the lobby name box, in reference pixels.")]
        public float createFieldPadding = 24f;
        [Tooltip("Text size of the lobby name, in reference pixels.")]
        public float createFieldTextSize = 27f;
        [Tooltip("Height of the mode buttons (Conquest / Dominion), in reference pixels.")]
        public float createModeHeight = 84f;
        [Tooltip("Text size of the mode buttons, in reference pixels.")]
        public float createModeTextSize = 33f;
        [Tooltip("Text size of the lines under the mode and the team sizes, in reference pixels.")]
        public float createNoteSize = 21f;
        [Tooltip("Text size of coming soon under a mode that cannot be created yet, in reference pixels.")]
        public float createComingSoonSize = 18f;
        [Tooltip("Size of a team size button (3v3v3, 3v3), in reference pixels.")]
        public Vector2 createSizeButton = new Vector2(210f, 78f);
        [Tooltip("Text size of a team size button, in reference pixels.")]
        public float createSizeTextSize = 30f;
        [Tooltip("Space between the team size buttons, in reference pixels.")]
        public float createSizeGap = 18f;
        [Tooltip("Height of Cancel and Create, in reference pixels.")]
        public float createActionHeight = 72f;
        [Tooltip("Empty space left and right inside Cancel, in reference pixels.")]
        public float createCancelPadding = 36f;
        [Tooltip("Text size of Cancel, in reference pixels.")]
        public float createCancelTextSize = 24f;
        [Tooltip("Empty space left and right inside Create, in reference pixels.")]
        public float createCreatePadding = 48f;
        [Tooltip("Text size of Create, in reference pixels.")]
        public float createCreateTextSize = 30f;
        [Tooltip("Space between Cancel and Create, in reference pixels.")]
        public float createActionGap = 18f;
        [Tooltip("How long the short line about a lobby that could not be joined or created stays on screen, in seconds.")]
        public float lobbyMessageSeconds = 6f;
        [Tooltip("The small caption above the title.")]
        public string nameScreenKickerText = "PROJECT";
        [Tooltip("The big title.")]
        public string nameScreenTitleText = "OVERPOWER";
        [Tooltip("The label above the name box.")]
        public string nameScreenLabelText = "YOUR NAME";
        [Tooltip("The hint under the name box: {0} is the fewest letters, {1} the most (Lobby Config).")]
        public string nameScreenHintFormat = "{0} to {1} letters or numbers";
        [Tooltip("The button that opens the lobby list.")]
        public string nameScreenFindText = "FIND A LOBBY";
        [Tooltip("The small caption above the list heading.")]
        public string lobbyListKickerText = "PROJECT OVERPOWER";
        [Tooltip("The list heading.")]
        public string lobbyListTitleText = "Lobbies";
        [Tooltip("Written before the player's name at the top right of the list.")]
        public string lobbyListPlayingAsText = "Playing as";
        [Tooltip("The button that opens the create screen.")]
        public string lobbyListCreateText = "CREATE LOBBY";
        [Tooltip("The column headings, in order.")]
        public string[] lobbyListColumnTexts = { "LOBBY", "MODE", "PLAYERS", "STATUS", "HOST" };
        [Tooltip("The button of a lobby with a free team seat.")]
        public string lobbyListJoinText = "Join";
        [Tooltip("The button of a lobby where only spectator seats are free.")]
        public string lobbyListSpectateText = "Spectate";
        [Tooltip("The greyed button of a lobby with no free seat.")]
        public string lobbyListFullText = "Full";
        [Tooltip("The cyan button at the bottom left.")]
        public string lobbyListHowToText = "How to play";
        [Tooltip("The line at the bottom right of the list.")]
        public string lobbyListHintText = "The list updates by itself. A lobby closes when everyone has left.";
        [Tooltip("Shown in place of the table when the list is empty.")]
        public string lobbyListEmptyText = "No lobbies yet. Create one to get started.";
        [Tooltip("Shown in place of the table until the list has arrived.")]
        public string lobbyListConnectingText = "Connecting to the lobby list...";
        [Tooltip("Shown for a lobby whose mode this version does not know.")]
        public string lobbyListUnknownModeText = "Unknown mode";
        [Tooltip("The create screen's heading.")]
        public string createTitleText = "Create a lobby";
        [Tooltip("The label above the lobby name box.")]
        public string createNameLabelText = "LOBBY NAME";
        [Tooltip("What the lobby name box starts with: {0} is the player's name.")]
        public string createNamePrefillFormat = "{0}'s lobby";
        [Tooltip("What the lobby name box starts with when the player has no name.")]
        public string createNameFallbackText = "My lobby";
        [Tooltip("The label above the mode buttons.")]
        public string createModeLabelText = "MODE";
        [Tooltip("The label above the team size buttons.")]
        public string createTeamsLabelText = "TEAMS";
        [Tooltip("Written on a mode that cannot be created yet.")]
        public string createComingSoonText = "coming soon";
        [Tooltip("The line under the mode buttons, one per mode family in order (Conquest, Dominion).")]
        public string[] createFamilyNotes = { "Capture territory for credits, take capitals, be the last team standing.", "Hold zones to score points over three rounds and out-score the other teams." };
        [Tooltip("The line under the team sizes about another family: {0} is its name, {1} its sizes.")]
        public string createOtherFamilyFormat = "{0} offers {1} here instead.";
        [Tooltip("Written between two sizes in that line.")]
        public string createSizeJoiner = " and ";
        [Tooltip("The button that closes the create screen.")]
        public string createCancelText = "Cancel";
        [Tooltip("The button that creates the lobby.")]
        public string createCreateText = "CREATE";
        [Tooltip("Shown when the lobby could not be created.")]
        public string createFailedText = "Could not create the lobby. Try again.";
        [Tooltip("Shown on the name screen when the connection to the game server is lost.")]
        public string lobbyConnectionLostText = "Disconnected. Check your connection.";

        [Header("Lobby room")]
        [Tooltip("Fill of a taken seat.")]
        public Color lobbyRoomSeatFill = new Color(0.2f, 0.212f, 0.247f, 1f);
        [Tooltip("Fill of the No role box.")]
        public Color lobbyRoomSideFill = new Color(0.125f, 0.133f, 0.165f, 1f);
        [Tooltip("Outline of the No role box.")]
        public Color lobbyRoomSideBorder = new Color(0.2f, 0.212f, 0.247f, 1f);
        [Tooltip("The Spectators heading.")]
        public Color lobbyRoomHeadingColor = new Color(0.847f, 0.835f, 0.808f, 1f);
        [Tooltip("The coloured edge on top of each team column, by team number (Orange, Purple, Cyan).")]
        public Color[] lobbyRoomTeamColors = { new Color(1f, 0.45f, 0.05f, 1f), new Color(0.42f, 0.247f, 0.749f, 1f), new Color(0.169f, 0.722f, 0.769f, 1f) };
        [Tooltip("Empty space round the lobby room screen: left and right, then top and bottom, in reference pixels.")]
        public Vector2 lobbyRoomPadding = new Vector2(72f, 54f);
        [Tooltip("Space between the heading, the seats and the bottom row of the lobby room, in reference pixels.")]
        public float lobbyRoomGap = 30f;
        [Tooltip("How round the corners of the team columns and the side box are, in reference pixels.")]
        public float lobbyRoomBoxRadius = 12f;
        [Tooltip("Text size of the lobby name, in reference pixels.")]
        public float lobbyRoomTitleSize = 57f;
        [Tooltip("Height of the mode button, in reference pixels.")]
        public float lobbyRoomModeHeight = 51f;
        [Tooltip("Empty space left and right inside the mode button, in reference pixels.")]
        public float lobbyRoomModePadding = 21f;
        [Tooltip("Text size of the mode button, in reference pixels.")]
        public float lobbyRoomModeTextSize = 22.5f;
        [Tooltip("Text size of the line after the mode button (the host and the hint), in reference pixels.")]
        public float lobbyRoomHostLineSize = 22.5f;
        [Tooltip("Space between the team columns, and between the teams and the spectators, in reference pixels.")]
        public float lobbyRoomTeamGap = 24f;
        [Tooltip("Empty space inside a team column, the spectators box and the No role box, in reference pixels.")]
        public float lobbyRoomBoxPadding = 21f;
        [Tooltip("Thickness of the coloured edge on top of a team column, in reference pixels.")]
        public float lobbyRoomStripeHeight = 6f;
        [Tooltip("Text size of a team column heading, in reference pixels.")]
        public float lobbyRoomTeamNameSize = 33f;
        [Tooltip("Height of a seat button in a team column, in reference pixels.")]
        public float lobbyRoomSeatHeight = 69f;
        [Tooltip("Space between two seats, in reference pixels.")]
        public float lobbyRoomSeatGap = 12f;
        [Tooltip("Empty space left and right of a seat button text, in reference pixels.")]
        public float lobbyRoomSeatPadding = 21f;
        [Tooltip("Text size of a taken seat, in reference pixels.")]
        public float lobbyRoomSeatTextSize = 24f;
        [Tooltip("Text size of an empty seat, in reference pixels.")]
        public float lobbyRoomSeatEmptyTextSize = 22.5f;
        [Tooltip("Thickness of the outline round your own seat, in reference pixels.")]
        public float lobbyRoomMineBorderWidth = 3f;
        [Tooltip("Text size of the Spectators heading, in reference pixels.")]
        public float lobbyRoomSpectatorTitleSize = 30f;
        [Tooltip("Height of a spectator seat, in reference pixels.")]
        public float lobbyRoomSpectatorSeatHeight = 63f;
        [Tooltip("Text size of a spectator seat, in reference pixels.")]
        public float lobbyRoomSpectatorTextSize = 22.5f;
        [Tooltip("Space between spectator seats, in reference pixels.")]
        public float lobbyRoomSpectatorGap = 18f;
        [Tooltip("Width of the No role box, in reference pixels.")]
        public float lobbyRoomSideWidth = 375f;
        [Tooltip("Text size of the No role heading, in reference pixels.")]
        public float lobbyRoomSideTitleSize = 30f;
        [Tooltip("Text size of a name in the No role box, in reference pixels.")]
        public float lobbyRoomSideNameSize = 24f;
        [Tooltip("Text size of the note in the No role box, in reference pixels.")]
        public float lobbyRoomSideNoteSize = 19.5f;
        [Tooltip("Height of Leave my seat, in reference pixels.")]
        public float lobbyRoomSideButtonHeight = 60f;
        [Tooltip("Text size of Leave my seat, in reference pixels.")]
        public float lobbyRoomSideButtonTextSize = 21f;
        [Tooltip("Height of Leave lobby and How to play, in reference pixels.")]
        public float lobbyRoomBottomHeight = 72f;
        [Tooltip("Empty space left and right inside Leave lobby and How to play, in reference pixels.")]
        public float lobbyRoomBottomPadding = 33f;
        [Tooltip("Text size of Leave lobby and How to play, in reference pixels.")]
        public float lobbyRoomBottomTextSize = 24f;
        [Tooltip("Space between Leave lobby and How to play, in reference pixels.")]
        public float lobbyRoomBottomGap = 18f;
        [Tooltip("Height of Start game, in reference pixels.")]
        public float lobbyRoomStartHeight = 84f;
        [Tooltip("Empty space left and right inside Start game, in reference pixels.")]
        public float lobbyRoomStartPadding = 54f;
        [Tooltip("Text size of Start game, in reference pixels.")]
        public float lobbyRoomStartTextSize = 36f;
        [Tooltip("Text size of the line beside Start game (what it does, why it is greyed, or who the others wait for), in reference pixels.")]
        public float lobbyRoomStartHintSize = 21f;
        [Tooltip("Space between that line and Start game, in reference pixels.")]
        public float lobbyRoomStartGap = 24f;
        [Tooltip("The mode button, before its circled i: {0} is the mode name (Conquest 3v3v3).")]
        public string lobbyRoomModeFormat = "{0} \u00b7";
        [Tooltip("Size of the circled i at the right end of the mode button, in reference pixels. It is drawn (the fonts have no circled-i letter).")]
        public float lobbyRoomInfoIconSize = 30f;
        [Tooltip("The letter inside that circle.")]
        public string lobbyRoomInfoIconText = "i";
        [Tooltip("The line after the mode button: {0} is the host's name (drawn bold).")]
        public string lobbyRoomHostFormat = "\u00b7 host {0} \u00b7 click a seat to take it";
        [Tooltip("What an empty seat says.")]
        public string lobbyRoomEmptySeatText = "Empty seat";
        [Tooltip("Written after your own name on your seat.")]
        public string lobbyRoomYouSuffix = " (you)";
        [Tooltip("Written after the host's name on the host's seat.")]
        public string lobbyRoomHostSuffix = " \u00b7 host";
        [Tooltip("The heading over the spectator seats.")]
        public string lobbyRoomSpectatorsText = "Spectators";
        [Tooltip("The heading of the side box: {0} is how many players have no seat yet.")]
        public string lobbyRoomNoRoleFormat = "No role ({0})";
        [Tooltip("The note at the bottom of the No role box.")]
        public string lobbyRoomNoRoleNote = "At the start they fill the emptiest team, then the spectator seats.";
        [Tooltip("The button that puts you back in No role.")]
        public string lobbyRoomLeaveSeatText = "Leave my seat";
        [Tooltip("The button that leaves the lobby for the list.")]
        public string lobbyRoomLeaveLobbyText = "Leave lobby";
        [Tooltip("The host's button.")]
        public string lobbyRoomStartText = "START GAME";
        [Tooltip("The line beside Start game while it can be pressed.")]
        public string lobbyRoomStartHintText = "Everyone spawns into the warm-up";
        [Tooltip("The line beside the greyed Start game: {0} is the team that would stay empty.")]
        public string lobbyRoomStartBlockedFormat = "{0} needs a player";
        [Tooltip("What everyone but the host reads at the bottom right: {0} is the host's name.")]
        public string lobbyRoomWaitingFormat = "Waiting for {0} to start the game";
        [Tooltip("Width of the mode info and How to play pages, in reference pixels.")]
        public float lobbyRoomOverlayWidth = 1824f;
        [Tooltip("Height of the mode info and How to play pages, in reference pixels.")]
        public float lobbyRoomOverlayHeight = 984f;
        [Tooltip("Text size of the title of those pages, in reference pixels.")]
        public float lobbyRoomOverlayTitleSize = 60f;
        [Tooltip("Size of the close button of those pages, in reference pixels.")]
        public float lobbyRoomOverlayCloseSize = 66f;
        [Tooltip("The dark layer behind the mode info and How to play pages.")]
        public Color lobbyRoomOverlayShade = new Color(0f, 0f, 0f, 0.7f);

        [Header("Lobby screens: layout, words")]
        [Tooltip("Space between the parts of the lobby list's top row, in reference pixels.")]
        public float lobbyListHeaderGap = 24f;
        [Tooltip("Space between the small caption and the heading of the lobby list, in reference pixels.")]
        public float lobbyListTitleGap = 6f;
        [Tooltip("Space between the player's name and the Create lobby button, in reference pixels.")]
        public float lobbyListPlayerGap = 24f;
        [Tooltip("Space between 'Playing as' and the player's name, in reference pixels.")]
        public float lobbyListPlayingAsGap = 9f;
        [Tooltip("Space between the parts of the lobby list's bottom row, in reference pixels.")]
        public float lobbyListFooterGap = 24f;
        [Tooltip("How far one turn of the mouse wheel scrolls the lobby list.")]
        public float lobbyListScrollSensitivity = 40f;
        [Tooltip("Space between the name, mode and team size blocks of the create screen, in reference pixels.")]
        public float createBlockGap = 12f;
        [Tooltip("A mode that is coming soon: how many text heights the mode name is lifted to make room for the words under it.")]
        public float createComingSoonLabelShift = 1.2f;
        [Tooltip("A mode that is coming soon: height of the words under the name, in text heights.")]
        public float createComingSoonBoxHeight = 1.3f;
        [Tooltip("A mode that is coming soon: how far the words sit above the bottom edge of the button, in reference pixels.")]
        public float createComingSoonLift = 4f;
        [Tooltip("Space between the small caption and OVERPOWER on the name screen, in reference pixels.")]
        public float nameScreenTitleGap = 9f;
        [Tooltip("Shown when a late joiner finds every seat taken.")]
        public string lobbyLateJoinFullText = "Lobby full";
        [Tooltip("Shown when a late joiner's seat request was not answered in time.")]
        public string lobbyLateJoinNoSeatText = "Could not get a seat";
        [Tooltip("Shown when a join was refused because the lobby is full.")]
        public string lobbyJoinFullText = "That lobby is full.";
        [Tooltip("Shown when a join was refused because the lobby has closed.")]
        public string lobbyJoinClosedText = "That lobby has closed.";
        [Tooltip("Shown when a join was refused because the lobby is gone.")]
        public string lobbyJoinGoneText = "That lobby no longer exists.";
        [Tooltip("Shown when a join was refused for any other reason.")]
        public string lobbyJoinFailedText = "Could not join that lobby.";

        [Header("Warm-up bar")]
        [Tooltip("Fill of the warm-up bar at the top of the arena.")]
        public Color warmupBarFill = new Color(0.102f, 0.11f, 0.133f, 0.88f);
        [Tooltip("How round the corners of the warm-up bar are, in reference pixels.")]
        public float warmupBarRadius = 12f;
        [Tooltip("Empty space inside the bar with the End warm-up button: left and right, then top and bottom, in reference pixels.")]
        public Vector2 warmupBarPaddingHost = new Vector2(36f, 18f);
        [Tooltip("Empty space inside the bar without a button: left and right, then top and bottom, in reference pixels.")]
        public Vector2 warmupBarPaddingGuest = new Vector2(42f, 18f);
        [Tooltip("Space between the bar's text and the End warm-up button, in reference pixels.")]
        public float warmupBarGap = 30f;
        [Tooltip("Text size of WARM-UP and of the countdown, in reference pixels.")]
        public float warmupBarTitleSize = 36f;
        [Tooltip("Text size of the second line, in reference pixels.")]
        public float warmupBarInfoSize = 21f;
        [Tooltip("Height of End warm-up, in reference pixels.")]
        public float warmupBarButtonHeight = 72f;
        [Tooltip("Empty space left and right inside End warm-up, in reference pixels.")]
        public float warmupBarButtonPadding = 39f;
        [Tooltip("Text size of End warm-up, in reference pixels.")]
        public float warmupBarButtonTextSize = 30f;
        [Tooltip("The first line of the bar.")]
        public string warmupBarTitleText = "WARM-UP";
        [Tooltip("The host's second line: {0} is the player count.")]
        public string warmupBarInfoFormat = "Free shop \u00b7 nothing counts \u00b7 {0}";
        [Tooltip("The player count: {0} is how many.")]
        public string warmupBarPlayersText = "{0} players";
        [Tooltip("The player count when there is one.")]
        public string warmupBarOnePlayerText = "1 player";
        [Tooltip("Everyone else's second line: {0} is the host's name.")]
        public string warmupBarGuestFormat = "Free shop \u00b7 nothing counts \u00b7 {0} ends the warm-up";
        [Tooltip("The host's button.")]
        public string warmupBarButtonText = "END WARM-UP";
        [Tooltip("Under the greyed End warm-up: {0} is the team with nobody present.")]
        public string warmupBarBlockedFormat = "{0} has no player";
        [Tooltip("The second line of the countdown.")]
        public string warmupBarCountdownInfo = "Everything resets when it goes live";

        [Header("Chat")]
        [Tooltip("How see-through the dark chat panel is: 0 = invisible, 1 = solid. Board 7A: about 0.38.")]
        [Range(0f, 1f)] public float chatPanelAlpha = 0.38f;
        [Tooltip("Width and height of the chat panel, in reference pixels.")]
        public Vector2 chatPanelSize = new Vector2(630f, 360f);
        [Tooltip("Space between the chat panel and the bottom-left corner of the screen, in reference pixels.")]
        public Vector2 chatPanelMargin = new Vector2(72f, 54f);
        [Tooltip("In the lobby room: space between the chat panel and the bottom-left corner. The left edge clears the Leave lobby and How to play buttons.")]
        public Vector2 chatLobbyMargin = new Vector2(520f, 54f);
        [Tooltip("In the lobby room: width and height of the chat panel. It sits right of the bottom buttons and under the spectator row.")]
        public Vector2 chatLobbySize = new Vector2(630f, 270f);
        [Tooltip("Empty space between the chat panel's edge and what is in it, in reference pixels.")]
        public float chatPanelPadding = 24f;
        [Tooltip("Space between the lines and the typing box, in reference pixels.")]
        public float chatPanelGap = 12f;
        [Tooltip("How round the corners of the chat panel are, in reference pixels.")]
        public float chatPanelRadius = 12f;
        [Tooltip("Text size of a chat line, in reference pixels (board: 18 px x 1.5).")]
        public float chatTextSize = 27f;
        [Tooltip("Extra space between chat lines, as a percentage of the line height.")]
        public float chatLineSpacing = 30f;
        [Tooltip("Height of the typing box, in reference pixels.")]
        public float chatInputHeight = 66f;
        [Tooltip("Text size inside the typing box, in reference pixels.")]
        public float chatInputTextSize = 24f;
        [Tooltip("What the typing box says while it is empty.")]
        public string chatInputPlaceholder = "Enter to type, Escape to close";
        [Tooltip("Written before a spectator's name in chat.")]
        public string chatSpectatorTag = "[SPEC]";
        [Tooltip("How many chat lines are kept on screen; older ones are dropped.")]
        public int chatMaxLines = DefaultChatMaxLines;
        /// <summary>The line count used when no theme is assigned (and the default of chatMaxLines).</summary>
        public const int DefaultChatMaxLines = 40;
        [Tooltip("Colour of the chat panel's dark background (how see-through it is comes from Chat Panel Alpha).")]
        public Color chatPanelColor = new Color(15f / 255f, 16f / 255f, 20f / 255f, 1f);
        [Tooltip("Empty space between the typing box's edge and the text inside it, left and right, in reference pixels.")]
        public float chatInputTextInset = 18f;
        [Tooltip("Thickness of the typing box's border line, in reference pixels.")]
        public float chatInputBorderWidth = 1.5f;
        [Tooltip("How round the typing box's corners are compared with the chat panel's corners: 1 = the same, 0.5 = half as round.")]
        public float chatInputCornerFactor = 0.75f;
        [Tooltip("What the chat says for a moment when the line you pressed Enter on could not be sent (the chat server is reconnecting). The line stays in the typing box so you can press Enter again.")]
        public string chatNotSentHint = "Not sent, chat reconnecting";
        [Tooltip("How long the 'not sent' hint stays up, in seconds.")]
        public float chatNotSentHintSeconds = 3f;
        [Tooltip("Text size of the 'not sent' hint, in reference pixels.")]
        public float chatNotSentHintSize = 22f;
        [Tooltip("Height of the 'not sent' hint as a multiple of its text size (1.6 = a line and a bit of air).")]
        public float chatNotSentHintLineHeight = 1.6f;
        [Tooltip("Gap between the top of the typing box and the bottom of the 'not sent' hint, in reference pixels.")]
        public float chatNotSentHintGap = 2f;

        [Header("Lobby screens: layout numbers")]
        [Tooltip("Space between the lobby name and the mode row of the lobby room, in reference pixels.")]
        public float lobbyRoomHeaderGap = 3f;
        [Tooltip("Space between the mode button and the host line, in reference pixels.")]
        public float lobbyRoomModeRowGap = 15f;
        [Tooltip("Size of the i inside the mode button's circle, as a share of the circle's size.")]
        public float lobbyRoomInfoIconTextFactor = 0.62f;
        [Tooltip("Space between the mode name and the circled i, as a share of the mode button's padding.")]
        public float lobbyRoomModeIconGapFactor = 0.4f;
        [Tooltip("A little extra width of the mode button so its text never touches the edge, in reference pixels.")]
        public float lobbyRoomModeButtonSlack = 4f;
        [Tooltip("Height of a name line in the No role box, as a share of its text size.")]
        public float lobbyRoomSideNameLine = 1.4f;
        [Tooltip("Empty space right of End warm-up inside the host's bar, in reference pixels (the left side uses the padding above).")]
        public float warmupBarHostRightPadding = 20.88f;
        [Tooltip("Empty space left and right inside the name box on the name screen, in reference pixels.")]
        public float nameScreenFieldPadding = 23.04f;
        [Tooltip("How much rounder the mode and size buttons of the create screen are than the other buttons, in reference pixels.")]
        public float createModeRadiusExtra = 3f;

        [Header("How to play and the game mode info page")]
        [Tooltip("The pages of the How to play wiki: title, text and picture of each (the HowToPlayPages asset).")]
        public Overpower.Data.HowToPlayPages howToPlayPages;
        [Tooltip("The small heading over the page list of How to play.")]
        public string howToPlaySidebarTitle = "HOW TO PLAY";
        [Tooltip("The previous button: {0} is the title of the page before this one.")]
        public string howToPlayPreviousFormat = "<size=150%>\u2039</size> {0}";
        [Tooltip("The next button: {0} is the title of the page after this one.")]
        public string howToPlayNextFormat = "{0} <size=150%>\u203a</size>";
        [Tooltip("The page counter between the two buttons: {0} is this page's number, {1} how many pages there are.")]
        public string howToPlayCounterFormat = "{0} / {1}";
        [Tooltip("Written on How to play when the pages asset has no pages.")]
        public string howToPlayNoPagesText = "No pages yet.";
        [Tooltip("The small heading over the mode's name on the game mode info page.")]
        public string modeInfoKickerText = "GAME MODE";
        [Tooltip("Written on the game mode info page when the mode has no info cards.")]
        public string modeInfoNoCardsText = "Nothing to read about this mode yet.";
        [Tooltip("Background of the page list on the left of How to play.")]
        public Color howToPlaySidebarColor = new Color(0.082f, 0.086f, 0.106f, 1f);
        [Tooltip("Background behind a page's picture (the pictures are drawn on the same colour).")]
        public Color howToPlayPictureColor = new Color(0.055f, 0.059f, 0.075f, 1f);
        [Tooltip("Colour of a page's text.")]
        public Color howToPlayTextColor = new Color(0.957f, 0.949f, 0.929f, 1f);
        [Tooltip("Empty space left and right of the content of How to play and the mode info page, in reference pixels.")]
        public float howToPlayCardPaddingX = 54f;
        [Tooltip("Empty space above and below the content of those pages, in reference pixels.")]
        public float howToPlayCardPaddingY = 42f;
        [Tooltip("Space between the parts of the content column of How to play, in reference pixels.")]
        public float howToPlayContentGap = 30f;
        [Tooltip("Width of the page list of How to play, in reference pixels.")]
        public float howToPlaySidebarWidth = 390f;
        [Tooltip("Empty space left of a page list entry's text, in reference pixels.")]
        public float howToPlaySidebarPaddingX = 36f;
        [Tooltip("Empty space above and below the page list, in reference pixels.")]
        public float howToPlaySidebarPaddingY = 36f;
        [Tooltip("Space between two page list entries, in reference pixels.")]
        public float howToPlaySidebarGap = 6f;
        [Tooltip("Text size of the small heading over the page list, in reference pixels.")]
        public float howToPlaySidebarTitleSize = 18f;
        [Tooltip("Space between that heading and the first entry, in reference pixels.")]
        public float howToPlaySidebarTitleGap = 18f;
        [Tooltip("Extra space between the letters of that heading, in reference pixels.")]
        public float howToPlaySidebarTitleSpacing = 3f;
        [Tooltip("Height of one page list entry, in reference pixels.")]
        public float howToPlayEntryHeight = 58f;
        [Tooltip("Text size of a page list entry, in reference pixels.")]
        public float howToPlayEntryTextSize = 24f;
        [Tooltip("Width of the cyan bar beside the selected page, in reference pixels.")]
        public float howToPlaySelectedBarWidth = 4.5f;
        [Tooltip("Text size of a page's title, in reference pixels.")]
        public float howToPlayTitleSize = 60f;
        [Tooltip("Width of a page's picture, in reference pixels.")]
        public float howToPlayPictureWidth = 741f;
        [Tooltip("Height of a page's picture, in reference pixels (the pictures are 1040 x 800).")]
        public float howToPlayPictureHeight = 570f;
        [Tooltip("How round the corners of the picture are, in reference pixels.")]
        public float howToPlayPictureRadius = 12f;
        [Tooltip("Space between the picture and the text, in reference pixels.")]
        public float howToPlayPictureGap = 48f;
        [Tooltip("Largest text size of a page's text, in reference pixels. A longer page takes a smaller size so every page fits, all pages at the same size.")]
        public float howToPlayTextSize = 28.5f;
        [Tooltip("Smallest text size a page's text may shrink to, in reference pixels.")]
        public float howToPlayTextMinSize = 21f;
        [Tooltip("Extra space between the lines of a page's text, as a percentage of the text size.")]
        public float howToPlayTextLineSpacing = 35f;
        [Tooltip("Height of the previous and next buttons, in reference pixels.")]
        public float howToPlayButtonHeight = 66f;
        [Tooltip("Empty space left and right inside the previous and next buttons, in reference pixels.")]
        public float howToPlayButtonPadding = 30f;
        [Tooltip("Text size of the previous and next buttons, in reference pixels.")]
        public float howToPlayButtonTextSize = 22.5f;
        [Tooltip("Text size of the page counter, in reference pixels.")]
        public float howToPlayCounterSize = 21f;
        [Tooltip("Width of the page counter's box, as a multiple of its text size.")]
        public float howToPlayCounterWidthFactor = 8f;
        [Tooltip("Height of a line of page text, as a multiple of its text size (titles, kickers, counters, card titles).")]
        public float howToPlayLineHeightFactor = 1.5f;
        [Tooltip("Space between the small heading over a mode's name and the name, in reference pixels.")]
        public float modeInfoHeaderGap = 3f;
        [Tooltip("Text size of the small heading over the mode's name, in reference pixels.")]
        public float modeInfoKickerSize = 18f;
        [Tooltip("Extra space between the letters of that heading, in reference pixels.")]
        public float modeInfoKickerSpacing = 3f;
        [Tooltip("How many cards stand side by side on the game mode info page.")]
        public int modeInfoColumns = 3;
        [Tooltip("Space between the cards of the game mode info page, in reference pixels.")]
        public float modeInfoGap = 24f;
        [Tooltip("Empty space inside a card, in reference pixels.")]
        public float modeInfoCardPadding = 30f;
        [Tooltip("Space between a card's title and its text, in reference pixels.")]
        public float modeInfoCardGap = 15f;
        [Tooltip("How round the corners of a card are, in reference pixels.")]
        public float modeInfoCardRadius = 12f;
        [Tooltip("Thickness of the coloured edge across the top of a card, in reference pixels.")]
        public float modeInfoAccentHeight = 6f;
        [Tooltip("Text size of a card's title, in reference pixels.")]
        public float modeInfoCardTitleSize = 36f;
        [Tooltip("Largest text size of a card's text, in reference pixels. A mode with a lot to say takes a smaller size so every card fits, all cards at the same size.")]
        public float modeInfoCardTextSize = 24f;
        [Tooltip("Smallest text size a card's text may shrink to, in reference pixels.")]
        public float modeInfoCardTextMinSize = 15f;
        [Tooltip("Extra height kept free in every row of cards when working out whether the text fits, in reference pixels.")]
        public float modeInfoFitSlack = 6f;
        [Tooltip("Extra space between the lines of a card's text, as a percentage of the text size.")]
        public float modeInfoCardTextLineSpacing = 20f;
        [Tooltip("Width and height of the cross on the close button of those pages, in reference pixels. It is drawn (the fonts have no cross).")]
        public float lobbyRoomCloseCrossSize = 21f;
        [Tooltip("Thickness of the lines of that cross, in reference pixels.")]
        public float lobbyRoomCloseCrossThickness = 3f;

        [Header("Look colours (the colour language of abilities and guns)")]
        [Tooltip("The grey of every movement ability (Dash, Blink, Sprint, the Zip Gun and the Teleport rings): their streaks, shards, speed-lines, cable and rings. The Arena Look's Movement Team Tint mixes a little of the caster's team colour into it, so others can tell whose dash it was while it still reads as movement and never as the gold Shield. Kept clearly brighter than the neutral zone lines, so an effect never sinks into the floor grid.")]
        [UnityEngine.Serialization.FormerlySerializedAs("movementSilverColor")]
        public Color movementGreyColor = new Color(0.76f, 0.77f, 0.78f, 1f);
        [Tooltip("The glowing yellow-orange heart of fire: the hot end of the Flamethrower's embers and of the Rocket Cursor's burning ground's embers. Keep it paler than the orange team and well off the Shield's gold, toward orange.")]
        public Color fireCoreColor = new Color(1f, 0.7f, 0.33f, 1f);
        [Tooltip("The orange-red of fire as it is seen: the Flamethrower's flat fan on the floor and the cool end of the embers. Keep it on the red side of the orange team (a plain or yellower orange reads as the team) and well off the Shield's gold.")]
        public Color fireEdgeColor = new Color(1f, 0.38f, 0.1f, 1f);
        [Tooltip("Each flame's colour over its life, from left (born) to right (burnt out), for the Flamethrower's jet and the Rocket Cursor's burning ground: it tints the flame sheet, whose own red rim and yellow inner tongue show through. Keep it at or below plain colour (the Arena Look's Flame Body Glow scales it): a body brighter than white washes out to cream and reads as smoke. Its alpha fades the flame out.")]
        public Gradient fireBodyOverLife = FireRamp(
            new[] { new GradientColorKey(new Color(1f, 0.571f, 0.242f), 0f), new GradientColorKey(new Color(0.934f, 0.462f, 0.185f), 0.35f),
                    new GradientColorKey(new Color(0.877f, 0.414f, 0.167f), 0.62f), new GradientColorKey(new Color(0.781f, 0.341f, 0.148f), 0.82f),
                    new GradientColorKey(new Color(0.372f, 0.148f, 0.097f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0.5f, 0.62f), new GradientAlphaKey(0f, 0.82f), new GradientAlphaKey(0f, 1f) });
        [Tooltip("The colour over its life of each flame's hot heart, drawn over the flame with light added, from left (born) to right (burnt out). The Arena Look's Flame Glow lifts it past white so it blooms: only the heart glows, the flame around it keeps its orange-red. Its alpha fades the heart out before the flame.")]
        public Gradient fireCoreOverLife = FireRamp(
            new[] { new GradientColorKey(new Color(1f, 0.701f, 0.332f), 0f), new GradientColorKey(new Color(0.827f, 0.485f, 0.19f), 0.5f),
                    new GradientColorKey(new Color(0.455f, 0.206f, 0.061f), 0.85f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0.15f, 0.85f), new GradientAlphaKey(0f, 1f) });

        private static Gradient FireRamp(GradientColorKey[] colours, GradientAlphaKey[] alphas)
        {
            var ramp = new Gradient();
            ramp.SetKeys(colours, alphas);
            return ramp;
        }
        [Tooltip("The red of damage: the Raybeam, the spikes of its mark over an enemy it left vulnerable, and the Electric Fence's arcs. Red, because green reads as healing; keep it clearly redder than the orange team and away from the Mark gun's hot pink.")]
        public Color dangerColor = new Color(1f, 0.18f, 0.15f, 1f);
        [Tooltip("The white of a hit or a shot leaving: every gun's impact flash, the rocket's shockwave ring and a mine's blast, the muzzle flash, the Bounce ricochet chevron (all drawn brighter by the Arena Look's Gun Flash Glow) and the white-hot core of a bullet.")]
        public Color flashWhiteColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("The green of healing: the health packs' cross and the edge of their pad, the plus-shaped sparks rising round any player whose health is going up, and the Heal Zone. Keep it clearly green, never blue-green: it must not read as the cyan team, the electric blue of the Stun Gun or the white of a hit.")]
        public Color healGreenColor = new Color(0.25f, 1f, 0.3f, 1f);
        [Tooltip("The light grey of the lines on ground and walls around a zone nobody owns. Bright enough that the zone stands out from the faint grid, but never glowing and never a colour, so a neutral zone is not mistaken for owned ground.")]
        public Color arenaNeutralLineColor = new Color(0.6f, 0.62f, 0.66f, 1f);
        [Tooltip("The plain grey of the lines on ground and walls in a zone that is out of play (a closed corner, or a capital nobody plays for). Darker than a neutral zone's lines and never glowing, so it reads as dead ground, but still light enough that the dark walls there keep their stripes and the line along their top.")]
        public Color arenaOutOfPlayLineColor = new Color(0.45f, 0.45f, 0.47f, 1f);
        [Tooltip("The asset that holds the arena's world-look numbers and the look of every ability (its shape and what its colour means). Effects find their look through this reference.")]
        public ArenaLook arenaLook;

        [Header("Death card and pop-ups")]
        [Tooltip("The death card's countdown line while a player waits to respawn. {0} is the whole seconds left, rounded up, so the last second reads 1.")]
        public string deathCardRespawnFormat = "RESPAWN IN {0}";
        [Tooltip("Width of the death card (the RESPAWN IN card and the waiting card), in reference pixels (the HUD's 1920x1080 grid).")]
        public float deathCardWidth = 520f;
        [Tooltip("Text size of the death card's main line (the countdown, or the waiting text), in reference pixels.")]
        public float deathCardTitleSize = 44f;
        [Tooltip("Space inside the death card around its contents, in reference pixels: x left and right, y top and bottom.")]
        public Vector2 deathCardPadding = new Vector2(28f, 18f);
        [Tooltip("Space between the lines of the death card (the countdown, the capital-under-attack note, the Quit button), in reference pixels. " +
                 "The saved-log box uses the same space between its lines.")]
        public float deathCardGap = 10f;
        [Tooltip("The gap the death card keeps to the screen's top edge, the Dominion round bar, the HUD's top message and the ability bar, in reference pixels.")]
        public float deathCardMargin = 16f;
        [Tooltip("Text size of the countdown while the shop is open: the card shrinks to fit the strip between the shop and the ability bar, so both stay readable.")]
        public float deathCardShopOpenTitleSize = 26f;
        [Tooltip("Space inside the death card while the shop is open, in reference pixels: x left and right, y top and bottom (small, so the card fits under the shop).")]
        public Vector2 deathCardShopOpenPadding = new Vector2(20f, 3f);
        [Tooltip("The gap the death card keeps to the open shop and to the ability bar while the shop is open, in reference pixels.")]
        public float deathCardShopOpenMargin = 6f;
        [Tooltip("The dark edge on the world while this player is dead: its colour, and its alpha is how dark the corners get (the middle stays clear). " +
                 "The HUD and every panel draw over it.")]
        public Color deathVignetteColor = new Color(0f, 0f, 0f, 0.5f);
        [Tooltip("The dark wash over the game behind the Escape pop-up and the Connection lost box, so the question reads first.")]
        public Color popupDimColor = new Color(0f, 0f, 0f, 0.55f);
        [Tooltip("Width of the Escape pop-up's card, in reference pixels.")]
        public float popupCardWidth = 640f;
        [Tooltip("Text size of the question at the top of the Escape pop-up, in reference pixels.")]
        public float popupTitleSize = 40f;
        [Tooltip("Text size of the plain text in the pop-ups (the volume row, the saved-log box), in reference pixels.")]
        public float popupBodySize = 22f;
        [Tooltip("The label in front of the volume slider on the Escape pop-up. The slider sets the volume of everything in the game, gunfire included.")]
        public string masterVolumeLabel = "MASTER VOLUME";
        [Tooltip("How the volume reads next to the slider on the Escape pop-up. {0} is the volume from 0 to 100.")]
        public string masterVolumePercentFormat = "{0}%";
        [Tooltip("Height of the volume row on the Escape pop-up, and of the slider's knob, in reference pixels.")]
        public float masterVolumeRowHeight = 36f;
        [Tooltip("The saved-log box's button that closes the box (only the box, nothing else).")]
        public string matchLogDismissText = "Dismiss";
        [Tooltip("Width of the YOU WIN / YOU LOSE card, in reference pixels. Its height fits the title and the buttons.")]
        public float resultPanelCardWidth = 720f;
        [Tooltip("Text size of YOU WIN / YOU LOSE on their card, in reference pixels.")]
        public float resultPanelTitleSize = 72f;
        [Tooltip("Width of the button on the YOU WIN / YOU LOSE card (Quit, or Back to the lobby list once the match is over), in reference pixels.")]
        public float resultPanelButtonWidth = 440f;
    }
}
