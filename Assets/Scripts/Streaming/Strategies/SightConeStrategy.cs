using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Sight-driven streaming (SRS 4.3): loads only chunks the cone can currently see, plus the
    /// player's own chunk. Combine with the Mouse-aim toggle to "paint" loaded chunks with the cursor.
    /// </summary>
    [CreateAssetMenu(menuName = "Level Streaming/Strategy/Sight Cone", fileName = "SightConeStrategy")]
    public class SightConeStrategy : StreamingStrategy
    {
        [Header("Recompute sensitivity")]
        [Tooltip("World-space movement bucket: the cone re-streams when the player moves this far.")]
        [Min(0.01f)] public float positionStep = 0.2f;
        [Tooltip("Facing bucket in degrees: the cone re-streams when its aim rotates this much.")]
        [Min(0.5f)] public float angleStep = 4f;

        protected override string DefaultName => "Sight Cone";

        // Re-stream as the cone is aimed/moved, not just on chunk crossings.
        public override StreamingSignature GetSignature(ChunkCoord playerChunk, StreamingContext ctx)
        {
            SightCone sight = ctx.Sight;
            if (sight == null) return base.GetSignature(playerChunk, ctx);

            int qx = Mathf.RoundToInt(ctx.PlayerWorldPos.x / positionStep);
            int qy = Mathf.RoundToInt(ctx.PlayerWorldPos.y / positionStep);

            Vector2 f = sight.CurrentFacing;
            float angle = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg;
            int qa = Mathf.RoundToInt(angle / angleStep);

            return new StreamingSignature(qx, qy, qa, 0);
        }

        public override IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
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
