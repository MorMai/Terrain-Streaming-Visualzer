using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>Square window of chunks centered on the player. radius 1 => 3x3 (the spec default).</summary>
    [CreateAssetMenu(menuName = "Level Streaming/Strategy/Grid Window", fileName = "GridWindowStrategy")]
    public class GridWindowStrategy : StreamingStrategy
    {
        [Tooltip("Window half-extent in chunks. 1 => 3x3, 2 => 5x5, ...")]
        [Min(0)] public int radius = 1;

        protected override string DefaultName => $"Grid Window {2 * radius + 1}×{2 * radius + 1}";

        public override IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
        }
    }
}
