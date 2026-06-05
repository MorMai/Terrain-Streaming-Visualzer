using System;

namespace LevelStreaming
{
    /// <summary>Lifecycle state of a chunk.</summary>
    public enum ChunkState
    {
        Unloaded,
        Loading,
        Loaded,
        Unloading
    }

    /// <summary>
    /// Detail tier of a chunk (v2 design §5.2). High-detail chunks carry collision/high-poly
    /// content; proxy chunks are the low-poly, shared-material horizon ring.
    /// </summary>
    public enum ChunkTier
    {
        HighDetail,
        Proxy
    }

    /// <summary>
    /// Pure data + state for one chunk. Knows nothing about how it is loaded or rendered.
    /// Emits <see cref="StateChanged"/> on every transition so views can react.
    /// The actual (simulated) load timing is driven externally by the ChunkStreamer,
    /// which keeps this class free of any MonoBehaviour/coroutine dependency. That seam is
    /// what lets real async asset loading replace the simulation later without touching views.
    /// </summary>
    public class Chunk
    {
        public ChunkCoord Coord { get; }
        public ChunkState State { get; private set; }

        /// <summary>Detail tier this chunk is currently being streamed at (HD vs proxy ring).</summary>
        public ChunkTier Tier { get; set; } = ChunkTier.HighDetail;

        /// <summary>Fired after State changes. Argument is this chunk.</summary>
        public event Action<Chunk> StateChanged;

        public Chunk(ChunkCoord coord)
        {
            Coord = coord;
            State = ChunkState.Unloaded;
        }

        public void SetState(ChunkState newState)
        {
            if (State == newState) return;
            State = newState;
            StateChanged?.Invoke(this);
        }
    }
}
