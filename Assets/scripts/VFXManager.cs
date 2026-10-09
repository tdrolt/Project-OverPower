using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(this);
    }

    public void PlayVFX(GameObject effectObject, Vector3 effectPosition)
    {
        GameObject vfxObject = Instantiate(effectObject, effectPosition, Quaternion.identity);

        ParticleSystem[] particleSystems = vfxObject.GetComponentsInChildren<ParticleSystem>();

        float maxLength = 0f;

        foreach (ParticleSystem individualParticleSystem in particleSystems)
        {
            float currentKnownMaxLength = individualParticleSystem.main.duration
                                        + individualParticleSystem.main.startLifetime.constantMax;

            if (currentKnownMaxLength > maxLength)
                maxLength = currentKnownMaxLength;
        }

        // Cleans itself up once the longest child system has finished (duration + start lifetime).
        Destroy(vfxObject, maxLength);
    }
}
