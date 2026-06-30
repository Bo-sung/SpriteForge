using SkiaSharp;
using SpriteForge.Core.Models;

namespace SpriteForge.Core.PixelArt;

/// <summary>
/// Dithers opaque pixels against a target palette just before quantization. Two modes are supported:
/// ordered (4×4 Bayer) and Floyd–Steinberg error diffusion. Because the pass maps every opaque pixel
/// to a palette color, the subsequent <see cref="PaletteQuantizer"/> step (run with the same palette)
/// is idempotent and preserves the dither pattern exactly.
/// </summary>
public static class DitherPass
{
    // Standard 4×4 Bayer (recursive-tiling) threshold matrix, values 0..15.
    private static readonly int[,] Bayer4x4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    /// <summary>
    /// Resolves the palette the dither pass and the quantizer should share: the supplied fixed
    /// palette when non-empty, otherwise a Wu palette built from the opaque pixels of
    /// <paramref name="src"/>.
    /// </summary>
    /// <param name="src">The bitmap whose opaque colors drive Wu palette selection.</param>
    /// <param name="maxColors">Maximum palette size for Wu quantization.</param>
    /// <param name="fixedPalette">A caller-supplied fixed palette, or null to build one.</param>
    /// <returns>The resolved palette; empty if the image has no opaque pixels.</returns>
    public static SKColor[] ResolvePalette(SKBitmap src, int maxColors, SKColor[]? fixedPalette)
    {
        ArgumentNullException.ThrowIfNull(src);

        if (fixedPalette is { Length: > 0 })
        {
            return fixedPalette;
        }

        SKColor[] px = BitmapHelpers.Read(src);
        var opaque = new List<SKColor>();
        foreach (SKColor c in px)
        {
            if (c.Alpha != 0)
            {
                opaque.Add(c);
            }
        }

        return opaque.Count == 0
            ? Array.Empty<SKColor>()
            : new WuColorQuantizer(opaque, maxColors).Palette;
    }

    /// <summary>
    /// Returns a new bitmap with the opaque pixels dithered to <paramref name="palette"/>. Transparent
    /// pixels (A=0) pass through unchanged so the edge-dilation RGB underneath them is preserved.
    /// </summary>
    /// <param name="src">The source bitmap (unpremultiplied RGBA).</param>
    /// <param name="mode">The dithering algorithm; <see cref="DitherMode.None"/> returns a plain copy.</param>
    /// <param name="palette">The target palette (typically from <see cref="ResolvePalette"/>).</param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    public static SKBitmap Apply(SKBitmap src, DitherMode mode, SKColor[] palette)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(palette);

        if (mode == DitherMode.None || palette.Length == 0)
        {
            // Nothing to do; return a normalized copy so the caller can dispose intermediates uniformly.
            var copy = BitmapHelpers.CreateRgba(src.Width, src.Height);
            BitmapHelpers.Write(copy, BitmapHelpers.Read(src));
            return copy;
        }

        return mode == DitherMode.Bayer ? ApplyBayer(src, palette) : ApplyFloyd(src, palette);
    }

    private static SKBitmap ApplyBayer(SKBitmap src, SKColor[] palette)
    {
        int w = src.Width, h = src.Height;
        SKColor[] px = BitmapHelpers.Read(src);
        var output = new SKColor[px.Length];

        // Ordered-dither amplitude ≈ palette spacing, approximating the palette as a uniform RGB cube.
        double amplitude = 255.0 / Math.Max(1.0, Math.Cbrt(palette.Length));

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                SKColor c = px[idx];
                if (c.Alpha == 0)
                {
                    output[idx] = c; // preserve transparent (edge-dilation RGB)
                    continue;
                }

                // Threshold offset in [-0.5, 0.5) from the Bayer cell, scaled to the amplitude.
                double offset = (((Bayer4x4[y & 3, x & 3] + 0.5) / 16.0) - 0.5) * amplitude;
                var nudged = new SKColor(
                    ClampToByte(c.Red + offset),
                    ClampToByte(c.Green + offset),
                    ClampToByte(c.Blue + offset),
                    255);
                output[idx] = Nearest(nudged, palette);
            }
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }

    private static SKBitmap ApplyFloyd(SKBitmap src, SKColor[] palette)
    {
        int w = src.Width, h = src.Height;
        SKColor[] px = BitmapHelpers.Read(src);
        var output = (SKColor[])px.Clone();

        // Per-channel accumulated error, diffused forward in scan order.
        var errR = new float[px.Length];
        var errG = new float[px.Length];
        var errB = new float[px.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                SKColor c = px[idx];
                if (c.Alpha == 0)
                {
                    continue; // transparent passes through; error is not applied here
                }

                float fr = c.Red + errR[idx];
                float fg = c.Green + errG[idx];
                float fb = c.Blue + errB[idx];

                SKColor chosen = Nearest(
                    new SKColor(ClampToByte(fr), ClampToByte(fg), ClampToByte(fb), 255), palette);
                output[idx] = chosen;

                float dr = fr - chosen.Red;
                float dg = fg - chosen.Green;
                float db = fb - chosen.Blue;

                // Floyd–Steinberg distribution: 7/16 right, 3/16 below-left, 5/16 below, 1/16 below-right.
                Diffuse(errR, errG, errB, x + 1, y, w, h, dr, dg, db, 7f / 16f);
                Diffuse(errR, errG, errB, x - 1, y + 1, w, h, dr, dg, db, 3f / 16f);
                Diffuse(errR, errG, errB, x, y + 1, w, h, dr, dg, db, 5f / 16f);
                Diffuse(errR, errG, errB, x + 1, y + 1, w, h, dr, dg, db, 1f / 16f);
            }
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }

    private static void Diffuse(
        float[] errR, float[] errG, float[] errB, int x, int y, int w, int h,
        float dr, float dg, float db, float factor)
    {
        if (x < 0 || y < 0 || x >= w || y >= h)
        {
            return;
        }

        int i = (y * w) + x;
        errR[i] += dr * factor;
        errG[i] += dg * factor;
        errB[i] += db * factor;
    }

    /// <summary>Finds the nearest palette color by squared Euclidean distance in RGB (alpha forced 255).</summary>
    private static SKColor Nearest(SKColor c, SKColor[] palette)
    {
        int best = 0;
        long bestDist = long.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            SKColor p = palette[i];
            int dr = c.Red - p.Red;
            int dg = c.Green - p.Green;
            int db = c.Blue - p.Blue;
            long dist = ((long)dr * dr) + ((long)dg * dg) + ((long)db * db);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        SKColor chosen = palette[best];
        return new SKColor(chosen.Red, chosen.Green, chosen.Blue, 255);
    }

    private static byte ClampToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}
