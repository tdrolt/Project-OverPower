using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Overpower.TestRange;

/// <summary>
/// One player's three ability slots - Equipment (right mouse), Ultimate (Space), Mobility (Left
/// Shift). Primary (left mouse) stays WeaponFiring's. This class owns the parts every ability
/// shares, so no ability has to write them again: reading the keys, the "can you act" gate, the
/// press buffer, the one network message, and what happens on death and respawn. Everything that
/// makes a dash a dash lives in its own AbilityModule prefab instead - see AbilityModule.
///
/// WHY ONE RPC HERE, AND NONE ON THE MODULES. PUN only delivers an RPC to components on the
/// PhotonView's own GameObject, which is the player root - so a module (a child object) can never
/// receive one. The alternative, a PhotonView per module, means allocating network ids at runtime
/// for every ability equipped. Instead every ability's every cast travels through RPC_CastAbility
/// below with the slot and the ability id beside it, and adding an ability never touches the RPC
/// list in PhotonServerSettings, the most fragile file in the project.
///
/// WHO DECIDES. Only the owner's copy of this component reads keys and checks the gate. Every copy,
/// the owner's included, runs the cast when the RPC arrives - and never gates it again: overheat,
/// stun and cooldowns are not replicated, so a receiver checking them would be checking the wrong
/// player's numbers and silently dropping real casts.
///
/// TRUST MODEL: cooldowns, charges and casts are client-authoritative - the caster's own machine
/// decides and everyone else believes it. No anti-cheat; this is a prototype on a relay with no
/// server that could check anything.
///
/// Deliberately NOT IPunObservable - PlayerNetSync is the player's only observable (see its class
/// comment). And deliberately not switched off by PlayerLifecycle while dead: this component must
/// stay awake to hear AliveChanged and to keep receiving casts sent just before a death.
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
    [SerializeField, Tooltip("The right-mouse ability a player starts the match with. The shop and " +
             "the test range replace it at runtime through PlayerLoadout.")]
    private AbilityDefinition startingEquipment;

    [SerializeField, Tooltip("The Space ability a player starts the match with.")]
    private AbilityDefinition startingUltimate;

    [SerializeField, Tooltip("The Left Shift ability a player starts the match with.")]
    private AbilityDefinition startingMobility;

    // Indexed by SlotIndex: 0 Equipment, 1 Ultimate, 2 Mobility. Primary has no entry - it is a weapon.
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

    // A bad cast RPC (wrong slot for its id, unknown id) is reported once per session, not once per
    // packet - a stale build on the other end would otherwise fill the log every cast.
    private static bool loggedRejectedCast;

    /// <summary>Raised on this client whenever a slot's module is created, replaced or removed - for
    /// the HUD to redraw that slot's icon.</summary>
    public event System.Action<AbilitySlot> SlotChanged;

    /// <summary>Task T3 (telemetry): raised on the caster's own client right after a successful
    /// cast's RPC is sent - TryCast only, never SendPhase's later phases (a channel completing or
    /// being cancelled is not a fresh cast). PlayerTelemetry reads its own transform.position for
    /// the `cast` line's x/z, so this carries only what a "cast" event actually needs to identify
    /// which one happened.</summary>
    public event System.Action<AbilitySlot, int> Cast;

    private void Awake()
    {
        owner = new AbilityOwner(gameObject);
        input = GetComponent<PlayerInputRouter>();

        // Loud, matching WeaponFiring: a silent null here would leave a player with abilities that
        // either never equip or cannot be resolved on anyone else's machine.
        if (catalogue == null)
            Debug.LogError($"[AbilityRunner] {name}: Ability Catalogue is not assigned - abilities cannot be equipped or received.");
        if (gameplayConfig == null)
            Debug.LogError($"[AbilityRunner] {name}: GameplayConfig is not assigned - the press buffer falls back to 0s (no buffering).");
        if (moduleParent == null)
            Debug.LogError($"[AbilityRunner] {name}: Module Parent is not assigned - modules will be created on the player root instead.");

        // Subscribed in Awake, not Start: PlayerLifecycle's own Start can already raise AliveChanged
        // (a late joiner learning this player is dead), and Start order between components is not
        // guaranteed.
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

        input.EquipmentPressed += HandleEquipmentPressed;
        input.UltimatePressed += HandleUltimatePressed;
        input.MobilityPressed += HandleMobilityPressed;
    }

    private void OnDisable()
    {
        TestRangeResetRegistry.Unregister(this);

        if (input == null)
            return;

        input.EquipmentPressed -= HandleEquipmentPressed;
        input.UltimatePressed -= HandleUltimatePressed;
        input.MobilityPressed -= HandleMobilityPressed;
    }

    private void HandleEquipmentPressed() => PressSlot(AbilitySlot.Equipment);
    private void HandleUltimatePressed() => PressSlot(AbilitySlot.Ultimate);
    private void HandleMobilityPressed() => PressSlot(AbilitySlot.Mobility);

    // ---- the owner's frame ----------------------------------------------------------------

    private void Update()
    {
        if (!photonView.IsMine)
            return; // Nobody else's machine decides anything about this player's casts.

        float deltaTime = Time.deltaTime;
        float now = Time.time;
        // No literal duplicating GameplayConfig's own default here: the missing-config error is
        // already logged in Awake, so a missing config degrades honestly to no buffering at all
        // rather than silently guessing at the tuning value.
        float window = gameplayConfig != null ? gameplayConfig.AbilityPressBufferSeconds : 0f;

        InterruptOnStunOrSilence();

        CastBlock actorBlock = ActorBlock();
        bool canAct = actorBlock == CastBlock.None;

        for (int i = 0; i < SlotCount; i++)
        {
            AbilityModule module = modules[i];
            if (module == null)
                continue;

            // Cooldown first, so a charge returning this very frame is already spendable by a
            // press buffered a moment ago - the whole point of the buffer.
            module.TickCooldown(deltaTime);

            if (pressBuffers[i].IsPending(now, window) &&
                CastGate.ForAbility(actorBlock, module.HasChargeGate, module.HasCharge, module.IsReady) == CastBlock.None)
            {
                bool cast = TryCast(module);

                // Consumed on an actual cast, or on a refusal from a module that does not ask to retry: a module
                // refusing (no valid target) is normally an answer, and retrying it every frame for the rest of the
                // window would just ask the same question again. A module that opts into
                // RetriesRefusalWithinBuffer (Dash: review fix) keeps the press alive instead, so the SAME press
                // still fires the moment the refusal's own reason clears within the window.
                if (cast || !module.RetriesRefusalWithinBuffer)
                    pressBuffers[i].TryConsume(now, window);
            }

            // held is only ever true while the player can act, so a channel or sprint ends the
            // instant a stun, silence or death lands - see AbilityModule.OwnerTick.
            bool held = canAct && IsHeld(SlotFromIndex(i));
            module.OwnerTick(deltaTime, held, canAct);
        }
    }

    /// <summary>
    /// Owner only. Presses one slot's key as far as abilities are concerned - buffered, then gated
    /// and cast on this frame's Update. Public so the test range and editor tooling drive the exact
    /// path a key press does; there is no second casting route that could behave differently.
    /// </summary>
    public void PressSlot(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        if (!photonView.IsMine || index < 0)
            return;

        pressBuffers[index].Press(Time.time);
    }

    /// <summary>
    /// Owner only. Holds one slot's key down, as far as abilities are concerned, for the given
    /// number of seconds - PressSlot's partner for testing channels and sprints from editor tooling,
    /// where the real keyboard does not reach the game. Deliberately time-limited rather than an
    /// on/off switch, so a tool that forgets to let go can never leave a key stuck down; 0 lets go
    /// at once. Still subject to the gate: a stun or death ends the hold exactly as it would a
    /// real key.
    /// </summary>
    public void HoldSlot(AbilitySlot slot, float seconds)
    {
        int index = SlotIndex(slot);
        if (!photonView.IsMine || index < 0)
            return;

        toolHoldUntil[index] = Time.time + Mathf.Max(0f, seconds);
    }

    /// <summary>True when the cast actually happened - the buffered press consumption above reads this.</summary>
    private bool TryCast(AbilityModule module)
    {
        CastContext ctx = BuildContext();
        if (!module.TryBuildCast(ctx, out CastPayload payload))
            return false;

        // Spent BEFORE the RPC goes out. With RpcTarget.All the caster's own copy of the RPC runs
        // synchronously inside photonView.RPC, so ExecuteCast must already see the charge gone -
        // a module reading ChargesAvailable there would otherwise count a charge it just used.
        if (module.SpendsChargeWhenCast && !module.TrySpendChargeForCast())
            return false;

        SendCast(module, 0, payload);
        Cast?.Invoke(module.Definition.Slot, module.Definition.Id);
        return true;
    }

    /// <summary>What the caster's machine knows at the press, gathered in one place so no module
    /// ever reads the mouse or the camera itself.</summary>
    private CastContext BuildContext()
    {
        Vector3 origin = transform.position;
        // SafeMuzzlePosition, not MuzzlePosition - a cast fired flush against a wall (the stun gun,
        // the zip gun) must start on the near side of it, exactly like a weapon shot now does. See
        // WeaponFiring.SafeMuzzlePosition's own comment (Task 1.9 follow-up review finding).
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

        // All, not AllViaServer (unlike WeaponFiring): an ability usually moves the caster's own
        // body, and waiting for the server round trip before your own dash starts reads as input
        // lag. Other clients still receive one sender's RPCs in the order they were sent.
        photonView.RPC(nameof(RPC_CastAbility), RpcTarget.All, (byte)module.Definition.Slot,
                       module.Definition.Id, phase, payload.Origin, payload.Direction, payload.Point,
                       payload.Seed, payload.IntArg, payload.FloatArg);
    }

    // ---- every client -------------------------------------------------------------------------

    /// <summary>
    /// Runs one cast on this machine, for whoever sent it. Appended to the RpcList in
    /// PhotonServerSettings: NEVER rename it and never change its parameters - PUN sends an index
    /// into that list, and every machine in a room must agree on it.
    ///
    /// No gate here, on purpose (see the class comment). Everything about the caster comes from the
    /// parameters or info.Sender; inside this body PhotonNetwork.LocalPlayer is the receiver.
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
            // A later phase for a module this client no longer holds is simply over: the
            // Unequipped interrupt already stopped it here. Re-equipping the old module just to
            // show it being cancelled would throw away the newer loadout.
            if (phase != 0)
                return;

            // The cast beat the loadout property here - Custom Properties and RPCs are separate
            // messages. The id travels beside the slot precisely so this can be fixed on the spot
            // instead of running whichever module happened to be in the slot.
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
    /// this machine and nothing else. PlayerLoadout is the only thing that should call it - it is
    /// what tells the other clients. Equipping the id already there does nothing, so a loadout
    /// echo or a repeated property update is harmless.
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
            // Told BEFORE it is destroyed, while it is still in the slot, so a channel it cancels can
            // still send its "cancelled" phase under its own id.
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

        pressBuffers[index] = new CastGate.PressBuffer(); // A press meant for the old ability is not for this one.
        SlotChanged?.Invoke(slot);
    }

    // ---- reads, for PlayerLoadout, the HUD and the test range ----------------------------------

    /// <summary>The slot's status for the HUD, or null when the slot is empty.</summary>
    public IAbilityStatus StatusFor(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        return index >= 0 ? modules[index] : null;
    }

    /// <summary>The id in the slot, or LoadoutProperties.Empty.</summary>
    public int EquippedId(AbilitySlot slot)
    {
        int index = SlotIndex(slot);
        return index >= 0 && modules[index] != null ? modules[index].Definition.Id : LoadoutProperties.Empty;
    }

    /// <summary>The prefab's starting ability for a slot, as an id - what PlayerLoadout equips at
    /// spawn when nothing else has been chosen.</summary>
    public int StartingId(AbilitySlot slot)
    {
        AbilityDefinition definition = slot == AbilitySlot.Equipment ? startingEquipment
                                     : slot == AbilitySlot.Ultimate ? startingUltimate
                                     : slot == AbilitySlot.Mobility ? startingMobility
                                     : null;
        return definition != null ? definition.Id : LoadoutProperties.Empty;
    }

    /// <summary>
    /// Why this slot cannot cast right now, live - for the HUD to grey out an icon and say why.
    /// NotReady for an empty slot. Meaningful on the owner's machine only: stun, overheat and
    /// cooldowns are not replicated, so a remote copy would read its own defaults.
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

    /// <summary>2.7b Decision 6: the same cleanup a death-then-respawn already runs through
    /// HandleAliveChanged, without ever publishing a death - interrupt every module (there is no
    /// InterruptReason for "the match reset", so this reuses Died, exactly as the plan specifies),
    /// reset every cooldown, then let each module react to "respawned" so it rearms whatever OnRespawned
    /// means to it. Owner only: only the owner ever ticks or resets these modules.</summary>
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
    /// refills cooldowns - on the owner, the only machine that counts them - and then lets each
    /// module tidy up. Ultimate charge (Task 1.11) is separate and must not reset here.
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

    /// <summary>The same actor-wide rule WeaponFiring.TryFire asks, so the weapon and the abilities
    /// can never disagree about whether this player may act.</summary>
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
            case AbilitySlot.Equipment: return input.EquipmentHeld;
            case AbilitySlot.Ultimate: return input.UltimateHeld;
            case AbilitySlot.Mobility: return input.MobilityHeld;
            default: return false;
        }
    }

    /// <summary>-1 for Primary or anything out of range, which every caller treats as "not an
    /// ability slot" - including a malformed slot byte arriving over the network.</summary>
    private static int SlotIndex(AbilitySlot slot)
    {
        switch (slot)
        {
            case AbilitySlot.Equipment: return 0;
            case AbilitySlot.Ultimate: return 1;
            case AbilitySlot.Mobility: return 2;
            default: return -1;
        }
    }

    private static AbilitySlot SlotFromIndex(int index) =>
        index == 0 ? AbilitySlot.Equipment : index == 1 ? AbilitySlot.Ultimate : AbilitySlot.Mobility;
}
