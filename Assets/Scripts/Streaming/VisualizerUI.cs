using UnityEngine;
using UnityEngine.UI;

namespace LevelStreaming
{
    /// <summary>
    /// Controller for the debug overlay. The Canvas and all its widgets live in the scene
    /// (so you can design them in the Editor); this component just wires the buttons/toggle
    /// and pushes live stats into the labels each frame. Drag the scene references in below,
    /// or let them stay null and the matching widgets simply won't update.
    /// </summary>
    public class VisualizerUI : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        public StreamingManager manager;
        public SightCone sight;
        public CameraController cameraController;

        [Header("System bar")]
        public Text title;
        public Text index;
        public Button backButton;
        public Button nextButton;

        [Header("Debug stats")]
        public Text playerChunk;
        public Text tracked;
        public Text loaded;
        public Text loading;
        public Text unloading;
        public Text fov;
        public Text aimState;
        public Toggle aimToggle;

        [Header("Camera")]
        public Toggle followToggle;
        public Text zoomValue;
        public Button zoomInButton;
        public Button zoomOutButton;
        public Button resetViewButton;

        void Awake()
        {
            if (manager == null) manager = FindObjectOfType<StreamingManager>();
            if (sight == null) sight = FindObjectOfType<SightCone>();
            if (cameraController == null) cameraController = FindObjectOfType<CameraController>();
        }

        void Start()
        {
            if (backButton != null) backButton.onClick.AddListener(() => manager?.PrevStrategy());
            if (nextButton != null) nextButton.onClick.AddListener(() => manager?.NextStrategy());

            if (aimToggle != null)
            {
                if (sight != null) aimToggle.SetIsOnWithoutNotify(sight.useMouseAim);
                aimToggle.onValueChanged.AddListener(v => { if (sight != null) sight.useMouseAim = v; });
            }

            if (followToggle != null)
            {
                if (cameraController != null) followToggle.SetIsOnWithoutNotify(cameraController.followPlayer);
                followToggle.onValueChanged.AddListener(v => { if (cameraController != null) cameraController.followPlayer = v; });
            }
            if (zoomInButton != null) zoomInButton.onClick.AddListener(() => cameraController?.ZoomIn());
            if (zoomOutButton != null) zoomOutButton.onClick.AddListener(() => cameraController?.ZoomOut());
            if (resetViewButton != null) resetViewButton.onClick.AddListener(() => cameraController?.ResetView());
        }

        void Update()
        {
            if (manager != null)
            {
                Set(title, manager.ActiveStrategyName);
                Set(index, $"{manager.ActiveStrategyIndex + 1} / {manager.StrategyCount}");
                Set(playerChunk, $"Player chunk   {manager.PlayerChunk}");
                Set(tracked, $"Tracked        {manager.TrackedCount}");
                Set(loaded, $"Loaded         {manager.CountInState(ChunkState.Loaded)}");
                Set(loading, $"Loading        {manager.CountInState(ChunkState.Loading)}");
                Set(unloading, $"Unloading      {manager.CountInState(ChunkState.Unloading)}");
            }

            if (sight != null)
            {
                Set(fov, $"FOV {sight.fovAngle:0}°    Dist {sight.viewDistance:0.0}");
                Set(aimState, $"Cone aim: {(sight.useMouseAim ? "Mouse cursor" : "Movement dir")}");
                if (aimToggle != null && aimToggle.isOn != sight.useMouseAim)
                    aimToggle.SetIsOnWithoutNotify(sight.useMouseAim);
            }

            if (cameraController != null)
            {
                Set(zoomValue, $"Zoom: {cameraController.CurrentZoom:0.0}");
                if (followToggle != null && followToggle.isOn != cameraController.followPlayer)
                    followToggle.SetIsOnWithoutNotify(cameraController.followPlayer);
            }
        }

        private static void Set(Text t, string s)
        {
            if (t != null) t.text = s;
        }
    }
}
