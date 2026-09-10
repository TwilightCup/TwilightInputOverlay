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
    /// All user-tunable settings for the input overlay. Persisted to
    /// settings.ini as human-readable text; missing keys fall back to defaults.
    /// </summary>
    public sealed class SettingsModel
    {
        public bool ShowHud = true;
        public bool ShowKeyText = true;

        // Bottom-left anchored HUD placement/scaling.
        public float OffsetX = 16f;
        public float OffsetY = 16f;
        public float Scale = 1f;

        // Key-grid geometry.
        public float Spacing = 0f;
        public float CornerRadius = 0f;

        // Idle/pressed transition animation speed. Higher fades faster; 0 disables.
        public float FadeSpeed = 8f;

        // Standalone-panel keybind; not shown when integrated into HSRTimer.
        public KeyCode PanelKey = KeyCode.Home;

        public string CurrentLang = "en";

        public ButtonStyle Idle = ButtonStyle.DefaultIdle();
        public ButtonStyle Pressed = ButtonStyle.DefaultPressed();

        private const string MainSection = "settings";
        private const string IdleSection = "idle";
        private const string PressedSection = "pressed";

        public void Load()
        {
            foreach (var p in PersistenceService.Read(PersistenceService.PathFor("settings.ini")))
            {
                if (p.Section == MainSection)
                    Apply(p.Key, p.Value);
                else if (p.Section == IdleSection)
                    ApplyStyle(Idle, p.Key, p.Value, IdleSection);
                else if (p.Section == PressedSection)
                    ApplyStyle(Pressed, p.Key, p.Value, PressedSection);
            }
        }

        private void Apply(string key, string value)
        {
            try
            {
                switch (key)
                {
                    case "show_hud": ShowHud = ParseBool(value, ShowHud); break;
                    case "show_key_text": ShowKeyText = ParseBool(value, ShowKeyText); break;
                    case "offset_x": OffsetX = ParseFloat(value, OffsetX); break;
                    case "offset_y": OffsetY = ParseFloat(value, OffsetY); break;
                    case "scale": Scale = Mathf.Max(0.1f, ParseFloat(value, Scale)); break;
                    case "spacing": Spacing = Mathf.Max(0f, ParseFloat(value, Spacing)); break;
                    case "corner_radius": CornerRadius = Mathf.Max(0f, ParseFloat(value, CornerRadius)); break;
                    case "fade_speed": FadeSpeed = Mathf.Max(0f, ParseFloat(value, FadeSpeed)); break;
                    case "panel_key": PanelKey = ParseKeyCode(value, PanelKey); break;
                    case "language": CurrentLang = value; break;
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

        public void Save()
        {
            var main = new Dictionary<string, string>
            {
                ["show_hud"] = ShowHud ? "true" : "false",
                ["show_key_text"] = ShowKeyText ? "true" : "false",
                ["offset_x"] = OffsetX.ToString("0.###", CultureInfo.InvariantCulture),
                ["offset_y"] = OffsetY.ToString("0.###", CultureInfo.InvariantCulture),
                ["scale"] = Scale.ToString("0.###", CultureInfo.InvariantCulture),
                ["spacing"] = Spacing.ToString("0.###", CultureInfo.InvariantCulture),
                ["corner_radius"] = CornerRadius.ToString("0.###", CultureInfo.InvariantCulture),
                ["fade_speed"] = FadeSpeed.ToString("0.###", CultureInfo.InvariantCulture),
                ["panel_key"] = PanelKey.ToString(),
                ["language"] = CurrentLang,
            };
            var idle = StyleSection(Idle);
            var pressed = StyleSection(Pressed);

            PersistenceService.Write(
                PersistenceService.PathFor("settings.ini"),
                new[]
                {
                    new KeyValuePair<string, IDictionary<string, string>>(MainSection, main),
                    new KeyValuePair<string, IDictionary<string, string>>(IdleSection, idle),
                    new KeyValuePair<string, IDictionary<string, string>>(PressedSection, pressed),
                },
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
    }
}
