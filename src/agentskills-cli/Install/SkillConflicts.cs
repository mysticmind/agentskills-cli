using AgentSkills.SkillModel;

namespace AgentSkills.Install;

/// <summary>
/// An incoming skill whose install name is already taken in the target scope by a skill
/// from a different source (or by an untracked, manually installed skill).
/// </summary>
/// <param name="SkillName">The incoming skill's name in its source.</param>
/// <param name="InstallName">The name it would be installed under (the alias, if one was given).</param>
/// <param name="Scope"><c>global</c> or <c>project</c>.</param>
/// <param name="ExistingSource">The existing skill's lock <c>source</c>; null when it isn't tracked.</param>
public sealed record SkillConflict(
    string SkillName,
    string InstallName,
    string Scope,
    string? ExistingSource,
    string? ExistingSourceType)
{
    public string DescribeExisting() => ExistingSource is null
        ? $"an untracked skill named '{InstallName}' (installed manually or by another tool)"
        : $"'{InstallName}' from {ExistingSource} ({ExistingSourceType})";
}

/// <summary>Raised when an install would silently replace a skill from a different source.</summary>
public sealed class SkillConflictException : SkillsException
{
    public IReadOnlyList<SkillConflict> Conflicts { get; }

    public SkillConflictException(IReadOnlyList<SkillConflict> conflicts)
        : base(BuildMessage(conflicts), exitCode: 2)
    {
        Conflicts = conflicts;
    }

    private static string BuildMessage(IReadOnlyList<SkillConflict> conflicts)
    {
        var lines = conflicts.Select(c =>
            $"  - '{c.SkillName}' conflicts with {c.DescribeExisting()} in the {c.Scope} scope");
        var example = conflicts[0];
        return "Skill name conflict; nothing was installed:" + Environment.NewLine +
               string.Join(Environment.NewLine, lines) + Environment.NewLine +
               $"Install under another name with --as {example.SkillName}=<new-name>, " +
               "or overwrite the existing skill(s) with --replace.";
    }
}

public static class SkillConflicts
{
    /// <summary>
    /// Finds incoming skills whose install name is taken in the target scope by a skill from
    /// a different source. Re-installing from the same source (any version or ref) is not a
    /// conflict, nor is a stale lock entry with nothing on disk.
    /// </summary>
    /// <param name="incomingSource">The lock <c>source</c> the install would record (see <see cref="InstallService.TrackedSource"/>).</param>
    /// <param name="skills">Incoming skills: their source name and the name they'd be installed under.</param>
    public static List<SkillConflict> Find(
        string incomingSource,
        IEnumerable<(string SkillName, string InstallName)> skills,
        bool global,
        string? cwd = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var scope = global ? "global" : "project";
        var incomingBase = PackageMatcher.ExtractBaseSource(incomingSource);

        var onDisk = Installer.List(global: global, cwd: cwd)
            .Select(s => SkillNameSanitizer.Sanitize(s.Name))
            .ToHashSet(StringComparer.Ordinal);

        var tracked = new Dictionary<string, (string Source, string SourceType)>(StringComparer.Ordinal);
        if (global)
        {
            foreach (var (name, e) in SkillLock.Load().Skills) tracked[SkillNameSanitizer.Sanitize(name)] = (e.Source, e.SourceType);
        }
        else
        {
            foreach (var (name, e) in LocalLock.Load(cwd).Skills) tracked[SkillNameSanitizer.Sanitize(name)] = (e.Source, e.SourceType);
        }

        var conflicts = new List<SkillConflict>();
        foreach (var (skillName, installName) in skills)
        {
            var key = SkillNameSanitizer.Sanitize(installName);
            if (!onDisk.Contains(key)) continue;

            if (!tracked.TryGetValue(key, out var existing))
            {
                conflicts.Add(new SkillConflict(skillName, installName, scope, null, null));
                continue;
            }

            if (!string.Equals(PackageMatcher.ExtractBaseSource(existing.Source), incomingBase, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new SkillConflict(skillName, installName, scope, existing.Source, existing.SourceType));
            }
        }
        return conflicts;
    }

    /// <summary>
    /// Suggests an alias for <paramref name="skillName"/> from the incoming source: the GitHub
    /// owner, npm scope, or first segment of a NuGet id (e.g. <c>foo</c> from
    /// <c>Contoso.AgentSkills</c> → <c>foo-contoso</c>), made unique against <paramref name="taken"/>.
    /// </summary>
    public static string SuggestAlias(string skillName, string incomingSource, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var baseId = PackageMatcher.ExtractBaseSource(incomingSource ?? string.Empty).TrimStart('@');
        var token = baseId.Contains('/', StringComparison.Ordinal)
            ? baseId[..baseId.IndexOf('/', StringComparison.Ordinal)]
            : baseId.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "alt";
        if (Path.IsPathRooted(baseId)) token = "local";

        var candidate = SkillNameSanitizer.Sanitize($"{skillName}-{token}");
        if (candidate.Length > 60) candidate = candidate[..60].TrimEnd('-', '.');
        var result = candidate;
        for (var n = 2; taken(result); n++) result = $"{candidate}-{n}";
        return result;
    }

    /// <summary>All install names (sanitized) used in a scope, on disk or in its lock.</summary>
    public static HashSet<string> NamesInScope(bool global, string? cwd = null)
    {
        var names = Installer.List(global: global, cwd: cwd)
            .Select(s => SkillNameSanitizer.Sanitize(s.Name))
            .ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> keys = global ? SkillLock.Load().Skills.Keys : LocalLock.Load(cwd).Skills.Keys;
        foreach (var k in keys) names.Add(SkillNameSanitizer.Sanitize(k));
        return names;
    }

    /// <summary>
    /// Aliases chosen in an earlier install of the same source: lock entries in the scope whose
    /// <c>originalName</c> is one of <paramref name="skillNames"/> and whose source has the
    /// same base id. Returns source name → alias.
    /// </summary>
    public static Dictionary<string, string> RememberedAliases(
        string incomingSource, IEnumerable<string> skillNames, bool global, string? cwd = null)
    {
        var incomingBase = PackageMatcher.ExtractBaseSource(incomingSource);
        var wanted = skillNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<(string Key, string Source, string? Original)> entries = global
            ? SkillLock.Load().Skills.Select(kv => (kv.Key, kv.Value.Source, kv.Value.OriginalName))
            : LocalLock.Load(cwd).Skills.Select(kv => (kv.Key, kv.Value.Source, kv.Value.OriginalName));

        foreach (var (key, source, original) in entries)
        {
            if (original is null || !wanted.Contains(original)) continue;
            if (!string.Equals(PackageMatcher.ExtractBaseSource(source), incomingBase, StringComparison.OrdinalIgnoreCase)) continue;
            result[original] = key;
        }
        return result;
    }

    /// <summary>
    /// True when <paramref name="name"/> can be used as an install name as-is: it is already
    /// in the sanitized directory form (lowercase letters, digits, '-', '.', '_').
    /// </summary>
    public static bool IsValidAlias(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 64 &&
        string.Equals(SkillNameSanitizer.Sanitize(name), name, StringComparison.Ordinal);

    /// <summary>
    /// Parses <c>--as</c> values. Each is <c>skill=alias</c>, or a bare <c>alias</c> when exactly
    /// one skill is being installed. Returns original name → alias.
    /// </summary>
    public static Dictionary<string, string> ParseAliases(IReadOnlyList<string> values, IReadOnlyList<string> skillNames)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(skillNames);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in values)
        {
            var value = raw.Trim();
            string skill, alias;
            var eq = value.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                skill = value[..eq].Trim();
                alias = value[(eq + 1)..].Trim();
            }
            else if (skillNames.Count == 1)
            {
                skill = skillNames[0];
                alias = value;
            }
            else
            {
                throw new UserInputException(
                    $"--as {value}: {skillNames.Count} skills are selected, so say which one to rename: --as <skill>=<new-name>.");
            }

            var match = skillNames.FirstOrDefault(n => string.Equals(n, skill, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new UserInputException(
                    $"--as {value}: '{skill}' is not one of the skills being installed ({string.Join(", ", skillNames)}).");
            }
            if (!IsValidAlias(alias))
            {
                throw new UserInputException(
                    $"--as {value}: '{alias}' is not a valid skill name. Use lowercase letters, digits and hyphens (for example '{SkillNameSanitizer.Sanitize(alias)}').");
            }
            result[match] = alias;
        }
        return result;
    }
}
