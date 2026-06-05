using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// The v2 design's core streaming policy (§5.2): two concentric <b>Chebyshev (square)</b> rings
    /// centred on the player's chunk.
    /// <list type="bullet">
    /// <item>HD window: <c>Chebyshev(coord, player) &lt;= HighDetailRadius</c> (radius 1 =&gt; 3x3) — high-detail content.</item>
    /// <item>Proxy ring: out to <c>HighDetailRadius + ProxyRingDepth</c> — low-poly horizon content.</item>
    /// <item>Beyond: unloaded.</item>
    /// </list>
    /// Radii are read from a <see cref="World_Cfg"/> asset when assigned, otherwise from the local
    /// fallbacks below — so the same strategy honours the designer config yet still works standalone
    /// in the visualizer.
    /// </summary>
    [CreateAssetMenu(menuName = "Level Streaming/Strategy/Chebyshev Ring (HD + Proxy)", fileName = "ChebyshevRingStrategy")]
    public class ChebyshevRingStrategy : StreamingStrategy
    {
        [Header("World config (optional — overrides the fallbacks below)")]
        [Tooltip("If assigned, HighDetailRadius / ProxyRingDepth come from this World_Cfg asset.")]
        public World_Cfg config;

        [Header("Fallback radii (used when no World_Cfg is assigned)")]
        [Tooltip("High-detail Chebyshev radius in chunks. 1 => 3x3 HD window.")]
        [Min(0)] public int highDetailRadius = 1;
        [Tooltip("Extra proxy rings beyond the HD window.")]
        [Min(0)] public int proxyRingDepth = 2;

        private int HD => config != null ? config.HighDetailRadius : highDetailRadius;
        private int Outer => config != null ? config.OuterRingRadius : highDetailRadius + proxyRingDepth;

        protected override string DefaultName => $"Chebyshev HD{HD} + Proxy{Outer - HD}";

        // Re-stream on chunk crossings AND when the radii change (live config tuning).
        public override StreamingSignature GetSignature(ChunkCoord playerChunk, StreamingContext ctx)
            => new StreamingSignature(playerChunk.cx, playerChunk.cy, HD, Outer);

        public override IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx)
        {
            int outer = Outer;
            for (int dy = -outer; dy <= outer; dy++)
            for (int dx = -outer; dx <= outer; dx++)
                yield return new ChunkCoord(playerChunk.cx + dx, playerChunk.cy + dy);
        }

        public override ChunkTier TierFor(ChunkCoord coord, ChunkCoord playerChunk, StreamingContext ctx)
        {
            int cheb = Mathf.Max(Mathf.Abs(coord.cx - playerChunk.cx), Mathf.Abs(coord.cy - playerChunk.cy));
            return cheb <= HD ? ChunkTier.HighDetail : ChunkTier.Proxy;
        }
    }
}
