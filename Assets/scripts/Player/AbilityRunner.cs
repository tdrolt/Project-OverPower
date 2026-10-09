using UnityEngine.Serialization;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Overpower.TestRange;

/// <summary>
/// One player's three ability slots - Attachment (right mouse), Ultimate (Space), Mobility (Left
/// Shift); Primary stays WeaponFiring's. Owns what every ability shares: reading the keys, the "can
/// you act" gate, the press buffer, the one network message, and death and respawn. What makes a
/// dash a dash lives in its own AbilityModule prefab.
///
/// WHY ONE RPC HERE, AND NONE ON THE MODULES. PUN only delivers an RPC to components on the
/// PhotonView's own GameObject (the player root), so a child module can never receive one, and a
/// PhotonView per module would allocate network ids at runtime. Every cast travels through
/// RPC_CastAbility with the slot and ability id beside it, so adding an ability never touches the RPC
/// list in PhotonServerSettings, the most fragile file in the project.
///
/// WHO DECIDES. Only the owner's copy reads keys and checks the gate. Every copy, the owner's
/// included, runs the cast when the RPC arrives and never gates it again: overheat, stun and
/// cooldowns are not replicated, so a receiver would check the wrong player's numbers and silently
/// drop real casts.
///
/// TRUST MODEL: client-authoritative; the caster's machine decides and everyone else believes it.
/// No anti-cheat (a relay with no server that could check anything).
///
/// Deliberately NOT IPunObservable - PlayerNetSync is the only observable. And deliberately not
/// switched off by PlayerLifecycle while dead: it must stay awake to hear AliveChanged and to keep
/// receiving casts sent just before a death.
/// </summary>
public class AbilityRunner : MonoBehaviourPun, ITestRangeResettable
{
    [SerializeField, Tooltip("Every ability in the game. Ability ids arriving over the network are " +
             "looked up here, never by position in its list.")]
    private AbilityCatalogue catalogue;

    [SerializeField, Tooltip("Match tuning asset. The press buffer (how early a key press still " +
             "counts) comes from here.")]
    private GameplayConfig gameplayConfig;

    [SerializeField, Tooltip("The empty 'Abilities' child of the player root that equipped ability " +
             "modules are created under. Must NOT be inside PBRCharacter: that object is hidden on " +
             "death and has its own PhotonView.")]
    private Transform moduleParent;

    [Header("Starting abilities (leave empty for none)")]
    [SerializeField, FormerlySerializedAs("startingEquipment"), Tooltip("The right-mouse ability a player starts the match with. The shop and " +
             "the test range replace it at runtime through PlayerLoadout.")]
    private AbilityDefinition startingAttachment;

    [SerializeField, Tooltip("The Space ability a player starts the match with.")]
    private AbilityDefinition startingUltimate;

    [SerializeField, Tooltip("The Left Shift ability a player starts the match with.")]
    private AbilityDefinition startingMobility;

    // Indexed by SlotIndex: 0 Attachment, 1 Ultimate, 2 Mobility. Primary has no entry - it is a weapon.
    private const int SlotCount = 3;
    private readonly AbilityModule[] modules = new AbilityModule[SlotCount];
    private readonly CastGate.PressBuffer[] pressBuffers =
    {
        new CastGate.PressBuffer(), new CastGate.PressBuffer(), new CastGate.PressBuffer()
    };

    // Per slot, the time until which HoldSlot keeps that key held. 0 = no tool hold.
    private readonly float[] toolHoldUntil = new float[SlotCount];

    private AbilityOwner owner;
    private PlayerInputRouter input;

    // Mirrors of last frame's stun/silence, so the modules are interrupted on the frame each one
    // switches ON, not on every frame it stays on.
    private bool wasStunned;
    private bool wasSilenced;

    // A bad cast RPC (wrong slot for its id, unknown id) is reported once per session, not per packet;
    // a stale build on the other end would otherwise fill the log every cast.
    private static bool loggedRejectedCast;

    /// <summary>Raised on this client whenever a slot's module is created, replaced or removed, for the
    /// HUD to redraw that slot's icon.</summary>
    public event System.Action<AbilitySlot> SlotChanged;

    /// <summary>Raised on the caster's own client right after a successful cast's RPC is sent: TryCast
    /// only, never SendPhase's later phases (a channel completing or being cancelled is not a fresh
    /// cast). PlayerTelemetry logs its `cast` line from it.</summary>
    public event System.Action<AbilitySlot, int> Cast;

    /// <summary>Raised instead of Cast for a follow-up to an earlier cast (the AoE Zone's throw): the
    /// same slot and ability id, but not a fresh use of the ability.</summary>
    public event System.Action<AbilitySlot, int> FollowUpCast;

    private void Awake()
    {
        owner = new AbilityOwner(gameObject);
        input = GetComponent<PlayerInputRouter>();

        // Loud: a silent null would leave abilities that never equip or cannot be resolved on anyone
        // else's machine.
        if (catalogue == null)
            Debug.LogError($"[AbilityRunner] {name}: Ability Catalogue is not assigned - abilities cannot be equipped or received.");
        if (gameplayConfig == null)
            Debug.LogError($"[AbilityRunner] {name}: GameplayConfig is not assigned - the press buffer falls back to 0s (no buffering).");
        if (moduleParent == null)
            Debug.LogError($"[AbilityRunner] {name}: Module Parent is not assigned - modules will be created on the player root instead.");

        // Awake, not Start: PlayerLifecycle's Start can already raise AliveChanged (a late joiner
        // learning this player is dead), and Start order between components is not guaranteed.
        if (owner.Lifecycle != null)
            owner.Lifecycle.AliveChanged += HandleAliveChanged;
    }

    private void OnDestroy()
    {
        if (owner != null && owner.Lifecycle != null)
            owner.Lifecycle.AliveChanged -= HandleAliveChanged;
    }

    private void OnEnable()
    {
        // Only your own player's cooldowns are yours to reset from the test range.
        if (photonView.IsMine)
            TestRangeResetRegistry.Register(this);

        if (input == null)
            return;

        input.AttachmentPressed += HandleAttachmentPressed;
        input.UltimatePressed += HandleUltimatePressed;
        input.MobilityPressed += HandleMobilityPressed;
    }

    private void OnDisable()
    {
        TestRangeResetRegistry.Unregister(this);

        if (input == null)
            return;

        input.AttachmentPressed -= HandleAttachmentPressed;
        input.UltimatePressed -= HandleUltimatePressed;
        input.MobilityPressed -= HandleMobilityPressed;
    }

    private void HandleAttachmentPressed() => PressSlot(AbilitySlot.Attachment);
    private void HandleUltimatePressed() => PressSlot(AbilitySlot.Ultimate);
    private void HandleMobilityPressed() => PressSlot(AbilitySlot.Mobility);

    // ---- the owner's frame ----------------------------------------------------------------

    private void Update()
    {
        if (!photonView.IsMine)
            return; // nobody else's machine decides anything about this player's casts

        float deltaTime = Time.deltaTime;
        float now = Time.time;
        // No literal duplicating GameplayConfig's default: a missing config (logged in Awake) degrades
        // to no buffering rather than silently guessing the tuning value.
        float window = gameplayConfig != null ? gameplayConfig.AbilityPressBufferSeconds : 0f;

        InterruptOnStunOrSilence();

        CastBlock actorBlock = ActorBlock();
        bool canAct = actorBlock == CastBlock.None;

        for (int i = 0; i < SlotCount; i++)
        {
            AbilityModule module = modules[i];
            if (module == null)
                continue;

            // Cooldown first, so a charge returning this very frame is spendable by an already
            // buffered press.
            module.TickCooldown(deltaTime);

            if (pressBuffers[i].IsPending(now, window) &&
                CastGate.ForAbility(actorBlock, module.HasChargeGate, module.HasCharge, module.IsReady) == CastBlock.None)
            {
                bool cast = TryCast(module);

                // Consumed on a cast, or on a refusal from a module that does not ask to retry: a refusal
                // (no valid target) is normally an answer, and retrying every frame would ask the same
                // question again. A module with RetriesRefusalWithinBuffer (Dash) keeps the press alive,
                // so the SAME press fires once the refusal's reason clears within the window.
                if (cast || !module.RetriesRefusalWithinBuffer)
                    pressBuffers[i].TryConsume(now, window);
            }

            // held is true only while the player can act, so a channel or sprint ends the instant a
            // stun, silence or death lands (AbilityModule.OwnerTick).
            bool held = canAct && IsHeld(SlotFromIndex(i));
            module.OwnerTick(deltaTime, held, canAct);
        }
    }

    /// <summary>
    /// Owner only. Presses one slot's key: buffered, then gated and cast in Update. Public so the test
    /// range and editor tooling drive the exact path a key press does; there is no second casting route.
    /// </summary>
    public void PressSlot(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        if (!photonView.IsMine || index < 0)
            return;

        pressBuffers[index].Press(Time.time);
    }

    /// <summary>
    /// Owner only. Holds one slot's key down for the given seconds - PressSlot's partner for testing
    /// channels and sprints from editor tooling, where the real keyboard does not reach the game.
    /// Time-limited, not an on/off switch, so a tool that forgets to let go never leaves a key stuck;
    /// 0 lets go at once. Still subject to the gate: a stun or death ends the hold like a real key.
    /// </summary>
    public void HoldSlot(AbilitySlot slot, float seconds)
    {
        int index = SlotIndex(slot);
        if (!photonView.IsMine || index < 0)
            return;

        toolHoldUntil[index] = Time.time + Mathf.Max(0f, seconds);
    }

    /// <summary>True when the cast actually happened.</summary>
    private bool TryCast(AbilityModule module)
    {
        CastContext ctx = BuildContext();
        if (!module.TryBuildCast(ctx, out CastPayload payload))
            return false;

        // Spent BEFORE the RPC goes out. With RpcTarget.All the caster's own copy runs synchronously
        // inside photonView.RPC, so ExecuteCast must already see the charge gone, or a module reading
        // ChargesAvailable there would count a charge it just used.
        if (module.SpendsChargeWhenCast && !module.TrySpendChargeForCast())
            return false;

        SendCast(module, 0, payload);
        if (module.IsFollowUpCast(payload))
            FollowUpCast?.Invoke(module.Definition.Slot, module.Definition.Id);
        else
            Cast?.Invoke(module.Definition.Slot, module.Definition.Id);
        return true;
    }

    /// <summary>What the caster's machine knows at the press, gathered in one place so no module reads
    /// the mouse or the camera itself.</summary>
    private CastContext BuildContext()
    {
        Vector3 origin = transform.position;
        // SafeMuzzlePosition, not MuzzlePosition: a cast fired flush against a wall (stun gun, zip
        // gun) must start on the near side of it, like a weapon shot (WeaponFiring.SafeMuzzlePosition).
        Vector3 muzzle = owner.Weapon != null ? owner.Weapon.SafeMuzzlePosition : origin;
        Vector3 aim = owner.Aim != null ? owner.Aim.AimDirection : transform.forward;
        Vector3 point = owner.Aim != null ? owner.Aim.GroundPointUnderCursor : origin;
        Vector3 move = owner.Motor != null ? Vector3.ClampMagnitude(owner.Motor.MovementInput(), 1f) : Vector3.zero;
        return new CastContext(origin, muzzle, aim, point, move);
    }

    /// <summary>Owner only. The one way a cast, or a later phase of one, leaves this machine.</summary>
    internal void SendCast(AbilityModule module, byte phase, in CastPayload payload)
    {
        if (!photonView.IsMine || module == null || module.Definition == null)
            return;

        // All, not AllViaServer (unlike WeaponFiring): an ability usually moves the caster's own body,
        // and waiting for the server round trip before your own dash reads as input lag. Other clients
        // still receive one sender's RPCs in send order.
        photonView.RPC(nameof(RPC_CastAbility), RpcTarget.All, (byte)module.Definition.Slot,
                       module.Definition.Id, phase, payload.Origin, payload.Direction, payload.Point,
                       payload.Seed, payload.IntArg, payload.FloatArg);
    }

    // ---- every client -------------------------------------------------------------------------

    /// <summary>
    /// Runs one cast on this machine, for whoever sent it. In the RpcList in PhotonServerSettings:
    /// NEVER rename it and never change its parameters - PUN sends an index into that list, and every
    /// machine in a room must agree on it.
    ///
    /// No gate here, on purpose (class comment). Everything about the caster comes from the parameters
    /// or info.Sender; inside this body PhotonNetwork.LocalPlayer is the receiver.
    /// </summary>
    [PunRPC]
    private void RPC_CastAbility(byte slot, int abilityId, byte phase, Vector3 origin, Vector3 direction,
                                 Vector3 point, int seed, int intArg, float floatArg, PhotonMessageInfo info)
    {
        AbilitySlot abilitySlot = (AbilitySlot)slot;
        int index = SlotIndex(abilitySlot);
        if (index < 0)
        {
            RejectCastOnce($"slot {slot} is not an ability slot");
            return;
        }

        AbilityModule module = modules[index];
        if (module == null || module.Definition.Id != abilityId)
        {
            // A later phase for a module this client no longer holds is over: the Unequipped interrupt
            // already stopped it. Re-equipping the old module to show it cancelled would throw away
            // the newer loadout.
            if (phase != 0)
                return;

            // The cast beat the loadout property: Custom Properties and RPCs are separate messages.
            // The id travels beside the slot so this is fixed on the spot instead of running
            // whichever module is in the slot.
            AbilityDefinition definition = catalogue != null ? catalogue.Resolve(abilityId) : null;
            if (definition == null || definition.Slot != abilitySlot)
            {
                RejectCastOnce(definition == null
                    ? $"ability id {abilityId} is not in the catalogue"
                    : $"ability id {abilityId} belongs to {definition.Slot}, not {abilitySlot}");
                return;
            }

            Equip(abilitySlot, abilityId);
            module = modules[index];
            if (module == null)
                return;
        }

        var payload = new CastPayload
        {
            Origin = origin, Direction = direction, Point = point,
            Seed = seed, IntArg = intArg, FloatArg = floatArg
        };

        int casterActor = info.Sender != null ? info.Sender.ActorNumber : -1;
        Teams.TryGetTeam(info.Sender, out int casterTeam);
        bool isCasterClient = info.Sender != null && info.Sender.IsLocal;
        float secondsLate = Mathf.Max(0f, (float)(PhotonNetwork.Time - info.SentServerTime));

        module.ExecuteCast(new CastEvent(payload, phase, casterActor, casterTeam, isCasterClient, secondsLate));
    }

    private static void RejectCastOnce(string reason)
    {
        if (loggedRejectedCast)
            return;

        loggedRejectedCast = true;
        Debug.LogWarning($"[AbilityRunner] rejected a cast: {reason}. Usually two builds from different " +
                         "commits in one room. Further rejections this session are not logged.");
    }

    /// <summary>
    /// Puts an ability in a slot, or empties it with LoadoutProperties.Empty. APPLY-ONLY: it changes
    /// this machine and nothing else; PlayerLoadout is the only caller and tells the other clients.
    /// Equipping the id already there does nothing, so a loadout echo is harmless.
    /// </summary>
    public void Equip(AbilitySlot slot, int abilityId)
    {
        int index = SlotIndex(slot);
        if (index < 0)
        {
            Debug.LogWarning($"[AbilityRunner] {name}: {slot} is not an ability slot - weapons go through PlayerLoadout.SetWeapon.");
            return;
        }

        AbilityModule current = modules[index];
        if ((current != null ? current.Definition.Id : LoadoutProperties.Empty) == abilityId)
            return;

        AbilityDefinition definition = null;
        AbilityModule prefabModule = null;
        if (abilityId != LoadoutProperties.Empty)
        {
            definition = catalogue != null ? catalogue.Resolve(abilityId) : null;
            if (definition == null)
            {
                Debug.LogWarning($"[AbilityRunner] {name}: no ability with Id {abilityId} in the catalogue - keeping the current one.");
                return;
            }
            if (definition.Slot != slot)
            {
                Debug.LogWarning($"[AbilityRunner] {name}: '{definition.name}' is a {definition.Slot} ability and cannot go in {slot}.");
                return;
            }

            prefabModule = definition.ModulePrefab != null ? definition.ModulePrefab.GetComponent<AbilityModule>() : null;
            if (prefabModule == null)
            {
                Debug.LogError($"[AbilityRunner] {name}: '{definition.name}' has no Module Prefab with an AbilityModule on it - cannot equip.");
                return;
            }
        }

        if (current != null)
        {
            // Told BEFORE it is destroyed, while still in the slot, so a channel it cancels can still
            // send its "cancelled" phase under its own id.
            current.Interrupt(InterruptReason.Unequipped);
            modules[index] = null;
            Destroy(current.gameObject);
        }

        if (definition != null)
        {
            Transform parent = moduleParent != null ? moduleParent : transform;
            GameObject instance = Instantiate(definition.ModulePrefab, parent, false);
            AbilityModule module = instance.GetComponent<AbilityModule>();
            module.Bind(this, owner, definition);
            modules[index] = module;
            module.OnEquip();
        }

        pressBuffers[index] = new CastGate.PressBuffer(); // a press meant for the old ability is not for this one
        SlotChanged?.Invoke(slot);
    }

    // ---- reads, for PlayerLoadout, the HUD and the test range ----------------------------------

    /// <summary>Null when the slot is empty.</summary>
    public IAbilityStatus StatusFor(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        return index >= 0 ? modules[index] : null;
    }

    public int EquippedId(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        return index >= 0 && modules[index] != null ? modules[index].Definition.Id : LoadoutProperties.Empty;
    }

    /// <summary>What PlayerLoadout equips at spawn when nothing else has been chosen.</summary>
    public int StartingId(AbilitySlot slot)
    {
        AbilityDefinition definition = slot == AbilitySlot.Attachment ? startingAttachment
                                     : slot == AbilitySlot.Ultimate ? startingUltimate
                                     : slot == AbilitySlot.Mobility ? startingMobility
                                     : null;
        return definition != null ? definition.Id : LoadoutProperties.Empty;
    }

    /// <summary>
    /// Why this slot cannot cast right now, for the HUD. NotReady for an empty slot. Owner's machine
    /// only: stun, overheat and cooldowns are not replicated, so a remote copy reads defaults.
    /// </summary>
    public CastBlock BlockFor(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        AbilityModule module = index >= 0 ? modules[index] : null;
        if (module == null)
            return CastBlock.NotReady;

        return CastGate.ForAbility(ActorBlock(), module.HasChargeGate, module.HasCharge, module.IsReady);
    }

    /// <summary>F1's "Reset Cooldowns". Registered for the owner only.</summary>
    public void ResetForTestRange() => ResetCooldowns();

    /// <summary>The cleanup a death-then-respawn runs through HandleAliveChanged, without publishing a
    /// death: interrupt every module (no InterruptReason exists for "the match reset", so this reuses
    /// Died), reset every cooldown, then let each module rearm in OnRespawned. Owner only.</summary>
    public void ResetForMatchStart()
    {
        if (!photonView.IsMine)
            return;

        ForEachModule(module => module.Interrupt(InterruptReason.Died));
        ResetCooldowns();
        ForEachModule(module => module.OnRespawned());
    }

    // ---- death, stun, silence -----------------------------------------------------------------

    /// <summary>
    /// Fires on every client (PlayerLifecycle replicates alive state). Dying interrupts everything
    /// everywhere, so a channel stops on every screen without waiting for a message. Coming back
    /// refills cooldowns (on the owner, the only machine that counts them) and lets each module tidy
    /// up. Ultimate charge is separate and must not reset here.
    /// </summary>
    private void HandleAliveChanged(bool alive)
    {
        if (!alive)
        {
            ForEachModule(module => module.Interrupt(InterruptReason.Died));
            return;
        }

        if (photonView.IsMine)
            ResetCooldowns();
        ForEachModule(module => module.OnRespawned());
    }

    /// <summary>Owner only - stun and overheat are owner-only state. Interrupts on the frame each
    /// one switches on; a module that shows something must send its own "cancelled" phase.</summary>
    private void InterruptOnStunOrSilence()
    {
        bool stunned = owner.Status != null && owner.Status.IsStunned;
        bool silenced = owner.Overheat != null && owner.Overheat.IsSilenced;

        if (stunned && !wasStunned)
            ForEachModule(module => module.Interrupt(InterruptReason.Stunned));
        if (silenced && !wasSilenced)
            ForEachModule(module => module.Interrupt(InterruptReason.Silenced));

        wasStunned = stunned;
        wasSilenced = silenced;
    }

    /// <summary>The same actor-wide rule as WeaponFiring.TryFire, so weapon and abilities cannot
    /// disagree about whether this player may act.</summary>
    private CastBlock ActorBlock()
    {
        bool alive = owner.Lifecycle == null || owner.Lifecycle.IsAlive;
        bool stunned = owner.Status != null && owner.Status.IsStunned;
        bool silenced = owner.Overheat != null && owner.Overheat.IsSilenced;
        return CastGate.ForActor(alive, stunned, silenced);
    }

    private void ResetCooldowns() => ForEachModule(module => module.ResetCooldowns());

    private void ForEachModule(System.Action<AbilityModule> action)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (modules[i] != null)
                action(modules[i]);
        }
    }

    // ---- slots ----------------------------------------------------------------------------------

    private bool IsHeld(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        if (index >= 0 && Time.time < toolHoldUntil[index])
            return true;
        if (input == null)
            return false;

        switch (slot)
        {
            case AbilitySlot.Attachment: return input.AttachmentHeld;
            case AbilitySlot.Ultimate: return input.UltimateHeld;
            case AbilitySlot.Mobility: return input.MobilityHeld;
            default: return false;
        }
    }

    /// <summary>-1 for Primary or anything out of range, including a malformed slot byte from the
    /// network; every caller treats it as "not an ability slot".</summary>
    private static int SlotIndex(AbilitySlot slot)
    {
        switch (slot)
        {
            case AbilitySlot.Attachment: return 0;
            case AbilitySlot.Ultimate: return 1;
            case AbilitySlot.Mobility: return 2;
            default: return -1;
        }
    }

    private static AbilitySlot SlotFromIndex(int index) =>
        index == 0 ? AbilitySlot.Attachment : index == 1 ? AbilitySlot.Ultimate : AbilitySlot.Mobility;
}
