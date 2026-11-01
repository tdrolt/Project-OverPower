using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Match;
using Overpower.Net;
using TMPro;

/// <summary>
/// The one place gameplay input enters the game. Reads the OverpowerControls actions asset and
/// exposes each of the nine design-doc bindings as a property/event pair; nothing else should call
/// Input or an InputAction directly.
///
/// A bot's body is driven through the same properties and events (BeginBotDrive, SetBotInput, PressBotVent), so
/// nothing that reads the router needs to know who is playing.
///
/// Deliberately NOT IPunObservable: see PlayerNetSync's class comment.
/// </summary>
public class PlayerInputRouter : MonoBehaviour
{
    [SerializeField, Tooltip("The Input System actions asset holding the Gameplay map. Rebinding " +
             "a key is an edit to this asset (Window > Analysis > Input Actions), never an edit to " +
             "this script.")]
    private InputActionAsset controls;

    private PhotonView photonView;
    private PlayerLifecycle playerLifecycle;
    private InputActionMap gameplayMap;
    private InputAction moveAction, primaryAction, attachmentAction, ultimateAction;
    private InputAction mobilityAction, mapAction, shopAction, scoreboardAction, ventAction;
    private InputAction turnCameraLeftAction, turnCameraRightAction;

    // Mirrors PlayerLifecycle.IsAlive, updated once per AliveChanged instead of polled per read.
    private bool isAlive = true;

    // Every tool that currently wants gameplay input suppressed, keyed by the caller (SetToolFocus).
    // A HashSet, not one bool: with a shared bool, opening the loadout screen while F1 was open and
    // then closing it would unlock input under the still-open F1 panel. Keyed by owner, each tool
    // can only release its OWN claim.
    private readonly HashSet<object> toolFocusOwners = new HashSet<object>();

    // Whether the cursor sits over a raycast-target Graphic on a canvas with a GraphicRaycaster.
    // Cached once a frame in Update, not read inside the Input System callbacks, because
    // EventSystem.IsPointerOverGameObject() logs a warning outside a MonoBehaviour message. The HUD
    // canvas has no GraphicRaycaster, so hovering its bars never sets this; only a real clickable
    // panel (F1, the loadout screen) does.
    private bool pointerOverUi;

    // Set for good by BeginBotDrive; from then on the bot fields below are this body's only input.
    private bool botDriven;
    private Vector3 botMove;
    private bool botPrimary, botAttachment, botUltimate, botMobility;

    /// <summary>Raw WASD, not camera-relative: PlayerMotor converts, since that depends on the camera rig. A bot's is its world
    /// move as (x, z), for readers that only ask whether the body walks (MechAnimator).</summary>
    public Vector2 MoveAxis => botDriven
        ? (InputSuppressed ? Vector2.zero : new Vector2(botMove.x, botMove.z))
        : gameplayMap != null && !InputSuppressed ? moveAction.ReadValue<Vector2>() : Vector2.zero;

    /// <summary>True once BeginBotDrive has handed this body to the host's bot.</summary>
    public bool BotDriven => botDriven;

    /// <summary>The bot's move in world space, length at most 1; zero while suppressed, like MoveAxis. PlayerMotor takes it as it
    /// is: the host's camera turns with its human, not with the bot.</summary>
    public Vector3 BotWorldMove => botDriven && !InputSuppressed ? botMove : Vector3.zero;

    // Each of the four ability slots has both a polled Held property and an edge event on purpose: a
    // charge weapon reads Held every frame, a dash only cares about the instant of press.
    //
    // Primary and Attachment gate on PointerFireBlocked (the pointer over UI as well): both are mouse buttons, so they can land on
    // a UI click; the other two Held properties are keys and cannot.
    public bool PrimaryHeld => botDriven ? botPrimary && !PointerFireBlocked : gameplayMap != null && !PointerFireBlocked && primaryAction.IsPressed();
    public bool AttachmentHeld => botDriven ? botAttachment && !PointerFireBlocked : gameplayMap != null && !PointerFireBlocked && attachmentAction.IsPressed();
    public bool UltimateHeld => botDriven ? botUltimate && !InputSuppressed : gameplayMap != null && !InputSuppressed && ultimateAction.IsPressed();
    public bool MobilityHeld => botDriven ? botMobility && !InputSuppressed : gameplayMap != null && !InputSuppressed && mobilityAction.IsPressed();

    /// <summary>Q/E held: -1 turns the camera left, +1 right (CameraTracking asks CameraKeysBlocked first).</summary>
    public float CameraTurnAxis => gameplayMap != null ? CameraViewRules.TurnAxis(turnCameraLeftAction.IsPressed(), turnCameraRightAction.IsPressed()) : 0f;

    /// <summary>Typing in chat or a panel holding input stops Q/E turning the camera; being knocked out does not (you may look
    /// around while you wait).</summary>
    public bool CameraKeysBlocked => gameplayMap == null || IsTypingInChat() || toolFocusOwners.Count > 0;

    public event System.Action PrimaryPressed;
    public event System.Action PrimaryReleased;
    public event System.Action AttachmentPressed;
    public event System.Action UltimatePressed;
    public event System.Action MobilityPressed;
    public event System.Action MobilityReleased;

    /// <summary>The overheat Vent button (R), gated like MobilityPressed (Emit/InputSuppressed).
    /// PlayerOverheat is the only subscriber (owner only).</summary>
    public event System.Action VentPressed;

    // Scoreboard, Shop and Map each have their own, narrower emit gate - see EmitScoreboardPressed,
    // ShopSuppressed and MapSuppressed.
    public event System.Action MapToggled;
    public event System.Action ShopToggled;
    public event System.Action ScoreboardPressed;
    public event System.Action ScoreboardReleased;

    /// <summary>True while dead, typing into a text field (chat), or while any tool has claimed focus
    /// (SetToolFocus). Everything above except ShopToggled and MapToggled is gated on this being false;
    /// those two have narrower gates (ShopSuppressed, MapSuppressed). A bot is only stopped by being dead: the
    /// chat box and the panels on the host's screen are its human's.
    ///
    /// Deliberately NOT gated on PlayerOverheat.CanAct: overheat silencing is an ability-level rule the
    /// ability system enforces, and an overheated player must still be able to walk away.</summary>
    public bool InputSuppressed => !isAlive || (!botDriven && (IsTypingInChat() || toolFocusOwners.Count > 0));

    /// <summary>Whether a mouse-button click is kept from firing: input suppressed, or the pointer over a clickable panel. The
    /// crosshair shows exactly where this is false (CrosshairRules.ShowCrosshair), so the arrow is back wherever a click is for the UI.</summary>
    public static bool FireBlocked(bool inputSuppressed, bool pointerOverUi) => inputSuppressed || pointerOverUi;

    public bool PointerFireBlocked => FireBlocked(InputSuppressed, pointerOverUi);

    /// <summary>Only typing in chat blocks Shop (a player waiting to respawn can shop, D4). A tool holding
    /// general focus must not: P is how the loadout screen (itself a focus owner) closes, and F1 being open
    /// must not swallow it, or closing the loadout while F1 holds focus would fail.</summary>
    private bool ShopSuppressed => IsTypingInChat();

    /// <summary>MapToggled's narrowest gate: M must work while dead and while the loadout screen holds
    /// focus, because opening the large map closes the P screen. Only typing blocks it; otherwise a large
    /// map opened before death could not be closed until respawn.</summary>
    private bool MapSuppressed => IsTypingInChat();

    /// <summary>
    /// Input lock for anything that is a tool rather than gameplay (F1 panel, loadout screen), so a click
    /// on it cannot also fire the weapon underneath. owner is the claiming MonoBehaviour (see
    /// toolFocusOwners). Safe with hasFocus: false for an owner that never claimed, and safe to call
    /// twice with the same value.
    /// </summary>
    public void SetToolFocus(object owner, bool hasFocus)
    {
        if (owner == null)
            return;

        if (hasFocus)
            toolFocusOwners.Add(owner);
        else
            toolFocusOwners.Remove(owner);
    }

    /// <summary>Hands this body to the host's bot for good: the device map goes off (the host's keyboard and mouse are its human's)
    /// and input comes only from SetBotInput and PressBotVent.</summary>
    public void BeginBotDrive()
    {
        botDriven = true;
        pointerOverUi = false;
        gameplayMap?.Disable();
    }

    /// <summary>The bot's input, given every frame: a world-space move (flattened, clamped to length 1) and which of the four held
    /// buttons are down. A button going down or coming up raises the same Pressed/Released events a key would, through the same gates.
    /// Ignored until BeginBotDrive.</summary>
    public void SetBotInput(Vector3 worldMove, bool primaryHeld, bool attachmentHeld, bool ultimateHeld, bool mobilityHeld)
    {
        if (!botDriven)
            return;

        worldMove.y = 0f;
        botMove = Vector3.ClampMagnitude(worldMove, 1f);

        bool wasPrimary = botPrimary, wasAttachment = botAttachment, wasUltimate = botUltimate, wasMobility = botMobility;
        // Stored before the events, as a key reads IsPressed inside its own started callback: a listener asking Held sees it down.
        botPrimary = primaryHeld;
        botAttachment = attachmentHeld;
        botUltimate = ultimateHeld;
        botMobility = mobilityHeld;

        if (primaryHeld && !wasPrimary) EmitPointerGated(PrimaryPressed);
        else if (!primaryHeld && wasPrimary) Emit(PrimaryReleased);
        if (attachmentHeld && !wasAttachment) EmitPointerGated(AttachmentPressed);
        if (ultimateHeld && !wasUltimate) Emit(UltimatePressed);
        if (mobilityHeld && !wasMobility) Emit(MobilityPressed);
        else if (!mobilityHeld && wasMobility) Emit(MobilityReleased);
    }

    /// <summary>The bot's press of the Vent button (VentPressed), gated like the key. Ignored until BeginBotDrive.</summary>
    public void PressBotVent()
    {
        if (botDriven)
            Emit(VentPressed);
    }

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerLifecycle = GetComponent<PlayerLifecycle>();

        if (controls == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset is not assigned - this player has no input.");
            return;
        }

        // Every player prefab instance (9 in a 3v3v3 match) shares the same serialized asset unless it
        // is cloned here; otherwise Enable()/Disable() would race across all routers on one shared map
        // and whichever OnEnable ran last would decide whether the LOCAL player's input worked.
        controls = Instantiate(controls);
        gameplayMap = controls.FindActionMap("Gameplay", throwIfNotFound: false);
        if (gameplayMap == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset has no 'Gameplay' action map.");
            return;
        }

        moveAction = gameplayMap.FindAction("Move"); primaryAction = gameplayMap.FindAction("Primary");
        attachmentAction = gameplayMap.FindAction("Attachment"); ultimateAction = gameplayMap.FindAction("Ultimate");
        mobilityAction = gameplayMap.FindAction("Mobility"); mapAction = gameplayMap.FindAction("ExpandMap");
        shopAction = gameplayMap.FindAction("Shop"); scoreboardAction = gameplayMap.FindAction("Scoreboard");
        ventAction = gameplayMap.FindAction("Vent");
        turnCameraLeftAction = gameplayMap.FindAction("TurnCameraLeft"); turnCameraRightAction = gameplayMap.FindAction("TurnCameraRight");

        if (moveAction == null || primaryAction == null || attachmentAction == null || ultimateAction == null ||
            mobilityAction == null || mapAction == null || shopAction == null || scoreboardAction == null ||
            ventAction == null || turnCameraLeftAction == null || turnCameraRightAction == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Gameplay map is missing one or more of the " +
                            "eleven expected actions - check OverpowerControls.inputactions.");
            gameplayMap = null; // marks setup as failed; every property above checks it
            return;
        }

        // Button actions with no interaction go Started -> Performed on press (same frame) and
        // Canceled on release, so started/canceled are the clean press/release edges.
        // Primary/Attachment PRESSED go through EmitPointerGated (see that method). Release stays on
        // plain Emit: a click that opened a tool fired no press, so there is nothing for a release to
        // wrongly end, and gating it would risk a stuck-held weapon if the pointer were over UI on
        // the frame the button came up.
        primaryAction.started += _ => EmitPointerGated(PrimaryPressed); primaryAction.canceled += _ => Emit(PrimaryReleased);
        attachmentAction.started += _ => EmitPointerGated(AttachmentPressed); ultimateAction.started += _ => Emit(UltimatePressed);
        mobilityAction.started += _ => Emit(MobilityPressed); mobilityAction.canceled += _ => Emit(MobilityReleased);
        mapAction.started += _ => EmitMap(); shopAction.started += _ => EmitShop();
        scoreboardAction.started += _ => EmitScoreboardPressed(); scoreboardAction.canceled += _ => ScoreboardReleased?.Invoke();
        ventAction.started += _ => Emit(VentPressed);
    }

    private void OnEnable()
    {
        // Only the person at this keyboard reads devices: every other copy's map is disabled outright so the Input System stops
        // polling devices for it. That includes a bot's body on the host, which is IsMine there: enabled, the human's own keys
        // would drive every bot. A null map means Awake already logged why.
        if (gameplayMap != null)
        {
            if (BodyRole.IsLocalHuman(photonView) && !botDriven)
                gameplayMap.Enable();
            else
                gameplayMap.Disable();
        }

        // Every body this client controls follows its alive gate, a bot's too.
        if (photonView != null && photonView.IsMine && playerLifecycle != null)
        {
            isAlive = playerLifecycle.IsAlive;
            playerLifecycle.AliveChanged += HandleAliveChanged;
        }
    }

    private void OnDisable()
    {
        gameplayMap?.Disable(); // entering/leaving play mode must not leak an enabled map
        if (playerLifecycle != null)
            playerLifecycle.AliveChanged -= HandleAliveChanged;
    }

    /// <summary>A UI click and a mouse-button gameplay action are the SAME physical click: a "Loadout (P)"
    /// HUD button press reached the router as a Primary press too and fired the weapon before the button's
    /// onClick claimed tool focus. Hence pointerOverUi, read here once a frame rather than inside the Input
    /// System callback (IsPointerOverGameObject() warns outside a MonoBehaviour message). Only meaningful
    /// for the local player.</summary>
    private void Update()
    {
        // Only the local human's clicks can be a UI click on THIS machine; skips the redundant
        // EventSystem queries for the other copies in a full room, the host's bots included.
        if (botDriven || (photonView != null && !BodyRole.IsLocalHuman(photonView)))
            return;

        pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void HandleAliveChanged(bool alive) => isAlive = alive;

    private void Emit(System.Action evt)
    {
        if (!InputSuppressed)
            evt?.Invoke();
    }

    /// <summary>Primary/Attachment's emit path: both are mouse buttons, so both can land on a UI click
    /// before its onClick has claimed tool focus (see Update). Only a clickable panel (F1, the loadout
    /// screen) blocks firing; the HUD canvas has no GraphicRaycaster.</summary>
    private void EmitPointerGated(System.Action evt)
    {
        if (!PointerFireBlocked)
            evt?.Invoke();
    }

    /// <summary>ShopToggled's own emit path (see ShopSuppressed).</summary>
    private void EmitShop()
    {
        if (!ShopSuppressed)
            ShopToggled?.Invoke();
    }

    /// <summary>ScoreboardPressed's own emit path (D12): only typing in chat blocks it (ScoreboardRules.MayOpen).
    /// The release is never gated, so a board opened just before typing or dying can always close.</summary>
    private void EmitScoreboardPressed()
    {
        if (ScoreboardRules.MayOpen(IsTypingInChat()))
            ScoreboardPressed?.Invoke();
    }

    private void EmitMap()
    {
        if (!MapSuppressed)
            MapToggled?.Invoke();
    }

    /// <summary>True the instant a text field takes focus (the chat box is a TMP_InputField), so you
    /// cannot fire while typing. Covers the legacy InputField too. Public because BugMarkerKey shares
    /// this check to refuse Ctrl+B while typing.</summary>
    public static bool IsTypingInChat()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null;
    }
}
