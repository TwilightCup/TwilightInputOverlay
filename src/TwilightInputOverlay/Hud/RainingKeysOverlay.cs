using System.Collections.Generic;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// "Raining Keys": when a key of the first HUD row (<see cref="SettingsModel.Rows"/>[0],
    /// the visually top row) is pressed, a bar (rectangle) rises from the top of that key.
    /// While the key is held, the bar's top edge extends upward at the flow speed; after
    /// release the whole bar keeps translating upward at the same speed until its bottom
    /// passes the top edge of the region, then it disappears. Pressing again during the
    /// release animation spawns another bar that animates independently, so several bars
    /// can be visible at once.
    /// <para>
    /// Bars are clipped to a rectangular region whose width is the first row's total width,
    /// whose height is <see cref="SettingsModel.RainingHeight"/> and whose bottom edge sits
    /// <see cref="SettingsModel.RainingGap"/> above the key tops; the region itself is not
    /// drawn. A width-1 dual key spawns one bar per half (each listening to its own keybind),
    /// like the per-half pressed style of the HUD keys; a configured fixed width is split in
    /// two, so each half bar is half the width and the two halves stay adjacent with their
    /// seam at the key's centre. A single bar's width defaults to the width of the key it
    /// rises from; a fixed width can be configured instead.
    /// Bars are plain rectangles by default, or rounded rectangles when
    /// <see cref="SettingsModel.RainingRadius"/> &gt; 0. All Raining Keys values are
    /// HUD-space and scale with <see cref="SettingsModel.Scale"/>.
    /// </para>
    /// <para>
    /// The row geometry (cell size, spacing, row origin, key width) is computed with the
    /// same formulas as <see cref="InputHud"/>; keep the two in sync if the layout math
    /// ever changes.
    /// </para>
    /// </summary>
    public class RainingKeysOverlay : MonoBehaviour
    {
        /// <summary>
        /// One active bar. Screen/GUI Y grows downward, so "up" is smaller Y.
        /// While <see cref="Held"/> the bottom edge is pinned at the key's top and only
        /// the top extends; once the key is released the whole bar translates upward.
        /// </summary>
        private sealed class Bar
        {
            public float TopY;    // screen Y of the top edge
            public float BottomY; // screen Y of the bottom edge
            public bool Held;     // true while the key is held (top extends)
        }

        /// <summary>
        /// Per-input-slot state. The slot scheme mirrors <see cref="InputHud"/>'s fade
        /// slots: a single key uses id*2, a dual key uses id*2 (left half) and id*2+1
        /// (right half), so layout edits drop stale entries naturally.
        /// </summary>
        private sealed class Rain
        {
            public bool WasPressed;
            public readonly List<Bar> Bars = new List<Bar>();
        }

        /// <summary>Defensive cap on concurrent bars per slot (a release animation is
        /// finite, so this only guards pathological states).</summary>
        private const int MaxBarsPerSlot = 32;

        private Dictionary<int, Rain> _rains = new Dictionary<int, Rain>();

        // ── Debug logging (SettingsModel.DebugInfo) ───────────────────────
        // When on, per-second stats are logged to the BepInEx log: concurrent
        // bar count and tallest bar (px), the rounded-rect caches' call / bake
        // rates (a bake allocates a Texture2D + Color32[]; bakes are the
        // full-size fill masks, capBakes the bar end caps), the cache sizes,
        // and the managed-memory trend. In steady state bakes and capBakes
        // should both be ~0 — a sustained nonzero rate means something is
        // re-baking every frame, the first thing to check on a memory report.
        private float _debugTimer;
        private int _debugMaxBars;
        private float _debugMaxBarHeight;
        private int _debugBakeStart;
        private int _debugCapBakeStart;
        private long _debugLastMem;
        private bool _wasDebug;

        private static int SlotOf(KeyEntry e, int half)
        {
            // half -1/0 = single or left half, 1 = right half of a dual key.
            return half <= 0 ? e.Id * 2 : e.Id * 2 + 1;
        }

        private void Update()
        {
            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var s = cfg.Settings;

            if (!s.DebugInfo)
            {
                _debugTimer = 0f;
                _debugMaxBars = 0;
                _debugMaxBarHeight = 0f;
                _debugBakeStart = RoundedRectTextureCache.FillBakes;
                _debugCapBakeStart = RoundedRectTextureCache.EndCapBakes;
                _wasDebug = false;
            }
            else if (!_wasDebug)
            {
                _wasDebug = true;
                _debugLastMem = System.GC.GetTotalMemory(false);
                Plugin.Logger.LogInfo(string.Format(
                    "[RainingKeys] debug logging enabled: enabled={0} height={1} gap={2} speed={3} width={4} radius={5} scale={6}",
                    s.RainingEnabled, s.RainingHeight, s.RainingGap, s.RainingSpeed, s.RainingWidth, s.RainingRadius, s.Scale));
            }

            float speed = Mathf.Max(0f, s.RainingSpeed * s.Scale);
            if (!s.RainingEnabled || !s.ShowHud || s.Rows == null || s.Rows.Count == 0 || speed <= 0f)
            {
                _rains.Clear();
                return;
            }

            // Same layout math as InputHud.OnGUI: keep in sync.
            float cell = Mathf.Max(8f, 56f * s.Scale);
            float spacing = Mathf.Max(0f, s.Spacing * s.Scale);
            float blockHeight = s.Rows.Count * cell + (s.Rows.Count - 1) * spacing;
            float rowY = Screen.height - s.OffsetY - blockHeight;

            float regionHeightPx = Mathf.Max(1f, s.RainingHeight * s.Scale);
            float gapPx = Mathf.Max(0f, s.RainingGap * s.Scale);
            float regionBottom = rowY - gapPx;
            float regionTop = regionBottom - regionHeightPx;
            float dt = Time.deltaTime;
            if (dt <= 0f) dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 1f / 60f;

            var row = s.Rows[0];
            var next = new Dictionary<int, Rain>();
            for (int i = 0; i < row.Keys.Count; i++)
            {
                var e = row.Keys[i];
                if (e.Blank) continue;

                if (e.Width == 1 && e.Dual)
                {
                    Advance(next, SlotOf(e, 0), InputState.IsHeld(e.Key1), rowY, regionTop, speed, dt);
                    Advance(next, SlotOf(e, 1), InputState.IsHeld(e.Key2), rowY, regionTop, speed, dt);
                }
                else
                {
                    Advance(next, SlotOf(e, -1), InputState.IsHeld(e.Key1), rowY, regionTop, speed, dt);
                }
            }
            _rains = next;

            if (s.DebugInfo)
                LogDebugStats(s, regionHeightPx, gapPx, dt);
        }

        private void LogDebugStats(SettingsModel s, float regionHeightPx, float gapPx, float dt)
        {
            int bars = 0;
            float maxHeight = 0f;
            foreach (var kv in _rains)
            {
                bars += kv.Value.Bars.Count;
                var list = kv.Value.Bars;
                for (int i = 0; i < list.Count; i++)
                {
                    float h = list[i].BottomY - list[i].TopY;
                    if (h > maxHeight) maxHeight = h;
                }
            }
            _debugMaxBars = Mathf.Max(_debugMaxBars, bars);
            _debugMaxBarHeight = Mathf.Max(_debugMaxBarHeight, maxHeight);

            _debugTimer += dt;
            if (_debugTimer < 1f)
                return;

            // A bar's height is bounded by gap + region height (the held bar is
            // clamped to the region top); warn if a bar ever exceeds that, which
            // would mean the clamp stopped working.
            float bound = regionHeightPx + gapPx + 1f;
            if (_debugMaxBarHeight > bound)
                Plugin.Logger.LogWarning(string.Format(
                    "[RainingKeys] bar height exceeded bound: max={0:0.##}px bound={1:0.##}px (height={2} gap={3} scale={4})",
                    _debugMaxBarHeight, bound, s.RainingHeight, s.RainingGap, s.Scale));

            int bakes = RoundedRectTextureCache.FillBakes - _debugBakeStart;
            int capBakes = RoundedRectTextureCache.EndCapBakes - _debugCapBakeStart;
            long mem = System.GC.GetTotalMemory(false);
            long delta = mem - _debugLastMem;
            Plugin.Logger.LogInfo(string.Format(
                "[RainingKeys] 1s: bars(max)={0} barH(max)={1:0.##}px regionH={2:0.##}px fillTexCalls={3} bakes={4}/s capBakes={5}/s cache={6} gcMem={7:0.0}MB delta={8:+#0.0;-#0.0}MB/s",
                _debugMaxBars, _debugMaxBarHeight, regionHeightPx,
                RoundedRectTextureCache.FillCalls, bakes, capBakes,
                RoundedRectTextureCache.FillCacheCount + RoundedRectTextureCache.EndCapCacheCount,
                mem / (1024f * 1024f), delta / (1024f * 1024f)));

            _debugTimer = 0f;
            _debugMaxBars = 0;
            _debugMaxBarHeight = 0f;
            _debugBakeStart = RoundedRectTextureCache.FillBakes;
            _debugCapBakeStart = RoundedRectTextureCache.EndCapBakes;
            _debugLastMem = mem;
        }

        private void Advance(Dictionary<int, Rain> next, int slot, bool pressed,
            float rowY, float regionTop, float speed, float dt)
        {
            Rain rain;
            if (!_rains.TryGetValue(slot, out rain))
                rain = new Rain();

            // Rising edge: a new bar appears with height 0 at the key's top.
            if (pressed && !rain.WasPressed)
            {
                if (rain.Bars.Count >= MaxBarsPerSlot)
                    rain.Bars.RemoveAt(0);
                rain.Bars.Add(new Bar { TopY = rowY, BottomY = rowY, Held = true });
            }
            rain.WasPressed = pressed;

            var kept = new List<Bar>(rain.Bars.Count);
            foreach (var bar in rain.Bars)
            {
                if (bar.Held && pressed)
                {
                    // While held the top extends upward, but never past the
                    // region's top edge: the region clip hides anything beyond
                    // it anyway, and stopping there keeps the bar's height
                    // bounded (a held bar is never removed, so an unclamped
                    // top would grow without limit and re-bake a larger and
                    // larger rounded-rect texture every frame, eventually
                    // crashing the game).
                    bar.TopY = Mathf.Max(regionTop, bar.TopY - speed * dt);
                }
                if (bar.Held && !pressed)
                    bar.Held = false;                    // just released: start translating
                if (!bar.Held)
                {
                    // Released: the whole bar keeps moving up at the same speed.
                    bar.TopY -= speed * dt;
                    bar.BottomY -= speed * dt;
                }
                if (bar.BottomY <= regionTop)
                    continue;                            // fully above the region: gone
                kept.Add(bar);
            }
            rain.Bars.Clear();
            rain.Bars.AddRange(kept);

            if (rain.Bars.Count > 0 || pressed)
                next[slot] = rain;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            var cfg = ConfigService.Instance;
            if (cfg == null) return;
            var s = cfg.Settings;
            if (!s.RainingEnabled || !s.ShowHud || s.Rows == null || s.Rows.Count == 0)
                return;

            // Same layout math as InputHud.OnGUI: keep in sync.
            float cell = Mathf.Max(8f, 56f * s.Scale);
            float spacing = Mathf.Max(0f, s.Spacing * s.Scale);
            float blockHeight = s.Rows.Count * cell + (s.Rows.Count - 1) * spacing;
            float rowY = Screen.height - s.OffsetY - blockHeight;

            var row = s.Rows[0];
            float rowWidth = RowWidth(row, cell, spacing);
            float regionHeight = Mathf.Max(1f, s.RainingHeight * s.Scale);
            float regionBottom = rowY - Mathf.Max(0f, s.RainingGap * s.Scale);
            var region = new Rect(s.OffsetX, regionBottom - regionHeight, rowWidth, regionHeight);
            if (region.width <= 0f || region.height <= 0f)
                return;

            float fixedWidth = s.RainingWidth > 0f ? Mathf.Max(1f, s.RainingWidth * s.Scale) : 0f;
            int radius = Mathf.Max(0, Mathf.RoundToInt(s.RainingRadius * s.Scale));

            Color prev = GUI.color;
            GUI.BeginGroup(region);
            try
            {
                GUI.color = s.RainingColor;
                float regionScreenTop = region.y;
                float cx = 0f; // key x relative to the region (region.x == rowX)
                for (int i = 0; i < row.Keys.Count; i++)
                {
                    var e = row.Keys[i];
                    float w = KeyWidth(e, cell, spacing);
                    if (!e.Blank)
                    {
                        if (e.Width == 1 && e.Dual)
                        {
                            Rain left, right;
                            // A dual key splits the configured bar width in two: each
                            // half bar is half the width and the two halves stay adjacent,
                            // their seam at the key's centre (auto width = key half,
                            // which gives the same result as before).
                            float halfW = fixedWidth > 0f ? fixedWidth * 0.5f : w * 0.5f;
                            float keyCenter = cx + w * 0.5f;
                            if (_rains.TryGetValue(SlotOf(e, 0), out left))
                                DrawBars(left, keyCenter - halfW * 0.5f, halfW, radius, regionScreenTop);
                            if (_rains.TryGetValue(SlotOf(e, 1), out right))
                                DrawBars(right, keyCenter + halfW * 0.5f, halfW, radius, regionScreenTop);
                        }
                        else
                        {
                            Rain rain;
                            if (_rains.TryGetValue(SlotOf(e, -1), out rain))
                                DrawBars(rain, cx + w * 0.5f, fixedWidth > 0f ? fixedWidth : w, radius, regionScreenTop);
                        }
                    }
                    cx += w;
                    if (i < row.Keys.Count - 1)
                        cx += spacing;
                }
            }
            finally
            {
                GUI.color = prev;
                GUI.EndGroup();
            }
        }

        /// <summary>Draw one slot's bars centered at <paramref name="centerX"/>
        /// (region-local x), clipped to the region by the enclosing BeginGroup.
        /// <paramref name="regionScreenTop"/> is the region's top edge in screen
        /// Y; bars store screen Y, the group draws region-local Y.
        /// Rounded bars are drawn as three non-overlapping bands — a rounded
        /// top cap, a plain whiteTexture middle and a rounded bottom cap — so
        /// the rounded-corner texture is baked once per (width, radius) instead
        /// of once per frame per bar height; per-frame height changes only move
        /// the caps. The bands tile into the exact silhouette of the old single
        /// per-height texture (same ShapeCoverage arcs, same radius clamping),
        /// and because they never overlap they never double-blend even with a
        /// semi-transparent bar colour.</summary>
        private void DrawBars(Rain rain, float centerX, float width, int radius, float regionScreenTop)
        {
            var bars = rain.Bars;
            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                float h = bar.BottomY - bar.TopY;
                if (h <= 0f) continue;
                float top = bar.TopY - regionScreenTop;
                float x = centerX - width * 0.5f;

                if (radius <= 0)
                {
                    GUI.DrawTexture(new Rect(x, top, width, h), Texture2D.whiteTexture);
                    continue;
                }

                // Same clamping as RoundedRectTextureCache.ClampRadius so the
                // geometry matches a full rounded rect exactly.
                int wInt = Mathf.Max(1, Mathf.RoundToInt(width));
                int hInt = Mathf.Max(1, Mathf.RoundToInt(h));
                int rad = Mathf.Max(0, Mathf.Min(radius, Mathf.Min(wInt, hInt) / 2));
                if (rad <= 0)
                {
                    GUI.DrawTexture(new Rect(x, top, width, h), Texture2D.whiteTexture);
                    continue;
                }

                float midH = h - 2 * rad;
                if (midH > 0f)
                    GUI.DrawTexture(new Rect(x, top + rad, width, midH), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x, top, width, rad), RoundedRectTextureCache.GetEndCap(wInt, rad, true));
                GUI.DrawTexture(new Rect(x, top + h - rad, width, rad), RoundedRectTextureCache.GetEndCap(wInt, rad, false));
            }
        }

        /// <summary>Total width of one row: key widths plus the inter-key spacings.</summary>
        private static float RowWidth(KeyRow row, float cell, float spacing)
        {
            float w = 0f;
            for (int i = 0; i < row.Keys.Count; i++)
            {
                w += KeyWidth(row.Keys[i], cell, spacing);
                if (i < row.Keys.Count - 1)
                    w += spacing;
            }
            return w;
        }

        /// <summary>Width of one key unit: 1 cell, or n cells + (n-1) internal gaps
        /// (mirrors InputHud.KeyWidth).</summary>
        private static float KeyWidth(KeyEntry e, float cell, float spacing)
        {
            if (e.Width <= 1) return cell;
            return cell * e.Width + spacing * (e.Width - 1);
        }
    }
}
