using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The STUNNED / SLOWED label and its thin shrinking bar over ONE player's head (D18), built onto the overhead
    /// canvas above the player's plate (NameplateLayout), so everyone sees it and the fog hides it with the plate.
    /// Owned and ticked by that player's PlayerStatusEffects on every client's copy; it reads PlayerStatusEffects.TryGetStatusLabel
    /// (the real status on the owner's copy, the published Player Property on everyone else's). Text and colour are written only
    /// when the label changes and the bar only when its fill moves, so an idle player allocates nothing.
    /// </summary>
    public sealed class StatusLabelOverhead
    {
        private readonly PlayerStatusEffects source;
        private readonly UiTheme theme;
        private readonly GameObject root;
        private readonly TMP_Text text;
        private readonly Image fill;

        private StatusLabel lastLabel = (StatusLabel)(-1);
        private float lastFill = -1f;

        private StatusLabelOverhead(PlayerStatusEffects source, UiTheme theme, GameObject root, TMP_Text text, Image fill)
        {
            this.source = source;
            this.theme = theme;
            this.root = root;
            this.text = text;
            this.fill = fill;
        }

        /// <summary>Builds the label above the plate on the overhead canvas, or returns null (with a warning) when the
        /// player has no theme, canvas or bar sprite to build it from.</summary>
        public static StatusLabelOverhead TryCreate(PlayerStatusEffects source, UiTheme theme, Canvas canvas)
        {
            if (theme == null || canvas == null || theme.barSprite == null)
            {
                Debug.LogWarning($"[StatusLabelOverhead] {source.name}: no UiTheme, bar sprite or overhead canvas - this player will show no STUNNED / SLOWED label.");
                return null;
            }

            // Placed for a living player's plate: nobody wears a status while dead, and the plate's top never moves anyway.
            NameplateLayout.Frame frame = NameplateLayout.Compute(NameplateLayout.From(theme), 0f, true);

            GameObject root = new GameObject("Status Label", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;
            rootRt.sizeDelta = Vector2.zero;

            TextMeshProUGUI text = NameplateView.BuildText(root.transform, "Text", theme, theme.nameplateStatusTextSize, frame.statusLabel);
            text.fontStyle = FontStyles.Bold;

            GameObject trackGo = new GameObject("Bar", typeof(RectTransform));
            trackGo.transform.SetParent(root.transform, false);
            RectTransform trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = trackRt.anchorMax = trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.anchoredPosition = frame.statusBar.center;
            trackRt.sizeDelta = frame.statusBar.size;
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
                if (!StatusLabelRule.Look(label, theme.statusStunnedText, theme.statusStunnedColor,
                                          theme.statusSlowedText, theme.statusSlowedColor, out string words, out Color colour))
                {
                    root.SetActive(false);
                    return;
                }
                text.text = words;
                text.color = colour;
                fill.color = colour;
                root.SetActive(true);
            }

            if (!root.activeSelf)
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
