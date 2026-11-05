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
    /// <param name="scopeSelector">
    /// Optional. When supplied (returning <c>"global"</c> or <c>"project"</c>), source matches
    /// are checked against the lock for that item's own scope, so a package installed in the
    /// project never selects a same-named skill installed globally from a different source.
    /// </param>
    public static List<TSkill> ResolveTargets<TSkill>(
        IReadOnlyList<string> arguments,
        IReadOnlyList<TSkill> installed,
        Func<TSkill, string> nameSelector,
        Func<TSkill, string>? scopeSelector = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(nameSelector);
        if (arguments.Count == 0) return installed.ToList();

        var names = new HashSet<string>(arguments, StringComparer.OrdinalIgnoreCase);
        var sourceMatches = ResolveScopedSkillNamesFromPackages(arguments).ToList();
        var anyScope = sourceMatches.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scoped = sourceMatches.Select(m => $"{m.Scope}:{m.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var picked = new List<TSkill>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in installed)
        {
            var name = nameSelector(item);
            var scope = scopeSelector?.Invoke(item);
            var key = scope is null ? name : $"{scope}:{name}";

            var hit = names.Contains(name)
                || (scope is null ? anyScope.Contains(name) : scoped.Contains(key));

            if (hit && seen.Add(key)) picked.Add(item);
        }
        return picked;
    }

    /// <summary>
    /// Walks the global and project locks to find every skill installed from the given
    /// package(s). When a locator pins a version, only that version is matched; when no
    /// version is pinned, any installed version of the base id matches.
    /// </summary>
    public static IEnumerable<string> ResolveSkillNamesFromPackages(IEnumerable<string> packages) =>
        ResolveScopedSkillNamesFromPackages(packages).Select(m => m.Name).Distinct(StringComparer.Ordinal);

    /// <summary>Same as <see cref="ResolveSkillNamesFromPackages"/> but keeps which lock (scope) each match came from.</summary>
    public static IEnumerable<(string Scope, string Name)> ResolveScopedSkillNamesFromPackages(IEnumerable<string> packages)
    {
        var locators = packages
            .Select(NormalizeToLocator)
            .Where(l => !string.IsNullOrEmpty(l.BaseId))
            .ToList();

        if (locators.Count == 0) yield break;

        foreach (var (name, entry) in SkillLock.Load().Skills)
        {
            if (Matches(entry.Source, locators)) yield return ("global", name);
        }

        foreach (var (name, entry) in LocalLock.Load().Skills)
        {
            if (Matches(entry.Source, locators)) yield return ("project", name);
        }
    }

    /// <summary>
    /// True when a lock entry is selected by <paramref name="arguments"/>: either an argument
    /// equals the skill name, or it is a source locator whose base id matches the entry's
    /// source. Unlike <c>list</c>/<c>remove</c>, a pinned version is <b>not</b> a filter here
    /// (for <c>update</c> it is the version to move to), so only the base id is compared.
    /// </summary>
    public static bool MatchesByNameOrBaseId(IReadOnlyList<Locator> locators, IReadOnlyList<string> arguments, string skillName, string lockSource)
    {
        ArgumentNullException.ThrowIfNull(locators);
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0) return true;
        if (arguments.Any(a => string.Equals(a, skillName, StringComparison.OrdinalIgnoreCase))) return true;
        var entryBase = ExtractBaseSource(lockSource);
        return locators.Any(l => l.BaseId.Length > 0 && string.Equals(l.BaseId, entryBase, StringComparison.OrdinalIgnoreCase));
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
    public sealed record SourceInfo(string Source, string BaseId, string SourceType, string? OriginalName = null);

    /// <summary>
    /// Returns the source the skill was installed from. With <paramref name="scope"/>
    /// (<c>"global"</c> / <c>"project"</c>) only that scope's lock is consulted; without it the
    /// global lock wins when the name appears in both. Null when the skill isn't tracked
    /// (installed manually, or before lock tracking).
    /// </summary>
    public static SourceInfo? LookupSource(string skillName, string? scope = null)
    {
        if (scope is null or "global")
        {
            var global = SkillLock.Load();
            if (global.Skills.TryGetValue(skillName, out var g))
            {
                return new SourceInfo(g.Source, ExtractBaseSource(g.Source), g.SourceType, g.OriginalName);
            }
        }
        if (scope is null or "project")
        {
            var local = LocalLock.Load();
            if (local.Skills.TryGetValue(skillName, out var l))
            {
                return new SourceInfo(l.Source, ExtractBaseSource(l.Source), l.SourceType, l.OriginalName);
            }
        }
        return null;
    }
}
