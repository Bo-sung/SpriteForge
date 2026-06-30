using SkiaSharp;
using SpriteForge.Core.Models;

namespace SpriteForge.Core.PixelArt;

/// <summary>
/// Draws an outline around the sprite silhouette. Runs as the final pixel-art step (after jaggy
/// cleanup), so the silhouette is already finalized when the outline is laid down and no later pass
/// can erase it.
/// </summary>
public static class OutlinePass
{
    /// <summary>
    /// Returns a new bitmap with an outline applied. The source is never modified.
    /// </summary>
    /// <param name="src">The source bitmap (unpremultiplied RGBA, binarized alpha).</param>
    /// <param name="outlineColor">The outline color; its alpha is ignored (drawn fully opaque).</param>
    /// <param name="type">
    /// <see cref="OutlineType.Outer"/> paints the outline into the transparent pixels bordering the
    /// silhouette (growing it); <see cref="OutlineType.Inner"/> recolors the silhouette's own
    /// boundary pixels (keeping its size).
    /// </param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    /// <remarks>
    /// The output keeps the layer's canonical unpremultiplied RGBA format: outline pixels are opaque,
    /// and transparent pixels that are not turned into outline keep their (edge-dilation) RGB so the
    /// final PNG stays free of dark fringing.
    /// </remarks>
    public static SKBitmap Apply(SKBitmap src, SKColor outlineColor, OutlineType type)
    {
        ArgumentNullException.ThrowIfNull(src);

        int w = src.Width, h = src.Height;
        SKColor[] px = BitmapHelpers.Read(src);

        // Decisions read from the original snapshot (px); writes go to a separate buffer so a freshly
        // painted outline pixel never seeds further outline within the same pass.
        var output = (SKColor[])px.Clone();
        var outline = new SKColor(outlineColor.Red, outlineColor.Green, outlineColor.Blue, 255);

        ReadOnlySpan<(int dx, int dy)> neighbors = stackalloc (int, int)[]
        {
            (0, -1), (0, 1), (-1, 0), (1, 0),
        };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                if (px[idx].Alpha != 255)
                {
                    continue; // only opaque silhouette pixels seed an outline
                }

                foreach (var (dx, dy) in neighbors)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    {
                        continue; // the image border is not a transparent edge
                    }

                    int nIdx = (ny * w) + nx;
                    if (px[nIdx].Alpha != 0)
                    {
                        continue; // neighbor is opaque -> no silhouette edge here
                    }

                    // An opaque pixel borders a transparent one.
                    if (type == OutlineType.Outer)
                    {
                        output[nIdx] = outline; // paint the transparent neighbor (never an opaque pixel)
                    }
                    else
                    {
                        output[idx] = outline; // recolor this boundary pixel
                        break;                  // inner: one transparent neighbor is enough
                    }
                }
            }
        }

        var dst = BitmapHelpers.CreateRgba(w, h);
        BitmapHelpers.Write(dst, output);
        return dst;
    }
}
