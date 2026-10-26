using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Guards the trap that silently switched off all player replication from 32e2b1a until
    /// 2026-09-15: a PhotonView's Observable Search setting does NOT run for an object spawned with
    /// PhotonNetwork.Instantiate. PhotonView.Awake returns early when ViewID is already set, and
    /// PhotonNetwork.Instantiate sets it before Awake - so only the ObservedComponents list SAVED in
    /// the prefab is ever used. That list is only rewritten when someone views the PhotonView in the
    /// Inspector. The player prefab kept a dead reference to the deleted Multiplayer component, so
    /// PlayerNetSync never sent position, rotation, health or armor to anyone.
    /// </summary>
    public class NetworkPrefabObservablesTests
    {
        [Test]
        public void EveryNetworkPrefabPhotonView_SavesTheObservablesItsSearchWouldFind()
        {
            var problems = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (PhotonView view in prefab.GetComponentsInChildren<PhotonView>(true))
                {
                    if (view.observableSearch == PhotonView.ObservableSearch.Manual)
                        continue;

                    // The exact search PhotonView.FindObservables runs, read-only on the asset.
                    var found = new List<Component>();
                    view.transform.GetNestedComponentsInChildren<Component, IPunObservable, PhotonView>(
                        view.observableSearch == PhotonView.ObservableSearch.AutoFindAll, found);

                    var saved = (view.ObservedComponents ?? new List<Component>()).Where(c => c != null).ToList();

                    if (!new HashSet<Component>(found).SetEquals(saved))
                    {
                        problems.Add($"{path} '{view.name}': saved [{string.Join(", ", saved.Select(c => c.GetType().Name))}] " +
                                     $"but its search finds [{string.Join(", ", found.Select(c => c.GetType().Name))}]");
                    }
                }
            }

            Assert.IsEmpty(problems,
                "A network-spawned PhotonView only ever uses its SAVED Observed Components list - select it in " +
                "the Inspector (which refreshes the list) and save the prefab:\n" + string.Join("\n", problems));
        }
    }
}
