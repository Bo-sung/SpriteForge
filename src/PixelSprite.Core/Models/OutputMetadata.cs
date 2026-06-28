namespace PixelSprite.Core.Models;

/// <summary>
/// Root metadata describing a packed sprite sheet. Serialized to <c>metadata.json</c> with a
/// camelCase naming policy so the on-disk JSON matches the Unity-facing schema exactly.
/// </summary>
public sealed class OutputMetadata
{
    /// <summary>Width of a single sprite cell in pixels.</summary>
    public int SpriteWidth { get; init; }

    /// <summary>Height of a single sprite cell in pixels.</summary>
    public int SpriteHeight { get; init; }

    /// <summary>Number of rendered directions (rows in the sheet).</summary>
    public int Directions { get; init; }

    /// <summary>Per-animation metadata, one entry per animation packed into the sheet.</summary>
    public IReadOnlyList<AnimationMetadata> Animations { get; init; } = new List<AnimationMetadata>();

    /// <summary>Normalized pivot point used by the importing engine.</summary>
    public PivotMetadata Pivot { get; init; } = new();
}

/// <summary>
/// Metadata for a single animation within a packed sprite sheet.
/// </summary>
public sealed class AnimationMetadata
{
    /// <summary>Animation name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Number of frames (columns) for this animation.</summary>
    public int FrameCount { get; init; }

    /// <summary>Playback rate this animation was sampled at.</summary>
    public int Fps { get; init; }

    /// <summary>Zero-based row index of this animation within the sheet.</summary>
    public int SheetRow { get; init; }
}

/// <summary>
/// Normalized sprite pivot. <c>(0.5, 0.5)</c> is the default: the root/hips bone is anchored to
/// the frame centre during rendering, so the body centre is the stable pivot point.
/// </summary>
public sealed class PivotMetadata
{
    /// <summary>Horizontal pivot, normalized 0..1.</summary>
    public float X { get; init; } = 0.5f;

    /// <summary>Vertical pivot, normalized 0..1.</summary>
    public float Y { get; init; } = 0.5f;
}
