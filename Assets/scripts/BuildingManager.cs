using UnityEngine;
using Project.Tools.DictionaryHelp;
using Photon.Pun;
using System.Collections.Generic;

public class BuildingManager : MonoBehaviourPun
{
    public static BuildingManager Instance { get; private set; }
   
    [SerializeField] public SerializableDictionary<int, TowerData> TowerDictionary;
    public Dictionary<int, int> CathedralBuildingIDs = new Dictionary<int, int>() { { 6, 0 }, { 7, 1 }, { 8, 2 } };

    // Towers register themselves here so ownership changes can drive their flag colour.
    // TowerData.Building exists for this but is null on all nine towers in the scene, and a new
    // tower (the planned Tier-4 centre) would need remembering to wire up by hand. Registering
    // is one line in BuildingCapture.Start and cannot be forgotten.
    private readonly Dictionary<int, BuildingCapture> captures = new Dictionary<int, BuildingCapture>();

    public void RegisterCapture(int buildingID, BuildingCapture capture)
    {
        captures[buildingID] = capture;
    }
      
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        } else
        {
            Destroy(gameObject);
        }
    }

    public void UpdateTowerDictionary(bool value, int controllingTeam, int buildingID)
    {
        // AllBuffered, not All: a player joining mid-match must receive every ownership change
        // that already happened, otherwise their TowerDictionary only has the scene defaults and
        // the adjacency gate in BuildingCapture.OnTriggerEnter refuses to let them capture
        // anything next to a tower their team took before they joined.
        //
        // Buffered RPCs accumulate for the room's lifetime. With 9 towers in a prototype match
        // that is fine; if capture churn ever gets high, replicate ownership through Room Custom
        // Properties instead of a buffered call per change.
        photonView.RPC("RPC_UpdateTowerDictionary", RpcTarget.AllBuffered, value, controllingTeam, buildingID);
    }

    [PunRPC]
    private void RPC_UpdateTowerDictionary(bool value, int controllingTeam, int buildingID)
    {
        TowerData towerData = TowerDictionary[buildingID];

        // Once per ownership change, and replayed once per tower for a late joiner. That replay
        // is itself the signal that buffered ownership reached them.
        Debug.Log($"[TOWER] {buildingID} -> {(value ? $"team {controllingTeam}" : "neutral")}");

        towerData.isCaptured = value;
        towerData.controllingTeam = controllingTeam;

        TowerDictionary.Remove(buildingID);
        TowerDictionary.Add(buildingID, towerData);

        // The flag is presentation derived from this state, not a separate message. Driving it
        // from here means a late joiner replaying the buffered ownership call also gets the
        // right flag colour, instead of ownership being correct while the map looks wrong.
        if (captures.TryGetValue(buildingID, out BuildingCapture capture) && capture != null)
            capture.ApplyOwnerVisual(value, controllingTeam);
    }
}

[System.Serializable]
public struct TowerData
{
    public GameObject Building;
    public bool isCaptured;
    public int controllingTeam;
    public List<int> Adjacents;
}


