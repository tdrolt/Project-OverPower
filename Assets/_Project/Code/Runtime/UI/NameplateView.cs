using System.Collections.Generic;
using Overpower.Vision;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The plate over one player's head: a dark backing with a team-coloured edge, the name in TextMeshPro (crisp at any distance),
    /// and under it the health-and-armour bar PlayerHealth drives, cut by divider lines, with the armour-upgrade mark on the shield.
    /// Your own plate drops the name and splits the bar into a shield row over an HP row. Built at runtime on the canvas EnemyVisibility switches off in
    /// the fog, found by EnemyVisibility's own lookup, so an enemy's plate vanishes with their body; it adds no canvas of its own,
    /// which would draw outside that switch. PlayerHealth and PlayerNameTag both call Ensure: whichever runs first builds it.
    /// Placement is NameplateLayout's; numbers and colours are UiTheme's.
    /// </summary>
    public sealed class NameplateView : MonoBehaviour
    {
        private UiTheme theme;
        private float nameWidth;
        private bool barShown = true;
        private bool ownPlate;
        private float markArmour;
        private int markCells;
        private int shownAbsorb = -1;
        private int shownRecharge = -1;
        private readonly List<Image> markSquares = new List<Image>();
        private readonly List<Image> markChevrons = new List<Image>();
        private Image healthFill;
        private Image shieldFill;
        private Image track;
        private Image shieldTrack;
        private static readonly Dictionary<float, Sprite> squareSprites = new Dictionary<float, Sprite>();
        private static Sprite chevronSprite;
        private static float chevronStroke = -1f;

        // One material for every overhead text, as PlayerHud does for the HUD: letting TMP clone one per label would make a
        // material per player per label.
        private static Material sharedText;
        private static Material sharedTextSource;

        public TextMeshProUGUI NameText { get; private set; }
        public Image Backing { get; private set; }
        public Image[] Edges { get; private set; }
        public RectTransform Bar { get; private set; }
        public RectTransform Root { get; private set; }

        /// <summary>The player's plate, built the first time it is asked for; null when the player has no theme, overhead canvas
        /// or bar to build it from.</summary>
        public static NameplateView Ensure(PlayerHealth health)
        {
            if (health == null || health.Theme == null)
                return null;
            Canvas canvas = EnemyVisibility.FindOverheadCanvas(health);
            RectTransform bar = health.OverheadBar;
            if (canvas == null || bar == null)
                return null;

            NameplateView view = canvas.GetComponent<NameplateView>();
            if (view == null)
            {
                view = canvas.gameObject.AddComponent<NameplateView>();
                view.Build(health.Theme, canvas.transform, bar, health.OverheadHealthFill, health.OverheadShieldFill, health.OverheadTrack);
            }
            return view;
        }

        private void Build(UiTheme uiTheme, Transform canvas, RectTransform bar, Image healthFill, Image shieldFill, Image track)
        {
            theme = uiTheme;
            this.healthFill = healthFill;
            this.shieldFill = shieldFill;
            this.track = track;
            Root = NewRect("Nameplate", canvas);
            Root.SetAsFirstSibling();

            Backing = NewImage("Backing", Root);
            Backing.color = theme.nameplateBackingColor;
            Edges = new[]
            {
                NewEdge("Edge Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f)),
                NewEdge("Edge Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f)),
                NewEdge("Edge Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f)),
                NewEdge("Edge Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f)),
            };

            // Drawn in sibling order: backing and edges, then the bar, then the marks and the name.
            Bar = bar;
            Bar.SetParent(Root, false);
            if (healthFill != null)
                HealthLines = new BarDividers(healthFill.rectTransform, theme.nameplateSegmentLineWidth, theme.barSegmentLineColor);
            if (shieldFill != null)
                ArmourLines = new BarDividers(shieldFill.rectTransform, theme.nameplateSegmentLineWidth, theme.barSegmentLineColor);
            Marks = NewRect("Armour Marks", Root);
            NameText = BuildText(Root, "Name", theme, theme.nameplateNameSize, default);
            NameText.overflowMode = TextOverflowModes.Ellipsis;
            Relayout();
        }

        public void SetName(string playerName)
        {
            NameText.text = playerName ?? string.Empty;
            nameWidth = string.IsNullOrEmpty(playerName) ? 0f : NameText.GetPreferredValues(playerName).x;
            Relayout();
        }

        public void SetColours(Color nameColour, Color edgeColour)
        {
            NameText.color = nameColour;
            foreach (Image edge in Edges)
                edge.color = edgeColour;
        }

        /// <summary>The bar's divider lines (UiTheme.barSegmentSize apart): health's on the health fill, under the shield drawn over
        /// it; armour's on the shield fill, measured against the armour this player can hold, shown only where the shield reaches.</summary>
        public void ShowSegments(float maxHealth, float armourCapacity, float shieldFill)
        {
            float size = theme.barSegmentSize;
            if (HealthLines != null)
                HealthLines.Layout(maxHealth, size);
            if (ArmourLines != null)
            {
                ArmourLines.Layout(armourCapacity, size);
                ArmourLines.ShowUpTo(shieldFill);
            }
        }

        /// <summary>The armour upgrades this player bought, as one mark centred on the shield (ArmourMarks decides its shape), shown
        /// only while the shield holds armour. Called on every client: the levels reach every copy of a player through
        /// PlayerHealth.SetArmorLevels.</summary>
        public void ShowArmourMarks(int absorbLevel, int rechargeLevel, float armour)
        {
            bool changed = armour > 0f != markArmour > 0f;
            markArmour = armour;
            if (changed)
                Marks.gameObject.SetActive(ArmourMarks.Shown(barShown, markCells, markArmour));
            if (absorbLevel == shownAbsorb && rechargeLevel == shownRecharge)
                return;
            shownAbsorb = absorbLevel;
            shownRecharge = rechargeLevel;

            ArmourMarks.Marks m = ArmourMarks.Compose(absorbLevel, rechargeLevel);
            markCells = m.cells;
            float size = theme.nameplateArmourMarkSize;

            for (int i = 0; i < Mathf.Max(m.squares, markSquares.Count); i++)
            {
                if (i >= m.squares)
                {
                    markSquares[i].gameObject.SetActive(false);
                    continue;
                }
                if (i == markSquares.Count)
                    markSquares.Add(NewImage("Absorb Square", Marks));
                float side = ArmourMarks.SquareSize(size, i, theme.nameplateArmourNestedScale);
                Image sq = markSquares[i];
                sq.gameObject.SetActive(true);
                // Each nesting depth gets its own sprite so every outline keeps the theme's line width, not one scaled down with the square.
                sq.sprite = SquareSprite(theme.nameplateArmourSquareLine / Mathf.Max(0.01f, side));
                sq.color = theme.nameplateArmourMarkColor;
                Place(sq.rectTransform, new Rect(-side * 0.5f, -side * 0.5f, side, side));
            }

            float width = theme.nameplateArmourChevronWidth;
            float chevronStep = theme.nameplateArmourChevronStep;
            Sprite chevron = SharedSprite(ref chevronSprite, ref chevronStroke, theme.nameplateArmourChevronLine / Mathf.Max(0.01f, width), GeneratedSprites.BuildChevron);
            for (int j = 0; j < Mathf.Max(m.chevrons, markChevrons.Count); j++)
            {
                if (j >= m.chevrons)
                {
                    markChevrons[j].gameObject.SetActive(false);
                    continue;
                }
                if (j == markChevrons.Count)
                    markChevrons.Add(NewImage("Recharge V", Marks));
                Image v = markChevrons[j];
                v.gameObject.SetActive(true);
                v.sprite = chevron;
                v.color = theme.nameplateArmourMarkColor;
                // The stack is centred on the mark (inside the innermost square when there is one), first "v" on top.
                float y = ((m.chevrons - 1) * 0.5f - j) * chevronStep;
                Place(v.rectTransform, new Rect(-width * 0.5f, y - width * 0.25f, width, width * 0.5f));
            }
            Relayout();
        }

        /// <summary>Your own plate (NameplateRules.ShowsOwnBars): no name, and the bar split into a shield row over an HP row, each
        /// with its own track.</summary>
        public void SetOwnPlate(bool own)
        {
            if (own == ownPlate)
                return;
            ownPlate = own;
            NameText.gameObject.SetActive(!own);
            if (own && shieldTrack == null && track != null)
            {
                shieldTrack = NewImage("Shield Track", Bar);
                shieldTrack.sprite = theme.barSprite;
                shieldTrack.color = theme.barTrackColor;
                shieldTrack.rectTransform.SetSiblingIndex(track.rectTransform.GetSiblingIndex() + 1);
            }
            Relayout();
        }

        public bool OwnPlate => ownPlate;

        /// <summary>On your own plate the shield row's track goes when you can hold no armour, as on the HUD.</summary>
        public void ShowShieldRow(bool shown)
        {
            if (shieldTrack != null)
                shieldTrack.enabled = shown;
        }

        public BarDividers HealthLines { get; private set; }
        public BarDividers ArmourLines { get; private set; }
        public RectTransform Marks { get; private set; }
        public IReadOnlyList<Image> MarkSquares => markSquares;
        public IReadOnlyList<Image> MarkChevrons => markChevrons;

        private static Sprite SquareSprite(float lineFraction)
        {
            float key = Mathf.Round(lineFraction * 1000f) / 1000f;
            if (!squareSprites.TryGetValue(key, out Sprite sprite) || sprite == null)
                squareSprites[key] = sprite = GeneratedSprites.BuildSquareRing(key);
            return sprite;
        }

        // One shape for every plate; rebuilt only when the theme's line width for it changes.
        private static Sprite SharedSprite(ref Sprite sprite, ref float builtFor, float fraction, System.Func<float, Sprite> build)
        {
            if (sprite == null || !Mathf.Approximately(builtFor, fraction))
            {
                sprite = build(fraction);
                builtFor = fraction;
            }
            return sprite;
        }

        /// <summary>A dead player's bar is hidden (PlayerHealth.SetOverheadBarVisible); the plate shrinks to the name.</summary>
        public void SetBarShown(bool shown)
        {
            if (shown == barShown)
                return;
            barShown = shown;
            Relayout();
        }

        private void Relayout()
        {
            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), nameWidth, barShown, ownPlate);
            Place(Backing.rectTransform, f.plate);
            Backing.enabled = f.plate.height > 0f;
            Place(Marks, f.marks);
            Marks.gameObject.SetActive(ArmourMarks.Shown(barShown, markCells, markArmour));
            Place(NameText.rectTransform, f.name);
            // Trap: while cutting long names with dots, TMP drops a whole line taller than its rect, and a line is ~1.15x the
            // font size, so the name gets its row's height again above and below as room.
            NameText.margin = new Vector4(0f, -f.name.height, 0f, -f.name.height);
            Place(Bar, f.bar);
            if (ownPlate)
            {
                // The rows as anchors inside the bar, so the fills, their divider lines and the tracks follow the bar's rect.
                Fit(healthFill, f.bar, f.healthRow);
                Fit(track, f.bar, f.healthRow);
                Fit(shieldFill, f.bar, f.shieldRow);
                Fit(shieldTrack, f.bar, f.shieldRow);
            }
            foreach (Image edge in Edges)
            {
                RectTransform rt = edge.rectTransform;
                bool across = rt.anchorMin.y == rt.anchorMax.y;
                // The side edges stop short of the top and bottom ones, so a see-through team colour does not double up in the corners.
                rt.sizeDelta = across ? new Vector2(0f, f.edge) : new Vector2(f.edge, -2f * f.edge);
                edge.enabled = f.edge > 0f && f.plate.height > 0f;
            }
        }

        private static void Fit(Image image, Rect bar, Rect row)
        {
            if (image == null || bar.height <= 0f)
                return;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = new Vector2(0f, (row.yMin - bar.yMin) / bar.height);
            rt.anchorMax = new Vector2(1f, (row.yMax - bar.yMin) / bar.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>A line of overhead text: TextMeshPro in the nameplate font at the given size, centred in the given rect,
        /// with the HUD's outline and shadow so it reads over any floor. Used for the name, the STUNNED / SLOWED label and "Wasted".</summary>
        public static TextMeshProUGUI BuildText(Transform parent, string objectName, UiTheme theme, float size, Rect rect)
        {
            RectTransform rt = NewRect(objectName, parent);
            Place(rt, rect);
            TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = theme.nameplateFont != null ? theme.nameplateFont : theme.font;
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = theme.textColor;
            text.raycastTarget = false;
            text.fontSharedMaterial = TextMaterial(theme, text.fontSharedMaterial);
            return text;
        }

        private static Material TextMaterial(UiTheme theme, Material fontMaterial)
        {
            if (sharedText == null || sharedTextSource != fontMaterial)
            {
                sharedText = new Material(fontMaterial) { name = "Nameplate Text", hideFlags = HideFlags.DontSave };
                sharedTextSource = fontMaterial;
            }
            theme.ApplyHudTextStyle(sharedText);
            return sharedText;
        }

        private Image NewEdge(string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            Image edge = NewImage(objectName, Backing.rectTransform);
            RectTransform rt = edge.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            return edge;
        }

        private static Image NewImage(string objectName, Transform parent)
        {
            Image image = NewRect(objectName, parent).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        private static void Place(RectTransform rt, Rect rect)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = rect.center;
            rt.sizeDelta = rect.size;
        }
    }
}
