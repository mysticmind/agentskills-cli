using System.Text.RegularExpressions;

namespace Skills.SkillModel;

public static partial class SkillNameSanitizer
{
    [GeneratedRegex(@"[^a-z0-9._]+")]
    private static partial Regex InvalidChars();

    [GeneratedRegex(@"^[.\-]+|[.\-]+$")]
    private static partial Regex EdgeChars();

    /// <summary>
    /// Mirrors upstream installer.ts <c>sanitizeName</c>: lowercase, collapse non
    /// <c>[a-z0-9._]</c> runs to <c>-</c>, strip leading/trailing dots/hyphens, cap at 255.
    /// </summary>
    public static string Sanitize(string name)
    {
        var lower = name.ToLowerInvariant();
        var collapsed = InvalidChars().Replace(lower, "-");
        var trimmed = EdgeChars().Replace(collapsed, string.Empty);
        var capped = trimmed.Length > 255 ? trimmed[..255] : trimmed;
        return capped.Length == 0 ? "unnamed-skill" : capped;
    }
}
