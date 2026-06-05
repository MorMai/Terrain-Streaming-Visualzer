using System;

namespace LevelStreaming
{
    /// <summary>
    /// The seam between the streamer's state machine and the actual work of bringing chunk content in
    /// and out. The v2 design loads via <b>Addressables</b>; this project ships a simulated loader for
    /// the visualizer. The streamer owns ring evaluation, hysteresis and the chunk state machine and
    /// only delegates the load/unload <i>action</i> here, so a real Addressables implementation can drop
    /// in without touching streaming logic.
    /// </summary>
    public interface IChunkLoader
    {
        /// <summary>Begin bringing a chunk's content in. Invoke <paramref name="onComplete"/> once it is ready.</summary>
        void Load(ChunkCoord coord, ChunkTier tier, Action onComplete);

        /// <summary>Begin releasing a chunk's content. Invoke <paramref name="onComplete"/> once it is gone.</summary>
        void Unload(ChunkCoord coord, Action onComplete);

        /// <summary>Abort an in-flight load/unload for this coord (chunk left the ring before it finished).</summary>
        void Abort(ChunkCoord coord);
    }
}
