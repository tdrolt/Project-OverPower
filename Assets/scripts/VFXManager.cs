using System.Collections.Generic;
using Overpower.UI;
using Overpower.Weapons;
using UnityEngine;

/// <summary>
/// Plays the impact and muzzle flashes. Copies are pooled per prefab (FxPoolRule): a finished copy is switched off and kept, never
/// destroyed, so a nine-player SMG fight makes no new objects once each flash has been seen a few times. A flash lasts as long as its
/// longest particle system (duration + start lifetime). Each flash is drawn a little toward the camera (FxPoolRule.Placed) so one on a
/// wall is not half buried in it.
/// </summary>
public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance;

    [SerializeField, Tooltip("Where the most copies of one flash on screen at once and how far a flash is drawn toward the camera come " +
             "from (its Arena Look's Fx Pool Cap and Fx Camera Pull): Assets/Gameplay/Config/UiTheme.asset.")]
    private UiTheme theme;

    private sealed class Copy
    {
        public GameObject Root;
        public ParticleSystem[] Systems;
        public float EndsAt;
    }

    private sealed class Pool
    {
        public float Seconds;
        public readonly Queue<Copy> Playing = new Queue<Copy>(); // oldest first: every copy of one prefab lasts the same time
        public readonly Stack<Copy> Idle = new Stack<Copy>();
    }

    private const int FallbackCap = 24;

    private readonly Dictionary<GameObject, Pool> pools = new Dictionary<GameObject, Pool>();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(this);
    }

    public void PlayVFX(GameObject effectObject, Vector3 effectPosition) => PlayVFX(effectObject, effectPosition, Quaternion.identity);

    /// <summary>A flash turned to <paramref name="rotation"/>; only a flash drawn in its own space (the ricochet chevron) shows the turn.</summary>
    public void PlayVFX(GameObject effectObject, Vector3 effectPosition, Quaternion rotation)
    {
        if (effectObject == null)
            return;
        if (!pools.TryGetValue(effectObject, out Pool pool))
        {
            pool = new Pool { Seconds = LengthOf(effectObject) };
            pools.Add(effectObject, pool);
        }
        DropDestroyed(pool);

        int cap = theme != null && theme.arenaLook != null ? theme.arenaLook.fxPoolCap : FallbackCap;
        Copy copy;
        switch (FxPoolRule.Take(pool.Idle.Count, pool.Playing.Count, cap))
        {
            case FxPoolAction.ReuseIdle:
                copy = pool.Idle.Pop();
                break;
            case FxPoolAction.RecycleOldest:
                copy = pool.Playing.Dequeue();
                break;
            default:
                GameObject root = Instantiate(effectObject, effectPosition, Quaternion.identity, transform);
                root.SetActive(false);
                copy = new Copy { Root = root, Systems = root.GetComponentsInChildren<ParticleSystem>(true) };
                break;
        }

        Camera view = Camera.main;
        float pull = theme != null && theme.arenaLook != null ? theme.arenaLook.fxCameraPull : 0f;
        Vector3 drawnAt = view != null ? FxPoolRule.Placed(effectPosition, view.transform.position, pull) : effectPosition;
        copy.Root.transform.SetPositionAndRotation(drawnAt, rotation);
        if (copy.Root.activeSelf)
        {
            // A recycled copy is mid-play: clear it and start over at the new spot.
            foreach (ParticleSystem system in copy.Systems)
            {
                system.Clear(false);
                system.Play(false);
            }
        }
        else
            copy.Root.SetActive(true); // every flash plays on awake
        copy.EndsAt = Time.time + pool.Seconds;
        pool.Playing.Enqueue(copy);
    }

    private void Update()
    {
        float now = Time.time;
        foreach (Pool pool in pools.Values)
        {
            while (pool.Playing.Count > 0 && pool.Playing.Peek().EndsAt <= now)
            {
                Copy done = pool.Playing.Dequeue();
                if (done.Root == null)
                    continue;
                done.Root.SetActive(false);
                pool.Idle.Push(done);
            }
        }
    }

    /// <summary>Copies destroyed behind the pool's back (a scene unload under a kept manager) are forgotten.</summary>
    private static void DropDestroyed(Pool pool)
    {
        while (pool.Idle.Count > 0 && pool.Idle.Peek().Root == null)
            pool.Idle.Pop();
        while (pool.Playing.Count > 0 && pool.Playing.Peek().Root == null)
            pool.Playing.Dequeue();
    }

    private static float LengthOf(GameObject effect)
    {
        float maxLength = 0f;
        foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            float length = system.main.duration + system.main.startLifetime.constantMax;
            if (length > maxLength)
                maxLength = length;
        }
        return maxLength;
    }
}
