using System.Text.Json;

namespace SpriteForge.Core.Models;

/// <summary>
/// Loads a <see cref="RetargetMap"/> from a JSON file. Schema (camelCase, comments + trailing commas):
/// <code>
/// {
///   "name": "Mixamo -> Unreal Mannequin",
///   "scaleTranslations": false,
///   "bones": {
///     "mixamorig:Hips":         "pelvis",
///     "mixamorig:Spine":        "spine_01",
///     "mixamorig:RightArm":     "upperarm_r",
///     "mixamorig:RightHand":    "hand_r"
///   }
/// }
/// </code>
/// Both <c>name</c> and <c>scaleTranslations</c> are optional (defaults: empty name, false).
/// </summary>
public static class RetargetMapLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads and validates the retarget map at <paramref name="path"/>.</summary>
    /// <exception cref="FileNotFoundException">The map path does not exist.</exception>
    /// <exception cref="InvalidOperationException">The map has no <c>bones</c> object.</exception>
    public static RetargetMap Load(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"Retarget map not found: {path}", path);
        }

        string json = System.IO.File.ReadAllText(path);
        MapDto? dto = JsonSerializer.Deserialize<MapDto>(json, JsonOptions);
        if (dto?.Bones is null || dto.Bones.Count == 0)
        {
            throw new InvalidOperationException($"Retarget map '{path}' has no 'bones' entries.");
        }

        var bones = new Dictionary<string, string>(dto.Bones.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in dto.Bones)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new InvalidOperationException(
                    $"Retarget map '{path}' contains an empty bone mapping (key='{pair.Key}', value='{pair.Value}').");
            }

            bones[pair.Key] = pair.Value;
        }

        return new RetargetMap
        {
            Name = dto.Name ?? string.Empty,
            ScaleTranslations = dto.ScaleTranslations,
            Bones = bones,
        };
    }

    private sealed class MapDto
    {
        public string? Name { get; init; }
        public bool ScaleTranslations { get; init; }
        public Dictionary<string, string>? Bones { get; init; }
    }
}
