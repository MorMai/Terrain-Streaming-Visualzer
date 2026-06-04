using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>Loads every chunk within a circular radius of the player (a "disc" window).</summary>
    [CreateAssetMenu(menuName = "Level Streaming/Strategy/Radius", fileName = "RadiusStrategy")]
    public class RadiusStrategy : StreamingStrategy
    {
        [Min(0)] public int radius = 2;

        protected override string DefaultName => $"Radius (R={radius})";

        public override IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            int r = radius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                    yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
        }
    }
}
