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
        public PlayerController player;

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

        [Header("World / Player")]
        public InputField chunkSizeInput;
        public InputField moveSpeedInput;

        [Header("Sight Cone")]
        public Slider fovSlider;
        public Text fovLabel;
        public Slider viewDistanceSlider;
        public Text viewDistanceLabel;

        void Awake()
        {
            if (manager == null) manager = FindObjectOfType<StreamingManager>();
            if (sight == null) sight = FindObjectOfType<SightCone>();
            if (cameraController == null) cameraController = FindObjectOfType<CameraController>();
            if (player == null) player = FindObjectOfType<PlayerController>();
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

            if (chunkSizeInput != null)
            {
                chunkSizeInput.text = player != null ? player.chunkSize.ToString("0.###") : "1";
                chunkSizeInput.onEndEdit.AddListener(OnChunkSizeEntered);
            }
            if (moveSpeedInput != null)
            {
                moveSpeedInput.text = player != null ? player.moveSpeed.ToString("0.###") : "4";
                moveSpeedInput.onEndEdit.AddListener(OnMoveSpeedEntered);
            }

            if (fovSlider != null)
            {
                fovSlider.minValue = 1f; fovSlider.maxValue = 179f; fovSlider.wholeNumbers = true;
                if (sight != null) fovSlider.SetValueWithoutNotify(sight.fovAngle);
                fovSlider.onValueChanged.AddListener(v => { if (sight != null) sight.fovAngle = v; });
            }
            if (viewDistanceSlider != null)
            {
                viewDistanceSlider.minValue = 0.5f; viewDistanceSlider.maxValue = 15f; viewDistanceSlider.wholeNumbers = false;
                if (sight != null) viewDistanceSlider.SetValueWithoutNotify(sight.viewDistance);
                viewDistanceSlider.onValueChanged.AddListener(v => { if (sight != null) sight.viewDistance = v; });
            }
        }

        // Chunk size can't change live -> apply + reset the whole simulation.
        private void OnChunkSizeEntered(string s)
        {
            if (manager != null && TryParse(s, out float v) && v > 0.01f)
                manager.SetChunkSize(v);
            if (chunkSizeInput != null && player != null)
                chunkSizeInput.text = player.chunkSize.ToString("0.###");
        }

        // Move speed applies live, no reset needed.
        private void OnMoveSpeedEntered(string s)
        {
            if (player != null && TryParse(s, out float v) && v >= 0f)
                player.moveSpeed = v;
            if (moveSpeedInput != null && player != null)
                moveSpeedInput.text = player.moveSpeed.ToString("0.###");
        }

        private static bool TryParse(string s, out float value) => float.TryParse(
            s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

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

            if (sight != null)
            {
                Set(fovLabel, $"FOV: {sight.fovAngle:0}°");
                Set(viewDistanceLabel, $"View distance: {sight.viewDistance:0.0}");
                if (fovSlider != null && !Mathf.Approximately(fovSlider.value, sight.fovAngle))
                    fovSlider.SetValueWithoutNotify(sight.fovAngle);
                if (viewDistanceSlider != null && !Mathf.Approximately(viewDistanceSlider.value, sight.viewDistance))
                    viewDistanceSlider.SetValueWithoutNotify(sight.viewDistance);
            }
        }

        private static void Set(Text t, string s)
        {
            if (t != null) t.text = s;
        }
    }
}
