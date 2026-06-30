using SkiaSharp;

namespace SpriteForge.Core.Models;

/// <summary>How an outline is drawn relative to the sprite silhouette.</summary>
public enum OutlineType
{
    /// <summary>Paint the outline into the transparent pixels surrounding the silhouette (grows the shape by 1px).</summary>
    Outer,

    /// <summary>Recolor the silhouette's own boundary pixels (keeps the shape's size).</summary>
    Inner,
}

/// <summary>Dithering applied just before palette quantization.</summary>
public enum DitherMode
{
    /// <summary>No dithering.</summary>
    None,

    /// <summary>Ordered dithering with a 4×4 Bayer threshold matrix.</summary>
    Bayer,

    /// <summary>Floyd–Steinberg error-diffusion dithering.</summary>
    Floyd,
}

/// <summary>
/// Which artifact-cleanup passes to run. See <c>ArtifactCleaner</c>.
/// </summary>
public sealed class CleanupOptions
{
    /// <summary>Run the morphological open (erosion then dilation) to remove isolated noise. Default true.</summary>
    public bool Morph { get; init; } = true;

    /// <summary>Run the jaggy cleanup (replace pixels that differ from all 4 cardinal neighbors). Default true.</summary>
    public bool Jaggy { get; init; } = true;
}

/// <summary>
/// Options that control the pixel-art post-processing of each rendered high-resolution frame:
/// downscale resolution, palette size, alpha handling, cleanup passes, dithering, and outlining.
/// </summary>
public sealed class PixelArtOptions
{
    /// <summary>
    /// Final pixel-art resolution in pixels (square). Default 48. A value of 0 skips the downscale
    /// step entirely, processing the image at its input resolution (for already-pixel-art images).
    /// </summary>
    public int SpriteSize { get; init; } = 48;

    /// <summary>Maximum number of palette colors for Wu quantization. Default 32.</summary>
    public int MaxColors { get; init; } = 32;

    /// <summary>
    /// Optional path to a fixed-palette PNG. When set, Wu quantization is skipped and colors
    /// are mapped to the nearest palette entry. <see langword="null"/> runs Wu quantization.
    /// </summary>
    public string? PalettePath { get; init; }

    /// <summary>Alpha binarization / opacity cutoff in the range 0-255. Default 128.</summary>
    public byte AlphaThreshold { get; init; } = 128;

    /// <summary>
    /// Copy the RGB of the nearest opaque neighbor into transparent border pixels (alpha stays 0)
    /// to prevent dark fringing from premultiplied-alpha bleed. Default true.
    /// </summary>
    public bool EdgeDilate { get; init; } = true;

    /// <summary>Which artifact-cleanup passes to run.</summary>
    public CleanupOptions Cleanup { get; init; } = new();

    /// <summary>Draw an outline around the sprite silhouette as the final step. Default false.</summary>
    public bool Outline { get; init; }

    /// <summary>Outline color (alpha is forced opaque when the outline is drawn). Default black.</summary>
    public SKColor OutlineColor { get; init; } = SKColors.Black;

    /// <summary>Whether the outline is drawn outside or on the silhouette boundary. Default <see cref="OutlineType.Outer"/>.</summary>
    public OutlineType OutlineType { get; init; } = OutlineType.Outer;

    /// <summary>Dithering applied before quantization. Default <see cref="DitherMode.None"/>.</summary>
    public DitherMode Dither { get; init; } = DitherMode.None;
}
