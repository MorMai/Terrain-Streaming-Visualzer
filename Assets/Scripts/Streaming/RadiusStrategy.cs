using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Loads every chunk within a circular radius of the player (a "disc" window).
    /// Demonstrates that swapping strategies needs no manager/renderer changes.
    /// </summary>
    public class RadiusStrategy : MonoBehaviour, IStreamingStrategy
    {
        [Min(0)] public int radius = 2;

        public string DisplayName => $"Radius (R={radius})";
        public int SortOrder => 1;

        public IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            int r = radius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                    yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
        }
    }
}
