using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Sight-driven streaming (SRS 4.3): loads only chunks the cone can currently see,
    /// plus the player's own chunk. Pair with the Mouse-aim toggle to "paint" loaded
    /// chunks with the cursor. Falls back to just the player chunk if no SightCone exists.
    /// </summary>
    public class SightStreamStrategy : MonoBehaviour, IStreamingStrategy
    {
        public string DisplayName => "Sight Cone";
        public int SortOrder => 2;

        public IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            yield return playerChunk; // keep the ground under the player loaded

            SightCone sight = ctx.Sight;
            if (sight == null) yield break;

            float size = ctx.ChunkSize > 0f ? ctx.ChunkSize : 1f;
            int r = Mathf.CeilToInt(sight.viewDistance / size) + 1;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var c = new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
                if (sight.IsChunkInSight(c)) yield return c;
            }
        }
    }
}
