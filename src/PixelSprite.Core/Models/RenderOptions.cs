namespace PixelSprite.Core.Models;

/// <summary>
/// The up axis of the source model. FBX is commonly Y-up; some exporters use Z-up.
/// </summary>
public enum UpAxis
{
    /// <summary>Y is up (default for most FBX/GLB content).</summary>
    Y,

    /// <summary>Z is up (common for some DCC tools such as Blender exports).</summary>
    Z,
}

/// <summary>
/// Options that control the 3D rendering stage: which model and animation to load,
/// how many directions to render, the offscreen resolution, and camera framing.
/// </summary>
public sealed class RenderOptions
{
    /// <summary>Path to the input FBX or GLB file (skinned mesh + animation, or mesh only). Required.</summary>
    public required string Input { get; init; }

    /// <summary>
    /// Optional path to a separate animation-only FBX, retargeted onto <see cref="Input"/>
    /// by matching bone names. <see langword="null"/> uses the animation embedded in <see cref="Input"/>.
    /// </summary>
    public string? Anim { get; init; }

    /// <summary>Number of rendered directions: 2, 4, or 8. Default 8.</summary>
    public int Directions { get; init; } = 8;

    /// <summary>Offscreen render resolution in pixels (square). Default 256.</summary>
    public int RenderSize { get; init; } = 256;

    /// <summary>Animation frame sampling rate in frames per second. Default 12.</summary>
    public int Fps { get; init; } = 12;

    /// <summary>Force an exact frame count. 0 means sample the whole clip at <see cref="Fps"/>. Default 0.</summary>
    public int Frames { get; init; }

    /// <summary>Camera vertical angle in degrees (SC1-style isometric default). Default 26.5.</summary>
    public float CamPitch { get; init; } = 26.5f;

    /// <summary>Camera zoom factor; values above 1 zoom in. Default 1.0.</summary>
    public float CamZoom { get; init; } = 1.0f;

    /// <summary>Use an orthographic projection instead of perspective. Default false.</summary>
    public bool Ortho { get; init; }

    /// <summary>Up axis of the source model. Default <see cref="UpAxis.Y"/>.</summary>
    public UpAxis UpAxis { get; init; } = UpAxis.Y;

    /// <summary>
    /// Remove root motion: hold the root/hips bone's horizontal translation so the character stays
    /// centered (the vertical bob is kept). Default false.
    /// </summary>
    public bool InPlace { get; init; }
}
