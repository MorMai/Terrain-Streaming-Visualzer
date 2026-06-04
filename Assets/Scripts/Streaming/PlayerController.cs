using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LevelStreaming
{
    /// <summary>
    /// Top-down movement. Single source of truth for chunkSize and the world->chunk mapping.
    /// Exposes WorldPos, CurrentChunk and Facing for the rest of the system.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("World")]
        [Tooltip("World-space size of one square chunk. Shared with manager/renderer.")]
        [Min(0.01f)] public float chunkSize = 1f;

        [Header("Movement")]
        [Min(0f)] public float moveSpeed = 4f;

        public Vector2 WorldPos => transform.position;
        public ChunkCoord CurrentChunk => ChunkCoord.FromWorld(WorldPos, chunkSize);

        /// <summary>Normalized facing direction, retained when standing still.</summary>
        public Vector2 Facing { get; private set; } = Vector2.up;

        void Update()
        {
            Vector2 input = ReadInput();

            if (input.sqrMagnitude > 0.0001f)
            {
                input.Normalize();
                Facing = input;
                transform.position += (Vector3)(input * moveSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Reads WASD/arrow movement, supporting either the new Input System package
        /// or the legacy Input Manager depending on the project's active backend.
        /// </summary>
        private static Vector2 ReadInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                    - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            return new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
#endif
        }
    }
}
