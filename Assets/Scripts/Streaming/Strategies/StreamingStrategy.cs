using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Base class for streaming strategies authored as ScriptableObject assets. Create concrete
    /// strategies via the Project window (Create > Level Streaming > Strategy > ...), tune them in
    /// the Inspector, and drop them into the ChunkStreamer's Strategies list to add/remove/reorder.
    /// Assets are shared and must stay stateless: decisions come only from their config + the context.
    /// </summary>
    public abstract class StreamingStrategy : ScriptableObject, IStreamingStrategy
    {
        [SerializeField]
        [Tooltip("Optional. Overrides the name shown in the debug UI title; leave empty for the default.")]
        private string displayNameOverride;

        public string DisplayName =>
            string.IsNullOrEmpty(displayNameOverride) ? DefaultName : displayNameOverride;

        /// <summary>Name used when no override is set.</summary>
        protected abstract string DefaultName { get; }

        public abstract IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx);

        /// <summary>
        /// Default: recompute only when the player's chunk changes. Dynamic strategies (e.g. the
        /// sight cone) override this to also react to aim/position.
        /// </summary>
        public virtual StreamingSignature GetSignature(ChunkCoord playerChunk, StreamingContext ctx)
            => new StreamingSignature(playerChunk.cx, playerChunk.cy, 0, 0);

        /// <summary>
        /// Detail tier a desired chunk should be streamed at (v2 design §5.2). The default treats
        /// every chunk as high-detail; the Chebyshev ring strategy overrides this to mark the outer
        /// rings as proxy so the streamer/loader can request low-poly horizon content there.
        /// </summary>
        public virtual ChunkTier TierFor(ChunkCoord coord, ChunkCoord playerChunk, StreamingContext ctx)
            => ChunkTier.HighDetail;
    }
}
