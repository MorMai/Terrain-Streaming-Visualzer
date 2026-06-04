using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace LevelStreaming
{
    /// <summary>
    /// Canvas-based (uGUI) debug overlay for the streaming visualizer:
    ///  - Top bar: current streaming-system title with Back / Next to cycle systems
    ///    (every IStreamingStrategy in the scene shows up here automatically).
    ///  - Left panel: live, color-coded chunk-state stats and a Mouse-aim toggle that
    ///    switches the cone between movement-facing and cursor-facing.
    /// Builds its whole hierarchy at runtime and self-spawns at play start, so the scene
    /// needs no manual wiring. An EventSystem is created with the correct input module for
    /// whichever input backend the project uses.
    /// </summary>
    public class VisualizerUI : MonoBehaviour
    {
        // ---- palette ----
        static readonly Color PanelBg    = new Color(0.09f, 0.10f, 0.14f, 0.88f);
        static readonly Color BtnNormal  = new Color(0.18f, 0.21f, 0.28f, 1f);
        static readonly Color BtnHover   = new Color(0.27f, 0.32f, 0.42f, 1f);
        static readonly Color BtnPressed = new Color(0.12f, 0.14f, 0.19f, 1f);
        static readonly Color Accent     = new Color(0.30f, 0.85f, 0.40f, 1f);
        static readonly Color TitleCol   = new Color(0.58f, 0.86f, 1.00f, 1f);
        static readonly Color Caption    = new Color(0.60f, 0.68f, 0.80f, 1f);
        static readonly Color Dim        = new Color(0.78f, 0.83f, 0.90f, 1f);
        static readonly Color ColLoaded    = new Color(0.34f, 0.86f, 0.44f, 1f);
        static readonly Color ColLoading   = new Color(0.96f, 0.80f, 0.28f, 1f);
        static readonly Color ColUnloading = new Color(0.96f, 0.52f, 0.22f, 1f);

        private StreamingManager _manager;
        private SightCone _sight;
        private Font _font;

        private Text _title, _index, _playerChunk, _tracked, _loaded, _loading, _unloading, _fov, _aimState;
        private Toggle _aimToggle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindObjectOfType<StreamingManager>() == null) return;     // only in the visualizer scene
            if (FindObjectOfType<VisualizerUI>() != null) return;
            new GameObject("VisualizerUI (auto)").AddComponent<VisualizerUI>();
        }

        void Awake()
        {
            _manager = FindObjectOfType<StreamingManager>();
            _sight = FindObjectOfType<SightCone>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUI();
        }

        void Update()
        {
            if (_manager == null) return;

            _title.text = _manager.ActiveStrategyName;
            _index.text = $"{_manager.ActiveStrategyIndex + 1} / {_manager.StrategyCount}";
            _playerChunk.text = $"Player chunk   {_manager.PlayerChunk}";
            _tracked.text   = $"Tracked        {_manager.TrackedCount}";
            _loaded.text    = $"Loaded         {_manager.CountInState(ChunkState.Loaded)}";
            _loading.text   = $"Loading        {_manager.CountInState(ChunkState.Loading)}";
            _unloading.text = $"Unloading      {_manager.CountInState(ChunkState.Unloading)}";

            if (_sight != null)
            {
                _fov.text = $"FOV {_sight.fovAngle:0}°    Dist {_sight.viewDistance:0.0}";
                _aimState.text = $"Cone aim: {(_sight.useMouseAim ? "Mouse cursor" : "Movement dir")}";
                if (_aimToggle != null && _aimToggle.isOn != _sight.useMouseAim)
                    _aimToggle.SetIsOnWithoutNotify(_sight.useMouseAim);
            }
        }

        // -------------------------------------------------------------------------------
        // UI construction
        // -------------------------------------------------------------------------------
        private void BuildUI()
        {
            EnsureEventSystem();

            var canvasGo = NewUI("Canvas", null);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.transform.SetParent(transform, false);

            BuildSystemBar(canvas.transform);
            BuildDebugPanel(canvas.transform);
        }

        private void BuildSystemBar(Transform parent)
        {
            var panel = MakePanel("SystemBar", parent,
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                pos: new Vector2(0, -14), width: 500,
                padding: new RectOffset(16, 16, 12, 14), spacing: 6);

            MakeLabel(panel, "STREAMING SYSTEM", 15, Caption, TextAnchor.MiddleCenter);
            _title = MakeLabel(panel, "—", 30, TitleCol, TextAnchor.MiddleCenter, FontStyle.Bold);

            var row = NewUI("Row", panel);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            SetLayout(row, minHeight: 44, preferredHeight: 44);

            MakeButton(row.transform, "<  Back", () => _manager?.PrevStrategy());
            _index = MakeLabel(row.transform, "0 / 0", 18, Dim, TextAnchor.MiddleCenter);
            SetLayout(_index.gameObject, preferredWidth: 80, flexibleWidth: 0);
            MakeButton(row.transform, "Next  >", () => _manager?.NextStrategy());
        }

        private void BuildDebugPanel(Transform parent)
        {
            var panel = MakePanel("DebugPanel", parent,
                anchor: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                pos: new Vector2(14, -14), width: 320,
                padding: new RectOffset(16, 16, 12, 14), spacing: 5);

            MakeLabel(panel, "DEBUG", 18, Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MakeDivider(panel);

            _playerChunk = MakeStat(panel, Dim);
            _tracked     = MakeStat(panel, Dim);
            _loaded      = MakeStat(panel, ColLoaded);
            _loading     = MakeStat(panel, ColLoading);
            _unloading   = MakeStat(panel, ColUnloading);

            MakeDivider(panel);
            _fov = MakeStat(panel, Dim);
            _aimToggle = MakeToggle(panel, "Mouse-aim cone",
                _sight != null && _sight.useMouseAim,
                v => { if (_sight != null) _sight.useMouseAim = v; });
            _aimState = MakeStat(panel, Caption);

            MakeDivider(panel);
            MakeLabel(panel, "WASD / arrows to move", 14, Caption, TextAnchor.MiddleLeft, FontStyle.Italic);
        }

        // -------------------------------------------------------------------------------
        // Builder helpers
        // -------------------------------------------------------------------------------
        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            // Use the Input System UI module (resolved by reflection so this compiles
            // regardless of which input backend/package version is present), and assign
            // its default UI actions so pointer clicks work without a serialized asset.
            var moduleType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                var module = es.AddComponent(moduleType);
                var assign = moduleType.GetMethod("AssignDefaultActions");
                assign?.Invoke(module, null);
            }
            else
            {
                es.AddComponent<StandaloneInputModule>();
            }
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        private GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private Transform MakePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 pos, float width, RectOffset padding, float spacing)
        {
            var go = NewUI(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 120);

            var img = go.AddComponent<Image>();
            img.color = PanelBg;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = padding;
            vlg.spacing = spacing;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go.transform;
        }

        private Text MakeLabel(Transform parent, string text, int size, Color color,
            TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var go = NewUI("Label", parent);
            var t = go.AddComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.color = color;
            t.alignment = anchor; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private Text MakeStat(Transform parent, Color color)
        {
            var t = MakeLabel(parent, "", 16, color, TextAnchor.MiddleLeft);
            SetLayout(t.gameObject, minHeight: 22, preferredHeight: 22);
            return t;
        }

        private void MakeDivider(Transform parent)
        {
            var go = NewUI("Divider", parent);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);
            SetLayout(go, minHeight: 2, preferredHeight: 2);
        }

        private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = NewUI($"Btn_{label}", parent);
            var img = go.AddComponent<Image>();
            img.color = BtnNormal;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = BtnNormal; cb.highlightedColor = BtnHover;
            cb.pressedColor = BtnPressed; cb.selectedColor = BtnNormal;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.onClick.AddListener(onClick);

            var txt = MakeLabel(go.transform, label, 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch((RectTransform)txt.transform);

            SetLayout(go, minHeight: 40, preferredHeight: 40, flexibleWidth: 1);
            return btn;
        }

        private Toggle MakeToggle(Transform parent, string label, bool isOn,
            UnityEngine.Events.UnityAction<bool> onChanged)
        {
            var go = NewUI("Toggle", parent);
            SetLayout(go, minHeight: 28, preferredHeight: 28);
            var tog = go.AddComponent<Toggle>();

            var box = NewUI("Box", go.transform);
            var brt = (RectTransform)box.transform;
            brt.anchorMin = new Vector2(0f, 0.5f); brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(2, 0);
            brt.sizeDelta = new Vector2(22, 22);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = BtnNormal;

            var check = NewUI("Check", box.transform);
            var crt = (RectTransform)check.transform;
            Stretch(crt); crt.offsetMin = new Vector2(4, 4); crt.offsetMax = new Vector2(-4, -4);
            var checkImg = check.AddComponent<Image>();
            checkImg.color = Accent;

            var lbl = MakeLabel(go.transform, label, 16, Dim, TextAnchor.MiddleLeft);
            var lrt = (RectTransform)lbl.transform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1);
            lrt.offsetMin = new Vector2(32, 0); lrt.offsetMax = new Vector2(0, 0);

            tog.targetGraphic = boxImg;
            tog.graphic = checkImg;
            tog.SetIsOnWithoutNotify(isOn);
            tog.onValueChanged.AddListener(onChanged);
            return tog;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void SetLayout(GameObject go, float minHeight = -1, float preferredHeight = -1,
            float preferredWidth = -1, float flexibleWidth = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minHeight >= 0) le.minHeight = minHeight;
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
            if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
        }
    }
}
