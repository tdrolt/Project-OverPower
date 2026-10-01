using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public GameObject AudioPrefab;
    public static AudioManager Instance;

    void Awake()
    {
        if(Instance == null)
            Instance = this;
        else
            Destroy(Instance);
    }

    // Only guns call this (the shot in WeaponFiring, the impact in ProjectileMotor - the Stun Gun and Zip Gun bullets' impact
    // sounds use this same prefab, so they are 3D too). The shot-sound prefab holds the hearing
    // distances (Tudor: full to 15 m, silent at 35 m, fully 3D); the listener is on the followed player (ListenerRig).
    public void Play3D(AudioClip clip, Vector3 position) 
    { 
        GameObject audioGameObject = Instantiate(AudioPrefab, position, Quaternion.identity);
        AudioSource source = audioGameObject.GetComponent<AudioSource>();

        source.clip = clip;
        source.Play();

        Destroy(audioGameObject, clip.length);
    }
}
