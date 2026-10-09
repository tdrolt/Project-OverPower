using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Turns victim-side damage back into credit for the attacker who dealt it. PlayerHealth.ApplyDamage
/// returns unless photonView.IsMine (every client simulates every hit, only the victim's own copy
/// keeps the result), so an attacker's client never learns how much damage it dealt or that it
/// killed. Armor recharge (PlayerHealth.NoteDealtDamage), the zip gun's cooldown reset on a takedown
/// and ultimate charge all need exactly that.
///
/// Runs entirely on the VICTIM's own client: it listens to its own PlayerHealth, accumulates what
/// each attacker took off in a DamageCreditLedger, and tells each attacker their share through a
/// targeted RPC (the same frame an isolated hit lands, or immediately on death). OnEnable returns
/// for a non-owner, so credit is counted exactly once however many clients simulated the hit.
///
/// Lives on the player ROOT beside PlayerHealth: PUN delivers an RPC only to a component on the
/// PhotonView's own GameObject, not PBRCharacter, which has its own PhotonView for its animator.
///
/// Deliberately NOT IPunObservable - PlayerNetSync is the only observable; this only RPCs.
/// </summary>
public class PlayerCombatCredit : MonoBehaviourPun
{
    [SerializeField, Tooltip("The shortest gap between two credit reports to the SAME attacker, in " +
             "seconds. An isolated hit is sent the same frame it lands, so this only ever throttles a " +
             "rapid follow-up: a second hit within this many seconds of the last report waits and " +
             "merges into the next one. Lower is more responsive at the cost of more RPCs per fight.")]
    private float creditFlushSeconds = 0.25f;

    [SerializeField, Tooltip("How long before a kill an attacker's last hit still counts toward an " +
             "assist takedown, in seconds. A hit older than this did not meaningfully contribute.")]
    private float assistWindowSeconds = 8f;

    private PlayerHealth playerHealth;
    private readonly DamageCreditLedger ledger = new DamageCreditLedger();
    private float nextFlushTime;

    /// <summary>Every assist actor from the death HandleDied just resolved, captured before
    /// ledger.Clear() wipes the last-hit times. PlayerHealth.Died reaches this handler before
    /// PlayerTelemetry's (Unity runs every OnEnable, where this subscribes, before any Start, where
    /// PlayerTelemetry subscribes), so its `death` line reads this rather than calling AssistersSince
    /// against an already-cleared ledger. Check LastDeathTime rather than trusting that ordering.</summary>
    public IReadOnlyList<int> LastDeathAssisters { get; private set; } = System.Array.Empty<int>();

    /// <summary>Time.time of the death LastDeathAssisters belongs to, -1 before any death this life.
    /// PlayerTelemetry compares it with its own captured death time before trusting the list, so a
    /// refactor moving either subscription to another lifecycle method cannot silently bring back the
    /// ordering bug.</summary>
    public float LastDeathTime { get; private set; } = -1f;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();

        // Loud: a silent null would mean nobody who shoots this player ever gets credit, with no clue why.
        if (playerHealth == null)
            Debug.LogError($"[PlayerCombatCredit] {name}: no PlayerHealth on the player root - damage credit cannot work.");
    }

    private void OnEnable()
    {
        if (playerHealth == null)
            return;

        // Only the victim's own client accumulates or sends credit. Elsewhere PlayerHealth.ApplyDamage
        // already discarded its results at the IsMine guard, so subscribing would see nothing or a
        // stale copy of health/armor and report the wrong amount.
        if (!photonView.IsMine)
            return;

        playerHealth.Damaged += HandleDamaged;
        playerHealth.Died += HandleDied;
    }

    private void OnDisable()
    {
        if (playerHealth == null)
            return;

        playerHealth.Damaged -= HandleDamaged;
        playerHealth.Died -= HandleDied;
    }

    /// <summary>LEADING-EDGE flush, in LateUpdate so every hit this frame (a shotgun's whole pellet
    /// spread) is already in the ledger. An isolated hit's credit leaves at the END OF THE SAME FRAME it
    /// landed; a follow-up within creditFlushSeconds of the last SENT report waits and merges into the
    /// next one, which is the rate cap.</summary>
    private void LateUpdate()
    {
        if (!photonView.IsMine || !ledger.HasPending || Time.time < nextFlushTime)
            return;

        nextFlushTime = Time.time + creditFlushSeconds;

        var drained = ledger.Drain();
        for (int i = 0; i < drained.Count; i++)
            SendCredit(drained[i].actor, drained[i].amount, takedown: 0, cashedMark: drained[i].cashedMark, endsShield: drained[i].endsShield);
    }

    /// <summary>Forgets every warm-up hit so none can turn into a live assist or credit. Owner only.</summary>
    public void ResetForMatchStart()
    {
        if (photonView.IsMine)
            ledger.Clear();
    }

    /// <summary>Self-damage and an unresolved source are skipped here, not in the ledger: only this
    /// player knows whose ledger it is.</summary>
    private void HandleDamaged(DamageResult result, DamageInfo info)
    {
        if (info.SourceActorNumber <= 0 || info.SourceActorNumber == photonView.OwnerActorNr)
            return;

        // Total, not HealthLost alone: armor absorbed is still damage the attacker dealt. Whether THIS hit
        // cashed a mark ORs into the attacker's ledger entry.
        // A hit from a mine, field or burn set up before the attacker's respawn is flagged so it never ends
        // the shield they got on coming back (A26); read on the victim's client from the attacker's
        // replicated dShd.
        Player attacker = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(info.SourceActorNumber) : null;
        bool endsShield = !Overpower.Dominion.RespawnShield.IsFromBeforeRespawn(attacker, info.EffectPlacedMs);
        ledger.Record(info.SourceActorNumber, result.Total, Time.time, result.Mark == MarkOutcome.Cashed, endsShield);
    }

    /// <summary>
    /// The kill/assist flush. The killer is whoever DamageInfo.SourceActorNumber names on the LETHAL
    /// hit, the value PlayerHealth.ApplyDamage used for AddScore(1), so kill credit never disagrees
    /// with the scoreboard. Every other actor who hit within the assist window gets a takedown marker
    /// too, even if their damage was already flushed: AssistersSince answers from last-hit time alone.
    /// </summary>
    private void HandleDied(DamageInfo info)
    {
        int killerActor = info.SourceActorNumber;
        var drained = ledger.Drain();
        var notified = new System.Collections.Generic.HashSet<int>();

        // Captured before ledger.Clear() below (see LastDeathAssisters).
        LastDeathAssisters = new List<int>(ledger.AssistersSince(Time.time, assistWindowSeconds, killerActor));
        LastDeathTime = Time.time;

        if (killerActor > 0)
        {
            var killerEntry = EntryFor(drained, killerActor);
            SendCredit(killerActor, killerEntry.amount, takedown: 1, cashedMark: killerEntry.cashedMark, endsShield: killerEntry.endsShield);
            notified.Add(killerActor);
        }

        foreach (int assistActor in LastDeathAssisters)
        {
            if (!notified.Add(assistActor))
                continue;

            var assistEntry = EntryFor(drained, assistActor);
            SendCredit(assistActor, assistEntry.amount, takedown: 2, cashedMark: assistEntry.cashedMark, endsShield: assistEntry.endsShield);
        }

        // Anyone who dealt damage but neither killed nor stayed within the assist window still earns
        // credit for the damage, settled now rather than waiting for LateUpdate.
        foreach (var entry in drained)
        {
            if (notified.Add(entry.actor))
                SendCredit(entry.actor, entry.amount, takedown: 0, cashedMark: entry.cashedMark, endsShield: entry.endsShield);
        }

        // Every SendCredit above read playerHealth.MarkSecondsLeftFor, which is 0 by now: PlayerHealth's
        // lethal block clears its marks BEFORE raising Died, so this flush correctly carries
        // markSecondsLeft 0 and hides every attacker's diamond.
        ledger.Clear();
    }

    private static (float amount, bool cashedMark, bool endsShield) EntryFor(
        System.Collections.Generic.IReadOnlyList<(int actor, float amount, bool cashedMark, bool endsShield)> drained, int actor)
    {
        for (int i = 0; i < drained.Count; i++)
        {
            if (drained[i].actor == actor)
                return (drained[i].amount, drained[i].cashedMark, drained[i].endsShield);
        }

        return (0f, false, false);
    }

    /// <summary>Targets exactly one attacker, so credit is seen by nobody else. A left-room actor
    /// resolves to null and is skipped. The seconds left on THIS attacker's mark ride on the same
    /// message as the damage/takedown, never a separate RPC.</summary>
    private void SendCredit(int actorNumber, float amount, byte takedown, bool cashedMark, bool endsShield)
    {
        Player attacker = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actorNumber) : null;
        if (attacker == null)
            return;

        float markSecondsLeft = playerHealth != null ? playerHealth.MarkSecondsLeftFor(actorNumber) : 0f;
        photonView.RPC(nameof(RPC_DamageCredit), attacker, amount, takedown, cashedMark, markSecondsLeft, endsShield);
    }

    /// <summary>
    /// Arrives on the ATTACKER's machine, but on the VICTIM's replicated player object: it was sent
    /// through the victim's PhotonView, merely targeted at the attacker. Inside an RPC body "local" is
    /// the RECEIVER, and the receiver is not this component's own object: reach for the attacker's OWN
    /// PlayerHealth through PlayerLookup (as RPC_HandleDeathMaster does), never `this` or `GetComponent`.
    ///
    /// info.Sender is checked against this object's owner (the victim) as an anti-spoof sanity check:
    /// only the owner of this networked object should credit damage through it.
    ///
    /// Parameters are only ever APPENDED (cashedMark, markSecondsLeft, then endsShield); the RpcList
    /// indexes method NAMES, so appending leaves it untouched. But PUN type-checks incoming arguments
    /// against the receiver's signature, logs "RPC method ... not found" on a mismatch and drops the
    /// call, so builds from before and after an append cannot exchange damage credit at all: every
    /// client build must come from the same commit. endsShield is false when every hit in the report
    /// came from a mine, field or burn set up before this attacker's respawn, so it does not end the
    /// respawn shield; the victim works that out from the attacker's dShd and DamageInfo.EffectPlacedMs
    /// (A26).
    /// </summary>
    [PunRPC]
    private void RPC_DamageCredit(float amount, byte takedown, bool cashedMark, float markSecondsLeft, bool endsShield, PhotonMessageInfo info)
    {
        if (info.Sender == null || info.Sender != photonView.Owner)
            return;

        PhotonView localView = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
        PlayerHealth localHealth = localView != null ? localView.GetComponent<PlayerHealth>() : null;

        if (amount > 0f)
        {
            localHealth?.NoteDealtDamage();
            CombatEvents.RaiseDamageDealt(amount);
            // `transform` is the VICTIM as this attacker sees it (see the RPC comment above).
            CombatEvents.RaiseHitReported(transform, amount, cashedMark);
            // Damage that landed on an enemy (A25/A26). A hit the victim's shield stopped never gets here
            // (no credit is sent); endsShield is false when every hit came from an effect set up before the respawn.
            CombatEvents.RaiseEnemyAffected(false, !endsShield);
        }

        // Always, even when amount is 0 (a death flush with no fresh damage still must hide a live
        // diamond), but after the sender check so a spoofed sender cannot clear or draw a diamond.
        CombatEvents.RaiseMarkReported(transform, markSecondsLeft);

        CombatEvents.RaisePlayerCredit(amount, takedown);

        if (takedown == 1)
            CombatEvents.RaiseTakedown(true);
        else if (takedown == 2)
            CombatEvents.RaiseTakedown(false);
    }
}
