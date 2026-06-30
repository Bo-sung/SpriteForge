using SkiaSharp;

namespace SpriteForge.Core.Models;

/// <summary>
/// One fully processed pixel-art frame: the final bitmap together with its position
/// in the (direction, frame) grid and the owning animation name.
/// </summary>
public sealed class SpriteFrame : IDisposable
{
    /// <summary>The processed pixel-art bitmap (RGBA8888, transparent background preserved).</summary>
    public required SKBitmap Bitmap { get; init; }

    /// <summary>Zero-based direction index (row in the sprite sheet).</summary>
    public required int DirectionIndex { get; init; }

    /// <summary>Zero-based frame index within the animation (column in the sprite sheet).</summary>
    public required int FrameIndex { get; init; }

    /// <summary>Name of the animation this frame belongs to.</summary>
    public required string AnimName { get; init; }

    /// <summary>Disposes the underlying bitmap.</summary>
    public void Dispose() => Bitmap.Dispose();
}
