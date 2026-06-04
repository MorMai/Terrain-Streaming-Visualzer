using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Context handed to a strategy so it can make richer decisions without the
    /// StreamingManager hard-coding any particular policy. Extend freely; existing
    /// strategies ignore fields they don't need.
    /// </summary>
    public struct StreamingContext
    {
        public float ChunkSize;
        /// <summary>Optional sight cone, present if a SightCone exists in the scene.</summary>
        public SightCone Sight;
        /// <summary>Player's continuous world position (for strategies that need sub-chunk precision).</summary>
        public Vector2 PlayerWorldPos;
    }

    /// <summary>
    /// A small, equatable value a strategy returns to say "my desired set is unchanged while this
    /// is unchanged." The StreamingManager re-streams only when the signature changes, so static
    /// strategies (chunk-based) recompute on boundary crossings while dynamic ones (sight cone)
    /// recompute as their inputs — aim, position — change.
    /// </summary>
    public readonly struct StreamingSignature : IEquatable<StreamingSignature>
    {
        public readonly int A, B, C, D;
        public StreamingSignature(int a, int b, int c, int d) { A = a; B = b; C = c; D = d; }

        public bool Equals(StreamingSignature o) => A == o.A && B == o.B && C == o.C && D == o.D;
        public override bool Equals(object o) => o is StreamingSignature s && Equals(s);
        public override int GetHashCode() => unchecked((((A * 397) ^ B) * 397 ^ C) * 397 ^ D);
    }

    /// <summary>
    /// Pluggable decision: "which chunks should be loaded right now?". The StreamingManager
    /// consumes ONLY this interface and diffs the result against what is currently loaded,
    /// so swapping strategies (radius, sight-based, predictive, priority...) needs no change
    /// to the manager or any renderer.
    /// </summary>
    public interface IStreamingStrategy
    {
        /// <summary>Human-readable name shown in the debug UI title.</summary>
        string DisplayName { get; }

        IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, StreamingContext ctx);

        /// <summary>
        /// Recompute trigger. The manager re-streams whenever this value changes. Chunk-based
        /// strategies return something derived from <paramref name="playerChunk"/> (so they only
        /// recompute on boundary crossings); aim-based strategies fold in their facing/position.
        /// </summary>
        StreamingSignature GetSignature(ChunkCoord playerChunk, StreamingContext ctx);
    }
}
