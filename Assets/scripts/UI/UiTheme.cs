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
        [Tooltip("Background behind HUD groups (the slots row, the bars panel). NOT the loadout screen - that " +
                 "reads its own Loadout Panel Colour below instead; see that field's tooltip for why the two are " +
                 "kept separate.")] public Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.85f);
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

        [Header("Capture bar")]
        [Tooltip("Width of the small world-space bar shown above a tower while it is being captured or drained, in world units (metres) - NOT canvas units, since this bar is not on the screen-space HUD.")]
        public float captureBarWidth = 2.2f;
        [Tooltip("Height of the capture bar, in world units (metres).")]
        public float captureBarHeight = 0.22f;
        [Tooltip("Height above the tower's own transform (its scene origin, NOT the flag mesh's own height) the capture bar sits at, in world units (metres). Measured against the scene's own towers (Task 2.1d): every tower's roof peaks at ~5.7m and its flag sits at ~5.6-5.7m, so this must clear that or the bar renders behind/inside the roof and never shows.")]
        public float captureBarHeightOffset = 6.5f;
        [Tooltip("Empty part of the capture bar, drawn behind the coloured fill - the fill itself uses Shot Color For the capturing (or draining) team, same colours as that team's shots.")]
        public Color captureBarTrackColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);

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
