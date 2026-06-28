using PixelSprite.Core.Models;
using Silk.NET.Assimp;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Pairs an attachment's resolved Assimp scene with its definition. The scene pointer is owned by the
/// caller (<see cref="RenderJob"/>) and only referenced here for the duration of rendering; this wrapper
/// is the unit the renderer's per-frame loop consumes.
/// </summary>
public sealed class AttachmentScene
{
    /// <summary>Creates an attachment scene wrapper.</summary>
    /// <param name="scene">The loaded Assimp scene (by value; the caller owns the original pointer).</param>
    /// <param name="definition">The attachment definition (socket / master-pose config).</param>
    public AttachmentScene(Scene scene, Attachment definition)
    {
        Scene = scene;
        Definition = definition;
    }

    /// <summary>The attachment's loaded Assimp scene (geometry only; no animation).</summary>
    public Scene Scene { get; }

    /// <summary>The attachment definition: socket bone, offset, or master-pose flag.</summary>
    public Attachment Definition { get; }
}
