using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LevelStreaming
{
    /// <summary>
    /// Top-down movement. Single source of truth for chunkSize and the world->chunk mapping.
    /// Distinguishes RENDERED position (transform, kept near origin by the floating-origin system)
    /// from ABSOLUTE/virtual position (rendered + offset). Chunk coordinates derive from the
    /// absolute position so streaming is unaffected by origin rebasing.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("World")]
        [Tooltip("World-space size of one square chunk. Shared with manager/renderer.")]
        [Min(0.01f)] public float chunkSize = 1f;

        [Header("Movement")]
        [Min(0f)] public float moveSpeed = 4f;

        [Header("Floating origin (optional)")]
        public FloatingOrigin floatingOrigin;

        /// <summary>Rendered (rebased) position — what the camera actually shows.</summary>
        public Vector2 WorldPos => transform.position;

        public double OffsetX => floatingOrigin != null ? floatingOrigin.OffsetX : 0.0;
        public double OffsetY => floatingOrigin != null ? floatingOrigin.OffsetY : 0.0;

        /// <summary>Absolute (virtual) position = rendered + accumulated origin offset.</summary>
        public double AbsoluteX => transform.position.x + OffsetX;
        public double AbsoluteY => transform.position.y + OffsetY;
        public Vector2 AbsolutePosition => new Vector2((float)AbsoluteX, (float)AbsoluteY);

        public ChunkCoord CurrentChunk => new ChunkCoord(
            (int)Math.Floor(AbsoluteX / chunkSize),
            (int)Math.Floor(AbsoluteY / chunkSize));

        /// <summary>Normalized facing direction, retained when standing still.</summary>
        public Vector2 Facing { get; private set; } = Vector2.up;

        void Awake()
        {
            if (floatingOrigin == null) floatingOrigin = FindObjectOfType<FloatingOrigin>();
        }

        /// <summary>Rendered (rebased) world center of a chunk, accounting for the origin offset.</summary>
        public Vector2 ChunkCenterRendered(ChunkCoord c)
        {
            double ax = (c.cx + 0.5) * chunkSize;
            double ay = (c.cy + 0.5) * chunkSize;
            return new Vector2((float)(ax - OffsetX), (float)(ay - OffsetY));
        }

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
