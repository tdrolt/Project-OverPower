using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.Weapons;
using Overpower.TestRange;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Player prefab, owner only: logs everything this player's own client is the sole authority on
    /// (Principle 2 of the design doc - "a hit" is logged by the victim, "shots"/"casts" by the
    /// shooter/caster, all of which are always THIS player when this component is enabled at all).
    /// Every T3 event goes through MatchTelemetry.Instance.Log, which no-ops when telemetry is off
    /// or the session's file never opened - this class never checks that itself.
    ///
    /// REMOTE COPIES DO NOTHING: Awake disables this component outright when photonView.IsMine is
    /// false, before any GetComponent call or subscription runs - the same shape OverPowerBuff's own
    /// Awake already uses. Deleting Assets/scripts/Telemetry still leaves the game running (design
    /// doc, Principle 1): nothing outside this folder depends on this class existing.
    ///
    /// ALLOCATION: one TelemetryLine is built once and reused for every event this component ever
    /// raises (Begin/.../End is safe to call again immediately - see TelemetryLine's own class
    /// comment); the only heap traffic per frame is the two cheap bool reads in Update's polling
    /// (IsSilenced, IsFull), not a single line built or logged unless something actually happened or
    /// the sample interval elapsed.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerTelemetry : MonoBehaviourPun
    {
        [Tooltip("Turns this player's own telemetry on/off and holds the sample interval and " +
                 "whether positions are recorded - the same asset MatchTelemetry reads, assigned " +
                 "separately here since MatchTelemetry keeps its own copy private.")]
        [SerializeField] private TelemetryConfig config;

        private PlayerHealth playerHealth;
        private PlayerStatusEffects statusEffects;
        private PlayerLifecycle lifecycle;
        private WeaponFiring weaponFiring;
        private AbilityRunner abilityRunner;
        private OverPowerBuff overPowerBuff;
        private PlayerOverheat overheat;
        private UltimateCharge ultimateCharge;
        private PlayerCombatCredit combatCredit;
        private GoldWallet goldWallet;

        private readonly TelemetryLine line = new TelemetryLine();

        private float sampleTimer;

        // Polled state transitions (Update), never events of their own - see the design doc's
        // "Changes from the spec" note.
        private bool wasSilenced;
        private bool wasUltimateFull;

        // Time.time this ultimate last became ready (IsFull false -> true), or -1 while it either
        // has not filled yet or was already spent since - ultimateUsed's own "seconds since ready".
        private float ultimateReadyTime = -1f;

        // Time.time this life started - Start() for the very first life, AliveChanged(true) for
        // every respawn after. death's own "time alive".
        private float aliveSinceTime;

        // Time.time the most recent death happened - respawn's own "time dead".
        private float deathTime;

        private sealed class ShotAccumulator
        {
            public int Pulls;
            public int Projectiles;
        }

        // Cleared on every flush (sample interval, death, OnDestroy) rather than reset in place -
        // see FlushShots. Never allocated per frame: only Fired (a real shot) or a flush touches it.
        private readonly Dictionary<int, ShotAccumulator> shotsByWeapon = new Dictionary<int, ShotAccumulator>();

        private void Awake()
        {
            // Nobody but the owner should log their own combat/economy facts - see the class
            // comment. Every field below stays null on a remote copy, which is what Start's
            // subscriptions and Update's polling both rely on never running.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            if (config == null)
                Debug.LogError($"[PlayerTelemetry] {name}: TelemetryConfig is not assigned - the sample interval and Record Positions fall back to hardcoded defaults.");

            playerHealth = GetComponent<PlayerHealth>();
            statusEffects = GetComponent<PlayerStatusEffects>();
            lifecycle = GetComponent<PlayerLifecycle>();
            abilityRunner = GetComponent<AbilityRunner>();
            overPowerBuff = GetComponent<OverPowerBuff>();
            overheat = GetComponent<PlayerOverheat>();
            ultimateCharge = GetComponent<UltimateCharge>();
            combatCredit = GetComponent<PlayerCombatCredit>();
            goldWallet = GetComponent<GoldWallet>();
            // Not on the root - see WeaponFiring's own siblings (OverPowerBuff, PlayerLifecycle)
            // for the identical GetComponentInChildren lookup.
            weaponFiring = GetComponentInChildren<WeaponFiring>(true);

            if (playerHealth == null || statusEffects == null || lifecycle == null || weaponFiring == null)
                Debug.LogError($"[PlayerTelemetry] {name}: missing PlayerHealth/PlayerStatusEffects/PlayerLifecycle/WeaponFiring - most events cannot be logged for this player.");
        }

        private void Start()
        {
            aliveSinceTime = Time.time;

            if (playerHealth != null)
            {
                playerHealth.Damaged += HandleDamaged;
                playerHealth.Died += HandleDied;
            }
            if (statusEffects != null)
                statusEffects.StatusApplied += HandleStatusApplied;
            if (lifecycle != null)
                lifecycle.AliveChanged += HandleAliveChanged;
            if (weaponFiring != null)
                weaponFiring.Fired += HandleFired;
            if (abilityRunner != null)
                abilityRunner.Cast += HandleCast;
            if (overPowerBuff != null)
            {
                overPowerBuff.Triggered += HandleOverpowerTriggered;
                overPowerBuff.Ended += HandleOverpowerEnded;
            }

            // Static event (Task T3): a dummy is not networked and lives in exactly one client's
            // scene, so the only listener that could ever be right is that same client's own local
            // player - see DummyTarget.AnyDamaged's own comment.
            DummyTarget.AnyDamaged += HandleDummyDamaged;
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.Damaged -= HandleDamaged;
                playerHealth.Died -= HandleDied;
            }
            if (statusEffects != null)
                statusEffects.StatusApplied -= HandleStatusApplied;
            if (lifecycle != null)
                lifecycle.AliveChanged -= HandleAliveChanged;
            if (weaponFiring != null)
                weaponFiring.Fired -= HandleFired;
            if (abilityRunner != null)
                abilityRunner.Cast -= HandleCast;
            if (overPowerBuff != null)
            {
                overPowerBuff.Triggered -= HandleOverpowerTriggered;
                overPowerBuff.Ended -= HandleOverpowerEnded;
            }
            DummyTarget.AnyDamaged -= HandleDummyDamaged;

            // "On death/leave" - see the plan's own `shots` bullet. This player object is about to
            // stop existing (this client leaving the room, or the player itself being destroyed),
            // so anything accumulated since the last sample must not be lost.
            FlushShots();
        }

        private void Update()
        {
            if (MatchTelemetry.Instance == null)
                return;

            PollOverheat();
            PollUltimateCharge();

            float interval = config != null ? config.SampleIntervalSeconds : 5f;
            sampleTimer += Time.unscaledDeltaTime;
            if (sampleTimer < interval)
                return;

            sampleTimer = 0f;
            WriteSample();
            FlushShots();
        }

        // ---------------------------------------------------------------- overheat / ultimate polling

        private void PollOverheat()
        {
            if (overheat == null)
                return;

            bool silenced = overheat.IsSilenced;
            if (silenced == wasSilenced)
                return;
            wasSilenced = silenced;

            line.Begin(TelemetryKeys.Overheat, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.State, silenced ? "silenced" : "recovered");
            line.Int(TelemetryKeys.Weapon, weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : -1);
            MatchTelemetry.Instance.Log(line);
        }

        private void PollUltimateCharge()
        {
            if (ultimateCharge == null)
                return;

            bool full = ultimateCharge.IsFull;
            if (full == wasUltimateFull)
                return;
            wasUltimateFull = full;

            if (!full)
                return; // Only the false -> true edge is `ultimateReady` (going down just means it was spent).

            ultimateReadyTime = Time.time;

            line.Begin(TelemetryKeys.UltimateReady, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.AbilityId, abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Ultimate) : -1);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- shots

        private void HandleFired(int weaponId, int projectileCount)
        {
            if (!shotsByWeapon.TryGetValue(weaponId, out ShotAccumulator acc))
            {
                acc = new ShotAccumulator();
                shotsByWeapon[weaponId] = acc;
            }

            acc.Pulls++;
            acc.Projectiles += projectileCount;
        }

        private void FlushShots()
        {
            if (shotsByWeapon.Count == 0 || MatchTelemetry.Instance == null)
                return;

            foreach (KeyValuePair<int, ShotAccumulator> pair in shotsByWeapon)
            {
                line.Begin(TelemetryKeys.Shots, MatchTelemetry.Instance.Now);
                line.Int(TelemetryKeys.Weapon, pair.Key);
                line.Int(TelemetryKeys.Pulls, pair.Value.Pulls);
                line.Int(TelemetryKeys.Projectiles, pair.Value.Projectiles);
                MatchTelemetry.Instance.Log(line);
            }

            shotsByWeapon.Clear();
        }

        // ---------------------------------------------------------------- cast / ultimate used

        private void HandleCast(AbilitySlot slot, int abilityId)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Cast, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Slot, (int)slot);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.X, transform.position.x);
            line.Float(TelemetryKeys.Z, transform.position.z);
            MatchTelemetry.Instance.Log(line);

            if (slot != AbilitySlot.Ultimate)
                return;

            float sinceReady = ultimateReadyTime >= 0f ? Time.time - ultimateReadyTime : -1f;
            ultimateReadyTime = -1f; // Spent - the next `ultimateReady` starts a fresh wait.

            line.Begin(TelemetryKeys.UltimateUsed, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.SecondsSinceReady, sinceReady);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- status

        private void HandleStatusApplied(StatusKind kind, int sourceActor, int abilityId, float durationOrMagnitude)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Status, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.Effect, kind.ToString().ToLowerInvariant());
            line.Int(TelemetryKeys.SourceActor, sourceActor);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.DurationOrMagnitude, durationOrMagnitude);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- overpower

        private void HandleOverpowerTriggered()
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Overpower, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.State, "triggered");
            line.Float(TelemetryKeys.ZoneDistance, ZoneDistance());
            line.Float(TelemetryKeys.HealthAtTrigger, playerHealth != null ? playerHealth.Health : 0f);
            MatchTelemetry.Instance.Log(line);
        }

        private void HandleOverpowerEnded(string reason)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Overpower, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.State, "ended");
            line.String(TelemetryKeys.Reason, reason);
            line.Float(TelemetryKeys.ZoneDistance, ZoneDistance());
            line.Float(TelemetryKeys.HealthAtTrigger, playerHealth != null ? playerHealth.Health : 0f);
            MatchTelemetry.Instance.Log(line);
        }

        private float ZoneDistance()
        {
            if (BuildingManager.Instance == null || playerHealth == null)
                return float.PositiveInfinity;

            return BuildingManager.Instance.DistanceToOwnedZoneEdge(transform.position, playerHealth.TeamId);
        }

        // ---------------------------------------------------------------- hit (real player + dummy)

        /// <summary>PlayerHealth.Damaged, on this player's own (victim's) client.</summary>
        private void HandleDamaged(DamageResult result, DamageInfo info)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Hit, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Attacker, info.SourceActorNumber);
            line.Int(TelemetryKeys.AttackerTeam, info.SourceTeamId);
            line.Int(TelemetryKeys.Victim, photonView.OwnerActorNr);
            line.Int(TelemetryKeys.VictimTeam, playerHealth.TeamId);
            line.Int(TelemetryKeys.Weapon, info.WeaponId);
            line.Int(TelemetryKeys.AbilityId, info.AbilityId);
            line.String(TelemetryKeys.Source, info.Source.ToString());
            line.Float(TelemetryKeys.Raw, info.Amount);
            line.Float(TelemetryKeys.ArmorAbsorbed, result.ArmorAbsorbed);
            line.Float(TelemetryKeys.HealthLost, result.HealthLost);
            line.Bool(TelemetryKeys.Lethal, result.Lethal);
            line.Float(TelemetryKeys.Distance, DistanceToAttacker(info.SourceActorNumber));
            line.Float(TelemetryKeys.Vulnerable, statusEffects != null ? statusEffects.Vulnerability : 0f);
            line.Bool(TelemetryKeys.Invulnerable, statusEffects != null && statusEffects.IsInvulnerable);
            line.Bool(TelemetryKeys.OverpowerActive, overPowerBuff != null && overPowerBuff.IsActive);
            MatchTelemetry.Instance.Log(line);
        }

        /// <summary>DummyTarget.AnyDamaged: a dummy has no owner of its own (it is not networked -
        /// see its class comment), so the only client that can ever see this event is the one whose
        /// local player actually fired the shot - the test range is where Tudor tunes weapons, so
        /// these hits matter as much as a real player's. Victim actor/team are -1: a dummy has no
        /// Photon identity to report (DummyTarget.ActorNumber is already -1 by the same convention).</summary>
        private void HandleDummyDamaged(DummyTarget dummy, DamageResult result, DamageInfo info)
        {
            if (MatchTelemetry.Instance == null || PhotonNetwork.LocalPlayer == null)
                return;

            // Only this player's own shots landing on a dummy are this client's to log - see the
            // class comment. A dummy is plain scenery with no owner guard of its own, so this is
            // the one place that check has to happen.
            if (info.SourceActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            line.Begin(TelemetryKeys.Hit, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Attacker, info.SourceActorNumber);
            line.Int(TelemetryKeys.AttackerTeam, info.SourceTeamId);
            line.Int(TelemetryKeys.Victim, -1);
            line.Int(TelemetryKeys.VictimTeam, -1);
            line.Int(TelemetryKeys.Weapon, info.WeaponId);
            line.Int(TelemetryKeys.AbilityId, info.AbilityId);
            line.String(TelemetryKeys.Source, info.Source.ToString());
            line.Float(TelemetryKeys.Raw, info.Amount);
            line.Float(TelemetryKeys.ArmorAbsorbed, result.ArmorAbsorbed);
            line.Float(TelemetryKeys.HealthLost, result.HealthLost);
            line.Bool(TelemetryKeys.Lethal, result.Lethal);
            line.Float(TelemetryKeys.Distance, Vector3.Distance(transform.position, dummy.transform.position));
            line.Float(TelemetryKeys.Vulnerable, dummy.Vulnerability);
            line.Bool(TelemetryKeys.Invulnerable, dummy.IsInvulnerable);
            line.Bool(TelemetryKeys.OverpowerActive, false); // A dummy is never OverPower's victim.
            MatchTelemetry.Instance.Log(line);
        }

        /// <summary>Distance from the attacker's own replicated position to this player (the
        /// victim) - PlayerNetSync.NetworkPosition once it has received anything from its owner,
        /// falling back to the attacker's raw transform otherwise (see HasReceivedFromOwner's own
        /// comment). PositiveInfinity - which TelemetryLine.Float writes as JSON null - when the
        /// attacker's PhotonView cannot be found at all (already left the room).</summary>
        private float DistanceToAttacker(int attackerActor)
        {
            PhotonView attackerView = PlayerLookup.GetPhotonViewFor(attackerActor);
            if (attackerView == null)
                return float.PositiveInfinity;

            PlayerNetSync netSync = attackerView.GetComponent<PlayerNetSync>();
            Vector3 attackerPosition = netSync != null && netSync.HasReceivedFromOwner
                ? netSync.NetworkPosition
                : attackerView.transform.position;

            return Vector3.Distance(attackerPosition, transform.position);
        }

        // ---------------------------------------------------------------- death / respawn

        /// <summary>PlayerHealth.Died, on this player's own (victim's) client.</summary>
        private void HandleDied(DamageInfo info)
        {
            deathTime = Time.time;

            if (MatchTelemetry.Instance == null)
                return;

            Teams.TryGetTeam(info.SourceActorNumber, out int killerTeam);

            line.Begin(TelemetryKeys.Death, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Killer, info.SourceActorNumber);
            line.Int(TelemetryKeys.KillerTeam, killerTeam);
            line.Int(TelemetryKeys.Weapon, info.WeaponId);
            line.Int(TelemetryKeys.AbilityId, info.AbilityId);
            line.Ints(TelemetryKeys.Assists, AssistArray());
            line.Float(TelemetryKeys.X, transform.position.x);
            line.Float(TelemetryKeys.Z, transform.position.z);
            line.Float(TelemetryKeys.TimeAlive, Time.time - aliveSinceTime);
            line.Int(TelemetryKeys.UnspentGold, goldWallet != null ? goldWallet.Balance : 0);
            WriteLoadout(useDeathKeys: true);
            MatchTelemetry.Instance.Log(line);

            // "on death/leave" - see FlushShots's own callers.
            FlushShots();
        }

        private int[] AssistArray()
        {
            if (combatCredit == null)
                return System.Array.Empty<int>();

            IReadOnlyList<int> assisters = combatCredit.LastDeathAssisters;
            var array = new int[assisters.Count];
            for (int i = 0; i < assisters.Count; i++)
                array[i] = assisters[i];
            return array;
        }

        private void HandleAliveChanged(bool alive)
        {
            if (!alive)
                return; // Death itself is logged from HandleDied above, off PlayerHealth.Died.

            float timeDead = Time.time - deathTime;
            aliveSinceTime = Time.time;

            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Respawn, MatchTelemetry.Instance.Now);
            line.Float(TelemetryKeys.X, transform.position.x);
            line.Float(TelemetryKeys.Z, transform.position.z);
            line.Float(TelemetryKeys.TimeDead, timeDead);
            line.Bool(TelemetryKeys.UnderAttackSpawn, lifecycle != null && lifecycle.LastRespawnWasUnderAttackSpawn);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- sample

        private void WriteSample()
        {
            if (MatchTelemetry.Instance == null)
                return;

            bool recordPositions = config == null || config.RecordPositions;
            int zone = -1;
            BuildingManager.Instance?.TryGetZoneAt(transform.position, out zone);

            line.Begin(TelemetryKeys.Sample, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Balance, goldWallet != null ? goldWallet.Balance : 0);
            if (recordPositions)
            {
                line.Float(TelemetryKeys.X, transform.position.x);
                line.Float(TelemetryKeys.Z, transform.position.z);
            }
            line.Bool(TelemetryKeys.Alive, lifecycle == null || lifecycle.IsAlive);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, playerHealth != null ? playerHealth.TeamId : -1);
            WriteLoadout(useDeathKeys: false);
            line.Float(TelemetryKeys.Health, playerHealth != null ? playerHealth.Health : 0f);
            line.Float(TelemetryKeys.Armor, playerHealth != null ? playerHealth.Armor : 0f);
            line.Float(TelemetryKeys.UltimateCharge, ultimateCharge != null ? ultimateCharge.Normalised : 0f);
            line.Float(TelemetryKeys.OverheatLevel, overheat != null ? overheat.Normalised : 0f);
            line.Int(TelemetryKeys.Ping, PhotonNetwork.GetPing());
            MatchTelemetry.Instance.Log(line);
        }

        /// <summary>Weapon/equipment/mobility/ultimate ids and both armor levels - this player's own
        /// loadout, written into whichever event is currently being built (callers Begin first).
        /// `sample` has no other use for Weapon/Equipment/Mobility/Ultimate, so it writes them
        /// directly; `death` already uses those same keys for the KILLING weapon/ability (matching
        /// `hit`'s convention), so it needs the separate Loadout* keys instead - see their own
        /// comment on TelemetryKeys.</summary>
        private void WriteLoadout(bool useDeathKeys)
        {
            int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
            int equipmentId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Equipment) : LoadoutProperties.Empty;
            int mobilityId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Mobility) : LoadoutProperties.Empty;
            int ultimateId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Ultimate) : LoadoutProperties.Empty;

            line.Int(useDeathKeys ? TelemetryKeys.LoadoutWeapon : TelemetryKeys.Weapon, weaponId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutEquipment : TelemetryKeys.Equipment, equipmentId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutMobility : TelemetryKeys.Mobility, mobilityId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutUltimate : TelemetryKeys.Ultimate, ultimateId);
            line.Int(TelemetryKeys.AbsorbLevel, playerHealth != null ? playerHealth.AbsorbLevel : 0);
            line.Int(TelemetryKeys.RechargeLevel, playerHealth != null ? playerHealth.RechargeLevel : 0);
        }
    }
}
