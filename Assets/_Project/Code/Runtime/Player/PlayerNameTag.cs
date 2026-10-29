using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The name on the plate above one player (NameplateView), coloured friend or threat, with the plate's edge in the player's team
/// colour. Player identity, not match state, so it is not part of MatchUi and must be right from the moment the player appears.
///
/// The colours compare two Photon Custom Properties (this player's team and mine) and EITHER can
/// arrive after the object spawns. Decided once in Start, a missing value gave a wrong colour that
/// never corrected: a teammate showed red forever, or, with my own team missing too, both read as
/// "unknown", matched, and an enemy showed green forever. So it recomputes on EVERY team property
/// update, for this player and for me, and is never decided once.
///
/// Deliberately NOT IPunObservable - see PlayerNetSync.cs for why there can only be one.
/// </summary>
public class PlayerNameTag : MonoBehaviourPunCallbacks
{
    // The mesh colour has the same late-arriving-team problem, so one place drives both.
    private PlayerTeamAppearance teamAppearance;
    private PlayerHealth health;
    private NameplateView plate;

    // Awake, not Start: MonoBehaviourPunCallbacks registers as a callback target in OnEnable, before
    // Start, so a team property arriving in that window would find this null and skip the recolour.
    private void Awake()
    {
        teamAppearance = GetComponent<PlayerTeamAppearance>();
        health = GetComponent<PlayerHealth>();
    }

    private void Start()
    {
        plate = NameplateView.Ensure(health);
        // A missing plate is invisible at runtime: the name simply never appears.
        if (plate == null)
            Debug.LogError($"[PlayerNameTag] {name}: no plate - the player needs a PlayerHealth with a UiTheme, an overhead " +
                            "canvas and its bar, or they will have no name and no friend-or-enemy colour.");
        else
        {
            plate.SetOwnPlate(NameplateRules.ShowsOwnBars(photonView.IsMine));
            plate.SetName(photonView.Owner != null ? photonView.Owner.NickName : string.Empty);
        }

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

        // One prefab serves all three teams: without this every player keeps the template's neutral look.
        teamAppearance?.Apply();
    }

    /// Grey while the answer is not known yet, rather than guessing: a briefly neutral name beats a
    /// confidently wrong one.
    private void UpdateNameTagColour()
    {
        if (plate == null || health == null || health.Theme == null)
            return;

        PlayerTeam team = GetComponent<PlayerTeam>();
        int theirTeam = team != null && team.HasTeam ? team.teamID : PlayerTeam.NoTeam;
        int localTeam = PlayerTeam.NoTeam;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerTeam.TeamKey, out object raw)
            && raw is int value)
        {
            localTeam = value;
        }

        UiTheme theme = health.Theme;
        Color nameColour = NameplateRules.NameColour(photonView.IsMine, localTeam, theirTeam, theme.nameplateSelfNameColor,
            theme.nameplateTeammateNameColor, theme.nameplateEnemyNameColor, theme.nameplateUnknownNameColor);
        plate.SetColours(nameColour, NameplateRules.EdgeColour(theirTeam, theme));
    }
}
