using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Turns victim-side damage back into credit for the attacker who dealt it - the piece the damage
/// funnel is missing. PlayerHealth.ApplyDamage returns immediately unless photonView.IsMine (every
/// client simulates every hit and only the victim's own copy keeps the result), so an attacker's
/// own client never learns how much damage it dealt, or that it landed a kill. Three things need
/// exactly that and have no way to get it today: armor recharge (PlayerHealth.NoteDealtDamage has
/// no callers), the zip gun's cooldown reset on a takedown (Task 1.7), and ultimate charge
/// (Task 1.11).
///
/// Runs entirely from the VICTIM's own client: it listens to its own PlayerHealth, accumulates how
/// much each attacker actually took off in a DamageCreditLedger, and tells each attacker their share
/// through a targeted RPC - the same frame an isolated hit lands, or immediately on death. Only the
/// victim's owner ever runs this (OnEnable below returns for a non-owner), so credit is counted
/// exactly once no matter how many clients simulated the hit.
///
/// Lives on the player ROOT, beside PlayerHealth: PUN only ever delivers an RPC to a component on
/// the PhotonView's own GameObject, which is the root - not PBRCharacter, which has its own
/// PhotonView for its animator.
///
/// Deliberately NOT IPunObservable - PlayerNetSync is the player's only observable (see its class
/// comment); nothing here needs to serialize, only to RPC.
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

    /// <summary>Task T3 (telemetry): every assist actor from the death HandleDied just resolved,
    /// captured here before ledger.Clear() wipes the ledger's own last-hit times. PlayerHealth.Died
    /// always reaches this component's own handler before PlayerTelemetry's (Unity runs every
    /// OnEnable, where this subscribes, before any Start, where PlayerTelemetry subscribes - see
    /// its own class comment), so PlayerTelemetry's `death` line reads this rather than calling
    /// AssistersSince itself against an already-cleared ledger.
    ///
    /// T3 review: that ordering guarantee is real, but a reader should not have to trust it blindly -
    /// see LastDeathTime below for the freshness guard a caller can check instead of assuming.</summary>
    public IReadOnlyList<int> LastDeathAssisters { get; private set; } = System.Array.Empty<int>();

    /// <summary>Task T3 review: Time.time of the death LastDeathAssisters belongs to, -1 before any
    /// death this life. PlayerTelemetry compares this against its own captured death time before
    /// trusting LastDeathAssisters, rather than relying purely on subscriber ordering between two
    /// separate components - a guard against a future refactor (either component's subscription
    /// moving from OnEnable/Start to some other lifecycle method) silently reintroducing the ordering
    /// bug LastDeathAssisters was built to avoid in the first place.</summary>
    public float LastDeathTime { get; private set; } = -1f;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();

        // Loud, matching PlayerHealth/WeaponFiring: a silent null here would mean nobody who shoots
        // this player ever gets credit for it, with no clue in the console why.
        if (playerHealth == null)
            Debug.LogError($"[PlayerCombatCredit] {name}: no PlayerHealth on the player root - damage credit cannot work.");
    }

    private void OnEnable()
    {
        if (playerHealth == null)
            return;

        // Only the victim's own client ever accumulates or sends credit - see the class comment.
        // Every other client simulated the same hits and PlayerHealth.ApplyDamage already threw
        // its results away at the IsMine guard, so subscribing there too would either see nothing
        // or, worse, see a stale copy of health/armor and report the wrong amount.
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

    /// <summary>Mark plan step 2, Decision 11: the flush is now LEADING-EDGE, and moved to LateUpdate
    /// so every hit this frame (a shotgun's whole pellet spread included) is already in the ledger
    /// before this runs. `ledger.HasPending` is the change from before: an isolated hit's credit now
    /// leaves at the END OF THE SAME FRAME it landed, where the old fixed 0.25s tick could add up to
    /// 0.25s of pure latency for a hit that happened to land right after the tick had just fired. A
    /// follow-up hit within creditFlushSeconds of the last SENT report still waits and merges into the
    /// next one - same rate cap, same totals, only the isolated case got faster. Ultimate charge and
    /// armour recharge just hear sooner; nothing about what they hear changed.</summary>
    private void LateUpdate()
    {
        if (!photonView.IsMine || !ledger.HasPending || Time.time < nextFlushTime)
            return;

        nextFlushTime = Time.time + creditFlushSeconds;

        var drained = ledger.Drain();
        for (int i = 0; i < drained.Count; i++)
            SendCredit(drained[i].actor, drained[i].amount, takedown: 0, cashedMark: drained[i].cashedMark);
    }

    /// <summary>Records one hit. Self-damage and an unresolved source are skipped here rather than
    /// in the ledger - DamageCreditLedger has no notion of whose ledger it is, only PlayerHealth's
    /// owner (this player) knows that a source actor matching its own is a self-hit.</summary>
    private void HandleDamaged(DamageResult result, DamageInfo info)
    {
        if (info.SourceActorNumber <= 0 || info.SourceActorNumber == photonView.OwnerActorNr)
            return;

        // Total, not HealthLost alone: armor absorbed is still damage the attacker actually dealt,
        // the same figure PlayerHealth's own UpdateOverheadBar and DamageResolver's callers use.
        // Mark plan step 4: whether THIS hit cashed a mark ORs into the attacker's own ledger entry.
        ledger.Record(info.SourceActorNumber, result.Total, Time.time, result.Mark == MarkOutcome.Cashed);
    }

    /// <summary>
    /// The kill/assist flush. The killer is whoever DamageInfo.SourceActorNumber names on the
    /// LETHAL hit - the exact same value PlayerHealth.ApplyDamage already used for
    /// sourcePlayer?.AddScore(1) a moment earlier, so kill credit here can never disagree with the
    /// scoreboard's. Every other actor who hit this player within the assist window gets a takedown
    /// marker too, even if their damage was already flushed out by an earlier LateUpdate flush -
    /// AssistersSince answers from last-hit time alone, independent of what has been paid out.
    /// </summary>
    private void HandleDied(DamageInfo info)
    {
        int killerActor = info.SourceActorNumber;
        var drained = ledger.Drain();
        var notified = new System.Collections.Generic.HashSet<int>();

        // Task T3: captured before ledger.Clear() below - see LastDeathAssisters's own comment.
        LastDeathAssisters = new List<int>(ledger.AssistersSince(Time.time, assistWindowSeconds, killerActor));
        LastDeathTime = Time.time;

        if (killerActor > 0)
        {
            var killerEntry = EntryFor(drained, killerActor);
            SendCredit(killerActor, killerEntry.amount, takedown: 1, cashedMark: killerEntry.cashedMark);
            notified.Add(killerActor);
        }

        foreach (int assistActor in LastDeathAssisters)
        {
            if (!notified.Add(assistActor))
                continue;

            var assistEntry = EntryFor(drained, assistActor);
            SendCredit(assistActor, assistEntry.amount, takedown: 2, cashedMark: assistEntry.cashedMark);
        }

        // Anyone who dealt damage this fight but neither landed the kill nor stayed within the
        // assist window still earns the credit for the damage itself - the ordinary flush's own
        // contract, just settled immediately rather than waiting for LateUpdate to notice it.
        foreach (var entry in drained)
        {
            if (notified.Add(entry.actor))
                SendCredit(entry.actor, entry.amount, takedown: 0, cashedMark: entry.cashedMark);
        }

        // Mark plan step 4: every SendCredit call above already read playerHealth.MarkSecondsLeftFor,
        // which is 0 for everyone by now - PlayerHealth's own lethal block clears its marks BEFORE
        // raising Died (see that method's own comment), so this death flush's messages correctly
        // carry markSecondsLeft 0, hiding every attacker's diamond, without anything special here.
        ledger.Clear();
    }

    private static (float amount, bool cashedMark) EntryFor(
        System.Collections.Generic.IReadOnlyList<(int actor, float amount, bool cashedMark)> drained, int actor)
    {
        for (int i = 0; i < drained.Count; i++)
        {
            if (drained[i].actor == actor)
                return (drained[i].amount, drained[i].cashedMark);
        }

        return (0f, false);
    }

    /// <summary>Targets exactly one attacker, so credit is never seen by anyone but the player it
    /// belongs to. A left-room actor number resolves to null and is simply skipped - nobody is left
    /// to credit. Mark plan step 4: also reads this victim's own MarkLedger for how many seconds
    /// THIS attacker's mark (if any) still has left, so the attacker's diamond (mark step 5) always
    /// rides on the same message as the damage/takedown it goes with, never a separate RPC.</summary>
    private void SendCredit(int actorNumber, float amount, byte takedown, bool cashedMark)
    {
        Player attacker = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actorNumber) : null;
        if (attacker == null)
            return;

        float markSecondsLeft = playerHealth != null ? playerHealth.MarkSecondsLeftFor(actorNumber) : 0f;
        photonView.RPC(nameof(RPC_DamageCredit), attacker, amount, takedown, cashedMark, markSecondsLeft);
    }

    /// <summary>
    /// Arrives on the ATTACKER's machine, but on the VICTIM's replicated player object - this RPC
    /// was sent through the victim's own PhotonView, merely targeted at the attacker. Inside an RPC
    /// body "local" is the RECEIVER, and the receiver here is not this component's own object: it
    /// must reach for the attacker's OWN PlayerHealth through PlayerLookup (the same lookup
    /// RPC_HandleDeathMaster already uses to find a specific actor's object), never `this` or
    /// `GetComponent` on the object the RPC body is running on.
    ///
    /// info.Sender is checked against this object's owner (the victim) as a cheap anti-spoof
    /// sanity check: only the victim who actually owns this networked object should ever be the
    /// one crediting damage through it.
    ///
    /// Mark plan step 4 (the one RPC signature change in this whole plan): cashedMark and
    /// markSecondsLeft are APPENDED after the existing two parameters, same name, same [PunRPC]
    /// count - appending parameters does not touch the RpcList (it indexes method NAMES, not
    /// signatures, same as WeaponFiring's own appended-parameters note on RPC_FireWeapon), but every
    /// client build must be running this exact signature from this commit on, or the extra
    /// parameters silently misalign.
    /// </summary>
    [PunRPC]
    private void RPC_DamageCredit(float amount, byte takedown, bool cashedMark, float markSecondsLeft, PhotonMessageInfo info)
    {
        if (info.Sender == null || info.Sender != photonView.Owner)
            return;

        PhotonView localView = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
        PlayerHealth localHealth = localView != null ? localView.GetComponent<PlayerHealth>() : null;

        if (amount > 0f)
        {
            localHealth?.NoteDealtDamage();
            CombatEvents.RaiseDamageDealt(amount);
            // `transform` here is the VICTIM as this attacker sees it - this RPC runs on the victim's
            // own replicated object, merely targeted at the attacker (the class comment above).
            CombatEvents.RaiseHitReported(transform, amount, cashedMark);
        }

        // Always, even when amount is 0 (a death flush that carries no fresh damage still needs to
        // hide a live diamond) - after the sender check above, never before it, so a spoofed sender
        // can never clear or draw a diamond on a machine it does not own the RPC's own victim on.
        CombatEvents.RaiseMarkReported(transform, markSecondsLeft);

        if (takedown == 1)
            CombatEvents.RaiseTakedown(true);
        else if (takedown == 2)
            CombatEvents.RaiseTakedown(false);
    }
}
