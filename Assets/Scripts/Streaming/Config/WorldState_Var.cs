using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Runtime world state (the <c>*_Var</c> convention from the v2 design). Holds the
    /// full-precision true position and the cumulative floating-origin offset so any system can
    /// read absolute coordinates without depending on the streamer directly.
    ///
    /// The v2 design specifies <c>double3</c> fields; this project has no <c>com.unity.mathematics</c>
    /// dependency and is a 2D (top-down XY) visualizer, so the horizontal plane is stored as a pair
    /// of <see cref="double"/> values — same full-precision intent, zero extra packages. Altitude is
    /// governed by <see cref="World_Cfg.TravelAltitude"/>, not stored here.
    ///
    /// Being a runtime SO, its fields persist in the asset between sessions; call <see cref="ResetState"/>
    /// at startup if you want a clean origin.
    /// </summary>
    [CreateAssetMenu(menuName = "World Engine/WorldState_Var", fileName = "WorldState_Var")]
    public class WorldState_Var : ScriptableObject
    {
        [Header("True position (full precision, horizontal plane)")]
        public double TrueWorldX;
        public double TrueWorldY;

        [Header("Cumulative floating-origin offset")]
        [Tooltip("absolute = rendered + CumulativeOffset")]
        public double CumulativeOffsetX;
        public double CumulativeOffsetY;

        [Header("Streaming")]
        public Vector2Int ChunkIndex;

        [Header("Diagnostics")]
        [Tooltip("Number of origin recenters performed this session.")]
        public int ShiftCounter;

        /// <summary>Reset everything to the physical origin.</summary>
        public void ResetState()
        {
            TrueWorldX = TrueWorldY = 0;
            CumulativeOffsetX = CumulativeOffsetY = 0;
            ChunkIndex = Vector2Int.zero;
            ShiftCounter = 0;
        }

        /// <summary>Apply one origin shift: bank <paramref name="delta"/> into the cumulative offset.</summary>
        public void AccumulateShift(Vector2 delta)
        {
            CumulativeOffsetX += delta.x;
            CumulativeOffsetY += delta.y;
            ShiftCounter++;
        }

        /// <summary>Recompute <see cref="TrueWorldX"/>/<see cref="TrueWorldY"/> and <see cref="ChunkIndex"/> from a rendered position.</summary>
        public void UpdateFromRendered(Vector2 renderedPos, float chunkSize)
        {
            TrueWorldX = renderedPos.x + CumulativeOffsetX;
            TrueWorldY = renderedPos.y + CumulativeOffsetY;
            if (chunkSize > 0f)
            {
                ChunkIndex = new Vector2Int(
                    Mathf.FloorToInt((float)(TrueWorldX / chunkSize)),
                    Mathf.FloorToInt((float)(TrueWorldY / chunkSize)));
            }
        }
    }
}
