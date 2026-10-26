using Overpower.Vision;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The plate over one player's head: a dark backing with a team-coloured edge, the name in TextMeshPro (crisp at any distance)
    /// and under it the health-and-armour bar PlayerHealth drives. Built at runtime on the canvas EnemyVisibility switches off in
    /// the fog, found by EnemyVisibility's own lookup, so an enemy's plate vanishes with their body; it adds no canvas of its own,
    /// which would draw outside that switch. PlayerHealth and PlayerNameTag both call Ensure: whichever runs first builds it.
    /// Placement is NameplateLayout's; numbers and colours are UiTheme's.
    /// </summary>
    public sealed class NameplateView : MonoBehaviour
    {
        private UiTheme theme;
        private float nameWidth;
        private bool barShown = true;

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
                view.Build(health.Theme, canvas.transform, bar);
            }
            return view;
        }

        private void Build(UiTheme uiTheme, Transform canvas, RectTransform bar)
        {
            theme = uiTheme;
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

            // Drawn in sibling order: backing and edges, then the bar, then the name.
            Bar = bar;
            Bar.SetParent(Root, false);
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
            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), nameWidth, barShown);
            Place(Backing.rectTransform, f.plate);
            Place(NameText.rectTransform, f.name);
            // Trap: while cutting long names with dots, TMP drops a whole line taller than its rect, and a line is ~1.15x the
            // font size, so the name gets its row's height again above and below as room.
            NameText.margin = new Vector4(0f, -f.name.height, 0f, -f.name.height);
            Place(Bar, f.bar);
            foreach (Image edge in Edges)
            {
                RectTransform rt = edge.rectTransform;
                bool across = rt.anchorMin.y == rt.anchorMax.y;
                // The side edges stop short of the top and bottom ones, so a see-through team colour does not double up in the corners.
                rt.sizeDelta = across ? new Vector2(0f, f.edge) : new Vector2(f.edge, -2f * f.edge);
                edge.enabled = f.edge > 0f;
            }
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
