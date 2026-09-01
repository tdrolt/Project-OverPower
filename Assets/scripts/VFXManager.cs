using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VFXManager : MonoBehaviour
{
    // Singleton instance of VFXManager
    public static VFXManager Instance;

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        if (Instance == null)
            Instance = this; // Set this instance as the singleton instance
        else
            Destroy(this); // Destroy if another instance already exists
    }

    // Method to play VFX at a specified position
    public void PlayVFX(GameObject effectObject, Vector3 effectPosition)
    {
        // Instantiate the VFX at the given position with no rotation (Quaternion.identity)
        GameObject vfxObject = Instantiate(effectObject, effectPosition, Quaternion.identity);

        // Get all ParticleSystems in the instantiated VFX object and its children
        ParticleSystem[] particleSystems = vfxObject.GetComponentsInChildren<ParticleSystem>();

        float maxLength = 0f;

        // Calculate the longest duration a ParticleSystem will play
        foreach (ParticleSystem individualParticleSystem in particleSystems)
        {
            float currentKnownMaxLength = individualParticleSystem.main.duration
                                        + individualParticleSystem.main.startLifetime.constantMax;

            // Update the maxLength if the current particle system's duration is longer
            if (currentKnownMaxLength > maxLength)
                maxLength = currentKnownMaxLength;
        }

        // Destroy the VFX object after the longest ParticleSystem duration has passed
        Destroy(vfxObject, maxLength);
    }
}
