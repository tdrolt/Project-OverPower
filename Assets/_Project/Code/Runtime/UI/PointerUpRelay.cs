using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Overpower.UI
{
    /// <summary>Tells when the mouse is let go over a control it was pressed on (a slider's drag or click ends): Slider has no event for it.</summary>
    public sealed class PointerUpRelay : MonoBehaviour, IPointerUpHandler
    {
        public Action Released;

        public void OnPointerUp(PointerEventData eventData) => Released?.Invoke();
    }
}
