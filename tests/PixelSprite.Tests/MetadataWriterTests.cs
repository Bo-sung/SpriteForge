using System.Text.Json;
using FluentAssertions;
using PixelSprite.Core.Models;
using PixelSprite.Core.Packing;
using Xunit;

namespace PixelSprite.Tests;

public sealed class MetadataWriterTests
{
    [Fact]
    public void Write_EmitsCamelCaseSchemaWithExpectedValues()
    {
        var metadata = new OutputMetadata
        {
            SpriteWidth = 48,
            SpriteHeight = 48,
            Directions = 8,
            Animations = new List<AnimationMetadata>
            {
                new() { Name = "Walk", FrameCount = 12, Fps = 12, SheetRow = 0 },
            },
            Pivot = new PivotMetadata { X = 0.5f, Y = 0.0f },
        };

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            MetadataWriter.Write(path, metadata);

            string json = File.ReadAllText(path);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // camelCase keys must exist.
            root.TryGetProperty("spriteWidth", out JsonElement spriteWidth).Should().BeTrue();
            root.TryGetProperty("spriteHeight", out _).Should().BeTrue();
            root.TryGetProperty("directions", out JsonElement directions).Should().BeTrue();
            root.TryGetProperty("animations", out JsonElement animations).Should().BeTrue();
            root.TryGetProperty("pivot", out JsonElement pivot).Should().BeTrue();

            spriteWidth.GetInt32().Should().Be(48);
            directions.GetInt32().Should().Be(8);

            animations.ValueKind.Should().Be(JsonValueKind.Array);
            animations.GetArrayLength().Should().Be(1);
            JsonElement firstAnim = animations[0];
            firstAnim.GetProperty("name").GetString().Should().Be("Walk");
            firstAnim.GetProperty("frameCount").GetInt32().Should().Be(12);
            firstAnim.GetProperty("fps").GetInt32().Should().Be(12);
            firstAnim.GetProperty("sheetRow").GetInt32().Should().Be(0);

            pivot.GetProperty("x").GetSingle().Should().Be(0.5f);
            pivot.GetProperty("y").GetSingle().Should().Be(0.0f);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
