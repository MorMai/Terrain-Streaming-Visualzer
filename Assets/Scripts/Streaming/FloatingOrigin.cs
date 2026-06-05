using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Floating-origin (origin rebasing) executor. When the player drifts past a threshold from the
    /// rendered origin, the world is shifted back so the player stays near (0,0) in Unity space —
    /// keeping float precision high — while a high-precision (double) offset tracks the cumulative
    /// shift. The player's *absolute* (virtual) position = rendered position + offset, and chunk
    /// coordinates are derived from the absolute position so streaming is unaffected by rebasing.
    ///
    /// Aligned with the v2 design's floating-origin section: on each shift it banks the horizontal
    /// <c>delta</c> into <see cref="WorldState_Var.CumulativeOffsetX"/>/<c>Y</c> (<c>*_Var</c>), bumps
    /// the <c>ShiftCounter</c>, and broadcasts the delta on <see cref="onOriginShifted_Sig"/>
    /// (<c>*_Sig</c>) so every subscriber — camera rig, audio listener, particles, VFX — can
    /// self-correct by <c>-delta</c>. Trigger threshold/hysteresis are read from a
    /// <see cref="World_Cfg"/> when one is assigned.
    ///
    /// (Design note: the v2 doc has the <c>ChunkStreamer</c> own the shift; this project keeps the
    /// shift in this dedicated component and has the streamer subscribe — same signal/subscriber model,
    /// it just lives on its own GameObject.)
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FloatingOrigin : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [SerializeField] private PlayerController player;

        [Header("Settings")]
        public bool useFloatingOrigin = true;
        [Tooltip("Rebase when the player gets this many Unity units from the rendered origin. Overridden by World_Cfg.ThresholdDistance when a config is assigned.")]
        [Min(1f)] public float threshold = 20f;
        [Tooltip("Also shift the main camera on rebase (keeps the view stable when not following).")]
        public bool shiftCamera = true;

        [Header("Design assets (optional — *_Cfg / *_Var / *_Sig)")]
        [Tooltip("If assigned, ThresholdDistance + HysteresisBand come from here.")]
        public World_Cfg config;
        [Tooltip("Runtime record updated with the cumulative offset and shift count.")]
        public WorldState_Var worldState;
        [Tooltip("Broadcast the horizontal delta to all subscribers after a shift.")]
        public Vector2Event_Sig onOriginShifted_Sig;

        /// <summary>Cumulative world offset: absolutePos = renderedPos + (OffsetX, OffsetY).</summary>
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public Vector2 LastShift { get; private set; }
        public int RebaseCount { get; private set; }

        // v2-design vocabulary aliases.
        public Vector2 CumulativeOffset => new Vector2((float)OffsetX, (float)OffsetY);
        public int ShiftCounter => RebaseCount;

        private float TriggerThreshold => config != null ? config.ThresholdDistance + config.HysteresisBand : threshold;

        void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();
        }

        void Update()
        {
            if (!useFloatingOrigin || player == null) return;

            // Horizontal magnitude from the rendered origin (design §4.2: length(pos.x, pos.z)).
            Vector3 p = player.transform.position;
            Vector2 horizontal = new Vector2(p.x, p.y);
            if (horizontal.magnitude <= TriggerThreshold) return;

            Rebase(horizontal); // bring the player back to the rendered origin
        }

        private void Rebase(Vector2 shift)
        {
            ApplyShift(shift);

            OffsetX += shift.x;
            OffsetY += shift.y;
            LastShift = shift;
            RebaseCount++;

            if (worldState != null) worldState.AccumulateShift(shift);
            onOriginShifted_Sig?.Raise(shift);
        }

        /// <summary>Shift the directly-owned transforms (player + optionally camera) by -shift.</summary>
        private void ApplyShift(Vector2 shift)
        {
            player.transform.position -= new Vector3(shift.x, shift.y, 0f);

            if (shiftCamera && Camera.main != null)
            {
                Vector3 c = Camera.main.transform.position;
                Camera.main.transform.position = new Vector3(c.x - shift.x, c.y - shift.y, c.z);
            }
        }

        /// <summary>
        /// Sovereign Jump support (design §8): teleport so the player's <b>absolute</b> position
        /// becomes <paramref name="absolute"/> while landing near physical origin (rendered ≈ 0).
        /// The whole target is banked into the cumulative offset; this is a discontinuity, not a
        /// shift, so it does not emit a per-frame delta on <see cref="onOriginShifted_Sig"/>.
        /// </summary>
        public void JumpToAbsolute(Vector2 absolute)
        {
            OffsetX = absolute.x;
            OffsetY = absolute.y;

            float pz = player.transform.position.z;
            player.transform.position = new Vector3(0f, 0f, pz);

            if (shiftCamera && Camera.main != null)
            {
                Vector3 c = Camera.main.transform.position;
                Camera.main.transform.position = new Vector3(0f, 0f, c.z);
            }

            if (worldState != null)
            {
                worldState.CumulativeOffsetX = OffsetX;
                worldState.CumulativeOffsetY = OffsetY;
                worldState.TrueWorldX = absolute.x;
                worldState.TrueWorldY = absolute.y;
            }
        }
    }
}
