using System.Numerics;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Builds the relative transform matrix for an equipment socket, equivalent to Unreal Engine's
/// <c>FTransform::ToMatrixWithScale</c> (Scale → Rotate → Translate) and Unity's
/// <c>Transform.localToWorldMatrix</c> for an SRT.
/// </summary>
/// <remarks>
/// <para>
/// This project uses the <c>System.Numerics</c> row-vector convention (a vertex <c>v</c> is
/// transformed as <c>v × M</c>), the same convention the renderer's CPU skinning uses
/// (<c>offset × global</c>). Compose matrices left-to-right in application order:
/// </para>
/// <code>
/// weaponVertexWorld = weaponVertexLocal × weaponNode × socketOffset × boneGlobal
/// </code>
/// </remarks>
public static class SocketTransform
{
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>
    /// Builds a relative SRT matrix from a socket offset. Rotation is given in <b>degrees</b> as
    /// Euler angles applied Y(X'Z'') (yaw, pitch, roll) via
    /// <see cref="Quaternion.CreateFromYawPitchRoll"/>.
    /// </summary>
    public static Matrix4x4 ToMatrix(Vector3 position, Vector3 rotationEulerDegrees, float scale)
    {
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
            rotationEulerDegrees.Y * DegToRad,
            rotationEulerDegrees.X * DegToRad,
            rotationEulerDegrees.Z * DegToRad);

        return ToMatrix(position, rotation, scale);
    }

    /// <summary>Builds a relative SRT matrix from a quaternion rotation.</summary>
    public static Matrix4x4 ToMatrix(Vector3 position, Quaternion rotation, float scale)
    {
        float safeScale = MathF.Max(scale, 1e-6f);
        return Matrix4x4.CreateScale(safeScale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(position);
    }

    /// <summary>Returns <paramref name="m"/> with its translation zeroed (for transforming normals).</summary>
    public static Matrix4x4 WithoutTranslation(Matrix4x4 m)
    {
        m.M41 = 0f;
        m.M42 = 0f;
        m.M43 = 0f;
        return m;
    }
}
