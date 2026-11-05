using AgentSkills.Agents;
using AgentSkills.Hosting;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using AgentSkills.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Xunit;

namespace AgentSkills.Tests;

/// <summary>
/// Exercises install scoping and the update engine against a temp project directory.
/// Global state is isolated by pointing XDG_STATE_HOME at a temp dir, so these run serially.
/// </summary>
[Collection(SerialTests.Name)]
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agentskills-cli-update-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _sourceDir;
    private readonly string? _previousXdgState;
    private readonly ServiceProvider _services;

    public UpdateServiceTests()
    {
        _project = Path.Combine(_root, "project");
        _sourceDir = Path.Combine(_root, "src", "demo-skill");
        Directory.CreateDirectory(_project);
        Directory.CreateDirectory(_sourceDir);
        WriteSkill(_sourceDir, "demo-skill", "v1");

        _previousXdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", Path.Combine(_root, "state"));

        _services = new ServiceCollection()
            .AddSkillsRuntime(LogLevel.Warning, AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(new StringWriter()) }))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", _previousXdgState);
        _services.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private static void WriteSkill(string dir, string name, string body) =>
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"""
            ---
            name: {name}
            description: Test skill.
            ---
            {body}
            """);

    private async Task InstallProjectAsync()
    {
        var installer = _services.GetRequiredService<IInstallService>();
        await installer.InstallAsync(new InstallRequest(
            SourceInput: _sourceDir,
            SkillFilter: [],
            Agents: [AgentRegistry.Get("universal")],
            Global: false,
            Mode: InstallMode.Symlink,
            ResolveOptions: ResolveOptions.Default,
            Cwd: _project), CancellationToken.None);
    }

    private UpdateQuery ProjectQuery(params string[] targets) => new(
        Targets: targets,
        IncludeGlobal: false,
        IncludeProject: true,
        Agents: null,
        IncludePrerelease: false,
        Force: false,
        ResolveOptions: ResolveOptions.Default,
        Cwd: _project);

    [Fact]
    public async Task ProjectInstall_WritesOnlyTheProjectLock()
    {
        await InstallProjectAsync();

        Assert.True(File.Exists(LocalLock.GetLockPath(_project)));
        Assert.True(LocalLock.Load(_project).Skills.ContainsKey("demo-skill"));
        Assert.False(File.Exists(SkillLock.GetLockPath()), "project installs must not write the global lock");
    }

    [Fact]
    public async Task LocalSource_DetectsChange_AndUpdateReinstalls()
    {
        await InstallProjectAsync();
        var updates = _services.GetRequiredService<UpdateService>();

        using (var clean = await updates.CheckAsync(ProjectQuery(), null, CancellationToken.None))
        {
            Assert.Equal(UpdateStatus.UpToDate, Assert.Single(clean.Items).Status);
        }

        WriteSkill(_sourceDir, "demo-skill", "v2");
        var query = ProjectQuery();
        using (var plan = await updates.CheckAsync(query, null, CancellationToken.None))
        {
            Assert.Equal(UpdateStatus.UpdateAvailable, Assert.Single(plan.Items).Status);
            await updates.ApplyAsync(plan, query, null, CancellationToken.None);
            Assert.Equal(UpdateStatus.Updated, plan.Items[0].Status);
        }

        var installed = File.ReadAllText(Path.Combine(_project, ".agents", "skills", "demo-skill", "SKILL.md"));
        Assert.Contains("v2", installed, StringComparison.Ordinal);

        using var after = await updates.CheckAsync(ProjectQuery(), null, CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, Assert.Single(after.Items).Status);
    }

    [Fact]
    public async Task Targets_FilterByNameAndSource()
    {
        await InstallProjectAsync();
        var updates = _services.GetRequiredService<UpdateService>();

        using var byName = await updates.CheckAsync(ProjectQuery("demo-skill"), null, CancellationToken.None);
        using var bySource = await updates.CheckAsync(ProjectQuery(_sourceDir), null, CancellationToken.None);
        using var other = await updates.CheckAsync(ProjectQuery("something-else"), null, CancellationToken.None);

        Assert.Single(byName.Items);
        Assert.Single(bySource.Items);
        Assert.Empty(other.Items);
    }

    [Fact]
    public async Task StaleLockEntry_IsReported_ThenPruned()
    {
        var doc = new LocalLockDocument();
        doc.Skills["ghost"] = new LocalLockEntry { Source = "acme/ghost", SourceType = "github", ComputedHash = "x" };
        LocalLock.Save(doc, _project);

        var updates = _services.GetRequiredService<UpdateService>();
        var query = ProjectQuery();
        using var plan = await updates.CheckAsync(query, null, CancellationToken.None);

        var item = Assert.Single(plan.Items);
        Assert.Equal(UpdateStatus.Skipped, item.Status);
        Assert.True(item.StaleLockEntry);

        await updates.ApplyAsync(plan, query, null, CancellationToken.None);
        Assert.Empty(LocalLock.Load(_project).Skills);
    }

    [Fact]
    public async Task PackageSource_UsesVersionLookup_AndHonorsPinnedTarget()
    {
        // A project-installed npm skill at 1.0.0 (lock entry + installed folder).
        var skillDir = Path.Combine(_project, ".agents", "skills", "pkg-skill");
        Directory.CreateDirectory(skillDir);
        WriteSkill(skillDir, "pkg-skill", "x");
        var doc = new LocalLockDocument();
        doc.Skills["pkg-skill"] = new LocalLockEntry { Source = "@acme/skills@1.0.0", SourceType = "npm", Ref = "1.0.0", ComputedHash = "h" };
        LocalLock.Save(doc, _project);

        var lookup = new FakeLookup("1.2.0");
        var updates = new UpdateService(
            _services.GetRequiredService<ISourceResolver>(),
            _services.GetRequiredService<IInstallService>(),
            lookup,
            NullLogger<UpdateService>.Instance);

        using (var latest = await updates.CheckAsync(ProjectQuery("@acme/skills"), null, CancellationToken.None))
        {
            var item = Assert.Single(latest.Items);
            Assert.Equal(UpdateStatus.UpdateAvailable, item.Status);
            Assert.Equal("1.0.0", item.Current);
            Assert.Equal("1.2.0", item.Latest);
            Assert.Equal("npm:@acme/skills@1.2.0", item.InstallSpec);
        }

        using (var pinned = await updates.CheckAsync(ProjectQuery("@acme/skills@1.1.0"), null, CancellationToken.None))
        {
            var item = Assert.Single(pinned.Items);
            Assert.Equal(UpdateStatus.UpdateAvailable, item.Status);
            Assert.Equal("npm:@acme/skills@1.1.0", item.InstallSpec);
        }
        Assert.Equal(1, lookup.Calls); // the pinned check never asks the registry

        lookup.Latest = "1.0.0";
        using var current = await updates.CheckAsync(ProjectQuery(), null, CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, Assert.Single(current.Items).Status);
    }

    [Fact]
    public async Task PackageLookupFailure_IsReportedPerSkill()
    {
        var skillDir = Path.Combine(_project, ".agents", "skills", "pkg-skill");
        Directory.CreateDirectory(skillDir);
        WriteSkill(skillDir, "pkg-skill", "x");
        var doc = new LocalLockDocument();
        doc.Skills["pkg-skill"] = new LocalLockEntry { Source = "Acme.Skills@1.0.0", SourceType = "nuget", ComputedHash = "h" };
        LocalLock.Save(doc, _project);

        var updates = new UpdateService(
            _services.GetRequiredService<ISourceResolver>(),
            _services.GetRequiredService<IInstallService>(),
            new FakeLookup(null) { Throw = true },
            NullLogger<UpdateService>.Instance);

        using var plan = await updates.CheckAsync(ProjectQuery(), null, CancellationToken.None);
        var item = Assert.Single(plan.Items);
        Assert.Equal(UpdateStatus.Failed, item.Status);
        Assert.Contains("feed down", item.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionLock_KeepsPackagesOnTheirMajor()
    {
        var skillDir = Path.Combine(_project, ".agents", "skills", "pkg-skill");
        Directory.CreateDirectory(skillDir);
        WriteSkill(skillDir, "pkg-skill", "x");
        var doc = new LocalLockDocument();
        doc.Skills["pkg-skill"] = new LocalLockEntry { Source = "Acme.Skills@1.4.0", SourceType = "nuget", ComputedHash = "h" };
        LocalLock.Save(doc, _project);

        var lookup = new FakeLookup(null) { Versions = ["1.4.0", "1.4.3", "1.9.0", "2.0.0", "2.1.0-beta"] };
        var updates = new UpdateService(
            _services.GetRequiredService<ISourceResolver>(),
            _services.GetRequiredService<IInstallService>(),
            lookup,
            NullLogger<UpdateService>.Instance);

        async Task<string?> LatestWith(VersionLock versionLock)
        {
            using var plan = await updates.CheckAsync(ProjectQuery() with { VersionLock = versionLock }, null, CancellationToken.None);
            return Assert.Single(plan.Items).Latest;
        }

        Assert.Equal("2.0.0", await LatestWith(VersionLock.None));
        Assert.Equal("1.9.0", await LatestWith(VersionLock.Major));
        Assert.Equal("1.4.3", await LatestWith(VersionLock.Minor));
    }

    [Fact]
    public void DetectMode_SymlinkVsCopy()
    {
        var claude = AgentRegistry.Get("claude-code");
        var canonical = Path.Combine(_project, ".agents", "skills", "s");
        Directory.CreateDirectory(canonical);
        var agentDir = Path.Combine(_project, ".claude", "skills");
        Directory.CreateDirectory(agentDir);

        Directory.CreateSymbolicLink(Path.Combine(agentDir, "s"), canonical);
        Assert.Equal(InstallMode.Symlink, InstallLayout.DetectMode("s", [claude], global: false, _project));

        Directory.Delete(Path.Combine(agentDir, "s"));
        Directory.CreateDirectory(Path.Combine(agentDir, "s"));
        Assert.Equal(InstallMode.Copy, InstallLayout.DetectMode("s", [claude], global: false, _project));

        Assert.Equal(InstallMode.Symlink, InstallLayout.DetectMode("s", [AgentRegistry.Get("universal")], global: false, _project));
    }

    [Fact]
    public void LocateSkill_PrefersRecordedPath_ThenScans()
    {
        var root = Path.Combine(_root, "repo");
        var deep = Path.Combine(root, "a", "b", "c", "d", "e", "f", "deep-skill");
        Directory.CreateDirectory(deep);
        WriteSkill(deep, "deep-skill", "x");
        var source = new FakeSource(root);

        Assert.NotNull(UpdateService.LocateSkill(source, "deep-skill", "a/b/c/d/e/f/deep-skill/SKILL.md"));
        Assert.Null(UpdateService.LocateSkill(source, "deep-skill", "../escape/SKILL.md"));
        Assert.Null(UpdateService.LocateSkill(source, "missing", null));
    }

    private sealed class FakeLookup(string? latest) : IPackageVersionLookup
    {
        public string? Latest { get; set; } = latest;
        public IReadOnlyList<string>? Versions { get; set; }
        public bool Throw { get; init; }
        public int Calls { get; private set; }

        public Task<PackageVersions> GetVersionsAsync(string sourceType, string packageId, ResolveOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("feed down");
            var versions = Versions ?? (Latest is null ? [] : [Latest]);
            return Task.FromResult(new PackageVersions(versions, sourceType == "npm" ? Latest : null));
        }
    }

    private sealed class FakeSource(string root) : ISkillSource
    {
        public ParsedSource Parsed { get; } = new(SourceType.Local, root, LocalPath: root);
        public string RootPath { get; } = root;
        public string? Subpath => null;
        public string DisplaySource => RootPath;
        public string SourceTypeLabel => "local";
        public string? SourceUrl => RootPath;
        public string? Reference => null;
        public void Dispose() { }
    }
}
