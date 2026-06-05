using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Per-chunk authoring metadata (the <c>*_Def</c> convention from the v2 design): the Addressable
    /// key to instantiate for this grid cell plus its content tier. Deliberately holds <b>no</b>
    /// procedural-noise parameters — content is authored art streamed via Addressables, not generated.
    ///
    /// In the simulated visualizer these are optional; a real <c>AddressablesChunkLoader</c> resolves
    /// a chunk's key/tier from the matching <see cref="ChunkDef"/> (looked up by <see cref="chunkIndex"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "World Engine/ChunkDef", fileName = "ChunkDef")]
    public class ChunkDef : ScriptableObject
    {
        [Tooltip("Grid coordinate this definition describes.")]
        public Vector2Int chunkIndex;

        [Tooltip("Addressables key (e.g. HD_Chunk_3_7 or a shared Proxy prefab key).")]
        public string addressableKey;

        [Tooltip("Default content tier for this chunk.")]
        public ChunkTier tier = ChunkTier.HighDetail;
    }
}
