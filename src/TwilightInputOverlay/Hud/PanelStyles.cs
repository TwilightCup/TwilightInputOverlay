using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Shared IMGUI styles used by both the standalone settings panel and the
    /// HSRTimer/TwilightTimer-integrated tab. Uses a dynamic OS font with CJK fallbacks.
    /// </summary>
    internal static class PanelStyles
    {
        private static bool _ready;
        private static Font _font;

        public static GUIStyle Label { get; private set; }
        public static GUIStyle Value { get; private set; }
        public static GUIStyle Section { get; private set; }
        public static GUIStyle Small { get; private set; }
        public static GUIStyle Toggle { get; private set; }
        public static GUIStyle Button { get; private set; }
        public static GUIStyle TextField { get; private set; }

        public static void Ensure()
        {
            if (_ready) return;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC",
                    "Noto Sans CJK", "Heiti SC", "Arial Unicode MS", "Arial",
                }, 14);
            }
            catch { _font = null; }

            Label = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 13, wordWrap = false };
            Value = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 13 };
            Section = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 14, fontStyle = FontStyle.Bold };
            Small = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 11, wordWrap = true };
            Toggle = new GUIStyle(GUI.skin.toggle) { font = _font, fontSize = 13, wordWrap = false };
            Button = new GUIStyle(GUI.skin.button) { font = _font, fontSize = 13, wordWrap = false };
            TextField = new GUIStyle(GUI.skin.textField) { font = _font, fontSize = 13 };
            _ready = true;
        }
    }
}
