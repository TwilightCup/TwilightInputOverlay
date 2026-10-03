using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Draws a mouse cursor inside a configurable rectangular region. The cursor
    /// is driven by the mouse's movement delta (so it also works while the game
    /// locks/hides the hardware cursor, as Human: Fall Flat does): each frame it
    /// moves by the mouse delta and, when it leaves the region, either wraps to
    /// the opposite edge or snaps back to the region centre, per
    /// <see cref="SettingsModel.CursorWrap"/>.
    /// <para>
    /// By default the delta comes from the raw mouse movement axes
    /// (<see cref="SettingsModel.CursorRawInput"/>), which keep reporting even
    /// when the system pointer is pinned at a screen edge, e.g. while a menu is
    /// open. Turning that off falls back to the hardware cursor's screen
    /// position (and the smoothed axes while the cursor is locked).
    /// </para>
    /// <para>
    /// The region can also be shown as a colour-tinted rectangle drawn behind
    /// the cursor (and its trail), so the movement bounds are visible; its RGBA
    /// is configurable.
    /// </para>
    /// <para>
    /// The cursor is directional: it rotates toward its motion direction and
    /// drags a rear half-ellipse trail that stretches with speed and recovers as
    /// it slows. Cursor and trail are drawn from a single union-shaped mask
    /// (<see cref="CursorShapeTextureCache"/>) tinted with
    /// <see cref="SettingsModel.CursorColor"/>, so they share one colour and one
    /// alpha and read as a single body even when semi-transparent.
    /// </para>
    /// </summary>
    public class MouseCursorOverlay : MonoBehaviour
    {
        // Unity's "Mouse X/Y" axes are scaled by the Input Manager sensitivity
        // (0.1 by default), so a raw axis unit is roughly a tenth of a pixel.
        // This converts the locked-cursor axis input back to screen pixels.
        private const float AxisToPixels = 10f;

        // How fast the cursor turns toward a new motion direction (degrees/sec).
        private const float TurnSpeed = 1080f;

        // Reference speed at which the trail reaches its full stretch. Tied to
        // the cursor radius so it feels the same at any size.
        private const float SpeedReferenceFactor = 60f;

        private Vector2 _pos;
        private float _stretch = 1f;
        private float _angle;
        private bool _initialized;

        private Vector2 _prevMousePos;
        private bool _hasPrevMousePos;

        private void Update()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var s = cfg.Settings;
            if (!s.ShowCursor)
            {
                _initialized = false;
                _hasPrevMousePos = false;
                return;
            }

            Rect region = RegionOf(s);
            if (!_initialized)
            {
                _pos = region.center;
                _initialized = true;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;

            Vector2 delta = ReadMouseDelta(s);
            _pos += delta;

            ApplyExitPolicy(ref _pos, region, s.CursorWrap);

            float radius = Mathf.Max(1f, s.CursorRadius);
            UpdateTrail(delta, dt, radius, s.TrailMaxStretch, s.TrailResponse);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var s = cfg.Settings;
            if (!s.ShowCursor && !s.ShowCursorRegion) return;

            // Region backdrop first, so the cursor and its trail draw on top.
            if (s.ShowCursorRegion)
                DrawRegion(RegionOf(s), s.CursorRegionColor);

            if (s.ShowCursor)
                DrawCursor(s);
        }

        private static void DrawRegion(Rect region, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(region, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void DrawCursor(SettingsModel s)
        {
            int radius = Mathf.Max(1, Mathf.RoundToInt(s.CursorRadius));
            int rear = Mathf.Max(radius, Mathf.RoundToInt(radius * Mathf.Max(1f, _stretch)));
            Texture2D tex = CursorShapeTextureCache.Get(radius, rear);

            // Place the texture so its circle centre lands on _pos; the trail
            // extends to the left of the centre inside the texture.
            float xOrigin = CursorShapeTextureCache.CenterX(rear);
            float yOrigin = CursorShapeTextureCache.CenterY(radius);
            var rect = new Rect(_pos.x - xOrigin, _pos.y - yOrigin, tex.width, tex.height);

            Matrix4x4 prevMatrix = GUI.matrix;
            Color prevColor = GUI.color;
            GUIUtility.RotateAroundPivot(_angle, _pos);
            GUI.color = s.CursorColor;
            GUI.DrawTexture(rect, tex);
            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        private void UpdateTrail(Vector2 delta, float dt, float radius, float maxStretch, float response)
        {
            // Direction follows the motion, so the trail always drags behind.
            if (delta.sqrMagnitude > 0.000001f)
            {
                float targetAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                _angle = Mathf.MoveTowardsAngle(_angle, targetAngle, TurnSpeed * dt);
            }

            float speed = delta.magnitude / dt;
            float reference = Mathf.Max(1f, radius * SpeedReferenceFactor);
            float target = 1f + (Mathf.Max(1f, maxStretch) - 1f) * Mathf.Clamp01(speed / reference);
            _stretch = response <= 0f
                ? target
                : Mathf.MoveTowards(_stretch, target, response * dt);
        }

        private static void ApplyExitPolicy(ref Vector2 pos, Rect region, bool wrap)
        {
            if (!Outside(pos, region)) return;

            if (wrap && region.width > 0f && region.height > 0f)
            {
                pos.x = region.xMin + Mathf.Repeat(pos.x - region.xMin, region.width);
                pos.y = region.yMin + Mathf.Repeat(pos.y - region.yMin, region.height);
            }
            else
            {
                pos = region.center;
            }
        }

        private static bool Outside(Vector2 pos, Rect region)
        {
            return pos.x < region.xMin || pos.x > region.xMax
                || pos.y < region.yMin || pos.y > region.yMax;
        }

        private static Rect RegionOf(SettingsModel s)
        {
            // Bottom-right anchored: X/Y are distances from the right/bottom edges.
            float w = Mathf.Max(1f, s.CursorRegionWidth);
            float h = Mathf.Max(1f, s.CursorRegionHeight);
            float right = Screen.width - s.CursorRegionX;
            float bottom = Screen.height - s.CursorRegionY;
            return new Rect(right - w, bottom - h, w, h);
        }

        private Vector2 ReadMouseDelta(SettingsModel s)
        {
            // Raw input: read the mouse movement axes directly. Unlike the
            // hardware cursor's screen position, these keep reporting while the
            // OS cursor is pinned at a screen edge (e.g. a menu), so the overlay
            // cursor does not stall there.
            if (s.CursorRawInput)
            {
                _hasPrevMousePos = false;
                return AxisDelta(raw: true, sensitivity: s.CursorSensitivity);
            }

            // Non-raw: prefer the visible cursor's real screen-space movement.
            try
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    Vector3 mp = Input.mousePosition;
                    var cur = new Vector2(mp.x, mp.y);
                    Vector2 d = _hasPrevMousePos ? cur - _prevMousePos : Vector2.zero;
                    _prevMousePos = cur;
                    _hasPrevMousePos = true;
                    // Screen Y grows upward, GUI Y grows downward.
                    return new Vector2(d.x * s.CursorSensitivity, -d.y * s.CursorSensitivity);
                }
            }
            catch
            {
                // Fall through to the axis path.
            }

            // Locked cursor: mousePosition is pinned, so read the movement axes.
            _hasPrevMousePos = false;
            return AxisDelta(raw: false, sensitivity: s.CursorSensitivity);
        }

        private static Vector2 AxisDelta(bool raw, float sensitivity)
        {
            try
            {
                float mx = raw ? Input.GetAxisRaw("Mouse X") : Input.GetAxis("Mouse X");
                float my = raw ? Input.GetAxisRaw("Mouse Y") : Input.GetAxis("Mouse Y");
                return new Vector2(mx * sensitivity * AxisToPixels, -my * sensitivity * AxisToPixels);
            }
            catch
            {
                return Vector2.zero;
            }
        }
    }
}
