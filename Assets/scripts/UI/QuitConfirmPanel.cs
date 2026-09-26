using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Playtest extras P6 (2026-09-26): Escape asks "Close the game?" - a runtime, scene-level
    /// component (one instance, like TestRangePanel/MatchStartPanel, not per-player), built the same
    /// way: a clickable overlay canvas with a GraphicRaycaster, controls built in code so every
    /// listener sits on the same line as the button it belongs to.
    ///
    /// A raw Keyboard.current poll, not an InputAction - a tool key, matching every other Escape
    /// consumer in this project (LoadoutScreen.cs:390-394, chatmanager.cs's own Escape block, F1).
    ///
    /// OWNERSHIP: LoadoutScreen and the chat panel both close THEMSELVES on Escape, in the same
    /// frame - Update() order between components is not fixed, so this checks EscapeOwnershipRule
    /// (this frame OR the frame before) rather than just "is the shop/chat open right now". See that
    /// rule's own class comment.
    ///
    /// While open this claims general tool focus on the local player's own PlayerInputRouter (no
    /// moving or shooting while deciding - the match keeps running, it's multiplayer), the same
    /// "resolve the local player, claim/release keyed by this" recipe TestRangePanel already uses for
    /// exactly this reason.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuitConfirmPanel : MonoBehaviour
    {
        [Tooltip("Read for quitPromptText/quitYesText/quitNoText.")]
        [SerializeField] private UiTheme theme;

        private GameObject uiRoot;
        private TextMeshProUGUI promptLabel;
        private bool visible;

        // One shared TMP material for both button labels (playtest extras P6 follow-up, item 1) - same
        // reasoning as MatchStartPanel.ApplyOutline/PlayerHud.AddLabel: without it TMP clones a new
        // material the moment a label's outline is touched, one per button, for no reason.
        private Material textMaterial;

        private bool shopOpenLastFrame;
        private bool chatOpenLastFrame;

        private GameObject cachedLocalPlayer;
        private PlayerInputRouter claimedRouter;

        private void Awake()
        {
            BuildUi();
        }

        private void OnDisable()
        {
            claimedRouter?.SetToolFocus(this, false);
            claimedRouter = null;
        }

        private void OnDestroy()
        {
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        private void Update()
        {
            bool shopOpenNow = LoadoutScreen.IsOpen;
            bool chatOpenNow = PhotonChat.IsOpen;
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (!visible)
            {
                if (escapePressed && !EscapeOwnershipRule.BelongsToShopOrChat(shopOpenNow, shopOpenLastFrame, chatOpenNow, chatOpenLastFrame))
                    Open();
            }
            else if (escapePressed)
            {
                Close();
            }

            shopOpenLastFrame = shopOpenNow;
            chatOpenLastFrame = chatOpenNow;
        }

        private void Open()
        {
            visible = true;
            if (promptLabel != null && theme != null)
                promptLabel.text = theme.quitPromptText;
            uiRoot.SetActive(true);

            PlayerInputRouter router = ResolveLocalPlayer()?.GetComponentInChildren<PlayerInputRouter>(true);
            if (router != claimedRouter)
            {
                claimedRouter?.SetToolFocus(this, false);
                claimedRouter = router;
            }
            claimedRouter?.SetToolFocus(this, true);
        }

        private void Close()
        {
            visible = false;
            uiRoot.SetActive(false);
            claimedRouter?.SetToolFocus(this, false);
            claimedRouter = null;
        }

        private GameObject ResolveLocalPlayer()
        {
            if (cachedLocalPlayer != null)
                return cachedLocalPlayer;

            if (PhotonNetwork.LocalPlayer == null)
                return null;

            PhotonView view = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            cachedLocalPlayer = view != null ? view.gameObject : null;
            return cachedLocalPlayer;
        }

        // ---- Yes / No ----

        /// <summary>NEVER call this from an automated verification pass - it stops Play Mode in the
        /// Editor (or quits the built Player). Check this wiring by reading it instead.</summary>
        private void OnYesClicked() => GameQuit.Quit();

        private void OnNoClicked() => Close();

        // ---- UI construction (TestRangePanel.BuildUi/AddButton's own recipe) ----

        private void BuildUi()
        {
            var canvasGo = new GameObject("Quit Confirm Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 700; // Above the F1 test range panel (500) - a decision to close the game must never be hidden behind a debug tool.
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            uiRoot = canvasGo;

            GameObject dim = new GameObject("Dim", typeof(RectTransform));
            dim.transform.SetParent(canvasGo.transform, false);
            Image dimImage = dim.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.55f);
            RectTransform dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
            // Widened from an original 480 (playtest extras P6 follow-up, item 1): two Start-button-sized
            // buttons (theme.matchStartButtonSize.x = 260 each) plus the row's own 12 spacing need 532
            // just for the buttons - 480 used to work only because the old buttons shrank to share it.
            panelRt.sizeDelta = new Vector2(600f, 0f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var res = new TMP_DefaultControls.Resources();
            promptLabel = AddLabel(panel.transform, theme != null ? theme.quitPromptText : "Close the game?");
            promptLabel.alignment = TextAlignmentOptions.Center;
            promptLabel.fontSize = 26f;

            GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            // All four off (playtest extras P6 follow-up, item 1): a freshly-added HorizontalLayoutGroup
            // defaults childForceExpandHeight to true, which - even with width left alone - stretched
            // both buttons to the ROW's own height, and the row's own height comes from its children in
            // the first place, so it collapsed to 0 (measured live: rect height 0 - the popup's real
            // symptom was zero-height buttons, not just a bad colour). Each button keeps its own fixed
            // size (theme.matchStartButtonSize) via a LayoutElement - see AddButton below.
            rowLayout.childControlWidth = rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            // Real buttons, not the bare default sprite these used to render with - tiny dark labels,
            // no fill at all against the dark panel (the brief's own capture, escape_popup.png). Same
            // size/font/outline recipe as MatchStartPanel.BuildRowButton's own Start button, duplicated
            // rather than shared - that method is private to its own canvas/layout, and every other
            // control on this panel is already built the same "copy the recipe" way (see the class
            // comment). Yes takes the Start button's own green (an affirmative action, like starting
            // the match); No takes Bar Track Colour, the same neutral/grey fill Loadout's own buttons
            // already reuse for exactly this reason (see UiTheme.matchStartButtonColor's tooltip) - no
            // new UiTheme token needed for either.
            AddButton(buttonRow.transform, res, theme != null ? theme.quitYesText : "Yes",
                theme != null ? theme.matchStartButtonColor : new Color(0.16f, 0.45f, 0.25f, 0.95f), OnYesClicked);
            AddButton(buttonRow.transform, res, theme != null ? theme.quitNoText : "No",
                theme != null ? theme.barTrackColor : new Color(0.18f, 0.18f, 0.2f, 1f), OnNoClicked);

            uiRoot.SetActive(false);
        }

        private static TextMeshProUGUI AddLabel(Transform parent, string text)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        /// <summary>Playtest extras P6 follow-up (item 1): a real button - filled background, sized and
        /// coloured from the Start button's own UiTheme tokens - not just a label sitting on the bare
        /// default sprite. Instance method now (was static): the fill colour still comes from the
        /// caller, but the label's font/size/outline come from this panel's own `theme`/`textMaterial`.</summary>
        private void AddButton(Transform parent, TMP_DefaultControls.Resources res, string label, Color fillColor,
                                UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            Vector2 size = theme != null ? theme.matchStartButtonSize : new Vector2(260f, 52f);
            go.GetComponent<RectTransform>().sizeDelta = size;
            // The row's own HorizontalLayoutGroup has every control/expand flag off (see BuildUi), so it
            // never resizes children - but it still needs a LayoutElement to report a size for ITS OWN
            // preferred-height calculation (the sizeDelta above alone measured as 0 there - Unity reads
            // preferred size from an ILayoutElement, not the raw RectTransform, once a LayoutGroup is
            // involved at all).
            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            go.GetComponent<Image>().color = fillColor;

            TextMeshProUGUI buttonLabel = go.GetComponentInChildren<TextMeshProUGUI>();
            buttonLabel.text = label;
            if (theme != null)
            {
                if (theme.font != null)
                    buttonLabel.font = theme.font;
                buttonLabel.fontSize = theme.bodyTextSize;
                buttonLabel.color = theme.textColor;
                ApplyOutline(buttonLabel);
            }

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // Same fix as every other code-built control here - see TestRangePanel.AddDropdown's comment.
        }

        /// <summary>Same reasoning as MatchStartPanel.ApplyOutline: one shared Material instance for
        /// every button label this panel builds, instead of letting TMP auto-clone one per label.</summary>
        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
        }
    }
}
