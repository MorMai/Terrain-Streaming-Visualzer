using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Heart of the system. Detects when the player crosses a chunk boundary, asks the active
    /// IStreamingStrategy which chunks it wants, diffs that against what is currently loaded/loading,
    /// and drives simulated load/unload lifecycles.
    /// </summary>
    public class StreamingManager : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private PlayerController player;
        [SerializeField] private SightCone sight;

        [Header("Strategies (ScriptableObject assets)")]
        public List<StreamingStrategy> strategies = new();
        [Min(0)] public int startIndex = 0;

        [Header("Simulated load timing")]
        [Min(0f)] public float loadDelay = 0.6f;
        [Min(0f)] public float unloadDelay = 0.25f;

        [Header("Unload hysteresis")]
        [Tooltip("Time-based: an unwanted chunk waits this long (seconds) before it may unload.")]
        public bool useTimeHysteresis = true;
        [Min(0f)] public float unloadCooldown = 3.0f;
        [Tooltip("Distance-based: an unwanted chunk stays loaded until the player is at least this far (world units) from it.")]
        public bool useDistanceHysteresis = false;
        [Min(0f)] public float unloadDistance = 4.0f;

        [Header("Debug")]
        public bool logStateChanges = true;
        public bool drawGizmos = true;

        public event Action<Chunk> ChunkStateChanged;
        public event Action StrategyChanged;
        public event Action WorldReset;

        private readonly List<IStreamingStrategy> _strategies = new();
        private int _activeIndex;
        private IStreamingStrategy _strategy;
        private readonly Dictionary<ChunkCoord, Chunk> _chunks = new();
        private readonly Dictionary<ChunkCoord, Coroutine> _running = new();

        // Track chunks that are cooling down. Key = Chunk, Value = Time when it should actually unload.
        private readonly Dictionary<ChunkCoord, float> _unloadQueue = new();

        private StreamingSignature _lastSignature;
        private bool _initialized;

        public float ChunkSize => player != null ? player.chunkSize : 1f;

        void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();
            if (sight == null) sight = FindObjectOfType<SightCone>();
            BuildStrategyList();
        }

        private void BuildStrategyList()
        {
            _strategies.Clear();
            foreach (var s in strategies)
                if (s != null) _strategies.Add(s);

            _activeIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, _strategies.Count - 1));
            _strategy = _strategies.Count > 0 ? _strategies[_activeIndex] : null;
        }

        public int StrategyCount => _strategies.Count;
        public int ActiveStrategyIndex => _activeIndex;
        public string ActiveStrategyName => _strategy != null ? _strategy.DisplayName : "<none>";

        public void NextStrategy() => SetActiveStrategy(_activeIndex + 1);
        public void PrevStrategy() => SetActiveStrategy(_activeIndex - 1);

        public void SetActiveStrategy(int index)
        {
            if (_strategies.Count == 0) return;
            _activeIndex = ((index % _strategies.Count) + _strategies.Count) % _strategies.Count;
            _strategy = _strategies[_activeIndex];
            if (_initialized)
            {
                var ctx = BuildContext();
                _lastSignature = _strategy.GetSignature(player.CurrentChunk, ctx);
                Recompute(player.CurrentChunk, ctx);
            }
            StrategyChanged?.Invoke();
        }

        public void SetChunkSize(float newSize)
        {
            if (player != null)
            {
                player.chunkSize = Mathf.Max(0.01f, newSize);
                float z = player.transform.position.z;
                player.transform.position = new Vector3(player.chunkSize * 0.5f, player.chunkSize * 0.5f, z);
            }
            ResetStreaming();
        }

        public void ResetStreaming()
        {
            foreach (var co in _running.Values)
                if (co != null) StopCoroutine(co);
            _running.Clear();
            _chunks.Clear();
            _unloadQueue.Clear();

            WorldReset?.Invoke();

            if (_initialized && _strategy != null && player != null)
            {
                var ctx = BuildContext();
                _lastSignature = _strategy.GetSignature(player.CurrentChunk, ctx);
                Recompute(player.CurrentChunk, ctx);
            }
        }

        public int TrackedCount => _chunks.Count;
        public ChunkCoord PlayerChunk => player != null ? player.CurrentChunk : default;

        public int CountInState(ChunkState state)
        {
            int n = 0;
            foreach (var c in _chunks.Values)
                if (c.State == state) n++;
            return n;
        }

        void Start()
        {
            if (player == null || _strategy == null)
            {
                Debug.LogError("[StreamingManager] Missing PlayerController or IStreamingStrategy.");
                enabled = false;
                return;
            }
            _initialized = true;
            var ctx = BuildContext();
            _lastSignature = _strategy.GetSignature(player.CurrentChunk, ctx);
            Recompute(player.CurrentChunk, ctx);
        }

        void Update()
        {
            if (!_initialized || _strategy == null) return;

            var ctx = BuildContext();
            var sig = _strategy.GetSignature(player.CurrentChunk, ctx);
            if (!sig.Equals(_lastSignature))
            {
                _lastSignature = sig;
                Recompute(player.CurrentChunk, ctx);
            }

            // Process our time-delayed unloads
            ProcessUnloadQueue();
        }

        private StreamingContext BuildContext()
        {
            return new StreamingContext
            {
                ChunkSize = ChunkSize,
                Sight = sight,
                PlayerWorldPos = player != null ? player.WorldPos : Vector2.zero,
            };
        }

        private void Recompute(ChunkCoord playerChunk, StreamingContext ctx)
        {
            var desired = new HashSet<ChunkCoord>(_strategy.GetDesiredChunks(playerChunk, ctx));

            // 1. Load newly desired chunks
            foreach (var coord in desired)
            {
                // If this chunk was scheduled to be destroyed, rescue it!
                if (_unloadQueue.ContainsKey(coord))
                {
                    _unloadQueue.Remove(coord);
                    if (logStateChanges) Debug.Log($"[Chunk {coord}] Unload canceled (saved by user movement/rotation).");
                }

                var st = GetState(coord);
                if (st == ChunkState.Loaded || st == ChunkState.Loading) continue;
                RequestLoad(coord);
            }

            // 2. Queue undesired active chunks for delayed unload
            var active = new List<ChunkCoord>(_chunks.Keys);
            foreach (var coord in active)
            {
                if (desired.Contains(coord)) continue;

                var st = _chunks[coord].State;
                // Only queue chunks that are actually usable or currently trying to get loaded
                if (st == ChunkState.Loaded || st == ChunkState.Loading)
                {
                    if (!_unloadQueue.ContainsKey(coord))
                    {
                        _unloadQueue[coord] = Time.time; // remember WHEN it became unwanted
                    }
                }
            }
        }

        /// <summary>How many chunks are currently waiting (cooling down) to be unloaded.</summary>
        public int UnloadQueueCount => _unloadQueue.Count;

        private void ProcessUnloadQueue()
        {
            if (_unloadQueue.Count == 0) return;

            float now = Time.time;
            List<ChunkCoord> toUnload = null;

            foreach (var kvp in _unloadQueue)
            {
                ChunkCoord coord = kvp.Key;
                float queuedAt = kvp.Value;

                // A disabled condition never blocks the unload; an enabled one must be satisfied.
                bool timeReady = !useTimeHysteresis || (now - queuedAt >= unloadCooldown);
                bool distanceReady = !useDistanceHysteresis || DistanceReady(coord);

                if (timeReady && distanceReady)
                {
                    toUnload ??= new List<ChunkCoord>();
                    toUnload.Add(coord);
                }
            }

            if (toUnload != null)
            {
                foreach (var coord in toUnload)
                {
                    _unloadQueue.Remove(coord);
                    RequestUnload(coord); // execute the real unload lifecycle
                }
            }
        }

        // Distance measured in rendered space (translation-invariant, so floating-origin safe).
        private bool DistanceReady(ChunkCoord coord)
        {
            if (player == null) return true;
            float d = Vector2.Distance(player.WorldPos, player.ChunkCenterRendered(coord));
            return d >= unloadDistance;
        }

        private Chunk GetOrCreate(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                chunk = new Chunk(coord);
                chunk.StateChanged += OnChunkStateChanged;
                _chunks[coord] = chunk;
            }
            return chunk;
        }

        private void OnChunkStateChanged(Chunk c)
        {
            if (logStateChanges)
                Debug.Log($"[Chunk {c.Coord}] -> {c.State}");
            ChunkStateChanged?.Invoke(c);
        }

        private void RequestLoad(ChunkCoord coord)
        {
            var chunk = GetOrCreate(coord);
            StartLifecycle(coord, LoadRoutine(chunk));
        }

        private void RequestUnload(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out var chunk)) return;
            StartLifecycle(coord, UnloadRoutine(chunk));
        }

        private void StartLifecycle(ChunkCoord coord, IEnumerator routine)
        {
            if (_running.TryGetValue(coord, out var existing) && existing != null)
                StopCoroutine(existing);
            _running[coord] = StartCoroutine(routine);
        }

        private IEnumerator LoadRoutine(Chunk chunk)
        {
            chunk.SetState(ChunkState.Loading);
            if (loadDelay > 0f) yield return new WaitForSeconds(loadDelay);
            chunk.SetState(ChunkState.Loaded);
            _running.Remove(chunk.Coord);
        }

        private IEnumerator UnloadRoutine(Chunk chunk)
        {
            chunk.SetState(ChunkState.Unloading);
            if (unloadDelay > 0f) yield return new WaitForSeconds(unloadDelay);
            chunk.SetState(ChunkState.Unloaded);
            _running.Remove(chunk.Coord);
        }

        public ChunkState GetState(ChunkCoord coord)
            => _chunks.TryGetValue(coord, out var c) ? c.State : ChunkState.Unloaded;

        void OnDrawGizmos()
        {
            if (!drawGizmos || !Application.isPlaying || player == null) return;
            float size = ChunkSize;
            foreach (var kv in _chunks)
            {
                // Visual differentiator for chunks cooling down
                if (_unloadQueue.ContainsKey(kv.Key))
                {
                    Gizmos.color = new Color(0.5f, 0.2f, 0.8f); // Purple means "Unload Buffer Pending"
                }
                else
                {
                    switch (kv.Value.State)
                    {
                        case ChunkState.Loading: Gizmos.color = Color.yellow; break;
                        case ChunkState.Loaded: Gizmos.color = Color.green; break;
                        case ChunkState.Unloading: Gizmos.color = new Color(1f, 0.5f, 0f); break;
                        default: Gizmos.color = Color.gray; break;
                    }
                }
                Vector2 c = player.ChunkCenterRendered(kv.Key);
                Gizmos.DrawWireCube(c, new Vector3(size * 0.96f, size * 0.96f, 0f));
            }
        }
    }
}