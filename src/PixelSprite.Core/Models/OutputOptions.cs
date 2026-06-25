namespace PixelSprite.Core.Models;

/// <summary>
/// How the rendered frames are written to disk.
/// </summary>
public enum OutputMode
{
    /// <summary>Assemble a single sprite sheet PNG plus a metadata JSON file.</summary>
    Sheet,

    /// <summary>Write each frame as an individual PNG file.</summary>
    Frames,

    /// <summary>Write both the sprite sheet and the individual frames.</summary>
    Both,
}

/// <summary>
/// Options that control how and where output is written.
/// </summary>
public sealed class OutputOptions
{
    /// <summary>Output mode: sheet, frames, or both. Default <see cref="OutputMode.Sheet"/>.</summary>
    public OutputMode Mode { get; init; } = OutputMode.Sheet;

    /// <summary>Output directory. Default <c>./output</c>.</summary>
    public string OutDir { get; init; } = "./output";

    /// <summary>Print per-frame progress to the console. Default false.</summary>
    public bool Verbose { get; init; }
}
