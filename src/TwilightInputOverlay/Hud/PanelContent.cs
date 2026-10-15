using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// The shared IMGUI content for the input-overlay settings. It is drawn both
    /// by the standalone settings panel and by the HSRTimer/TwilightTimer-integrated tab.
    /// The content is split into three drill-down sub-pages — Key Overlay (the
    /// key-grid HUD), Mouse Overlay (cursor region + trail) and Key Layout (the
    /// customizable key layout) — reachable from the root page's entry buttons;
    /// each sub-page has a Back button at the top. The root page also hosts the
    /// preset selector (mirroring HSRTimer's R11 preset UI) directly above the
    /// entry buttons. The Key Layout page drills one level deeper into a row
    /// sub-page. When <c>integrated</c> is true, the controls the timer panel
    /// already owns — the settings-panel keybind and the language selector — are
    /// omitted from the root page. All edits write directly into
    /// <see cref="ConfigService.Settings"/> so they apply live.
    /// </summary>
    internal static class PanelContent
    {
        /// <summary>
        /// Which page of the panel is currently shown. The root page lists the
        /// three sub-pages; each sub-page replaces the whole content area and
        /// has a Back button at the top, mirroring HSRTimer's drill-down
        /// sub-page pattern. The Key Layout sub-page is itself a drill-down:
        /// <see cref="_activeRow"/> &gt;= 0 means a row sub-page is open.
        /// </summary>
        private enum SubPage
        {
            Root,
            KeyOverlay,
            MouseOverlay,
            RainingKeys,
            KeyLayout,
        }

        private static readonly Dictionary<string, string> ColorHexBuf = new Dictionary<string, string>();
        private static string[] _langCodes;
        private static string[] _langDisplays;
        private static bool _langDropdownOpen;
        private static int _editingState; // 0 = idle, 1 = pressed
        private static SubPage _subPage = SubPage.Root;

        // Preset selector state (mirrors HSRTimer's R11 preset UI). The selected
        // preset itself lives in SettingsModel.CurrentPreset; these fields only
        // back the IMGUI controls.
        private static string[] _presetNames;
        private static bool _presetDropdownOpen;
        private static bool _presetCreating;
        private static bool _presetDeleting;
        private static string _presetNewName = "";
        private static string _presetErrorKey;

        // Key Layout drill-down state. Indices are used for rows/keys; after any
        // deletion the affected state is cleared (indices drift, HSRTimer-style).
        private static int _activeRow = -1;         // -1 = key-layout page, else row index
        private static int _expandedKey = -1;       // accordion-expanded key index inside the row
        private static int _confirmDeleteRow = -1;
        private static int _confirmDeleteKey = -1;

        // Text buffers for the key editor's single-char labels and the width
        // field, keyed by the stable KeyEntry id so rows/keys can be deleted
        // without buffers leaking into re-used positions.
        private static readonly Dictionary<string, string> LabelBuf = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> WidthBuf = new Dictionary<string, string>();

        // Pending key rebind. Capture runs at the very top of Draw so no other
        // control consumes the press first.
        private static string _pendingRebind;
        private static Action<KeyCode> _pendingApply;
        private static bool _pendingAllowMouse;

        /// <summary>
        /// Reset transient UI state — the open sub-pages, the language dropdown,
        /// any pending key rebind and the editor buffers — so the standalone
        /// panel always opens on the root page. Static state survives across
        /// panel sessions, so this must be called whenever the panel is (re)opened.
        /// </summary>
        public static void ResetTransientState()
        {
            _subPage = SubPage.Root;
            _langDropdownOpen = false;
            _pendingRebind = null;
            _pendingApply = null;
            _pendingAllowMouse = false;
            _activeRow = -1;
            _expandedKey = -1;
            _confirmDeleteRow = -1;
            _confirmDeleteKey = -1;
            _presetDropdownOpen = false;
            _presetCreating = false;
            _presetDeleting = false;
            _presetNewName = "";
            _presetErrorKey = null;
            _presetNames = null; // re-read the list each time the panel opens
            LabelBuf.Clear();
            WidthBuf.Clear();
        }

        public static void Draw(bool integrated)
        {
            PanelStyles.Ensure();
            CapturePendingRebind();

            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var s = cfg.Settings;
            var loc = cfg.Localization;

            // A sub-page takes over the whole content area; its Back button
            // returns to the parent page and stops rendering the sub-page this frame.
            switch (_subPage)
            {
                case SubPage.KeyOverlay:
                    DrawKeyOverlayPage(loc, cfg);
                    return;
                case SubPage.MouseOverlay:
                    DrawMouseOverlayPage(loc, cfg);
                    return;
                case SubPage.RainingKeys:
                    DrawRainingKeysPage(loc, cfg);
                    return;
                case SubPage.KeyLayout:
                    if (_activeRow >= 0 && _activeRow < s.Rows.Count)
                    {
                        DrawRowPage(loc, cfg, _activeRow);
                        return;
                    }
                    // The active row vanished underneath us; fall back to the
                    // key-layout list page.
                    _activeRow = -1;
                    DrawKeyLayoutPage(loc, cfg);
                    return;
            }

            // ── Root page: panel-general controls (standalone only) + the
            //    preset selector + the entry buttons that drill into the
            //    sub-pages. The preset component sits on the root page, above
            //    the sub-page entry buttons. ──
            if (!integrated)
            {
                Section(loc.Get("PANEL_GENERAL"));
                KeybindRow(loc, "SETTINGS_PANEL_KEY", "panel-key",
                    () => s.PanelKey, k => s.PanelKey = k, allowMouse: false);
                DrawLanguageSelector(cfg, loc);
                if (GUILayout.Button(loc.Get("PANEL_RELOAD_LANGUAGE"), PanelStyles.Button))
                {
                    cfg.ReloadLanguage();
                    RefreshLanguageList();
                }
            }

            // Diagnostics toggle (plugin-specific, available in both modes).
            Section(loc.Get("PANEL_DEBUG"));
            s.DebugInfo = Toggle(loc.Get("SETTINGS_DEBUG_INFO"), s.DebugInfo);

            Section(loc.Get("SETTINGS_PRESET"));
            DrawPresetSelector(cfg, loc);

            GUILayout.Space(6);
            GUILayout.Label(loc.Get("PANEL_SELECT_PAGE"), PanelStyles.Small);
            if (GUILayout.Button(loc.Get("TAB_KEY_OVERLAY"), PanelStyles.Button))
                _subPage = SubPage.KeyOverlay;
            if (GUILayout.Button(loc.Get("TAB_MOUSE_OVERLAY"), PanelStyles.Button))
                _subPage = SubPage.MouseOverlay;
            if (GUILayout.Button(loc.Get("TAB_RAINING_KEYS"), PanelStyles.Button))
                _subPage = SubPage.RainingKeys;
            if (GUILayout.Button(loc.Get("TAB_KEY_LAYOUT"), PanelStyles.Button))
                _subPage = SubPage.KeyLayout;
        }

        // ── Sub-page: Key Overlay (the key-grid HUD) ──
        private static void DrawKeyOverlayPage(LocalizationService loc, ConfigService cfg)
        {
            var s = cfg.Settings;
            GUILayout.Space(6);
            if (GUILayout.Button(loc.Get("PANEL_BACK"), PanelStyles.Button))
            {
                _subPage = SubPage.Root;
                return; // stop rendering the sub-page this frame
            }

            Section(loc.Get("PANEL_HUD"));
            s.ShowHud = Toggle(loc.Get("SETTINGS_SHOW_HUD"), s.ShowHud);
            s.ShowKeyText = Toggle(loc.Get("SETTINGS_SHOW_KEY_TEXT"), s.ShowKeyText);
            s.KeyTextSize = Mathf.Clamp(SliderRow(loc.Get("SETTINGS_KEY_TEXT_SIZE"), s.KeyTextSize, 0.1f, 1.5f), 0.1f, 1.5f);
            s.KeyTextOffsetX = FloatFieldRow(loc.Get("SETTINGS_KEY_TEXT_OFFSET_X"), s.KeyTextOffsetX, "0.##");
            s.KeyTextOffsetY = FloatFieldRow(loc.Get("SETTINGS_KEY_TEXT_OFFSET_Y"), s.KeyTextOffsetY, "0.##");
            s.OffsetX = FloatFieldRow(loc.Get("PANEL_OFFSET_X"), s.OffsetX, "0.##");
            s.OffsetY = FloatFieldRow(loc.Get("PANEL_OFFSET_Y"), s.OffsetY, "0.##");
            s.Scale = Mathf.Max(0.1f, SliderRow(loc.Get("PANEL_SCALE"), s.Scale, 0.1f, 3f));

            Section(loc.Get("PANEL_ANIMATION"));
            s.FadeSpeed = Mathf.Max(0f, SliderRow(loc.Get("SETTINGS_FADE_SPEED"), s.FadeSpeed, 0f, 20f));

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

        // ── Sub-page: Mouse Overlay (cursor region + trail) ──
        private static void DrawMouseOverlayPage(LocalizationService loc, ConfigService cfg)
        {
            var s = cfg.Settings;
            GUILayout.Space(6);
            if (GUILayout.Button(loc.Get("PANEL_BACK"), PanelStyles.Button))
            {
                _subPage = SubPage.Root;
                return; // stop rendering the sub-page this frame
            }

            Section(loc.Get("PANEL_CURSOR"));
            s.ShowCursor = Toggle(loc.Get("SETTINGS_SHOW_CURSOR"), s.ShowCursor);
            s.ShowCursorRegion = Toggle(loc.Get("SETTINGS_SHOW_CURSOR_REGION"), s.ShowCursorRegion);
            s.CursorRawInput = Toggle(loc.Get("SETTINGS_CURSOR_RAW_INPUT"), s.CursorRawInput);
            s.CursorClamp = Toggle(loc.Get("SETTINGS_CURSOR_CLAMP"), s.CursorClamp);
            s.CursorWrap = Toggle(loc.Get("SETTINGS_CURSOR_WRAP"), s.CursorWrap);
            s.CursorRegionX = FloatFieldRow(loc.Get("PANEL_CURSOR_REGION_X"), s.CursorRegionX, "0.##");
            s.CursorRegionY = FloatFieldRow(loc.Get("PANEL_CURSOR_REGION_Y"), s.CursorRegionY, "0.##");
            s.CursorRegionWidth = Mathf.Max(1f, FloatFieldRow(loc.Get("PANEL_CURSOR_REGION_W"), s.CursorRegionWidth, "0.##"));
            s.CursorRegionHeight = Mathf.Max(1f, FloatFieldRow(loc.Get("PANEL_CURSOR_REGION_H"), s.CursorRegionHeight, "0.##"));
            ColorRow(loc, "PANEL_CURSOR_REGION_COLOR", s.CursorRegionColor, c => s.CursorRegionColor = c);
            s.CursorRadius = Mathf.Max(1f, SliderRow(loc.Get("SETTINGS_CURSOR_RADIUS"), s.CursorRadius, 2f, 64f));
            s.CursorSensitivity = Mathf.Max(0f, SliderRow(loc.Get("SETTINGS_CURSOR_SENSITIVITY"), s.CursorSensitivity, 0.1f, 10f));
            s.TrailMaxStretch = Mathf.Max(1f, SliderRow(loc.Get("SETTINGS_TRAIL_STRETCH"), s.TrailMaxStretch, 1f, 4f));
            s.TrailResponse = Mathf.Max(0f, SliderRow(loc.Get("SETTINGS_TRAIL_RESPONSE"), s.TrailResponse, 0f, 30f));
            ColorRow(loc, "PANEL_CURSOR_COLOR", s.CursorColor, c => s.CursorColor = c);
        }

        // ── Sub-page: Raining Keys (bars rising from the first row) ──
        private static void DrawRainingKeysPage(LocalizationService loc, ConfigService cfg)
        {
            var s = cfg.Settings;
            GUILayout.Space(6);
            if (GUILayout.Button(loc.Get("PANEL_BACK"), PanelStyles.Button))
            {
                _subPage = SubPage.Root;
                return; // stop rendering the sub-page this frame
            }

            Section(loc.Get("PANEL_RAINING"));
            GUILayout.Label(loc.Get("SETTINGS_RAINING_HINT"), PanelStyles.Small);
            s.RainingEnabled = Toggle(loc.Get("SETTINGS_RAINING_ENABLED"), s.RainingEnabled);
            s.RainingHeight = IntFieldRow(loc.Get("SETTINGS_RAINING_HEIGHT"), s.RainingHeight, "raining:height", 1, 500);
            s.RainingGap = IntFieldRow(loc.Get("SETTINGS_RAINING_GAP"), s.RainingGap, "raining:gap", 0, 200);
            s.RainingSpeed = IntFieldRow(loc.Get("SETTINGS_RAINING_SPEED"), s.RainingSpeed, "raining:speed", 1, 1000);
            s.RainingWidth = Mathf.Max(0f, FloatFieldRow(loc.Get("SETTINGS_RAINING_WIDTH"), s.RainingWidth, "0.##"));
            s.RainingRadius = Mathf.Max(0f, SliderRow(loc.Get("SETTINGS_RAINING_RADIUS"), s.RainingRadius, 0f, 24f));
            ColorRow(loc, "PANEL_RAINING_COLOR", s.RainingColor, c => s.RainingColor = c);
        }

        // ── Sub-page: Key Layout (the customizable key layout) ──
        private static void DrawKeyLayoutPage(LocalizationService loc, ConfigService cfg)
        {
            var s = cfg.Settings;
            GUILayout.Space(6);
            if (GUILayout.Button(loc.Get("PANEL_BACK"), PanelStyles.Button))
            {
                _subPage = SubPage.Root;
                return;
            }

            Section(loc.Get("KEY_LAYOUT_ROWS"));
            GUILayout.Label(loc.Get("KEY_LAYOUT_HINT"), PanelStyles.Small);

            for (int i = 0; i < s.Rows.Count; i++)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(RowTitle(loc, s.Rows[i], i), PanelStyles.Button, GUILayout.ExpandWidth(true)))
                {
                    _activeRow = i;
                    _expandedKey = -1;
                    _confirmDeleteRow = -1;
                    _confirmDeleteKey = -1;
                }
                MoveButton(loc, "PANEL_MOVE_UP", i > 0, () => MoveRow(s, i, i - 1));
                MoveButton(loc, "PANEL_MOVE_DOWN", i < s.Rows.Count - 1, () => MoveRow(s, i, i + 1));
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button(loc.Get("KEY_LAYOUT_ADD_ROW"), PanelStyles.Button))
            {
                s.Rows.Add(new KeyRow());
                // Jump straight into the new row so it is immediately editable.
                _activeRow = s.Rows.Count - 1;
                _expandedKey = -1;
                _confirmDeleteRow = -1;
                _confirmDeleteKey = -1;
            }
        }

        private static string RowTitle(LocalizationService loc, KeyRow row, int index)
        {
            var parts = new List<string>();
            foreach (var e in row.Keys)
            {
                if (e.Blank)
                    parts.Add("_");
                else if (e.Width == 1 && e.Dual)
                    parts.Add((e.Label ?? "") + "/" + (e.Label2 ?? ""));
                else
                    parts.Add(e.Label ?? "");
            }
            string summary = parts.Count == 0 ? loc.Get("KEY_LAYOUT_EMPTY_ROW") : string.Join(" ", parts.ToArray());
            return loc.Get("KEY_LAYOUT_ROW", index + 1) + ": " + summary;
        }

        // ── Sub-sub-page: one row (list of keys + delete row) ──
        private static void DrawRowPage(LocalizationService loc, ConfigService cfg, int rowIndex)
        {
            var row = cfg.Settings.Rows[rowIndex];
            GUILayout.Space(6);
            if (GUILayout.Button(loc.Get("PANEL_BACK"), PanelStyles.Button))
            {
                _activeRow = -1;
                _expandedKey = -1;
                _confirmDeleteRow = -1;
                _confirmDeleteKey = -1;
                return;
            }

            Section(loc.Get("KEY_LAYOUT_KEYS"));
            for (int i = 0; i < row.Keys.Count; i++)
                DrawKeyItem(loc, row, i);

            if (GUILayout.Button(loc.Get("KEY_LAYOUT_ADD_KEY"), PanelStyles.Button))
            {
                var e = new KeyEntry();
                row.Keys.Add(e);
                _expandedKey = row.Keys.Count - 1; // open the new key immediately
                _confirmDeleteKey = -1;
            }

            // Bottom of the row page: delete the whole row with confirmation.
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            bool confirmingRow = _confirmDeleteRow == rowIndex;
            if (GUILayout.Button(confirmingRow ? loc.Get("PANEL_CONFIRM_DELETE") : loc.Get("KEY_LAYOUT_DELETE_ROW"),
                    PanelStyles.Button, GUILayout.Width(180)))
            {
                if (!confirmingRow) _confirmDeleteRow = rowIndex;
                else DeleteRow(cfg.Settings, rowIndex);
            }
            if (confirmingRow && GUILayout.Button(loc.Get("PANEL_CANCEL"), PanelStyles.Button, GUILayout.Width(120)))
                _confirmDeleteRow = -1;
            GUILayout.EndHorizontal();
        }

        private static void DeleteRow(SettingsModel s, int rowIndex)
        {
            s.Rows.RemoveAt(rowIndex);
            _activeRow = -1;
            _expandedKey = -1;
            _confirmDeleteRow = -1;
            _confirmDeleteKey = -1;
        }

        // ── One key: accordion header + (when expanded) the full editor ──
        private static void DrawKeyItem(LocalizationService loc, KeyRow row, int keyIndex)
        {
            var e = row.Keys[keyIndex];
            bool expanded = _expandedKey == keyIndex;
            string title = (expanded ? "▾ " : "▸ ") + KeyItemTitle(loc, e);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(title, PanelStyles.Button, GUILayout.ExpandWidth(true)))
            {
                _expandedKey = expanded ? -1 : keyIndex;
                _confirmDeleteKey = -1;
            }
            MoveButton(loc, "PANEL_MOVE_UP", keyIndex > 0, () => MoveKey(row, keyIndex, keyIndex - 1));
            MoveButton(loc, "PANEL_MOVE_DOWN", keyIndex < row.Keys.Count - 1, () => MoveKey(row, keyIndex, keyIndex + 1));
            GUILayout.EndHorizontal();
            if (!expanded)
                return;

            // ── Key editor ──
            // 1. Display character (single char).
            string label = SingleCharField(loc, "KEY_LABEL", e.Label, "label:" + e.Id);
            e.Label = label;

            // 2. Bound key.
            KeybindRow(loc, "KEY_BINDING", "kb:" + e.Id + ":1",
                () => e.Key1, k => e.Key1 = k, allowMouse: true);

            // 3. Width (default 1); > 1 draws like the space key.
            e.Width = IntFieldRow(loc.Get("KEY_WIDTH"), e.Width, "width:" + e.Id,
                SettingsModel.KeyWidthMin, SettingsModel.KeyWidthMax);

            // 4. Dual keybind — only for width-1 keys; a second keybind set at
            //    another width stays stored but is inert (not shown, not used).
            if (e.Width == 1)
            {
                e.Dual = Toggle(loc.Get("KEY_DUAL"), e.Dual);
                if (e.Dual)
                {
                    string label2 = SingleCharField(loc, "KEY_LABEL_2", e.Label2, "label2:" + e.Id);
                    e.Label2 = label2;
                    KeybindRow(loc, "KEY_BINDING_2", "kb:" + e.Id + ":2",
                        () => e.Key2, k => e.Key2 = k, allowMouse: true);
                }
            }

            // 5. Blank key: reserves the width, no behaviour, no drawing.
            e.Blank = Toggle(loc.Get("KEY_BLANK"), e.Blank);

            // 6. Delete this key with confirmation (bottom of the editor).
            GUILayout.BeginHorizontal();
            bool confirmingKey = _confirmDeleteKey == keyIndex;
            if (GUILayout.Button(confirmingKey ? loc.Get("PANEL_CONFIRM_DELETE") : loc.Get("KEY_DELETE"),
                    PanelStyles.Button, GUILayout.Width(180)))
            {
                if (!confirmingKey) _confirmDeleteKey = keyIndex;
                else DeleteKey(row, keyIndex);
            }
            if (confirmingKey && GUILayout.Button(loc.Get("PANEL_CANCEL"), PanelStyles.Button, GUILayout.Width(120)))
                _confirmDeleteKey = -1;
            GUILayout.EndHorizontal();
        }

        private static string KeyItemTitle(LocalizationService loc, KeyEntry e)
        {
            if (e.Blank)
                return loc.Get("KEY_BLANK_SHORT");
            string s = (string.IsNullOrEmpty(e.Label) ? "?" : e.Label) + "  " + KeyName(e.Key1);
            if (e.Width == 1 && e.Dual)
                s += " / " + (string.IsNullOrEmpty(e.Label2) ? "?" : e.Label2) + " " + KeyName(e.Key2);
            if (e.Width > 1)
                s += "  x" + e.Width;
            return s;
        }

        private static void DeleteKey(KeyRow row, int keyIndex)
        {
            var e = row.Keys[keyIndex];
            LabelBuf.Remove("label:" + e.Id);
            LabelBuf.Remove("label2:" + e.Id);
            WidthBuf.Remove("width:" + e.Id);
            row.Keys.RemoveAt(keyIndex);
            _expandedKey = -1;   // indices drifted; clear the accordion state
            _confirmDeleteKey = -1;
        }

        // ── reorder helpers (Up / Down on row and key list items) ──

        /// <summary>
        /// A fixed-width Up/Down button; disabled (greyed out) when the move is
        /// not possible so the list's first/last edge is self-explanatory.
        /// </summary>
        private static void MoveButton(LocalizationService loc, string labelKey, bool enabled, Action move)
        {
            bool prev = GUI.enabled;
            if (!enabled) GUI.enabled = false;
            if (GUILayout.Button(loc.Get(labelKey), PanelStyles.Button, GUILayout.Width(64)))
                move();
            GUI.enabled = prev;
        }

        private static void MoveRow(SettingsModel s, int from, int to)
        {
            var row = s.Rows[from];
            s.Rows.RemoveAt(from);
            s.Rows.Insert(to, row);
            // Keep index-based transient state pointing at the same item.
            _activeRow = RemapIndex(_activeRow, from, to);
            _confirmDeleteRow = RemapIndex(_confirmDeleteRow, from, to);
        }

        private static void MoveKey(KeyRow row, int from, int to)
        {
            var e = row.Keys[from];
            row.Keys.RemoveAt(from);
            row.Keys.Insert(to, e);
            _expandedKey = RemapIndex(_expandedKey, from, to);
            _confirmDeleteKey = RemapIndex(_confirmDeleteKey, from, to);
        }

        private static int RemapIndex(int idx, int from, int to)
        {
            if (idx == from) return to;
            if (idx == to) return from;
            return idx;
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

        /// <summary>
        /// Single-character text input with a stable buffer (IMGUI fields are
        /// stateless). Extra characters typed/pasted are cut to the first one so
        /// the model always holds a single display character.
        /// </summary>
        private static string SingleCharField(LocalizationService loc, string labelKey, string value, string bufKey)
        {
            GUILayout.BeginHorizontal();
            string buf;
            if (!LabelBuf.TryGetValue(bufKey, out buf))
            {
                buf = value;
                LabelBuf[bufKey] = buf;
            }
            string next = GUILayout.TextField(buf, PanelStyles.TextField, GUILayout.Width(60));
            if (next.Length > 1) next = next.Substring(0, 1);
            LabelBuf[bufKey] = next;
            GUILayout.Label(loc.Get(labelKey), PanelStyles.Label);
            GUILayout.EndHorizontal();
            return next;
        }

        /// <summary>
        /// Integer input with the buffer pattern from the HSRTimer reference:
        /// the field keeps whatever the user typed, the model gets the parsed
        /// (clamped) value, and when the user is not typing the buffer snaps
        /// back to the effective value so external clamps are reflected.
        /// </summary>
        private static int IntFieldRow(string label, int value, string bufKey, int min, int max)
        {
            GUILayout.BeginHorizontal();
            string buf;
            if (!WidthBuf.TryGetValue(bufKey, out buf))
            {
                buf = value.ToString(CultureInfo.InvariantCulture);
                WidthBuf[bufKey] = buf;
            }
            string next = GUILayout.TextField(buf, PanelStyles.TextField, GUILayout.Width(80));
            bool editing = next != buf;
            WidthBuf[bufKey] = next;
            GUILayout.Label(label, PanelStyles.Label);
            GUILayout.EndHorizontal();

            int parsed;
            if (int.TryParse(next.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                int clamped = Mathf.Clamp(parsed, min, max);
                if (!editing && clamped != value)
                    WidthBuf[bufKey] = clamped.ToString(CultureInfo.InvariantCulture);
                return clamped;
            }
            return value; // unparseable (half-typed) → keep the old value
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

        // ── keybind capture + row ──

        /// <summary>
        /// Capture the press for an in-progress rebind. Runs at the very top of
        /// <see cref="Draw"/> so no other control consumes the event first.
        /// Keyboard keys bind on KeyDown; mouse buttons (including left/right,
        /// needed for the hand keys) bind on MouseDown. Unity's IMGUI event
        /// stream only reports mouse buttons 0-2, so the side buttons (Mouse3+)
        /// are captured by polling in <see cref="CapturePolledMouseButton"/>.
        /// Escape cancels.
        /// </summary>
        private static void CapturePendingRebind()
        {
            if (_pendingRebind == null) return;

            if (Event.current.type == EventType.KeyDown)
            {
                KeyCode pressed = Event.current.keyCode;
                if (pressed == KeyCode.Escape)
                {
                    _pendingRebind = null;
                    _pendingApply = null;
                    _pendingAllowMouse = false;
                    return; // let the escape event propagate (e.g. panel close)
                }
                if (!IsModifier(pressed))
                {
                    ApplyPending(pressed);
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseDown && _pendingAllowMouse)
            {
                KeyCode pressed = MouseKeyCodeForButton(Event.current.button);
                if (pressed != KeyCode.None)
                {
                    ApplyPending(pressed);
                    Event.current.Use();
                }
            }

            // IMGUI events never carry mouse buttons 3+ (the side buttons), so a
            // mouse-armed rebind must also poll the raw button state to catch them.
            if (_pendingAllowMouse && _pendingRebind != null)
                CapturePolledMouseButton();
        }

        /// <summary>
        /// Unity's IMGUI event stream only carries mouse buttons 0-2 (left,
        /// right, middle); the extra buttons (XButton1/XButton2 and beyond,
        /// KeyCode.Mouse3-Mouse6) never arrive as an
        /// <see cref="EventType.MouseDown"/>. Poll the raw button state instead.
        /// <see cref="Input.GetMouseButtonDown"/> is true for exactly one frame,
        /// so each press is applied once. Runs only while a mouse-armed rebind is
        /// pending; buttons 0-2 are already handled by the event path above.
        /// </summary>
        private static void CapturePolledMouseButton()
        {
            for (int button = 3; button <= 6; button++)
            {
                if (Input.GetMouseButtonDown(button))
                {
                    ApplyPending((KeyCode)((int)KeyCode.Mouse0 + button));
                    break;
                }
            }
        }

        private static void ApplyPending(KeyCode pressed)
        {
            var apply = _pendingApply;
            _pendingRebind = null;
            _pendingApply = null;
            _pendingAllowMouse = false;
            if (apply != null)
                apply(pressed);
        }

        private static void KeybindRow(LocalizationService loc, string key, string id,
            Func<KeyCode> get, Action<KeyCode> set, bool allowMouse = false)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(loc.Get(key), PanelStyles.Label, GUILayout.Width(180));
            bool pending = _pendingRebind == id;
            string btn = pending ? loc.Get("PANEL_PRESS_KEY_CANCEL") : KeyName(get());
            if (GUILayout.Button(btn, PanelStyles.Button, GUILayout.Width(150)))
            {
                if (pending)
                {
                    // Keyboard-armed rows cancel by clicking again; mouse-armed
                    // rows cannot (the click would bind Mouse0) and use Esc.
                    _pendingRebind = null;
                    _pendingApply = null;
                    _pendingAllowMouse = false;
                }
                else
                {
                    _pendingRebind = id;
                    _pendingApply = set;
                    _pendingAllowMouse = allowMouse;
                }
            }
            GUILayout.EndHorizontal();
        }

        private static KeyCode MouseKeyCodeForButton(int button)
        {
            if (button < 0 || button > 6) return KeyCode.None;
            return (KeyCode)((int)KeyCode.Mouse0 + button);
        }

        private static string KeyName(KeyCode kc)
        {
            return kc == KeyCode.None ? "-" : kc.ToString();
        }

        private static bool IsModifier(KeyCode k)
        {
            return k == KeyCode.LeftShift || k == KeyCode.RightShift
                || k == KeyCode.LeftControl || k == KeyCode.RightControl
                || k == KeyCode.LeftAlt || k == KeyCode.RightAlt
                || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;
        }

        // Single-select language picker, same control as HSRTimer's/TwilightTimer's.
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

        // ── presets (mirrors HSRTimer's R11 preset UI) ──

        /// <summary>
        /// Single-select preset picker. Mirrors the language dropdown: a button
        /// + collapsible list, with "New preset" at the bottom. The selection is
        /// a real config item (SettingsModel.CurrentPreset) persisted via the
        /// normal SaveSettings path; selecting a preset only switches the
        /// selection, "Load preset" applies its snapshot to the live config.
        /// </summary>
        private static void DrawPresetSelector(ConfigService cfg, LocalizationService loc)
        {
            if (_presetNames == null) RefreshPresetList();
            var s = cfg.Settings;
            if (_presetNames == null) return;

            string current = s.CurrentPreset;
            if (!PresetStore.Exists(current))
                current = PresetStore.DefaultPresetName;

            string selected = (_presetDropdownOpen ? "▾ " : "▸ ") + current + "  " + loc.Get("SETTINGS_PRESET_SELECT_HINT");
            if (GUILayout.Button(selected, PanelStyles.Button))
                _presetDropdownOpen = !_presetDropdownOpen;

            if (_presetDropdownOpen)
            {
                foreach (var name in _presetNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    string item = string.Equals(name, s.CurrentPreset, StringComparison.Ordinal) ? "✓  " + name : name;
                    if (GUILayout.Button(item, PanelStyles.Button))
                    {
                        if (!string.Equals(name, s.CurrentPreset, StringComparison.Ordinal))
                        {
                            s.CurrentPreset = name;
                            cfg.SaveSettings();
                            _presetErrorKey = null;
                            _presetCreating = false;
                            _presetDeleting = false;
                        }
                        _presetDropdownOpen = false;
                    }
                }

                // New-preset input row appears directly above the New preset
                // button, below all existing preset options.
                if (_presetCreating)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(loc.Get("SETTINGS_PRESET_NEW_NAME"), PanelStyles.Label);
                    _presetNewName = GUILayout.TextField(_presetNewName, PanelStyles.TextField, GUILayout.Width(160));
                    if (GUILayout.Button(loc.Get("SETTINGS_PRESET_CONFIRM"), PanelStyles.Button, GUILayout.Width(80)))
                        ConfirmNewPreset(cfg);
                    if (GUILayout.Button(loc.Get("SETTINGS_PRESET_CANCEL"), PanelStyles.Button, GUILayout.Width(80)))
                    {
                        _presetCreating = false;
                        _presetNewName = "";
                        _presetErrorKey = null;
                    }
                    GUILayout.EndHorizontal();
                    if (_presetErrorKey != null)
                        GUILayout.Label(loc.Get(_presetErrorKey), PanelStyles.Small);
                }

                if (GUILayout.Button(loc.Get("SETTINGS_PRESET_NEW"), PanelStyles.Button))
                {
                    _presetCreating = !_presetCreating;
                    _presetDeleting = false;
                    _presetNewName = "";
                    _presetErrorKey = null;
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(loc.Get("SETTINGS_PRESET_LOAD"), PanelStyles.Button))
            {
                if (PresetStore.LoadCurrent(cfg))
                    _presetErrorKey = null;
                else
                    _presetErrorKey = "SETTINGS_PRESET_LOAD_FAILED";
            }
            if (GUILayout.Button(loc.Get("SETTINGS_PRESET_SAVE"), PanelStyles.Button))
            {
                if (PresetStore.SaveToCurrent(cfg))
                    _presetErrorKey = null;
                else
                    _presetErrorKey = "SETTINGS_PRESET_SAVE_FAILED";
            }
            GUILayout.EndHorizontal();

            bool isDefault = string.Equals(s.CurrentPreset, PresetStore.DefaultPresetName, StringComparison.OrdinalIgnoreCase);
            if (!isDefault)
            {
                // Delete with confirmation: the single button expands into a
                // "Confirm delete" / "Cancel" pair so an accidental click
                // cannot destroy a preset. The confirm state is dropped when
                // the panel reopens or the selection changes.
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_presetDeleting ? loc.Get("SETTINGS_PRESET_DELETE_CONFIRM") : loc.Get("SETTINGS_PRESET_DELETE"), PanelStyles.Button))
                {
                    if (!_presetDeleting)
                    {
                        _presetDeleting = true;
                        _presetErrorKey = null;
                    }
                    else if (PresetStore.DeleteCurrent(cfg))
                    {
                        RefreshPresetList();
                        _presetDropdownOpen = false;
                        _presetCreating = false;
                        _presetDeleting = false;
                        _presetErrorKey = null;
                    }
                    else
                    {
                        _presetErrorKey = "SETTINGS_PRESET_DELETE_FAILED";
                    }
                }
                if (_presetDeleting && GUILayout.Button(loc.Get("SETTINGS_PRESET_CANCEL"), PanelStyles.Button))
                {
                    _presetDeleting = false;
                    _presetErrorKey = null;
                }
                GUILayout.EndHorizontal();
            }

            if (_presetErrorKey != null)
                GUILayout.Label(loc.Get(_presetErrorKey), PanelStyles.Small);
        }

        private static void ConfirmNewPreset(ConfigService cfg)
        {
            string errorKey;
            if (PresetStore.TryCreate(_presetNewName, cfg, out errorKey))
            {
                RefreshPresetList();
                _presetCreating = false;
                _presetNewName = "";
                _presetErrorKey = null;
                // Keep the dropdown open so the new option is visible immediately.
                _presetDropdownOpen = true;
            }
            else
            {
                _presetErrorKey = errorKey;
            }
        }

        private static void RefreshPresetList()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            _presetNames = PresetStore.ListPresets();
            if (System.Array.IndexOf(_presetNames, cfg.Settings.CurrentPreset) < 0)
                cfg.Settings.CurrentPreset = PresetStore.DefaultPresetName;
        }
    }
}
