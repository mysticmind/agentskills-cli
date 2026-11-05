using AgentSkills.Hosting;
using AgentSkills.Install;
using Xunit;

namespace AgentSkills.Tests;

public class VersionInfoTests
{
    [Theory]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("1.1.0+1c6acad0123456789", "1.1.0")]
    [InlineData("2.0.0-beta.1+abc", "2.0.0-beta.1")]
    public void StripMetadata(string input, string expected) =>
        Assert.Equal(expected, VersionInfo.StripMetadata(input));

    [Theory]
    [InlineData("1.1.0", null)]
    [InlineData("1.1.0+", null)]
    [InlineData("1.1.0+1c6acad0123456789", "1c6acad")]
    [InlineData("1.1.0+abc", "abc")]
    public void ExtractCommit(string input, string? expected) =>
        Assert.Equal(expected, VersionInfo.ExtractCommit(input));

    [Fact]
    public void Version_HasNoBuildMetadata() =>
        Assert.DoesNotContain("+", VersionInfo.Version, StringComparison.Ordinal);
}

public class PackageVersionLookupTests
{
    private static readonly PackageVersions NuGet = new(["1.0.0", "1.2.0", "1.3.0-beta.1", "0.9.0", "2.0.0", "2.1.0"]);

    private static string? Pick(PackageVersions v, string? current, bool pre = false, VersionLock lck = VersionLock.None) =>
        PackageVersionLookup.Pick(v, current, pre, lck);

    [Fact]
    public void StableByDefault() => Assert.Equal("2.1.0", Pick(NuGet, "1.0.0"));

    [Fact]
    public void PrereleaseWhenAsked() =>
        Assert.Equal("1.3.0-beta.1", Pick(new(["1.2.0", "1.3.0-beta.1"]), "1.2.0", pre: true));

    [Fact]
    public void NoVersions_ReturnsNull() => Assert.Null(Pick(new([]), "1.0.0"));

    [Fact]
    public void OnlyPrereleases_WithoutFlag_ReturnsNull() => Assert.Null(Pick(new(["1.0.0-alpha"]), null));

    [Theory]
    [InlineData("1.0.0", VersionLock.Major, "1.2.0")]
    [InlineData("1.0.0", VersionLock.Minor, "1.0.0")]
    [InlineData("1.2.0", VersionLock.Minor, "1.2.0")]
    [InlineData("2.0.0", VersionLock.Major, "2.1.0")]
    [InlineData("3.0.0", VersionLock.Major, null)]
    [InlineData("garbage", VersionLock.Major, "2.1.0")] // unparseable current: lock ignored
    public void VersionLock_StaysInRange(string current, VersionLock lck, string? expected) =>
        Assert.Equal(expected, Pick(NuGet, current, lck: lck));

    [Fact]
    public void VersionLock_Major_WithPrerelease() =>
        Assert.Equal("1.3.0-beta.1", Pick(NuGet, "1.0.0", pre: true, lck: VersionLock.Major));

    [Fact]
    public void Npm_UsesDistTagLatest() =>
        // dist-tags.latest wins even when a higher version exists (npm semantics).
        Assert.Equal("1.2.0", Pick(new(["1.0.0", "1.2.0", "2.0.0"], "1.2.0"), "1.0.0"));

    [Fact]
    public void Npm_Prerelease_HighestSemVer() =>
        Assert.Equal("2.0.0-rc.1", Pick(new(["1.2.0", "2.0.0-rc.1"], "1.2.0"), "1.0.0", pre: true));

    [Fact]
    public void Npm_NoDistTag_FallsBackToHighestStable() =>
        Assert.Equal("1.5.0", Pick(new(["1.0.0", "1.5.0", "2.0.0-alpha"]), "1.0.0"));

    [Fact]
    public void Npm_VersionLock_IgnoresDistTagOutsideRange() =>
        Assert.Equal("1.9.0", Pick(new(["1.0.0", "1.9.0", "2.0.0"], "2.0.0"), "1.0.0", lck: VersionLock.Major));

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.1.0", false)]
    [InlineData("1.0.0-beta", "1.0.0", true)]
    [InlineData(null, "1.0.0", true)]
    [InlineData("1.0.0", null, false)]
    public void IsNewer(string? current, string? candidate, bool expected) =>
        Assert.Equal(expected, PackageVersionLookup.IsNewer(current, candidate));

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0-beta.2", true)]
    [InlineData(null, false)]
    [InlineData("not-a-version", false)]
    public void IsPrerelease(string? version, bool expected) =>
        Assert.Equal(expected, PackageVersionLookup.IsPrerelease(version));
}

public class BuildSpecTests
{
    [Theory]
    [InlineData("github", "acme/skills", null, null, "github:acme/skills")]
    [InlineData("github", "acme/skills", null, "v2", "github:acme/skills#v2")]
    [InlineData("gitlab", "acme/sub/skills", null, "main", "gitlab:acme/sub/skills#main")]
    [InlineData("gitlab", "acme/skills", "https://gitlab.example.com/acme/skills.git", null, "https://gitlab.example.com/acme/skills.git")]
    [InlineData("git", "https://git.example.com/x.git", null, "dev", "https://git.example.com/x.git#dev")]
    [InlineData("nuget", "Acme.Skills@1.2.0", null, "1.2.0", "nuget:Acme.Skills@1.2.0")]
    [InlineData("npm", "@acme/skills@1.0.0", null, "1.0.0", "npm:@acme/skills@1.0.0")]
    [InlineData("local", "/tmp/skill", null, null, "/tmp/skill")]
    [InlineData("well-known", "https://example.com", null, null, "https://example.com")]
    [InlineData("mystery", "x", null, null, null)]
    public void BuildSpec(string type, string source, string? url, string? @ref, string? expected) =>
        Assert.Equal(expected, UpdateService.BuildSpec(type, source, url, @ref));

    [Fact]
    public void GitHubSpec_RoundTripsThroughParser()
    {
        var parsed = Sources.SourceParser.Parse(UpdateService.BuildSpec("github", "acme/skills", null, "v2")!);
        Assert.Equal(Sources.SourceType.GitHub, parsed.Type);
        Assert.Equal("v2", parsed.Ref);
    }

    [Fact]
    public void NpmSpec_RoundTripsThroughParser()
    {
        var parsed = Sources.SourceParser.Parse(UpdateService.BuildSpec("npm", "@acme/skills@1.0.0", null, null)!);
        Assert.Equal(Sources.SourceType.Npm, parsed.Type);
        Assert.Equal("@acme/skills", parsed.PackageId);
        Assert.Equal("1.0.0", parsed.PackageVersion);
    }
}

public class MatchesByNameOrBaseIdTests
{
    private static bool Match(string[] args, string name, string source) =>
        PackageMatcher.MatchesByNameOrBaseId(args.Select(PackageMatcher.NormalizeToLocator).ToList(), args, name, source);

    [Fact]
    public void NoArgs_MatchesEverything() => Assert.True(Match([], "x", "acme/skills"));

    [Fact]
    public void BySkillName() => Assert.True(Match(["Hello-Skill"], "hello-skill", "acme/skills"));

    [Fact]
    public void ByNuGetPackage_IgnoresInstalledVersion() =>
        Assert.True(Match(["Acme.Skills"], "a", "Acme.Skills@1.0.0"));

    [Fact]
    public void PinnedVersion_StillMatchesOtherInstalledVersion() =>
        // For update the @version is the target, not a filter.
        Assert.True(Match(["Acme.Skills@2.0.0"], "a", "Acme.Skills@1.0.0"));

    [Fact]
    public void ByScopedNpmPackage() => Assert.True(Match(["@acme/skills"], "a", "@acme/skills@1.4.0"));

    [Fact]
    public void ByGitHubUrl() => Assert.True(Match(["https://github.com/acme/skills"], "a", "acme/skills"));

    [Fact]
    public void DifferentPackage_NoMatch() => Assert.False(Match(["Acme.Other"], "a", "Acme.Skills@1.0.0"));
}

public sealed class NuGetLayoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agentskills-cli-nupkg-" + Guid.NewGuid().ToString("N"));

    public NuGetLayoutTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private void Make(string relative) => Directory.CreateDirectory(Path.Combine(_dir, relative));

    [Fact]
    public void RootSkillsFolder_IsUsed()
    {
        Make("skills/contoso-widgets-usage");
        Assert.Equal("skills", Sources.NuGetSource.FindSkillsSubpath(_dir));
    }

    [Fact]
    public void RootSkillsFolder_MatchedCaseInsensitively()
    {
        Make("Skills/x");
        Assert.Equal("Skills", Sources.NuGetSource.FindSkillsSubpath(_dir));
    }

    [Fact]
    public void ContentFilesLayout_StillRead()
    {
        Make("contentFiles/any/any/skills/x");
        Assert.Equal(Path.Combine("contentFiles", "any", "any", "skills"), Sources.NuGetSource.FindSkillsSubpath(_dir));
    }

    [Fact]
    public void BothLayouts_RootWins()
    {
        Make("skills/x");
        Make("contentFiles/any/any/skills/y");
        Assert.Equal("skills", Sources.NuGetSource.FindSkillsSubpath(_dir));
    }

    [Fact]
    public void NoSkillsFolder_ReturnsNull() => Assert.Null(Sources.NuGetSource.FindSkillsSubpath(_dir));
}
