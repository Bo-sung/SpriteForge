using PixelSprite.Core.Models;
using PixelSprite.Core.PixelArt;
using SkiaSharp;

namespace PixelSprite.Core.Packing;

/// <summary>
/// Assembles processed pixel-art frames into a single sprite sheet. Rows correspond to directions
/// and columns to frames. The sheet background is always fully transparent.
/// </summary>
public static class SpriteSheetPacker
{
    /// <summary>
    /// Packs <paramref name="frames"/> into one sheet bitmap.
    /// </summary>
    /// <param name="frames">
    /// The processed frames to place. Each frame is positioned at
    /// <c>(FrameIndex * spriteW, DirectionIndex * spriteH)</c>.
    /// </param>
    /// <param name="spriteW">Width of a single sprite cell in pixels.</param>
    /// <param name="spriteH">Height of a single sprite cell in pixels.</param>
    /// <param name="dirCount">Number of directions (rows).</param>
    /// <param name="frameCount">Number of frames per direction (columns).</param>
    /// <returns>
    /// A new RGBA8888 (unpremultiplied) bitmap of size
    /// <c>(spriteW * frameCount) x (spriteH * dirCount)</c> with a transparent background.
    /// The caller owns and must dispose it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="frames"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any size or count argument is not positive.</exception>
    public static SKBitmap Pack(
        IReadOnlyList<SpriteFrame> frames,
        int spriteW,
        int spriteH,
        int dirCount,
        int frameCount)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spriteW);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spriteH);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dirCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);

        int width = spriteW * frameCount;
        int height = spriteH * dirCount;

        // Canonical unpremultiplied RGBA8888 surface, already erased to fully transparent.
        SKBitmap target = BitmapHelpers.CreateRgba(width, height);

        using var canvas = new SKCanvas(target);

        // Never fill the background with a color — the sheet must stay transparent.
        canvas.Clear(SKColors.Transparent);

        // Nearest sampling keeps pixel-art crisp (frames are placed 1:1, so this is just future-proofing).
        var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
        foreach (SpriteFrame frame in frames)
        {
            int x = frame.FrameIndex * spriteW;
            int y = frame.DirectionIndex * spriteH;
            canvas.DrawBitmap(frame.Bitmap, x, y, sampling);
        }

        return target;
    }
}
