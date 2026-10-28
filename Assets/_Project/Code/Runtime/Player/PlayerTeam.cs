using UnityEngine;
using Photon.Pun;

/// <summary>
/// A read-only view of which team this player belongs to. The team lives in exactly one place:
/// this player's Photon Custom Property "teamID", so capture and friendly-fire can never disagree.
/// State, not an event (CODING-STANDARDS section 5, rules 2 and 3): late joiners get it for free.
/// </summary>
public class PlayerTeam : MonoBehaviourPun
{
    public const string TeamKey = "teamID";

    /// Returned before the property has arrived. Deliberately -1 rather than 0, which would
    /// silently claim team 0.
    public const int NoTeam = -1;

    /// Check this before acting on teamID.
    public bool HasTeam => teamID != NoTeam;

    public int teamID
    {
        get
        {
            Photon.Realtime.Player owner = photonView != null ? photonView.Owner : null;
            if (owner != null && owner.CustomProperties.TryGetValue(TeamKey, out object raw) && raw is int value)
                return value;

            return NoTeam;
        }
    }
}
