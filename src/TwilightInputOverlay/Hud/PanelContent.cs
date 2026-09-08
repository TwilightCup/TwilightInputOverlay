using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// The shared IMGUI content for the input-overlay settings. It is drawn both
    /// by the standalone settings panel and by the HSRTimer-integrated tab.
    /// When <c>integrated</c> is true, the controls HSRTimer already owns —
    /// the settings-panel keybind and the language selector — are omitted.
    /// All edits write directly into <see cref="ConfigService.Settings"/> so
    /// they apply live.
    /// </summary>
    internal static class PanelContent
    {
        private static readonly Dictionary<string, string> ColorHexBuf = new Dictionary<string, string>();
        private static string[] _langCodes;
        private static string[] _langDisplays;
        private static bool _langDropdownOpen;
        private static int _editingState; // 0 = idle, 1 = pressed
        private static string _pendingRebind;

        public static void Draw(bool integrated)
        {
            PanelStyles.Ensure();
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var s = cfg.Settings;
            var loc = cfg.Localization;

            if (!integrated)
            {
                Section(loc.Get("PANEL_GENERAL"));
                KeybindRow(loc, "SETTINGS_PANEL_KEY", () => s.PanelKey, k => s.PanelKey = k);
                DrawLanguageSelector(cfg, loc);
                if (GUILayout.Button(loc.Get("PANEL_RELOAD_LANGUAGE"), PanelStyles.Button))
                {
                    cfg.ReloadLanguage();
                    RefreshLanguageList();
                }
            }

            Section(loc.Get("PANEL_HUD"));
            s.ShowHud = Toggle(loc.Get("SETTINGS_SHOW_HUD"), s.ShowHud);
            s.ShowKeyText = Toggle(loc.Get("SETTINGS_SHOW_KEY_TEXT"), s.ShowKeyText);
            s.OffsetX = FloatFieldRow(loc.Get("PANEL_OFFSET_X"), s.OffsetX, "0.##");
            s.OffsetY = FloatFieldRow(loc.Get("PANEL_OFFSET_Y"), s.OffsetY, "0.##");
            s.Scale = Mathf.Max(0.1f, SliderRow(loc.Get("PANEL_SCALE"), s.Scale, 0.1f, 3f));

            Section(loc.Get("PANEL_GRID"));
            s.Spacing = Mathf.Max(0f, SliderRow(loc.Get("SETTINGS_SPACING"), s.Spacing, 0f, 24f));
            s.CornerRadius = Mathf.Max(0f, SliderRow(loc.Get("SETTINGS_CORNER_RADIUS"), s.CornerRadius, 0f, 24f));

            Section(loc.Get("PANEL_STYLE"));
            int nextState = GUILayout.SelectionGrid(_editingState,
                new[] { loc.Get("PANEL_STATE_IDLE"), loc.Get("PANEL_STATE_PRESSED") },
                2, PanelStyles.Button);
            if (nextState != _editingState)
                _editingState = nextState;

            ButtonStyle style = _editingState == 0 ? s.Idle : s.Pressed;
            ColorRow(loc, "PANEL_COLOR_TEXT", style.Text, c => style.Text = c);
            ColorRow(loc, "PANEL_COLOR_BORDER", style.Border, c => style.Border = c);
            ColorRow(loc, "PANEL_COLOR_FILL", style.Fill, c => style.Fill = c);
        }

        // ── sections / widgets ──

        private static void Section(string title)
        {
            GUILayout.Space(6);
            GUILayout.Label(title, PanelStyles.Section);
        }

        private static bool Toggle(string label, bool value)
        {
            return GUILayout.Toggle(value, label, PanelStyles.Toggle);
        }

        private static float SliderRow(string label, float value, float min, float max)
        {
            GUILayout.Label(label + ": " + value.ToString("0.##"), PanelStyles.Value);
            return GUILayout.HorizontalSlider(value, min, max);
        }

        private static float FloatFieldRow(string label, float value, string format = "F0")
        {
            GUILayout.BeginHorizontal();
            string newText = GUILayout.TextField(value.ToString(format), PanelStyles.TextField, GUILayout.Width(70));
            GUILayout.Label(label, PanelStyles.Label);
            GUILayout.EndHorizontal();
            float parsed;
            if (float.TryParse(newText, out parsed)) return parsed;
            return value;
        }

        private static string TextFieldRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            string newText = GUILayout.TextField(value, PanelStyles.TextField, GUILayout.Width(220));
            GUILayout.Label(label, PanelStyles.Label);
            GUILayout.EndHorizontal();
            return newText;
        }

        private static void ColorRow(LocalizationService loc, string key, Color c, Action<Color> set)
        {
            string bufKey = _editingState + ":" + key;

            // Hex input.
            GUILayout.BeginHorizontal();
            if (!ColorHexBuf.TryGetValue(bufKey, out string buf))
                buf = ColorUtil.ToHex(c);
            string newText = GUILayout.TextField(buf, PanelStyles.TextField, GUILayout.Width(90));
            ColorHexBuf[bufKey] = newText;
            if (newText != buf && ColorUtil.TryParseColor(newText, out Color fromText))
                set(fromText);
            GUILayout.Label(loc.Get(key), PanelStyles.Label);
            GUILayout.Label(loc.Get("PANEL_COLOR_HEX"), PanelStyles.Label);
            GUILayout.EndHorizontal();

            // RGBA sliders.
            GUILayout.BeginHorizontal();
            float r = LabeledSlider("R", c.r); GUILayout.Space(4);
            float g = LabeledSlider("G", c.g); GUILayout.Space(4);
            float b = LabeledSlider("B", c.b); GUILayout.Space(4);
            float a = LabeledSlider("A", c.a);
            GUILayout.EndHorizontal();
            if (r != c.r || g != c.g || b != c.b || a != c.a)
            {
                Color next = new Color(r, g, b, a);
                set(next);
                ColorHexBuf[bufKey] = ColorUtil.ToHex(next);
            }
        }

        private static float LabeledSlider(string label, float value)
        {
            GUILayout.Label(label, GUILayout.Width(14));
            return GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.Width(70));
        }

        private static void KeybindRow(LocalizationService loc, string key,
            Func<KeyCode> get, Action<KeyCode> set)
        {
            // Capture a keypress while a rebind is pending. Only used by the
            // standalone panel; integrated mode does not draw this row.
            if (_pendingRebind != null && Event.current.type == EventType.KeyDown)
            {
                KeyCode pressed = Event.current.keyCode;
                if (!IsModifier(pressed))
                {
                    set(pressed);
                    _pendingRebind = null;
                    Event.current.Use();
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(loc.Get(key), PanelStyles.Label, GUILayout.Width(200));
            bool pending = _pendingRebind == key;
            string btn = pending ? loc.Get("PANEL_PRESS_KEY") : get().ToString();
            if (GUILayout.Button(btn, PanelStyles.Button, GUILayout.Width(140)))
                _pendingRebind = pending ? null : key;
            GUILayout.EndHorizontal();
        }

        private static bool IsModifier(KeyCode k)
        {
            return k == KeyCode.LeftShift || k == KeyCode.RightShift
                || k == KeyCode.LeftControl || k == KeyCode.RightControl
                || k == KeyCode.LeftAlt || k == KeyCode.RightAlt
                || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;
        }

        // Single-select language picker, same control as HSRTimer's.
        private static void DrawLanguageSelector(ConfigService cfg, LocalizationService loc)
        {
            if (_langCodes == null) RefreshLanguageList();
            if (_langCodes == null || _langCodes.Length == 0) return;

            int current = Array.IndexOf(_langCodes, cfg.Localization.CurrentCode);
            if (current < 0) current = 0;

            string selected = _langDisplays[current] + "  " + loc.Get("PANEL_LANG_SELECT_HINT");
            if (GUILayout.Button(selected, PanelStyles.Button))
                _langDropdownOpen = !_langDropdownOpen;

            if (_langDropdownOpen)
            {
                for (int i = 0; i < _langDisplays.Length; i++)
                {
                    string item = i == current ? "✓  " + _langDisplays[i] : _langDisplays[i];
                    if (GUILayout.Button(item, PanelStyles.Button))
                    {
                        if (i != current)
                        {
                            cfg.Localization.SetLanguage(_langCodes[i]);
                            cfg.Settings.CurrentLang = cfg.Localization.CurrentCode;
                            RefreshLanguageList();
                        }
                        _langDropdownOpen = false;
                    }
                }
            }
        }

        private static void RefreshLanguageList()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var codes = new List<string>();
            var displays = new List<string>();
            foreach (var lang in cfg.Localization.Languages)
            {
                codes.Add(lang.Code);
                displays.Add(lang.DisplayName ?? lang.Code);
            }
            _langCodes = codes.ToArray();
            _langDisplays = displays.ToArray();
        }
    }
}
