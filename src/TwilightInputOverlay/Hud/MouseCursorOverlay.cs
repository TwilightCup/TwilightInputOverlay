using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Draws a mouse cursor inside a configurable rectangular region. The cursor
    /// is driven by the mouse's movement delta (so it also works while the game
    /// locks/hides the hardware cursor, as Human: Fall Flat does): each frame it
    /// moves by the mouse delta and, when it leaves the region, either wraps to
    /// the opposite edge, snaps back to the region centre, or is clamped to the
    /// region bounds, per <see cref="SettingsModel.CursorWrap"/> and
    /// <see cref="SettingsModel.CursorClamp"/> (clamp takes precedence and keeps
    /// the cursor inside the region, stopping at the edge).
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
    /// it slows. The heading tracks the current motion closely; on a sharp
    /// reversal it snaps and the trail briefly retracts to a circle so the flip
    /// is never seen as a sweep through the perpendicular. Cursor and trail are
    /// drawn from a single union-shaped mask
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

        // Heading tracking. The trail must always sit behind the current motion,
        // so ordinary turns are followed quickly (a base rate plus a gain that
        // grows with the turn size). A change larger than ReversalAngle is a
        // reversal: it is snapped instantly instead of swept through the
        // perpendicular, and the trail is briefly retracted so the flip happens
        // while the tail is invisible.
        private const float VelocitySmoothTau = 0.02f; // velocity smoothing, seconds
        private const float MinSpeed = 10f;            // px/s; below this the heading is kept
        private const float BaseTurn = 1800f;          // deg/s for the smallest tracked turn
        private const float TurnGain = 20f;            // extra deg/s per degree of turn
        private const float ReversalAngle = 100f;      // deg; past this, snap + retract
        private const float HeadingDeadZone = 2f;      // deg; ignore smaller changes
        private const float ReversalHold = 0.05f;      // s the trail stays retracted

        // Reference speed at which the trail reaches its full stretch. Tied to
        // the cursor radius so it feels the same at any size.
        private const float SpeedReferenceFactor = 60f;

        private Vector2 _pos;
        private float _stretch = 1f;
        private float _angle;
        private bool _initialized;

        // Smoothed velocity (px/s, GUI space) used to derive a stable heading,
        // and the remaining time the trail stays retracted after a reversal.
        private Vector2 _velocity;
        private float _reversalHold;

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
                _velocity = Vector2.zero;
                _reversalHold = 0f;
                return;
            }

            Rect region = RegionOf(s);
            if (!_initialized)
            {
                _pos = region.center;
                _velocity = Vector2.zero;
                _reversalHold = 0f;
                _initialized = true;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;

            Vector2 delta = ReadMouseDelta(s);
            _pos += delta;

            ApplyExitPolicy(ref _pos, region, s.CursorWrap, s.CursorClamp);

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
            Vector2 instantVelocity = delta / dt;
            float speed = instantVelocity.magnitude;

            UpdateHeading(instantVelocity, speed, dt);

            if (_reversalHold > 0f)
                _reversalHold = Mathf.Max(0f, _reversalHold - dt);

            float reference = Mathf.Max(1f, radius * SpeedReferenceFactor);
            float target = 1f + (Mathf.Max(1f, maxStretch) - 1f) * Mathf.Clamp01(speed / reference);

            // While the trail is retracted after a reversal, pin it to a circle so
            // the heading flip happens while the tail cannot be seen; it then
            // regrows in the new direction. (A circle is rotationally symmetric.)
            if (_reversalHold > 0f)
                target = 1f;

            _stretch = response <= 0f
                ? target
                : Mathf.MoveTowards(_stretch, target, response * dt);
        }

        private void UpdateHeading(Vector2 instantVelocity, float speed, float dt)
        {
            float blend = 1f - Mathf.Exp(-dt / VelocitySmoothTau);

            if (speed < MinSpeed)
            {
                // Too slow to trust the direction; let the smoothed velocity decay
                // so a quick flick does not fling the heading around.
                _velocity = Vector2.Lerp(_velocity, Vector2.zero, blend);
                return;
            }

            _velocity = Vector2.Lerp(_velocity, instantVelocity, blend);
            if (_velocity.sqrMagnitude <= 0.000001f) return;

            float targetAngle = Mathf.Atan2(_velocity.y, _velocity.x) * Mathf.Rad2Deg;
            float diff = Mathf.Abs(Mathf.DeltaAngle(_angle, targetAngle));

            if (diff > ReversalAngle)
            {
                // Reversal: flip now and retract the trail to hide the flip.
                _angle = targetAngle;
                _stretch = 1f;
                _reversalHold = ReversalHold;
            }
            else if (diff > HeadingDeadZone)
            {
                // Ordinary turn: a high base rate plus a gain for larger turns, so
                // even a small kink is followed within a frame instead of lagging.
                float turnRate = BaseTurn + TurnGain * diff;
                _angle = Mathf.MoveTowardsAngle(_angle, targetAngle, turnRate * dt);
            }
        }

        private static void ApplyExitPolicy(ref Vector2 pos, Rect region, bool wrap, bool clamp)
        {
            // Clamp: never leave the region; the cursor stops at the edge. This
            // takes precedence over wrap/return-to-centre, so it is checked first.
            if (clamp)
            {
                pos.x = Mathf.Clamp(pos.x, region.xMin, region.xMax);
                pos.y = Mathf.Clamp(pos.y, region.yMin, region.yMax);
                return;
            }

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
