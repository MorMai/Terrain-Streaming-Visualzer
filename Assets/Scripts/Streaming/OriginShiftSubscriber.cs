using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Reference implementation of a v2-design origin-shift subscriber (§4.3 step 5). A world-space
    /// object that is <b>not</b> driven by the player/camera rig — e.g. a parked airship proxy, a
    /// VFX emitter, an audio source — registers with <see cref="Vector2Event_Sig"/> and shifts its own
    /// transform by <c>-delta</c> when the origin recenters, so it stays put in absolute space.
    ///
    /// Drop this on any such GameObject and assign the same <c>onOriginShifted_Sig</c> asset the
    /// <see cref="FloatingOrigin"/> raises. (Objects already positioned in rendered space each frame —
    /// like the streamed grid cells — don't need this; they follow the offset implicitly.)
    /// </summary>
    public class OriginShiftSubscriber : MonoBehaviour
    {
        [Tooltip("The origin-shift channel raised by FloatingOrigin.")]
        public Vector2Event_Sig onOriginShifted_Sig;

        void OnEnable()
        {
            if (onOriginShifted_Sig != null) onOriginShifted_Sig.Register(OnOriginShifted);
        }

        void OnDisable()
        {
            if (onOriginShifted_Sig != null) onOriginShifted_Sig.Unregister(OnOriginShifted);
        }

        private void OnOriginShifted(Vector2 delta)
        {
            transform.position -= new Vector3(delta.x, delta.y, 0f);
        }
    }
}
