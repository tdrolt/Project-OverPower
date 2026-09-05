using UnityEngine;
using Photon.Pun;

/// <summary>
/// A read-only view of which team this player belongs to.
///
/// The team lives in exactly one place: this player's Photon Custom Property "teamID". It used to
/// be written three separate ways -- a direct field assignment, a Custom Property, AND a buffered
/// RPC -- which meant three sources of truth for one fact, read by different systems. The capture
/// system read the field while the friendly-fire check read the property, so they could disagree
/// about which team you were on.
///
/// See CODING-STANDARDS.md section 5, rules 2 and 3: this is state, not an event, so it belongs in
/// a Custom Property rather than a buffered RPC. Late joiners get it for free.
/// </summary>
public class PlayerTeam : MonoBehaviourPun
{
    public const string TeamKey = "teamID";

    /// Returned before the property has arrived from the server. Deliberately -1 rather than 0:
    /// the old default silently claimed team 0 for a player whose team was not known yet.
    public const int NoTeam = -1;

    /// True once this player's team is actually known. Check this before acting on teamID.
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
