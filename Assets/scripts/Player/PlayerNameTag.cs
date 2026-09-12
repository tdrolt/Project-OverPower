using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The name floating above one player, and the colour that says whether they are a friend or a
/// threat. Split out of Multiplayer.cs (Task 0.11b) alongside PlayerLifecycle and MatchUI.
///
/// Its own component rather than part of MatchUI because this is player identity, not match state:
/// it answers "who is that" at a glance and has to be right from the moment the player appears,
/// whereas MatchUI only ever says something at a death or at the end of the match.
///
/// The reason it is worth a file of its own is the bug it exists to prevent. The colour is a
/// comparison between two Photon Custom Properties - this player's team and mine - and EITHER can
/// arrive after the object spawns. It used to be decided once in Start, so whichever value was
/// missing at that instant produced a wrong colour that never corrected itself: a teammate whose
/// team had not arrived yet showed red forever, and if my own team was missing too, both read as
/// "unknown" and therefore matched, so an enemy showed green forever. Telling friend from enemy is
/// the single most important thing on screen in a 3v3v3 fight.
///
/// That got worse, not better, when team assignment moved from a buffered RPC to a Custom Property:
/// a value that used to effectively arrive before spawn now genuinely arrives after it. So this
/// recomputes on EVERY team property update, for this player and for me, and is never decided once.
///
/// Deliberately NOT IPunObservable - see PlayerNetSync.cs for why there can only be one.
/// </summary>
public class PlayerNameTag : MonoBehaviourPunCallbacks
{
    [SerializeField, Tooltip("The world-space Text above this player. Shows the owner's Photon " +
             "nickname, coloured white for yourself, green for a teammate, red for an enemy and " +
             "grey while the teams are still arriving.")]
    private Text playerNameText;

    // The mesh colour has exactly the same late-arriving-team problem as the name tag, so one
    // place reacts to the team property and drives both. See PlayerTeamAppearance.cs.
    private PlayerTeamAppearance teamAppearance;

    // Cached in Awake, not Start: MonoBehaviourPunCallbacks registers this as a callback target in
    // OnEnable, which runs before Start, so a team property arriving in that window would otherwise
    // find this reference still null and silently skip the mesh recolour.
    private void Awake() => teamAppearance = GetComponent<PlayerTeamAppearance>();

    private void Start()
    {
        // A missing reference here is invisible at runtime - the tag simply never appears - and
        // this component's predecessor was attached to nothing at all for a while, so nothing
        // complained then either.
        if (playerNameText == null)
        {
            Debug.LogError($"[PlayerNameTag] {name}: playerNameText is not assigned - this player " +
                            "will have no name tag and no friend-or-enemy colour.");
            return;
        }

        playerNameText.text = photonView.Owner != null ? photonView.Owner.NickName : string.Empty;

        UpdateNameTagColour();
        teamAppearance?.Apply();   // no-op if the team has not arrived yet
    }

    /// Recomputes whenever EITHER team becomes known - see the class comment. Registered by
    /// MonoBehaviourPunCallbacks, which adds this as a callback target in OnEnable, so an update
    /// arriving before Start is handled too (every read below is null-guarded for that reason).
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (!changedProps.ContainsKey(PlayerTeam.TeamKey))
            return;

        // The colour compares THIS player's team against MY team, so it has to react to either one
        // arriving, not just this player's.
        if (targetPlayer != photonView.Owner && targetPlayer != PhotonNetwork.LocalPlayer)
            return;

        UpdateNameTagColour();

        // One prefab is now shared by all three teams, so without this every player wears team 0's
        // colour until something else happens to call Apply.
        teamAppearance?.Apply();
    }

    /// Green for a teammate, red for an enemy, white for yourself, grey while the answer is not
    /// known yet.
    ///
    /// Grey rather than guessing: a briefly neutral tag is better than a confidently wrong one, and
    /// it resolves within a moment. Guessing is what the old version did, and it guessed
    /// permanently.
    private void UpdateNameTagColour()
    {
        if (playerNameText == null)
            return;

        if (photonView.IsMine)
        {
            playerNameText.color = Color.white;
            return;
        }

        PlayerTeam team = GetComponent<PlayerTeam>();
        int localTeam = PlayerTeam.NoTeam;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out object raw)
            && raw is int value)
        {
            localTeam = value;
        }

        if (team == null || !team.HasTeam || localTeam == PlayerTeam.NoTeam)
        {
            playerNameText.color = Color.grey;
            return;
        }

        playerNameText.color = team.teamID == localTeam ? Color.green : Color.red;
    }
}
