using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using System.Linq;

public class BuildingCapture : MonoBehaviourPun
{
    public int buildingID;

    [Header("Capture Settings")]
    public float captureThreshold = 100f;
    public float baseCaptureRate = 20f;
    public float captureRadius = 10f;
    // Seconds to drain a fully captured tower back to neutral once an undefended enemy holds it.
    // Replaces the old 'decayRate' field, which nothing read: the decay was hardcoded as
    // captureThreshold / 5f, so changing decayRate in the Inspector did nothing at all.
    // 5 here reproduces the previous behaviour exactly.
    public float decaySeconds = 5f;

    // Seconds a neutralised tower cannot be recaptured. Was hardcoded in CooldownRoutine.
    public float recaptureCooldownSeconds = 5f;

    [Header("Visual Settings")]
    public Renderer flagRenderer;
    public Material neutralMaterial;
    public Material team0Material;
    public Material team1Material;
    public Material team2Material;

    [Header("Audio Settings")]
    public AudioClip capturingSound;
    public AudioClip capturedSound;
    private AudioSource audioSource;

    [Header("Building Capture ID")]
    [Tooltip("This will be set dynamically to the team id of the first eligible player.")]
    public int capturingID = -1; // -1 means 'unset'

    private int controllingTeam = -1;
    private float captureProgress = 0f;
    private bool isCaptured = false;
    private bool isDecaying = false;
    private bool isOnCooldown = false;

    private List<PlayerTeam> playersInZone = new List<PlayerTeam>();

    void Start()
    {
        BuildingManager.Instance.RegisterCapture(buildingID, this);

        ConfigureCollider();
        InitializeAudio();
        if (BuildingManager.Instance.CathedralBuildingIDs.ContainsKey(buildingID))
        {
            int owner = BuildingManager.Instance.CathedralBuildingIDs[buildingID];
            capturingID = owner;
            controllingTeam = owner;
            isCaptured = true;
            captureProgress = captureThreshold;
        }
        else
            ResetFlag();

        // A buffered ownership call can arrive before this component existed to be notified, so
        // read the current replicated state once here rather than relying only on being told.
        if (BuildingManager.Instance.TowerDictionary.ContainsKey(buildingID))
        {
            TowerData current = BuildingManager.Instance.TowerDictionary[buildingID];
            ApplyOwnerVisual(current.isCaptured, current.controllingTeam);
        }

        Debug.Log($"[BuildingCapture] Building ready with capturingID {capturingID}. Waiting for rightful team to show up.");
    }

    void ConfigureCollider()
    {
        var collider = GetComponent<SphereCollider>();
        if (collider)
        {
            collider.radius = captureRadius;
        }
        else
        {
            Debug.LogWarning("[BuildingCapture] No SphereCollider found on the building!");
        }
    }

    void InitializeAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (!audioSource)
            Debug.LogWarning("[BuildingCapture] Missing AudioSource component!");
    }

    void ResetFlag()
    {
        if (flagRenderer && neutralMaterial)
        {
            flagRenderer.material = neutralMaterial;
        }
        else
        {
            Debug.LogWarning("[BuildingCapture] Missing flagRenderer or neutralMaterial!");
        }
    }

    void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // A player who died or disconnected while standing in the ring leaves a destroyed
        // reference behind: OnTriggerExit cannot fire for an object that no longer exists.
        // Every consumer below reads p.teamID, so one stale entry throws a
        // MissingReferenceException every frame and the capture system stops working for the
        // rest of the match. Unity's == treats a destroyed object as null, so this catches both
        // the destroyed and the disconnected case.
        playersInZone.RemoveAll(p => p == null);

        if (isCaptured)
        {
            HandleCapturedState(); // handles recapture decay if an enemy is present
            return;
        }

        if (isOnCooldown) return;

        CalculateCaptureProgress();
    }


    // NEW: Modified to handle recapture decay if enemy enters
    void HandleCapturedState()
    {
        bool enemyPresent = playersInZone.Any(p => p.teamID != controllingTeam);
        bool teamMemberPresent = playersInZone.Any(p => p.teamID == controllingTeam);

        if (enemyPresent && !teamMemberPresent)
        {
            if (!isDecaying)
            {
                isDecaying = true;
                captureProgress = captureThreshold;
                capturingID = playersInZone.First(p => p.teamID != controllingTeam).teamID;
                photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, capturingID);
                Debug.Log("[HandleCapturedState] Enemy detected. Starting recapture decay.");

                // Play recapture sound when an enemy starts recapturing
                PlayRecaptureSound();  // This was missing from your decay logic
            }

            // Stop the capturing sound if decaying
            StopCapturingSound();
        }
        else
        {
            isDecaying = false;
        }

        if (isDecaying)
        {
            UpdateDecay();

            if (captureProgress <= 0)
            {
                StopCapturingSound(); // Ensure sound stops if neutralized
                NeutralizeBuilding();
            }
        }
    }


    void StartDecay()
    {
        isDecaying = true;
        captureProgress = captureThreshold;
        // Decay started � progress resets to threshold.
    }

    void UpdateDecay()
    {
        float seconds = Mathf.Max(0.01f, decaySeconds);
        captureProgress -= (captureThreshold / seconds) * Time.deltaTime;
    }

    void NeutralizeBuilding()
    {
        // REMOVING THE CAPTURED BUILDING FROM TEAM DATA
        UpdateBuildingManager(false);

        controllingTeam = -1;
        isCaptured = false;
        isDecaying = false;
        // No RPC_UpdateFlag here: UpdateBuildingManager(false) above already replicated the
        // ownership change, and the flag follows that.
        PlayNeutralizationSound();
        StartCoroutine(CooldownRoutine());
    }

    IEnumerator CooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(recaptureCooldownSeconds);
        isOnCooldown = false;
    }

    void CalculateCaptureProgress()
    {
        if (capturingID == -1 && playersInZone.Count != 0)
        {
            capturingID = playersInZone[0].teamID;
            captureProgress = 0;
        }

        var eligiblePlayers = playersInZone.Where(p => p.teamID == capturingID).ToList();
        var enemyPlayers = playersInZone.Any(p => p.teamID != capturingID);

        if (eligiblePlayers.Any() && !enemyPlayers)
        {
            int count = eligiblePlayers.Count;
            float contribution = count * baseCaptureRate * Time.deltaTime;
            captureProgress += contribution;
            captureProgress = Mathf.Clamp(captureProgress, 0, captureThreshold);

            // Start the capturing sound only if progress is increasing
            if (!audioSource.isPlaying && captureProgress > 0 && captureProgress < captureThreshold)
            {
                PlayCapturingSound();
            }

            // Stop capturing sound and complete capture if progress reaches the threshold
            if (captureProgress >= captureThreshold)
            {
                StopCapturingSound();
                CompleteCapture(capturingID);
            }
        }
        else
        {


            // Stop the capturing sound if no eligible players are capturing
            if (audioSource.isPlaying)
            {
                StopCapturingSound();
            }
        }
    }

    void PlayCapturingSound()
    {
        // Play sound if it's not already playing, and only if the capture is in progress
        if (audioSource && capturingSound && !audioSource.isPlaying && captureProgress > 0 && captureProgress < captureThreshold)
        {
            photonView.RPC("RPC_PlayCaptureSound", RpcTarget.All);
        }
    }

    void StopCapturingSound()
    {
        // Stop the sound if it is currently playing and matches the capturing sound
        if (audioSource && audioSource.isPlaying && audioSource.clip == capturingSound)
        {
            photonView.RPC("RPC_StopCapturingSound", RpcTarget.All);
        }
    }
    void PlayCapturedSound()
    {
        photonView.RPC("RPC_PlayCapturedSound", RpcTarget.All);
    }
    void PlayNeutralizationSound()
    {
        if (audioSource && capturedSound)  // You can use a unique neutralization sound if needed
        {
            photonView.RPC("RPC_PlayNeutralizationSound", RpcTarget.All);
            Debug.Log("[PlayNeutralizationSound] Played neutralization sound.");
        }
    }
    void PlayRecaptureSound()
    {
        if (audioSource && capturedSound)  // You can use a unique neutralization sound if needed
        {
            photonView.RPC("RPC_PlayRecaptureSound", RpcTarget.All);
            Debug.Log("[playRecaptureSound] Played Recapture sound.");
        }
    }


    void CompleteCapture(int capturingTeam)
    {

        UpdateBuildingManager(false);

        controllingTeam = capturingTeam;
        isCaptured = true;

        // CALLING THE UPDATE FUNCTION TO UPLOAD THE LATEST DATA
        UpdateBuildingManager(true);

        Debug.Log($"[BuildingCapture] Building captured by team {capturingTeam}!");
        photonView.RPC("RPC_CompleteCapture", RpcTarget.All, controllingTeam);

        // Stop capturing sound and play captured sound
        StopCapturingSound();
        PlayCapturedSound();
    }


    // THE FUNCTION TO UPDATE THE BUILDING DATA FOR ALL PLAYERS
    void UpdateBuildingManager(bool value)
    {
        if (controllingTeam == -1)
            return;

        BuildingManager.Instance.UpdateTowerDictionary(value, controllingTeam, buildingID);
    }

    // Sound only. The flag is no longer set here: it follows replicated ownership via
    // BuildingManager, so a late joiner gets the right colour without needing this call.
    [PunRPC]
    void RPC_CompleteCapture(int teamID)
    {
        if (audioSource && capturedSound)
            audioSource.PlayOneShot(capturedSound);
    }

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponent<PlayerTeam>();
        if (!player) return;

        BuildingManager manager = BuildingManager.Instance;
        if (manager == null) return;

        // Your own capital is always enterable. This test used to sit INSIDE the loop below, so
        // it could only be reached by a tower that has at least one adjacent, and it was
        // re-evaluated once per adjacent. A tower with an empty Adjacents list was therefore
        // uncapturable by anyone -- worth knowing before the Tier-4 centre zone is added.
        bool allowed = manager.CathedralBuildingIDs.TryGetValue(buildingID, out int capitalOwner)
                       && capitalOwner == player.teamID;

        if (!allowed)
        {
            // Guarded: the dictionary indexer logs an error and hands back a default TowerData
            // for a missing key, which silently reads as "neutral, owned by nobody".
            if (!manager.TowerDictionary.ContainsKey(buildingID))
            {
                Debug.LogWarning($"[BuildingCapture] Tower {buildingID} is missing from the TowerDictionary.");
                return;
            }

            var adjacents = manager.TowerDictionary[buildingID].Adjacents;
            if (adjacents != null)
            {
                foreach (var adjacent in adjacents)
                {
                    if (!manager.TowerDictionary.ContainsKey(adjacent))
                        continue;

                    TowerData adjacentTowerData = manager.TowerDictionary[adjacent];
                    if (adjacentTowerData.isCaptured && adjacentTowerData.controllingTeam == player.teamID)
                    {
                        allowed = true;
                        break;
                    }
                }
            }
        }

        if (!allowed)
            return;

        if (capturingID == -1)
            capturingID = player.teamID;

        // No immediate reset if an enemy enters; recapture decay is handled in HandleCapturedState.
        photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, player.teamID);

        if (player.photonView.IsMine)
        {
            Debug.Log($"[BuildingCapture] Team {player.teamID} entered tower {buildingID} (capturingID {capturingID}).");
            photonView.RPC("RPC_AddToZone", RpcTarget.MasterClient, player.photonView.ViewID);
        }
    }

    void OnTriggerExit(Collider other)
    {
        var player = other.GetComponent<PlayerTeam>();
        if (player && player.photonView.IsMine)
        {
            photonView.RPC("RPC_RemoveFromZone", RpcTarget.MasterClient, player.photonView.ViewID);
            /*if (player.teamID == capturingID)
            {
                Debug.Log($"[OnTriggerExit] Player from team {player.teamID} left zone (matched capturingID).");
            }
            else
            {
                Debug.Log($"[OnTriggerExit] Player from team {player.teamID} left zone (ignored, as capturingID is {capturingID}).");
            }*/
        }
    }

    [PunRPC]
    void RPC_UpdateCapturingID(int teamID)
    {
        if (capturingID == -1)
        {
            capturingID = teamID;
            Debug.Log($"[RPC_UpdateCapturingID] CapturingID was unset. Now set to player's teamID: {capturingID}");
        }
    }

    [PunRPC]
    void RPC_AddToZone(int viewID)
    {
        Debug.Log($"[RPC_AddToZone] Inside Function");
        var pv = PhotonView.Find(viewID);
        if (pv && pv.GetComponent<PlayerTeam>() is PlayerTeam pt)
        {
            if (!playersInZone.Contains(pt))
            {
                playersInZone.Add(pt);
                Debug.Log($"[RPC_AddToZone] Added player (Team {pt.teamID}) to zone.");
                if (!isCaptured && audioSource && !audioSource.isPlaying)
                {
                    photonView.RPC("RPC_PlayCaptureSound", RpcTarget.All);
                }
            }
            else
            {
                Debug.LogWarning("[RPC_AddToZone] Player already in zone!");
            }
        }
        else
        {
            Debug.LogWarning("[RPC_AddToZone] PlayerTeam component not found!");
        }
    }

    [PunRPC]
    void RPC_RemoveFromZone(int viewID)
    {
        var pt = PhotonView.Find(viewID)?.GetComponent<PlayerTeam>();
        if (pt && playersInZone.Contains(pt))
        {
            playersInZone.Remove(pt);

            if (!playersInZone.Any(p => p.teamID == capturingID))
            {
                capturingID = -1;
                captureProgress = 0;
            }

            Debug.Log($"[RPC_RemoveFromZone] Removed player (Team {pt.teamID}) from zone.");
        }
        else
        {
            Debug.LogWarning("[RPC_RemoveFromZone] Player not found in zone!");
        }
    }

    [PunRPC]
    void RPC_PlayCaptureSound()
    {
        if (audioSource && capturingSound && !audioSource.isPlaying)
        {
            audioSource.clip = capturingSound;
            audioSource.loop = true;
            audioSource.Play();
            Debug.Log("[RPC_PlayCaptureSound] Playing capturing sound.");
        }
    }

    [PunRPC]
    void RPC_StopCapturingSound()
    {
        if (audioSource && audioSource.isPlaying && audioSource.clip == capturingSound)
        {
            audioSource.Stop();
            audioSource.loop = false; // Ensure the loop is disabled
            Debug.Log("[RPC_StopCapturingSound] Stopped capturing sound.");
        }
    }

    [PunRPC]
    void RPC_PlayCapturedSound()
    {
        // Play the captured sound on all clients
        if (audioSource && capturedSound)
        {
            audioSource.PlayOneShot(capturedSound);
            Debug.Log("[RPC_PlayCapturedSound] Played captured sound.");
        }
    }

    [PunRPC]
    void RPC_PlayRecaptureSound()
    {
        if (audioSource && capturingSound)
        {
            audioSource.clip = capturingSound;
            audioSource.loop = true;
            audioSource.Play();
            Debug.Log("[RPC_PlayRecaptureSound] Playing recapture sound.");
        }
    }
    [PunRPC]
    void RPC_PlayNeutralizationSound()
    {
        if (audioSource && capturedSound)
        {
            audioSource.PlayOneShot(capturedSound);
            Debug.Log("[RPC_PlayNeutralizationSound] Played neutralization sound.");
        }
    }


    /// Sets the flag to match replicated ownership. Called on every client from
    /// BuildingManager.RPC_UpdateTowerDictionary, so the flag always follows the state rather
    /// than arriving as its own message that a late joiner never receives.
    /// Takes 'captured' separately because the dictionary keeps the previous owner's team id
    /// after a neutralise -- it is set to false before controllingTeam is cleared.
    public void ApplyOwnerVisual(bool captured, int teamID)
    {
        if (!flagRenderer)
            return;

        flagRenderer.material = GetTeamMaterial(captured ? teamID : -1);
    }

    Material GetTeamMaterial(int teamID)
    {
        return teamID switch
        {
            0 => team0Material,
            1 => team1Material,
            2 => team2Material,
            _ => neutralMaterial
        };
    }
    public bool IsCapturedByTeam(int teamID)
    {
        return isCaptured && controllingTeam == teamID;
    }

}
