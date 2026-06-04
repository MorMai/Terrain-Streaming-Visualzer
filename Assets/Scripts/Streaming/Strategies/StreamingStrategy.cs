using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Base class for streaming strategies authored as ScriptableObject assets. Create concrete
    /// strategies via the Project window (Create > Level Streaming > Strategy > ...), tune them in
    /// the Inspector, and drop them into the StreamingManager's Strategies list to add/remove/reorder.
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
    }
}
