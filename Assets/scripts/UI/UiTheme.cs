using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Every visual value the HUD, the loadout screen, the aim cone and the overhead bars share, in one asset so
    /// readability is tuned in one place - plus, since Task 11a/11b, every shot's trail/core tint (ShotTeamVisuals)
    /// and a laser's wind-up warning line and fired beam (Hitscan). Also the one home for the reference
    /// resolution/match value a scene-built canvas that is not PlayerHud's own (chat's canvases) scales by, through
    /// ThemedCanvasScaler - see that component's class comment. Presentation only - no gameplay number belongs here.
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
        [Tooltip("Slot BORDER colour while the ability is active - a channel, a dash mid-flight, sprint held. " +
                 "Unchanged by HUD step 2: at full alpha it already reads as a lit frame.")]
        public Color slotActiveGlowColor = new Color(1f, 0.85f, 0.25f, 1f);
        [Tooltip("Charge pip colour when that charge is available.")]
        public Color pipAvailableColor = Color.white;
        [Tooltip("Charge pip colour when that charge is spent.")]
        public Color pipSpentColor = new Color(1f, 1f, 1f, 0.15f);
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
        public float loadoutPanelTopMargin = 40f;
        [Tooltip("Width of the weapon-tree/armor column on the left, in canvas units - fixed so the ability column on the right (Task 9b) lines up beside it instead of both fighting over leftover space.")]
        public float loadoutLeftColumnWidth = 640f;
        [Tooltip("Width of the ability-picks column on the right, in canvas units - holds the Mobility/Equipment/Ultimate card grids (see LoadoutScreen.BuildAbilitiesUi). Kept equal to Loadout Left Column Width so neither column reads as the odd one out.")]
        public float loadoutRightColumnWidth = 640f;
        [Tooltip("Width of one weapon node button in the upgrade tree, in canvas units - also the width of one ability card in the right-hand column's grids, so both columns read as the same kind of pickable button.")]
        public float loadoutNodeWidth = 130f;
        [Tooltip("Height of one weapon node button in the upgrade tree, in canvas units - also the height of one ability card in the right-hand column's grids.")]
        public float loadoutNodeHeight = 64f;
        [Tooltip("Gap between sibling weapon nodes - side to side in the branches row, and top to bottom in a branch's own stack of children - in canvas units. Also the gap between ability cards in the right-hand column's grids, both directions.")]
        public float loadoutNodeSpacing = 14f;
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
        [Tooltip("Fixed height of the hover-description strip under the weapon/ability columns, in canvas units - fixed so switching between a short weapon hover and a long ability description never resizes the panel around it. Generous enough for a three-line name/description/numbers block; a longer hover string overflows past it rather than growing it.")]
        public float loadoutDescriptionPanelHeight = 150f;
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
                                               // was tried first and measured unreadable against the arena's
                                               // orange/sand ground - Task 11a review, 616x576 capture)
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

        /// <summary>The trail/tint colour for a shot fired by teamId, or Unknown Team Shot Colour for an
        /// id Teams.TryGetTeam could not resolve (-1) or that falls outside Team Shot Colors - the same
        /// fail-open reading ShooterTeamId already carries everywhere else in the weapons code.</summary>
        public Color ShotColorFor(int teamId)
        {
            if (teamShotColors != null && teamId >= 0 && teamId < teamShotColors.Length)
                return teamShotColors[teamId];

            return unknownTeamShotColor;
        }

        // Not serialized - rebuilt lazily the first time each bucket is asked for, then reused for the
        // rest of the play session. Every client simulates every projectile (a nine-player SMG burst is a
        // lot of bullets), so ShotTeamVisuals must not allocate a new Gradient per shot - see GradientFor.
        [System.NonSerialized] private Dictionary<int, Gradient> cachedShotGradients;

        /// <summary>Playtest polish review fix 3: without this, editing Team Shot Colors (or the
        /// unknown-team fallback) in the Inspector while the Editor is open kept handing out the
        /// OLD Gradient objects until the next domain reload - a live colour tweak looked like it
        /// did nothing. Clearing the cache here just means the next GradientFor call rebuilds it
        /// from the field's new value; it costs nothing at runtime, since a build never calls
        /// OnValidate at all.</summary>
        private void OnValidate() => cachedShotGradients = null;

        /// <summary>The same colour ShotColorFor(teamId) returns, pre-built into the two-key fade-to-
        /// transparent Gradient a shot's TrailRenderer wants, and cached by resolved bucket (0/1/2, or -1
        /// for every unresolved id) so two calls for the same team return the exact same Gradient object
        /// instead of allocating a fresh one - see the class comment on why that matters here.</summary>
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
                 "teams to two (GDD p.20-21: losing your capital now eliminates your team at once, " +
                 "instead of starting a last stand). Uses the same toast label and duration as the " +
                 "bounty payout (Bounty Toast Duration Seconds above).")]
        public string twoTeamsLeftBannerText = "Two teams left - losing your capital now eliminates you";

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
        [Tooltip("Bubble diameter of a Tier 3 zone on the corner minimap, in canvas units (small in the GDD).")]
        public float minimapBubbleDiameterTier3 = 24f;
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
        [Tooltip("Size of a way-in arrowhead, in canvas units. It points from a team's zone toward the neutral zone next to it.")]
        public float minimapArrowheadSize = 12f;
        [Tooltip("Size of your own arrow on the minimap, in canvas units. It points where you face.")]
        public float minimapOwnMarkerSize = 18f;
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
        [Tooltip("A capital nobody is playing for - the third capital when the host starts a two-team match. Its ring on the " +
                 "ground and its minimap bubble take this colour. Keep it darker and more see-through than the neutral grey, " +
                 "so it reads as closed, not as ground you can take.")]
        public Color outOfPlayZoneColor = new Color(0.12f, 0.12f, 0.12f, 0.45f);
        [Tooltip("Warm-up line shown to everyone while fewer than two teams have a player yet - nothing counts, the shop is " +
                 "free, and the match starts on its own once all three teams are here.")]
        public string warmupWaitingText = "Warm-up: nothing counts yet and the shop is free. The match starts when all three teams have a player.";
        [Tooltip("Warm-up line shown to the HOST once exactly two teams have a player - Start now with two, or wait for a " +
                 "third team to arrive.")]
        public string warmupHostText = "Warm-up: two teams are here. Start now with two teams, or wait for a third.";
        [Tooltip("Warm-up line shown to everyone ELSE once exactly two teams have a player - the host has the Start button, " +
                 "not you.")]
        public string warmupGuestText = "Warm-up: waiting for the host to start, or for a third team.";
        [Tooltip("Shown to everyone while the countdown counts down to going live. {0} is the whole seconds left, on this " +
                 "client's own synced server clock - it MUST stay in the text, or the number never shows. The countdown's " +
                 "own length is GameplayConfig > Match Start Countdown Seconds.")]
        public string matchCountdownText = "Match starts in {0}";
        [Tooltip("The host's Start button label - shown only while exactly two teams have a player and nobody is still " +
                 "team-less.")]
        public string matchStartButtonText = "Start match (2 teams)";
        [Tooltip("Fill colour of the host's Start button. Deliberately its own colour, not Bar Track Colour (the ordinary " +
                 "grey button fill, e.g. Loadout) - Start is the one button that begins the match, so it must stand out.")]
        public Color matchStartButtonColor = new Color(0.16f, 0.45f, 0.25f, 0.95f);
        [Tooltip("Width/height of the host's Start button, in canvas units.")]
        public Vector2 matchStartButtonSize = new Vector2(260f, 52f);
        [Tooltip("Gap from the top of the screen to the top of the warm-up/countdown line, in canvas units. It must clear " +
                 "a two-line toast above it (the longest a warm-up can raise is the 'capital under attack' respawn " +
                 "toast), or the two run into each other.")]
        public float warmupTopOffset = 130f;
        [Tooltip("Width/height of the warm-up/countdown line's own box, in canvas units - the text wraps inside it. The " +
                 "host's Start button sits directly below this box.")]
        public Vector2 warmupLineSize = new Vector2(900f, 64f);
        [Tooltip("Toast shown the instant the match goes live with all three teams - the ordinary case.")]
        public string matchLiveToastText = "The match is live! Zones, gold, loadouts and respawn timers are reset.";
        [Tooltip("Toast shown the instant a host-started match goes live with two teams - losing your capital now means " +
                 "you're out.")]
        public string matchLiveTwoTeamsToastText = "The match is live with two teams: lose your capital and you're out.";

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

        /// <summary>Writes this theme's outline, weight and drop-shadow onto one shared TextMeshPro material -
        /// the one home for those seven numbers, called by PlayerHud, the loadout screen and the minimap, which
        /// each build exactly one material for every label they own (see PlayerHud.ApplyOutline's comment for why
        /// one shared material beats letting TMP clone one per label).
        ///
        /// The keyword is the part that is easy to get wrong: setting _UnderlayColor and friends does nothing at
        /// all until UNDERLAY_ON is enabled on the material, so the shadow silently never appears.</summary>
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
    }
}
