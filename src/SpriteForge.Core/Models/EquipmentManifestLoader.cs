using System.Numerics;
using System.Text.Json;

namespace SpriteForge.Core.Models;

/// <summary>
/// Loads an <see cref="EquipmentManifest"/> from a JSON file using the Unreal/Unity-style schema.
/// </summary>
/// <remarks>
/// Schema (camelCase, comments + trailing commas allowed):
/// <code>
/// {
///   "attachments": [
///     {
///       "name": "sword",
///       "file": "./assets/sword.glb",
///       "socketBone": "Hand_R",
///       "offset": { "position": [0,0,0.05], "rotation": [0,90,0], "scale": 1.0 }
///     },
///     {
///       "name": "helmet",
///       "file": "./assets/helmet.glb",
///       "useMasterPose": true
///     }
///   ]
/// }
/// </code>
/// The <c>offset</c> object is optional; each field defaults to no translation / no rotation / scale 1,
/// and <c>position</c>/<c>rotation</c> accept 2- or 3-element arrays (missing elements treated as 0).
/// <c>file</c> paths are resolved relative to the manifest's directory (so the manifest and its assets
/// travel together) and verified to exist at load time.
/// </remarks>
public static class EquipmentManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads and validates the manifest at <paramref name="path"/>.</summary>
    /// <exception cref="FileNotFoundException">The manifest path, or a referenced attachment file, does not exist.</exception>
    /// <exception cref="InvalidOperationException">An attachment is misconfigured (e.g. socket mode without a bone).</exception>
    public static EquipmentManifest Load(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"Equipment manifest not found: {path}", path);
        }

        string json = System.IO.File.ReadAllText(path);
        ManifestDto? dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        if (dto?.Attachments is null)
        {
            throw new InvalidOperationException($"Equipment manifest '{path}' has no 'attachments' array.");
        }

        string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        var resolved = new List<Attachment>(dto.Attachments.Count);
        foreach (AttachmentDto a in dto.Attachments)
        {
            resolved.Add(ResolveAttachment(a, baseDir));
        }

        return new EquipmentManifest { Attachments = resolved };
    }

    private static Attachment ResolveAttachment(AttachmentDto dto, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(dto.File))
        {
            throw new InvalidOperationException("An attachment is missing its 'file' path.");
        }

        bool useMasterPose = dto.UseMasterPose;
        if (!useMasterPose && string.IsNullOrWhiteSpace(dto.SocketBone))
        {
            throw new InvalidOperationException(
                $"Attachment '{dto.File}' is in socket mode but has no 'socketBone'. " +
                "Set 'socketBone' or enable 'useMasterPose'.");
        }

        string resolvedFile = System.IO.Path.IsPathRooted(dto.File)
            ? dto.File
            : System.IO.Path.Combine(baseDir, dto.File);
        resolvedFile = System.IO.Path.GetFullPath(resolvedFile);

        // Verify early so the render loop never starts with a dangling attachment.
        if (!System.IO.File.Exists(resolvedFile))
        {
            throw new FileNotFoundException(
                $"Attachment '{(string.IsNullOrEmpty(dto.Name) ? dto.File : dto.Name)}' file not found: {resolvedFile}",
                resolvedFile);
        }

        string name = string.IsNullOrWhiteSpace(dto.Name)
            ? System.IO.Path.GetFileNameWithoutExtension(dto.File)
            : dto.Name!;

        OffsetDto off = dto.Offset ?? new OffsetDto();

        return new Attachment
        {
            Name = name,
            File = dto.File!,
            ResolvedFile = resolvedFile,
            SocketBone = dto.SocketBone,
            OffsetPosition = ToVector3(off.Position),
            OffsetRotation = ToVector3(off.Rotation),
            OffsetScale = off.Scale ?? 1f,
            UseMasterPose = useMasterPose,
        };
    }

    /// <summary>Reads up to 3 floats from an array (missing/short → 0), tolerating null.</summary>
    private static Vector3 ToVector3(float[]? arr)
    {
        if (arr is null || arr.Length == 0)
        {
            return Vector3.Zero;
        }

        float x = arr.Length > 0 ? arr[0] : 0f;
        float y = arr.Length > 1 ? arr[1] : 0f;
        float z = arr.Length > 2 ? arr[2] : 0f;
        return new Vector3(x, y, z);
    }

    // ---- DTOs (file-local; the on-disk shape is intentionally lenient) ----

    private sealed class ManifestDto
    {
        public List<AttachmentDto> Attachments { get; init; } = new();
    }

    private sealed class AttachmentDto
    {
        public string? Name { get; init; }
        public string? File { get; init; }
        public string? SocketBone { get; init; }
        public OffsetDto? Offset { get; init; }
        public bool UseMasterPose { get; init; }
    }

    private sealed class OffsetDto
    {
        public float[]? Position { get; init; }
        public float[]? Rotation { get; init; }
        public float? Scale { get; init; }
    }
}
