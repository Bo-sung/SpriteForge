using SpriteForge.Core.Models;
using SkiaSharp;

namespace SpriteForge.Core.PixelArt;

/// <summary>
/// Orchestrates the full pixel-art conversion of a single high-resolution rendered frame.
/// </summary>
/// <remarks>
/// Pipeline order:
/// <list type="number">
/// <item>Morphological clean (optional)</item>
/// <item>Dominant-color downscale (skipped when <see cref="PixelArtOptions.SpriteSize"/> is 0)</item>
/// <item>Alpha binarize (+ edge dilation)</item>
/// <item>Dither (optional; before quantization, against the shared palette)</item>
/// <item>Palette quantize</item>
/// <item>Jaggy clean (optional)</item>
/// <item>Outline (optional; after jaggy, on the finalized silhouette)</item>
/// </list>
/// </remarks>
public sealed class PixelArtProcessor
{
    private string? _cachedPalettePath;
    private SKColor[]? _cachedPalette;

    /// <summary>Converts a high-resolution frame into a finished pixel-art bitmap.</summary>
    /// <param name="highResFrame">The rendered frame (unpremultiplied RGBA, transparent background).</param>
    /// <param name="opts">Pixel-art options.</param>
    /// <returns>
    /// A new bitmap; the caller owns it. Square at <see cref="PixelArtOptions.SpriteSize"/> resolution,
    /// unless that is 0, in which case the input resolution is kept (downscale skipped).
    /// </returns>
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

        // SpriteSize 0 means "already at pixel-art resolution": skip the downscale and run only the
        // color/cleanup passes. Used by the standalone pixelart CLI on existing pixel images.
        if (opts.SpriteSize > 0)
        {
            current = Replace(current, DominantDownscaler.Downscale(
                current, opts.SpriteSize, opts.SpriteSize, opts.AlphaThreshold));
        }

        current = Replace(current, AlphaBinarizer.Binarize(
            current, opts.AlphaThreshold, opts.EdgeDilate));

        // Resolve the palette once so the dither pass and the quantizer map to the SAME colors:
        // dithering against one palette and then quantizing to a different one would blur the pattern.
        // With dithering off this stays the fixed palette (possibly null), so quantization keeps its
        // original Wu-internal behavior and nothing about the non-dithered path changes.
        SKColor[]? quantPalette = fixedPalette;
        if (opts.Dither != DitherMode.None)
        {
            quantPalette = DitherPass.ResolvePalette(current, opts.MaxColors, fixedPalette);
            current = Replace(current, DitherPass.Apply(current, opts.Dither, quantPalette));
        }

        current = Replace(current, PaletteQuantizer.Quantize(
            current, opts.MaxColors, quantPalette));

        if (opts.Cleanup.Jaggy)
        {
            current = Replace(current, ArtifactCleaner.JaggyClean(current));
        }

        // Outline runs last, on the finalized silhouette, so quantization/jaggy cannot erase it.
        if (opts.Outline)
        {
            current = Replace(current, OutlinePass.Apply(current, opts.OutlineColor, opts.OutlineType));
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
