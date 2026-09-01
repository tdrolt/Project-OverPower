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
        photonView.RPC("RPC_UpdateTowerDictionary", RpcTarget.All, value, controllingTeam, buildingID);
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


