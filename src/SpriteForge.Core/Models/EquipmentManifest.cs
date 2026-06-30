namespace SpriteForge.Core.Models;

/// <summary>
/// A collection of <see cref="Attachment"/> entries loaded from an equipment manifest JSON file.
/// Passed to <c>RenderJob.Execute</c> to equip weapons/armor onto the character before rendering.
/// </summary>
public sealed class EquipmentManifest
{
    /// <summary>The attachments to apply, in declaration order.</summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = Array.Empty<Attachment>();
}
