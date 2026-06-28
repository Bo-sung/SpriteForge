using System.Numerics;
using FluentAssertions;
using PixelSprite.Core.Rendering;

namespace PixelSprite.Tests;

public sealed class SocketTransformTests
{
    private const float Eps = 1e-5f;

    [Fact]
    public void Identity_WhenNoOffset_ProducesIdentityMatrix()
    {
        Matrix4x4 m = SocketTransform.ToMatrix(Vector3.Zero, Vector3.Zero, 1f);
        m.Should().Be(Matrix4x4.Identity);
    }

    [Fact]
    public void Translation_TranslatesPointByOffset()
    {
        Matrix4x4 m = SocketTransform.ToMatrix(new Vector3(5f, 0f, 0f), Vector3.Zero, 1f);
        Vector3 p = Vector3.Transform(Vector3.Zero, m);
        p.Should().Be(new Vector3(5f, 0f, 0f));
    }

    [Fact]
    public void Scale_ScalesPointUniformly()
    {
        Matrix4x4 m = SocketTransform.ToMatrix(Vector3.Zero, Vector3.Zero, 2f);
        Vector3 p = Vector3.Transform(new Vector3(1f, 0f, 0f), m);
        p.Should().Be(new Vector3(2f, 0f, 0f));
    }

    [Fact]
    public void Yaw90_RotatesXAxisPointToNegativeZ()
    {
        // +Y rotation by 90° maps +X to -Z in System.Numerics' right-handed, row-vector form
        // (CreateFromYawPitchRoll with yaw=90° == CreateRotationY(90°)).
        Matrix4x4 m = SocketTransform.ToMatrix(Vector3.Zero, new Vector3(0f, 90f, 0f), 1f);
        Vector3 p = Vector3.Transform(new Vector3(1f, 0f, 0f), m);
        p.X.Should().BeApproximately(0f, Eps);
        p.Y.Should().BeApproximately(0f, Eps);
        p.Z.Should().BeApproximately(-1f, Eps);
    }

    [Fact]
    public void SrtOrder_AppliesScaleBeforeTranslation()
    {
        // S × R × T (row-vector application): a point is scaled first, then translated.
        // Point (1,0,0) with scale 2 and translation (5,0,0) -> (1*2)+5 = 7 along X.
        // (If order were T × S, the result would be (1+5)*2 = 12 — this guards against that bug.)
        Matrix4x4 m = SocketTransform.ToMatrix(new Vector3(5f, 0f, 0f), Vector3.Zero, 2f);
        Vector3 p = Vector3.Transform(new Vector3(1f, 0f, 0f), m);
        p.Should().Be(new Vector3(7f, 0f, 0f));
    }

    [Fact]
    public void SocketWorldMatrix_CombinesOffsetAndBoneGlobal()
    {
        // Unreal's AttachToComponent formula, row-vector: weaponWorld = offset × boneGlobal.
        // Bone at (10,0,0) with identity rotation; socket offset translation (1,0,0) in bone space.
        // The weapon's local origin therefore lands at (10+1, 0, 0).
        Matrix4x4 boneGlobal = Matrix4x4.CreateTranslation(10f, 0f, 0f);
        Matrix4x4 offset = SocketTransform.ToMatrix(new Vector3(1f, 0f, 0f), Vector3.Zero, 1f);
        Matrix4x4 socketWorld = offset * boneGlobal;

        Vector3 weaponLocalOrigin = Vector3.Transform(Vector3.Zero, socketWorld);
        weaponLocalOrigin.Should().Be(new Vector3(11f, 0f, 0f));
    }

    [Fact]
    public void SocketWorldMatrix_AppliesOffsetInRotatedBoneSpace()
    {
        // If the bone is rotated 90° about Y, the socket's local +X offset points along the bone's
        // rotated +X (= world -Z). Offset (1,0,0) on a bone at the origin, rotated Y90°,
        // should place the weapon origin at (0,0,-1).
        Matrix4x4 boneGlobal = Matrix4x4.CreateRotationY(MathF.PI / 2f);
        Matrix4x4 offset = SocketTransform.ToMatrix(new Vector3(1f, 0f, 0f), Vector3.Zero, 1f);
        Matrix4x4 socketWorld = offset * boneGlobal;

        Vector3 p = Vector3.Transform(Vector3.Zero, socketWorld);
        p.X.Should().BeApproximately(0f, Eps);
        p.Z.Should().BeApproximately(-1f, Eps);
    }

    [Fact]
    public void WithoutTranslation_ZeroesTranslationRowAndKeepsScale()
    {
        Matrix4x4 m = SocketTransform.ToMatrix(new Vector3(5f, 6f, 7f), Vector3.Zero, 2f);
        Matrix4x4 n = SocketTransform.WithoutTranslation(m);

        n.Translation.Should().Be(Vector3.Zero);
        n.M11.Should().Be(2f); // scale preserved
    }
}
