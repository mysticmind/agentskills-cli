using Microsoft.Extensions.Logging;
using AgentSkills.SkillModel;
using AgentSkills.Sources;

namespace AgentSkills.Install;

public sealed class InstallService : IInstallService
{
    private readonly ISourceResolver _resolver;
    private readonly ILogger<InstallService> _logger;

    public InstallService(ISourceResolver resolver, ILogger<InstallService> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<InstallSummary> InstallAsync(InstallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var source = await _resolver
            .ResolveAsync(request.SourceInput, request.ResolveOptions, cancellationToken)
            .ConfigureAwait(false);

        // User --path overrides whatever subpath the source string encoded (e.g. a git
        // /tree/<ref>/<path> URL or an npm/NuGet "skills root" convention).
        var effectiveSubpath = ResolveSubpath(request.SubpathOverride, source.Subpath, source.RootPath);

        var discovered = SkillDiscovery.Discover(
            source.RootPath,
            effectiveSubpath,
            new SkillDiscovery.DiscoverOptions(IncludeInternal: request.IncludeInternal));

        if (discovered.Count == 0)
        {
            throw new SkillSourceException(source.DisplaySource, $"No SKILL.md files found in {source.DisplaySource}.");
        }

        var filtered = FilterByName(discovered, request.SkillFilter);
        if (filtered.Count == 0)
        {
            throw new UserInputException(
                $"None of the requested skills [{string.Join(", ", request.SkillFilter)}] are present in {source.DisplaySource}.");
        }

        return await InstallSkillsAsync(source, filtered, request, cancellationToken).ConfigureAwait(false);
    }

    public Task<InstallSummary> InstallSkillsAsync(
        ISkillSource source,
        IReadOnlyList<Skill> skills,
        InstallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Installing {SkillCount} skill(s) for {AgentCount} agent(s) (mode={Mode}, global={Global})",
            skills.Count, request.Agents.Count, request.Mode, request.Global);

        // Each scope owns exactly one lock: global installs go to the global lock, project
        // installs to ./skills-lock.json. (Before 1.1.0 project installs also wrote the
        // global lock, which made `update -g` treat project skills as global ones.)
        var globalLock = request.Global ? SkillLock.Load() : null;
        var localLock = request.Global ? null : LocalLock.Load(request.Cwd);

        // Fail before touching disk if the lock can't be written afterwards.
        if (globalLock?.ReadOnlyReason is { } g) throw new LockFileException(SkillLock.GetLockPath(), g);
        if (localLock?.ReadOnlyReason is { } l) throw new LockFileException(LocalLock.GetLockPath(request.Cwd), l);

        var plan = PlanInstallNames(skills, request.Aliases);
        var trackedSource = TrackedSource(source);
        if (!request.Replace)
        {
            var conflicts = SkillConflicts.Find(
                trackedSource, plan.Select(p => (p.Skill.Name, p.InstallName)), request.Global, request.Cwd);
            if (conflicts.Count > 0) throw new SkillConflictException(conflicts);
        }

        var outcomes = new List<SkillInstallOutcome>(skills.Count * request.Agents.Count);
        var roots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (skill, installName) in plan)
        {
            var aliased = !string.Equals(installName, skill.Name, StringComparison.Ordinal);
            var installSkill = aliased ? skill with { Name = installName } : skill;
            var anySuccess = false;
            foreach (var agent in request.Agents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = Installer.InstallForAgent(installSkill, agent,
                    new Installer.Options(Global: request.Global, Cwd: request.Cwd, Mode: request.Mode, RewriteName: aliased));

                outcomes.Add(new SkillInstallOutcome(skill, agent, result, aliased ? installName : null));

                if (result.Success)
                {
                    anySuccess = true;
                    var displayPath = result.CanonicalPath is not null && result.Skipped ? result.CanonicalPath : result.Path;
                    var root = Path.GetDirectoryName(displayPath);
                    if (!string.IsNullOrEmpty(root)) roots.Add(root);
                }
            }

            if (anySuccess)
            {
                UpdateLocks(source, skill, installName, globalLock, localLock);
            }
        }

        if (globalLock is not null) SkillLock.Save(globalLock);
        if (localLock is not null) LocalLock.Save(localLock, request.Cwd);

        return Task.FromResult(new InstallSummary(source.DisplaySource, outcomes, roots));
    }

    /// <summary>
    /// Picks the subpath we scan within the staged source. The user's <c>--path</c>
    /// always wins; otherwise the parsed source's own subpath is used. Validates
    /// against path traversal and confirms the directory exists before returning.
    /// </summary>
    private static string? ResolveSubpath(string? userOverride, string? parsedSubpath, string rootPath)
    {
        var chosen = !string.IsNullOrWhiteSpace(userOverride)
            ? PathSafety.SanitizeSubpath(userOverride.Trim().Replace('\\', '/'))
            : parsedSubpath;

        if (string.IsNullOrEmpty(chosen)) return null;

        // SkillDiscovery validates path safety against the root, but report a clearer
        // error here when the directory simply doesn't exist in the staged source.
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, chosen));
        if (!Directory.Exists(fullPath))
        {
            throw new UserInputException(
                $"Path '{chosen}' does not exist inside the staged source. " +
                "Double-check --path or drop the flag to scan the whole source.");
        }
        return chosen;
    }

    private static List<Skill> FilterByName(List<Skill> skills, IReadOnlyList<string> names)
    {
        if (names.Count == 0 || names.Contains("*", StringComparer.Ordinal))
        {
            return skills;
        }
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return skills.Where(s => wanted.Contains(s.Name)).ToList();
    }

    /// <summary>
    /// Pairs each skill with the name it will be installed under, validating aliases and
    /// rejecting two incoming skills that would land in the same directory.
    /// </summary>
    private static List<(Skill Skill, string InstallName)> PlanInstallNames(
        IReadOnlyList<Skill> skills, IReadOnlyDictionary<string, string>? aliases)
    {
        var lookup = aliases is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase);

        var plan = new List<(Skill, string)>(skills.Count);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            var installName = skill.Name;
            if (lookup.TryGetValue(skill.Name, out var alias) && !string.Equals(alias, skill.Name, StringComparison.Ordinal))
            {
                if (!SkillConflicts.IsValidAlias(alias))
                {
                    throw new UserInputException($"'{alias}' is not a valid skill name for '{skill.Name}'.");
                }
                installName = alias;
            }

            var dir = SkillNameSanitizer.Sanitize(installName);
            if (used.TryGetValue(dir, out var other))
            {
                throw new UserInputException(
                    $"'{skill.Name}' and '{other}' would both be installed as '{dir}'. Give one of them another name with --as.");
            }
            used[dir] = skill.Name;
            plan.Add((skill, installName));
        }
        return plan;
    }

    private static void UpdateLocks(ISkillSource source, Skill skill, string installName, GlobalLockDocument? globalLock, LocalLockDocument? localLock)
    {
        var skillPath = ComputeSkillPath(source, skill);
        var trackedSource = TrackedSource(source);
        var originalName = string.Equals(installName, skill.Name, StringComparison.Ordinal) ? null : skill.Name;

        if (globalLock is not null)
        {
            var now = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            if (!globalLock.Skills.TryGetValue(installName, out var entry))
            {
                entry = new GlobalLockEntry { InstalledAt = now };
                globalLock.Skills[installName] = entry;
            }

            entry.Source = trackedSource;
            entry.SourceType = source.SourceTypeLabel;
            entry.SourceUrl = source.SourceUrl;
            entry.Ref = source.Reference;
            entry.SkillPath = skillPath;
            entry.SkillFolderHash = ComputeBaseline(source, skill, skillPath) ?? string.Empty;
            entry.UpdatedAt = now;
            entry.OriginalName = originalName;
        }

        if (localLock is not null)
        {
            // Reuse an existing entry so fields written by other tools (Extra) survive.
            if (!localLock.Skills.TryGetValue(installName, out var local))
            {
                local = new LocalLockEntry();
                localLock.Skills[installName] = local;
            }
            local.Source = trackedSource;
            local.SourceType = source.SourceTypeLabel;
            local.Ref = source.Reference;
            local.SkillPath = skillPath;
            local.ComputedHash = LocalLock.ComputeFolderHash(skill.Path);
            local.OriginalName = originalName;
        }
    }

    /// <summary>The value written to a lock entry's <c>source</c> field for this staged source.</summary>
    internal static string TrackedSource(ISkillSource source) => source.Parsed.Type switch
    {
        SourceType.NuGet or SourceType.Npm => $"{source.Parsed.PackageId}@{source.Parsed.PackageVersion}",
        SourceType.GitHub or SourceType.GitLab => SourceParser.GetOwnerRepo(source.Parsed) ?? source.DisplaySource,
        _ => source.DisplaySource,
    };

    /// <summary>Path of the skill's SKILL.md relative to the staged source root (forward slashes).</summary>
    internal static string? ComputeSkillPath(ISkillSource source, Skill skill)
    {
        try
        {
            if (source.Parsed.Type == SourceType.Local)
            {
                return null;
            }
            var rel = Path.GetRelativePath(source.RootPath, skill.Path).Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(rel) || rel == "." ? "SKILL.md" : $"{rel}/SKILL.md";
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The global lock's change-detection baseline (<c>skillFolderHash</c>): the git tree SHA
    /// for git-backed sources (matching upstream), otherwise the folder content hash.
    /// </summary>
    internal static string? ComputeBaseline(ISkillSource source, Skill skill, string? skillPath)
    {
        if (source is GitSource git)
        {
            var folder = skillPath is null
                ? null
                : skillPath.EndsWith("/SKILL.md", StringComparison.Ordinal)
                    ? skillPath[..^"/SKILL.md".Length]
                    : skillPath == "SKILL.md" ? null : skillPath;
            return git.ReadFolderTreeSha(folder);
        }

        try
        {
            return LocalLock.ComputeFolderHash(skill.Path);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
