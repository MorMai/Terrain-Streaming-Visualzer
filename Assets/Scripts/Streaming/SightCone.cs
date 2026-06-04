using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LevelStreaming
{
    /// <summary>
    /// Draws a wedge / frustum of sight from the player using a LineRenderer outline and
    /// exposes IsChunkInSight() so a future strategy can stream by visibility.
    /// Purely visual in v1 (no occlusion).
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class SightCone : MonoBehaviour
    {
        [Header("Shape")]
        [Range(1f, 179f)] public float fovAngle = 70f;
        [Min(0.1f)] public float viewDistance = 3f;
        [Tooltip("Arc tessellation. More = smoother far edge.")]
        [Range(2, 64)] public int arcSegments = 16;

        [Header("Aim")]
        [Tooltip("When true the cone points at the mouse cursor instead of the movement direction. Toggle from the debug UI.")]
        public bool useMouseAim = false;

        [Header("Look")]
        public Color coneColor = new Color(1f, 1f, 0.35f, 0.9f);
        [Min(0.001f)] public float lineWidth = 0.04f;

        private LineRenderer _lr;
        private PlayerController _player;
        private Camera _cam;

        /// <summary>
        /// Effective facing used for both drawing and IsChunkInSight: the mouse direction
        /// when mouse-aim is on (falling back to movement facing if the cursor is on the player),
        /// otherwise the player's movement facing.
        /// </summary>
        public Vector2 CurrentFacing
        {
            get
            {
                Vector2 origin = _player != null ? _player.WorldPos : (Vector2)transform.position;
                if (useMouseAim)
                {
                    Vector2 dir = MouseWorld() - origin;
                    if (dir.sqrMagnitude > 0.0001f) return dir.normalized;
                }
                return _player != null ? _player.Facing : Vector2.up;
            }
        }

        void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            _cam = Camera.main;
            _player = GetComponentInParent<PlayerController>();
            if (_player == null) _player = FindObjectOfType<PlayerController>();

            _lr.useWorldSpace = true;
            _lr.loop = true;
            _lr.numCornerVertices = 2;
            _lr.alignment = LineAlignment.View;
            _lr.textureMode = LineTextureMode.Stretch;
            if (_lr.sharedMaterial == null)
                _lr.material = new Material(Shader.Find("Sprites/Default"));
        }

        void LateUpdate()
        {
            _lr.startWidth = _lr.endWidth = lineWidth;
            _lr.startColor = _lr.endColor = coneColor;
            RebuildOutline();
        }

        private void RebuildOutline()
        {
            Vector2 origin = _player != null ? _player.WorldPos : (Vector2)transform.position;
            Vector2 facing = CurrentFacing;
            float facingDeg = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;

            int points = arcSegments + 2; // origin + arc endpoints (loop closes back to origin)
            _lr.positionCount = points;

            float half = fovAngle * 0.5f;
            float z = transform.position.z;

            _lr.SetPosition(0, new Vector3(origin.x, origin.y, z));
            for (int i = 0; i <= arcSegments; i++)
            {
                float t = (float)i / arcSegments;
                float ang = (facingDeg - half + fovAngle * t) * Mathf.Deg2Rad;
                Vector2 p = origin + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * viewDistance;
                _lr.SetPosition(i + 1, new Vector3(p.x, p.y, z));
            }
        }

        /// <summary>True if the given chunk's center lies inside the sight wedge.</summary>
        public bool IsChunkInSight(ChunkCoord coord)
        {
            if (_player == null) return false;
            Vector2 center = coord.ToWorldCenter(_player.chunkSize);
            Vector2 to = center - _player.WorldPos;
            if (to.sqrMagnitude > viewDistance * viewDistance) return false;

            float angTo = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            Vector2 facing = CurrentFacing;
            float facingDeg = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            float delta = Mathf.Abs(Mathf.DeltaAngle(facingDeg, angTo));
            return delta <= fovAngle * 0.5f;
        }

        /// <summary>Mouse cursor position in world space (orthographic-safe).</summary>
        private Vector2 MouseWorld()
        {
            if (_cam == null) _cam = Camera.main;
            Vector2 fallback = _player != null ? _player.WorldPos : (Vector2)transform.position;
            if (_cam == null) return fallback;

            Vector3 screen;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return fallback;
            screen = Mouse.current.position.ReadValue();
#else
            screen = Input.mousePosition;
#endif
            screen.z = Mathf.Abs(_cam.transform.position.z);
            Vector3 world = _cam.ScreenToWorldPoint(screen);
            return new Vector2(world.x, world.y);
        }
    }
}
