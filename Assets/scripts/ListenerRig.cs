using UnityEngine;

/// <summary>
/// Gun sounds fade by distance from YOU, not from the camera that hangs above and behind. The scene's AudioListener
/// sits on the camera, so CameraTracking moves it onto a child object placed on the followed player every frame. The
/// child keeps the camera's turn, so left and right in the headphones match the screen.
/// </summary>
public static class ListenerRig
{
    /// <summary>Turns the camera's own listener off and puts a new one on a child object. Returns that child, or null when
    /// the camera has no listener (nothing is changed then).</summary>
    public static Transform MoveListenerToChild(GameObject cameraObject)
    {
        var own = cameraObject.GetComponent<AudioListener>();
        if (own == null)
            return null;
        own.enabled = false;

        var child = new GameObject("Audio Listener");
        child.transform.SetParent(cameraObject.transform, false);
        child.AddComponent<AudioListener>();
        return child.transform;
    }

    /// <summary>Places the listener on the target's body, or back on the camera when there is no target.</summary>
    public static void Follow(Transform listener, Transform target)
    {
        if (listener == null)
            return;
        if (target != null)
            listener.position = target.position;
        else
            listener.localPosition = Vector3.zero;
    }
}
