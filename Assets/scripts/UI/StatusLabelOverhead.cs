using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The STUNNED / SLOWED label and its thin shrinking bar over ONE player's head (Tudor D18), built at
    /// runtime onto the overhead canvas the health bar and name already sit on, so it is seen by everyone.
    /// Owned and ticked by that player's PlayerStatusEffects, on every client's copy; what it shows comes
    /// from PlayerStatusEffects.TryGetStatusLabel (the real status on the owner's copy, the published
    /// Player Property on everyone else's).
    ///
    /// Text and colour are written only when the label changes and the bar only when its fill moves, so an
    /// idle player costs one call and a comparison per frame and nothing is allocated.
    /// </summary>
    public sealed class StatusLabelOverhead
    {
        private readonly PlayerStatusEffects source;
        private readonly UiTheme theme;
        private readonly GameObject root;
        private readonly Text text;
        private readonly Image fill;

        private StatusLabel lastLabel = (StatusLabel)(-1);
        private float lastFill = -1f;

        private StatusLabelOverhead(PlayerStatusEffects source, UiTheme theme, GameObject root, Text text, Image fill)
        {
            this.source = source;
            this.theme = theme;
            this.root = root;
            this.text = text;
            this.fill = fill;
        }

        /// <summary>Builds the label under the overhead canvas, or returns null (with a warning) when the
        /// player has no theme, canvas or bar sprite to build it from.</summary>
        public static StatusLabelOverhead TryCreate(PlayerStatusEffects source, UiTheme theme, Canvas canvas)
        {
            if (theme == null || canvas == null || theme.barSprite == null)
            {
                Debug.LogWarning($"[StatusLabelOverhead] {source.name}: no UiTheme, bar sprite or overhead canvas - this player will show no STUNNED / SLOWED label.");
                return null;
            }

            // The name text is the size and font reference: the label reads as a sibling of the name.
            Text nameText = canvas.GetComponentInChildren<Text>(true);

            GameObject root = new GameObject("Status Label", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;
            rootRt.sizeDelta = Vector2.zero;

            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = new Vector2(0f, theme.statusOverheadLabelY);
            textRt.sizeDelta = new Vector2(200f, 40f);
            if (nameText != null)
                textRt.localScale = nameText.rectTransform.localScale;
            Text text = textGo.AddComponent<Text>();
            if (nameText != null)
                text.font = nameText.font;
            text.fontSize = Mathf.RoundToInt(theme.statusOverheadTextSize);
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            Outline outline = textGo.AddComponent<Outline>();
            outline.effectColor = theme.textOutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);

            GameObject trackGo = new GameObject("Bar", typeof(RectTransform));
            trackGo.transform.SetParent(root.transform, false);
            RectTransform trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = trackRt.anchorMax = trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.anchoredPosition = new Vector2(0f, theme.statusOverheadBarY);
            trackRt.sizeDelta = theme.statusOverheadBarSize;
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
            // Sprite before Type: a Filled Image with no sprite ignores fillAmount and draws full (see PlayerHud.BuildBar).
            fill.sprite = theme.barSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.raycastTarget = false;

            root.SetActive(false);
            return new StatusLabelOverhead(source, theme, root, text, fill);
        }

        /// <summary>Once per frame: follow whatever the player's status says.</summary>
        public void Tick()
        {
            source.TryGetStatusLabel(out StatusLabel label, out float remaining, out float total);

            if (label != lastLabel)
            {
                lastLabel = label;
                lastFill = -1f;
                if (label == StatusLabel.None)
                {
                    root.SetActive(false);
                    return;
                }

                bool stunned = label == StatusLabel.Stunned;
                Color colour = stunned ? theme.statusStunnedColor : theme.statusSlowedColor;
                text.text = stunned ? theme.statusStunnedText : theme.statusSlowedText;
                text.color = colour;
                fill.color = colour;
                root.SetActive(true);
            }

            if (label == StatusLabel.None)
                return;

            float amount = StatusLabelRule.Fill(remaining, total);
            if (Mathf.Abs(amount - lastFill) > 0.002f)
            {
                lastFill = amount;
                fill.fillAmount = amount;
            }
        }
    }
}
