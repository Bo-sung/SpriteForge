namespace PixelSprite.Core.Models;

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
/// downscale resolution, palette size, alpha handling, and cleanup passes.
/// </summary>
public sealed class PixelArtOptions
{
    /// <summary>Final pixel-art resolution in pixels (square). Default 48.</summary>
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
}
