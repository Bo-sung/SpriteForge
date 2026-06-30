using System.IO;
using FluentAssertions;
using SpriteForge.Core.Models;

namespace PixelSprite.Tests;

public sealed class RetargetMapLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public RetargetMapLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pixelsprite-retarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteMap(string json, string name = "retarget.json")
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_ParsesNameScaleAndBones()
    {
        string path = WriteMap(@"{
            // joint mapping
            ""name"": ""Mixamo -> Unreal"",
            ""scaleTranslations"": true,
            ""bones"": {
                ""mixamorig:Hips"": ""pelvis"",
                ""mixamorig:RightArm"": ""upperarm_r""
            }
        }");

        RetargetMap m = RetargetMapLoader.Load(path);
        m.Name.Should().Be("Mixamo -> Unreal");
        m.ScaleTranslations.Should().BeTrue();
        m.Bones.Should().HaveCount(2);
        m.Bones["mixamorig:Hips"].Should().Be("pelvis");
        m.Bones["mixamorig:RightArm"].Should().Be("upperarm_r");
    }

    [Fact]
    public void Load_OptionalFields_DefaultToEmptyAndFalse()
    {
        string path = WriteMap(@"{ ""bones"": { ""a"": ""b"" } }");
        RetargetMap m = RetargetMapLoader.Load(path);
        m.Name.Should().BeEmpty();
        m.ScaleTranslations.Should().BeFalse();
    }

    [Fact]
    public void Load_NoBones_Throws()
    {
        string path = WriteMap(@"{ ""name"": ""empty"" }");
        Action act = () => RetargetMapLoader.Load(path);
        act.Should().Throw<InvalidOperationException>().WithMessage("*bones*");
    }

    [Fact]
    public void Load_EmptyBoneMapping_Throws()
    {
        string path = WriteMap(@"{ ""bones"": { """": ""b"" } }");
        Action act = () => RetargetMapLoader.Load(path);
        act.Should().Throw<InvalidOperationException>().WithMessage("*empty bone mapping*");
    }

    [Fact]
    public void Load_EmptyValue_Throws()
    {
        string path = WriteMap(@"{ ""bones"": { ""mixamorig:Hips"": """" } }");
        Action act = () => RetargetMapLoader.Load(path);
        act.Should().Throw<InvalidOperationException>().WithMessage("*empty bone mapping*");
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Action act = () => RetargetMapLoader.Load(Path.Combine(_tempDir, "nope.json"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_AllowsTrailingCommasAndComments()
    {
        // JSON5-ish leniency: comments + trailing commas must not break parsing.
        string path = WriteMap(@"{
            ""bones"": {
                ""a"": ""b"", // a comment
            },
        }");
        RetargetMap m = RetargetMapLoader.Load(path);
        m.Bones["a"].Should().Be("b");
    }
}
