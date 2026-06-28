using System.Globalization;
using System.Text;
using Silk.NET.Assimp;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Resolves a socket bone name against a character skeleton with a tolerant, normalization-based
/// match — so a manifest can say <c>"righthand"</c> and bind to Mixamo's <c>mixamorig:RightHand</c>
/// without the user having to copy the exact name (namespace prefix, casing, separators).
/// </summary>
/// <remarks>
/// <para>
/// This is the CLI analogue of an engine's bone-picker dropdown: there the user picks from a known
/// list, so the name is always exact. Here, a free-text <c>socketBone</c> string is matched with
/// progressive relaxation, falling back to an exact lookup last so existing manifests keep working.
/// </para>
/// <para>
/// <b>Normalization</b> strips anything that's not a letter or digit and lowercases, so
/// <c>mixamorig:RightHand</c>, <c>RightHand</c>, <c>right_hand</c>, <c>RIGHT-HAND</c> and
/// <c>righthand</c> all normalize to <c>righthand</c> and match each other.
/// </para>
/// </remarks>
public static class BoneMatcher
{
    /// <summary>
    /// Resolves <paramref name="socketBone"/> against the keys of <paramref name="boneNames"/> (the
    /// character's bone-name set). Returns the exact bone name on a hit, or null when no bone matches
    /// even loosely.
    /// </summary>
    /// <param name="boneNames">The character's bone names (the keys of the per-node global-transform map).</param>
    /// <param name="socketBone">The socket bone name from the equipment manifest (may be loose).</param>
    /// <returns>The exact bone name in <paramref name="boneNames"/>, or null.</returns>
    public static string? Resolve(IEnumerable<string> boneNames, string? socketBone)
    {
        if (string.IsNullOrWhiteSpace(socketBone))
        {
            return null;
        }

        var names = boneNames as ICollection<string> ?? boneNames.ToList();
        if (names.Count == 0)
        {
            return null;
        }

        // Exact (ordinal) match first: the common, fast path. Keeps existing manifests verbatim.
        if (names.Contains(socketBone))
        {
            return socketBone;
        }

        string needle = Normalize(socketBone);

        // Exact-normalized match (case/separator-insensitive but full-name). Catches
        // "righthand" vs "RightHand" and "Right_Hand" without prefix ambiguity.
        foreach (string name in names)
        {
            if (Normalize(name) == needle)
            {
                return name;
            }
        }

        // Suffix match: the manifest gave a bare part that the skeleton carries as a suffix, after a
        // namespace prefix and/or a path. e.g. "righthand" -> "mixamorig:RightHand",
        // "hand" -> "Armature|Hand_R". Only a UNIQUE suffix is accepted to avoid binding a weapon to
        // the wrong hand when both "RightHand" and "RightHandThumb1" contain "hand".
        return ResolveUniqueSuffix(names, needle);
    }

    /// <summary>
    /// The normalized names that <paramref name="socketBone"/> could plausibly bind to (used to enrich
    /// "bone not found" errors with suggestions). Returns up to <paramref name="limit"/> candidates
    /// whose normalized form contains the normalized needle.
    /// </summary>
    public static IReadOnlyList<string> Suggest(IEnumerable<string> boneNames, string? socketBone, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(socketBone))
        {
            return Array.Empty<string>();
        }

        string needle = Normalize(socketBone);
        if (needle.Length == 0)
        {
            return Array.Empty<string>();
        }

        var hits = new List<string>(limit);
        foreach (string name in boneNames)
        {
            if (hits.Count >= limit)
            {
                break;
            }

            if (Normalize(name).Contains(needle, StringComparison.Ordinal))
            {
                hits.Add(name);
            }
        }

        return hits;
    }

    /// <summary>
    /// Finds the single bone whose normalized name ENDS with the (normalized) needle. Returns null if
    /// there is no match, or if more than one bone matches (ambiguous) — ambiguity is left to the user
    /// to resolve by supplying a more specific name.
    /// </summary>
    private static string? ResolveUniqueSuffix(IEnumerable<string> names, string needle)
    {
        // Snapshot once so the caller's enumerable isn't enumerated twice.
        var list = names as ICollection<string> ?? names.ToList();
        string? match = null;
        bool ambiguous = false;
        foreach (string name in list)
        {
            string norm = Normalize(name);
            if (norm.Length > needle.Length && norm.EndsWith(needle, StringComparison.Ordinal))
            {
                if (match is not null)
                {
                    ambiguous = true;
                    break;
                }

                match = name;
            }
        }

        return ambiguous ? null : match;
    }

    /// <summary>
    /// Lowercases and keeps only letters/digits, dropping namespace prefixes (<c>mixamorig:</c>), path
    /// separators (<c>|</c>), and word separators (<c>_</c>, <c>-</c>, <c>.</c>, space). So
    /// <c>mixamorig:RightHand</c> -> <c>righthand</c>.
    /// </summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }
}
