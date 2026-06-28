using FluentAssertions;
using PixelSprite.Core.Rendering;

namespace PixelSprite.Tests;

public sealed class BoneMatcherTests
{
    // A representative Mixamo bone set (namespacing prefix "mixamorig:").
    private static readonly string[] Mixamo =
    {
        "mixamorig:Hips",
        "mixamorig:Spine",
        "mixamorig:Spine1",
        "mixamorig:Spine2",
        "mixamorig:Neck",
        "mixamorig:Head",
        "mixamorig:LeftArm",
        "mixamorig:LeftForeArm",
        "mixamorig:LeftHand",
        "mixamorig:RightArm",
        "mixamorig:RightForeArm",
        "mixamorig:RightHand",
        "mixamorig:RightHandThumb1",
    };

    [Fact]
    public void Resolve_ExactMatch_ReturnsVerbatim()
    {
        // The common path: the manifest already has the exact name. No normalization needed.
        BoneMatcher.Resolve(Mixamo, "mixamorig:RightHand").Should().Be("mixamorig:RightHand");
    }

    [Fact]
    public void Resolve_StripsNamespacePrefix()
    {
        // "RightHand" should bind to "mixamorig:RightHand" — the manifest doesn't need the prefix.
        BoneMatcher.Resolve(Mixamo, "RightHand").Should().Be("mixamorig:RightHand");
    }

    [Fact]
    public void Resolve_IgnoresCasing()
    {
        BoneMatcher.Resolve(Mixamo, "righthand").Should().Be("mixamorig:RightHand");
        BoneMatcher.Resolve(Mixamo, "RIGHTHAND").Should().Be("mixamorig:RightHand");
        BoneMatcher.Resolve(Mixamo, "rightHand").Should().Be("mixamorig:RightHand");
    }

    [Fact]
    public void Resolve_IgnoresWordSeparators()
    {
        BoneMatcher.Resolve(Mixamo, "right_hand").Should().Be("mixamorig:RightHand");
        BoneMatcher.Resolve(Mixamo, "right-hand").Should().Be("mixamorig:RightHand");
        BoneMatcher.Resolve(Mixamo, "right.hand").Should().Be("mixamorig:RightHand");
    }

    [Fact]
    public void Resolve_NullOrEmpty_ReturnsNull()
    {
        BoneMatcher.Resolve(Mixamo, null).Should().BeNull();
        BoneMatcher.Resolve(Mixamo, "").Should().BeNull();
        BoneMatcher.Resolve(Mixamo, "   ").Should().BeNull();
    }

    [Fact]
    public void Resolve_EmptyBoneSet_ReturnsNull()
    {
        BoneMatcher.Resolve(Array.Empty<string>(), "RightHand").Should().BeNull();
    }

    [Fact]
    public void Resolve_AmbiguousSuffix_ReturnsNull_WhenMultipleBonesEndInNeedle()
    {
        // "hand" is a suffix of both "RightHand" and "LeftHand" -> ambiguous, refuse to guess.
        BoneMatcher.Resolve(Mixamo, "hand").Should().BeNull();
    }

    [Fact]
    public void Resolve_UniqueSuffix_MatchesWhenOnlyOneBoneEndsInNeedle()
    {
        // "forearm" matches both LeftForeArm and RightForeArm only AFTER normalization: "leftforearm"
        // and "rightforearm". So plain "forearm" is a suffix of both -> ambiguous -> null. But
        // "rightforearm" uniquely matches.
        BoneMatcher.Resolve(Mixamo, "rightforearm").Should().Be("mixamorig:RightForeArm");
    }

    [Fact]
    public void Resolve_NonExistent_ReturnsNull()
    {
        BoneMatcher.Resolve(Mixamo, "Tail").Should().BeNull();
    }

    [Fact]
    public void Normalize_StripsNonAlphanumericAndLowercases()
    {
        // Normalize is a pure canonicalization: lowercase + drop separators. It does NOT strip the
        // namespace prefix (that happens in Resolve via suffix matching). So "mixamorig:RightHand" ->
        // "mixamorigrighthand" (prefix kept, separators gone).
        BoneMatcher.Normalize("mixamorig:RightHand").Should().Be("mixamorigrighthand");
        BoneMatcher.Normalize("Armature|Hand_R").Should().Be("armaturehandr");
        BoneMatcher.Normalize("RIGHT-HAND").Should().Be("righthand");
        BoneMatcher.Normalize("").Should().BeEmpty();
    }

    [Fact]
    public void Suggest_ReturnsContainmentMatches()
    {
        IReadOnlyList<string> s = BoneMatcher.Suggest(Mixamo, "hand");
        s.Should().Contain("mixamorig:LeftHand");
        s.Should().Contain("mixamorig:RightHand");
        s.Should().Contain("mixamorig:RightHandThumb1");
    }

    [Fact]
    public void Suggest_RespectsLimit()
    {
        IReadOnlyList<string> s = BoneMatcher.Suggest(Mixamo, "hand", limit: 2);
        s.Should().HaveCount(2);
    }

    [Fact]
    public void Suggest_EmptyNeedle_ReturnsEmpty()
    {
        BoneMatcher.Suggest(Mixamo, null).Should().BeEmpty();
        BoneMatcher.Suggest(Mixamo, "").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_PathSeparator_Ignored()
    {
        // "Armature|RightHand" style names: the manifest can omit the path segment.
        var bones = new[] { "Armature|Hand_R", "Armature|Hand_L" };
        BoneMatcher.Resolve(bones, "Hand_R").Should().Be("Armature|Hand_R");
    }
}
