using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// The streaming + origin driver from the v2 "Floating Origin &amp; Terrain Streaming" design — a
    /// <b>MonoBehaviour</b> (not ECS), per World Engine v1.7. Each frame it asks the active
    /// <see cref="StreamingStrategy"/> which chunks it wants (the design's Chebyshev HD/Proxy rings),
    /// diffs that against what is loaded, and drives the chunk state machine. The actual load/unload
    /// work is delegated to an <see cref="IChunkLoader"/> (simulated here; Addressables in production),
    /// and lifecycle transitions are broadcast over the <c>*_Sig</c> ScriptableObject event bus.
    ///
    /// Renamed from <c>StreamingManager</c> to match the design's <c>ChunkStreamer</c>; the serialized
    /// field names are unchanged so existing scene wiring is preserved.
    /// </summary>
    public class ChunkStreamer : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private PlayerController player;
        [SerializeField] private SightCone sight;
        [Tooltip("Optional. Origin-shift owner; needed for Sovereign Jump.")]
        [SerializeField] private FloatingOrigin floatingOrigin;

        [Header("Design assets (optional — *_Cfg / *_Var / *_Sig / registry)")]
        [Tooltip("Designer world constants. Strategies that support it read ring radii from here.")]
        public World_Cfg config;
        [Tooltip("Runtime world state record (true pos, chunk index). Updated each frame when assigned.")]
        public WorldState_Var worldState;
        [Tooltip("Raised when a chunk reaches Loaded.")]
        public ChunkEvent_Sig onChunkLoaded_Sig;
        [Tooltip("Raised when a chunk reaches Unloaded.")]
        public ChunkEvent_Sig onChunkUnloaded_Sig;
        [Tooltip("Sovereign Jump (fast-travel) target table.")]
        public LocationRegistry_SO locations;

        [Header("Strategies (ScriptableObject assets)")]
        public List<StreamingStrategy> strategies = new();
        [Min(0)] public int startIndex = 0;

        [Header("Simulated load timing")]
        [Tooltip("Seconds in the Loading state before a chunk becomes Loaded. 0 = instant.")]
        [Min(0f)] public float loadDelay = 0.6f;
        [Tooltip("Seconds in the Unloading state before a chunk becomes Unloaded. 0 = instant.")]
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

        // Chunks that are cooling down. Key = coord, Value = Time when it became unwanted.
        private readonly Dictionary<ChunkCoord, float> _unloadQueue = new();

        private IChunkLoader _loader;
        private StreamingSignature _lastSignature;
        private bool _initialized;

        public float ChunkSize => player != null ? player.chunkSize : 1f;

        /// <summary>Swap the loader (e.g. inject an AddressablesChunkLoader). Defaults to a simulated one.</summary>
        public IChunkLoader Loader
        {
            get => _loader;
            set => _loader = value;
        }

        void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();
            if (sight == null) sight = FindObjectOfType<SightCone>();
            if (floatingOrigin == null) floatingOrigin = FindObjectOfType<FloatingOrigin>();
            _loader ??= new SimulatedChunkLoader(this, () => loadDelay, () => unloadDelay);
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
            foreach (var coord in _chunks.Keys)
                _loader?.Abort(coord);
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

        /// <summary>
        /// Sovereign Jump (design §8): teleport to a registered location, recompute the origin offset
        /// so the player lands near physical origin, and stream the destination rings.
        /// </summary>
        public bool SovereignJump(string locationId)
        {
            if (locations == null || floatingOrigin == null || player == null) return false;
            if (!locations.TryGet(locationId, out Vector2 absolute)) return false;
            floatingOrigin.JumpToAbsolute(absolute);
            ResetStreaming();
            return true;
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
                Debug.LogError("[ChunkStreamer] Missing PlayerController or IStreamingStrategy.");
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

            ProcessUnloadQueue();
            PublishWorldState();
        }

        /// <summary>Mirror the player's absolute position / chunk index into the WorldState_Var record.</summary>
        private void PublishWorldState()
        {
            if (worldState == null || player == null) return;
            worldState.TrueWorldX = player.AbsoluteX;
            worldState.TrueWorldY = player.AbsoluteY;
            var c = player.CurrentChunk;
            worldState.ChunkIndex = new Vector2Int(c.cx, c.cy);
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

            // 1. Load (or re-tier) newly desired chunks.
            foreach (var coord in desired)
            {
                ChunkTier tier = _strategy.TierFor(coord, playerChunk, ctx);

                // Rescue a chunk that was queued for delayed unload.
                if (_unloadQueue.ContainsKey(coord))
                {
                    _unloadQueue.Remove(coord);
                    if (logStateChanges) Debug.Log($"[Chunk {coord}] Unload canceled (saved by user movement/rotation).");
                }

                var chunk = GetOrCreate(coord);
                chunk.Tier = tier; // a real loader would swap content on a tier change; the visualizer just recolors

                if (chunk.State == ChunkState.Loaded || chunk.State == ChunkState.Loading) continue;
                RequestLoad(chunk);
            }

            // 2. Queue undesired active chunks for delayed unload.
            var active = new List<ChunkCoord>(_chunks.Keys);
            foreach (var coord in active)
            {
                if (desired.Contains(coord)) continue;

                var st = _chunks[coord].State;
                if (st == ChunkState.Loaded || st == ChunkState.Loading)
                {
                    if (!_unloadQueue.ContainsKey(coord))
                        _unloadQueue[coord] = Time.time;
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
                    RequestUnload(coord);
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
                Debug.Log($"[Chunk {c.Coord}] -> {c.State} ({c.Tier})");
            ChunkStateChanged?.Invoke(c);
        }

        // --- chunk state machine, driven through the IChunkLoader seam ---

        private void RequestLoad(Chunk chunk)
        {
            ChunkCoord coord = chunk.Coord;
            chunk.SetState(ChunkState.Loading);
            _loader.Load(coord, chunk.Tier, () => OnLoadComplete(coord));
        }

        private void OnLoadComplete(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out var chunk) || chunk.State != ChunkState.Loading) return;
            chunk.SetState(ChunkState.Loaded);
            onChunkLoaded_Sig?.Raise(coord);
        }

        private void RequestUnload(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out var chunk)) return;
            _loader.Abort(coord); // release an in-flight load if it was still Loading
            chunk.SetState(ChunkState.Unloading);
            _loader.Unload(coord, () => OnUnloadComplete(coord));
        }

        private void OnUnloadComplete(ChunkCoord coord)
        {
            if (!_chunks.TryGetValue(coord, out var chunk)) return;
            chunk.SetState(ChunkState.Unloaded);
            _chunks.Remove(coord);
            onChunkUnloaded_Sig?.Raise(coord);
        }

        public ChunkState GetState(ChunkCoord coord)
            => _chunks.TryGetValue(coord, out var c) ? c.State : ChunkState.Unloaded;

        /// <summary>Current detail tier of a tracked chunk (defaults to high-detail when untracked).</summary>
        public ChunkTier GetTier(ChunkCoord coord)
            => _chunks.TryGetValue(coord, out var c) ? c.Tier : ChunkTier.HighDetail;

        void OnDrawGizmos()
        {
            if (!drawGizmos || !Application.isPlaying || player == null) return;
            float size = ChunkSize;
            foreach (var kv in _chunks)
            {
                if (_unloadQueue.ContainsKey(kv.Key))
                {
                    Gizmos.color = new Color(0.5f, 0.2f, 0.8f); // purple: unload buffer pending
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
                    // Dim the proxy ring so the HD/Proxy split is visible.
                    if (kv.Value.Tier == ChunkTier.Proxy) Gizmos.color *= 0.55f;
                }
                Vector2 c = player.ChunkCenterRendered(kv.Key);
                Gizmos.DrawWireCube(c, new Vector3(size * 0.96f, size * 0.96f, 0f));
            }
        }
    }
}
