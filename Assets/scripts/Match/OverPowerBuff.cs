using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Overpower.Weapons;

namespace Overpower.Match
{
    /// <summary>
    /// Wires the OverPowerState rule (Task 2.6, GDD p.20) into one player: listens for enemy hits
    /// and this player's own death, tracks distance from the player's own territory every frame,
    /// and applies/reverts the buff's three effects - an instant shield refill, +10% damage/fire
    /// rate/range on the primary, and overheat nullified - the moment the rule says to.
    ///
    /// PLAYER ROOT, OWNER ONLY. Nobody but the owner needs their own OverPower state simulated -
    /// the same reasoning AimConeView and PlayerHud already give for the same shape of component:
    /// a remote copy has no local WeaponFiring/PlayerOverheat state that should ever be touched by
    /// this player's own hits, and PlayerHealth.Damaged/Died only mean something useful on the
    /// machine that owns the health being tracked.
    ///
    /// [C, Task 2.6, not in the GDD - see assumptions-for-tudor.md, "OverPower (built)"]: the buff
    /// also ends outright the instant this player dies, exactly like leaving the territory's radius
    /// does. The GDD says nothing about death; a buff (or an armed state left over from a fight
    /// that ended in a death) surviving a respawn back at full health at the capital would be an
    /// odd carry-over nobody asked for.
    /// </summary>
    public class OverPowerBuff : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Match tuning asset. The hit-window seconds, the territory radius, " +
                 "the health threshold that triggers the buff and the +10% stat bonus all come from " +
                 "here, one home for every OverPower number.")]
        private GameplayConfig gameplayConfig;

        private PlayerHealth playerHealth;
        private PlayerOverheat overheat;
        private WeaponFiring weaponFiring;

        private OverPowerState state;

        /// <summary>Two enemy teams have hit this player within the window, near their own
        /// territory, but they have not yet dropped under the health threshold - the HUD's fainter
        /// "about to happen" hint.</summary>
        public bool IsArmed => state != null && state.Armed;

        /// <summary>The buff is live right now: shield refilled, stats boosted, overheat nullified.</summary>
        public bool IsActive => state != null && state.Active;

        /// <summary>Task T3 (telemetry): raised the instant Activate() runs - see that method.</summary>
        public event System.Action Triggered;

        /// <summary>Task T3 (telemetry): raised the instant Deactivate() runs, with "distance" (left
        /// the territory radius - Update's own check) or "death" (HandleDied) - the same two paths
        /// the class comment already documents.</summary>
        public event System.Action<string> Ended;

        private void Awake()
        {
            // Nobody but the owner should simulate their own OverPower state - see the class
            // comment. Every field below stays null on a remote copy, which is what Update, Awake's
            // own subscriptions (never reached) and OnDestroy's unsubscribe guard all rely on.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            // Task 2.6 review fix: GameplayConfig is this buff's ONE home for every number it
            // uses (window/radius/threshold/bonus) - a hardcoded fallback here would be a second,
            // silently-diverging copy of numbers a designer already tunes on the asset. Missing
            // config is loud and this component simply does not run, the same shape PlayerHealth/
            // PlayerOverheat/AimConeView already use for their own required assets.
            if (gameplayConfig == null)
            {
                Debug.LogError($"[OverPowerBuff] {name}: GameplayConfig is not assigned - the comeback buff cannot run.");
                enabled = false;
                return;
            }

            playerHealth = GetComponent<PlayerHealth>();
            overheat = GetComponent<PlayerOverheat>();
            // Searched in children as well as on the root, matching PlayerLifecycle's own lookup -
            // the primary weapon's component does not live on the player root.
            weaponFiring = GetComponentInChildren<WeaponFiring>(true);

            if (playerHealth == null || overheat == null || weaponFiring == null)
            {
                Debug.LogError($"[OverPowerBuff] {name}: missing PlayerHealth/PlayerOverheat/WeaponFiring on this player - the comeback buff cannot run.");
                enabled = false;
                return;
            }

            state = new OverPowerState(gameplayConfig.OverPowerWindowSeconds, gameplayConfig.OverPowerRadius,
                                       gameplayConfig.OverPowerHealthThreshold);

            playerHealth.Damaged += HandleDamaged;
            playerHealth.Died += HandleDied;
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.Damaged -= HandleDamaged;
                playerHealth.Died -= HandleDied;
            }
        }

        private void Update()
        {
            if (playerHealth == null || state == null)
                return;

            bool wasActive = state.Active;

            state.UpdateDistance(DistanceToOwnTerritory());

            if (state.CheckTrigger(playerHealth.Health))
                Activate();

            if (wasActive && !state.Active)
                Deactivate("distance"); // The only way Update itself ends it - see UpdateDistance.
        }

        /// <summary>PlayerHealth.Damaged handler: works out the attacker's TEAM from the actor
        /// number DamageInfo carries, then hands off to RegisterHitFrom below. Self and teammate
        /// hits never reach here in practice - PlayerHealth.ApplyDamage already refuses both before
        /// raising Damaged (see its own comment) - but both are still checked here as a
        /// belt-and-braces per the task brief, in case that upstream guard ever changes. An unknown
        /// team (Teams.TryGetTeam fails) never arms the buff either - fails SAFE, the opposite of
        /// Teams.AreSameTeam's own deliberate fail-open, because arming a comeback buff on bad data
        /// is a worse mistake here than the one friendly-fire's fail-open avoids.</summary>
        private void HandleDamaged(DamageResult result, DamageInfo info)
        {
            if (info.SourceActorNumber <= 0)
                return;

            Player attacker = PhotonNetwork.CurrentRoom != null
                ? PhotonNetwork.CurrentRoom.GetPlayer(info.SourceActorNumber)
                : null;
            if (attacker == null || attacker == photonView.Owner)
                return;

            if (!Teams.TryGetTeam(attacker, out int attackerTeam) || attackerTeam == playerHealth.TeamId)
                return;

            RegisterHitFrom(attackerTeam);
        }

        /// <summary>
        /// Records one enemy team's hit against the rule. Kept separate from the actor-to-team
        /// lookup in HandleDamaged above (Task 2.6 step 7) so the rule can be armed - by a real
        /// second enemy team over the two-client harness, or by reflection in a single-client/
        /// two-client verification pass that has at most one real enemy team to hit from - without
        /// needing a live player standing on every team the GDD's "both enemy teams" describes.
        /// </summary>
        private void RegisterHitFrom(int attackerTeam)
        {
            state.RecordEnemyHit(attackerTeam, Time.time, DistanceToOwnTerritory());
        }

        private void HandleDied(DamageInfo info)
        {
            bool wasActive = state.Active;
            state.EndOnDeath();
            if (wasActive)
                Deactivate("death");
        }

        private float DistanceToOwnTerritory()
        {
            if (BuildingManager.Instance == null)
                return float.PositiveInfinity;

            return BuildingManager.Instance.DistanceToOwnedZoneEdge(transform.position, playerHealth.TeamId);
        }

        /// <summary>The moment the buff triggers: "regenerate their shield instantly and gain a 10%
        /// increase in 3 of the highest parameters on their primary ability while nullifying the
        /// overheat mechanic" (GDD p.20).
        ///
        /// Task 2.6 review fix: Clear(), not just SetSuppressed - a defender who triggers this
        /// while ALREADY silenced (heat maxed from the fight that just dropped them under the
        /// threshold) used to stay silenced through their own comeback moment, because
        /// SetSuppressed only blocks future Add calls and does nothing about heat/silence already
        /// in effect. "Nullifying the overheat mechanic" has to mean the weapon is usable the
        /// instant the buff triggers, not merely that heat stops climbing further. Sprint spends
        /// the same shared pool (PlayerOverheat's own class comment), so this clears its lockout
        /// too - a deliberate side effect, not a special case for the weapon.</summary>
        private void Activate()
        {
            playerHealth?.RefillArmor();

            float bonus = gameplayConfig.OverPowerStatBonus;
            weaponFiring?.SetStatMultipliers(1f + bonus, 1f + bonus, 1f + bonus);
            overheat?.Clear();
            overheat?.SetSuppressed(this, true);

            Triggered?.Invoke();
        }

        /// <summary>Reached either from distancing yourself past the territory radius or from dying
        /// (HandleDied) - both put every multiplier straight back to 1 and lift the overheat
        /// suppression. The shield refill Activate granted is NOT undone: it already happened, the
        /// same way a health regen tick is not un-ticked when regen conditions change.</summary>
        private void Deactivate(string reason)
        {
            weaponFiring?.SetStatMultipliers(1f, 1f, 1f);
            overheat?.SetSuppressed(this, false);

            Ended?.Invoke(reason);
        }
    }
}
