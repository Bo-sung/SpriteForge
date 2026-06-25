using SkiaSharp;

namespace PixelSprite.Core.PixelArt;

/// <summary>
/// Reduces the opaque colors of a bitmap to a limited palette. Uses the vendored
/// <see cref="WuColorQuantizer"/> when no fixed palette is supplied, otherwise maps each opaque
/// pixel to its nearest fixed-palette color. Transparent pixels are never quantized.
/// </summary>
public static class PaletteQuantizer
{
    /// <summary>
    /// Quantizes the opaque colors of <paramref name="src"/>.
    /// </summary>
    /// <param name="src">The source bitmap (unpremultiplied RGBA).</param>
    /// <param name="maxColors">Maximum palette size when running Wu quantization.</param>
    /// <param name="fixedPalette">
    /// When non-null and non-empty, Wu quantization is skipped and each opaque pixel is mapped to the
    /// nearest color in this palette (Euclidean RGB distance).
    /// </param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    public static SKBitmap Quantize(SKBitmap src, int maxColors, SKColor[]? fixedPalette = null)
    {
        ArgumentNullException.ThrowIfNull(src);

        int w = src.Width, h = src.Height;
        SKColor[] px = BitmapHelpers.Read(src);
        var output = new SKColor[px.Length];

        // Transparent pixels (A == 0) are excluded from quantization and pass through unchanged.
        // Passing them through (rather than forcing (0,0,0,0)) preserves the RGB that edge dilation
        // wrote under transparent border pixels, which is what prevents dark fringing in the final PNG.
        bool useFixed = fixedPalette is { Length: > 0 };
        WuColorQuantizer? wu = null;

        if (!useFixed)
        {
            // Build the Wu palette from the opaque pixels only.
            var opaque = new List<SKColor>();
            foreach (SKColor c in px)
            {
                if (c.Alpha != 0)
                {
                    opaque.Add(c);
                }
            }

            if (opaque.Count == 0)
            {
                // Nothing to quantize; return a transparent copy.
                var empty = BitmapHelpers.CreateRgba(w, h);
                BitmapHelpers.Write(empty, px);
                return empty;
            }

            wu = new WuColorQuantizer(opaque, maxColors);
        }

        for (int i = 0; i < px.Length; i++)
        {
            SKColor c = px[i];
            if (c.Alpha == 0)
            {
                output[i] = c; // pass through (preserves edge-dilation RGB)
                continue;
            }

            output[i] = useFixed
                ? NearestColor(c, fixedPalette!)
                : wu!.MapToPalette(c);
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }

    /// <summary>
    /// Loads a fixed palette from a PNG file: every distinct opaque color becomes a palette entry.
    /// </summary>
    /// <param name="path">Path to a palette PNG.</param>
    /// <returns>The distinct opaque colors found in the image.</returns>
    /// <exception cref="FileNotFoundException">The palette file does not exist.</exception>
    /// <exception cref="InvalidOperationException">The palette file could not be decoded or has no opaque colors.</exception>
    public static SKColor[] LoadPalettePng(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Palette file not found: {path}", path);
        }

        using SKBitmap? bmp = SKBitmap.Decode(path);
        if (bmp is null)
        {
            throw new InvalidOperationException($"Could not decode palette image: {path}");
        }

        var seen = new HashSet<uint>();
        var colors = new List<SKColor>();
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                if (c.Alpha == 0)
                {
                    continue;
                }

                var rgb = new SKColor(c.Red, c.Green, c.Blue, 255);
                if (seen.Add((uint)rgb))
                {
                    colors.Add(rgb);
                }
            }
        }

        if (colors.Count == 0)
        {
            throw new InvalidOperationException($"Palette image has no opaque colors: {path}");
        }

        return colors.ToArray();
    }

    /// <summary>Finds the nearest palette color by squared Euclidean distance in RGB.</summary>
    private static SKColor NearestColor(SKColor c, SKColor[] palette)
    {
        int best = 0;
        long bestDist = long.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            SKColor p = palette[i];
            int dr = c.Red - p.Red;
            int dg = c.Green - p.Green;
            int db = c.Blue - p.Blue;
            long dist = (long)(dr * dr) + (dg * dg) + (db * db);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        // Preserve full opacity; transparent pixels never reach here.
        SKColor chosen = palette[best];
        return new SKColor(chosen.Red, chosen.Green, chosen.Blue, 255);
    }
}
