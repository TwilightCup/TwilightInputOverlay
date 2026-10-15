using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// The three color parts of one key state: text, border, and fill. Both the
    /// idle and pressed states own one of these; every key shares the same
    /// colors within a state.
    /// </summary>
    public sealed class ButtonStyle
    {
        public Color Text = new Color(0f, 0f, 0f, 1f);
        public Color Border = new Color(0f, 0f, 0f, 0f);
        public Color Fill = new Color(1f, 1f, 1f, 0.30f);

        public static ButtonStyle DefaultIdle()
        {
            return new ButtonStyle
            {
                Text = new Color(0f, 0f, 0f, 1f),
                Border = new Color(0f, 0f, 0f, 0f),
                Fill = new Color(1f, 1f, 1f, 0.30f),
            };
        }

        public static ButtonStyle DefaultPressed()
        {
            return new ButtonStyle
            {
                Text = new Color(1f, 1f, 1f, 1f),
                Border = new Color(0f, 0f, 0f, 0f),
                Fill = new Color(0f, 0f, 0f, 1f),
            };
        }
    }

    /// <summary>
    /// One "key" of the customizable HUD layout: a single displayed unit that
    /// listens to one key (or, when <see cref="Dual"/> is set and the width is
    /// 1, two keys shown as two half-width labels like the old left/right hand
    /// cell). A width &gt; 1 draws one wide cell that absorbs the internal
    /// spacings (like the old jump/space key). <see cref="Blank"/> turns the
    /// unit into an empty slot that still reserves its width but has neither
    /// behaviour nor drawing.
    /// </summary>
    public sealed class KeyEntry
    {
        /// <summary>Session-unique identity, used by the HUD for fade tracking.</summary>
        public int Id;

        public string Label = "";
        public KeyCode Key1 = KeyCode.None;

        /// <summary>Width in grid cells; &gt; 1 behaves like the space key.</summary>
        public int Width = 1;

        /// <summary>Only effective when <see cref="Width"/> == 1: a second
        /// label/keybind shown as the right half of the unit.</summary>
        public bool Dual = false;
        public string Label2 = "";
        public KeyCode Key2 = KeyCode.None;

        /// <summary>Empty slot: reserves the width, no behaviour, no drawing.</summary>
        public bool Blank = false;

        public KeyEntry()
        {
            Id = SettingsModel.AllocateKeyId();
        }
    }

    /// <summary>One row of the customizable HUD layout: keys run left to right,
    /// rows stack top to bottom.</summary>
    public sealed class KeyRow
    {
        public List<KeyEntry> Keys = new List<KeyEntry>();
    }

    /// <summary>
    /// All user-tunable settings for the input overlay. Persisted to
    /// settings.ini as human-readable text; missing keys fall back to defaults.
    /// </summary>
    public sealed class SettingsModel
    {
        private static int _nextKeyId = 1;

        /// <summary>Monotonic id allocator for key units (never reused within a session).</summary>
        public static int AllocateKeyId() => _nextKeyId++;

        /// <summary>Bounds for a key's width in grid cells (keeps the HUD and its
        /// baked textures within sane dimensions).</summary>
        public const int KeyWidthMin = 1;
        public const int KeyWidthMax = 32;

        /// <summary>
        /// The customizable HUD layout: rows top-to-bottom, keys left-to-right.
        /// Falls back to the fixed classic layout (play dead / forward / hands,
        /// movement, jump) when the config has no row sections.
        /// </summary>
        public List<KeyRow> Rows = new List<KeyRow>();

        public bool ShowHud = true;
        public bool ShowKeyText = true;

        // Key text: font size as a fraction of one grid cell (0.5 = the classic
        // size), and the label's offset from the text origin — a fixed point
        // just above each key's centre (positive X = right, positive Y = up;
        // see InputHud.TextOriginY). The offsets scale with the HUD like the
        // other geometry values so the text stays proportionally placed.
        public float KeyTextSize = 0.5f;
        public float KeyTextOffsetX = 0f;
        public float KeyTextOffsetY = 0f;

        // Bottom-left anchored HUD placement/scaling.
        public float OffsetX = 16f;
        public float OffsetY = 16f;
        public float Scale = 1f;

        // Key-grid geometry.
        public float Spacing = 0f;
        public float CornerRadius = 0f;

        // Idle/pressed transition animation speed. Higher fades faster; 0 disables.
        public float FadeSpeed = 8f;

        // Mouse-cursor overlay: a bounded region in which a tinted circle follows
        // the mouse. The cursor rotates toward its motion direction and grows a
        // rear half-ellipse trail that stretches with speed, then recovers.
        public bool ShowCursor = true;
        // Draw the region rectangle itself as a tinted backdrop behind the cursor.
        public bool ShowCursorRegion = true;
        // Read the raw mouse movement axes instead of the hardware cursor's screen
        // position. The movement axes keep reporting while the OS cursor is pinned
        // at a screen edge (e.g. in a menu), so the overlay cursor keeps moving.
        public bool CursorRawInput = true;
        // true = leave the region from the opposite edge; false = snap back to the centre.
        public bool CursorWrap = true;
        // true = clamp the cursor to the region bounds so it never leaves the
        // region (it stops at the edge); takes precedence over CursorWrap.
        public bool CursorClamp = false;
        // Region rectangle, anchored to the screen's bottom-right corner, so
        // CursorRegionX is the distance from the right edge and CursorRegionY
        // the distance from the bottom edge.
        public float CursorRegionX = 40f;
        public float CursorRegionY = 40f;
        public float CursorRegionWidth = 360f;
        public float CursorRegionHeight = 240f;
        public Color CursorRegionColor = new Color(1f, 1f, 1f, 0.12f);
        public float CursorRadius = 12f;
        public float CursorSensitivity = 1f;
        public float TrailMaxStretch = 2.5f;
        public float TrailResponse = 12f;
        public Color CursorColor = new Color(1f, 1f, 1f, 1f);

        // Raining Keys: bars rise from the top of the first-row keys into a
        // rectangular region above them (width = first-row width, height =
        // RainingHeight, its bottom edge sits RainingGap above the key tops).
        // While a key is held the bar's top extends upward at RainingSpeed;
        // after release the whole bar keeps translating up until its bottom
        // passes the region's top edge, then it disappears. All these values
        // are HUD-space and scale with Scale, like spacing / corner radius.
        public bool RainingEnabled = false;
        public int RainingHeight = 160;   // region height (HUD units)
        public int RainingGap = 0;        // region bottom edge vs key tops (HUD units)
        public int RainingSpeed = 200;    // flow speed (HUD units / second)
        public float RainingWidth = 0f;      // 0 = match the key's own width
        public float RainingRadius = 0f;     // bar corner radius (HUD units); 0 = square
        public Color RainingColor = new Color(1f, 1f, 1f, 1f);

        // Standalone-panel keybind; not shown when integrated into a timer's panel.
        public KeyCode PanelKey = KeyCode.Home;

        // Diagnostics: when on, the Raining Keys overlay logs per-second stats
        // (bar counts/heights, rounded-rect texture-cache churn, managed memory)
        // to the BepInEx log. A global preference like the panel key and
        // language, so it is excluded from preset snapshots.
        public bool DebugInfo = false;

        public string CurrentLang = "en";

        // ── Presets (R11) ───────────────────────────────────────────────
        // The currently selected preset. The selection itself is a normal config
        // item ([Presets] Current) so it survives restarts; there is
        // intentionally NO separate "presets initialized" flag (first-load /
        // upgrade is detected by the presence of the presets directory and the
        // default preset, see PresetStore.EnsureInitialized).
        public string CurrentPreset = PresetStore.DefaultPresetName;

        public ButtonStyle Idle = ButtonStyle.DefaultIdle();
        public ButtonStyle Pressed = ButtonStyle.DefaultPressed();

        private const string MainSection = "settings";
        private const string IdleSection = "idle";
        private const string PressedSection = "pressed";
        private const string PresetsSection = "Presets";

        public void Load()
        {
            // Idempotent: Rows are rebuilt from the file below, so a preset
            // load can call Load() again without stale rows/keys surviving.
            Rows.Clear();

            int rowCount = -1;      // -1 = not specified by the file
            bool sawRowSection = false;

            foreach (var p in PersistenceService.Read(PersistenceService.PathFor("settings.ini")))
            {
                if (p.Section == MainSection)
                {
                    // row_count lives in the main section but is applied to the
                    // layout, not to a scalar setting.
                    if (p.Key == "row_count")
                    {
                        int rc;
                        if (int.TryParse(p.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rc) && rc >= 0)
                            rowCount = rc;
                        continue;
                    }
                    Apply(p.Key, p.Value);
                }
                else if (p.Section == IdleSection)
                    ApplyStyle(Idle, p.Key, p.Value, IdleSection);
                else if (p.Section == PressedSection)
                    ApplyStyle(Pressed, p.Key, p.Value, PressedSection);
                else if (p.Section == PresetsSection)
                    ApplyPresets(p.Key, p.Value);
                else if (IsRowSection(p.Section))
                {
                    sawRowSection = true;
                    ApplyRow(p.Section, p.Key, p.Value);
                }
            }

            if (rowCount >= 0)
            {
                // Explicit count wins (covers the "all rows deleted" case where
                // there are no row sections left to trigger a default fallback).
                while (Rows.Count < rowCount) Rows.Add(new KeyRow());
                if (Rows.Count > rowCount) Rows.RemoveRange(rowCount, Rows.Count - rowCount);
            }
            else if (!sawRowSection)
            {
                DefaultLayout();
            }
        }

        /// <summary>Seed the classic fixed HUD as the default custom layout.</summary>
        private void DefaultLayout()
        {
            Rows.Clear();

            var row0 = new KeyRow();
            row0.Keys.Add(NewKey("Y", KeyCode.Y, 1, false, "", KeyCode.None));
            row0.Keys.Add(NewKey("W", KeyCode.W, 1, false, "", KeyCode.None));
            row0.Keys.Add(NewKey("L", KeyCode.Mouse0, 1, true, "R", KeyCode.Mouse1)); // hands
            Rows.Add(row0);

            var row1 = new KeyRow();
            row1.Keys.Add(NewKey("A", KeyCode.A, 1, false, "", KeyCode.None));
            row1.Keys.Add(NewKey("S", KeyCode.S, 1, false, "", KeyCode.None));
            row1.Keys.Add(NewKey("D", KeyCode.D, 1, false, "", KeyCode.None));
            Rows.Add(row1);

            var row2 = new KeyRow();
            row2.Keys.Add(NewKey("—", KeyCode.Space, 3, false, "", KeyCode.None)); // jump
            Rows.Add(row2);
        }

        private static KeyEntry NewKey(string label, KeyCode key1, int width, bool dual, string label2, KeyCode key2)
        {
            return new KeyEntry
            {
                Label = label,
                Key1 = key1,
                Width = width,
                Dual = dual,
                Label2 = label2,
                Key2 = key2,
            };
        }

        private static bool IsRowSection(string section)
        {
            if (string.IsNullOrEmpty(section) || section.Length < 4)
                return false;
            if (section[0] != 'r' || section[1] != 'o' || section[2] != 'w')
                return false;
            for (int i = 3; i < section.Length; i++)
                if (!char.IsDigit(section[i]))
                    return false;
            return true;
        }

        private void ApplyRow(string section, string key, string value)
        {
            try
            {
                int rowIndex = int.Parse(section.Substring(3), CultureInfo.InvariantCulture);
                if (rowIndex < 0) return;
                while (Rows.Count <= rowIndex) Rows.Add(new KeyRow());
                var row = Rows[rowIndex];

                // key format: key{N}_{field}
                if (key.Length < 7 || key[0] != 'k' || key[1] != 'e' || key[2] != 'y')
                    return;
                int underscore = key.IndexOf('_');
                if (underscore <= 3 || underscore >= key.Length - 1)
                    return;
                int keyIndex;
                if (!int.TryParse(key.Substring(3, underscore - 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out keyIndex))
                    return;
                if (keyIndex < 0) return;
                while (row.Keys.Count <= keyIndex) row.Keys.Add(new KeyEntry());
                var e = row.Keys[keyIndex];

                switch (key.Substring(underscore + 1))
                {
                    case "label": e.Label = SingleChar(value); break;
                    case "key1": e.Key1 = ParseKeyCode(value, e.Key1); break;
                    case "width": e.Width = Mathf.Clamp(ParseInt(value, e.Width), KeyWidthMin, KeyWidthMax); break;
                    case "dual": e.Dual = ParseBool(value, e.Dual); break;
                    case "label2": e.Label2 = SingleChar(value); break;
                    case "key2": e.Key2 = ParseKeyCode(value, e.Key2); break;
                    case "blank": e.Blank = ParseBool(value, e.Blank); break;
                    default:
                        Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: unknown row key '{key}', ignored.");
                        break;
                }
            }
            catch
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: bad row line '{key}' = '{value}', ignored.");
            }
        }

        /// <summary>Labels are single characters; keep only the first UTF-16 unit.</summary>
        private static string SingleChar(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length > 1 ? value.Substring(0, 1) : value;
        }

        private void Apply(string key, string value)
        {
            try
            {
                switch (key)
                {
                    case "show_hud": ShowHud = ParseBool(value, ShowHud); break;
                    case "show_key_text": ShowKeyText = ParseBool(value, ShowKeyText); break;
                    case "key_text_size": KeyTextSize = Mathf.Clamp(ParseFloat(value, KeyTextSize), 0.1f, 1.5f); break;
                    case "key_text_offset_x": KeyTextOffsetX = ParseFloat(value, KeyTextOffsetX); break;
                    case "key_text_offset_y": KeyTextOffsetY = ParseFloat(value, KeyTextOffsetY); break;
                    case "offset_x": OffsetX = ParseFloat(value, OffsetX); break;
                    case "offset_y": OffsetY = ParseFloat(value, OffsetY); break;
                    case "scale": Scale = Mathf.Max(0.1f, ParseFloat(value, Scale)); break;
                    case "spacing": Spacing = Mathf.Max(0f, ParseFloat(value, Spacing)); break;
                    case "corner_radius": CornerRadius = Mathf.Max(0f, ParseFloat(value, CornerRadius)); break;
                    case "fade_speed": FadeSpeed = Mathf.Max(0f, ParseFloat(value, FadeSpeed)); break;
                    case "show_cursor": ShowCursor = ParseBool(value, ShowCursor); break;
                    case "show_cursor_region": ShowCursorRegion = ParseBool(value, ShowCursorRegion); break;
                    case "cursor_raw_input": CursorRawInput = ParseBool(value, CursorRawInput); break;
                    case "cursor_wrap": CursorWrap = ParseBool(value, CursorWrap); break;
                    case "cursor_clamp": CursorClamp = ParseBool(value, CursorClamp); break;
                    case "cursor_region_x": CursorRegionX = ParseFloat(value, CursorRegionX); break;
                    case "cursor_region_y": CursorRegionY = ParseFloat(value, CursorRegionY); break;
                    case "cursor_region_width": CursorRegionWidth = Mathf.Max(1f, ParseFloat(value, CursorRegionWidth)); break;
                    case "cursor_region_height": CursorRegionHeight = Mathf.Max(1f, ParseFloat(value, CursorRegionHeight)); break;
                    case "cursor_region_color": CursorRegionColor = ColorUtil.ParseColor(value, CursorRegionColor); break;
                    case "cursor_radius": CursorRadius = Mathf.Max(1f, ParseFloat(value, CursorRadius)); break;
                    case "cursor_sensitivity": CursorSensitivity = Mathf.Max(0f, ParseFloat(value, CursorSensitivity)); break;
                    case "trail_max_stretch": TrailMaxStretch = Mathf.Max(1f, ParseFloat(value, TrailMaxStretch)); break;
                    case "trail_response": TrailResponse = Mathf.Max(0f, ParseFloat(value, TrailResponse)); break;
                    case "cursor_color": CursorColor = ColorUtil.ParseColor(value, CursorColor); break;
                    case "raining_enabled": RainingEnabled = ParseBool(value, RainingEnabled); break;
                    case "raining_height": RainingHeight = Mathf.Max(1, ParseInt(value, RainingHeight)); break;
                    case "raining_gap": RainingGap = Mathf.Max(0, ParseInt(value, RainingGap)); break;
                    case "raining_speed": RainingSpeed = Mathf.Max(0, ParseInt(value, RainingSpeed)); break;
                    case "raining_width": RainingWidth = Mathf.Max(0f, ParseFloat(value, RainingWidth)); break;
                    case "raining_radius": RainingRadius = Mathf.Max(0f, ParseFloat(value, RainingRadius)); break;
                    case "raining_color": RainingColor = ColorUtil.ParseColor(value, RainingColor); break;
                    case "panel_key": PanelKey = ParseKeyCode(value, PanelKey); break;
                    case "language": CurrentLang = value; break;
                    case "debug_info": DebugInfo = ParseBool(value, DebugInfo); break;
                    default:
                        Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: unknown key '{key}', ignored.");
                        break;
                }
            }
            catch
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: bad value for '{key}' = '{value}', kept default.");
            }
        }

        private static void ApplyStyle(ButtonStyle style, string key, string value, string section)
        {
            try
            {
                switch (key)
                {
                    case "text": style.Text = ColorUtil.ParseColor(value, style.Text); break;
                    case "border": style.Border = ColorUtil.ParseColor(value, style.Border); break;
                    case "fill": style.Fill = ColorUtil.ParseColor(value, style.Fill); break;
                    default:
                        Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: unknown {section} key '{key}', ignored.");
                        break;
                }
            }
            catch
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: bad {section} value for '{key}' = '{value}', kept default.");
            }
        }

        private void ApplyPresets(string key, string value)
        {
            try
            {
                switch (key)
                {
                    case "Current": CurrentPreset = string.IsNullOrEmpty(value) ? PresetStore.DefaultPresetName : value; break;
                    default:
                        Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: unknown Presets key '{key}', ignored.");
                        break;
                }
            }
            catch
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: settings.ini: bad Presets value for '{key}' = '{value}', kept default.");
            }
        }

        public void Save()
        {
            WriteTo(PersistenceService.PathFor("settings.ini"), fullConfig: true);
        }

        /// <summary>
        /// Write the whole configuration to <paramref name="path"/>, used by
        /// <see cref="PresetStore"/> to snapshot the current config into a preset
        /// folder. Global preferences are intentionally left out of snapshots so
        /// loading a preset never changes them: the UI language, the standalone
        /// panel hotkey, and the [Presets] selection itself (a snapshot must not
        /// be able to re-select another preset).
        /// </summary>
        public void SaveTo(string path)
        {
            WriteTo(path, fullConfig: false);
        }

        private void WriteTo(string path, bool fullConfig)
        {
            var main = new Dictionary<string, string>
            {
                ["show_hud"] = ShowHud ? "true" : "false",
                ["show_key_text"] = ShowKeyText ? "true" : "false",
                ["key_text_size"] = KeyTextSize.ToString("0.###", CultureInfo.InvariantCulture),
                ["key_text_offset_x"] = KeyTextOffsetX.ToString("0.###", CultureInfo.InvariantCulture),
                ["key_text_offset_y"] = KeyTextOffsetY.ToString("0.###", CultureInfo.InvariantCulture),
                ["offset_x"] = OffsetX.ToString("0.###", CultureInfo.InvariantCulture),
                ["offset_y"] = OffsetY.ToString("0.###", CultureInfo.InvariantCulture),
                ["scale"] = Scale.ToString("0.###", CultureInfo.InvariantCulture),
                ["spacing"] = Spacing.ToString("0.###", CultureInfo.InvariantCulture),
                ["corner_radius"] = CornerRadius.ToString("0.###", CultureInfo.InvariantCulture),
                ["fade_speed"] = FadeSpeed.ToString("0.###", CultureInfo.InvariantCulture),
                ["show_cursor"] = ShowCursor ? "true" : "false",
                ["show_cursor_region"] = ShowCursorRegion ? "true" : "false",
                ["cursor_raw_input"] = CursorRawInput ? "true" : "false",
                ["cursor_wrap"] = CursorWrap ? "true" : "false",
                ["cursor_clamp"] = CursorClamp ? "true" : "false",
                ["cursor_region_x"] = CursorRegionX.ToString("0.###", CultureInfo.InvariantCulture),
                ["cursor_region_y"] = CursorRegionY.ToString("0.###", CultureInfo.InvariantCulture),
                ["cursor_region_width"] = CursorRegionWidth.ToString("0.###", CultureInfo.InvariantCulture),
                ["cursor_region_height"] = CursorRegionHeight.ToString("0.###", CultureInfo.InvariantCulture),
                ["cursor_region_color"] = ColorUtil.ToHex(CursorRegionColor),
                ["cursor_radius"] = CursorRadius.ToString("0.###", CultureInfo.InvariantCulture),
                ["cursor_sensitivity"] = CursorSensitivity.ToString("0.###", CultureInfo.InvariantCulture),
                ["trail_max_stretch"] = TrailMaxStretch.ToString("0.###", CultureInfo.InvariantCulture),
                ["trail_response"] = TrailResponse.ToString("0.###", CultureInfo.InvariantCulture),
                ["cursor_color"] = ColorUtil.ToHex(CursorColor),
                ["raining_enabled"] = RainingEnabled ? "true" : "false",
                ["raining_height"] = RainingHeight.ToString(CultureInfo.InvariantCulture),
                ["raining_gap"] = RainingGap.ToString(CultureInfo.InvariantCulture),
                ["raining_speed"] = RainingSpeed.ToString(CultureInfo.InvariantCulture),
                ["raining_width"] = RainingWidth.ToString("0.###", CultureInfo.InvariantCulture),
                ["raining_radius"] = RainingRadius.ToString("0.###", CultureInfo.InvariantCulture),
                ["raining_color"] = ColorUtil.ToHex(RainingColor),
                ["panel_key"] = PanelKey.ToString(),
                ["language"] = CurrentLang,
                ["debug_info"] = DebugInfo ? "true" : "false",
                ["row_count"] = Rows.Count.ToString(CultureInfo.InvariantCulture),
            };
            if (!fullConfig)
            {
                // Preset snapshots keep the global preferences (UI language,
                // panel hotkey) out, as documented on SaveTo.
                main.Remove("panel_key");
                main.Remove("language");
                main.Remove("debug_info");
            }
            var idle = StyleSection(Idle);
            var pressed = StyleSection(Pressed);

            var sections = new List<KeyValuePair<string, IDictionary<string, string>>>
            {
                new KeyValuePair<string, IDictionary<string, string>>(MainSection, main),
                new KeyValuePair<string, IDictionary<string, string>>(IdleSection, idle),
                new KeyValuePair<string, IDictionary<string, string>>(PressedSection, pressed),
            };

            for (int r = 0; r < Rows.Count; r++)
            {
                var row = Rows[r];
                var d = new Dictionary<string, string>();
                for (int k = 0; k < row.Keys.Count; k++)
                {
                    var e = row.Keys[k];
                    string prefix = "key" + k + "_";
                    d[prefix + "label"] = e.Label;
                    d[prefix + "key1"] = e.Key1.ToString();
                    d[prefix + "width"] = e.Width.ToString(CultureInfo.InvariantCulture);
                    d[prefix + "dual"] = e.Dual ? "true" : "false";
                    d[prefix + "label2"] = e.Label2;
                    d[prefix + "key2"] = e.Key2.ToString();
                    d[prefix + "blank"] = e.Blank ? "true" : "false";
                }
                sections.Add(new KeyValuePair<string, IDictionary<string, string>>("row" + r, d));
            }

            if (fullConfig)
            {
                var presets = new Dictionary<string, string>
                {
                    ["Current"] = string.IsNullOrEmpty(CurrentPreset) ? PresetStore.DefaultPresetName : CurrentPreset,
                };
                sections.Add(new KeyValuePair<string, IDictionary<string, string>>(PresetsSection, presets));
            }

            PersistenceService.Write(
                path,
                sections,
                "TwilightInputOverlay settings. Lines of the form 'key = value'. Bad lines are ignored.");
        }

        private static Dictionary<string, string> StyleSection(ButtonStyle style)
        {
            return new Dictionary<string, string>
            {
                ["text"] = ColorUtil.ToHex(style.Text),
                ["border"] = ColorUtil.ToHex(style.Border),
                ["fill"] = ColorUtil.ToHex(style.Fill),
            };
        }

        // ── tolerant parsing helpers ──
        public static bool ParseBool(string s, bool fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
            if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            return fallback;
        }

        public static KeyCode ParseKeyCode(string s, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return System.Enum.TryParse(s, true, out KeyCode kc) ? kc : fallback;
        }

        public static float ParseFloat(string s, float fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : fallback;
        }

        public static int ParseInt(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }
    }
}
