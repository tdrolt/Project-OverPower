using System.Collections.Generic;
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
    [CreateAssetMenu(menuName = "Overpower/UI Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Canvas")]
        [Tooltip("The screen size the UI is designed at. Text and panels scale from this to the real screen.")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Tooltip("0 = scale with screen width, 1 = with height, 0.5 = a mix. 0.5 keeps ultrawide and 16:10 readable.")]
        [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;
        [Tooltip("How big the whole HUD is drawn, as a fraction of the sizes below: 1 is full size, 0.8 is 20% " +
                 "smaller (Tudor, 2026-09-17). Applied ONCE, as a scale on the HUD panel and on the gold/shop " +
                 "block in the bottom-right corner - every other size on this asset stays in its own units, so a " +
                 "designer tunes Bar Width or Slot Width normally and this one number makes the whole group " +
                 "bigger or smaller. It does NOT scale the minimap, the toast, the match panels, the chat or the " +
                 "F1 test range panel, which each sit on their own.")]
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
        [Tooltip("Colour of items you cannot pick yet - a dark, mostly-opaque fill with muted text on top (Muted Text Colour), not a near-transparent wash: at low alpha over a translucent panel this read as barely-there rather than clearly locked (Task 9a review, 616x576 capture).")]
        public Color lockedColor = new Color(0.09f, 0.09f, 0.10f, 0.92f);
        [Tooltip("Space between a panel's background edge and the bars/slots inside it, in canvas units, on every side.")]
        public float hudPanelPadding = 16f;
        [Tooltip("Distance from the bottom of the screen to the bottom of the HUD panel, in canvas units. Kept " +
                 "small - Task 5 shrank the chat prompt so the HUD no longer needs to clear a tall band.")]
        public float hudBottomOffset = 28f;

        [Header("Bars")]
        [Tooltip("Plain white sprite every filled bar uses. Without a sprite Unity ignores the fill amount and draws the bar full.")]
        public Sprite barSprite;
        [Tooltip("Width of every HUD bar (health, armor, overheat), in canvas units. The slots row beneath them " +
                 "does not read this directly (fix 7, Playtest polish review) - its own preferred width is " +
                 "computed from Slot Width and Hud Slot Spacing below (4 slots: the weapon plus the three " +
                 "ability slots - see PlayerHud.BuildUi). Keep 4 x Slot Width + 3 x Hud Slot Spacing equal to " +
                 "this number, or the slot row will no longer line up under the bars above it.")]
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
        [Tooltip("Colour the Vent band turns on a miss (pressed early, pressed late, or the window passed " +
                 "with nothing pressed) - stays this colour for the rest of the silence, so a player who " +
                 "pressed early knows at once rather than wondering if it is still coming. Opaque dark grey " +
                 "on purpose (review fix, captures d2/a2 2026-09-23): the earlier translucent mid-grey sat " +
                 "over the red silenced fill and read pinkish, almost the same as the dim 'not yet' look " +
                 "(white at 0.28 alpha over red also reads pink) - a player who pressed early could not " +
                 "tell they had missed. Opaque so it reads as 'closed' regardless of what is under it.")]
        public Color ventBandMissColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        [Header("HUD slots")]
        [Tooltip("Width of one weapon/ability slot box, in canvas units - sized so the longest short names " +
                 "(Raybeam, Shotgun, Baseline) and the longest key label (SPACE) both fit at Body Text Size. " +
                 "The slots row's own width is 4 of these plus 3 gaps of Hud Slot Spacing below - see Bar " +
                 "Width's tooltip for why that total is kept equal to it.")]
        public float slotWidth = 140f;
        [Tooltip("Horizontal gap between adjacent HUD slots (the weapon slot and the three ability slots), in " +
                 "canvas units - fix 7, Playtest polish review: this used to be a number hardcoded in " +
                 "PlayerHud.BuildUi that the slots row's own width (wrongly pinned to Bar Width instead of its " +
                 "own content) never accounted for. Now the one home for that gap, read by both the layout " +
                 "group's spacing and the row's own preferred-width calculation, so the two can never disagree.")]
        public float hudSlotSpacing = 10f;
        [Tooltip("Height of a slot's icon/name area, in canvas units - the only part the weapon slot has; the " +
                 "three ability slots add Slot Cooldown Area Height below it.")]
        public float slotIconBoxHeight = 104f;
        [Tooltip("Extra height below the icon box, in canvas units, reserved for the charge pips and the " +
                 "block-reason text on the three ability slots. The weapon slot has neither and never adds this.")]
        public float slotCooldownAreaHeight = 58f;
        [Tooltip("Height of the key strip (LMB / RMB / SPACE / SHIFT) across the top of a slot, in canvas units " +
                 "(Tudor, 2026-09-17: the key used to sit in the top-left corner). The icon and the ability name " +
                 "centre themselves in whatever is left of the icon box below it, so both read as centred in the " +
                 "square at once - which one shared rect could never do.")]
        public float slotKeyRowHeight = 30f;
        [Tooltip("Height of the block-reason line (\"recharging\", \"stunned\") under a slot's charge pips, in " +
                 "canvas units. Together with the pip row it has to fit inside Slot Cooldown Area Height above.")]
        public float slotReasonTextHeight = 26f;
        [Tooltip("Thickness of the border drawn around a weapon/ability slot, in canvas units (HUD step 2). The " +
                 "border is what carries the ready / blocked / active colour now: Tudor asked for the dark box " +
                 "behind the abilities to go, so the slot is a thin frame over a faint wash instead of a filled " +
                 "square. Raise it if the state colour is hard to see at a glance.")]
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
        [Tooltip("Width and height of one charge pip, in canvas units (HUD step 3 - this used to be a number " +
                 "typed into PlayerHud where no designer could reach it). Tudor asked for bigger indicators: at " +
                 "14 a pip is a tenth of a 140-unit slot, where the old 8 was a seventeenth. The pips sit in a " +
                 "row Pip Row Height tall, which has to be at least this plus twice Pip Outline Width.")]
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
                 "the AoE Zone can be thrown to the cursor once. Same colour as the READY text.")]
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
        [Tooltip("Colour of the STUNNED label and the thin bar shrinking under it.")]
        public Color statusStunnedColor = new Color(1f, 0.86f, 0.1f, 1f);
        [Tooltip("Colour of the SLOWED label and the thin bar shrinking under it - a cool blue so it never reads as the stun.")]
        public Color statusSlowedColor = new Color(0.4f, 0.7f, 1f, 1f);
        [Tooltip("Your own HUD label: how far below the middle of the screen it sits, in canvas units.")]
        public float statusHudOffsetY = 170f;
        [Tooltip("Your own HUD bar: width and height, in canvas units. Sits directly under the label.")]
        public Vector2 statusHudBarSize = new Vector2(220f, 8f);
        [Tooltip("Size of the label over a player's head (same units as the name above the health bar).")]
        public float statusOverheadTextSize = 15f;
        [Tooltip("How high above the health bar's centre the label sits, in overhead-canvas units (the name text sits at 5).")]
        public float statusOverheadLabelY = 15.5f;
        [Tooltip("How high above the health bar's centre the thin countdown bar sits, in overhead-canvas units - between the name and the label.")]
        public float statusOverheadBarY = 11f;
        [Tooltip("Overhead countdown bar: width and height, in overhead-canvas units (the health bar is 15 by 3).")]
        public Vector2 statusOverheadBarSize = new Vector2(15f, 1f);
        [Header("Shield wasted text (Invulnerability ultimate)")]
        [Tooltip("Text that pops over a player's head when their armed shield window ran out and nobody hit them.")]
        public string shieldWastedText = "Wasted";
        [Tooltip("Colour of the shield's \"Wasted\" text.")]
        public Color shieldWastedColor = new Color(0.75f, 0.78f, 0.85f, 1f);
        [Tooltip("How long the shield's \"Wasted\" text stays over the player's head, in seconds.")]
        public float shieldWastedSeconds = 1.2f;
        [Tooltip("Size of the \"Wasted\" text (same units as the name above the health bar).")]
        public float shieldWastedTextSize = 15f;
        [Tooltip("How high above the health bar's centre the \"Wasted\" text sits, in overhead-canvas units (the STUNNED label sits at 15.5, so this is clearly above it).")]
        public float shieldWastedY = 27f;
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
        [Tooltip("Background behind the loadout panel itself - its OWN colour, separate from Panel Colour (the HUD's), because a HUD panel sits over solid HUD chrome while this one sits over the game world: at Panel Colour's own opacity the world, and a player's overhead health/shield bar, showed straight through the middle of it (Task 9a review, 616x576 capture). Kept nearly opaque (~0.96) so nothing behind the modal panel is visible through it.")]
        public Color loadoutPanelColor = new Color(0.05f, 0.05f, 0.07f, 0.96f);
        [Tooltip("Space between the loadout panel's background edge and its title/columns, in canvas units, on every side.")]
        public float loadoutPanelPadding = 20f;
        [Tooltip("Extra vertical gap inserted above the 'Armor' heading, on top of the normal item spacing between it and the Reset Weapon button above it - at the normal spacing alone the heading read as crowding the button above it (Task 9a review, 616x576 capture).")]
        public float loadoutSectionGap = 16f;
        [Tooltip("Gap from the top of the screen to the top of the loadout panel, in canvas units. The panel is anchored to the TOP of the screen rather than dead-centre so a tall tree/armor column never grows down into the HUD, which sits at the bottom.")]
        public float loadoutPanelTopMargin = 24f;
        [Tooltip("Width of the shop's content area under the tabs, in canvas units - the same on both pages, so the panel never changes size when you switch tabs. Wide enough for the four weapon-tree columns (Loadout Node Width and Loadout Tree Column Gap) and for the three ability columns (Loadout Ability Card Width, two cards across).")]
        public float loadoutPageWidth = 1700f;
        [Tooltip("Height of the shop's content area under the tabs, in canvas units - the same on both pages. Must fit the weapon tree with its Reset Weapon button and the Abilities & Armor page's tallest column.")]
        public float loadoutPageHeight = 540f;
        [Tooltip("Width of one weapon node button in the upgrade tree, in canvas units. The weapons page has the whole content area to itself, so the nodes are big enough to read at a glance; eight upgrades sit side by side (two under each of the four families), so eight of these plus the gaps must fit Loadout Page Width.")]
        public float loadoutNodeWidth = 180f;
        [Tooltip("Height of one weapon node button in the upgrade tree, in canvas units.")]
        public float loadoutNodeHeight = 96f;
        [Tooltip("Width of one ability card on the Abilities & Armor page, in canvas units. Two cards fit across each of the three columns.")]
        public float loadoutAbilityCardWidth = 240f;
        [Tooltip("Height of one ability card on the Abilities & Armor page, in canvas units.")]
        public float loadoutAbilityCardHeight = 88f;
        [Tooltip("Horizontal space between the Mobility, Attachment and Ultimate columns on the Abilities & Armor page, in canvas units.")]
        public float loadoutAbilityColumnGap = 60f;
        [Tooltip("Gap between the two upgrades of a weapon family (side by side), between ability cards in a column's grid (both directions), and between the armor rows and Reset Armor, in canvas units.")]
        public float loadoutNodeSpacing = 20f;
        [Tooltip("Gap between an armor upgrade's text (Absorb, Recharge) and its own + button, in canvas units. Keep it much smaller than Armor Row Gap, so each + reads as part of the text before it.")]
        public float loadoutArmorPlusGap = 12f;
        [Tooltip("Gap between the Absorb group (text + button) and the Recharge group on the Abilities & Armor page, in canvas units. Keep it much larger than Armor Plus Gap, so a + button is never mistaken for the other upgrade's.")]
        public float loadoutArmorRowGap = 90f;
        [Tooltip("Thickness of the highlight border drawn around the equipped weapon node or ability card, in canvas units.")]
        public float loadoutEquippedBorderWidth = 4f;
        [Tooltip("Width and height of a small square icon button - the close X and the armor +Absorb/+Recharge steppers - in canvas units. Raised from an original 44 (Task 9a review, 616x576 capture): a bigger button gives the bigger glyph below more room to stay legible.")]
        public float loadoutStepperButtonSize = 56f;
        [Tooltip("Font size for the glyph inside a stepper button (the close X, the armor + buttons), in canvas units - bigger than Body Text Size on purpose: at Body Text Size a '+' drawn this small read as a flat dash rather than a clear plus (Task 9a review, 616x576 capture).")]
        public float loadoutStepperFontSize = 32f;
        [Tooltip("Width of a labelled loadout button - Reset Weapon, Reset Armor - in canvas units.")]
        public float loadoutSmallButtonWidth = 170f;
        [Tooltip("Height of a labelled loadout button - Reset Weapon, Reset Armor - in canvas units.")]
        public float loadoutSmallButtonHeight = 42f;
        [Tooltip("Node fill for a weapon you can upgrade into right now.")]
        public Color loadoutSelectableColor = new Color(0.16f, 0.16f, 0.19f, 0.95f);
        [Tooltip("Node fill for a weapon you already passed through on the way to your current one.")]
        public Color loadoutOwnedColor = new Color(0.30f, 0.30f, 0.33f, 0.85f);
        [Tooltip("Node/card fill for a Selectable item the shop gate refuses right now (out of territory, in combat, or short of gold) - Task 2.5b review fix 1. Deliberately its OWN colour, distinct from Locked Colour: a shop-blocked item is still reachable (leave the zone, wait out combat, earn the gold) where a tree-Locked item genuinely cannot be picked yet, and the two used to be painted identically. Also distinct from the HUD's Slot Blocked Colour, which marks a HUD ability slot that cannot fire right now (dead/stunned/recharging) - a different question with its own look.")]
        public Color loadoutShopBlockedColor = new Color(0.22f, 0.14f, 0.05f, 0.92f);
        [Tooltip("Font size percentage (of Body/Small Text Size) for the price/status line under a weapon node or ability card's name - e.g. \"<size=70%>\" (Task 2.5b review fix 6: this used to be a magic string typed out at every call site).")]
        [Range(10f, 100f)] public float loadoutPriceLineSizePercent = 70f;
        [Tooltip("Seconds a refused click's reason (\"Need 700 more gold\", \"Out of combat in 2.4s\"...) stays shown in the header status line before it reverts to the ordinary gate status - Task 2.5b review fix 2. A click on a shop-blocked item used to do nothing visible at all.")]
        public float loadoutBlockedReasonDurationSeconds = 2f;
        [Tooltip("Second line on a weapon node that can only be reached by selling your current weapon path first. {0} = the new weapon's price, {1} = the gold selling back gives right now (the same number the Reset Weapon button shows). Not shown when there is nothing to sell back.")]
        public string loadoutSwapFormat = "Swap {0} (sell back +{1})";
        [Tooltip("Font size percentage for that swap line (smaller than Loadout Price Line Size Percent so it fits the node).")]
        [Range(10f, 100f)] public float loadoutSwapLineSizePercent = 55f;
        [Tooltip("Message after selling weapons with Reset Weapon. {0} = weapon name, {1} = gold refunded.")]
        public string loadoutSoldWeaponFormat = "Sold {0}: +{1} gold";
        [Tooltip("Message after selling armour upgrades with Reset Armor. {0} = gold refunded.")]
        public string loadoutSoldArmorFormat = "Sold armour upgrades: +{0} gold";
        [Tooltip("Seconds a 'Sold ...' message stays on the shop's status line.")]
        public float loadoutSoldMessageDurationSeconds = 3f;
        [Tooltip("Seconds the pointer must rest on a weapon node, ability card or armour row before its pop-up (name, what it does, numbers) appears next to the cursor (Task 13: 0.5 s). Moving off hides it at once; moving to another item restarts the wait.")]
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
        [Tooltip("The shop header's note in a Dominion break: picks are free there, no gold involved.")]
        public string loadoutDominionFreeText = "Free (break)";
        [Tooltip("The shop header's note for a player who joined a Dominion match in the middle of a round: they get one free pick before they spawn, outside the break.")]
        public string loadoutDominionLateJoinerFreeText = "Free (your one pick)";
        [Tooltip("What the shop says when it is asked to open (P, or the Loadout button) in a Dominion match outside the break, and what its header says if it is somehow open then. Shown as a short toast over the game when the shop stays shut.")]
        public string loadoutShopClosedText = "The shop opens in the break";
        [Tooltip("Armour row wording for a row that is at its own top level with upgrades still left in the shared limit.")]
        public string loadoutArmorTopLevelText = "top level";
        [Tooltip("The Absorb armour row. {0} = its level, {1} = the status text (which upgrade is next, or max).")]
        public string loadoutArmorAbsorbRowFormat = "Absorb lv {0}: {1}";
        [Tooltip("The Recharge armour row. {0} = its level, {1} = the status text (which upgrade is next, or max).")]
        public string loadoutArmorRechargeRowFormat = "Recharge lv {0}: {1}";
        [Tooltip("Colour of the arrows from each weapon to the upgrades it opens in the shop's weapon tree (neutral grey, Task 5b-2).")]
        public Color loadoutArrowColor = new Color(0.42f, 0.42f, 0.47f, 1f);
        [Tooltip("Thickness of those arrows, in canvas units.")]
        public float loadoutArrowWidth = 3f;
        [Tooltip("Length of the arrowhead at the upgrade end of each tree arrow, in canvas units.")]
        public float loadoutArrowHeadSize = 12f;
        [Tooltip("Vertical space between one weapon-tree row and the next, in canvas units - room for the arrows to read (Task 5b-2, widened in Task 13 now the tree has its own page).")]
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
        [Tooltip("Trail colour for each team, index = team id (0/1/2) - Teams.TryGetTeam's own numbering, " +
                 "read through ShotColorFor below rather than indexed directly so an out-of-range id falls " +
                 "back safely. NOT the same three colours as the player meshes (white/black/cyan): team 1's " +
                 "mesh is black, and a black trail would be invisible against the arena's dark walls, so its " +
                 "shot colour is chosen separately here instead of copied from the mesh.")]
        public Color[] teamShotColors = new Color[3]
        {
            new Color(0.93f, 0.97f, 1f, 1f),  // team 0 - cool near-white (mesh is white; a warm pale yellow
                                               // measured unreadable against the arena's orange/sand ground)
            new Color(0.68f, 0.32f, 1f, 1f),  // team 1 - violet (mesh is black - would be invisible as a trail)
            new Color(0.15f, 0.95f, 1f, 1f),  // team 2 - cyan (matches its mesh)
        };
        [Tooltip("Trail/tint colour used when the shooter's team could not be resolved - Teams.TryGetTeam " +
                 "returned -1. Should never actually appear in a real match; only a debug/test-range shot " +
                 "fired with no team assigned reads this. Deliberately dimmer AND lower alpha than every " +
                 "real team colour - both the trail's fade (reads straight off this colour's alpha) and " +
                 "the core's glow (ShotTeamVisuals.TintCore scales emission by this same alpha) key off " +
                 "it, so an unresolved shot reads as a faint, washed-out ghost rather than a fourth team " +
                 "colour. Two earlier versions both still read as a near-duplicate of Team 0's near-white " +
                 "once Team 0's own washed-out blue tint was fixed: a flat mid-grey at full alpha, then a " +
                 "light grey whose CORE glow (before emission also scaled by alpha) was still just as " +
                 "bright as a real team's (Task 11a follow-up review, 616x576 captures).")]
        public Color unknownTeamShotColor = new Color(0.55f, 0.55f, 0.58f, 0.5f);
        [Tooltip("How many seconds a shot's trail keeps fading behind it after the bullet itself is gone. " +
                 "Raised from an original 0.25 - at the game's normal camera zoom a shot crosses almost the " +
                 "whole visible frame in under half a second, and 0.25 left too little of the tail actually " +
                 "drawn to compare colours by (Task 11a follow-up review, 616x576 capture).")]
        public float trailTime = 0.35f;
        [Tooltip("Trail width where it meets the bullet, in metres. Raised from an original 0.09 - too thin " +
                 "a ribbon to read its colour at a glance at the game's normal camera zoom (Task 11a " +
                 "follow-up review, 616x576 capture).")]
        public float trailStartWidth = 0.15f;
        [Tooltip("Trail width at its fading tail end, in metres - thinner than Trail Start Width so the " +
                 "trail reads as tapering off rather than a solid ribbon.")]
        public float trailEndWidth = 0.02f;
        [Tooltip("Shared unlit material every shot trail renders with - Assets/Gameplay/UI/ShotTrail.mat, " +
                 "built the same way Task 7's Aim Cone Line Material was (URP Particles/Unlit, alpha " +
                 "transparent, no shadows). Its own colour stays white: every trail tints itself through " +
                 "TrailRenderer.colorGradient, which is what lets one material serve every team.")]
        public Material trailMaterial;
        [Tooltip("0 = the bullet's core keeps its own material colour, 1 = fully replaced by the team " +
                 "colour. How far ShotTeamVisuals lerps the core's tint toward Shot Color For. Raised from " +
                 "an original 0.65, which against Boolet Weapon.mat's ORIGINAL blue base colour left the " +
                 "core reading as a washed-out version of that old blue for every team rather than the " +
                 "team's own colour - fixed together with turning that base colour neutral white below, so " +
                 "the tint now has a true white to blend from instead of fighting a saturated blue " +
                 "(Task 11a follow-up review, 616x576 capture).")]
        [Range(0f, 1f)] public float bulletTintStrength = 0.9f;
        [Tooltip("Emission brightness multiplier on the bullet core's team colour, so the core itself - not " +
                 "just its trail - reads as a bright, glowing shot rather than a flat-lit sphere at a " +
                 "glance. 1 = no boost over the plain team colour. Raised from an original 2.4 - the scene's " +
                 "Bloom (Assets/Settings/SampleSceneProfile.asset, threshold 1.0) only blooms a pixel whose " +
                 "linear value clears that threshold, and 2.4 left the dimmer channels of some team colours " +
                 "under it (Task 11a follow-up review).")]
        public float bulletEmission = 5f;

        /// <summary>The trail/tint colour for a shot fired by teamId; Unknown Team Shot Colour for an unresolved
        /// (-1) or out-of-range id - the same fail-open reading ShooterTeamId carries in the rest of the weapons code.</summary>
        public Color ShotColorFor(int teamId)
        {
            if (teamShotColors != null && teamId >= 0 && teamId < teamShotColors.Length)
                return teamShotColors[teamId];

            return unknownTeamShotColor;
        }

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
            int bucket = (teamShotColors != null && teamId >= 0 && teamId < teamShotColors.Length) ? teamId : -1;

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
        [Tooltip("Width of the wind-up warning line the instant the trigger is pulled, in metres. Grows to " +
                 "Laser Warning End Width over the wind-up, so the line visibly thickens as the beam gets " +
                 "closer to firing - part of the Task 11b telegraph (design reversed the earlier no-warning " +
                 "call, see IgnoreWalls.cs).")]
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
        [Tooltip("Gap between the top of the 'Loadout (P)' button and the gold readout sitting above it, in " +
                 "canvas units. Tudor, 2026-09-17: gold and the shop are the same system, so the readout moved " +
                 "out of the ability bar and up against the button that spends it.")]
        public float goldShopGap = 8f;
        [Tooltip("Font size of the income line (\"+7.7/s\") as a percentage of the balance line above it. The " +
                 "balance is the number you act on; the income is context, so it is deliberately smaller - the " +
                 "same relationship Loadout Price Line Size Percent gives a shop item's price.")]
        [Range(30f, 100f)] public float goldIncomeSizePercent = 75f;

        [Header("Bounty toast")]
        [Tooltip("Text colour of the transient \"Bounty +900\" toast shown when a capture pays your " +
                 "team a bounty (Task 2.4, GDD p.20) - the same warm amber family as Gold Text " +
                 "Colour, so a bounty reads as an emphatic version of the same gold reward rather " +
                 "than an unrelated alert colour.")]
        public Color bountyToastColor = new Color(1f, 0.82f, 0.2f, 1f);
        [Tooltip("Seconds the bounty toast stays on screen before it hides itself again.")]
        public float bountyToastDurationSeconds = 3f;

        [Header("Capital under attack (Tudor, 2026-09-16)")]
        [Tooltip("Shown on the respawn panel while a player waits to respawn and their capital is currently " +
                 "under attack, so their coming respawn will land at their Tier 2 zone instead of the capital.")]
        public string capitalUnderAttackRespawnNote = "Your capital is under attack - you will respawn at your Tier 2 zone";
        [Tooltip("HUD toast shown right after a player respawns at their capital's Tier 2 zone because the " +
                 "capital was under attack. Uses the same toast label and duration as the bounty payout " +
                 "(Bounty Toast Duration Seconds above).")]
        public string capitalUnderAttackRespawnToast = "Respawned at Tier 2: capital under attack";
        [Tooltip("Font size of the capital-under-attack respawn note, in canvas units (B3 review, 2026-09-16) - " +
                 "its own dedicated size rather than Body Text Size: plain white text at that size read too " +
                 "faint against the respawn panel's pale salmon wash (616x576 capture) to notice at a glance.")]
        public float capitalUnderAttackNoteFontSize = 30f;
        [Tooltip("Text colour of the capital-under-attack respawn note (B3 review, 2026-09-16).")]
        public Color capitalUnderAttackNoteColor = Color.white;
        [Tooltip("Background strip drawn behind the capital-under-attack respawn note (B3 review, 2026-09-16) - " +
                 "the same readability trick the HUD's own Panel Colour gives every bar/slot group, applied here " +
                 "because the note otherwise fights the respawn panel's own pale wash instead of standing out " +
                 "against it. Semi-opaque dark so the note still reads as sitting ON the respawn panel, not as a " +
                 "second, unrelated overlay.")]
        public Color capitalUnderAttackNoteBackingColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("Phase transition (Task 2.7)")]
        [Tooltip("HUD toast shown to every surviving player the instant the match narrows from three " +
                 "teams to two (Tudor D17: the last stand is the same with two teams - a team with no base can't " +
                 "respawn and is out once every member is dead, unless it retakes or takes a base). Uses the same " +
                 "toast label and duration as the bounty payout (Bounty Toast Duration Seconds above). Keep it to two " +
                 "lines (about 52 characters): the toast label grows upward and clips at the top of the screen.")]
        public string twoTeamsLeftBannerText = "Two teams left: lose your base and you can't respawn";

        [Tooltip("The panel a dead player sees while their team holds no base and nobody on it can respawn - shown by " +
                 "MatchUI in place of the text baked into the waiting panel (Task 9b-2: one home for texts).")]
        public string waitingPanelText = "Waiting for team to \n\ncapture Base territory ";

        [Header("Capture ring (2026-09-16)")]
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
        [Tooltip("How many straight pieces make up each ring. More reads as a smoother circle; 96 is smooth at every zoom.")]
        [Range(16, 256)] public int captureRingSegments = 96;
        [Tooltip("The dark loop behind a capture's progress band, so how full it is reads like a loading bar.")]
        public Color captureRingTrackColor = new Color(0f, 0f, 0f, 0.4f);

        [Header("Minimap (2026-09-16)")]
        [Tooltip("Size (bounding diameter) of the triangular minimap in the top-right corner, in canvas units.")]
        public float minimapCornerSize = 340f;
        [Tooltip("Gap between the corner minimap's frame and the top and right screen edges, in canvas units.")]
        public float minimapCornerMargin = 24f;
        [Tooltip("Diameter of the large map shown in the middle of the screen while M is toggled on, in canvas units. " +
                 "Everything on it (bubbles, lines, labels) scales up from the corner sizes by the same amount. Shrinks " +
                 "to fit above Large Bottom Clearance if this would otherwise overlap it.")]
        public float minimapLargeSize = 860f;
        [Tooltip("Height of the strip left clear at the bottom of the screen for the HUD's ability bar while the large " +
                 "map (M) is open, in canvas units (controller review, 2026-09-17: the map used to cover the HUD). The " +
                 "large map's diameter shrinks below Large Size if it would otherwise overlap this strip, and the map " +
                 "centres itself in whatever space remains above it. HUD step 5 lowered it from 380 to 300 because the " +
                 "HUD itself is 20% smaller (Hud Scale) and the gold row left it.")]
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
        [Min(1f)] public float scanMinimapCountdownFontSize = 22f;
        [Tooltip("Where that countdown sits relative to the spot straight under the middle of the corner minimap, in canvas units: " +
                 "x moves it right, y moves it up (a negative y moves it further down, away from the map).")]
        public Vector2 scanMinimapCountdownOffset = new Vector2(0f, -4f);
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
        [Tooltip("How solid the small corner map is, 0 is invisible and 1 is fully opaque (Tudor, 2026-09-17: " +
                 "the corner map should be 20% more see-through, so 0.8). It multiplies everything on the map at " +
                 "once - the picture, the bubbles, the links and the markers.")]
        [Range(0f, 1f)] public float minimapCornerOpacity = 0.8f;
        [Tooltip("How solid the large map is while M is held open, 0 to 1. Tudor asked for full opacity here: " +
                 "you opened it on purpose, so nothing is hiding behind it that you would rather be looking at.")]
        [Range(0f, 1f)] public float minimapLargeOpacity = 1f;
        [Tooltip("How much opacity the large map gives up while you are moving, 0 to 1 (Tudor, 2026-09-17: " +
                 "\"decrease the opacity by 30%\"), so you can still see where you are running. Subtracted from " +
                 "Large Opacity above; the corner map never dims for movement.")]
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

        [Header("OverPower (Task 2.6, GDD p.20)")]
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

        [Header("Charge ring (2026-09-17)")]
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
        [Tooltip("Gap between the bottom of the corner minimap and the top of the F1 debug log, in SCREEN " +
                 "PIXELS (Tudor, 2026-09-17: the log used to open in the top-left corner, on top of the F1 test " +
                 "range panel). The gap is measured against the CORNER map's reserved space even while the large " +
                 "map is open, so the log does not jump every time someone presses M.")]
        public float debugLogGapBelowMinimapPixels = 6f;

        [Header("Match start (2.7b)")]
        [Tooltip("Only the colour the out-of-play paint path would use on a zone's ring and minimap bubble - and " +
                 "nothing shows that path in play any more: since 2026-09-25 an out-of-play zone (a host start's " +
                 "third capital, or any zone the phase-two cut closes) disappears entirely - tower, ring and " +
                 "minimap bubble - instead of being painted this colour. Kept for whichever paint path might use " +
                 "it again; not currently seen.")]
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
        [Tooltip("Fill colour of the green confirm button of the saved-log overlay (Open folder). Kept from the old Start button.")]
        public Color matchStartButtonColor = new Color(0.16f, 0.45f, 0.25f, 0.95f);
        [Tooltip("Width/height of the buttons of the saved-log overlay, in canvas units. Kept from the old Start button.")]
        public Vector2 matchStartButtonSize = new Vector2(260f, 52f);
        [Tooltip("Gap from the top of the screen to the top of the warm-up bar, in reference pixels. It must clear " +
                 "a two-line toast above it (the longest a warm-up can raise is the 'capital under attack' respawn " +
                 "toast), or the two run into each other.")]
        public float warmupTopOffset = 130f;
        [Tooltip("Toast shown the instant the match goes live with all three teams - the ordinary case.")]
        public string matchLiveToastText = "The match is live! Zones, gold, loadouts and respawn timers are reset.";
        [Tooltip("Toast shown the instant a host-started match goes live with two teams - lose your base and your team " +
                 "can't respawn until it retakes one (Tudor D17). Two lines at most (about 52 characters), as the bounty toast.")]
        public string matchLiveTwoTeamsToastText = "Two teams: lose your base and you can't respawn";

        [Header("Damage numbers (2026-09-18)")]
        [Tooltip("Pop a number beside an enemy each time your damage lands on them. Off hides them; nothing else about " +
                 "combat changes.")]
        public bool showDamageNumbers = true;
        [Tooltip("Canvas units, at the number's settled (post-pop) size.")]
        public float damageNumberTextSize = 30f;
        [Tooltip("An ordinary hit's number.")]
        public Color damageNumberColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("A hit that used up your mark (+50% damage) - both the bigger number and the mark diamond over " +
                 "an enemy you've marked share this colour. Keep it apart from the team colours and " +
                 "Immune Bar Colour.")]
        public Color markColor = new Color(1f, 0.45f, 0.1f, 1f);
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
        [Tooltip("Tudor's override on the Mark plan (2026-09-18): one live number per enemy, not one per hit - every " +
                 "new hit on the same enemy ADDS to its number, re-pops it and restarts this clock. This is how long " +
                 "the number stays fully solid (no rise, no fade) after the LAST hit that touched it before it starts " +
                 "rising and fading over Damage Number Lifetime Seconds above. While you keep damaging one enemy its " +
                 "number just keeps counting up in place.")]
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
        [Tooltip("Colour of the bubble around a player who has just respawned in Dominion (their respawn shield), seen by everyone who can see " +
                 "that player. Blue on purpose, so it never reads as the yellow Invulnerability look. Its alpha is how see-through the bubble is; " +
                 "it starts at the same alpha the Invulnerability bubble uses. The bubble's size is set in the Dominion Config (Shield Bubble Scale).")]
        public Color respawnShieldColor = new Color(0.3098f, 0.6392f, 1f, 0.5f);
        [Tooltip("The word that pops over a player whose Dominion respawn shield just stopped a hit, seen by everyone who can see that player. " +
                 "Separate from Blocked Text above, which belongs to the Invulnerability ultimate and stays as it is.")]
        public string respawnShieldBlockedText = "BLOCKED";
        [Tooltip("Colour of the respawn shield's BLOCKED word. The shield's blue on purpose, so it never reads as the grey Invulnerability Blocked.")]
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
        [Tooltip("Each team's colour in the Dominion HUD (the underline of its score, its round-win dots, its name on the break card), in team order: white, purple, cyan.")]
        public Color[] dominionTeamColors = { new Color(0.957f, 0.949f, 0.929f, 1f), new Color(0.42f, 0.247f, 0.749f, 1f), new Color(0.169f, 0.722f, 0.769f, 1f) };
        [Tooltip("Each team's lighter colour for writing its name or number on a dark card (a plain purple is too dark to read there), in team order: white, purple, cyan.")]
        public Color[] dominionTeamTextColors = { new Color(0.957f, 0.949f, 0.929f, 1f), new Color(0.612f, 0.482f, 0.878f, 1f), new Color(0.31f, 0.827f, 0.871f, 1f) };
        [Tooltip("The gold of the Dominion HUD's accents: the points of the centre payout, the break countdown.")]
        public Color dominionGoldColor = new Color(0.91f, 0.725f, 0.192f, 1f);
        [Tooltip("The dim grey of an empty round-win dot and of the small labels on the Dominion cards.")]
        public Color dominionDimColor = new Color(0.557f, 0.545f, 0.522f, 1f);
        [Tooltip("The soft light grey of the small headings on the Dominion HUD and cards (ROUND 2 OF 3, ROUND 1).")]
        public Color dominionMutedColor = new Color(0.725f, 0.714f, 0.682f, 1f);

        [Header("Dominion HUD: round bar (top of the screen during a round)")]
        [Tooltip("How far the round bar sits below the top edge of the screen.")]
        public float dominionBarTop = 30f;
        [Tooltip("The dark box behind the round bar.")]
        public Color dominionBarFill = new Color(0.059f, 0.063f, 0.078f, 0.75f);
        [Tooltip("The rounded corners of the round bar.")]
        public float dominionBarRadius = 12f;
        [Tooltip("Height of the round bar.")]
        public float dominionBarHeight = 126f;
        [Tooltip("The slightly darker box behind the round clock in the middle of the bar.")]
        public Color dominionBarClockFill = new Color(0f, 0f, 0f, 0.25f);
        [Tooltip("Width of each team's score block in a 2v2 match (the clock sits between the two).")]
        public float dominionBarSideWidth = 270f;
        [Tooltip("Width of the clock block in a 2v2 match.")]
        public float dominionBarClockWidth = 255f;
        [Tooltip("Width of each team's score block in a 3v3v3 match (three blocks, then the clock).")]
        public float dominionBar3SideWidth = 195f;
        [Tooltip("Width of the clock block in a 3v3v3 match.")]
        public float dominionBar3ClockWidth = 225f;
        [Tooltip("Thickness of the line under each team's score, in the team's colour.")]
        public float dominionBarEdgeThickness = 6f;
        [Tooltip("In a 2v2 match, how far a score sits from the clock side of its block.")]
        public float dominionBarScoreInset = 27f;
        [Tooltip("Size of a team's points in a 2v2 match.")]
        public float dominionScoreSize = 51f;
        [Tooltip("Size of a team's points in a 3v3v3 match (smaller, there are three).")]
        public float dominionScore3Size = 45f;
        [Tooltip("Size of the round clock in a 2v2 match.")]
        public float dominionClockSize = 54f;
        [Tooltip("Size of the round clock in a 3v3v3 match.")]
        public float dominionClock3Size = 48f;
        [Tooltip("Size of the small heading over the clock (ROUND 2 OF 3).")]
        public float dominionRoundLabelSize = 18f;
        [Tooltip("Extra space between the letters of the small headings, in reference pixels.")]
        public float dominionLabelSpacing = 3f;
        [Tooltip("Width and height of one round-win dot.")]
        public float dominionDotSize = 18f;
        [Tooltip("Space between two round-win dots.")]
        public float dominionDotGap = 9f;
        [Tooltip("Thickness of the ring of an empty round-win dot.")]
        public float dominionDotRing = 3f;
        [Tooltip("What the small heading over the round clock says. {0} = the round, {1} = how many rounds there are.")]
        public string dominionRoundLabelFormat = "ROUND {0} OF {1}";
        [Tooltip("What the round bar's clock slot says once the rounds are over and sudden death is on (there is no clock then).")]
        public string dominionBarSuddenText = "SUDDEN DEATH";
        [Tooltip("Size of the words in the clock slot during sudden death.")]
        public float dominionBarSuddenSize = 30f;
        [Tooltip("What the small heading over the round clock says during overtime, in place of the round number. The clock under it then counts the overtime's own minute down.")]
        public string dominionBarOvertimeText = "OVERTIME";
        [Tooltip("What flashes by the scores when the centre pays out. {0} = the points, {1} = the team's name in capitals.")]
        public string dominionFlashFormat = "+{0} {1}";
        [Tooltip("Size of the centre payout flash.")]
        public float dominionFlashSize = 36f;
        [Tooltip("How long the centre payout flash stays up, in seconds.")]
        public float dominionFlashSeconds = 2.5f;
        [Tooltip("How long the centre payout flash takes to fade out at the end of its time, in seconds.")]
        [Min(0.01f)] public float dominionFlashFadeSeconds = 0.5f;
        [Tooltip("Gap between the bottom of the round bar and the centre payout flash under it.")]
        public float dominionFlashGap = 6f;
        [Tooltip("How far below the top of the round bar a team's score starts.")]
        public float dominionScoreTop = 12f;
        [Tooltip("Gap between a team's score and its round-win dots under it.")]
        public float dominionDotsGap = 4f;
        [Tooltip("How far below the top of the bar the 'ROUND 2 OF 3' label sits, as a share of the bar's height (0.16 = a sixth).")]
        [Range(0f, 0.5f)] public float dominionRoundLabelTopShare = 0.16f;

        [Header("Dominion HUD: break card (between rounds)")]
        [Tooltip("How far the break card sits below the top edge of the screen. It sits high so the arena stays in view; the shop (P) opens on top of it.")]
        public float dominionBreakTop = 195f;
        [Tooltip("The dark box of the break card and the sudden-death banner.")]
        public Color dominionCardFill = new Color(0.059f, 0.063f, 0.078f, 0.85f);
        [Tooltip("The rounded corners of the break card.")]
        public float dominionCardRadius = 15f;
        [Tooltip("Empty space inside the break card, left and right, then top and bottom.")]
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
        [Tooltip("Size of the PICK YOUR BUILD button's words.")]
        public float dominionBreakButtonSize = 30f;
        [Tooltip("Height of the PICK YOUR BUILD button.")]
        public float dominionBreakButtonHeight = 72f;
        [Tooltip("Space left and right of the words inside the PICK YOUR BUILD button.")]
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
        [Tooltip("The big line of the break card when an overtime ran out and the round was shared. {0} = the names of the teams that share it, in capitals, joined by the Names Separator.")]
        public string dominionBreakSharedFormat = "SHARED · {0}";
        [Tooltip("What sits between the team names on the break card when a round is shared (WHITE + PURPLE).")]
        public string dominionBreakNamesSeparator = " + ";
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

        [Header("Dominion HUD: under the minimap")]
        [Tooltip("Gap between the minimap's lower edge and the Dominion lines under it (the centre countdown, the circle countdown).")]
        public float dominionUnderMinimapGap = 12f;
        [Tooltip("Size of the centre countdown line (CENTRE +200 IN 12).")]
        public float dominionCentreSize = 30f;
        [Tooltip("Size of the line saying who holds the centre.")]
        public float dominionCentreHolderSize = 21f;
        [Tooltip("The centre countdown under the minimap in a 3v3v3 match. {0} = the points the centre pays, {1} = seconds to the payout. The tags colour the points gold.")]
        public string dominionCentreFormat = "CENTRE <color=#E8B931>+{0}</color> IN {1}";
        [Tooltip("Who holds the centre. {0} = the team's name.")]
        public string dominionCentreHoldsFormat = "{0} holds it";
        [Tooltip("What the line says when nobody holds the centre.")]
        public string dominionCentreNobodyText = "Nobody holds it";
        [Tooltip("The line under the minimap while the sudden-death circle is still shrinking. {0} = the time left (m:ss).")]
        public string dominionShrinkFormat = "CIRCLE SHRINKS · {0}";
        [Tooltip("Size of the circle countdown line.")]
        public float dominionShrinkSize = 27f;

        [Header("Dominion HUD: score bars (bottom-right corner, during a round and overtime)")]
        [Tooltip("How long each team's score bar is, not counting the little box with its number in front of it.")]
        public float dominionScoreBarWidth = 255f;
        [Tooltip("How tall each team's score bar is.")]
        public float dominionScoreBarHeight = 33f;
        [Tooltip("The gap between one team's score bar and the next.")]
        public float dominionScoreBarGap = 9f;
        [Tooltip("The width of the little box in front of each bar that holds the team's points as a number.")]
        public float dominionScoreBarNumberWidth = 75f;
        [Tooltip("The size of the points number in that box. Small, so it never fights the round bar at the top.")]
        public float dominionScoreBarNumberSize = 24f;
        [Tooltip("How far above the gold readout (which sits over the Loadout (P) button) the score bars start, so the two never touch.")]
        public float dominionScoreBarsAboveGold = 12f;
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
        public int dominionResultSortingOrder = 5;

        [Header("Mark (2026-09-18)")]
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

        [Header("Towers (arena rebuild)")]
        [Tooltip("Colour of a tower's crown and column caps while nobody owns its zone. Owned towers use their team's " +
                 "colour (Team Shot Colors), a capital cut from a two-team match uses Out Of Play Zone Colour, and an " +
                 "attacked or drained tower pulses exactly like its ring on the ground. Keep it a plain mid grey: " +
                 "team 0 is near-white, so a light neutral would read as owned by team 0.")]
        public Color towerNeutralColor = new Color(0.42f, 0.42f, 0.42f, 1f);
        [Tooltip("Multiplies the owner's colour before TowerLook paints the Drum only (the tower's body) - the one " +
                 "piece still on the Lit 'Tower Stone' material, so it reads as the team colour with its own " +
                 "lighting/shading intact instead of going flat like everything else TowerLook paints (the Crown, " +
                 "caps, Plinth and shown shafts, all on the Unlit 'Tower Owner' material at the full colour, no " +
                 "shade - Tudor, 2026-09-23: 'the exterior collumns and the base... glow the same color as the " +
                 "top'). 1 would make the Drum just as bright/flat as the rest and lose the tower's silhouette; " +
                 "kept here next to Tower Neutral Colour because the two are only ever read together through " +
                 "OwnerPaintColours - chosen by capture, not calculation (Rule 6), see captures/towers-2026-09-21 " +
                 "and captures/tower-glow-2026-09-23.")]
        [Range(0f, 1f)] public float towerBodyShade = 0.8f;
        [Tooltip("How brightly an OWNED tower's crown, caps, columns and base glow in the owner's colour. 1 = " +
                 "flat colour, no glow; 1.3 = soft; 1.5 = clear glow; 1.75+ = strong, and violet starts turning " +
                 "lilac. Neutral and out-of-play towers never glow, so a glow means owned. The drum never glows.")]
        [Range(1f, 2.5f)] public float towerOwnerGlow = 1.5f;

        [Header("Playtest extras (2026-09-26)")]
        [Tooltip("HUD toast shown on Ctrl+B - 'a bug just happened' (BugMarkerKey): a screenshot is " +
                 "saved next to this client's own telemetry log, and its own `bug` line is written. " +
                 "Uses the same transient toast label every other HUD toast shares (PlayerHud.ShowToast).")]
        public string bugMarkedText = "Bug marked - type what happened in chat";
        [Tooltip("Escape's pop-up question (QuitConfirmPanel), shown only when neither the shop nor " +
                 "chat claimed the key this frame or the frame before.")]
        public string quitPromptText = "Close the game?";
        [Tooltip("QuitConfirmPanel's confirm button - runs the shared GameQuit.Quit() (zips this " +
                 "client's own match log if needed, then disconnects and quits).")]
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
        public int scoreboardSortingOrder = 30;
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
        [Tooltip("Each team's name on the scoreboard, in team order (White, Purple, Cyan). Each one is drawn in that " +
                 "team's Team Shot Colour.")]
        public string[] scoreboardTeamNames = { "White", "Purple", "Cyan" };
        [Tooltip("Shown for a team the scoreboard cannot name (a player whose team has not arrived yet).")]
        public string scoreboardUnknownTeamText = "Joining";

        [Header("Connection lost and rejoin (Task 9e, Tudor D21)")]
        [Tooltip("Title of the panel that appears when your connection to the match drops (ConnectionLostPanel).")]
        public string connectionLostTitle = "Connection lost";
        [Tooltip("The line under the title: what Rejoin does for you.")]
        public string connectionLostBody = "Rejoin to come back to the same match as the same player - same team, gold and loadout. You respawn as after a death.";
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
        [Tooltip("Label of the same button while the match is still running (a knocked-out player's lose screen): it closes the game.")]
        public string resultButtonQuit = "Quit";
        [Tooltip("Width of the connection lost panel, in reference pixels.")]
        public float connectionLostPanelWidth = 640f;
        [Tooltip("Fill colour of the connection lost panel.")]
        public Color connectionLostPanelColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        [Tooltip("Colour of the connection lost title.")]
        public Color connectionLostTitleColor = new Color(1f, 0.55f, 0.35f, 1f);
        [Tooltip("Text size of the connection lost title, in reference pixels.")]
        [Min(8f)] public float connectionLostTitleSize = 34f;
        [Tooltip("Text size of the line under the title, in reference pixels.")]
        [Min(8f)] public float connectionLostBodySize = 22f;
        [Tooltip("Size of the connection lost panel's Rejoin / Leave / OK buttons, in reference pixels.")]
        public Vector2 rejoinButtonSize = new Vector2(260f, 52f);
        [Tooltip("Size of the name screen's Rejoin your match button, in reference pixels. Wider than the panel's buttons: the " +
                 "name screen's font is wide and would wrap the label.")]
        public Vector2 rejoinMatchButtonSize = new Vector2(420f, 52f);
        [Tooltip("Fill colour of the Rejoin buttons - the same affirmative green the match Start button uses.")]
        public Color rejoinButtonColor = new Color(0.16f, 0.45f, 0.25f, 0.95f);
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

        [Header("Spectate (Task 9g, Tudor D28)")]
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

        [Header("Spectator bar (lobby Task 6, board 8)")]
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
        [Tooltip("Fill colour of the spectator result card.")]
        public Color spectatorResultCardColor = new Color(0.06f, 0.063f, 0.078f, 0.92f);
        [Tooltip("Empty space inside the spectator result card, left and right, then top and bottom, in reference pixels.")]
        public Vector2 spectatorResultCardPadding = new Vector2(64f, 36f);
        [Tooltip("How far the spectator result card sits below the top of the screen, in reference pixels.")]
        public float spectatorResultCardTop = 150f;
        [Tooltip("Space between the title and the button on a result card (the spectator's match result), in reference pixels.")]
        public float spectatorResultCardGap = 24f;
        [Tooltip("Letter spacing of the small heading above a result card's big line (and the Dominion break card's heading), as a share of the heading's size.")]
        public float resultHeadingSpacingShare = 0.23f;
        [Tooltip("How small a long big line on a fixed-width result card may shrink, as a share of its normal size (0.4 = down to 40%), before it is cut off.")]
        [Range(0.1f, 1f)] public float resultTitleMinSizeShare = 0.4f;

        [Header("Lobby screens (lobby Task 9)")]
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
        public string[] createFamilyNotes = { "Capture territory for gold, take capitals, be the last team standing.", "Hold zones to score points over three rounds and out-score the other teams." };
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

        [Header("Lobby room (lobby Task 10)")]
        [Tooltip("Fill of a taken seat.")]
        public Color lobbyRoomSeatFill = new Color(0.2f, 0.212f, 0.247f, 1f);
        [Tooltip("Fill of the No role box.")]
        public Color lobbyRoomSideFill = new Color(0.125f, 0.133f, 0.165f, 1f);
        [Tooltip("Outline of the No role box.")]
        public Color lobbyRoomSideBorder = new Color(0.2f, 0.212f, 0.247f, 1f);
        [Tooltip("The Spectators heading.")]
        public Color lobbyRoomHeadingColor = new Color(0.847f, 0.835f, 0.808f, 1f);
        [Tooltip("The coloured edge on top of each team column, by team number (White, Purple, Cyan).")]
        public Color[] lobbyRoomTeamColors = { new Color(0.957f, 0.949f, 0.929f, 1f), new Color(0.42f, 0.247f, 0.749f, 1f), new Color(0.169f, 0.722f, 0.769f, 1f) };
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

        [Header("Lobby screens: layout, words (lobby Task 9 review)")]
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

        [Header("Warm-up bar (lobby Task 10)")]
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

        [Header("Chat (lobby Task 11, board 7A)")]
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

        [Header("Lobby screens: layout numbers (lobby Task 10 review)")]
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

        [Header("How to play and the game mode info page (lobby Task 12)")]
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
    }
}
