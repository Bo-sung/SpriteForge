using System.Text;
using Silk.NET.Assimp;

namespace SpriteForge.Core.Rendering;

/// <summary>
/// Builds a human-readable dump of a model's bone/node tree, the CLI analogue of an engine's
/// bone-picker dropdown: it lists every bone name so an equipment manifest's <c>socketBone</c> can be
/// filled in exactly. Equipment-relevant sites (hand / head / spine / forearm) are flagged inline.
/// </summary>
public static class BoneReporter
{
    /// <summary>
    /// Formats the scene's node tree as an indented list, one node per line, with equipment hints.
    /// Returns a short header line plus the tree. Empty when the scene has no root node.
    /// </summary>
    public static unsafe string Report(Scene scene)
    {
        if (scene.MRootNode is null)
        {
            return "skeleton: no root node found.";
        }

        int count = CountNodes(scene.MRootNode);
        var sb = new StringBuilder();
        sb.Append("skeleton: ").Append(count).Append(" node(s).");
        AppendTree(sb, scene.MRootNode, depth: 0);
        return sb.ToString();
    }

    private static unsafe void AppendTree(StringBuilder sb, Node* node, int depth)
    {
        if (node is null)
        {
            return;
        }

        sb.Append('\n');
        for (int i = 0; i < depth; i++)
        {
            sb.Append("  ");
        }

        string name = node->MName.AsString;
        sb.Append(string.IsNullOrEmpty(name) ? "(unnamed)" : name);

        string? hint = EquipmentHint(name);
        if (hint is not null)
        {
            sb.Append("  <- ").Append(hint);
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            AppendTree(sb, node->MChildren[i], depth + 1);
        }
    }

    private static unsafe int CountNodes(Node* node)
    {
        if (node is null)
        {
            return 0;
        }

        int n = 1;
        for (uint i = 0; i < node->MNumChildren; i++)
        {
            n += CountNodes(node->MChildren[i]);
        }

        return n;
    }

    /// <summary>
    /// An inline equipment hint for a bone name, matched by its normalized form (so namespace prefixes
    /// like <c>mixamorig:</c> are ignored). Returns null for bones with no common equipment role.
    /// </summary>
    private static string? EquipmentHint(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string n = BoneMatcher.Normalize(name);

        // Order matters: check the more specific names first so "RightHand" doesn't trip the "Hand" rule
        // ambiguously with its own finger bones (we just want to flag the hand site, once).
        if (n.EndsWith("righthand", StringComparison.Ordinal)) return "weapon (right hand socket)";
        if (n.EndsWith("lefthand", StringComparison.Ordinal)) return "weapon / shield (left hand socket)";
        if (n.EndsWith("rightforearm", StringComparison.Ordinal)) return "shield / bracer (right forearm)";
        if (n.EndsWith("leftforearm", StringComparison.Ordinal)) return "shield / bracer (left forearm)";
        if (n.EndsWith("head", StringComparison.Ordinal)) return "helmet";
        if (n.EndsWith("spine2", StringComparison.Ordinal) || n.EndsWith("upperchest", StringComparison.Ordinal)) return "back sheath / backpack";
        if (n.EndsWith("hips", StringComparison.Ordinal) || n.EndsWith("pelvis", StringComparison.Ordinal)) return "root anchor (auto-detected)";

        return null;
    }
}
