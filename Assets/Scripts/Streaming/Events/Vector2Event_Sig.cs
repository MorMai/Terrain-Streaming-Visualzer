using System;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// ScriptableObject event channel (the <c>*_Sig</c> convention from the v2 design). Decouples the
    /// origin-shift broadcaster (<see cref="FloatingOrigin"/>) from its many subscribers — camera rig,
    /// audio listener, particles, VFX, travel-mode proxies — exactly the role the "Obvious Soap bus"
    /// plays in the production project.
    ///
    /// Carries the horizontal <c>delta</c> applied by an origin recenter; subscribers self-correct by
    /// shifting their own transforms by <c>-delta</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "World Engine/Signals/onOriginShifted_Sig", fileName = "onOriginShifted_Sig")]
    public class Vector2Event_Sig : ScriptableObject
    {
        private event Action<Vector2> _listeners;

        public void Register(Action<Vector2> listener) => _listeners += listener;
        public void Unregister(Action<Vector2> listener) => _listeners -= listener;

        public void Raise(Vector2 delta) => _listeners?.Invoke(delta);
    }
}
