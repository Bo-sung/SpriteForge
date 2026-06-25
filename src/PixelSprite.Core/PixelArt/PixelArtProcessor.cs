using PixelSprite.Core.Models;
using SkiaSharp;

namespace PixelSprite.Core.PixelArt;

/// <summary>
/// Orchestrates the full pixel-art conversion of a single high-resolution rendered frame.
/// </summary>
/// <remarks>
/// Pipeline order (per the project spec):
/// <list type="number">
/// <item>Morphological clean (optional)</item>
/// <item>Dominant-color downscale</item>
/// <item>Alpha binarize (+ edge dilation)</item>
/// <item>Palette quantize</item>
/// <item>Jaggy clean (optional)</item>
/// </list>
/// </remarks>
public sealed class PixelArtProcessor
{
    private string? _cachedPalettePath;
    private SKColor[]? _cachedPalette;

    /// <summary>Converts a high-resolution frame into a finished pixel-art bitmap.</summary>
    /// <param name="highResFrame">The rendered frame (unpremultiplied RGBA, transparent background).</param>
    /// <param name="opts">Pixel-art options.</param>
    /// <returns>A new bitmap at <see cref="PixelArtOptions.SpriteSize"/> resolution; the caller owns it.</returns>
    public SKBitmap Process(SKBitmap highResFrame, PixelArtOptions opts)
    {
        ArgumentNullException.ThrowIfNull(highResFrame);
        ArgumentNullException.ThrowIfNull(opts);

        SKColor[]? fixedPalette = LoadFixedPalette(opts.PalettePath);

        // Each step returns a fresh bitmap; dispose intermediates as we go. Start from a normalized
        // unpremultiplied RGBA copy so the caller's bitmap is never disposed here and the format is
        // canonical regardless of how the renderer produced the frame.
        SKBitmap current = BitmapHelpers.CreateRgba(highResFrame.Width, highResFrame.Height);
        BitmapHelpers.Write(current, BitmapHelpers.Read(highResFrame));

        if (opts.Cleanup.Morph)
        {
            current = Replace(current, ArtifactCleaner.MorphClean(current));
        }

        current = Replace(current, DominantDownscaler.Downscale(
            current, opts.SpriteSize, opts.SpriteSize, opts.AlphaThreshold));

        current = Replace(current, AlphaBinarizer.Binarize(
            current, opts.AlphaThreshold, opts.EdgeDilate));

        current = Replace(current, PaletteQuantizer.Quantize(
            current, opts.MaxColors, fixedPalette));

        if (opts.Cleanup.Jaggy)
        {
            current = Replace(current, ArtifactCleaner.JaggyClean(current));
        }

        return current;
    }

    /// <summary>Disposes the previous bitmap and returns the next one (fluent step chaining).</summary>
    private static SKBitmap Replace(SKBitmap previous, SKBitmap next)
    {
        previous.Dispose();
        return next;
    }

    private SKColor[]? LoadFixedPalette(string? palettePath)
    {
        if (string.IsNullOrEmpty(palettePath))
        {
            return null;
        }

        if (_cachedPalette is not null && _cachedPalettePath == palettePath)
        {
            return _cachedPalette;
        }

        _cachedPalette = PaletteQuantizer.LoadPalettePng(palettePath);
        _cachedPalettePath = palettePath;
        return _cachedPalette;
    }
}
