using SkiaSharp;

namespace PixelSprite.Core.PixelArt;

/// <summary>
/// Shared helpers for the pixel-art processing layer.
/// </summary>
/// <remarks>
/// All working bitmaps in this layer use <see cref="SKColorType.Rgba8888"/> with
/// <see cref="SKAlphaType.Unpremul"/>. Unpremultiplied storage is required so that R, G, B can be
/// manipulated independently of A — in particular so edge dilation can keep meaningful RGB under
/// fully transparent (A=0) pixels to prevent dark fringing. Premultiplied alpha belongs to the
/// OpenGL blend stage, not to this post-processing stage.
/// </remarks>
internal static class BitmapHelpers
{
    /// <summary>The canonical image info used throughout the pixel-art layer.</summary>
    public static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>Creates a new unpremultiplied RGBA8888 bitmap, cleared to fully transparent.</summary>
    public static SKBitmap CreateRgba(int width, int height)
    {
        var bmp = new SKBitmap(Info(width, height));
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    /// <summary>Reads every pixel into a row-major <see cref="SKColor"/> array (index = y * width + x).</summary>
    public static SKColor[] Read(SKBitmap src)
    {
        int w = src.Width, h = src.Height;
        var px = new SKColor[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                px[(y * w) + x] = src.GetPixel(x, y);
            }
        }

        return px;
    }

    /// <summary>Writes a row-major <see cref="SKColor"/> array back into a bitmap.</summary>
    public static void Write(SKBitmap dst, SKColor[] px)
    {
        int w = dst.Width, h = dst.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dst.SetPixel(x, y, px[(y * w) + x]);
            }
        }
    }
}
