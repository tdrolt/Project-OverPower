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
        [Tooltip("Width of the ability-picks column on the right, in canvas units - empty until Task 9b fills it in; reserved now so the panel does not visibly resize when that task adds content.")]
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
                 "fired with no team assigned reads this.")]
        public Color unknownTeamShotColor = new Color(0.5f, 0.5f, 0.53f, 1f);
        [Tooltip("How many seconds a shot's trail keeps fading behind it after the bullet itself is gone.")]
        public float trailTime = 0.25f;
        [Tooltip("Trail width where it meets the bullet, in metres.")]
        public float trailStartWidth = 0.09f;
        [Tooltip("Trail width at its fading tail end, in metres - thinner than Trail Start Width so the " +
                 "trail reads as tapering off rather than a solid ribbon.")]
        public float trailEndWidth = 0.01f;
        [Tooltip("Shared unlit material every shot trail renders with - Assets/Gameplay/UI/ShotTrail.mat, " +
                 "built the same way Task 7's Aim Cone Line Material was (URP Particles/Unlit, alpha " +
                 "transparent, no shadows). Its own colour stays white: every trail tints itself through " +
                 "TrailRenderer.colorGradient, which is what lets one material serve every team.")]
        public Material trailMaterial;
        [Tooltip("0 = the bullet's core keeps its own material colour, 1 = fully replaced by the team " +
                 "colour. How far ShotTeamVisuals lerps the core's tint toward Shot Color For.")]
        [Range(0f, 1f)] public float bulletTintStrength = 0.65f;
        [Tooltip("Emission brightness multiplier on the bullet core's team colour, so the core itself - not " +
                 "just its trail - reads as a bright, glowing shot rather than a flat-lit sphere at a " +
                 "glance. 1 = no boost over the plain team colour.")]
        public float bulletEmission = 2.4f;

        /// <summary>The trail/tint colour for a shot fired by teamId, or Unknown Team Shot Colour for an
        /// id Teams.TryGetTeam could not resolve (-1) or that falls outside Team Shot Colors - the same
        /// fail-open reading ShooterTeamId already carries everywhere else in the weapons code.</summary>
        public Color ShotColorFor(int teamId)
        {
            if (teamShotColors != null && teamId >= 0 && teamId < teamShotColors.Length)
                return teamShotColors[teamId];

            return unknownTeamShotColor;
        }

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
