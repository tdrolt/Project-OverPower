using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

/// <summary>
/// Colours the character mesh for this player's team at runtime. There is one player prefab, not one
/// per team: per-team prefabs differed only in the material of six SkinnedMeshRenderers, yet their
/// stats drifted apart silently. Three files that must agree eventually disagree.
/// </summary>
public class PlayerTeamAppearance : MonoBehaviour
{
    private SkinnedMeshRenderer[] meshRenderers;
    private int appliedTeam = PlayerTeam.NoTeam;
    private bool warnedNoLook;

    void Start()
    {
        Apply();
    }

    /// <summary>
    /// Safe to call repeatedly.
    ///
    /// Must be called again when the team arrives: teamID lives in a Photon Custom Property, which is
    /// asynchronous, so on a remote player it is routinely still NoTeam at Start().
    /// PlayerNameTag.OnPlayerPropertiesUpdate drives this and the name tag colour from one place.
    /// </summary>
    public void Apply()
    {
        PlayerTeam team = GetComponent<PlayerTeam>();
        PlayerHealth health = GetComponent<PlayerHealth>();
        ApplyTeam(team != null ? team.teamID : PlayerTeam.NoTeam, health != null ? health.Theme : null);
    }

    internal void ApplyTeam(int teamID, UiTheme theme)
    {
        // Team not known yet: keep the prefab's look and wait to be called again.
        if (teamID < 0 || teamID == appliedTeam)
            return;

        Material teamMaterial = MechMaterials.For(teamID, theme, theme != null ? theme.arenaLook : null);
        if (teamMaterial == null)
        {
            if (!warnedNoLook)
                Debug.LogError($"[PlayerTeamAppearance] {name}: no mech material for team {teamID} - the UiTheme needs an Arena Look with a Mech Template.");
            warnedNoLook = true;
            return;
        }

        // Include inactive: the mesh is disabled while dead, and a respawn can land on a team colour
        // assigned while hidden.
        if (meshRenderers == null)
            meshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // sharedMaterial, not material: .material clones the asset per renderer and leaks an
        // instance for every player that spawns. Each renderer has exactly one slot.
        foreach (SkinnedMeshRenderer meshRenderer in meshRenderers)
        {
            if (meshRenderer != null)
                meshRenderer.sharedMaterial = teamMaterial;
        }

        appliedTeam = teamID;
    }
}
