using System.Numerics;
using FluentAssertions;
using PixelSprite.Core.Models;
using PixelSprite.Core.Rendering;

namespace PixelSprite.Tests;

public sealed class RetargetMapperTests
{
    private static readonly string[] TargetBones =
    {
        "pelvis", "spine_01", "upperarm_r", "lowerarm_r", "hand_r",
        "upperarm_l", "lowerarm_l", "hand_l",
    };

    [Fact]
    public void ResolveTargetName_NoMap_ReturnsSourceVerbatim()
    {
        // Without a map the animation binds by verbatim name (the legacy behaviour).
        RetargetMapper.ResolveTargetName(map: null, "RightHand", TargetBones).Should().Be("RightHand");
    }

    [Fact]
    public void ResolveTargetName_UnmappedSource_ReturnsSourceVerbatim()
    {
        // A source bone with no map entry is left alone (it may still bind if names happen to match).
        var map = new RetargetMap { Bones = new Dictionary<string, string> { ["Hips"] = "pelvis" } };
        RetargetMapper.ResolveTargetName(map, "RightHand", TargetBones).Should().Be("RightHand");
    }

    [Fact]
    public void ResolveTargetName_MappedSourceExactValue_ReturnsValue()
    {
        var map = new RetargetMap { Bones = new Dictionary<string, string> { ["mixamorig:RightArm"] = "upperarm_r" } };
        RetargetMapper.ResolveTargetName(map, "mixamorig:RightArm", TargetBones).Should().Be("upperarm_r");
    }

    [Fact]
    public void ResolveTargetName_MappedValueFuzzyResolvedAgainstTarget()
    {
        // The map value "upperarm_r" should bind to the actual target bone "upperarm_r"; and a loose
        // map value should resolve through BoneMatcher so "handr" -> "hand_r".
        var map = new RetargetMap { Bones = new Dictionary<string, string> { ["mixamorig:RightHand"] = "handr" } };
        RetargetMapper.ResolveTargetName(map, "mixamorig:RightHand", TargetBones).Should().Be("hand_r");
    }

    [Fact]
    public void ScaleTranslation_NoMap_ReturnsPositionUnchanged()
    {
        var pos = new Vector3(1, 2, 3);
        RetargetMapper.ScaleTranslation(map: null, pos, "Hips", ratios: null).Should().Be(pos);
    }

    [Fact]
    public void ScaleTranslation_ScaleDisabled_ReturnsPositionUnchanged()
    {
        var map = new RetargetMap { ScaleTranslations = false };
        var ratios = new Dictionary<string, float> { ["Hips"] = 2f };
        var pos = new Vector3(1, 2, 3);
        RetargetMapper.ScaleTranslation(map, pos, "Hips", ratios).Should().Be(pos);
    }

    [Fact]
    public void ScaleTranslation_NoRatioForBone_ReturnsPositionUnchanged()
    {
        var map = new RetargetMap { ScaleTranslations = true };
        var ratios = new Dictionary<string, float> { ["Other"] = 2f };
        var pos = new Vector3(1, 2, 3);
        RetargetMapper.ScaleTranslation(map, pos, "Hips", ratios).Should().Be(pos);
    }

    [Fact]
    public void ScaleTranslation_RatioApplied_ScalesPosition()
    {
        var map = new RetargetMap { ScaleTranslations = true };
        var ratios = new Dictionary<string, float> { ["Hips"] = 0.5f };
        var pos = new Vector3(2, 4, 6);
        RetargetMapper.ScaleTranslation(map, pos, "Hips", ratios).Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void ScaleTranslation_RatioOfOne_ReturnsPositionUnchanged()
    {
        // Ratios essentially equal to 1 are skipped to avoid needless recomputation.
        var map = new RetargetMap { ScaleTranslations = true };
        var ratios = new Dictionary<string, float> { ["Hips"] = 1.0000001f };
        var pos = new Vector3(1, 2, 3);
        RetargetMapper.ScaleTranslation(map, pos, "Hips", ratios).Should().Be(pos);
    }
}
