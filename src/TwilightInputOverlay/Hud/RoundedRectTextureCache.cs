using System.Collections.Generic;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Generates and caches textures used to draw key cells. Fill and border are
    /// drawn as white "mask" textures that are tinted via GUI.color at draw time,
    /// so animated/fading colors never force texture regeneration — only the
    /// shape geometry (size/radius/border width) is baked. The border is drawn
    /// after the fill so it is always on top. There is also a split-fill variant
    /// used by the combined left/right hand key, which lets each half keep its
    /// own fill color while sharing one outer shape (two colors cannot be tinted
    /// in a single draw, so that one remains color-baked).
    /// </summary>
    internal static class RoundedRectTextureCache
    {
        private struct FillMaskKey
        {
            public int Width;
            public int Height;
            public int Radius;
        }

        private struct SplitFillKey
        {
            public int Width;
            public int Height;
            public int Radius;
            public uint LeftFill;
            public uint RightFill;
        }

        private struct BorderMaskKey
        {
            public int Width;
            public int Height;
            public int Radius;
            public int BorderWidth;
        }

        private static readonly Dictionary<FillMaskKey, Texture2D> FillMaskCache = new Dictionary<FillMaskKey, Texture2D>();
        private static readonly Dictionary<SplitFillKey, Texture2D> SplitFillCache = new Dictionary<SplitFillKey, Texture2D>();
        private static readonly Dictionary<BorderMaskKey, Texture2D> BorderMaskCache = new Dictionary<BorderMaskKey, Texture2D>();

        /// <summary>White fill mask; tint with GUI.color to apply the actual fill color.</summary>
        public static Texture2D GetFillMask(int width, int height, int radius)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            radius = ClampRadius(width, height, radius);

            var key = new FillMaskKey { Width = width, Height = height, Radius = radius };
            Texture2D tex;
            if (FillMaskCache.TryGetValue(key, out tex))
                return tex;

            if (FillMaskCache.Count > 32)
                FillMaskCache.Clear();
            tex = BuildFillMask(width, height, radius);
            FillMaskCache[key] = tex;
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

            if (SplitFillCache.Count > 64)
                SplitFillCache.Clear();
            tex = BuildSplitFill(width, height, radius, leftFill, rightFill);
            SplitFillCache[key] = tex;
            return tex;
        }

        /// <summary>White border mask; tint with GUI.color to apply the actual border color.</summary>
        public static Texture2D GetBorderMask(int width, int height, int radius, int borderWidth)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            radius = ClampRadius(width, height, radius);
            borderWidth = Mathf.Max(0, borderWidth);

            var key = new BorderMaskKey
            {
                Width = width,
                Height = height,
                Radius = radius,
                BorderWidth = borderWidth,
            };
            Texture2D tex;
            if (BorderMaskCache.TryGetValue(key, out tex))
                return tex;

            if (BorderMaskCache.Count > 32)
                BorderMaskCache.Clear();
            tex = BuildBorderMask(width, height, radius, borderWidth);
            BorderMaskCache[key] = tex;
            return tex;
        }

        private static int ClampRadius(int width, int height, int radius)
        {
            return Mathf.Max(0, Mathf.Min(radius, Mathf.Min(width, height) / 2));
        }

        private static Texture2D BuildFillMask(int width, int height, int radius)
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
                    Color c = new Color(1f, 1f, 1f, inside ? coverage : 0f);
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

        private static Texture2D BuildBorderMask(int width, int height, int radius, int borderWidth)
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
                    float alpha = 0f;

                    float coverage;
                    bool inside = ShapeCoverage(px, py, width, height, radius, out coverage);
                    if (inside && borderWidth > 0)
                    {
                        float edgeDist = EdgeDistance(px, py, width, height, radius);
                        if (edgeDist <= borderWidth)
                        {
                            alpha = radius <= 0f
                                ? 1f
                                : Mathf.Clamp01(borderWidth - edgeDist + 0.5f);
                        }
                    }
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
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
