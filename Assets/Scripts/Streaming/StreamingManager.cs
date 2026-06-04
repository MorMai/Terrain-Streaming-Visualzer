using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Heart of the system. Detects when the player crosses a chunk boundary, asks the active
    /// IStreamingStrategy which chunks it wants, diffs that against what is currently loaded/loading,
    /// and drives simulated load/unload lifecycles. Knows nothing about rendering: it only raises
    /// <see cref="ChunkStateChanged"/>. Recomputes ONLY on boundary crossings (NFR-3), no per-frame work.
    /// </summary>
    public class StreamingManager : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private PlayerController player;
        [SerializeField] private SightCone sight;

        [Header("Strategies (ScriptableObject assets)")]
        [Tooltip("Ordered list of streaming-strategy assets. Cycle with Back/Next in the order shown here. " +
                 "Create assets via the Project window: Create > Level Streaming > Strategy > ...")]
        public List<StreamingStrategy> strategies = new();
        [Tooltip("Which strategy in the list is active on start.")]
        [Min(0)] public int startIndex = 0;

        [Header("Simulated load timing")]
        [Min(0f)] public float loadDelay = 0.6f;
        [Min(0f)] public float unloadDelay = 0.25f;

        [Header("Debug")]
        public bool logStateChanges = true;
        public bool drawGizmos = true;

        /// <summary>Raised after any chunk changes state. The renderer subscribes to this.</summary>
        public event Action<Chunk> ChunkStateChanged;

        /// <summary>Raised after the active strategy changes (e.g. via Back/Next).</summary>
        public event Action StrategyChanged;

        /// <summary>Raised when the world is reset (e.g. chunk size changed). Views drop and rebuild.</summary>
        public event Action WorldReset;

        private readonly List<IStreamingStrategy> _strategies = new();
        private int _activeIndex;
        private IStreamingStrategy _strategy;
        private readonly Dictionary<ChunkCoord, Chunk> _chunks = new();
        private readonly Dictionary<ChunkCoord, Coroutine> _running = new();
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
                if (s != null) _strategies.Add(s); // skip empty slots in the asset list

            _activeIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, _strategies.Count - 1));
            _strategy = _strategies.Count > 0 ? _strategies[_activeIndex] : null;
        }

        // ---- Strategy cycling (driven by the debug UI) -------------------------------
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
                Recompute(player.CurrentChunk, ctx); // re-stream under the new policy
            }
            StrategyChanged?.Invoke();
        }

        // ---- World reset (for changes that can't apply live, e.g. chunk size) --------

        /// <summary>
        /// Apply a new world chunk size. Because every world&lt;-&gt;chunk coordinate depends on it,
        /// this can't change live: the player is recentered and the whole simulation is reset.
        /// </summary>
        public void SetChunkSize(float newSize)
        {
            if (player != null)
            {
                player.chunkSize = Mathf.Max(0.01f, newSize);
                float z = player.transform.position.z;
                player.transform.position =
                    new Vector3(player.chunkSize * 0.5f, player.chunkSize * 0.5f, z); // center of chunk (0,0)
            }
            ResetStreaming();
        }

        /// <summary>Stop everything, clear all chunks, and re-stream the initial window from scratch.</summary>
        public void ResetStreaming()
        {
            foreach (var co in _running.Values)
                if (co != null) StopCoroutine(co);
            _running.Clear();
            _chunks.Clear();

            WorldReset?.Invoke(); // tell the renderer to drop its cells and rebuild at the new size

            if (_initialized && _strategy != null && player != null)
            {
                var ctx = BuildContext();
                _lastSignature = _strategy.GetSignature(player.CurrentChunk, ctx);
                Recompute(player.CurrentChunk, ctx);
            }
        }

        // ---- Debug counters ----------------------------------------------------------
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
            Recompute(player.CurrentChunk, ctx); // initial window
        }

        void Update()
        {
            if (!_initialized || _strategy == null) return;

            // Re-stream only when the active strategy's signature changes. For chunk-based
            // strategies that's a boundary crossing; for the sight cone it's a change in aim/position.
            var ctx = BuildContext();
            var sig = _strategy.GetSignature(player.CurrentChunk, ctx);
            if (!sig.Equals(_lastSignature))
            {
                _lastSignature = sig;
                Recompute(player.CurrentChunk, ctx);
            }
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

            // Load newly desired chunks not already loaded/loading.
            foreach (var coord in desired)
            {
                var st = GetState(coord);
                if (st == ChunkState.Loaded || st == ChunkState.Loading) continue; // no redundant reloads
                RequestLoad(coord);
            }

            // Unload anything active that fell outside the window.
            var active = new List<ChunkCoord>(_chunks.Keys);
            foreach (var coord in active)
            {
                if (desired.Contains(coord)) continue;
                var st = _chunks[coord].State;
                if (st == ChunkState.Loaded || st == ChunkState.Loading)
                    RequestUnload(coord);
            }
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

        /// <summary>Current state of a chunk; Unloaded if never touched.</summary>
        public ChunkState GetState(ChunkCoord coord)
            => _chunks.TryGetValue(coord, out var c) ? c.State : ChunkState.Unloaded;

        void OnDrawGizmos()
        {
            if (!drawGizmos || !Application.isPlaying || player == null) return;
            float size = ChunkSize;
            foreach (var kv in _chunks)
            {
                switch (kv.Value.State)
                {
                    case ChunkState.Loading:   Gizmos.color = Color.yellow; break;
                    case ChunkState.Loaded:    Gizmos.color = Color.green;  break;
                    case ChunkState.Unloading: Gizmos.color = new Color(1f, 0.5f, 0f); break;
                    default:                   Gizmos.color = Color.gray;   break;
                }
                Vector2 c = kv.Key.ToWorldCenter(size);
                Gizmos.DrawWireCube(c, new Vector3(size * 0.96f, size * 0.96f, 0f));
            }
        }
    }
}
