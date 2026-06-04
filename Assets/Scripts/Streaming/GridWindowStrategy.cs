using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Default strategy: a square window of chunks centered on the player.
    /// radius = 1 yields the spec's 3x3 window; any radius is supported.
    /// </summary>
    public class GridWindowStrategy : MonoBehaviour, IStreamingStrategy
    {
        [Tooltip("Window half-extent in chunks. 1 => 3x3, 2 => 5x5, ...")]
        [Min(0)] public int radius = 1;

        public string DisplayName => $"Grid Window {2 * radius + 1}×{2 * radius + 1}";
        public int SortOrder => 0;

        public IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
        }
    }
}
