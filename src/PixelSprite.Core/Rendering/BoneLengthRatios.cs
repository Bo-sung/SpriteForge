using System.Numerics;
using PixelSprite.Core.Models;
using Silk.NET.Assimp;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Computes the per-bone length ratios a joint-mapping retarget needs: for each source bone that is
/// mapped to a target bone, the ratio <c>targetLength / sourceLength</c> used to rescale the animation's
/// translation. A bone's "length" is the distance to its first child bone (the standard skeleton
/// convention); leaf bones (no children) have length 0 and are left at ratio 1.
/// </summary>
/// <remarks>
/// Mirrors the second line of the classic joint-mapping pseudocode:
/// <c>tgt.position = src.position * (tgt.length / src.length)</c>. Ratios are computed once (in bind
/// pose) and cached for the run.
/// </remarks>
internal static class BoneLengthRatios
{
    /// <summary>
    /// Builds a map of <c>sourceBoneName -> ratio</c> for every entry in <paramref name="map"/>. Lengths
    /// are read from each skeleton's node hierarchy (bind pose); a bone with no resolvable target is
    /// omitted (caller falls back to ratio 1 / no rescale).
    /// </summary>
    public static unsafe IReadOnlyDictionary<string, float> Build(Scene* source, Scene* target, RetargetMap map)
    {
        // node name -> bind-pose local translation, for both skeletons (resolved once).
        Dictionary<string, Vector3> srcLocal = CollectLocalTranslations(source);
        Dictionary<string, Vector3> tgtLocal = CollectLocalTranslations(target);

        var ratios = new Dictionary<string, float>(map.Bones.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in map.Bones)
        {
            string srcBone = pair.Key;
            string tgtBone = pair.Value;

            // A bone's length is how far its FIRST CHILD sits from it, so we need each child node's own
            // translation (which is relative to this bone). Re-scan the trees for child offsets.
            float srcLen = ChildLength(source, srcBone, srcLocal);
            float tgtLen = ChildLength(target, tgtBone, tgtLocal);

            if (srcLen <= 1e-6f)
            {
                ratios[srcBone] = 1f; // source is a leaf or unresolvable: don't rescale
                continue;
            }

            ratios[srcBone] = tgtLen > 1e-6f ? tgtLen / srcLen : 1f;
        }

        return ratios;
    }

    /// <summary>Walks a tree collecting each node's own bind-pose translation (offset from its parent).</summary>
    private static unsafe Dictionary<string, Vector3> CollectLocalTranslations(Scene* scene)
    {
        var map = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        if (scene->MRootNode is not null)
        {
            CollectLocalTranslations(scene->MRootNode, map);
        }

        return map;
    }

    private static unsafe void CollectLocalTranslations(Node* node, Dictionary<string, Vector3> output)
    {
        if (node is null)
        {
            return;
        }

        string name = node->MName.AsString;
        if (!string.IsNullOrEmpty(name))
        {
            // node->MTransformation is the bind-pose local transform; .Translation is the offset.
            Matrix4x4 m = TransposeAssimp(node->MTransformation);
            output[name] = m.Translation;
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            CollectLocalTranslations(node->MChildren[i], output);
        }
    }

    /// <summary>The length of <paramref name="bone"/>: the translation magnitude of its first named child.</summary>
    private static unsafe float ChildLength(Scene* scene, string bone, Dictionary<string, Vector3> localTranslations)
    {
        Node* node = FindNode(scene->MRootNode, bone);
        if (node is null)
        {
            return 0f;
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            Node* child = node->MChildren[i];
            if (child is null)
            {
                continue;
            }

            string childName = child->MName.AsString;
            if (!string.IsNullOrEmpty(childName) && localTranslations.TryGetValue(childName, out Vector3 offset))
            {
                return offset.Length();
            }
        }

        return 0f; // leaf
    }

    private static unsafe Node* FindNode(Node* node, string name)
    {
        if (node is null)
        {
            return null;
        }

        if (node->MName.AsString == name)
        {
            return node;
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            Node* found = FindNode(node->MChildren[i], name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Assimp exposes aiMatrix4x4 with the same element layout as System.Numerics.Matrix4x4 but column-major
    /// intent; System.Numerics is row-vector, so transpose to consume it consistently with the renderer.
    /// </summary>
    private static Matrix4x4 TransposeAssimp(Matrix4x4 m) => Matrix4x4.Transpose(m);
}
