using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Pure view. Spawns a pooled grid of cell sprites around the player and colors each by its
    /// chunk's load state. Subscribes to StreamingManager.ChunkStateChanged and never asks WHY a
    /// chunk changed — satisfying the visualization/logic decoupling (NFR-1).
    /// </summary>
    public class ChunkGridRenderer : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [SerializeField] private StreamingManager manager;
        [SerializeField] private PlayerController player;

        [Header("Sprites")]
        [Tooltip("Sprite used for each chunk cell.")]
        public Sprite cellSprite;
        [Tooltip("Optional backdrop sprite tiled behind the cells.")]
        public Sprite backgroundSprite;

        [Header("View")]
        [Tooltip("Half-extent of visible cells around the player, in chunks.")]
        [Min(1)] public int viewRadius = 4;
        [Range(0f, 0.4f)] public float cellGap = 0.06f;
        public int sortingOrder = 0;

        [Header("State colors")]
        public Color unloadedColor = new Color(0.22f, 0.22f, 0.25f, 1f);
        public Color loadingColor = new Color(0.95f, 0.78f, 0.20f, 1f);
        public Color loadedColor = new Color(0.25f, 0.85f, 0.35f, 1f);
        public Color unloadingColor = new Color(0.95f, 0.45f, 0.15f, 1f);

        private readonly Dictionary<ChunkCoord, SpriteRenderer> _cells = new();
        private readonly Stack<SpriteRenderer> _pool = new();
        private SpriteRenderer _background;
        private Transform _cellRoot;
        private float _cellSpriteWorldSize = 1f;
        private ChunkCoord _lastCenter;
        private bool _started;

        void Awake()
        {
            if (manager == null) manager = FindObjectOfType<StreamingManager>();
            if (player == null) player = FindObjectOfType<PlayerController>();

            _cellRoot = new GameObject("Cells").transform;
            _cellRoot.SetParent(transform, false);

            if (cellSprite != null)
                _cellSpriteWorldSize = cellSprite.bounds.size.x;

            if (backgroundSprite != null)
            {
                var bgGo = new GameObject("Background");
                bgGo.transform.SetParent(transform, false);
                _background = bgGo.AddComponent<SpriteRenderer>();
                _background.sprite = backgroundSprite;
                _background.drawMode = SpriteDrawMode.Tiled;
                _background.tileMode = SpriteTileMode.Continuous;
                _background.color = new Color(1f, 1f, 1f, 0.18f);
                _background.sortingOrder = sortingOrder - 1;
            }
        }

        void OnEnable()
        {
            if (manager != null) manager.ChunkStateChanged += OnChunkStateChanged;
        }

        void OnDisable()
        {
            if (manager != null) manager.ChunkStateChanged -= OnChunkStateChanged;
        }

        void Start()
        {
            _lastCenter = player != null ? player.CurrentChunk : default;
            RebuildVisible(_lastCenter);
            _started = true;
        }

        void LateUpdate()
        {
            if (player == null) return;
            ChunkCoord center = player.CurrentChunk;
            if (center != _lastCenter || !_started)
            {
                _lastCenter = center;
                RebuildVisible(center);
                _started = true;
            }
            UpdateBackground(center);
        }

        private void UpdateBackground(ChunkCoord center)
        {
            if (_background == null) return;
            float size = manager != null ? manager.ChunkSize : 1f;
            Vector2 c = center.ToWorldCenter(size);
            _background.transform.position = new Vector3(c.x, c.y, 0.2f);
            float span = (viewRadius * 2 + 3) * size;
            _background.size = new Vector2(span, span);
        }

        /// <summary>Ensure exactly the window of cells around <paramref name="center"/> exists; recycle the rest.</summary>
        private void RebuildVisible(ChunkCoord center)
        {
            float size = manager != null ? manager.ChunkSize : 1f;

            // Recycle cells outside the new window.
            var toRemove = new List<ChunkCoord>();
            foreach (var kv in _cells)
            {
                if (Mathf.Abs(kv.Key.cx - center.cx) > viewRadius ||
                    Mathf.Abs(kv.Key.cy - center.cy) > viewRadius)
                    toRemove.Add(kv.Key);
            }
            foreach (var coord in toRemove)
            {
                Recycle(_cells[coord]);
                _cells.Remove(coord);
            }

            // Ensure cells inside the window exist and are colored.
            for (int dy = -viewRadius; dy <= viewRadius; dy++)
            for (int dx = -viewRadius; dx <= viewRadius; dx++)
            {
                var coord = new ChunkCoord(center.cx + dx, center.cy + dy);
                if (!_cells.TryGetValue(coord, out var sr))
                {
                    sr = Obtain();
                    Place(sr, coord, size);
                    _cells[coord] = sr;
                }
                Recolor(coord, sr);
            }
        }

        private void Place(SpriteRenderer sr, ChunkCoord coord, float size)
        {
            Vector2 c = coord.ToWorldCenter(size);
            sr.transform.position = new Vector3(c.x, c.y, 0.1f);
            float target = size * (1f - cellGap);
            float scale = _cellSpriteWorldSize > 0f ? target / _cellSpriteWorldSize : 1f;
            sr.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void OnChunkStateChanged(Chunk chunk)
        {
            if (_cells.TryGetValue(chunk.Coord, out var sr))
                sr.color = ColorFor(chunk.State);
        }

        private void Recolor(ChunkCoord coord, SpriteRenderer sr)
        {
            sr.color = ColorFor(manager != null ? manager.GetState(coord) : ChunkState.Unloaded);
        }

        private Color ColorFor(ChunkState state) => state switch
        {
            ChunkState.Loading => loadingColor,
            ChunkState.Loaded => loadedColor,
            ChunkState.Unloading => unloadingColor,
            _ => unloadedColor,
        };

        private SpriteRenderer Obtain()
        {
            SpriteRenderer sr = _pool.Count > 0 ? _pool.Pop() : NewCell();
            sr.gameObject.SetActive(true);
            return sr;
        }

        private void Recycle(SpriteRenderer sr)
        {
            sr.gameObject.SetActive(false);
            _pool.Push(sr);
        }

        private SpriteRenderer NewCell()
        {
            var go = new GameObject("Cell");
            go.transform.SetParent(_cellRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cellSprite;
            sr.sortingOrder = sortingOrder;
            return sr;
        }
    }
}
