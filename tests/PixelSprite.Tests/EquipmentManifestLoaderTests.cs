using System.Numerics;
using FluentAssertions;
using PixelSprite.Core.Models;

namespace PixelSprite.Tests;

public sealed class EquipmentManifestLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public EquipmentManifestLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pixelsprite-equip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteManifest(string json, string fileName = "equipment.json")
    {
        string path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Creates a placeholder attachment file so the loader's existence check passes.</summary>
    private void WriteAttachment(string name)
        => File.WriteAllText(Path.Combine(_tempDir, name), "placeholder");

    [Fact]
    public void Load_SocketAttachment_ParsesAllFields()
    {
        WriteAttachment("sword.glb");
        string path = WriteManifest(@"{
            // socket-mode weapon
            ""attachments"": [
                {
                    ""name"": ""sword"",
                    ""file"": ""sword.glb"",
                    ""socketBone"": ""Hand_R"",
                    ""offset"": { ""position"": [0, 0, 0.05], ""rotation"": [0, 90, 0], ""scale"": 1.5 }
                }
            ]
        }");

        EquipmentManifest m = EquipmentManifestLoader.Load(path);

        m.Attachments.Should().HaveCount(1);
        Attachment att = m.Attachments[0];
        att.Name.Should().Be("sword");
        att.File.Should().Be("sword.glb");
        att.SocketBone.Should().Be("Hand_R");
        att.UseMasterPose.Should().BeFalse();
        att.OffsetPosition.Should().Be(new Vector3(0f, 0f, 0.05f));
        att.OffsetRotation.Should().Be(new Vector3(0f, 90f, 0f));
        att.OffsetScale.Should().Be(1.5f);
        att.ResolvedFile.Should().Be(Path.Combine(_tempDir, "sword.glb"));
    }

    [Fact]
    public void Load_MasterPoseAttachment_ParsesFlagAndDerivesName()
    {
        WriteAttachment("helmet.glb");
        string path = WriteManifest(@"{
            ""attachments"": [
                { ""file"": ""helmet.glb"", ""useMasterPose"": true }
            ]
        }");

        EquipmentManifest m = EquipmentManifestLoader.Load(path);
        Attachment att = m.Attachments[0];

        att.UseMasterPose.Should().BeTrue();
        att.SocketBone.Should().BeNull();
        att.Name.Should().Be("helmet"); // derived from file name when omitted
    }

    [Fact]
    public void Load_MissingOffset_DefaultsToZeroTranslationRotationAndUnitScale()
    {
        WriteAttachment("shield.glb");
        string path = WriteManifest(@"{
            ""attachments"": [
                { ""file"": ""shield.glb"", ""socketBone"": ""Forearm_L"" }
            ]
        }");

        EquipmentManifest m = EquipmentManifestLoader.Load(path);
        Attachment att = m.Attachments[0];
        att.OffsetPosition.Should().Be(Vector3.Zero);
        att.OffsetRotation.Should().Be(Vector3.Zero);
        att.OffsetScale.Should().Be(1f);
    }

    [Fact]
    public void Load_PartialOffsetArray_ToleratesMissingElements()
    {
        WriteAttachment("dagger.glb");
        string path = WriteManifest(@"{
            ""attachments"": [
                { ""file"": ""dagger.glb"", ""socketBone"": ""Hand_R"",
                  ""offset"": { ""position"": [3] } }
            ]
        }");

        EquipmentManifest m = EquipmentManifestLoader.Load(path);
        Attachment att = m.Attachments[0];
        att.OffsetPosition.Should().Be(new Vector3(3f, 0f, 0f));
    }

    [Fact]
    public void Load_RelativePath_ResolvesAgainstManifestDirectory()
    {
        string sub = Path.Combine(_tempDir, "gear");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "ring.glb"), "placeholder");

        string path = WriteManifest(@"{
            ""attachments"": [
                { ""file"": ""gear/ring.glb"", ""socketBone"": ""Finger_R"" }
            ]
        }");

        EquipmentManifest m = EquipmentManifestLoader.Load(path);
        m.Attachments[0].ResolvedFile.Should().Be(Path.Combine(_tempDir, "gear", "ring.glb"));
    }

    [Fact]
    public void Load_MissingAttachmentFile_Throws()
    {
        string path = WriteManifest(@"{
            ""attachments"": [ { ""file"": ""nope.glb"", ""socketBone"": ""Hand_R"" } ]
        }");

        Action act = () => EquipmentManifestLoader.Load(path);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_SocketModeWithoutBone_Throws()
    {
        WriteAttachment("sword.glb");
        string path = WriteManifest(@"{
            ""attachments"": [ { ""file"": ""sword.glb"" } ]
        }");

        Action act = () => EquipmentManifestLoader.Load(path);
        act.Should().Throw<InvalidOperationException>().WithMessage("*socketBone*");
    }

    [Fact]
    public void Load_MissingManifestFile_Throws()
    {
        Action act = () => EquipmentManifestLoader.Load(Path.Combine(_tempDir, "missing.json"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_MultipleAttachments_PreservesOrder()
    {
        WriteAttachment("sword.glb");
        WriteAttachment("shield.glb");
        WriteAttachment("helmet.glb");
        string path = WriteManifest(@"{
            ""attachments"": [
                { ""file"": ""sword.glb"", ""socketBone"": ""Hand_R"" },
                { ""file"": ""shield.glb"", ""socketBone"": ""Forearm_L"" },
                { ""file"": ""helmet.glb"", ""useMasterPose"": true }
            ]
        }");

        EquipmentManifest m = EquipmentManifestLoader.Load(path);
        m.Attachments.Select(a => a.Name).Should().Equal("sword", "shield", "helmet");
    }
}
