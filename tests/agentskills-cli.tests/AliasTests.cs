using AgentSkills.Agents;
using AgentSkills.Hosting;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using AgentSkills.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Xunit;

namespace AgentSkills.Tests;

public class SkillNameRewriteTests
{
    [Fact]
    public void WithName_ReplacesTopLevelName_KeepsEverythingElse()
    {
        const string raw = "---\nname: greeter\ndescription: Says hi.\nmetadata:\n  name: nested\n---\nBody with name: greeter\n";

        var result = SkillManifest.WithName(raw, "greeter-contoso");

        Assert.Equal("---\nname: greeter-contoso\ndescription: Says hi.\nmetadata:\n  name: nested\n---\nBody with name: greeter\n", result);
    }

    [Fact]
    public void WithName_PreservesCrlf()
    {
        const string raw = "---\r\nname: a\r\ndescription: d\r\n---\r\nbody\r\n";
        Assert.Equal("---\r\nname: b\r\ndescription: d\r\n---\r\nbody\r\n", SkillManifest.WithName(raw, "b"));
    }

    [Fact]
    public void WithName_QuotedValue_IsReplaced()
    {
        var result = SkillManifest.WithName("---\nname: \"greeter\"\ndescription: d\n---\n", "x");
        Assert.Equal("x", SkillManifest.ParseFrontmatter(result).Data["name"]);
    }

    [Fact]
    public void WithName_NoNameKey_AddsOne()
    {
        var result = SkillManifest.WithName("---\ndescription: d\n---\nbody", "x");
        Assert.Equal("x", SkillManifest.ParseFrontmatter(result).Data["name"]);
        Assert.Equal("d", SkillManifest.ParseFrontmatter(result).Data["description"]);
    }
}

public class AliasParsingTests
{
    [Fact]
    public void BareAlias_WithOneSkill() =>
        Assert.Equal("greeter-b", SkillConflicts.ParseAliases(["greeter-b"], ["greeter"])["greeter"]);

    [Fact]
    public void KeyValue_IsCaseInsensitiveOnSkill() =>
        Assert.Equal("x-b", SkillConflicts.ParseAliases(["X=x-b"], ["x", "y"])["x"]);

    [Fact]
    public void BareAlias_WithSeveralSkills_Throws() =>
        Assert.Throws<UserInputException>(() => SkillConflicts.ParseAliases(["z"], ["x", "y"]));

    [Fact]
    public void UnknownSkill_Throws() =>
        Assert.Throws<UserInputException>(() => SkillConflicts.ParseAliases(["nope=z"], ["x"]));

    [Theory]
    [InlineData("Bad Name")]
    [InlineData("UPPER")]
    [InlineData("-leading")]
    [InlineData("")]
    public void InvalidAlias_Throws(string alias) =>
        Assert.Throws<UserInputException>(() => SkillConflicts.ParseAliases([$"x={alias}"], ["x"]));

    [Theory]
    [InlineData("Contoso.AgentSkills@1.2.0", "greeter-contoso")]
    [InlineData("@acme/skills@1.0.0", "greeter-acme")]
    [InlineData("acme/skills", "greeter-acme")]
    public void SuggestAlias_UsesOwnerScopeOrPackagePrefix(string source, string expected) =>
        Assert.Equal(expected, SkillConflicts.SuggestAlias("greeter", source, _ => false));

    [Fact]
    public void SuggestAlias_AvoidsTakenNames() =>
        Assert.Equal("greeter-acme-2", SkillConflicts.SuggestAlias("greeter", "acme/skills", n => n == "greeter-acme"));
}

/// <summary>Install-time conflict detection and aliasing against a temp project.</summary>
[Collection(SerialTests.Name)]
public sealed class AliasInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agentskills-cli-alias-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _acme;
    private readonly string _contoso;
    private readonly string? _previousXdgState;
    private readonly ServiceProvider _services;

    public AliasInstallTests()
    {
        _project = Path.Combine(_root, "project");
        _acme = Path.Combine(_root, "acme", "greeter");
        _contoso = Path.Combine(_root, "contoso", "greeter");
        Directory.CreateDirectory(_project);
        Directory.CreateDirectory(_acme);
        Directory.CreateDirectory(_contoso);
        WriteSkill(_acme, "acme");
        WriteSkill(_contoso, "contoso");

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

    private static void WriteSkill(string dir, string body) =>
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: greeter\ndescription: Greets.\n---\n{body}\n");

    private Task<InstallSummary> InstallAsync(string source, IReadOnlyDictionary<string, string>? aliases = null, bool replace = false) =>
        _services.GetRequiredService<IInstallService>().InstallAsync(new InstallRequest(
            SourceInput: source,
            SkillFilter: [],
            Agents: [AgentRegistry.Get("universal")],
            Global: false,
            Mode: InstallMode.Symlink,
            ResolveOptions: ResolveOptions.Default,
            Cwd: _project,
            Aliases: aliases,
            Replace: replace), CancellationToken.None);

    private string Installed(string name) =>
        File.ReadAllText(Path.Combine(_project, ".agents", "skills", name, "SKILL.md"));

    private static Dictionary<string, string> Alias(string from, string to) =>
        new(StringComparer.OrdinalIgnoreCase) { [from] = to };

    [Fact]
    public async Task DifferentSource_SameName_Throws_AndInstallsNothing()
    {
        await InstallAsync(_acme);

        var ex = await Assert.ThrowsAsync<SkillConflictException>(() => InstallAsync(_contoso));

        var conflict = Assert.Single(ex.Conflicts);
        Assert.Equal("greeter", conflict.SkillName);
        Assert.Equal("project", conflict.Scope);
        Assert.Equal(_acme, conflict.ExistingSource);
        Assert.Contains("acme", Installed("greeter"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameSource_Reinstall_IsNotAConflict()
    {
        await InstallAsync(_acme);
        WriteSkill(_acme, "acme v2");

        await InstallAsync(_acme);

        Assert.Contains("acme v2", Installed("greeter"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntrackedSkillOnDisk_IsAConflict()
    {
        var manual = Path.Combine(_project, ".agents", "skills", "greeter");
        Directory.CreateDirectory(manual);
        WriteSkill(manual, "hand-made");

        var ex = await Assert.ThrowsAsync<SkillConflictException>(() => InstallAsync(_acme));
        Assert.Null(Assert.Single(ex.Conflicts).ExistingSource);
    }

    [Fact]
    public async Task Alias_InstallsBoth_RewritesName_AndRecordsOriginal()
    {
        await InstallAsync(_acme);

        var summary = await InstallAsync(_contoso, Alias("greeter", "greeter-contoso"));

        Assert.Equal("greeter-contoso", summary.Outcomes[0].Name);
        Assert.Contains("acme", Installed("greeter"), StringComparison.Ordinal);
        var aliased = Installed("greeter-contoso");
        Assert.Contains("contoso", aliased, StringComparison.Ordinal);
        Assert.Equal("greeter-contoso", SkillManifest.ParseFrontmatter(aliased).Data["name"]);

        // The source is never modified.
        Assert.Equal("greeter", SkillManifest.ParseFrontmatter(File.ReadAllText(Path.Combine(_contoso, "SKILL.md"))).Data["name"]);

        var lockDoc = LocalLock.Load(_project);
        Assert.Null(lockDoc.Skills["greeter"].OriginalName);
        Assert.Equal("greeter", lockDoc.Skills["greeter-contoso"].OriginalName);
        Assert.Equal(_contoso, lockDoc.Skills["greeter-contoso"].Source);

        var listed = Installer.List(global: false, cwd: _project).Select(s => s.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["greeter", "greeter-contoso"], listed);
    }

    [Fact]
    public async Task Alias_ThatIsAlsoTaken_IsAConflict()
    {
        await InstallAsync(_acme, Alias("greeter", "greeter-x"));
        var ex = await Assert.ThrowsAsync<SkillConflictException>(() => InstallAsync(_contoso, Alias("greeter", "greeter-x")));
        Assert.Equal("greeter-x", Assert.Single(ex.Conflicts).InstallName);
    }

    [Fact]
    public async Task Replace_OverwritesTheOtherSource()
    {
        await InstallAsync(_acme);

        await InstallAsync(_contoso, replace: true);

        Assert.Contains("contoso", Installed("greeter"), StringComparison.Ordinal);
        Assert.Equal(_contoso, LocalLock.Load(_project).Skills["greeter"].Source);
    }

    [Fact]
    public async Task RememberedAliases_FindsEarlierAliasForSameSource()
    {
        await InstallAsync(_acme);
        await InstallAsync(_contoso, Alias("greeter", "greeter-contoso"));

        var remembered = SkillConflicts.RememberedAliases(_contoso, ["greeter"], global: false, _project);
        var forOther = SkillConflicts.RememberedAliases(_acme, ["greeter"], global: false, _project);

        Assert.Equal("greeter-contoso", remembered["greeter"]);
        Assert.Empty(forOther);
    }

    [Fact]
    public async Task Update_KeepsTheAlias()
    {
        await InstallAsync(_acme);
        await InstallAsync(_contoso, Alias("greeter", "greeter-contoso"));
        WriteSkill(_contoso, "contoso v2");

        var updates = _services.GetRequiredService<UpdateService>();
        var query = new UpdateQuery(["greeter-contoso"], IncludeGlobal: false, IncludeProject: true, Agents: null,
            IncludePrerelease: false, Force: false, ResolveOptions.Default, Cwd: _project);
        using var plan = await updates.CheckAsync(query, null, CancellationToken.None);
        var item = Assert.Single(plan.Items);
        Assert.Equal(UpdateStatus.UpdateAvailable, item.Status);
        Assert.Equal("greeter", item.OriginalName);

        await updates.ApplyAsync(plan, query, null, CancellationToken.None);

        Assert.Equal(UpdateStatus.Updated, item.Status);
        var updated = Installed("greeter-contoso");
        Assert.Contains("contoso v2", updated, StringComparison.Ordinal);
        Assert.Equal("greeter-contoso", SkillManifest.ParseFrontmatter(updated).Data["name"]);
        Assert.Contains("acme", Installed("greeter"), StringComparison.Ordinal);
        Assert.Equal("greeter", LocalLock.Load(_project).Skills["greeter-contoso"].OriginalName);
    }

    [Fact]
    public async Task UnknownLockFields_SurviveARewrite()
    {
        await InstallAsync(_acme);
        var path = LocalLock.GetLockPath(_project);
        File.WriteAllText(path, File.ReadAllText(path).Replace(
            "\"sourceType\": \"local\"", "\"sourceType\": \"local\", \"futureField\": 42", StringComparison.Ordinal));

        await InstallAsync(_acme);

        Assert.Contains("\"futureField\": 42", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
