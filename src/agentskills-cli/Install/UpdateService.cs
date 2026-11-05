using System.Text.Json.Serialization;
using AgentSkills.Agents;
using AgentSkills.SkillModel;
using AgentSkills.Sources;
using Microsoft.Extensions.Logging;

namespace AgentSkills.Install;

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    Updated,
    Skipped,
    Failed,
}

/// <summary>What <c>update</c> / <c>outdated</c> should look at.</summary>
/// <param name="Targets">Skill names and/or source locators (any shape <c>add</c> accepts). Empty = everything.</param>
/// <param name="Agents">When non-null, only skills installed for these agents, and reinstall only for them.</param>
/// <param name="Force">Treat every checkable skill as needing a reinstall.</param>
/// <param name="VersionLock">Keep NuGet/npm updates on the current major (or major.minor) version.</param>
public sealed record UpdateQuery(
    IReadOnlyList<string> Targets,
    bool IncludeGlobal,
    bool IncludeProject,
    IReadOnlyList<AgentConfig>? Agents,
    bool IncludePrerelease,
    bool Force,
    ResolveOptions ResolveOptions,
    string? Cwd = null,
    VersionLock VersionLock = VersionLock.None);

/// <summary>One tracked skill and what <c>update</c> found out about it.</summary>
public sealed class UpdateItem
{
    public required string Name { get; init; }
    public required string Scope { get; init; }
    public required string Source { get; init; }
    public required string SourceType { get; init; }
    [JsonIgnore] public string? SourceUrl { get; init; }
    [JsonIgnore] public string? Ref { get; init; }
    [JsonIgnore] public string? SkillPath { get; init; }
    [JsonIgnore] public string? Baseline { get; init; }

    /// <summary>The skill's name in its source when installed under an alias; null otherwise.</summary>
    public string? OriginalName { get; init; }

    /// <summary>The name to look the skill up by inside its source.</summary>
    [JsonIgnore] public string SourceName => OriginalName ?? Name;
    [JsonIgnore] public InstalledSkill? Installed { get; init; }

    public UpdateStatus Status { get; set; } = UpdateStatus.UpToDate;
    public string? Current { get; set; }
    public string? Latest { get; set; }
    public string? Reason { get; set; }
    public IReadOnlyList<string> Agents => Installed?.Agents ?? [];

    /// <summary>True when the lock tracks the skill but it isn't installed in that scope.</summary>
    [JsonIgnore] public bool StaleLockEntry { get; set; }

    /// <summary>The source string to resolve when reinstalling.</summary>
    [JsonIgnore] public string? InstallSpec { get; set; }

    internal void Mark(UpdateStatus status, string? reason = null)
    {
        Status = status;
        Reason = reason;
    }
}

/// <summary>Result of <see cref="UpdateService.CheckAsync"/>. Owns sources staged during the check.</summary>
public sealed class UpdatePlan : IDisposable
{
    internal UpdatePlan(List<UpdateItem> items) => Items = items;

    public IReadOnlyList<UpdateItem> Items { get; }
    internal Dictionary<string, ISkillSource> Staged { get; } = new(StringComparer.Ordinal);

    public int Count(UpdateStatus status) => Items.Count(i => i.Status == status);

    public void Dispose()
    {
        foreach (var source in Staged.Values)
        {
            try { source.Dispose(); } catch { /* best effort */ }
        }
        Staged.Clear();
    }
}

/// <summary>
/// The engine behind <c>update</c> and <c>outdated</c>. Works for every source type:
/// <list type="bullet">
///   <item>NuGet / npm: compares the locked version to the newest version on the feed or registry.</item>
///   <item>GitHub (global, with a recorded tree SHA): one GitHub Trees API call per repo.</item>
///   <item>Everything else (project GitHub, GitLab, git, well-known, local): stages the source
///   and compares the recorded baseline (tree SHA or content hash) to the fresh copy.</item>
/// </list>
/// Reinstalls keep each skill's scope, agents, and symlink/copy mode.
/// </summary>
public sealed class UpdateService
{
    public delegate Task<RepoTree?> TreeFetcher(string ownerRepo, string? @ref, CancellationToken cancellationToken);

    private readonly ISourceResolver _resolver;
    private readonly IInstallService _installer;
    private readonly IPackageVersionLookup _versions;
    private readonly ILogger<UpdateService> _logger;
    private readonly TreeFetcher _fetchTree;

    public UpdateService(
        ISourceResolver resolver,
        IInstallService installer,
        IPackageVersionLookup versions,
        ILogger<UpdateService> logger,
        TreeFetcher? treeFetcher = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fetchTree = treeFetcher ?? ((repo, r, ct) =>
            GitHubApi.FetchTreeAsync(repo, r, () => GitHubApi.ResolveToken(_logger), cancellationToken: ct));
    }

    // ─── Check ───────────────────────────────────────────────────────────────

    public async Task<UpdatePlan> CheckAsync(UpdateQuery query, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = new UpdatePlan(CollectTracked(query));
        try
        {
            var pending = plan.Items.Where(i => i.Status == UpdateStatus.UpToDate && i.Reason is null).ToList();

            var packages = pending.Where(i => i.SourceType is "nuget" or "npm").ToList();
            await CheckPackagesAsync(packages, query, progress, cancellationToken).ConfigureAwait(false);

            var generic = new List<UpdateItem>();
            var githubFast = new List<UpdateItem>();
            foreach (var item in pending.Except(packages))
            {
                var canUseTrees = !query.Force
                    && item.Scope == "global"
                    && item.SourceType == "github"
                    && !string.IsNullOrEmpty(item.Baseline)
                    && item.SkillPath is not null
                    && item.Source.Contains('/', StringComparison.Ordinal);
                (canUseTrees ? githubFast : generic).Add(item);
            }

            generic.AddRange(await CheckGitHubTreesAsync(githubFast, progress, cancellationToken).ConfigureAwait(false));
            await CheckByStagingAsync(plan, generic, query, progress, cancellationToken).ConfigureAwait(false);
            return plan;
        }
        catch
        {
            plan.Dispose();
            throw;
        }
    }

    private static List<UpdateItem> CollectTracked(UpdateQuery query)
    {
        var installed = Installer.List(global: null, agentFilter: null, cwd: query.Cwd)
            .GroupBy(s => $"{s.Scope}:{s.Name}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var locators = query.Targets.Select(PackageMatcher.NormalizeToLocator).ToList();
        var items = new List<UpdateItem>();

        void Add(string scope, string name, string source, string sourceType, string? sourceUrl, string? @ref, string? skillPath, string? baseline, string? originalName)
        {
            if (!PackageMatcher.MatchesByNameOrBaseId(locators, query.Targets, name, source)) return;

            installed.TryGetValue($"{scope}:{name}", out var inst);
            if (query.Agents is not null && inst is not null &&
                !inst.Agents.Any(a => query.Agents.Any(f => f.Name == a)))
            {
                return;
            }

            var item = new UpdateItem
            {
                Name = name,
                Scope = scope,
                Source = source,
                SourceType = sourceType,
                SourceUrl = sourceUrl,
                Ref = @ref,
                SkillPath = skillPath,
                Baseline = baseline,
                OriginalName = originalName,
                Installed = inst,
                Current = sourceType is "nuget" or "npm" ? PackageMatcher.ExtractVersion(source) : Short(baseline),
            };

            if (inst is null)
            {
                item.StaleLockEntry = true;
                item.Mark(UpdateStatus.Skipped, $"Tracked in the {scope} lock but not installed in this scope (stale entry)");
            }
            else if (sourceType is "nuget" or "npm" && sourceUrl?.StartsWith("file:", StringComparison.OrdinalIgnoreCase) == true)
            {
                item.Mark(UpdateStatus.Skipped, "Installed from a local package file; `add` the newer file to update");
            }
            items.Add(item);
        }

        if (query.IncludeGlobal)
        {
            foreach (var (name, e) in SkillLock.Load().Skills)
            {
                Add("global", name, e.Source, e.SourceType, e.SourceUrl, e.Ref, e.SkillPath, e.SkillFolderHash, e.OriginalName);
            }
        }
        if (query.IncludeProject)
        {
            foreach (var (name, e) in LocalLock.Load(query.Cwd).Skills)
            {
                Add("project", name, e.Source, e.SourceType, sourceUrl: null, e.Ref, e.SkillPath, e.ComputedHash, e.OriginalName);
            }
        }
        return items;
    }

    private async Task CheckPackagesAsync(
        List<UpdateItem> items, UpdateQuery query, IProgress<string>? progress, CancellationToken ct)
    {
        var pinned = query.Targets
            .Select(PackageMatcher.NormalizeToLocator)
            .Where(l => l.Version is not null)
            .GroupBy(l => l.BaseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Version!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in items.GroupBy(i => (i.SourceType, Base: PackageMatcher.ExtractBaseSource(i.Source))))
        {
            var (type, baseId) = group.Key;
            var isPinned = pinned.TryGetValue(baseId, out var pin);

            PackageVersions? available = null;
            if (!isPinned)
            {
                progress?.Report($"Checking {type} package {baseId}...");
                try
                {
                    available = await _versions.GetVersionsAsync(type, baseId, query.ResolveOptions, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Version lookup failed for {Package}", baseId);
                    foreach (var item in group) item.Mark(UpdateStatus.Failed, $"Could not check {baseId}: {ex.Message}");
                    continue;
                }
            }

            // Items in one group can sit on different versions (e.g. global 1.x, project 2.x),
            // so the target is chosen per item: --version-lock is relative to each one's version.
            foreach (var item in group)
            {
                var target = isPinned
                    ? pin
                    : PackageVersionLookup.Pick(
                        available!,
                        item.Current,
                        includePrerelease: query.IncludePrerelease || PackageVersionLookup.IsPrerelease(item.Current),
                        query.VersionLock);

                item.Latest = target;
                if (target is null)
                {
                    item.Mark(UpdateStatus.Failed, query.VersionLock == VersionLock.None
                        ? $"No versions of {baseId} found"
                        : $"No versions of {baseId} found within the current {query.VersionLock.ToString().ToLowerInvariant()} version");
                    continue;
                }

                item.InstallSpec = $"{type}:{baseId}@{target}";
                if (query.Force)
                {
                    item.Mark(UpdateStatus.UpdateAvailable, "forced");
                }
                else if (isPinned
                    ? !string.Equals(item.Current, target, StringComparison.OrdinalIgnoreCase)
                    : PackageVersionLookup.IsNewer(item.Current, target))
                {
                    item.Mark(UpdateStatus.UpdateAvailable, isPinned ? "pinned" : null);
                }
                else
                {
                    item.Mark(UpdateStatus.UpToDate);
                }
            }
        }
    }

    /// <summary>Returns the items the Trees API could not answer, for the staging fallback.</summary>
    private async Task<List<UpdateItem>> CheckGitHubTreesAsync(
        List<UpdateItem> items, IProgress<string>? progress, CancellationToken ct)
    {
        var fallback = new List<UpdateItem>();
        foreach (var group in items.GroupBy(i => (Source: i.Source.ToLowerInvariant(), i.Ref)))
        {
            var first = group.First();
            progress?.Report($"GitHub Trees API for {first.Source}...");
            RepoTree? tree;
            try
            {
                tree = await _fetchTree(first.Source, first.Ref, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Trees API failed for {Repo}", first.Source);
                tree = null;
            }

            if (tree is null)
            {
                fallback.AddRange(group);
                continue;
            }

            foreach (var item in group)
            {
                item.InstallSpec = BuildSpec(item.SourceType, item.Source, item.SourceUrl, item.Ref);
                var latest = GitHubApi.GetFolderHashFromTree(tree, item.SkillPath!);
                if (latest is null)
                {
                    item.Mark(UpdateStatus.Failed, $"Skill folder {item.SkillPath} no longer exists in {item.Source}");
                    continue;
                }
                item.Latest = Short(latest);
                item.Mark(string.Equals(latest, item.Baseline, StringComparison.Ordinal)
                    ? UpdateStatus.UpToDate
                    : UpdateStatus.UpdateAvailable);
            }
        }
        return fallback;
    }

    private async Task CheckByStagingAsync(
        UpdatePlan plan, List<UpdateItem> items, UpdateQuery query, IProgress<string>? progress, CancellationToken ct)
    {
        foreach (var item in items)
        {
            item.InstallSpec = BuildSpec(item.SourceType, item.Source, item.SourceUrl, item.Ref);
            if (item.InstallSpec is null)
            {
                item.Mark(UpdateStatus.Skipped, $"Cannot rebuild a source string for type '{item.SourceType}'; re-add it manually");
            }
        }

        foreach (var group in items.Where(i => i.InstallSpec is not null).GroupBy(i => i.InstallSpec!, StringComparer.Ordinal))
        {
            ISkillSource source;
            try
            {
                progress?.Report($"Fetching {group.Key}...");
                source = await StageAsync(plan, group.Key, query.ResolveOptions, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var item in group) item.Mark(UpdateStatus.Failed, $"Could not fetch {group.Key}: {ex.Message}");
                continue;
            }

            foreach (var item in group)
            {
                var skill = LocateSkill(source, item.SourceName, item.SkillPath);
                if (skill is null)
                {
                    item.Mark(UpdateStatus.Failed, $"Skill is no longer present in {source.DisplaySource}");
                    continue;
                }

                var fresh = item.Scope == "global"
                    ? InstallService.ComputeBaseline(source, skill, InstallService.ComputeSkillPath(source, skill))
                    : LocalLock.ComputeFolderHash(skill.Path);
                item.Latest = Short(fresh);

                if (query.Force)
                {
                    item.Mark(UpdateStatus.UpdateAvailable, "forced");
                }
                else if (string.IsNullOrEmpty(item.Baseline))
                {
                    item.Mark(UpdateStatus.Skipped, "No baseline recorded (installed before update tracking); use `update --force` to reinstall");
                }
                else if (fresh is null)
                {
                    item.Mark(UpdateStatus.Failed, "Could not compute the current hash of the source");
                }
                else
                {
                    item.Mark(string.Equals(fresh, item.Baseline, StringComparison.OrdinalIgnoreCase)
                        ? UpdateStatus.UpToDate
                        : UpdateStatus.UpdateAvailable);
                }
            }
        }
    }

    // ─── Apply ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reinstalls every <see cref="UpdateStatus.UpdateAvailable"/> item (marking it
    /// <see cref="UpdateStatus.Updated"/> or <see cref="UpdateStatus.Failed"/>) and prunes
    /// stale lock entries.
    /// </summary>
    public async Task ApplyAsync(UpdatePlan plan, UpdateQuery query, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(query);

        var work = plan.Items
            .Where(i => i.Status == UpdateStatus.UpdateAvailable && i.InstallSpec is not null && i.Installed is not null)
            .Select(i => (Item: i, Layout: InstallLayout.Detect(i.Installed!, query.Agents, query.Cwd)))
            .GroupBy(w => (
                Spec: w.Item.InstallSpec!,
                w.Item.Scope,
                w.Layout.Mode,
                Agents: string.Join(",", w.Layout.Agents.Select(a => a.Name).Order(StringComparer.Ordinal))));

        foreach (var group in work)
        {
            var groupItems = group.Select(w => w.Item).ToList();
            ISkillSource source;
            try
            {
                progress?.Report($"Updating from {group.Key.Spec}...");
                source = await StageAsync(plan, group.Key.Spec, query.ResolveOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var item in groupItems) item.Mark(UpdateStatus.Failed, $"Could not fetch {group.Key.Spec}: {ex.Message}");
                continue;
            }

            var located = new List<(UpdateItem Item, Skill Skill)>();
            foreach (var item in groupItems)
            {
                var skill = LocateSkill(source, item.SourceName, item.SkillPath);
                if (skill is null) item.Mark(UpdateStatus.Failed, $"Skill is no longer present in {source.DisplaySource}");
                else located.Add((item, skill));
            }
            if (located.Count == 0) continue;

            var request = new InstallRequest(
                SourceInput: group.Key.Spec,
                SkillFilter: [],
                Agents: group.First().Layout.Agents,
                Global: group.Key.Scope == "global",
                Mode: group.Key.Mode,
                ResolveOptions: query.ResolveOptions,
                IncludeInternal: true,
                Cwd: query.Cwd,
                Aliases: located
                    .Where(l => l.Item.OriginalName is not null)
                    .ToDictionary(l => l.Skill.Name, l => l.Item.Name, StringComparer.OrdinalIgnoreCase));

            try
            {
                var summary = await _installer
                    .InstallSkillsAsync(source, located.Select(l => l.Skill).ToList(), request, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var (item, skill) in located)
                {
                    var failures = summary.Outcomes
                        .Where(o => ReferenceEquals(o.Skill, skill) && !o.Result.Success)
                        .Select(o => $"{o.Agent.Name}: {o.Result.Error}")
                        .ToList();
                    if (failures.Count == 0) item.Mark(UpdateStatus.Updated, item.Reason);
                    else item.Mark(UpdateStatus.Failed, string.Join("; ", failures));
                }
            }
            catch (SkillsException ex)
            {
                foreach (var (item, _) in located) item.Mark(UpdateStatus.Failed, ex.Message);
            }
        }

        PruneStale(plan, query);
    }

    private static void PruneStale(UpdatePlan plan, UpdateQuery query)
    {
        var stale = plan.Items.Where(i => i.StaleLockEntry).ToList();
        if (stale.Count == 0) return;

        try
        {
            var globalNames = stale.Where(i => i.Scope == "global").Select(i => i.Name).ToList();
            if (globalNames.Count > 0)
            {
                var doc = SkillLock.Load();
                if (RemoveAll(doc.Skills, globalNames)) SkillLock.Save(doc);
            }

            var projectNames = stale.Where(i => i.Scope == "project").Select(i => i.Name).ToList();
            if (projectNames.Count > 0)
            {
                var doc = LocalLock.Load(query.Cwd);
                if (RemoveAll(doc.Skills, projectNames)) LocalLock.Save(doc, query.Cwd);
            }

            foreach (var item in stale) item.Reason = "Removed stale lock entry (not installed in this scope)";
        }
        catch (LockFileException ex)
        {
            foreach (var item in stale) item.Reason = $"Stale lock entry could not be removed: {ex.Message}";
        }
    }

    // ─── Shared helpers ──────────────────────────────────────────────────────

    private static bool RemoveAll<TValue>(SortedDictionary<string, TValue> skills, IEnumerable<string> names)
    {
        var changed = false;
        foreach (var name in names)
        {
            changed |= skills.Remove(name);
        }
        return changed;
    }

    private async Task<ISkillSource> StageAsync(UpdatePlan plan, string spec, ResolveOptions options, CancellationToken ct)
    {
        if (plan.Staged.TryGetValue(spec, out var existing)) return existing;
        var source = await _resolver.ResolveAsync(spec, options, ct).ConfigureAwait(false);
        plan.Staged[spec] = source;
        return source;
    }

    /// <summary>
    /// Finds <paramref name="name"/> in a staged source: first at the recorded
    /// <paramref name="skillPath"/>, then by a full-depth discovery scan.
    /// </summary>
    public static Skill? LocateSkill(ISkillSource source, string name, string? skillPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parseOptions = new SkillManifest.ParseOptions(IncludeInternal: true);

        if (!string.IsNullOrEmpty(skillPath))
        {
            var candidate = Path.GetFullPath(Path.Combine(source.RootPath, skillPath));
            if (PathSafety.IsPathInside(source.RootPath, candidate) && File.Exists(candidate))
            {
                var skill = SkillManifest.ParseSkillMd(candidate, parseOptions);
                if (skill is not null && string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return skill;
                }
            }
        }

        if (File.Exists(Path.Combine(source.RootPath, "SKILL.md")) || Directory.Exists(source.RootPath))
        {
            return SkillDiscovery
                .Discover(source.RootPath, subpath: null, new SkillDiscovery.DiscoverOptions(IncludeInternal: true, FullDepth: true))
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    /// <summary>
    /// Rebuilds a source string <c>add</c> understands from a lock entry. Packages keep
    /// their exact locked version (callers that want "latest" build their own spec).
    /// Null when the entry can't be turned back into a source.
    /// </summary>
    public static string? BuildSpec(string sourceType, string source, string? sourceUrl, string? @ref)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var fragment = string.IsNullOrEmpty(@ref) ? string.Empty : $"#{@ref}";
        return sourceType switch
        {
            "github" => source.Contains("://", StringComparison.Ordinal) || !source.Contains('/', StringComparison.Ordinal)
                ? (sourceUrl ?? source) + fragment
                : $"github:{source}{fragment}",
            "gitlab" => (sourceUrl ?? $"gitlab:{source}") + fragment,
            "git" => (sourceUrl ?? source) + fragment,
            "well-known" => source,
            "local" => source,
            "nuget" => source.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase) ? source : $"nuget:{source}",
            "npm" => source.StartsWith("npm:", StringComparison.OrdinalIgnoreCase) ? source : $"npm:{source}",
            _ => null,
        };
    }

    internal static string? Short(string? hash) =>
        string.IsNullOrEmpty(hash) ? null : hash.Length > 7 ? hash[..7] : hash;
}
