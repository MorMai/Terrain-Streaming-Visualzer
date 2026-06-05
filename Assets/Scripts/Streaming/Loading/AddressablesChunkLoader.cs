// Real Addressables loader implementing the v2 design §5.4 chunk state machine.
//
// This is the production drop-in for SimulatedChunkLoader. It is compiled ONLY when the
// STREAMING_ADDRESSABLES scripting define symbol is set AND com.unity.addressables is installed,
// so the visualizer keeps building with zero extra packages. To enable it:
//   1. Window > Package Manager > install "Addressables".
//   2. Project Settings > Player > Scripting Define Symbols > add STREAMING_ADDRESSABLES.
//   3. Author HD_Chunk_X_Y / Proxy prefabs and mark them Addressable; map keys via ChunkDef assets.
//   4. Construct one of these and assign it to ChunkStreamer (replacing the simulated loader).
#if STREAMING_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace LevelStreaming
{
    /// <summary>
    /// Streams chunk prefabs via Addressables. Integer chunk addressing is precision-immune, so an
    /// origin shift mid-load can never misplace a chunk: the placement callback runs against the
    /// CURRENT offset on completion (design §5.5 "offset snapshot").
    /// </summary>
    public class AddressablesChunkLoader : IChunkLoader
    {
        private readonly Func<ChunkCoord, ChunkTier, string> _keyResolver;
        private readonly Func<ChunkCoord, Vector3> _renderPos; // chunkRenderPos = chunkWorldPos - CumulativeOffset
        private readonly Transform _parent;

        private readonly Dictionary<ChunkCoord, AsyncOperationHandle<GameObject>> _handles = new();

        public AddressablesChunkLoader(
            Func<ChunkCoord, ChunkTier, string> keyResolver,
            Func<ChunkCoord, Vector3> renderPos,
            Transform parent)
        {
            _keyResolver = keyResolver;
            _renderPos = renderPos;
            _parent = parent;
        }

        public void Load(ChunkCoord coord, ChunkTier tier, Action onComplete)
        {
            Abort(coord);
            string key = _keyResolver(coord, tier);
            if (string.IsNullOrEmpty(key)) { onComplete?.Invoke(); return; }

            var handle = Addressables.InstantiateAsync(key, _renderPos(coord), Quaternion.identity, _parent);
            _handles[coord] = handle;
            handle.Completed += op =>
            {
                if (!_handles.TryGetValue(coord, out var h) || !h.Equals(op)) return; // aborted/superseded
                if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                    op.Result.transform.position = _renderPos(coord); // re-place with current offset
                onComplete?.Invoke();
            };
        }

        public void Unload(ChunkCoord coord, Action onComplete)
        {
            if (_handles.TryGetValue(coord, out var handle))
            {
                _handles.Remove(coord);
                if (handle.IsValid()) Addressables.ReleaseInstance(handle);
            }
            onComplete?.Invoke();
        }

        public void Abort(ChunkCoord coord)
        {
            if (_handles.TryGetValue(coord, out var handle))
            {
                _handles.Remove(coord);
                if (handle.IsValid()) Addressables.ReleaseInstance(handle); // releases an in-flight handle too
            }
        }
    }
}
#endif
