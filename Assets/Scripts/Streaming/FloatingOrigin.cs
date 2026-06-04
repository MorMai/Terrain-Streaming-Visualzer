using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Floating-origin (origin rebasing) system. When the player drifts past a threshold from the
    /// rendered origin, the world is shifted back so the player stays near (0,0) in Unity space —
    /// keeping float precision high — while a high-precision (double) offset tracks the cumulative
    /// shift. The player's *absolute* (virtual) position = rendered position + offset, and chunk
    /// coordinates are derived from the absolute position so streaming is unaffected by rebasing.
    ///
    /// Because the player, camera and grid all translate by the same amount, a rebase is visually
    /// seamless — only the debug readouts reveal it. Runs after the player has moved this frame so
    /// the renderer (LateUpdate) picks up the new offset the same frame, with no one-frame jump.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FloatingOrigin : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [SerializeField] private PlayerController player;

        [Header("Settings")]
        public bool useFloatingOrigin = true;
        [Tooltip("Rebase when the player gets this many Unity units from the rendered origin.")]
        [Min(1f)] public float threshold = 20f;
        [Tooltip("Also shift the main camera on rebase (keeps the view stable when not following).")]
        public bool shiftCamera = true;

        /// <summary>Cumulative world offset: absolutePos = renderedPos + (OffsetX, OffsetY).</summary>
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public Vector2 LastShift { get; private set; }
        public int RebaseCount { get; private set; }

        void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();
        }

        void Update()
        {
            if (!useFloatingOrigin || player == null) return;

            Vector3 p = player.transform.position;
            if (Mathf.Abs(p.x) < threshold && Mathf.Abs(p.y) < threshold) return;

            Rebase(new Vector2(p.x, p.y)); // bring the player back to the rendered origin
        }

        private void Rebase(Vector2 shift)
        {
            player.transform.position -= new Vector3(shift.x, shift.y, 0f);

            if (shiftCamera && Camera.main != null)
            {
                Vector3 c = Camera.main.transform.position;
                Camera.main.transform.position = new Vector3(c.x - shift.x, c.y - shift.y, c.z);
            }

            OffsetX += shift.x;
            OffsetY += shift.y;
            LastShift = shift;
            RebaseCount++;
        }
    }
}
