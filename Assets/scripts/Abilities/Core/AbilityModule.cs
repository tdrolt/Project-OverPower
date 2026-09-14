using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Abilities
{
    /// <summary>
    /// The base class every ability derives from - dash, mine, flamethrower, ultimate. One subclass
    /// per ability, sitting alone on its own prefab, holding every number that ability has.
    ///
    /// HOW ONE CAST HAPPENS
    /// 1. The player presses a key. AbilityRunner, on the OWNER's machine only, checks the gate
    ///    (dead, stunned, silenced, out of charges, IsReady) and calls TryBuildCast with what the
    ///    owner's machine knows (CastContext).
    /// 2. If the module agrees, the runner spends a charge and sends one RPC carrying the payload.
    /// 3. EVERY client - the caster's own included - receives it and calls ExecuteCast. That is
    ///    where the ability actually happens: a sphere appears, a dash starts, a mine is placed.
    ///
    /// So a module has two halves. The owner-only half (TryBuildCast, OwnerTick) may read input and
    /// local state. The every-client half (ExecuteCast, Interrupt, OnRespawned) must only use what
    /// the CastEvent gives it, because on every other machine "local" means someone else.
    ///
    /// Deliberately NOT MonoBehaviourPun, and a module prefab must have no PhotonView and no
    /// Collider: PUN only delivers an RPC to components on the PhotonView's own GameObject, so an
    /// RPC on a module would never arrive (use SendPhase instead), and a Collider would be swept
    /// onto the DeadPlayer layer with the rest of the player on death. AbilityDefinition's
    /// OnValidate refuses both.
    ///
    /// TRUST MODEL: cooldowns, charges and casts are decided by the caster's own client and
    /// believed by everyone else. There is no anti-cheat - this is a prototype on a Photon relay
    /// with no server to check anything, and checks written here would only look like security.
    /// </summary>
    public abstract class AbilityModule : MonoBehaviour, IAbilityStatus
    {
        [Header("Cooldown")]
        [SerializeField, Tooltip("Seconds for ONE charge to come back after it is spent. Charges " +
                 "return one at a time: with 2 charges and 5 seconds, spending both gets the first " +
                 "back after 5s and the second after 10s. 0 means a spent charge is back the very " +
                 "next frame. Can be changed while playing; it affects the next recharge.")]
        private float cooldownSeconds = 5f;

        [SerializeField, Tooltip("How many casts can be stored up and used back to back. 0 means " +
                 "this ability has no cooldown at all (a sprint that spends heat instead), and the " +
                 "HUD draws no cooldown for it.")]
        private int charges = 1;

        private AbilityRunner runner;

        // Null when charges is 0 - "no cooldown gate" is represented by there being no pool at all,
        // so nothing downstream can mistake an empty pool for one that is merely recharging.
        private ChargePool pool;

        /// <summary>The player this module belongs to. Bound just after the module is created -
        /// do not read it in Awake, where it is still null.</summary>
        protected AbilityOwner Owner { get; private set; }

        public AbilityDefinition Definition { get; private set; }

        // ---- OWNER only, called by AbilityRunner --------------------------------------------

        /// <summary>An extra gate on top of charges - ultimate charge full, a portal already
        /// placed. False greys the icon out as "not ready" rather than "recharging".</summary>
        public virtual bool IsReady => true;

        /// <summary>True for almost everything. A teleport returns false so the charge is spent only
        /// once the travel actually happens, by calling SpendCharge itself.</summary>
        protected virtual bool SpendsChargeOnCast => true;

        /// <summary>
        /// Owner only. Turns what the caster's machine knows into the payload every client will
        /// receive. Return false to refuse the cast (no valid target, say) - nothing is spent and
        /// nothing is sent. The only hook allowed to read input, the aim or the camera.
        /// </summary>
        public abstract bool TryBuildCast(in CastContext ctx, out CastPayload payload);

        /// <summary>
        /// Owner only, every frame the ability is equipped.
        /// held: the ability's key is down AND the player can act. It goes false the moment the key
        /// is released, the player dies, is stunned or silenced, or a tool window takes focus -
        /// which is why there is no "released" hook: a hold ends when held goes false, and that
        /// already covers every way a hold can end.
        /// canAct: the player is alive, not stunned and not silenced, whether or not any key is
        /// down. For abilities that channel without holding a key (standing in a teleport circle).
        /// </summary>
        public virtual void OwnerTick(float deltaTime, bool held, bool canAct) { }

        /// <summary>Owner only. Sends a later moment of this ability (a channel completing or being
        /// cancelled) through the runner's one RPC, so every client's ExecuteCast sees it with the
        /// given phase. The id travels with it, so it can never reach the wrong module.</summary>
        protected void SendPhase(byte phase, in CastPayload payload)
        {
            if (runner != null)
                runner.SendCast(this, phase, payload);
        }

        /// <summary>Owner only. Spends one charge; false if there was none to spend. Always true
        /// for an ability with no charges.</summary>
        protected bool SpendCharge() => pool == null || pool.TryConsume();

        /// <summary>Owner only. Refills every charge at once - the zip gun's reset on a takedown.</summary>
        protected void RefillCharges() => pool?.RefillAll();

        // ---- EVERY client, the caster included ------------------------------------------------

        /// <summary>Runs the cast (or one of its later phases) on this machine. Use only what the
        /// CastEvent carries - see the class comment for why.</summary>
        public abstract void ExecuteCast(in CastEvent cast);

        /// <summary>Just after the module is created and bound. Owner is safe to read from here on.</summary>
        public virtual void OnEquip() { }

        /// <summary>
        /// Stop whatever is running. Called on every client for Died and Unequipped; on the owner
        /// only for Stunned and Silenced, since only the owner knows about those. A module that
        /// cancels something visible must therefore also SendPhase its "cancelled" phase from the
        /// owner, or the other clients never stop drawing it. A module that set
        /// PlayerMotor.ExternalMotionControl must clear it here, or the respawned player cannot
        /// walk.
        /// </summary>
        public virtual void Interrupt(InterruptReason reason) { }

        /// <summary>On every client when the player comes back to life, after cooldowns refill.</summary>
        public virtual void OnRespawned() { }

        // ---- status, for the HUD and the gate -----------------------------------------------------

        public int ChargesAvailable => pool != null ? pool.Available : 0;
        public int MaxCharges => pool != null ? pool.MaxCharges : 0;
        public float RechargeProgress => pool != null ? pool.RechargeProgress : 0f;
        public virtual bool IsActive => false;

        /// <summary>The serialized cooldown this module was authored with - the DESIGN number, not
        /// a live remaining cooldown. Added for the loadout screen's hover text (Task 9b), which
        /// reads a MODULE PREFAB ASSET: ChargesAvailable/RechargeProgress above answer "how charged
        /// up is THIS player's live pool right now" and are 0 on a prefab, which has no pool at all
        /// (Bind never ran on it) - this and Charges below are the two numbers that exist either
        /// way.</summary>
        public float CooldownSeconds => cooldownSeconds;

        /// <summary>The serialized charge count this module was authored with - same "a prefab
        /// asset has no live pool to read instead" reasoning as CooldownSeconds above.</summary>
        public int Charges => charges;

        internal bool HasChargeGate => pool != null;
        internal bool HasCharge => pool == null || pool.Available > 0;
        internal bool SpendsChargeWhenCast => SpendsChargeOnCast;
        internal bool TrySpendChargeForCast() => SpendCharge();

        internal void Bind(AbilityRunner owningRunner, AbilityOwner owner, AbilityDefinition definition)
        {
            runner = owningRunner;
            Owner = owner;
            Definition = definition;
            ApplyCooldownTuning();
        }

        /// <summary>Owner only - nobody else's machine counts down your cooldowns.</summary>
        internal void TickCooldown(float deltaTime) => pool?.Tick(deltaTime);

        /// <summary>Respawn and the test range's "Reset Cooldowns" button.</summary>
        internal void ResetCooldowns() => pool?.RefillAll();

        /// <summary>
        /// Keeps the live pool in step with the two Inspector fields. Without this, ChargePool would
        /// keep the numbers it was built with and a designer retuning a cooldown during Play would
        /// see nothing change.
        /// </summary>
        private void ApplyCooldownTuning()
        {
            if (charges <= 0)
            {
                pool = null;
                return;
            }

            if (pool == null)
                pool = new ChargePool(charges, cooldownSeconds);
            else
            {
                pool.SetMaxCharges(charges);
                pool.SetRechargeSeconds(cooldownSeconds);
            }
        }

        protected virtual void OnValidate()
        {
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            charges = Mathf.Max(0, charges);

            // Only a module that is live on a player has a pool to retune. The prefab asset itself
            // also runs OnValidate, and has no runner.
            if (runner != null)
                ApplyCooldownTuning();
        }
    }
}
