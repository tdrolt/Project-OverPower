using System;
using System.Collections.Generic;
using Overpower.Data;
using Overpower.Lobby;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The create screen (board 2B, lobby Task 9): a card with the lobby name (prefilled "&lt;name&gt;'s lobby", at most the LobbyConfig length),
    /// the mode family (Conquest / Dominion), the team sizes of that family, Cancel and Create. Only modes marked available can be picked; the
    /// others are greyed with "coming soon". Create asks the LobbyDirectory for the lobby; Photon's room join then hands over to the lobby room
    /// (lobby Task 10). The choices are made by LobbyScreenRules; this class draws them.
    /// </summary>
    public sealed class CreateLobbyPanel : MonoBehaviour
    {
        private LobbyUiKit kit;
        private UiTheme theme;
        private RoomManager roomManager;
        private GameModeCatalogue catalogue;
        private LobbyConfig config;

        private GameObject root;
        private TMP_InputField nameField;
        private Transform familyRow;
        private Transform sizeRow;
        private TextMeshProUGUI familyNote;
        private TextMeshProUGUI sizeNote;
        private TextMeshProUGUI errorNote;
        private LobbyButton createButton;
        private LobbyButton cancelButton;
        private readonly List<(GameModeFamily family, LobbyButton button, Image fill, TMP_Text label, TextMeshProUGUI soon)> familyButtons =
            new List<(GameModeFamily, LobbyButton, Image, TMP_Text, TextMeshProUGUI)>();
        private readonly List<(GameModeDefinition mode, LobbyButton button, LobbyBox box)> sizeButtons =
            new List<(GameModeDefinition, LobbyButton, LobbyBox)>();

        private GameModeDefinition selected;
        private bool creating;

        /// <summary>Cancel was pressed.</summary>
        public event Action Cancelled;

        public bool IsShowing => root != null && root.activeSelf;
        public GameModeDefinition SelectedMode => selected;
        public string LobbyName => nameField != null ? nameField.text : "";
        public bool CreateEnabled => createButton != null && createButton.Button.interactable;
        public string NoteText => errorNote != null ? errorNote.text : "";

        public static CreateLobbyPanel Create(Transform canvas, LobbyUiKit kit, RoomManager manager)
        {
            var go = new GameObject("Create Lobby", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            CreateLobbyPanel panel = go.AddComponent<CreateLobbyPanel>();
            panel.Build(kit, manager);
            return panel;
        }

        /// <summary>Opens the screen with the name box prefilled for this player and the first available mode chosen.</summary>
        public void Show(string playerName)
        {
            int max = config != null ? config.LobbyNameMaxLength : LobbyConfig.DefaultLobbyNameMaxLength;
            nameField.characterLimit = max;
            nameField.text = LobbyScreenRules.LobbyNamePrefill(playerName, theme.createNamePrefillFormat, max, theme.createNameFallbackText);
            selected = LobbyScreenRules.DefaultMode(Modes);
            creating = false;
            errorNote.text = "";
            root.SetActive(true);
            Refresh();
        }

        public void Hide() => root.SetActive(false);

        /// <summary>Types a lobby name (what a driver does instead of typing).</summary>
        public void SetName(string text)
        {
            nameField.text = text;
            Refresh();
        }

        /// <summary>Presses a mode family button (Conquest / Dominion).</summary>
        public void PressFamily(GameModeFamily family)
        {
            foreach (var entry in familyButtons)
                if (entry.family == family) { entry.button.Press(); return; }
        }

        /// <summary>Presses a team size button.</summary>
        public void PressSize(GameModeDefinition mode)
        {
            foreach (var entry in sizeButtons)
                if (entry.mode == mode) { entry.button.Press(); return; }
        }

        public void PressCreate() => createButton.Press();
        public void PressCancel() => cancelButton.Press();

        /// <summary>The lobby could not be created: the screen is open again with the reason.</summary>
        public void ShowFailure(string text)
        {
            creating = false;
            errorNote.text = text;
            root.SetActive(true);
            Refresh();
        }

        private IReadOnlyList<GameModeDefinition> Modes => catalogue != null ? catalogue.Modes : null;

        // ---- choices ----

        private void ChooseFamily(GameModeFamily family)
        {
            selected = LobbyScreenRules.ChooseFamily(Modes, family, selected);
            Refresh();
        }

        private void ChooseSize(GameModeDefinition mode)
        {
            selected = LobbyScreenRules.ChooseSize(mode, selected);
            Refresh();
        }

        private void OnCreate()
        {
            if (creating || selected == null || roomManager == null || roomManager.Lobbies == null) return;
            string name = nameField.text.Trim();
            if (name.Length == 0) return;
            creating = true;
            errorNote.text = "";
            Refresh();
            Debug.Log($"[LOBBY] create pressed: \"{name}\" {selected.DisplayName}");
            if (!roomManager.Lobbies.Create(name, selected))
            {
                // Photon refused the call (not connected, or busy joining): no room was asked for, so no answer will ever come back
                ShowFailure(theme.createFailedText);
            }
        }

        /// <summary>Redraws the buttons from the current choice.</summary>
        private void Refresh()
        {
            foreach (var entry in familyButtons)
            {
                bool available = LobbyScreenRules.FamilyIsAvailable(Modes, entry.family);
                bool chosen = selected != null && selected.Family == entry.family;
                entry.button.EnabledFill = chosen ? theme.lobbyPurpleColor : theme.lobbyPanelColor;
                entry.button.EnabledText = chosen ? theme.lobbyOffWhiteColor : theme.lobbyMutedColor;
                entry.button.DisabledFill = theme.lobbyPanelColor;
                entry.button.DisabledText = theme.lobbyDimColor;
                entry.button.SetEnabled(available);
                entry.soon.gameObject.SetActive(!available);
                entry.label.rectTransform.offsetMin = new Vector2(0f, available ? 0f : theme.createComingSoonSize * theme.createComingSoonLabelShift);
            }

            familyNote.text = selected != null ? FamilyNote(selected.Family) : "";

            // the sizes of the chosen family
            foreach (var entry in sizeButtons)
            {
                entry.box.Outer.gameObject.SetActive(false); // gone from the layout now, destroyed at the end of the frame
                Destroy(entry.box.Outer.gameObject);
            }
            sizeButtons.Clear();
            if (selected != null)
            {
                foreach (GameModeDefinition mode in LobbyScreenRules.SizesOf(Modes, selected.Family))
                    sizeButtons.Add(BuildSizeButton(mode));
            }
            sizeNote.text = OtherFamilyNote();
            sizeNote.gameObject.SetActive(sizeNote.text.Length > 0);
            errorNote.gameObject.SetActive(errorNote.text.Length > 0);

            lastReady = PhotonNetwork.IsConnectedAndReady;
            createButton.SetEnabled(LobbyScreenRules.CreateMayBePressed(creating, lastReady, selected != null, nameField.text));
        }

        private bool lastReady;

        /// <summary>Create lobby is greyed while the connection is not ready and comes back by itself when it is.</summary>
        private void Update()
        {
            if (IsShowing && PhotonNetwork.IsConnectedAndReady != lastReady) Refresh();
        }

        private string FamilyNote(GameModeFamily family)
        {
            string[] notes = theme.createFamilyNotes;
            int index = (int)family;
            return notes != null && index >= 0 && index < notes.Length ? notes[index] : "";
        }

        /// <summary>"Dominion offers 2v2 and 3v3v3 here instead." for the other families, so the missing sizes are explained.</summary>
        private string OtherFamilyNote()
        {
            if (selected == null) return "";
            var lines = new List<string>();
            foreach (GameModeFamily family in LobbyScreenRules.Families(Modes))
            {
                if (family == selected.Family) continue;
                List<GameModeDefinition> sizes = LobbyScreenRules.SizesOf(Modes, family);
                if (sizes.Count == 0) continue;
                var names = new List<string>();
                foreach (GameModeDefinition mode in sizes) names.Add(mode.SizeName);
                lines.Add(string.Format(theme.createOtherFamilyFormat, sizes[0].FamilyName, string.Join(theme.createSizeJoiner, names)));
            }
            return string.Join("  ", lines);
        }

        // ---- building ----

        private void Build(LobbyUiKit uiKit, RoomManager manager)
        {
            kit = uiKit;
            theme = kit.Theme;
            roomManager = manager;
            catalogue = manager != null ? manager.ModeCatalogue : null;
            config = manager != null ? manager.LobbyCfg : null;

            root = gameObject;
            LobbyUiKit.Stretch((RectTransform)transform);
            root.AddComponent<Image>().color = theme.lobbyBackdropColor;

            LobbyBox card = kit.Box(transform, "Card", theme.lobbyDarkColor, theme.lobbyCardRadius, theme.lobbyCardBorderColor, theme.lobbyBorderWidth);
            card.Outer.anchorMin = card.Outer.anchorMax = card.Outer.pivot = new Vector2(0.5f, 0.5f);
            card.Outer.sizeDelta = new Vector2(theme.createCardWidth, 0f);
            card.Outer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            VerticalLayoutGroup column = card.Outer.gameObject.AddComponent<VerticalLayoutGroup>();
            float pad = theme.createCardPadding;
            column.padding = LobbyUiKit.Pad(pad, pad, pad, pad);
            column.spacing = theme.createCardGap;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            Transform content = card.Outer;

            kit.Text(content, "Title", theme.createTitleText, kit.Display, theme.createTitleSize, theme.lobbyOffWhiteColor);

            VerticalLayoutGroup nameBlock = LobbyUiKit.VGroup(content, "Lobby name", theme.createBlockGap);
            Label(nameBlock.transform, theme.createNameLabelText);
            nameField = kit.InputBox(nameBlock.transform, "Name box", "", theme.createFieldHeight, theme.createFieldTextSize, theme.createFieldPadding,
                TextAlignmentOptions.MidlineLeft, config != null ? config.LobbyNameMaxLength : LobbyConfig.DefaultLobbyNameMaxLength, false, out _);
            nameField.onValueChanged.AddListener(_ => { if (IsShowing) Refresh(); });

            VerticalLayoutGroup modeBlock = LobbyUiKit.VGroup(content, "Mode", theme.createBlockGap);
            Label(modeBlock.transform, theme.createModeLabelText);
            BuildFamilyRow(modeBlock.transform);
            familyNote = kit.Text(modeBlock.transform, "Mode note", "", kit.Body, theme.createNoteSize, theme.lobbyMutedColor, TextAlignmentOptions.MidlineLeft, 0f, true);

            VerticalLayoutGroup teamsBlock = LobbyUiKit.VGroup(content, "Teams", theme.createBlockGap);
            Label(teamsBlock.transform, theme.createTeamsLabelText);
            HorizontalLayoutGroup sizes = LobbyUiKit.HGroup(teamsBlock.transform, "Sizes", theme.createSizeGap, TextAnchor.MiddleLeft);
            sizeRow = sizes.transform;
            sizeNote = kit.Text(teamsBlock.transform, "Teams note", "", kit.Body, theme.createNoteSize, theme.lobbyDimColor, TextAlignmentOptions.MidlineLeft, 0f, true);
            errorNote = kit.Text(teamsBlock.transform, "Error", "", kit.Body, theme.createNoteSize, theme.lobbyErrorColor, TextAlignmentOptions.MidlineLeft, 0f, true);

            HorizontalLayoutGroup actions = LobbyUiKit.HGroup(content, "Actions", theme.createActionGap, TextAnchor.MiddleRight);
            actions.childForceExpandWidth = false;
            LobbyUiKit.Spacer(actions.transform);
            cancelButton = kit.MakeButton(actions.transform, "Cancel", theme.createCancelText, kit.Body, theme.createCancelTextSize, theme.lobbyOffWhiteColor,
                theme.lobbyDarkColor, theme.lobbyCornerRadius, theme.lobbyBorderColor, theme.lobbyBorderWidth);
            LobbyUiKit.Size(cancelButton.Root, -1f, theme.createActionHeight);
            LobbyUiKit.WidenToLabel(cancelButton, theme.createCancelPadding);
            cancelButton.Button.onClick.AddListener(() => Cancelled?.Invoke());
            createButton = kit.MakeButton(actions.transform, "Create", theme.createCreateText, kit.Display, theme.createCreateTextSize, theme.lobbyOffWhiteColor,
                theme.lobbyPurpleColor, theme.lobbyCornerRadius, theme.lobbyPurpleColor, 0f, theme.lobbyButtonSpacing);
            LobbyUiKit.Size(createButton.Root, -1f, theme.createActionHeight);
            LobbyUiKit.WidenToLabel(createButton, theme.createCreatePadding);
            createButton.Button.onClick.AddListener(OnCreate);

            root.SetActive(false);
        }

        private void Label(Transform parent, string text) =>
            kit.Text(parent, "Label", text, kit.Bold, theme.createLabelSize, theme.lobbyMutedColor, TextAlignmentOptions.MidlineLeft, theme.lobbyLabelSpacing);

        /// <summary>The Conquest / Dominion buttons side by side in one outlined, rounded strip.</summary>
        private void BuildFamilyRow(Transform parent)
        {
            LobbyBox strip = kit.Box(parent, "Mode strip", theme.lobbyPanelColor, theme.lobbyCornerRadius + theme.createModeRadiusExtra, theme.lobbyBorderColor, theme.lobbyBorderWidth);
            LobbyUiKit.Size(strip.Outer.gameObject, -1f, theme.createModeHeight);
            strip.Inner.gameObject.AddComponent<Mask>().showMaskGraphic = true; // the buttons are cut to the strip's rounded corners
            HorizontalLayoutGroup row = strip.Inner.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            familyRow = strip.Inner;

            foreach (GameModeFamily family in LobbyScreenRules.Families(Modes))
            {
                string familyName = LobbyScreenRules.SizesOf(Modes, family)[0].FamilyName;
                LobbyButton button = kit.MakeButton(familyRow, familyName, familyName.ToUpperInvariant(), kit.Display, theme.createModeTextSize,
                    theme.lobbyMutedColor, theme.lobbyPanelColor, 0f, theme.lobbyPanelColor, 0f);
                GameModeFamily captured = family;
                button.Button.onClick.AddListener(() => ChooseFamily(captured));

                // "coming soon" under the name, in the same button
                TextMeshProUGUI soon = kit.Text(button.Rect, "Coming soon", theme.createComingSoonText, kit.Body, theme.createComingSoonSize,
                    theme.lobbyDimColor, TextAlignmentOptions.Midline);
                RectTransform soonRect = (RectTransform)soon.transform;
                soonRect.anchorMin = new Vector2(0f, 0f);
                soonRect.anchorMax = new Vector2(1f, 0f);
                soonRect.pivot = new Vector2(0.5f, 0f);
                soonRect.anchoredPosition = new Vector2(0f, theme.createComingSoonLift);
                soonRect.sizeDelta = new Vector2(0f, theme.createComingSoonSize * theme.createComingSoonBoxHeight);
                soon.gameObject.SetActive(false);
                familyButtons.Add((family, button, button.Fill, button.Label, soon));
            }
        }

        private (GameModeDefinition, LobbyButton, LobbyBox) BuildSizeButton(GameModeDefinition mode)
        {
            bool selectable = LobbyScreenRules.IsSelectable(mode);
            bool chosen = mode == selected;
            Color fill = chosen ? Blend(theme.lobbyDarkColor, theme.lobbyCreateSelectedFill) : theme.lobbyPanelColor;
            Color border = chosen ? theme.lobbyPurpleColor : theme.lobbyCardBorderColor;
            LobbyButton button = kit.MakeButton(sizeRow, mode.SizeName, mode.SizeName, kit.Bold, theme.createSizeTextSize,
                selectable ? theme.lobbyOffWhiteColor : theme.lobbyDimColor, fill, theme.lobbyCornerRadius + theme.createModeRadiusExtra, border, theme.lobbyChosenBorderWidth);
            LobbyUiKit.Size(button.Root, theme.createSizeButton.x, theme.createSizeButton.y);
            button.DisabledFill = fill;
            button.SetEnabled(selectable);
            GameModeDefinition captured = mode;
            button.Button.onClick.AddListener(() => ChooseSize(captured));
            return (mode, button, new LobbyBox { Outer = button.Rect });
        }

        /// <summary>A see-through colour laid over a background (the chosen team size's fill is purple at a quarter strength over the card).</summary>
        private static Color Blend(Color background, Color overlay) =>
            new Color(Mathf.Lerp(background.r, overlay.r, overlay.a), Mathf.Lerp(background.g, overlay.g, overlay.a), Mathf.Lerp(background.b, overlay.b, overlay.a), 1f);
    }
}
