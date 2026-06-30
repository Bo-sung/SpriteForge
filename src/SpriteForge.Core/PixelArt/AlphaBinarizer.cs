using SkiaSharp;

namespace SpriteForge.Core.PixelArt;

/// <summary>
/// Binarizes the alpha channel (each pixel becomes fully opaque or fully transparent) and,
/// optionally, dilates RGB into transparent border pixels to prevent dark fringing.
/// Runs after downscaling and before palette quantization.
/// </summary>
public static class AlphaBinarizer
{
    /// <summary>
    /// Produces a new bitmap whose alpha is hard-thresholded to 0 or 255.
    /// </summary>
    /// <param name="src">The source bitmap (unpremultiplied RGBA).</param>
    /// <param name="threshold">Alpha cutoff in the range 0-255: <c>A &gt;= threshold</c> becomes opaque. Default 128.</param>
    /// <param name="edgeDilate">
    /// When true, transparent pixels that border an opaque pixel receive that neighbor's RGB
    /// (alpha stays 0). This prevents dark halos when the sprite is later filtered or scaled.
    /// </param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    public static SKBitmap Binarize(SKBitmap src, byte threshold = 128, bool edgeDilate = true)
    {
        ArgumentNullException.ThrowIfNull(src);

        int w = src.Width, h = src.Height;
        SKColor[] input = BitmapHelpers.Read(src);

        // Step 1: hard-threshold alpha; RGB is left untouched.
        var binar = new SKColor[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            SKColor c = input[i];
            byte a = c.Alpha >= threshold ? (byte)255 : (byte)0;
            binar[i] = new SKColor(c.Red, c.Green, c.Blue, a);
        }

        SKColor[] output = binar;

        // Step 2: edge dilation. Read from the step-1 snapshot, write to a fresh buffer so that
        // newly filled RGB does not cascade within a single pass. Alpha is never changed here.
        if (edgeDilate)
        {
            output = (SKColor[])binar.Clone();

            // Cardinal neighbor offsets, checked in order (up, down, left, right).
            ReadOnlySpan<(int dx, int dy)> neighbors = stackalloc (int, int)[]
            {
                (0, -1), (0, 1), (-1, 0), (1, 0),
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = (y * w) + x;
                    if (binar[idx].Alpha != 0)
                    {
                        continue; // only fill transparent pixels
                    }

                    foreach (var (dx, dy) in neighbors)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        {
                            continue;
                        }

                        SKColor n = binar[(ny * w) + nx];
                        if (n.Alpha == 255)
                        {
                            // Copy neighbor RGB; keep this pixel transparent.
                            output[idx] = new SKColor(n.Red, n.Green, n.Blue, 0);
                            break;
                        }
                    }
                }
            }
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }
}
