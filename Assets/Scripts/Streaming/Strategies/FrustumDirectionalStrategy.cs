using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Advanced directional frustum strategy modeled after World Streamer 2.
    /// Tracks look-angles, position shifts, and inspector configuration changes 
    /// to immediately force streaming updates during live visualization tweaking.
    /// </summary>
    [CreateAssetMenu(menuName = "Level Streaming/Strategy/Frustum Directional", fileName = "FrustumDirectionalStrategy")]
    public class FrustumDirectionalStrategy : StreamingStrategy
    {
        [Header("Directional Bias Weight")]
        [Tooltip("Pushes the scanning center forward along the viewing vector to pre-cache chunks ahead of the player's movement.")]
        [Min(0f)] public float forwardBias = 5f;

        [Header("Safety Margin")]
        [Tooltip("A strict block radius around the player that always stays loaded to avoid instant clipping when stepping backward.")]
        [Min(0)] public int safetyRadius = 1;

        [Header("Recompute sensitivity")]
        [Tooltip("World-space distance bucket: re-streams when player shifts this far.")]
        [Min(0.01f)] public float positionStep = 0.2f;
        [Tooltip("Rotational sensitivity bucket in degrees: re-streams when looking around.")]
        [Min(0.5f)] public float angleStep = 3.0f;

        protected override string DefaultName => "Frustum Directional (WS2 style)";

        public override StreamingSignature GetSignature(ChunkCoord playerChunk, StreamingContext ctx)
        {
            SightCone sight = ctx.Sight;
            if (sight == null) return base.GetSignature(playerChunk, ctx);

            // 1. Quantize position shifts
            int qx = Mathf.RoundToInt(ctx.PlayerWorldPos.x / positionStep);
            int qy = Mathf.RoundToInt(ctx.PlayerWorldPos.y / positionStep);

            // 2. Quantize camera orientation angles
            Vector2 forwardDir = sight.CurrentFacing;
            float currentAngle = Mathf.Atan2(forwardDir.y, forwardDir.x) * Mathf.Rad2Deg;
            int qa = Mathf.RoundToInt(currentAngle / angleStep);

            // 3. CRITICAL: Mix in configuration settings to force a refresh on inspector changes!
            // We use GetHashCode of the values combined with our settings to create a reactive key.
            int configHash = forwardBias.GetHashCode() ^ safetyRadius.GetHashCode() ^ sight.viewDistance.GetHashCode();

            return new StreamingSignature(qx, qy, qa, configHash);
        }

        public override IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            // Secure immediate safety grid around the player
            for (int dy = -safetyRadius; dy <= safetyRadius; dy++)
            {
                for (int dx = -safetyRadius; dx <= safetyRadius; dx++)
                {
                    yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
                }
            }

            SightCone sight = ctx.Sight;
            if (sight == null) yield break;

            float size = ctx.ChunkSize > 0f ? ctx.ChunkSize : 1f;

            // Calculate search radius based on the actual sight view distance plus our forward weight bias
            int searchRange = Mathf.CeilToInt((sight.viewDistance + forwardBias) / size) + 1;

            // Project an evaluation center forward based on where the player is currently aiming
            Vector2 lookDir = sight.CurrentFacing.normalized;
            Vector2 biasedWorldPos = ctx.PlayerWorldPos + (lookDir * forwardBias);

            // Convert that biased position back to a grid chunk coordinate to look ahead
            int biasedCx = Mathf.FloorToInt(biasedWorldPos.x / size);
            int biasedCy = Mathf.FloorToInt(biasedWorldPos.y / size);

            // Scan a grid outward from our predictive biased look-ahead center
            for (int dy = -searchRange; dy <= searchRange; dy++)
            {
                for (int dx = -searchRange; dx <= searchRange; dx++)
                {
                    ChunkCoord targetCoord = new ChunkCoord(biasedCx + dx, biasedCy + dy);

                    // Skip if this chunk falls inside our safety radius zone (already yielded above)
                    if (Mathf.Abs(targetCoord.cx - playerChunk.cx) <= safetyRadius &&
                        Mathf.Abs(targetCoord.cy - playerChunk.cy) <= safetyRadius)
                    {
                        continue;
                    }

                    // Hand off evaluation cleanly to your game framework's native rules
                    if (sight.IsChunkInSight(targetCoord))
                    {
                        yield return targetCoord;
                    }
                }
            }
        }
    }
}