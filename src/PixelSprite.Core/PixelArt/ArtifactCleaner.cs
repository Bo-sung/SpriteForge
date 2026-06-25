using SkiaSharp;

namespace PixelSprite.Core.PixelArt;

/// <summary>
/// Removes downscaling artifacts: a morphological open that erases isolated noise, and a jaggy
/// pass that fixes lone pixels differing from all of their cardinal neighbors.
/// </summary>
public static class ArtifactCleaner
{
    /// <summary>Foreground/background threshold used by the morphological pass.</summary>
    private const byte ForegroundAlpha = 128;

    /// <summary>
    /// Morphological open (erosion then dilation, 3×3 / 8-connected) on the opaque mask. Removes
    /// isolated opaque specks and thin protrusions; the result is always a subset of the original
    /// foreground, so surviving pixels keep their original color and removed pixels become transparent.
    /// </summary>
    /// <param name="src">The source bitmap (unpremultiplied RGBA). Alpha may be continuous.</param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    public static SKBitmap MorphClean(SKBitmap src)
    {
        ArgumentNullException.ThrowIfNull(src);

        int w = src.Width, h = src.Height;
        SKColor[] px = BitmapHelpers.Read(src);

        bool[] mask = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            mask[i] = px[i].Alpha >= ForegroundAlpha;
        }

        bool[] eroded = Erode(mask, w, h);
        bool[] opened = Dilate(eroded, w, h);

        var output = new SKColor[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            // Opening is anti-extensive (opened ⊆ original mask), so px[i] is meaningful where kept.
            output[i] = opened[i] ? px[i] : new SKColor(0, 0, 0, 0);
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }

    /// <summary>
    /// Jaggy cleanup: any opaque pixel whose color differs from every one of its (in-bounds) cardinal
    /// neighbors is replaced by the most frequent neighbor color. Transparent pixels are left
    /// untouched so the background and edge-dilation RGB are preserved.
    /// </summary>
    /// <param name="src">The source bitmap (unpremultiplied RGBA).</param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    public static SKBitmap JaggyClean(SKBitmap src)
    {
        ArgumentNullException.ThrowIfNull(src);

        int w = src.Width, h = src.Height;
        SKColor[] px = BitmapHelpers.Read(src);
        var output = (SKColor[])px.Clone();

        ReadOnlySpan<(int dx, int dy)> neighbors = stackalloc (int, int)[]
        {
            (0, -1), (0, 1), (-1, 0), (1, 0),
        };

        // Reused per-pixel scratch buffer for the (up to 4) cardinal neighbors.
        Span<SKColor> found = stackalloc SKColor[4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                SKColor c = px[idx];
                if (c.Alpha == 0)
                {
                    continue; // never alter transparent pixels
                }

                int count = 0;
                bool matchesAny = false;

                foreach (var (dx, dy) in neighbors)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    {
                        continue;
                    }

                    SKColor n = px[(ny * w) + nx];
                    found[count++] = n;
                    if (n == c)
                    {
                        matchesAny = true;
                        break;
                    }
                }

                if (matchesAny || count == 0)
                {
                    continue; // pixel agrees with a neighbor (or has none) -> not a jaggy
                }

                output[idx] = Majority(found[..count]);
            }
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }

    private static bool[] Erode(bool[] mask, int w, int h)
    {
        var result = new bool[mask.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                result[(y * w) + x] = AllNeighborsForeground(mask, w, h, x, y);
            }
        }

        return result;
    }

    private static bool[] Dilate(bool[] mask, int w, int h)
    {
        var result = new bool[mask.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                result[(y * w) + x] = AnyNeighborForeground(mask, w, h, x, y);
            }
        }

        return result;
    }

    /// <summary>True only if the pixel and all 8 neighbors are foreground (out-of-bounds counts as background).</summary>
    private static bool AllNeighborsForeground(bool[] mask, int w, int h, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !mask[(ny * w) + nx])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>True if the pixel or any of its 8 neighbors is foreground.</summary>
    private static bool AnyNeighborForeground(bool[] mask, int w, int h, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[(ny * w) + nx])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Returns the most frequent color among the given neighbors (first-seen wins ties).</summary>
    private static SKColor Majority(ReadOnlySpan<SKColor> colors)
    {
        SKColor best = colors[0];
        int bestCount = 0;
        for (int i = 0; i < colors.Length; i++)
        {
            int count = 0;
            for (int j = 0; j < colors.Length; j++)
            {
                if (colors[j] == colors[i])
                {
                    count++;
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                best = colors[i];
            }
        }

        return best;
    }
}
