using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>What each cell's text label shows (the "value" overlay).</summary>
    public enum CellLabelMode { None, Coordinates, State, Both }

    /// <summary>
    /// Pure view. Spawns a pooled grid of cell sprites around the player and colors each by its
    /// chunk's load state. Subscribes to StreamingManager.ChunkStateChanged and never asks WHY a
    /// chunk changed (NFR-1). Almost everything is exposed for tuning: colors, tint, smooth
    /// transitions, coordinate/state value labels, depths, gap, background and a player highlight.
    /// Most settings are applied live each frame, so you can tweak them in Play mode.
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
        [Tooltip("Shrinks each cell to leave a gap between them (0 = touching).")]
        [Range(0f, 0.45f)] public float cellGap = 0.06f;
        [Tooltip("Sorting order of the cell sprites. Background uses this -1, labels this +2.")]
        public int sortingOrder = 0;
        [Tooltip("Z depth the cells sit at (further from camera = larger Z).")]
        public float cellZ = 0.1f;
        [Tooltip("Uniform tint multiplied over every state color (white = no change).")]
        public Color cellTint = Color.white;

        [Header("State Colors")]
        public Color unloadedColor = new Color(0.22f, 0.22f, 0.25f, 1f);
        public Color loadingColor = new Color(0.95f, 0.78f, 0.20f, 1f);
        public Color loadedColor = new Color(0.25f, 0.85f, 0.35f, 1f);
        public Color unloadingColor = new Color(0.95f, 0.45f, 0.15f, 1f);

        [Header("Color Transitions")]
        [Tooltip("Fade cell colors instead of snapping on state change.")]
        public bool smoothColor = true;
        [Min(0.1f)] public float colorLerpSpeed = 8f;

        [Header("Value Labels")]
        public CellLabelMode labelMode = CellLabelMode.None;
        public Color labelColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("If on, the label uses the chunk's state color instead of Label Color.")]
        public bool labelUsesStateColor = false;
        [Tooltip("Font resolution (crispness). Higher = sharper but heavier.")]
        [Min(8)] public int labelFontResolution = 40;
        [Tooltip("On-screen size of the label.")]
        [Min(0.001f)] public float labelScale = 0.05f;

        [Header("Background")]
        public bool showBackground = true;
        [Tooltip("Color/alpha of the tiled backdrop.")]
        public Color backgroundTint = new Color(1f, 1f, 1f, 0.18f);
        [Tooltip("Extra chunks of backdrop beyond the visible window on each side.")]
        [Min(0)] public int backgroundPadding = 2;
        public float backgroundZ = 0.2f;

        [Header("Player Chunk Highlight")]
        public bool highlightPlayerChunk = false;
        public Color playerHighlightColor = new Color(0.40f, 0.80f, 1f, 1f);
        [Range(0f, 1f)] public float highlightStrength = 0.5f;

        // ---- internal ----
        private class Cell
        {
            public GameObject root;
            public SpriteRenderer sr;
            public TextMesh label;
            public MeshRenderer labelRenderer;
            public ChunkCoord coord;
            public Color target;
        }

        private readonly Dictionary<ChunkCoord, Cell> _cells = new();
        private readonly Stack<Cell> _pool = new();
        private SpriteRenderer _background;
        private Transform _cellRoot;
        private Font _labelFont;
        private ChunkCoord _lastCenter;
        private bool _started;

        void Awake()
        {
            if (manager == null) manager = FindObjectOfType<StreamingManager>();
            if (player == null) player = FindObjectOfType<PlayerController>();

            _labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                      ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            _cellRoot = new GameObject("Cells").transform;
            _cellRoot.SetParent(transform, false);

            if (backgroundSprite != null)
            {
                var bgGo = new GameObject("Background");
                bgGo.transform.SetParent(transform, false);
                _background = bgGo.AddComponent<SpriteRenderer>();
                _background.sprite = backgroundSprite;
                _background.drawMode = SpriteDrawMode.Tiled;
                _background.tileMode = SpriteTileMode.Continuous;
                _background.sortingOrder = sortingOrder - 1;
            }
        }

        void OnEnable()
        {
            if (manager != null)
            {
                manager.ChunkStateChanged += OnChunkStateChanged;
                manager.WorldReset += OnWorldReset;
            }
        }

        void OnDisable()
        {
            if (manager != null)
            {
                manager.ChunkStateChanged -= OnChunkStateChanged;
                manager.WorldReset -= OnWorldReset;
            }
        }

        /// <summary>Recycle every cell and force a fresh rebuild (e.g. after a chunk-size change).</summary>
        private void OnWorldReset()
        {
            foreach (var cell in _cells.Values) Recycle(cell);
            _cells.Clear();
            _started = false; // LateUpdate rebuilds at the new chunk size
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

            // Live visuals: keep cells positioned in rendered space (follows origin rebasing),
            // recompute color targets and ease colors toward them each frame.
            float t = smoothColor ? colorLerpSpeed * Time.deltaTime : 1f;
            foreach (var cell in _cells.Values)
            {
                if (player != null)
                {
                    Vector2 c = player.ChunkCenterRendered(cell.coord);
                    cell.root.transform.position = new Vector3(c.x, c.y, cellZ);
                }
                RefreshCell(cell, snap: false);
                cell.sr.color = smoothColor ? Color.Lerp(cell.sr.color, cell.target, t) : cell.target;
            }
        }

        private void UpdateBackground(ChunkCoord center)
        {
            if (_background == null) return;
            _background.enabled = showBackground;
            if (!showBackground) return;

            float size = manager != null ? manager.ChunkSize : 1f;
            _background.color = backgroundTint;
            _background.sortingOrder = sortingOrder - 1;
            Vector2 c = player != null ? player.WorldPos : center.ToWorldCenter(size);
            _background.transform.position = new Vector3(c.x, c.y, backgroundZ);
            float span = (viewRadius * 2 + 1 + backgroundPadding * 2) * size;
            _background.size = new Vector2(span, span);
        }

        /// <summary>Ensure exactly the window of cells around <paramref name="center"/> exists; recycle the rest.</summary>
        private void RebuildVisible(ChunkCoord center)
        {
            float size = manager != null ? manager.ChunkSize : 1f;

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

            for (int dy = -viewRadius; dy <= viewRadius; dy++)
            for (int dx = -viewRadius; dx <= viewRadius; dx++)
            {
                var coord = new ChunkCoord(center.cx + dx, center.cy + dy);
                if (!_cells.TryGetValue(coord, out var cell))
                {
                    cell = Obtain();
                    Place(cell, coord, size);
                    _cells[coord] = cell;
                }
            }
        }

        private void Place(Cell cell, ChunkCoord coord, float size)
        {
            cell.coord = coord;

            Vector2 c = player != null ? player.ChunkCenterRendered(coord) : coord.ToWorldCenter(size);
            cell.root.transform.position = new Vector3(c.x, c.y, cellZ);

            float spriteWorld = cellSprite != null ? cellSprite.bounds.size.x : 1f;
            float target = size * (1f - cellGap);
            float scale = spriteWorld > 0f ? target / spriteWorld : 1f;

            cell.sr.sprite = cellSprite;
            cell.sr.sortingOrder = sortingOrder;
            cell.sr.transform.localScale = new Vector3(scale, scale, 1f);

            cell.label.fontSize = labelFontResolution;
            cell.label.transform.localScale = Vector3.one * labelScale;
            cell.label.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            cell.labelRenderer.sortingOrder = sortingOrder + 2;

            SetLabelText(cell);
            RefreshCell(cell, snap: true);
        }

        private void OnChunkStateChanged(Chunk chunk)
        {
            if (_cells.TryGetValue(chunk.Coord, out var cell))
                SetLabelText(cell); // color handled in LateUpdate; keep state labels in sync now
        }

        /// <summary>Per-frame, allocation-free: recompute the target color and label styling.</summary>
        private void RefreshCell(Cell cell, bool snap)
        {
            ChunkState state = manager != null ? manager.GetState(cell.coord) : ChunkState.Unloaded;

            Color c = ColorFor(state) * cellTint;
            if (highlightPlayerChunk && cell.coord == _lastCenter)
                c = Color.Lerp(c, playerHighlightColor, highlightStrength);

            cell.target = c;
            if (snap) cell.sr.color = c;

            if (cell.labelRenderer != null)
            {
                bool show = labelMode != CellLabelMode.None;
                if (cell.labelRenderer.enabled != show) cell.labelRenderer.enabled = show;
                if (show) cell.label.color = labelUsesStateColor ? ColorFor(state) : labelColor;
            }
        }

        /// <summary>Allocates a string, so only called on rebuild and state-change (not per frame).</summary>
        private void SetLabelText(Cell cell)
        {
            if (cell.label == null) return;
            ChunkState state = manager != null ? manager.GetState(cell.coord) : ChunkState.Unloaded;
            switch (labelMode)
            {
                case CellLabelMode.Coordinates: cell.label.text = $"{cell.coord.cx},{cell.coord.cy}"; break;
                case CellLabelMode.State:        cell.label.text = state.ToString(); break;
                case CellLabelMode.Both:         cell.label.text = $"{cell.coord.cx},{cell.coord.cy}\n{state}"; break;
                default:                         cell.label.text = string.Empty; break;
            }
        }

        private Color ColorFor(ChunkState state) => state switch
        {
            ChunkState.Loading => loadingColor,
            ChunkState.Loaded => loadedColor,
            ChunkState.Unloading => unloadingColor,
            _ => unloadedColor,
        };

        private Cell Obtain()
        {
            Cell cell = _pool.Count > 0 ? _pool.Pop() : NewCell();
            cell.root.SetActive(true);
            return cell;
        }

        private void Recycle(Cell cell)
        {
            cell.root.SetActive(false);
            _pool.Push(cell);
        }

        private Cell NewCell()
        {
            var root = new GameObject("Cell");
            root.transform.SetParent(_cellRoot, false);

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(root.transform, false);
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            sr.sprite = cellSprite;
            sr.sortingOrder = sortingOrder;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.font = _labelFont;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            var mr = labelGo.GetComponent<MeshRenderer>();
            if (_labelFont != null) mr.sharedMaterial = _labelFont.material;
            mr.sortingOrder = sortingOrder + 2;
            mr.enabled = labelMode != CellLabelMode.None;

            return new Cell { root = root, sr = sr, label = tm, labelRenderer = mr };
        }
    }
}
