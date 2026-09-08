using System.Collections.Generic;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Generates and caches textures used to draw key cells. Fill and border are
    /// generated as separate textures and drawn in that order (fill first, then
    /// border on top), so the border can never be hidden by the fill. There is
    /// also a split-fill variant used by the combined left/right hand key, which
    /// lets each half keep its own fill color while sharing one outer shape.
    /// </summary>
    internal static class RoundedRectTextureCache
    {
        private struct FillKey
        {
            public int Width;
            public int Height;
            public int Radius;
            public uint Fill;
        }

        private struct SplitFillKey
        {
            public int Width;
            public int Height;
            public int Radius;
            public uint LeftFill;
            public uint RightFill;
        }

        private struct BorderKey
        {
            public int Width;
            public int Height;
            public int Radius;
            public int BorderWidth;
            public uint Border;
        }

        private static readonly Dictionary<FillKey, Texture2D> FillCache = new Dictionary<FillKey, Texture2D>();
        private static readonly Dictionary<SplitFillKey, Texture2D> SplitFillCache = new Dictionary<SplitFillKey, Texture2D>();
        private static readonly Dictionary<BorderKey, Texture2D> BorderCache = new Dictionary<BorderKey, Texture2D>();

        public static Texture2D GetFill(int width, int height, int radius, Color fill)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            radius = ClampRadius(width, height, radius);

            var key = new FillKey { Width = width, Height = height, Radius = radius, Fill = Pack(fill) };
            Texture2D tex;
            if (FillCache.TryGetValue(key, out tex))
                return tex;

            if (FillCache.Count > 32)
                FillCache.Clear();
            tex = BuildFill(width, height, radius, fill);
            FillCache[key] = tex;
            return tex;
        }

        public static Texture2D GetSplitFill(int width, int height, int radius, Color leftFill, Color rightFill)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            radius = ClampRadius(width, height, radius);

            var key = new SplitFillKey
            {
                Width = width,
                Height = height,
                Radius = radius,
                LeftFill = Pack(leftFill),
                RightFill = Pack(rightFill),
            };
            Texture2D tex;
            if (SplitFillCache.TryGetValue(key, out tex))
                return tex;

            if (SplitFillCache.Count > 32)
                SplitFillCache.Clear();
            tex = BuildSplitFill(width, height, radius, leftFill, rightFill);
            SplitFillCache[key] = tex;
            return tex;
        }

        public static Texture2D GetBorder(int width, int height, int radius, int borderWidth, Color border)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            radius = ClampRadius(width, height, radius);
            borderWidth = Mathf.Max(0, borderWidth);

            var key = new BorderKey
            {
                Width = width,
                Height = height,
                Radius = radius,
                BorderWidth = borderWidth,
                Border = Pack(border),
            };
            Texture2D tex;
            if (BorderCache.TryGetValue(key, out tex))
                return tex;

            if (BorderCache.Count > 32)
                BorderCache.Clear();
            tex = BuildBorder(width, height, radius, borderWidth, border);
            BorderCache[key] = tex;
            return tex;
        }

        private static int ClampRadius(int width, int height, int radius)
        {
            return Mathf.Max(0, Mathf.Min(radius, Mathf.Min(width, height) / 2));
        }

        private static Texture2D BuildFill(int width, int height, int radius, Color fill)
        {
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float coverage;
                    bool inside = ShapeCoverage(x + 0.5f, y + 0.5f, width, height, radius, out coverage);
                    Color c = new Color(0f, 0f, 0f, 0f);
                    if (inside)
                    {
                        c = fill;
                        c.a *= coverage;
                    }
                    pixels[y * width + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D BuildSplitFill(int width, int height, int radius, Color leftFill, Color rightFill)
        {
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float mid = width * 0.5f;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f;
                    float coverage;
                    bool inside = ShapeCoverage(px, y + 0.5f, width, height, radius, out coverage);
                    Color c = new Color(0f, 0f, 0f, 0f);
                    if (inside)
                    {
                        c = px < mid ? leftFill : rightFill;
                        c.a *= coverage;
                    }
                    pixels[y * width + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D BuildBorder(int width, int height, int radius, int borderWidth, Color border)
        {
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    Color c = new Color(0f, 0f, 0f, 0f);

                    float coverage;
                    bool inside = ShapeCoverage(px, py, width, height, radius, out coverage);
                    if (inside && borderWidth > 0)
                    {
                        float edgeDist = EdgeDistance(px, py, width, height, radius);
                        if (edgeDist <= borderWidth)
                        {
                            float borderCoverage = radius <= 0f
                                ? 1f
                                : Mathf.Clamp01(borderWidth - edgeDist + 0.5f);
                            c = border;
                            c.a *= borderCoverage;
                        }
                    }
                    pixels[y * width + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static bool ShapeCoverage(float px, float py, int width, int height, int radius, out float coverage)
        {
            if (radius <= 0)
            {
                // Pixel centers are always inside the rectangle.
                coverage = 1f;
                return true;
            }

            float r = radius;
            float nx = Mathf.Clamp(px, r, width - r);
            float ny = Mathf.Clamp(py, r, height - r);
            float dist = Vector2.Distance(new Vector2(px, py), new Vector2(nx, ny));
            if (dist > r)
            {
                coverage = 0f;
                return false;
            }

            coverage = Mathf.Clamp01((r - dist) + 0.5f);
            return true;
        }

        private static float EdgeDistance(float px, float py, int width, int height, int radius)
        {
            if (radius <= 0)
            {
                return Mathf.Min(Mathf.Min(px, width - px), Mathf.Min(py, height - py));
            }

            float r = radius;
            float nx = Mathf.Clamp(px, r, width - r);
            float ny = Mathf.Clamp(py, r, height - r);
            float dist = Vector2.Distance(new Vector2(px, py), new Vector2(nx, ny));
            return r - dist;
        }

        private static uint Pack(Color c)
        {
            uint r = (uint)(Mathf.Clamp01(c.r) * 255f + 0.5f);
            uint g = (uint)(Mathf.Clamp01(c.g) * 255f + 0.5f);
            uint b = (uint)(Mathf.Clamp01(c.b) * 255f + 0.5f);
            uint a = (uint)(Mathf.Clamp01(c.a) * 255f + 0.5f);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }
    }
}
