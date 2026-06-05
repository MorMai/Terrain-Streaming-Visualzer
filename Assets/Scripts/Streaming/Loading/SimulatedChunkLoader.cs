using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Visualizer stand-in for real Addressables loading: simply waits a configurable delay to mimic
    /// async load/release latency, then fires the completion callback. The delays are pulled live via
    /// delegates so the streamer's inspector sliders keep working. Swap this for
    /// <c>AddressablesChunkLoader</c> (see the <c>STREAMING_ADDRESSABLES</c>-guarded file) to load
    /// authored prefabs for real.
    /// </summary>
    public class SimulatedChunkLoader : IChunkLoader
    {
        private readonly MonoBehaviour _host;
        private readonly Func<float> _loadDelay;
        private readonly Func<float> _unloadDelay;
        private readonly Dictionary<ChunkCoord, Coroutine> _running = new();

        public SimulatedChunkLoader(MonoBehaviour coroutineHost, Func<float> loadDelay, Func<float> unloadDelay)
        {
            _host = coroutineHost;
            _loadDelay = loadDelay;
            _unloadDelay = unloadDelay;
        }

        public void Load(ChunkCoord coord, ChunkTier tier, Action onComplete)
            => Run(coord, _loadDelay != null ? _loadDelay() : 0f, onComplete);

        public void Unload(ChunkCoord coord, Action onComplete)
            => Run(coord, _unloadDelay != null ? _unloadDelay() : 0f, onComplete);

        public void Abort(ChunkCoord coord)
        {
            if (_running.TryGetValue(coord, out var co) && co != null)
                _host.StopCoroutine(co);
            _running.Remove(coord);
        }

        private void Run(ChunkCoord coord, float delay, Action onComplete)
        {
            Abort(coord); // one in-flight op per coord
            _running[coord] = _host.StartCoroutine(DelayRoutine(coord, delay, onComplete));
        }

        private IEnumerator DelayRoutine(ChunkCoord coord, float delay, Action onComplete)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            _running.Remove(coord);
            onComplete?.Invoke();
        }
    }
}
