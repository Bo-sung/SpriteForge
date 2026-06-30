using System.Text.Json;
using SpriteForge.Core.Models;

namespace SpriteForge.Core.Packing;

/// <summary>
/// Serializes <see cref="OutputMetadata"/> to a JSON file using the Unity-facing camelCase schema.
/// </summary>
public static class MetadataWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes <paramref name="metadata"/> to <paramref name="path"/> as indented camelCase JSON.
    /// Creates the parent directory if it does not already exist.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="metadata">The metadata to serialize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    public static void Write(string path, OutputMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metadata);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(metadata, SerializerOptions);
        File.WriteAllText(path, json);
    }
}
