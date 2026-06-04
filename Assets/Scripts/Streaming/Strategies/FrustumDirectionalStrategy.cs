using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Advanced directional frustum strategy modeled after World Streamer 2.
    /// Uses a combined viewing angle, forward direction projection, and falloff radius
    /// to prioritize streaming chunks ahead of the camera while aggressively discarding rear chunks.
    /// </summary>
    [CreateAssetMenu(menuName = "Level Streaming/Strategy/Frustum Directional", fileName = "FrustumDirectionalStrategy")]
    public class FrustumDirectionalStrategy : StreamingStrategy
    {
        [Header("Frustum / Cone settings")]
        [Tooltip("Maximum visual depth distance in world units to stream chunks.")]
        [Min(1f)] public float viewDistance = 15f;
        [Tooltip("The angle width (FOV) in degrees facing forward to load chunks.")]
        [Range(10f, 180f)] public float fieldOfView = 90f;

        [Header("Directional Weight")]
        [Tooltip("Pushes the loading center forward along the viewing vector to pre-cache chunks ahead of the player.")]
        [Min(0f)] public float forwardBias = 3f;

        [Header("Safety Margin")]
        [Tooltip("A strict block radius behind/around the player that always stays loaded to prevent instant edge clipping.")]
        [Min(0)] public int safetyRadius = 1;

        [Header("Recompute sensitivity")]
        [Tooltip("World-space distance bucket: re-streams when player shifts this far.")]
        [Min(0.01f)] public float positionStep = 0.2f;
        [Tooltip("Rotational sensitivity bucket in degrees: re-streams when looking around.")]
        [Min(0.5f)] public float angleStep = 3.0f;

        protected override string DefaultName => "Frustum Directional (WS2 style)";

        /// <summary>
        /// Recalculates whenever the player shifts past position thresholds or shifts their orientation angle.
        /// </summary>
        public override StreamingSignature GetSignature(ChunkCoord playerChunk, StreamingContext ctx)
        {
            SightCone sight = ctx.Sight;
            if (sight == null) return base.GetSignature(playerChunk, ctx);

            // Grid coordinate quantizations
            int qx = Mathf.RoundToInt(ctx.PlayerWorldPos.x / positionStep);
            int qy = Mathf.RoundToInt(ctx.PlayerWorldPos.y / positionStep);

            // Angular quantization
            Vector2 forwardDir = sight.CurrentFacing;
            float currentAngle = Mathf.Atan2(forwardDir.y, forwardDir.x) * Mathf.Rad2Deg;
            int qa = Mathf.RoundToInt(currentAngle / angleStep);

            return new StreamingSignature(qx, qy, qa, 1);
        }

        public override IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            SightCone sight = ctx.Sight;
            float size = ctx.ChunkSize > 0f ? ctx.ChunkSize : 1f;

            // Step 1: Secure immediate safety grid around player (e.g. 3x3 if safetyRadius is 1)
            for (int dy = -safetyRadius; dy <= safetyRadius; dy++)
            {
                for (int dx = -safetyRadius; dx <= safetyRadius; dx++)
                {
                    yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
                }
            }

            if (sight == null) yield break;

            // Step 2: Establish the biased focal center and scanning boundary limits
            Vector2 lookDir = sight.CurrentFacing.normalized;
            Vector2 biasedOrigin = ctx.PlayerWorldPos + (lookDir * forwardBias);

            // Total maximum bounding grid check derived from visual range depth + directional bias extension
            int searchRange = Mathf.CeilToInt((viewDistance + forwardBias) / size) + 1;
            float halfFov = fieldOfView * 0.5f;

            // Step 3: Run the directional / frustum cone scan loop
            for (int dy = -searchRange; dy <= searchRange; dy++)
            {
                for (int dx = -searchRange; dx <= searchRange; dx++)
                {
                    // Skip immediate blocks we already yielded inside the safety pass
                    if (Mathf.Abs(dx) <= safetyRadius && Mathf.Abs(dy) <= safetyRadius)
                        continue;

                    ChunkCoord targetCoord = new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
                    Vector2 targetWorldPos = targetCoord.ToWorldCenter(size);

                    // Vector originating from the player to the destination chunk center
                    Vector2 toChunk = targetWorldPos - ctx.PlayerWorldPos;
                    float distance = toChunk.magnitude;

                    // Out of streaming depth window cut-off
                    if (distance > viewDistance + forwardBias)
                        continue;

                    // Directional calculation: Determine angle discrepancy between player gaze and chunk orientation
                    float angleToChunk = Vector2.Angle(lookDir, toChunk);

                    // If it fits within the specified sight cone / frustum slice, cache it
                    if (angleToChunk <= halfFov)
                    {
                        yield return targetCoord;
                    }
                }
            }
        }
    }
}