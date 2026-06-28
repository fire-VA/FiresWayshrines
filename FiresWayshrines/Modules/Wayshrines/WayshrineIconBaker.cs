using FiresCore.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Runtime-generated map-pin icons for wayshrines. This intentionally
    /// mirrors the companion radial menu icon style: a white monochrome
    /// procedural drawing on a fully transparent texture, with no prefab
    /// render, no camera, and no baked background.
    /// </summary>
    public static class WayshrineIconBaker
    {
        private const int IconSize = 128;

        private static readonly Dictionary<string, Sprite> _cache
            = new Dictionary<string, Sprite>();

        /// <summary>
        /// Returns the icon for <paramref name="prefabName"/>, baking it
        /// on first call. Returns null if the prefab can't be found in
        /// <see cref="ZNetScene"/> (typical during early plugin Awake).
        /// </summary>
        public static Sprite GetIcon(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            if (_cache.TryGetValue(prefabName, out var cached) && cached != null) return cached;

            try
            {
                var sprite = CreateProceduralWayshrineIcon(prefabName);
                if (sprite != null) _cache[prefabName] = sprite;
                return sprite;
            }
            catch (System.Exception ex)
            {
                FiresLogger.LogWarning($"[WayshrineIconBaker] Bake failed for '{prefabName}': {ex.Message}");
                return null;
            }
        }

        private static Sprite CreateProceduralWayshrineIcon(string prefabName)
        {
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                name = prefabName + "_VAWayshrinePinIcon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[IconSize * IconSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            float c = (IconSize - 1) * 0.5f;
            Color white = new Color(1f, 1f, 1f, 0.95f);
            Color soft = new Color(1f, 1f, 1f, 0.55f);

            // Rune stone / shrine silhouette.
            DrawLine(pixels, IconSize, c - 24f, c - 34f, c - 20f, c + 6f, white, 5f);
            DrawLine(pixels, IconSize, c + 24f, c - 34f, c + 20f, c + 6f, white, 5f);
            DrawArc(pixels, IconSize, c, c + 6f, 22f, 0f, 180f, white, 5f);
            DrawLine(pixels, IconSize, c - 24f, c - 34f, c + 24f, c - 34f, white, 5f);

            // Inner portal glow/rune, still transparent around it.
            DrawArc(pixels, IconSize, c, c - 6f, 14f, 0f, 360f, soft, 3f);
            DrawLine(pixels, IconSize, c, c + 10f, c, c - 22f, white, 4f);
            DrawLine(pixels, IconSize, c - 10f, c - 4f, c, c + 10f, white, 4f);
            DrawLine(pixels, IconSize, c + 10f, c - 4f, c, c + 10f, white, 4f);
            DrawCircle(pixels, IconSize, c, c - 6f, 4f, white);

            // Small base stones so it reads as a location marker at minimap scale.
            DrawLine(pixels, IconSize, c - 30f, c - 42f, c + 30f, c - 42f, white, 4f);
            DrawCircle(pixels, IconSize, c - 34f, c - 42f, 4f, white);
            DrawCircle(pixels, IconSize, c + 34f, c - 42f, 4f, white);

            tex.SetPixels(pixels);
            tex.Apply();
            Object.DontDestroyOnLoad(tex);

            var sprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
            Object.DontDestroyOnLoad(sprite);
            return sprite;
        }

        private static void DrawLine(Color[] pixels, int size, float x0, float y0, float x1, float y1, Color color, float thickness)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2f));
            float halfThickness = thickness * 0.5f;

            for (int step = 0; step <= steps; step++)
            {
                float t = (float)step / steps;
                float px = Mathf.Lerp(x0, x1, t);
                float py = Mathf.Lerp(y0, y1, t);

                int minX = Mathf.Max(0, Mathf.FloorToInt(px - halfThickness - 1));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(px + halfThickness + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(py - halfThickness - 1));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(py + halfThickness + 1));

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dist = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        if (dist <= halfThickness - 0.5f)
                            pixels[y * size + x] = color;
                        else if (dist <= halfThickness + 0.5f)
                        {
                            float blend = Mathf.Clamp01(halfThickness + 0.5f - dist);
                            pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color, blend);
                        }
                    }
                }
            }
        }

        private static void DrawCircle(Color[] pixels, int size, float cx, float cy, float radius, Color color)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius + 1));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (dist <= radius - 0.5f)
                        pixels[y * size + x] = color;
                    else if (dist <= radius + 0.5f)
                    {
                        float blend = Mathf.Clamp01(radius + 0.5f - dist);
                        pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color, blend);
                    }
                }
            }
        }

        private static void DrawArc(Color[] pixels, int size, float cx, float cy, float radius, float startDeg, float endDeg, Color color, float thickness)
        {
            int steps = Mathf.CeilToInt(Mathf.Abs(endDeg - startDeg) / 3f);
            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(startDeg, endDeg, (float)i / steps) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(startDeg, endDeg, (float)(i + 1) / steps) * Mathf.Deg2Rad;
                DrawLine(pixels, size,
                    cx + Mathf.Cos(a0) * radius, cy + Mathf.Sin(a0) * radius,
                    cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius,
                    color, thickness);
            }
        }
    }
}
