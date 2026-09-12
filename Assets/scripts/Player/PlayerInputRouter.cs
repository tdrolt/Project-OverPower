using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The one place gameplay input enters the game. Reads the OverpowerControls actions asset and
/// exposes each of the eight design-doc bindings as a property/event pair - nothing else in the
/// project should call Input or an InputAction directly (Task 0.12, replacing the old per-script
/// Input.GetKey/GetAxisRaw polling).
///
/// Deliberately NOT IPunObservable: see PlayerNetSync's class comment for why a second observable
/// on this GameObject would silently corrupt the network wire format.
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
    private InputAction moveAction, primaryAction, equipmentAction, ultimateAction;
    private InputAction mobilityAction, mapAction, shopAction, scoreboardAction;

    // Mirrors PlayerLifecycle.IsAlive rather than polling it on every property read - updated once
    // per AliveChanged event instead.
    private bool isAlive = true;

    /// <summary>Raw WASD, not camera-relative - PlayerMotor does that conversion, since it depends
    /// on the camera rig and not on input.</summary>
    public Vector2 MoveAxis => gameplayMap != null && !InputSuppressed ? moveAction.ReadValue<Vector2>() : Vector2.zero;

    // A polled Held property AND an edge event exist for each of the four ability slots on
    // purpose: a charge weapon reads Held every frame it is still holding the trigger, while a
    // dash only cares about the instant the button went down. One cannot substitute for the other.
    public bool PrimaryHeld => gameplayMap != null && !InputSuppressed && primaryAction.IsPressed();
    public bool EquipmentHeld => gameplayMap != null && !InputSuppressed && equipmentAction.IsPressed();
    public bool UltimateHeld => gameplayMap != null && !InputSuppressed && ultimateAction.IsPressed();
    public bool MobilityHeld => gameplayMap != null && !InputSuppressed && mobilityAction.IsPressed();

    public event System.Action PrimaryPressed;
    public event System.Action PrimaryReleased;
    public event System.Action EquipmentPressed;
    public event System.Action UltimatePressed;
    public event System.Action MobilityPressed;
    public event System.Action MobilityReleased;

    // Nothing subscribes to these three yet, and that is correct - the map, shop and scoreboard
    // UIs do not exist. They are wired here so those later tasks have a gated input source to
    // subscribe to on day one instead of adding their own Input.GetKeyDown.
    public event System.Action MapToggled;
    public event System.Action ShopToggled;
    public event System.Action ScoreboardPressed;
    public event System.Action ScoreboardReleased;

    /// <summary>True while dead or while the player is typing into a text field (e.g. chat). Every
    /// property and event above is gated on this being false.
    ///
    /// Deliberately NOT gated on PlayerOverheat.CanAct - full overheat silencing the weapon and
    /// abilities is an ability-level rule the ability system enforces itself, not an input-level
    /// one, and the design wants an overheated player to still be able to walk away.</summary>
    public bool InputSuppressed => !isAlive || IsTypingInChat();

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerLifecycle = GetComponent<PlayerLifecycle>();

        if (controls == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset is not assigned - this player has no input.");
            return;
        }

        // Every player prefab instance in the scene (a 3v3v3 match renders 9 - yourself plus 8
        // others) shares the same serialized asset reference unless it is cloned here. Without
        // this, Enable()/Disable() below would race across all 9 routers on the one shared map:
        // whichever OnEnable ran last would decide whether the LOCAL player's input worked at all.
        controls = Instantiate(controls);
        gameplayMap = controls.FindActionMap("Gameplay", throwIfNotFound: false);
        if (gameplayMap == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset has no 'Gameplay' action map.");
            return;
        }

        moveAction = gameplayMap.FindAction("Move"); primaryAction = gameplayMap.FindAction("Primary");
        equipmentAction = gameplayMap.FindAction("Equipment"); ultimateAction = gameplayMap.FindAction("Ultimate");
        mobilityAction = gameplayMap.FindAction("Mobility"); mapAction = gameplayMap.FindAction("ExpandMap");
        shopAction = gameplayMap.FindAction("Shop"); scoreboardAction = gameplayMap.FindAction("Scoreboard");

        if (moveAction == null || primaryAction == null || equipmentAction == null || ultimateAction == null ||
            mobilityAction == null || mapAction == null || shopAction == null || scoreboardAction == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Gameplay map is missing one or more of the " +
                            "eight expected actions - check OverpowerControls.inputactions.");
            gameplayMap = null; // Marks setup as failed; every property above already checks this.
            return;
        }

        // Button actions with no interaction assigned go Waiting -> Started -> Performed on press
        // (same frame) and Performed -> Canceled on release, so started/canceled are the clean
        // press/release edges below.
        primaryAction.started += _ => Emit(PrimaryPressed); primaryAction.canceled += _ => Emit(PrimaryReleased);
        equipmentAction.started += _ => Emit(EquipmentPressed); ultimateAction.started += _ => Emit(UltimatePressed);
        mobilityAction.started += _ => Emit(MobilityPressed); mobilityAction.canceled += _ => Emit(MobilityReleased);
        mapAction.started += _ => Emit(MapToggled); shopAction.started += _ => Emit(ShopToggled);
        scoreboardAction.started += _ => Emit(ScoreboardPressed); scoreboardAction.canceled += _ => Emit(ScoreboardReleased);
    }

    private void OnEnable()
    {
        if (gameplayMap == null)
            return; // Awake already logged why.

        // Only the local player should ever read input - a remote copy's map is disabled outright
        // rather than just ignored, so the Input System stops polling devices for it entirely.
        if (photonView.IsMine)
        {
            gameplayMap.Enable();
            if (playerLifecycle != null)
            {
                isAlive = playerLifecycle.IsAlive;
                playerLifecycle.AliveChanged += HandleAliveChanged;
            }
        }
        else
        {
            gameplayMap.Disable();
        }
    }

    private void OnDisable()
    {
        gameplayMap?.Disable(); // Entering/leaving play mode must not leak an enabled map behind.
        if (playerLifecycle != null)
            playerLifecycle.AliveChanged -= HandleAliveChanged;
    }

    private void HandleAliveChanged(bool alive) => isAlive = alive;

    private void Emit(System.Action evt)
    {
        if (!InputSuppressed)
            evt?.Invoke();
    }

    /// <summary>Suppresses input the instant a text field takes focus - e.g. the chat box under
    /// "chat manager" (a TMP_InputField); without this you can fire your weapon while typing a
    /// message. Also covers the legacy UI InputField in case a future menu uses one.</summary>
    private static bool IsTypingInChat()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null;
    }
}
