using System.Numerics;
using PixelSprite.Core.Models;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Pure, allocation-light logic for retargeting a single sampled animation channel through a joint
/// mapping: resolve the target bone name, and (optionally) rescale the channel's translation by a
/// precomputed bone-length ratio. This is the per-frame heart of the joint-mapping algorithm; it has
/// no scene/Assimp dependency so it can be unit-tested directly.
/// </summary>
/// <remarks>
/// Implements, per channel:
/// <code>
/// tgtBone = map.Resolve(srcBone) ?? srcBone     // rotation transferred verbatim
/// tgtPos  = srcPos * lengthRatio                 // when scaleTranslations is on
/// </code>
/// </remarks>
public static class RetargetMapper
{
    /// <summary>
    /// Resolves the target bone name for <paramref name="sourceBone"/>: the map's value if present
    /// (exact, then fuzzy via <see cref="BoneMatcher"/> against the target skeleton), else the source
    /// name unchanged (so verbatim-name animations still bind without a map).
    /// </summary>
    /// <param name="map">The retarget map (may be null for no-remap).</param>
    /// <param name="sourceBone">The source animation channel's bone name.</param>
    /// <param name="targetBoneNames">The character's exact bone names (for fuzzy resolution of map values).</param>
    /// <returns>The resolved target bone name.</returns>
    public static string ResolveTargetName(RetargetMap? map, string sourceBone, IEnumerable<string> targetBoneNames)
    {
        if (map is null || string.IsNullOrEmpty(sourceBone))
        {
            return sourceBone;
        }

        if (!map.Bones.TryGetValue(sourceBone, out string? mapped))
        {
            return sourceBone;
        }

        // The map value may itself be loose (e.g. "hand_r" vs "mixamorig:..."); resolve it against the
        // target skeleton so the channel binds to the actual bone.
        string? resolved = BoneMatcher.Resolve(targetBoneNames, mapped);
        return resolved ?? mapped;
    }

    /// <summary>
    /// Rescales a sampled position by the bone-length ratio for <paramref name="sourceBone"/>, when
    /// retarget translation scaling is enabled. Returns the position unchanged otherwise.
    /// </summary>
    /// <param name="map">The retarget map (its <see cref="RetargetMap.ScaleTranslations"/> gates this).</param>
    /// <param name="position">The source channel's sampled translation.</param>
    /// <param name="sourceBone">The source bone name (key into <paramref name="ratios"/>).</param>
    /// <param name="ratios">Precomputed source-bone -> ratio table (target/source length). Null = no ratios.</param>
    /// <returns>The (possibly rescaled) position.</returns>
    public static Vector3 ScaleTranslation(RetargetMap? map, Vector3 position, string sourceBone, IReadOnlyDictionary<string, float>? ratios)
    {
        if (map is null || !map.ScaleTranslations || ratios is null)
        {
            return position;
        }

        if (!ratios.TryGetValue(sourceBone, out float ratio) || MathF.Abs(ratio - 1f) < 1e-5f)
        {
            return position;
        }

        return position * ratio;
    }
}
