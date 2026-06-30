using System.Text.Json.Serialization;

namespace SpriteForge.Core.Models;

/// <summary>
/// A skeletal retargeting map (joint mapping) for playing an animation authored for one skeleton on a
/// character built with a different one — e.g. a Mixamo animation (<c>mixamorig:RightHand</c>) onto an
/// Unreal-style rig (<c>hand_r</c>). Implements the joint-mapping algorithm described in retargeting
/// literature: the source bone's <b>rotation is transferred as-is</b>, and (optionally) the
/// <b>translation is rescaled</b> by the target/source bone-length ratio.
/// </summary>
/// <remarks>
/// <para>
/// This is the CLI analogue of Unreal Engine's Retarget Manager / Unity's Humanoid Avatar bone mapping:
/// a table of <c>sourceBone -> targetBone</c> pairs. Bone names are matched through <see cref="Rendering.BoneMatcher"/>
/// (case/separator/namespace-insensitive), so a map can say <c>"hand_r"</c> and resolve whatever exact
/// name the target skeleton uses.
/// </para>
/// <para>
/// <b>Limitation (per the retargeting literature):</b> joint mapping carries rotations verbatim, so
/// differences in <i>rest pose</i> (T- vs A-pose) or proportions are NOT corrected. For pixel-art
/// sprites where the pose is sampled coarsely this is usually acceptable; for hero-quality results,
/// bake the retarget in a DCC tool (Unreal IK Retargeter, Blender retargeting add-on) instead.
/// </para>
/// </remarks>
public sealed class RetargetMap
{
    /// <summary>Human-readable label shown in progress output.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Source -> target bone-name pairs. Source names come from the animation file; target names must
    /// exist on the character skeleton. Unmapped source bones are skipped (they keep the animation's
    /// verbatim name, which will only bind if it happens to match a character bone).
    /// </summary>
    public IReadOnlyDictionary<string, string> Bones { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When true, each mapped bone's translation is rescaled by the target/source bone-length ratio
    /// (the second line of the classic joint-mapping pseudocode). Most humanoid animation channels
    /// carry rotation only (translation ~0), so this mainly affects the root/hips travel. Default
    /// false (rotation-only retarget, which is usually what looks right).
    /// </summary>
    public bool ScaleTranslations { get; init; }
}
