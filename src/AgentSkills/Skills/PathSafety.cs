namespace AgentSkills.SkillModel;

public static class PathSafety
{
    /// <summary>Returns true when the resolved <paramref name="target"/> stays inside <paramref name="basePath"/>.</summary>
    public static bool IsPathInside(string basePath, string target)
    {
        var normBase = Path.GetFullPath(basePath);
        var normTarget = Path.GetFullPath(target);
        if (string.Equals(normBase, normTarget, StringComparison.Ordinal))
        {
            return true;
        }

        var withSep = normBase.EndsWith(Path.DirectorySeparatorChar) ? normBase : normBase + Path.DirectorySeparatorChar;
        return normTarget.StartsWith(withSep, StringComparison.Ordinal);
    }

    /// <summary>Throws if <paramref name="subpath"/> contains any <c>..</c> segments (mirrors <c>sanitizeSubpath</c>).</summary>
    public static string SanitizeSubpath(string subpath)
    {
        var normalized = subpath.Replace('\\', '/');
        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                throw new ArgumentException(
                    $"Unsafe subpath: \"{subpath}\" contains path traversal segments. Subpaths must not contain \"..\" components.",
                    nameof(subpath));
            }
        }

        return subpath;
    }
}
