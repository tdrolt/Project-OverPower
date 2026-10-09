using UnityEngine;

/// <summary>
/// Colours the character mesh for this player's team at runtime. There is one player prefab, not one
/// per team: per-team prefabs differed only in the material of six SkinnedMeshRenderers, yet their
/// stats drifted apart silently. Three files that must agree eventually disagree.
/// </summary>
public class PlayerTeamAppearance : MonoBehaviour
{
    /// <summary>
    /// Indexed by team ID: element 0 is team 0.
    ///
    /// BEWARE the asset names are 1-based and the teams 0-based: "PBR team 1.mat" belongs to team **0**.
    /// The index is the team; do not trust the asset name.
    ///
    /// "PBR team 1.mat" is also the source character's default material, shared with
    /// PBRCharacter.prefab. Do not edit it to recolour team 0; point this array at a new material.
    /// </summary>
    public Material[] teamMaterials = new Material[3];

    private SkinnedMeshRenderer[] meshRenderers;
    private int appliedTeam = PlayerTeam.NoTeam;

    void Awake()
    {
        // Include inactive: the mesh is disabled while dead, and a respawn can land on a team colour
        // assigned while hidden.
        meshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

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
        if (meshRenderers == null || teamMaterials == null)
            return;

        PlayerTeam team = GetComponent<PlayerTeam>();
        int teamID = team != null ? team.teamID : PlayerTeam.NoTeam;

        // Team not known yet: keep the prefab's look and wait to be called again.
        if (teamID < 0 || teamID >= teamMaterials.Length)
            return;

        if (teamID == appliedTeam)
            return;

        Material teamMaterial = teamMaterials[teamID];
        if (teamMaterial == null)
        {
            Debug.LogWarning($"[PlayerTeamAppearance] No material assigned for team {teamID}.");
            return;
        }

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
