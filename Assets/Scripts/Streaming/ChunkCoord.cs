using System;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Integer coordinate of a single chunk (grid cell). The atomic unit of streaming.
    /// </summary>
    [Serializable]
    public struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public int cx;
        public int cy;

        public ChunkCoord(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }

        /// <summary>World position -> chunk coord using floor division.</summary>
        public static ChunkCoord FromWorld(Vector2 worldPos, float chunkSize)
        {
            return new ChunkCoord(
                Mathf.FloorToInt(worldPos.x / chunkSize),
                Mathf.FloorToInt(worldPos.y / chunkSize));
        }

        /// <summary>Center of this chunk in world space.</summary>
        public Vector2 ToWorldCenter(float chunkSize)
        {
            return new Vector2((cx + 0.5f) * chunkSize, (cy + 0.5f) * chunkSize);
        }

        /// <summary>Bottom-left corner of this chunk in world space.</summary>
        public Vector2 ToWorldMin(float chunkSize)
        {
            return new Vector2(cx * chunkSize, cy * chunkSize);
        }

        public bool Equals(ChunkCoord other) => cx == other.cx && cy == other.cy;
        public override bool Equals(object obj) => obj is ChunkCoord o && Equals(o);
        public override int GetHashCode() => unchecked((cx * 397) ^ cy);
        public override string ToString() => $"({cx},{cy})";

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
    }
}
