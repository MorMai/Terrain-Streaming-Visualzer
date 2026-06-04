using System.Collections.Generic;

namespace LevelStreaming
{
    /// <summary>
    /// Context handed to a strategy so it can make richer decisions without the
    /// StreamingManager hard-coding any particular policy. Extend freely; existing
    /// strategies ignore fields they don't need.
    /// </summary>
    public struct StreamingContext
    {
        public float ChunkSize;
        /// <summary>Optional sight cone, present if a SightCone exists in the scene.</summary>
        public SightCone Sight;
    }

    /// <summary>
    /// Pluggable decision: "which chunks should be loaded right now?". The StreamingManager
    /// consumes ONLY this interface and diffs the result against what is currently loaded,
    /// so swapping strategies (radius, sight-based, predictive, priority...) needs no change
    /// to the manager or any renderer.
    /// </summary>
    public interface IStreamingStrategy
    {
        /// <summary>Human-readable name shown in the debug UI title.</summary>
        string DisplayName { get; }

        IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx);
    }
}
