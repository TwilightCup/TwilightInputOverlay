using System.Collections.Generic;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Builds the cursor + trail silhouette as a single white mask texture, so
    /// the two parts are one shape with one shared alpha. The cursor is a circle
    /// of <c>radius</c>; the trail is the circle's rear half stretched into a
    /// half-ellipse whose rear extent is <c>rear</c> (in pixels). When
    /// <c>rear == radius</c> the ellipse coincides with the rear half of the
    /// circle and the whole silhouette is exactly a circle.
    /// <para>
    /// Because the coverage is the union of both shapes baked into one texture,
    /// overlapping areas never blend twice: a semi-transparent cursor looks like
    /// one body instead of showing a seam where the trail overlaps the circle.
    /// Textures are cached by (<c>radius</c>, <c>rear</c>) and tinted at draw
    /// time via <see cref="GUI.color"/>, so only geometry is baked.
    /// </para>
    /// </summary>
    internal static class CursorShapeTextureCache
    {
        /// <summary>Transparent padding around the shape, in pixels, for anti-aliasing.</summary>
        public const int Pad = 2;

        private struct ShapeKey
        {
            public int Radius;
            public int Rear;
        }

        private static readonly Dictionary<ShapeKey, Texture2D> Cache = new Dictionary<ShapeKey, Texture2D>();
        private const int MaxEntries = 32;

        /// <summary>X offset of the circle centre inside a texture built for the given rear extent.</summary>
        public static int CenterX(int rear) => Pad + rear;

        /// <summary>Y offset of the circle centre inside a texture built for the given radius.</summary>
        public static int CenterY(int radius) => Pad + radius;

        /// <summary>
        /// Get (or build) the silhouette for the given circle radius and rear
        /// extent. <paramref name="rear"/> is clamped to at least
        /// <paramref name="radius"/>.
        /// </summary>
        public static Texture2D Get(int radius, int rear)
        {
            radius = Mathf.Max(1, radius);
            rear = Mathf.Max(radius, rear);

            var key = new ShapeKey { Radius = radius, Rear = rear };
            Texture2D tex;
            if (Cache.TryGetValue(key, out tex) && tex != null)
                return tex;

            if (Cache.Count >= MaxEntries)
            {
                foreach (var old in Cache.Values)
                    if (old != null) Object.Destroy(old);
                Cache.Clear();
            }

            tex = Build(radius, rear);
            Cache[key] = tex;
            return tex;
        }

        private static Texture2D Build(int radius, int rear)
        {
            int width = rear + radius + Pad * 2;
            int height = radius * 2 + Pad * 2;

            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // The circle centre sits `rear` from the left edge so the rear
            // half-ellipse (which reaches dx = -rear) always fits in the texture.
            float cx = Pad + rear;
            float cy = Pad + radius;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;

                    // Front: the full circle.
                    float circle = CircleCoverage(dx, dy, radius);

                    // Rear: the half-ellipse, only behind the circle centre.
                    float trail = 0f;
                    if (dx <= 0f)
                        trail = EllipseCoverage(dx, dy, rear, radius);

                    // Union coverage, so the two parts share a single alpha.
                    float coverage = Mathf.Clamp01(Mathf.Max(circle, trail));
                    pixels[y * width + x] = new Color(1f, 1f, 1f, coverage);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static float CircleCoverage(float dx, float dy, float radius)
        {
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(radius - dist + 0.5f);
        }

        private static float EllipseCoverage(float dx, float dy, float semiX, float semiY)
        {
            float nx = dx / semiX;
            float ny = dy / semiY;
            float t = Mathf.Sqrt(nx * nx + ny * ny);
            // Approximate signed-distance anti-aliasing along the smaller axis.
            return Mathf.Clamp01((1f - t) * Mathf.Min(semiX, semiY) + 0.5f);
        }
    }
}
