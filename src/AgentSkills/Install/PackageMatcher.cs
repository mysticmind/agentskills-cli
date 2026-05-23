using AgentSkills.Sources;

namespace AgentSkills.Install;

/// <summary>
/// Shared lock-file source helpers used by <c>list</c> and <c>remove</c>.
/// The lock entry's <c>source</c> field stores either <c>owner/repo</c> (git) or
/// <c>&lt;PackageId&gt;@&lt;version&gt;</c> (npm/NuGet); this type strips the
/// version, accepts the same prefixes <c>add</c> takes, and maps installed skill
/// names back to the package they came from.
/// </summary>
public static class PackageMatcher
{
    /// <summary>
    /// A user-supplied locator split into its base id and (for NuGet/npm only) an
    /// optional pinned version. When <see cref="Version"/> is non-null, callers should
    /// strict-match against the lock entry's full <c>source</c> (<c>id@version</c>);
    /// when null, any installed version of the base id is a match.
    /// </summary>
    public sealed record Locator(string BaseId, string? Version);

    /// <summary>Same as <see cref="NormalizeToLocator"/> but discards the version (compatibility shim).</summary>
    public static string NormalizeInput(string input) => NormalizeToLocator(input).BaseId;

    /// <summary>
    /// Routes a user-supplied locator through <see cref="SourceParser"/> and returns the
    /// base id the installer would write to the lock plus any version the user pinned.
    /// Only NuGet and npm carry versions; other source types return <see cref="Locator.Version"/>
    /// as <c>null</c>.
    /// </summary>
    public static Locator NormalizeToLocator(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0) return new Locator(string.Empty, null);

        try
        {
            var parsed = SourceParser.Parse(trimmed);
            return parsed.Type switch
            {
                SourceType.Local => new Locator(parsed.LocalPath ?? parsed.Url, null),
                SourceType.GitHub or SourceType.GitLab => new Locator(SourceParser.GetOwnerRepo(parsed) ?? parsed.Url, null),
                SourceType.NuGet or SourceType.Npm => new Locator(parsed.PackageId ?? trimmed, parsed.PackageVersion),
                SourceType.Git or SourceType.WellKnown => new Locator(parsed.Url, null),
                _ => new Locator(trimmed, null),
            };
        }
        catch
        {
            return new Locator(ExtractBaseSource(trimmed), null);
        }
    }

    /// <summary>Inverse of <see cref="ExtractBaseSource"/>: returns the <c>@version</c> suffix or null.</summary>
    public static string? ExtractVersion(string lockSourceValue)
    {
        if (string.IsNullOrEmpty(lockSourceValue)) return null;

        if (lockSourceValue.StartsWith('@'))
        {
            var slash = lockSourceValue.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                var at = lockSourceValue.IndexOf('@', slash + 1);
                return at > 0 ? lockSourceValue[(at + 1)..] : null;
            }
            return null;
        }

        var firstAt = lockSourceValue.IndexOf('@', StringComparison.Ordinal);
        return firstAt > 0 ? lockSourceValue[(firstAt + 1)..] : null;
    }

    /// <summary>
    /// Strips the <c>@version</c> suffix from a value stored in a lock file's
    /// <c>source</c> field. Aware of scoped npm: the first <c>@</c> is the scope marker,
    /// the version <c>@</c> appears after the slash.
    /// </summary>
    public static string ExtractBaseSource(string lockSourceValue)
    {
        if (string.IsNullOrEmpty(lockSourceValue)) return lockSourceValue;

        if (lockSourceValue.StartsWith('@'))
        {
            var slash = lockSourceValue.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                var at = lockSourceValue.IndexOf('@', slash + 1);
                return at > 0 ? lockSourceValue[..at] : lockSourceValue;
            }
            return lockSourceValue;
        }

        var firstAt = lockSourceValue.IndexOf('@', StringComparison.Ordinal);
        return firstAt > 0 ? lockSourceValue[..firstAt] : lockSourceValue;
    }

    /// <summary>
    /// Resolves a list of user-supplied positional arguments - where each argument may be
    /// either a skill name or any source locator that <c>add</c> accepts - into the set of
    /// installed skills it refers to. Skill-name and source matches are unioned, so a name
    /// that happens to look like a source matches both ways.
    /// </summary>
    public static List<TSkill> ResolveTargets<TSkill>(
        IReadOnlyList<string> arguments,
        IReadOnlyList<TSkill> installed,
        Func<TSkill, string> nameSelector)
    {
        if (arguments.Count == 0) return installed.ToList();

        var byName = installed
            .GroupBy(nameSelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var sourceMatches = ResolveSkillNamesFromPackages(arguments)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var picked = new Dictionary<string, TSkill>(StringComparer.OrdinalIgnoreCase);

        foreach (var arg in arguments)
        {
            if (byName.TryGetValue(arg, out var direct))
            {
                picked[nameSelector(direct)] = direct;
            }
        }

        foreach (var name in sourceMatches)
        {
            if (byName.TryGetValue(name, out var bySource))
            {
                picked[nameSelector(bySource)] = bySource;
            }
        }

        return picked.Values.ToList();
    }

    /// <summary>
    /// Walks the global and project locks to find every skill installed from the given
    /// package(s). When a locator pins a version, only that version is matched; when no
    /// version is pinned, any installed version of the base id matches.
    /// </summary>
    public static IEnumerable<string> ResolveSkillNamesFromPackages(IEnumerable<string> packages)
    {
        var locators = packages
            .Select(NormalizeToLocator)
            .Where(l => !string.IsNullOrEmpty(l.BaseId))
            .ToList();

        if (locators.Count == 0) yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, entry) in SkillLock.Load().Skills)
        {
            if (Matches(entry.Source, locators) && seen.Add(name))
            {
                yield return name;
            }
        }

        foreach (var (name, entry) in LocalLock.Load().Skills)
        {
            if (Matches(entry.Source, locators) && seen.Add(name))
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Per locator, returns the set of versions installed for that locator's base id.
    /// Used by the CLI to produce a helpful "did you mean…?" hint when the user pins a
    /// version that nothing matches.
    /// </summary>
    public static Dictionary<string, HashSet<string>> InstalledVersionsByBaseId(IEnumerable<string> packages)
    {
        var bases = packages
            .Select(p => NormalizeToLocator(p).BaseId)
            .Where(b => !string.IsNullOrEmpty(b))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (bases.Count == 0) return result;

        void Collect(IEnumerable<KeyValuePair<string, string>> entries)
        {
            foreach (var (_, source) in entries)
            {
                var b = ExtractBaseSource(source);
                if (!bases.Contains(b)) continue;
                var v = ExtractVersion(source);
                if (v is null) continue;
                if (!result.TryGetValue(b, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[b] = set;
                }
                set.Add(v);
            }
        }

        Collect(SkillLock.Load().Skills.Select(p => new KeyValuePair<string, string>(p.Key, p.Value.Source)));
        Collect(LocalLock.Load().Skills.Select(p => new KeyValuePair<string, string>(p.Key, p.Value.Source)));
        return result;
    }

    private static bool Matches(string lockSource, IReadOnlyList<Locator> locators)
    {
        var entryBase = ExtractBaseSource(lockSource);
        var entryVersion = ExtractVersion(lockSource);

        foreach (var loc in locators)
        {
            if (!string.Equals(loc.BaseId, entryBase, StringComparison.OrdinalIgnoreCase)) continue;
            if (loc.Version is null) return true;
            if (string.Equals(loc.Version, entryVersion, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// A skill's recorded source for display + grouping. Returns the raw
    /// <c>source</c> string (including version, e.g. <c>@jasperfx/ai-skills@1.0.0</c>)
    /// along with the stripped base id and source type.
    /// </summary>
    public sealed record SourceInfo(string Source, string BaseId, string SourceType);

    /// <summary>
    /// Returns the source the skill was installed from, preferring the global lock
    /// when the same name appears in both. Null when the skill isn't tracked
    /// (installed manually, or before lock tracking).
    /// </summary>
    public static SourceInfo? LookupSource(string skillName)
    {
        var global = SkillLock.Load();
        if (global.Skills.TryGetValue(skillName, out var g))
        {
            return new SourceInfo(g.Source, ExtractBaseSource(g.Source), g.SourceType);
        }
        var local = LocalLock.Load();
        if (local.Skills.TryGetValue(skillName, out var l))
        {
            return new SourceInfo(l.Source, ExtractBaseSource(l.Source), l.SourceType);
        }
        return null;
    }
}
