using System.Numerics;
using Silk.NET.Assimp;

namespace SpriteForge.Core.Rendering;

/// <summary>
/// The result of analyzing an animation for root motion — i.e. horizontal travel of the root/hips bone.
/// </summary>
/// <param name="Node">Name of the node that carries the most horizontal travel (the root/hips), or empty.</param>
/// <param name="TravelXZ">Extent of that node's horizontal (XZ) movement across the clip, in model units.</param>
/// <param name="ReferenceXZ">The node's XZ translation at the first keyframe, used as the in-place anchor.</param>
internal readonly record struct RootMotionInfo(string Node, float TravelXZ, Vector2 ReferenceXZ)
{
    /// <summary>True when the root travels horizontally enough to be considered root motion (not just bob/jitter).</summary>
    public bool HasMotion => TravelXZ > 1.0f;
}

/// <summary>
/// Detects root motion in an Assimp animation by finding the channel whose node moves the most in the
/// horizontal plane (Mixamo puts root motion on <c>mixamorig:Hips</c>).
/// </summary>
internal static class RootMotion
{
    /// <summary>Analyzes <paramref name="anim"/> and returns its dominant horizontal-travel node.</summary>
    public static unsafe RootMotionInfo Analyze(Animation* anim)
    {
        if (anim is null)
        {
            return new RootMotionInfo(string.Empty, 0f, Vector2.Zero);
        }

        string bestNode = string.Empty;
        float bestRange = 0f;
        Vector2 bestRef = Vector2.Zero;

        for (uint c = 0; c < anim->MNumChannels; c++)
        {
            NodeAnim* channel = anim->MChannels[c];
            if (channel is null || channel->MNumPositionKeys == 0)
            {
                continue;
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (uint k = 0; k < channel->MNumPositionKeys; k++)
            {
                Vector3 p = channel->MPositionKeys[k].MValue;
                minX = MathF.Min(minX, p.X);
                maxX = MathF.Max(maxX, p.X);
                minZ = MathF.Min(minZ, p.Z);
                maxZ = MathF.Max(maxZ, p.Z);
            }

            float dx = maxX - minX, dz = maxZ - minZ;
            float range = MathF.Sqrt((dx * dx) + (dz * dz));
            if (range > bestRange)
            {
                bestRange = range;
                bestNode = channel->MNodeName.AsString;
                Vector3 first = channel->MPositionKeys[0].MValue;
                bestRef = new Vector2(first.X, first.Z);
            }
        }

        return new RootMotionInfo(bestNode, bestRange, bestRef);
    }
}
