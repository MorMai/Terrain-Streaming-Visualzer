using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Lightweight IMGUI debug overlay for the streaming visualizer:
    ///  - Top bar: current streaming-system title with Back / Next to cycle systems
    ///    (extensible — every IStreamingStrategy in the scene shows up here).
    ///  - Left panel: live stats (player chunk, chunk-state counts) and a Mouse-aim toggle
    ///    that switches the cone between movement-facing and cursor-facing.
    /// Self-spawns at play start so no scene wiring is required.
    /// </summary>
    public class VisualizerUI : MonoBehaviour
    {
        private StreamingManager _manager;
        private SightCone _sight;

        private GUIStyle _titleStyle, _subStyle, _headerStyle, _labelStyle, _btnStyle, _centerStyle;
        private bool _stylesReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // Only activate in a scene that actually uses the streaming system.
            if (FindObjectOfType<StreamingManager>() == null) return;
            if (FindObjectOfType<VisualizerUI>() != null) return;
            new GameObject("VisualizerUI (auto)").AddComponent<VisualizerUI>();
        }

        void Awake()
        {
            _manager = FindObjectOfType<StreamingManager>();
            _sight = FindObjectOfType<SightCone>();
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            float s = Mathf.Clamp(Screen.height / 900f, 1f, 2f);

            _titleStyle = new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.RoundToInt(22 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _subStyle = new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.RoundToInt(11 * s), alignment = TextAnchor.MiddleCenter };
            _subStyle.normal.textColor = new Color(0.7f, 0.8f, 0.9f);
            _headerStyle = new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.RoundToInt(13 * s), fontStyle = FontStyle.Bold };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * s) };
            _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * s) };
            _centerStyle = new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleCenter };

            _stylesReady = true;
        }

        void OnGUI()
        {
            if (_manager == null) _manager = FindObjectOfType<StreamingManager>();
            if (_sight == null) _sight = FindObjectOfType<SightCone>();
            if (_manager == null) return;

            EnsureStyles();
            float s = Mathf.Clamp(Screen.height / 900f, 1f, 2f);

            DrawSystemBar(s);
            DrawDebugPanel(s);
        }

        private void DrawSystemBar(float s)
        {
            float w = 460 * s, h = 104 * s;
            var rect = new Rect((Screen.width - w) * 0.5f, 10 * s, w, h);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("STREAMING SYSTEM", _subStyle);
            GUILayout.Label(_manager.ActiveStrategyName, _titleStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<  Back", _btnStyle, GUILayout.Height(30 * s)))
                _manager.PrevStrategy();
            GUILayout.Label($"{_manager.ActiveStrategyIndex + 1} / {_manager.StrategyCount}",
                _centerStyle, GUILayout.Width(70 * s));
            if (GUILayout.Button("Next  >", _btnStyle, GUILayout.Height(30 * s)))
                _manager.NextStrategy();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawDebugPanel(float s)
        {
            float w = 250 * s, h = 252 * s;
            var rect = new Rect(10 * s, 10 * s, w, h);
            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.Label("DEBUG", _headerStyle);
            GUILayout.Label($"Player chunk : {_manager.PlayerChunk}", _labelStyle);
            GUILayout.Label($"Tracked      : {_manager.TrackedCount}", _labelStyle);
            GUILayout.Label($"Loaded       : {_manager.CountInState(ChunkState.Loaded)}", _labelStyle);
            GUILayout.Label($"Loading      : {_manager.CountInState(ChunkState.Loading)}", _labelStyle);
            GUILayout.Label($"Unloading    : {_manager.CountInState(ChunkState.Unloading)}", _labelStyle);

            GUILayout.Space(8 * s);
            if (_sight != null)
            {
                GUILayout.Label($"FOV {_sight.fovAngle:0}°   Dist {_sight.viewDistance:0.0}", _labelStyle);
                bool aim = GUILayout.Toggle(_sight.useMouseAim, "  Mouse-aim cone", _labelStyle);
                if (aim != _sight.useMouseAim) _sight.useMouseAim = aim;
                GUILayout.Label($"Cone aim : {(_sight.useMouseAim ? "Mouse cursor" : "Movement dir")}", _labelStyle);
            }

            GUILayout.Space(6 * s);
            GUILayout.Label("WASD / arrows to move", _subStyle);
            GUILayout.EndArea();
        }
    }
}
