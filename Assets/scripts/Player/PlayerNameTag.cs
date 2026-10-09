using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The name floating above one player, coloured friend or threat. Player identity, not match state,
/// so it is not part of MatchUI and must be right from the moment the player appears.
///
/// The colour compares two Photon Custom Properties (this player's team and mine) and EITHER can
/// arrive after the object spawns. Decided once in Start, a missing value gave a wrong colour that
/// never corrected: a teammate showed red forever, or, with my own team missing too, both read as
/// "unknown", matched, and an enemy showed green forever. So it recomputes on EVERY team property
/// update, for this player and for me, and is never decided once.
///
/// Deliberately NOT IPunObservable - see PlayerNetSync.cs for why there can only be one.
/// </summary>
public class PlayerNameTag : MonoBehaviourPunCallbacks
{
    [SerializeField, Tooltip("The world-space Text above this player. Shows the owner's Photon " +
             "nickname, coloured white for yourself, green for a teammate, red for an enemy and " +
             "grey while the teams are still arriving.")]
    private Text playerNameText;

    // The mesh colour has the same late-arriving-team problem, so one place drives both.
    private PlayerTeamAppearance teamAppearance;

    // Awake, not Start: MonoBehaviourPunCallbacks registers as a callback target in OnEnable, before
    // Start, so a team property arriving in that window would find this null and skip the recolour.
    private void Awake() => teamAppearance = GetComponent<PlayerTeamAppearance>();

    private void Start()
    {
        // A missing reference is invisible at runtime: the tag simply never appears.
        if (playerNameText == null)
        {
            Debug.LogError($"[PlayerNameTag] {name}: playerNameText is not assigned - this player " +
                            "will have no name tag and no friend-or-enemy colour.");
            return;
        }

        playerNameText.text = photonView.Owner != null ? photonView.Owner.NickName : string.Empty;
        // Second guard beside removing the overhead canvas's GraphicRaycaster (PlayerHealth.ApplyTheme):
        // a raycast-target name tag would make hovering ANY player's name count as "over UI" to
        // PlayerInputRouter and swallow a shot.
        playerNameText.raycastTarget = false;

        UpdateNameTagColour();
        teamAppearance?.Apply();   // no-op if the team has not arrived yet
    }

    /// Recomputes whenever EITHER team becomes known. An update can arrive before Start, so every
    /// read below is null-guarded.
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (!changedProps.ContainsKey(PlayerTeam.TeamKey))
            return;

        // Compares THIS player's team against MY team, so either arriving matters.
        if (targetPlayer != photonView.Owner && targetPlayer != PhotonNetwork.LocalPlayer)
            return;

        UpdateNameTagColour();

        // One prefab serves all three teams: without this every player wears team 0's colour.
        teamAppearance?.Apply();
    }

    /// Grey while the answer is not known yet, rather than guessing: a briefly neutral tag beats a
    /// confidently wrong one.
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
