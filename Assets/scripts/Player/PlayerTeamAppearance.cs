using UnityEngine;

/// <summary>
/// Colours the character mesh for this player's team at runtime.
///
/// There used to be three near-identical player prefabs, one per team, spawned as
/// teamPlayerPrefabs[teamID]. They were ~5000 lines each and differed in exactly one thing: which
/// material their six SkinnedMeshRenderers used. Everything else was meant to be identical and was
/// kept so by hand.
///
/// It failed. By 2026-09-08 their dash values had silently drifted -- team 0 dashed every 2.0s,
/// teams 1 and 2 every 0.5s -- so every playtest before that date was run on an asymmetric game,
/// and nothing short of diffing three files would have shown it. Three files that must agree will
/// eventually disagree. One file cannot disagree with itself.
///
/// So there is now one prefab, and the team colour is applied here instead.
/// </summary>
public class PlayerTeamAppearance : MonoBehaviour
{
    /// <summary>
    /// Indexed by team ID: element 0 is team 0.
    ///
    /// BEWARE the asset names are 1-based and the teams are 0-based, so "PBR team 1.mat" belongs
    /// to team **0**. That off-by-one is why this is an indexed array rather than three named
    /// fields -- the index is the team, and the asset name is not to be trusted.
    ///
    /// Also note "PBR team 1.mat" is the source character's default material and is shared with
    /// PBRCharacter.prefab, so it is not exclusively ours. Do not edit it to recolour team 0;
    /// point this array at a new material instead.
    /// </summary>
    public Material[] teamMaterials = new Material[3];

    private SkinnedMeshRenderer[] meshRenderers;
    private int appliedTeam = PlayerTeam.NoTeam;

    void Awake()
    {
        // Include inactive: the mesh is disabled while the player is dead, and a player can
        // respawn into a team colour that was assigned while they were hidden.
        meshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    void Start()
    {
        Apply();
    }

    /// <summary>
    /// Safe to call repeatedly; does nothing once the correct material is already on.
    ///
    /// Must be called again when the team arrives. teamID lives in a Photon Custom Property, which
    /// is asynchronous -- on a remote player it is routinely still NoTeam at Start(). This is the
    /// same trap the name tag colour hit, fixed in 5cd73a9 ("name tag colours recompute when teams
    /// arrive"); Multiplayer.OnPlayerPropertiesUpdate drives both from one place.
    /// </summary>
    public void Apply()
    {
        if (meshRenderers == null || teamMaterials == null)
            return;

        PlayerTeam team = GetComponent<PlayerTeam>();
        int teamID = team != null ? team.teamID : PlayerTeam.NoTeam;

        // Not known yet. Leave whatever the prefab authored and wait to be called again.
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

        // sharedMaterial, not material: assigning .material clones the asset per renderer and
        // leaks a material instance for every player that spawns. Each of these renderers has
        // exactly one slot, all six carrying the team colour.
        foreach (SkinnedMeshRenderer meshRenderer in meshRenderers)
        {
            if (meshRenderer != null)
                meshRenderer.sharedMaterial = teamMaterial;
        }

        appliedTeam = teamID;
    }
}
