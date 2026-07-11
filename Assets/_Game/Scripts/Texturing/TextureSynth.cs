using System;
using UnityEngine;
using Random = System.Random;

namespace IslandGame.Texturing
{
    /// <summary>
    /// The procedural pixel-art texture core: pure, deterministic functions
    /// of (style, base color, size, seed, hue shift) → pixels. STATIC CLASS
    /// by design: there is no per-asset state worth serializing — callers
    /// (the editor sections now, the Phase 9 batch pass next) own the
    /// parameter tuples, and a ScriptableObject tool would only add asset
    /// management around what is a math function. Runtime-callable: no
    /// editor dependencies (writing .png assets is the editor-side
    /// GeneratedTextureAssets' job).
    ///
    /// Each TextureStyle is a DISTINCT algorithm — grain columns, mottle
    /// blobs, weave threads, brushed rows — not one noise recolored; see the
    /// per-style methods. Same (params, seed) = same pixels, forever, so
    /// regenerating content is reproducible.
    ///
    /// PROJECT DEFAULT SIZE: 16 px for block textures (matches every existing
    /// block texture, the atlas' "one common size" convention, and the
    /// sub-voxel UV slicing — res-8 sub-faces sample clean 2×2 texel
    /// windows); 64 px for item icons.
    /// </summary>
    public static class TextureSynth
    {
        public const int DefaultBlockSize = 16;
        public const int DefaultIconSize = 64;

        // ------------------------------------------------------------------
        // Public API — material textures
        // ------------------------------------------------------------------

        /// <summary>Pixels for a block-style material texture (row-major, bottom-up like Texture2D).</summary>
        public static Color32[] GeneratePixels(TextureStyle style, Color baseColor, int size, int seed, float hueShift = 0f)
        {
            size = Mathf.Clamp(size, 8, 256);
            Color tinted = ShiftHue(baseColor, hueShift);
            var random = new Random(seed);
            var pixels = new Color32[size * size];

            switch (style)
            {
                case TextureStyle.Stone: FillStone(pixels, size, tinted, random); break;
                case TextureStyle.Wood: FillWood(pixels, size, tinted, random); break;
                case TextureStyle.Sand: FillSand(pixels, size, tinted, random); break;
                case TextureStyle.Grass: FillGrass(pixels, size, tinted, random); break;
                case TextureStyle.Metal: FillMetal(pixels, size, tinted, random); break;
                case TextureStyle.Fabric: FillFabric(pixels, size, tinted, random); break;
                case TextureStyle.Foliage: FillFoliage(pixels, size, tinted, random); break;
                default: FillLiquid(pixels, size, tinted, random); break;
            }

            return pixels;
        }

        /// <summary>In-memory point-filtered texture (previews, runtime use). The editor asset writer persists to disk instead.</summary>
        public static Texture2D GenerateTexture(TextureStyle style, Color baseColor, int size, int seed, float hueShift = 0f)
        {
            return ToTexture(GeneratePixels(style, baseColor, size, seed, hueShift), size);
        }

        // ------------------------------------------------------------------
        // Public API — item icons
        // ------------------------------------------------------------------

        /// <summary>
        /// Pixels for a flat two-tone icon: shape silhouette filled with
        /// primary (head/body) and secondary (handle/grip) plus a 1 px darker
        /// outline, on a transparent background — reads clearly at UI sizes.
        /// </summary>
        public static Color32[] GenerateIconPixels(IconShape shape, Color primary, Color secondary, int size, int seed)
        {
            size = Mathf.Clamp(size, 16, 256);
            var random = new Random(seed);
            var roles = new byte[size * size]; // 0 none, 1 primary, 2 secondary, 3 highlight

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    roles[y * size + x] = SampleShape(shape, u, v);
                }
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;
                    byte role = roles[index];
                    if (role == 0)
                    {
                        pixels[index] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color color = role == 2 ? secondary : role == 3 ? Scale(primary, 1.45f) : primary;

                    // Gentle per-pixel jitter keeps fills from looking flat-vector.
                    color = Scale(color, 1f + Range(random, -0.05f, 0.05f));

                    // 1 px darker outline where the fill meets transparency.
                    if (IsEdge(roles, size, x, y))
                        color = Scale(color, 0.5f);

                    color.a = 1f;
                    pixels[index] = color;
                }
            }

            return pixels;
        }

        public static Texture2D GenerateIconTexture(IconShape shape, Color primary, Color secondary, int size, int seed)
        {
            return ToTexture(GenerateIconPixels(shape, primary, secondary, size, seed), size);
        }

        // ------------------------------------------------------------------
        // Styles — each a distinct pixel algorithm
        // ------------------------------------------------------------------

        /// <summary>Stone: broad mottle blobs, a couple of meandering cracks, sparse speckle.</summary>
        private static void FillStone(Color32[] pixels, int size, Color baseColor, Random random)
        {
            float[] factor = NewFactorField(pixels.Length);

            // Broad darker/lighter patches give the rock its mottle.
            int blobCount = 2 + size / 8;
            for (int i = 0; i < blobCount; i++)
                StampBlob(factor, size, random, radiusFraction: Range(random, 0.18f, 0.34f),
                    strength: Range(random, -0.14f, 0.10f));

            // Cracks: darker random walks wandering down the tile.
            int crackCount = 1 + size / 16;
            for (int i = 0; i < crackCount; i++)
            {
                int x = random.Next(size);
                for (int y = size - 1; y >= 0; y--)
                {
                    factor[y * size + x] *= 0.66f;
                    x = Mathf.Clamp(x + random.Next(-1, 2), 0, size - 1);
                    if (random.NextDouble() < 0.12)
                        break; // cracks may peter out
                }
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                float f = factor[i] * (1f + Range(random, -0.08f, 0.08f));
                if (random.NextDouble() < 0.03)
                    f *= random.NextDouble() < 0.5 ? 0.8f : 1.15f; // speckle
                pixels[i] = ToColor32(Scale(baseColor, f));
            }
        }

        /// <summary>Wood: per-column grain shades, darker grain-line columns, chunky ring knots.</summary>
        private static void FillWood(Color32[] pixels, int size, Color baseColor, Random random)
        {
            float[] columnShade = SmoothNoiseLine(random, size, 0.88f, 1.08f, period: Mathf.Max(3, size / 4));

            // A few columns are full-height darker grain lines.
            var grainLine = new bool[size];
            for (int x = 0; x < size; x++)
                grainLine[x] = random.NextDouble() < 0.18;

            // 0-2 knots: concentric alternating rings, very pixel-art.
            int knotCount = random.Next(0, 3);
            var knotX = new float[knotCount];
            var knotY = new float[knotCount];
            var knotR = new float[knotCount];
            for (int i = 0; i < knotCount; i++)
            {
                knotX[i] = (float)random.NextDouble() * size;
                knotY[i] = (float)random.NextDouble() * size;
                knotR[i] = Range(random, 0.12f, 0.2f) * size;
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float f = columnShade[x] * (1f + Range(random, -0.04f, 0.04f));
                    if (grainLine[x])
                        f *= 0.8f;

                    for (int i = 0; i < knotCount; i++)
                    {
                        float dx = x + 0.5f - knotX[i];
                        float dy = y + 0.5f - knotY[i];
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance < knotR[i])
                            f *= ((int)distance & 1) == 0 ? 0.74f : 0.9f; // rings
                    }

                    pixels[y * size + x] = ToColor32(Scale(baseColor, f));
                }
            }
        }

        /// <summary>Sand: very fine speckle over faint horizontal dune ripples, bright grain glints.</summary>
        private static void FillSand(Color32[] pixels, int size, Color baseColor, Random random)
        {
            float[] ripplePhase = SmoothNoiseLine(random, size, 0f, Mathf.PI * 2f, period: Mathf.Max(4, size / 3));

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float ripple = 1f + 0.035f * Mathf.Sin(y * (Mathf.PI * 2f / size) * 2f + ripplePhase[x]);
                    float f = ripple * (1f + Range(random, -0.05f, 0.05f));

                    double roll = random.NextDouble();
                    if (roll < 0.08)
                        f *= Range(random, 1.1f, 1.2f); // catching-the-light grains
                    else if (roll < 0.11)
                        f *= 0.86f; // darker grains

                    pixels[y * size + x] = ToColor32(Scale(baseColor, f));
                }
            }
        }

        /// <summary>Grass: 2×2 mottle cells plus short vertical blade strokes and dry flecks.</summary>
        private static void FillGrass(Color32[] pixels, int size, Color baseColor, Random random)
        {
            // Blocky 2×2 mottle base — chunky, not per-pixel static.
            int cells = (size + 1) / 2;
            var cellShade = new float[cells * cells];
            for (int i = 0; i < cellShade.Length; i++)
                cellShade[i] = 1f + Range(random, -0.09f, 0.09f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float f = cellShade[(y / 2) * cells + (x / 2)] * (1f + Range(random, -0.03f, 0.03f));
                    pixels[y * size + x] = ToColor32(Scale(baseColor, f));
                }
            }

            // Blade strokes: 1 px wide, 2-4 px tall, alternately dark and light.
            int bladeCount = size;
            for (int i = 0; i < bladeCount; i++)
            {
                int x = random.Next(size);
                int y = random.Next(size);
                int height = random.Next(2, 5);
                float shade = (i & 1) == 0 ? 0.72f : 1.22f;
                for (int step = 0; step < height && y + step < size; step++)
                    pixels[(y + step) * size + x] = ToColor32(Scale(baseColor, shade));
            }

            // Occasional dry yellow fleck.
            var dry = new Color(0.78f, 0.72f, 0.3f);
            int fleckCount = Mathf.Max(2, size / 6);
            for (int i = 0; i < fleckCount; i++)
            {
                int index = random.Next(pixels.Length);
                pixels[index] = ToColor32(Color.Lerp(baseColor, dry, 0.65f));
            }
        }

        /// <summary>Metal: brushed horizontal rows, a soft sheen band, faint long scratches.</summary>
        private static void FillMetal(Color32[] pixels, int size, Color baseColor, Random random)
        {
            float[] rowShade = SmoothNoiseLine(random, size, 0.95f, 1.05f, period: Mathf.Max(2, size / 6));
            float sheenCenter = size * 0.62f;
            float sheenWidth = size * 0.2f;

            for (int y = 0; y < size; y++)
            {
                float distance = (y - sheenCenter) / sheenWidth;
                float sheen = 0.16f * Mathf.Exp(-distance * distance); // subtle highlight band
                for (int x = 0; x < size; x++)
                {
                    float f = rowShade[y] + sheen + Range(random, -0.015f, 0.015f);
                    pixels[y * size + x] = ToColor32(Scale(baseColor, f));
                }
            }

            // Scratches: brighter partial rows.
            int scratchCount = 2 + size / 16;
            for (int i = 0; i < scratchCount; i++)
            {
                int y = random.Next(size);
                int start = random.Next(size / 2);
                int length = random.Next(size / 3, size);
                for (int x = start; x < Mathf.Min(start + length, size); x++)
                {
                    int index = y * size + x;
                    pixels[index] = ToColor32(Scale((Color)pixels[index], 1.09f));
                }
            }
        }

        /// <summary>Fabric: a 2 px warp/weft weave checker with per-thread shading and tint jitter.</summary>
        private static void FillFabric(Color32[] pixels, int size, Color baseColor, Random random)
        {
            int thread = Mathf.Max(2, size / 8);
            int threadsPerSide = size / thread + 1;
            var threadJitter = new float[threadsPerSide * threadsPerSide];
            for (int i = 0; i < threadJitter.Length; i++)
                threadJitter[i] = 1f + Range(random, -0.045f, 0.045f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int threadX = x / thread;
                    int threadY = y / thread;
                    bool over = ((threadX + threadY) & 1) == 0; // warp over weft

                    float f = over ? 1.05f : 0.9f;
                    f *= threadJitter[threadY * threadsPerSide + threadX];

                    // Thread edges dip into the weave — the trailing pixel of
                    // each thread cell along its run direction darkens.
                    bool edge = over ? (y % thread) == thread - 1 : (x % thread) == thread - 1;
                    if (edge)
                        f *= 0.86f;

                    pixels[y * size + x] = ToColor32(Scale(baseColor, f));
                }
            }
        }

        /// <summary>Foliage: soft light/dark leaf-mass blobs, deep shadow gaps, single-pixel glints.</summary>
        private static void FillFoliage(Color32[] pixels, int size, Color baseColor, Random random)
        {
            float[] factor = NewFactorField(pixels.Length);

            int blobCount = 3 + size / 6;
            for (int i = 0; i < blobCount; i++)
                StampBlob(factor, size, random, radiusFraction: Range(random, 0.14f, 0.26f),
                    strength: (i & 1) == 0 ? Range(random, -0.18f, -0.08f) : Range(random, 0.06f, 0.14f));

            for (int i = 0; i < pixels.Length; i++)
            {
                float f = factor[i] * (1f + Range(random, -0.06f, 0.06f));

                double roll = random.NextDouble();
                if (roll < 0.04)
                    f *= 0.55f; // gaps into the canopy's dark interior
                else if (roll < 0.07)
                    f *= 1.25f; // leaf glints

                pixels[i] = ToColor32(Scale(baseColor, f));
            }
        }

        /// <summary>Liquid: smooth vertical depth gradient, gentle wave banding, sparse highlight streaks.</summary>
        private static void FillLiquid(Color32[] pixels, int size, Color baseColor, Random random)
        {
            float[] wavePhase = SmoothNoiseLine(random, size, 0f, Mathf.PI * 2f, period: Mathf.Max(4, size / 2));

            for (int y = 0; y < size; y++)
            {
                float depth = Mathf.Lerp(0.82f, 1.12f, (y + 0.5f) / size); // lighter surface up top
                for (int x = 0; x < size; x++)
                {
                    float wave = 1f + 0.04f * Mathf.Sin(x * (Mathf.PI * 2f / size) * 2f + wavePhase[y]);
                    pixels[y * size + x] = ToColor32(Scale(baseColor, depth * wave));
                }
            }

            // Highlight streaks: short horizontal glimmers near the top half.
            int streakCount = 3 + size / 8;
            for (int i = 0; i < streakCount; i++)
            {
                int y = size / 2 + random.Next(size / 2);
                int x = random.Next(size);
                int length = random.Next(2, 5);
                for (int step = 0; step < length && x + step < size; step++)
                {
                    int index = y * size + x + step;
                    pixels[index] = ToColor32(Scale((Color)pixels[index], 1.28f));
                }
            }
        }

        // ------------------------------------------------------------------
        // Icon silhouettes (u,v in 0..1, v up). Returns the role byte.
        // ------------------------------------------------------------------

        private static byte SampleShape(IconShape shape, float u, float v)
        {
            switch (shape)
            {
                case IconShape.RoundedSquare:
                {
                    float dx = Mathf.Max(Mathf.Abs(u - 0.5f) - 0.24f, 0f);
                    float dy = Mathf.Max(Mathf.Abs(v - 0.5f) - 0.24f, 0f);
                    return Mathf.Sqrt(dx * dx + dy * dy) < 0.12f ? (byte)1 : (byte)0;
                }

                case IconShape.Circle:
                {
                    float dx = u - 0.5f;
                    float dy = v - 0.5f;
                    return dx * dx + dy * dy < 0.36f * 0.36f ? (byte)1 : (byte)0;
                }

                case IconShape.Pickaxe:
                {
                    // Head: an arc hugging the top-right; handle: the diagonal shaft.
                    float cornerDx = 0.95f - u;
                    float cornerDy = 0.95f - v;
                    float cornerDistance = Mathf.Sqrt(cornerDx * cornerDx + cornerDy * cornerDy);
                    if (cornerDistance > 0.26f && cornerDistance < 0.5f && u + v > 1.05f)
                        return 1;

                    if (SegmentDistance(u, v, 0.16f, 0.16f, 0.8f, 0.8f) < 0.055f)
                        return 2;
                    return 0;
                }

                case IconShape.Axe:
                {
                    // Blade: a lobe on the upper-left side of the shaft's end.
                    float bladeDx = u - 0.62f;
                    float bladeDy = v - 0.74f;
                    if (bladeDx * bladeDx + bladeDy * bladeDy < 0.055f && v - u > -0.02f)
                        return 1;

                    if (SegmentDistance(u, v, 0.18f, 0.14f, 0.74f, 0.7f) < 0.05f)
                        return 2;
                    return 0;
                }

                case IconShape.Sword:
                {
                    if (SegmentDistance(u, v, 0.38f, 0.38f, 0.86f, 0.86f) < 0.05f)
                        return 1; // blade
                    if (SegmentDistance(u, v, 0.26f, 0.46f, 0.46f, 0.26f) < 0.035f)
                        return 1; // guard
                    if (SegmentDistance(u, v, 0.16f, 0.16f, 0.3f, 0.3f) < 0.045f)
                        return 2; // grip
                    return 0;
                }

                case IconShape.Droplet:
                {
                    // Sheen dot first so it wins over the body fill.
                    float sheenDx = u - 0.41f;
                    float sheenDy = v - 0.33f;
                    if (sheenDx * sheenDx + sheenDy * sheenDy < 0.055f * 0.055f)
                        return 3;

                    float bodyDx = u - 0.5f;
                    float bodyDy = v - 0.4f;
                    if (bodyDx * bodyDx + bodyDy * bodyDy < 0.26f * 0.26f)
                        return 1;

                    if (v >= 0.4f && v <= 0.86f)
                    {
                        float halfWidth = 0.26f * (0.86f - v) / 0.46f;
                        if (Mathf.Abs(u - 0.5f) < halfWidth)
                            return 1;
                    }

                    return 0;
                }

                default: // Ingot
                {
                    if (v < 0.32f || v > 0.62f)
                        return 0;

                    float t = (v - 0.32f) / 0.3f;
                    float halfWidth = Mathf.Lerp(0.34f, 0.26f, t);
                    if (Mathf.Abs(u - 0.5f) >= halfWidth)
                        return 0;

                    return v > 0.54f ? (byte)3 : (byte)1; // lighter top face
                }
            }
        }

        // ------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------

        private static float[] NewFactorField(int length)
        {
            var factor = new float[length];
            for (int i = 0; i < length; i++)
                factor[i] = 1f;
            return factor;
        }

        /// <summary>Adds a soft radial brightness offset at a random spot — the mottle building block.</summary>
        private static void StampBlob(float[] factor, int size, Random random, float radiusFraction, float strength)
        {
            float centerX = (float)random.NextDouble() * size;
            float centerY = (float)random.NextDouble() * size;
            float radius = radiusFraction * size;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radius));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(centerX + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radius));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(centerY + radius));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x + 0.5f - centerX;
                    float dy = y + 0.5f - centerY;
                    float falloff = 1f - Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    if (falloff > 0f)
                        factor[y * size + x] += strength * falloff;
                }
            }
        }

        /// <summary>Seeded 1D value noise with cosine interpolation — smooth wood grain / brushed rows.</summary>
        private static float[] SmoothNoiseLine(Random random, int size, float min, float max, int period)
        {
            int knots = size / period + 2;
            var knotValues = new float[knots];
            for (int i = 0; i < knots; i++)
                knotValues[i] = Range(random, min, max);

            var line = new float[size];
            for (int i = 0; i < size; i++)
            {
                float position = (float)i / period;
                int knot = (int)position;
                float t = position - knot;
                t = (1f - Mathf.Cos(t * Mathf.PI)) * 0.5f; // cosine ease
                line[i] = Mathf.Lerp(knotValues[knot], knotValues[knot + 1], t);
            }

            return line;
        }

        private static bool IsEdge(byte[] roles, int size, int x, int y)
        {
            if (x == 0 || x == size - 1 || y == 0 || y == size - 1)
                return true;

            return roles[y * size + x - 1] == 0 || roles[y * size + x + 1] == 0
                || roles[(y - 1) * size + x] == 0 || roles[(y + 1) * size + x] == 0;
        }

        private static float SegmentDistance(float u, float v, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float lengthSq = abx * abx + aby * aby;
            float t = lengthSq > 1e-8f
                ? Mathf.Clamp01(((u - ax) * abx + (v - ay) * aby) / lengthSq)
                : 0f;
            float dx = u - (ax + abx * t);
            float dy = v - (ay + aby * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Color ShiftHue(Color color, float hueShift)
        {
            if (Mathf.Approximately(hueShift, 0f))
                return color;

            Color.RGBToHSV(color, out float h, out float s, out float v);
            var shifted = Color.HSVToRGB(Mathf.Repeat(h + hueShift, 1f), s, v);
            shifted.a = color.a;
            return shifted;
        }

        private static Color Scale(Color color, float factor)
        {
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                color.a);
        }

        private static float Range(Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static Color32 ToColor32(Color color)
        {
            return color;
        }

        private static Texture2D ToTexture(Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
