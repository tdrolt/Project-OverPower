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
    public float decayRate = 10f;

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
        Debug.Log($"[BuildingCapture] Building ready with capturingID {capturingID}. Waiting for rightful team to show up.");
    }

    void ConfigureCollider()
    {
        var collider = GetComponent<SphereCollider>();
        if (collider)
        {
            collider.radius = captureRadius;
            Debug.Log($"[BuildingCapture] Collider radius set to {captureRadius}.");
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
        else
            Debug.Log("[BuildingCapture] AudioSource initialized.");
    }

    void ResetFlag()
    {
        if (flagRenderer && neutralMaterial)
        {
            flagRenderer.material = neutralMaterial;
            Debug.Log("[BuildingCapture] Flag reset to neutral.");
        }
        else
        {
            Debug.LogWarning("[BuildingCapture] Missing flagRenderer or neutralMaterial!");
        }
    }

    void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        foreach (var p in playersInZone)
        {
            Debug.Log($"[BuildingCapture] Player in zone: TeamID {p.teamID}");
        }

        if (isCaptured)
        {
            Debug.Log($"[BuildingCapture] Is Captured");
            HandleCapturedState(); // NEW: now handles recapture decay if enemy present
            return;
        }

        Debug.Log($"[BuildingCapture] Checking Cooldown");
        if (isOnCooldown) return;

        Debug.Log($"[BuildingCapture] Entering Calculate Capture Progress");

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
        // Decay started – progress resets to threshold.
    }

    void UpdateDecay()
    {
        float decayStep = (captureThreshold / 5f) * Time.deltaTime;
        captureProgress -= decayStep;
        Debug.Log($"[BuildingCapture] Recapture decay... New progress: {captureProgress} (-{decayStep} per frame).");
    }

    void NeutralizeBuilding()
    {
        // REMOVING THE CAPTURED BUILDING FROM TEAM DATA
        UpdateBuildingManager(false);

        controllingTeam = -1;
        isCaptured = false;
        isDecaying = false;
        photonView.RPC("RPC_UpdateFlag", RpcTarget.All, -1);
        PlayNeutralizationSound(); // Play neutralization sound when neutralizing the building
        StartCoroutine(CooldownRoutine());
    }

    // Kept the old recapture method, though now it's not used directly.
    [PunRPC]
    void RecaptureBuilding()
    {
        controllingTeam = -1;
        isCaptured = false;
        isDecaying = false;
        captureProgress = 0f;
        photonView.RPC("RPC_UpdateFlag", RpcTarget.All, -1);
        Debug.Log("[RecaptureBuilding] Building reset for recapture.");
    }

    IEnumerator CooldownRoutine()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(5f);
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

            Debug.Log($"[BuildingCapture] {count} eligible player(s) capturing. Progress increased by {contribution}, now at {captureProgress}.");

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
            //ApplyDecay();

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

    [PunRPC]
    void RPC_CompleteCapture(int teamID)
    {
        Material mat = GetTeamMaterial(teamID);

        if (flagRenderer)
        {
            flagRenderer.material = mat;
            Debug.Log($"[RPC_CompleteCapture] Flag updated to material {mat.name} for team {teamID}.");
        }
        else
        {
            Debug.LogWarning("[RPC_CompleteCapture] Missing flagRenderer!");
        }
        if (audioSource)
        {
            audioSource.PlayOneShot(capturedSound);
            Debug.Log("[RPC_CompleteCapture] Played captured sound.");
        }
    }

    void ApplyDecay()
    {
        float decayStep = decayRate * Time.deltaTime;
        captureProgress -= decayStep;
        captureProgress = Mathf.Max(captureProgress, 0);
    }

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponent<PlayerTeam>();
        if (player)
        {

            var adjacents = BuildingManager.Instance.TowerDictionary[buildingID].Adjacents;

            bool adjacentCaptured = false;

            foreach (var adjacent in adjacents)
            {
                TowerData adjacentTowerData = BuildingManager.Instance.TowerDictionary[adjacent];

                if (adjacentTowerData.isCaptured && adjacentTowerData.controllingTeam == player.teamID)
                {
                    //Debug.Log($"[OnTriggerEnter] Captured Adjacent Detected: {}.");
                    adjacentCaptured = true;
                    break;
                }

                if (BuildingManager.Instance.CathedralBuildingIDs.ContainsKey(buildingID) && BuildingManager.Instance.CathedralBuildingIDs[buildingID] == player.teamID)
                {
                    adjacentCaptured = true;
                    break;
                }
            }

            if (adjacentCaptured == false)
                return;

            if (capturingID == -1)
                capturingID = player.teamID;

            // NEW: No immediate reset if enemy enters; the recapture decay is now handled in HandleCapturedState.
            // We still update the capturingID if needed.            
            photonView.RPC("RPC_UpdateCapturingID", RpcTarget.MasterClient, player.teamID);

            if (player.photonView.IsMine)
            {
                Debug.Log($"[OnTriggerEnter] Player from team {player.teamID} entered zone and matches capturingID {capturingID}.");
                Debug.Log($"[OnTriggerEnter] Player's PhotonView ViewID: {player.photonView.ViewID}");
                photonView.RPC("RPC_AddToZone", RpcTarget.MasterClient, player.photonView.ViewID);
            }
            else if (player.photonView.IsMine)
            {
                Debug.Log($"[OnTriggerEnter] Player from team {player.teamID} entered zone but does NOT match capturingID {capturingID}.");
            }
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


    [PunRPC]
    void RPC_UpdateFlag(int teamID)
    {
        if (flagRenderer)
        {
            flagRenderer.material = GetTeamMaterial(teamID);
            Debug.Log($"[RPC_UpdateFlag] Flag updated to {(teamID == -1 ? "neutral" : $"team {teamID}")} material.");
        }
        else
        {
            Debug.LogWarning("[RPC_UpdateFlag] Missing flagRenderer!");
        }
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
