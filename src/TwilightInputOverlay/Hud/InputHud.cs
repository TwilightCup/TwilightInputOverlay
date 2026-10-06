using System.Collections.Generic;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Draws the input-overlay HUD from the customizable key layout
    /// (<see cref="SettingsModel.Rows"/>): rows stack top to bottom, keys run
    /// left to right, all anchored to the bottom-left of the screen. A key of
    /// width 1 is one grid cell; a wider key spans
    /// <c>width * cell + (width - 1) * spacing</c> so it absorbs the internal
    /// gaps like the classic jump/space key. A width-1 key with
    /// <see cref="KeyEntry.Dual"/> renders as two half-width labels sharing one
    /// cell (like the old left/right hand key), and a
    /// <see cref="KeyEntry.Blank"/> key reserves its width without drawing or
    /// reading input. Each key fades between the idle and pressed styles; the
    /// fade speed comes from <see cref="SettingsModel.FadeSpeed"/>.
    /// </summary>
    public class InputHud : MonoBehaviour
    {
        private GUIStyle _keyStyle;
        private Font _font;
        private int _appliedFontSize = -1;

        // Per-key fade progress keyed by a stable slot: 0 = fully idle,
        // 1 = fully pressed. A single key uses slot id*2; a dual key uses
        // id*2 (left half) and id*2+1 (right half), so toggling dual carries
        // the fade over instead of snapping. Rebuilt every Update from the
        // live layout, which also drops stale entries of deleted keys.
        private Dictionary<int, float> _fades = new Dictionary<int, float>();

        private static int FadeSlot(KeyEntry e, int half)
        {
            // half -1/0 = single or left half, 1 = right half of a dual key.
            return half <= 0 ? e.Id * 2 : e.Id * 2 + 1;
        }

        private void Awake()
        {
            _keyStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                richText = false,
                normal = { textColor = Color.white },
            };
        }

        private void Update()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;

            float speed = cfg.Settings.FadeSpeed;
            var next = new Dictionary<int, float>();

            foreach (var row in cfg.Settings.Rows)
            {
                foreach (var e in row.Keys)
                {
                    if (e.Blank) continue; // no behaviour, no fade

                    if (e.Width == 1 && e.Dual)
                    {
                        Advance(next, FadeSlot(e, 0), InputState.IsHeld(e.Key1), speed);
                        Advance(next, FadeSlot(e, 1), InputState.IsHeld(e.Key2), speed);
                    }
                    else
                    {
                        Advance(next, FadeSlot(e, -1), InputState.IsHeld(e.Key1), speed);
                    }
                }
            }

            _fades = next;
        }

        private void Advance(Dictionary<int, float> next, int slot, bool pressed, float speed)
        {
            float target = pressed ? 1f : 0f;
            float p;
            _fades.TryGetValue(slot, out p);

            if (speed <= 0f)
                next[slot] = target;
            else if (p < target)
                next[slot] = Mathf.Min(target, p + Time.deltaTime * speed);
            else if (p > target)
                next[slot] = Mathf.Max(target, p - Time.deltaTime * speed);
            else
                next[slot] = target;
        }

        private void OnGUI()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;

            var s = cfg.Settings;
            if (!s.ShowHud) return;

            var rows = s.Rows;
            if (rows == null || rows.Count == 0) return;

            float cell = Mathf.Max(8f, 56f * s.Scale);
            float spacing = Mathf.Max(0f, s.Spacing * s.Scale);
            float radius = Mathf.Max(0f, s.CornerRadius * s.Scale);
            int borderWidth = Mathf.Max(0, Mathf.RoundToInt(2f * s.Scale));

            // Bottom-left anchored: (OffsetX, OffsetY) from the bottom-left corner.
            float x = s.OffsetX;
            float blockHeight = rows.Count * cell + (rows.Count - 1) * spacing;
            float y = Screen.height - s.OffsetY - blockHeight;

            for (int r = 0; r < rows.Count; r++)
                DrawRow(rows[r], x, y + r * (cell + spacing), cell, spacing, radius, borderWidth, s.ShowKeyText);
        }

        private void DrawRow(KeyRow row, float x, float y, float cell,
            float spacing, float radius, int borderWidth, bool showText)
        {
            float cx = x;
            for (int i = 0; i < row.Keys.Count; i++)
            {
                var e = row.Keys[i];
                float w = KeyWidth(e, cell, spacing);

                if (e.Blank)
                {
                    // Empty slot: reserve the width, draw nothing.
                    cx += w;
                    if (i < row.Keys.Count - 1) cx += spacing;
                    continue;
                }

                var rect = new Rect(cx, y, w, cell);
                if (e.Width == 1 && e.Dual)
                    DrawDualKey(e, rect, cell, radius, borderWidth, showText);
                else
                    DrawKey(e, rect, cell, radius, borderWidth, showText);

                cx += w;
                if (i < row.Keys.Count - 1)
                    cx += spacing;
            }
        }

        /// <summary>Width of one key unit: 1 cell, or n cells + (n-1) internal gaps.</summary>
        private static float KeyWidth(KeyEntry e, float cell, float spacing)
        {
            if (e.Width <= 1) return cell;
            return cell * e.Width + spacing * (e.Width - 1);
        }

        private void DrawKey(KeyEntry e, Rect rect, float cell, float radius, int borderWidth, bool showText)
        {
            var s = ConfigService.Instance.Settings;
            float t = GetFade(FadeSlot(e, -1));
            var style = LerpStyle(s.Idle, s.Pressed, t);

            int tw = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int th = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            int rad = Mathf.Max(0, Mathf.RoundToInt(radius));

            Color prev = GUI.color;
            GUI.color = style.Fill;
            GUI.DrawTexture(rect, RoundedRectTextureCache.GetFillMask(tw, th, rad));
            GUI.color = style.Border;
            GUI.DrawTexture(rect, RoundedRectTextureCache.GetBorderMask(tw, th, rad, borderWidth));
            GUI.color = prev;

            if (showText && !string.IsNullOrEmpty(e.Label))
                DrawText(rect, e.Label, style.Text, cell);
        }

        private void DrawDualKey(KeyEntry e, Rect rect, float cell, float radius, int borderWidth, bool showText)
        {
            var s = ConfigService.Instance.Settings;
            float leftT = GetFade(FadeSlot(e, 0));
            float rightT = GetFade(FadeSlot(e, 1));
            var leftStyle = LerpStyle(s.Idle, s.Pressed, leftT);
            var rightStyle = LerpStyle(s.Idle, s.Pressed, rightT);

            // The shared border follows the more-pressed half, matching the old
            // OR semantics while still animating smoothly.
            var borderStyle = LerpStyle(s.Idle, s.Pressed, Mathf.Max(leftT, rightT));

            int tw = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int th = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            int rad = Mathf.Max(0, Mathf.RoundToInt(radius));

            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, RoundedRectTextureCache.GetSplitFill(tw, th, rad, leftStyle.Fill, rightStyle.Fill));
            GUI.color = borderStyle.Border;
            GUI.DrawTexture(rect, RoundedRectTextureCache.GetBorderMask(tw, th, rad, borderWidth));
            GUI.color = prev;

            if (showText)
            {
                var leftRect = new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height);
                var rightRect = new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, rect.height);
                if (!string.IsNullOrEmpty(e.Label))
                    DrawText(leftRect, e.Label, leftStyle.Text, cell);
                if (!string.IsNullOrEmpty(e.Label2))
                    DrawText(rightRect, e.Label2, rightStyle.Text, cell);
            }
        }

        private float GetFade(int slot)
        {
            float v;
            return _fades.TryGetValue(slot, out v) ? v : 0f;
        }

        private static ButtonStyle LerpStyle(ButtonStyle idle, ButtonStyle pressed, float t)
        {
            return new ButtonStyle
            {
                Text = Color.Lerp(idle.Text, pressed.Text, t),
                Border = Color.Lerp(idle.Border, pressed.Border, t),
                Fill = Color.Lerp(idle.Fill, pressed.Fill, t),
            };
        }

        private void DrawText(Rect rect, string label, Color color, float cell)
        {
            int fontSize = Mathf.Max(8, Mathf.RoundToInt(cell * 0.5f));
            EnsureFont(fontSize);
            _keyStyle.font = _font;
            _keyStyle.fontSize = fontSize;

            // IMGUI's MiddleCenter tends to sit glyphs slightly low; lift the
            // label a little so it looks vertically centered.
            Rect textRect = rect;
            float lift = Mathf.Max(1f, fontSize * 0.06f);
            textRect.y -= lift;

            Color prev = GUI.color;
            GUI.color = color;
            GUI.Label(textRect, label, _keyStyle);
            GUI.color = prev;
        }

        private void EnsureFont(int size)
        {
            if (size <= 0) size = 18;
            if (_font != null && _appliedFontSize == size) return;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC",
                    "Noto Sans CJK", "Heiti SC", "Arial Unicode MS", "Arial",
                }, size);
                _appliedFontSize = size;
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: dynamic font creation failed: {ex.Message}");
                _font = null;
            }
        }
    }
}
