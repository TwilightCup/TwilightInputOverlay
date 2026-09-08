using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// The standalone IMGUI settings panel used when HSRTimer is not present.
    /// Toggled by the configurable panel key (default Home). It draws the shared
    /// <see cref="PanelContent"/> and adds its own save/close/footer controls.
    /// When integrated into HSRTimer, this component is not created.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        private bool _visible;
        public bool IsVisible => _visible;

        private Rect _rect = new Rect(60f, 60f, 640f, 600f);
        private Vector2 _scroll;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            if (Input.GetKeyDown(cfg.Settings.PanelKey))
                Toggle();
        }

        private void OnDestroy()
        {
            if (ConfigService.Instance != null)
                ConfigService.Instance.SaveSettings();
        }

        private void OnApplicationQuit()
        {
            if (ConfigService.Instance != null)
                ConfigService.Instance.SaveSettings();
        }

        public void Toggle()
        {
            _visible = !_visible;
            if (!_visible && ConfigService.Instance != null)
                ConfigService.Instance.SaveSettings();
        }

        private void OnGUI()
        {
            if (!_visible) return;
            PanelStyles.Ensure();
            _rect = GUI.Window(GetInstanceID(), _rect, Draw, PluginInfo.PLUGIN_NAME);
        }

        private void Draw(int id)
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var loc = cfg.Localization;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandWidth(true));
            PanelContent.Draw(false);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(loc.Get("PANEL_SAVE"), PanelStyles.Button))
                cfg.SaveSettings();
            if (GUILayout.Button(loc.Get("PANEL_CLOSE"), PanelStyles.Button))
            {
                cfg.SaveSettings();
                _visible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(loc.Get("PANEL_FOOTER"), PanelStyles.Small);
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, _rect.width, 20));
        }
    }
}
