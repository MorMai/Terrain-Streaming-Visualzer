using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LevelStreaming
{
    /// <summary>
    /// Top-down camera rig: smoothly follows the player and zooms with the scroll wheel.
    /// Put it on the (orthographic) Main Camera. Everything is exposed for tuning, and the
    /// debug UI can toggle follow / step the zoom / reset the view at runtime.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [SerializeField] private Camera cam;
        [SerializeField] private PlayerController target;

        [Header("Follow")]
        public bool followPlayer = true;
        [Tooltip("Smaller = snappier. Time (s) to catch up to the player.")]
        [Min(0f)] public float followSmoothTime = 0.15f;
        public Vector2 followOffset = Vector2.zero;

        [Header("Zoom (orthographic size)")]
        [Min(0.5f)] public float defaultZoom = 6f;
        [Min(0.5f)] public float minZoom = 2f;
        [Min(0.5f)] public float maxZoom = 20f;
        [Tooltip("World units changed per scroll notch / per +- button press.")]
        [Min(0.01f)] public float zoomStep = 1f;
        [Tooltip("Time (s) to ease to the target zoom. 0 = instant.")]
        [Min(0f)] public float zoomSmoothTime = 0.12f;
        public bool enableScrollZoom = true;

        public bool FollowPlayer { get => followPlayer; set => followPlayer = value; }
        public float CurrentZoom => cam != null ? cam.orthographicSize : 0f;

        private float _targetZoom;
        private float _zoomVel;
        private Vector3 _moveVel;

        void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (target == null) target = FindObjectOfType<PlayerController>();
        }

        void Start()
        {
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Clamp(defaultZoom, minZoom, maxZoom);
            _targetZoom = cam.orthographicSize;
            if (target != null) SnapToTarget();
        }

        void LateUpdate()
        {
            if (cam == null) return;

            if (enableScrollZoom)
            {
                float scroll = ReadScroll();
                if (Mathf.Abs(scroll) > 0.0001f)
                    SetTargetZoom(_targetZoom - scroll * zoomStep); // scroll up -> zoom in
            }

            cam.orthographicSize = zoomSmoothTime > 0f
                ? Mathf.SmoothDamp(cam.orthographicSize, _targetZoom, ref _zoomVel, zoomSmoothTime)
                : _targetZoom;

            if (followPlayer && target != null)
            {
                Vector3 goal = DesiredPosition();
                transform.position = followSmoothTime > 0f
                    ? Vector3.SmoothDamp(transform.position, goal, ref _moveVel, followSmoothTime)
                    : goal;
            }
        }

        // ---- public API for the debug UI ----
        public void ZoomIn()  => SetTargetZoom(_targetZoom - zoomStep);
        public void ZoomOut() => SetTargetZoom(_targetZoom + zoomStep);

        public void ResetView()
        {
            SetTargetZoom(defaultZoom);
            if (target != null) SnapToTarget();
        }

        private void SetTargetZoom(float value)
        {
            _targetZoom = Mathf.Clamp(value, minZoom, maxZoom);
        }

        private Vector3 DesiredPosition()
        {
            Vector2 p = target.WorldPos + followOffset;
            return new Vector3(p.x, p.y, transform.position.z); // keep camera depth
        }

        private void SnapToTarget()
        {
            transform.position = DesiredPosition();
            _moveVel = Vector3.zero;
        }

        private static float ReadScroll()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return 0f;
            return Mouse.current.scroll.ReadValue().y / 120f; // normalize notch to ~±1
#else
            return Input.GetAxis("Mouse ScrollWheel") * 10f;
#endif
        }
    }
}
