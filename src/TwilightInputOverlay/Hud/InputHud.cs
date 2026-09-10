using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Draws the input-overlay HUD: a grid of key cells anchored to the
    /// bottom-left of the screen.
    /// <code>
    /// [装死][前进][左手/右手]
    /// [左移][后退][右移]
    /// [跳跃]
    /// </code>
    /// The left/right hand keys are rendered as one combined key: they share a
    /// single border, corner radius, and surrounding spacing, while each half
    /// keeps its own text and fill state. The combined width is one full grid
    /// cell, so the top row, middle row, and the jump row are all
    /// <c>3 * cell + 2 * spacing</c> wide.
    /// Each key fades between the idle and pressed styles; the fade speed comes
    /// from <see cref="SettingsModel.FadeSpeed"/>.
    /// </summary>
    public class InputHud : MonoBehaviour
    {
        private enum KeyKind
        {
            PlayDead,
            Forward,
            LeftHand,
            RightHand,
            Left,
            Back,
            Right,
            Jump,
        }

        private GUIStyle _keyStyle;
        private Font _font;
        private int _appliedFontSize = -1;

        // Per-key transition progress: 0 = fully idle, 1 = fully pressed.
        private float[] _fadeProgress = new float[8];

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
            for (int i = 0; i < _fadeProgress.Length; i++)
            {
                float target = IsPressed((KeyKind)i) ? 1f : 0f;
                if (speed <= 0f)
                {
                    _fadeProgress[i] = target;
                    continue;
                }

                float p = _fadeProgress[i];
                if (p < target)
                    _fadeProgress[i] = Mathf.Min(target, p + Time.deltaTime * speed);
                else if (p > target)
                    _fadeProgress[i] = Mathf.Max(target, p - Time.deltaTime * speed);
            }
        }

        private void OnGUI()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;

            var s = cfg.Settings;
            if (!s.ShowHud) return;

            float cell = Mathf.Max(8f, 56f * s.Scale);
            float spacing = Mathf.Max(0f, s.Spacing * s.Scale);
            float radius = Mathf.Max(0f, s.CornerRadius * s.Scale);
            int borderWidth = Mathf.Max(0, Mathf.RoundToInt(2f * s.Scale));

            // With the hands merged, every row is 3 cells + 2 gaps wide.
            float rowWidth = cell * 3f + spacing * 2f;
            float blockHeight = cell * 3f + spacing * 2f;

            // Bottom-left anchored: (OffsetX, OffsetY) from the bottom-left corner.
            float x = s.OffsetX;
            float y = Screen.height - s.OffsetY - blockHeight;

            DrawRow(new[] { KeyKind.PlayDead, KeyKind.Forward, KeyKind.LeftHand, KeyKind.RightHand },
                new[] { 1f, 1f, 0.5f, 0.5f }, x, y, cell, spacing, radius, borderWidth, s.ShowKeyText);

            DrawRow(new[] { KeyKind.Left, KeyKind.Back, KeyKind.Right },
                new[] { 1f, 1f, 1f }, x, y + cell + spacing, cell, spacing, radius, borderWidth, s.ShowKeyText);

            DrawRow(new[] { KeyKind.Jump },
                new[] { 3f }, x, y + 2f * (cell + spacing), cell, spacing, radius, borderWidth, s.ShowKeyText);
        }

        private void DrawRow(KeyKind[] kinds, float[] widths, float x, float y, float cell,
            float spacing, float radius, int borderWidth, bool showText)
        {
            float cx = x;
            for (int i = 0; i < kinds.Length; i++)
            {
                float drawnWidth;

                if (kinds[i] == KeyKind.LeftHand && i + 1 < kinds.Length && kinds[i + 1] == KeyKind.RightHand)
                {
                    // Left/right hand are one combined key: one full cell wide,
                    // no internal gap, shared border/corner/outer spacing.
                    var rect = new Rect(cx, y, cell, cell);
                    DrawCombinedHands(rect, cell, radius, borderWidth, showText);
                    drawnWidth = cell;
                    i++; // consume the right-hand entry
                }
                else
                {
                    float w = cell * widths[i];
                    if (kinds[i] == KeyKind.Jump)
                        w += spacing * 2f; // jump matches the 3-cell + 2-gap rows
                    var rect = new Rect(cx, y, w, cell);
                    DrawKey(kinds[i], rect, cell, radius, borderWidth, showText);
                    drawnWidth = w;
                }

                cx += drawnWidth;
                if (i < kinds.Length - 1)
                    cx += spacing;
            }
        }

        private void DrawKey(KeyKind kind, Rect rect, float cell, float radius, int borderWidth, bool showText)
        {
            var s = ConfigService.Instance.Settings;
            float t = _fadeProgress[(int)kind];
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

            if (showText)
            {
                // Keep the jump line exactly where it was; only letter labels get
                // the small vertical lift needed to sit visually centered.
                bool applyLift = kind != KeyKind.Jump;
                DrawText(rect, LabelFor(kind), style.Text, cell, applyLift);
            }
        }

        private void DrawCombinedHands(Rect rect, float cell, float radius, int borderWidth, bool showText)
        {
            var s = ConfigService.Instance.Settings;
            float leftT = _fadeProgress[(int)KeyKind.LeftHand];
            float rightT = _fadeProgress[(int)KeyKind.RightHand];
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
                DrawText(leftRect, "L", leftStyle.Text, cell, true);
                DrawText(rightRect, "R", rightStyle.Text, cell, true);
            }
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

        private void DrawText(Rect rect, string label, Color color, float cell, bool applyLift)
        {
            int fontSize = Mathf.Max(8, Mathf.RoundToInt(cell * 0.5f));
            EnsureFont(fontSize);
            _keyStyle.font = _font;
            _keyStyle.fontSize = fontSize;

            Rect textRect = rect;
            if (applyLift)
            {
                // IMGUI's MiddleCenter tends to sit glyphs slightly low; lift
                // letter labels a little so they look vertically centered.
                float lift = Mathf.Max(1f, fontSize * 0.06f);
                textRect.y -= lift;
            }

            Color prev = GUI.color;
            GUI.color = color;
            GUI.Label(textRect, label, _keyStyle);
            GUI.color = prev;
        }

        private static bool IsPressed(KeyKind kind)
        {
            switch (kind)
            {
                case KeyKind.PlayDead: return InputState.PlayDead;
                case KeyKind.Forward: return InputState.Forward;
                case KeyKind.LeftHand: return InputState.LeftHand;
                case KeyKind.RightHand: return InputState.RightHand;
                case KeyKind.Left: return InputState.Left;
                case KeyKind.Back: return InputState.Back;
                case KeyKind.Right: return InputState.Right;
                case KeyKind.Jump: return InputState.Jump;
                default: return false;
            }
        }

        private static string LabelFor(KeyKind kind)
        {
            switch (kind)
            {
                case KeyKind.PlayDead: return "Y";
                case KeyKind.Forward: return "W";
                case KeyKind.LeftHand: return "L";
                case KeyKind.RightHand: return "R";
                case KeyKind.Left: return "A";
                case KeyKind.Back: return "S";
                case KeyKind.Right: return "D";
                case KeyKind.Jump: return "—";
                default: return "";
            }
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
