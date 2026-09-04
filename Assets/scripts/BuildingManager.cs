using UnityEngine;
using Project.Tools.DictionaryHelp;
using Photon.Pun;
using System.Collections.Generic;

public class BuildingManager : MonoBehaviourPun
{
    public static BuildingManager Instance { get; private set; }
   
    [SerializeField] public SerializableDictionary<int, TowerData> TowerDictionary;
    public Dictionary<int, int> CathedralBuildingIDs = new Dictionary<int, int>() { { 6, 0 }, { 7, 1 }, { 8, 2 } };
      
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

        Debug.Log($"[BuildingManager]: Building {buildingID} captured by {controllingTeam}. Value: {value}");

        towerData.isCaptured = value;
        towerData.controllingTeam = controllingTeam;

        TowerDictionary.Remove(buildingID);
        TowerDictionary.Add(buildingID, towerData);
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


