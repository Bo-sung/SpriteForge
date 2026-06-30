using System.Numerics;
using System.Text.Json.Serialization;

namespace SpriteForge.Core.Models;

/// <summary>
/// A single equippable attachment, modelled on Unreal Engine's <c>USkeletalMeshSocket</c> +
/// <c>AttachToComponent</c> and Unity's parent-constraint / child-socket patterns.
/// </summary>
/// <remarks>
/// <para>
/// Two attachment modes are supported, mirroring how the major engines split equipment:
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Socket attachment</term> (<c>UseMasterPose = false</c>, the default): for items held in the
/// hand — swords, staves, shields, guns. The attachment's mesh is treated as a rigid static mesh and
/// placed at <c>offset × boneGlobal</c>, exactly Unreal's socket formula. The mesh follows the bone
/// every frame with no skinning.
/// </item>
/// <item>
/// <term>Master-pose attachment</term> (<c>UseMasterPose = true</c>): for body-fitting gear — armour,
/// gloves, helmets. The attachment file must contain a skinned mesh that shares the character's bone
/// names; it is skinned with the <em>character's</em> per-frame bone globals (the attachment's own
/// offset matrices are used), which is Unreal's <c>MasterPoseComponent</c> behaviour.
/// </item>
/// </list>
/// <para>
/// <c>OffsetRotation</c> is in <b>degrees</b>, as Euler angles applied Y(X'Z'') (yaw-pitch-roll),
/// matching the engine convention of editing a socket's relative rotation in degrees.
/// </para>
/// </remarks>
public sealed record Attachment
{
    /// <summary>Human-readable label used in progress output. Defaults to the file's base name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Path to the attachment's FBX/GLB file, as written in the manifest (may be relative).</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>
    /// The character bone the attachment binds to (socket mode). Required unless
    /// <see cref="UseMasterPose"/> is true.
    /// </summary>
    public string? SocketBone { get; init; }

    /// <summary>Socket-relative translation in model units (the engine's Relative Location).</summary>
    public Vector3 OffsetPosition { get; init; }

    /// <summary>Socket-relative Euler rotation in <b>degrees</b> (the engine's Relative Rotation).</summary>
    public Vector3 OffsetRotation { get; init; }

    /// <summary>Socket-relative uniform scale (default 1).</summary>
    public float OffsetScale { get; init; } = 1f;

    /// <summary>
    /// Master-pose mode: skin the attachment with the character's bone poses instead of socketing it.
    /// </summary>
    public bool UseMasterPose { get; init; }

    /// <summary>
    /// The fully-resolved, absolute file path (manifest-relative). Populated by
    /// <see cref="EquipmentManifestLoader"/>; not serialized.
    /// </summary>
    [JsonIgnore]
    public string ResolvedFile { get; init; } = string.Empty;
}
