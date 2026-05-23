using Microsoft.Extensions.Logging;
using AgentSkills.Agents;
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

        _logger.LogDebug("Installing {SkillCount} skill(s) for {AgentCount} agent(s) (mode={Mode}, global={Global})",
            filtered.Count, request.Agents.Count, request.Mode, request.Global);

        var lockDoc = SkillLock.Load();
        var localLockDoc = request.Global ? null : LocalLock.Load();

        var outcomes = new List<SkillInstallOutcome>(filtered.Count * request.Agents.Count);
        var roots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var skill in filtered)
        {
            foreach (var agent in request.Agents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = Installer.InstallForAgent(skill, agent,
                    new Installer.Options(Global: request.Global, Mode: request.Mode));

                outcomes.Add(new SkillInstallOutcome(skill, agent, result));

                if (result.Success)
                {
                    UpdateLocks(source, skill, lockDoc, localLockDoc);
                    var displayPath = result.CanonicalPath is not null && result.Skipped ? result.CanonicalPath : result.Path;
                    var root = Path.GetDirectoryName(displayPath);
                    if (!string.IsNullOrEmpty(root)) roots.Add(root);
                }
            }
        }

        SkillLock.Save(lockDoc);
        if (localLockDoc is not null)
        {
            LocalLock.Save(localLockDoc);
        }

        return new InstallSummary(source.DisplaySource, outcomes, roots);
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

    private static void UpdateLocks(ISkillSource source, Skill skill, GlobalLockDocument lockDoc, LocalLockDocument? localLockDoc)
    {
        var now = DateTime.UtcNow.ToString("o");
        var skillPath = ComputeSkillPath(source, skill);
        var folderHash = ComputeFolderHash(source, skill, skillPath);
        var trackedSource = TrackedSource(source);

        if (!lockDoc.Skills.TryGetValue(skill.Name, out var entry))
        {
            entry = new GlobalLockEntry { InstalledAt = now };
            lockDoc.Skills[skill.Name] = entry;
        }

        entry.Source = trackedSource;
        entry.SourceType = source.SourceTypeLabel;
        entry.SourceUrl = source.SourceUrl;
        entry.Ref = source.Reference;
        entry.SkillPath = skillPath;
        entry.SkillFolderHash = folderHash ?? string.Empty;
        entry.UpdatedAt = now;

        if (localLockDoc is not null)
        {
            localLockDoc.Skills[skill.Name] = new LocalLockEntry
            {
                Source = trackedSource,
                SourceType = source.SourceTypeLabel,
                Ref = source.Reference,
                SkillPath = skillPath,
                ComputedHash = LocalLock.ComputeFolderHash(skill.Path),
            };
        }
    }

    private static string TrackedSource(ISkillSource source) => source.Parsed.Type switch
    {
        SourceType.NuGet or SourceType.Npm => $"{source.Parsed.PackageId}@{source.Parsed.PackageVersion}",
        SourceType.GitHub or SourceType.GitLab => SourceParser.GetOwnerRepo(source.Parsed) ?? source.DisplaySource,
        _ => source.DisplaySource,
    };

    private static string? ComputeSkillPath(ISkillSource source, Skill skill)
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
        catch
        {
            return null;
        }
    }

    private static string? ComputeFolderHash(ISkillSource source, Skill skill, string? skillPath)
    {
        if (source is GitSource git && source.Parsed.Type == SourceType.GitHub)
        {
            var folder = skillPath is null
                ? null
                : skillPath.EndsWith("/SKILL.md", StringComparison.Ordinal)
                    ? skillPath[..^"/SKILL.md".Length]
                    : skillPath;
            return git.ReadFolderTreeSha(folder);
        }
        return null;
    }
}
