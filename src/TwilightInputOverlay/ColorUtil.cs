using System.Globalization;
using System.Text;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Color parsing/formatting shared by the HUD and the config layer. Hex
    /// colors accept either RRGGBB or RRGGBBAA; a missing alpha byte defaults to
    /// fully opaque. This keeps the config file human-editable while still
    /// supporting per-color alpha.
    /// </summary>
    public static class ColorUtil
    {
        /// <summary>Parse a hex color (RRGGBB or RRGGBBAA, optional leading #), or a fallback.</summary>
        public static Color ParseColor(string hex, Color fallback)
            => TryParseColor(hex, out Color c) ? c : fallback;

        /// <summary>Try to parse a hex color (RRGGBB or RRGGBBAA, optional leading #).
        /// Returns false for null/empty/invalid input so a half-typed value is
        /// never applied.</summary>
        public static bool TryParseColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.Trim();
            if (h.StartsWith("#")) h = h.Substring(1);
            if (h.Length == 6)
            {
                if (!TryHex(h, out uint rgb)) return false;
                color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
                return true;
            }
            if (h.Length == 8)
            {
                if (!TryHex(h, out uint rgba)) return false;
                color = new Color(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
                return true;
            }
            return false;
        }

        /// <summary>Emit a color as RRGGBBAA hex (always 8 chars, no #).</summary>
        public static string ToHex(Color c)
        {
            uint r = (uint)(Mathf.Clamp01(c.r) * 255f + 0.5f);
            uint g = (uint)(Mathf.Clamp01(c.g) * 255f + 0.5f);
            uint b = (uint)(Mathf.Clamp01(c.b) * 255f + 0.5f);
            uint a = (uint)(Mathf.Clamp01(c.a) * 255f + 0.5f);
            return string.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
        }

        private static bool TryHex(string s, out uint value)
            => uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
