using AgentSkills.Commands;
using AgentSkills.Install;
using AgentSkills.Sources;
using NuGet.Configuration;
using Xunit;

namespace AgentSkills.Tests;

public sealed class NuGetFeedConfigTests : IDisposable
{
    private readonly string _project = Path.Combine(Path.GetTempPath(), "agentskills-cli-feeds-" + Guid.NewGuid().ToString("N"));

    public NuGetFeedConfigTests() => Directory.CreateDirectory(_project);

    public void Dispose()
    {
        try { Directory.Delete(_project, recursive: true); } catch { /* ignore */ }
    }

    private string ProjectSkillsConfig => NuGetFeedConfig.ProjectPath(_project);

    private void WriteProjectSkillsConfig(string sources)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectSkillsConfig)!);
        File.WriteAllText(ProjectSkillsConfig, $"<?xml version=\"1.0\"?>\n<configuration><packageSources>{sources}</packageSources></configuration>\n");
    }

    [Fact]
    public void ProjectSkillsFeed_IsTriedFirst()
    {
        WriteProjectSkillsConfig("<add key=\"skills\" value=\"https://skills.example.com/v3/index.json\" />");

        var feeds = NuGetFeedConfig.List(_project);

        Assert.Equal("skills", feeds[0].Name);
        Assert.Equal(Path.GetFullPath(ProjectSkillsConfig), Path.GetFullPath(feeds[0].DefinedIn!));
    }

    [Fact]
    public void ClearInSkillsFile_HidesEveryOtherFeed()
    {
        WriteProjectSkillsConfig("<clear /><add key=\"only\" value=\"https://only.example.com/v3/index.json\" />");

        var feeds = NuGetFeedConfig.List(_project);

        Assert.Equal("only", Assert.Single(feeds).Name);
    }

    [Fact]
    public void ProjectNuGetConfig_IsRead()
    {
        // The normal project NuGet.config was ignored before 1.1.0 (settings loaded with no root).
        File.WriteAllText(Path.Combine(_project, "NuGet.config"),
            "<?xml version=\"1.0\"?>\n<configuration><packageSources><add key=\"team\" value=\"https://team.example.com/v3/index.json\" /></packageSources></configuration>\n");

        Assert.Contains(NuGetFeedConfig.List(_project), f => f.Name == "team");
    }

    [Fact]
    public void AddAndRemoveFeed_RoundTrip()
    {
        NuGetFeedConfig.AddFeed(ProjectSkillsConfig, "contoso", "https://contoso.example.com/v3/index.json");
        NuGetFeedConfig.AddFeed(ProjectSkillsConfig, "contoso", "https://contoso2.example.com/v3/index.json");

        var feed = Assert.Single(NuGetFeedConfig.List(_project), f => f.Name == "contoso");
        Assert.Equal("https://contoso2.example.com/v3/index.json", feed.Source);

        Assert.True(NuGetFeedConfig.RemoveFeed(ProjectSkillsConfig, "contoso"));
        Assert.False(NuGetFeedConfig.RemoveFeed(ProjectSkillsConfig, "contoso"));
        Assert.DoesNotContain(NuGetFeedConfig.List(_project), f => f.Name == "contoso");
    }

    [Fact]
    public void Credentials_AreStoredAndRestrictedToOwner()
    {
        NuGetFeedConfig.AddFeed(ProjectSkillsConfig, "private", "https://private.example.com/v3/index.json",
            username: "me", password: "secret", storePasswordInClearText: true);

        var settings = Settings.LoadSpecificSettings(Path.GetDirectoryName(ProjectSkillsConfig)!, NuGetFeedConfig.FileName);
        var source = new PackageSourceProvider(settings).GetPackageSourceByName("private");
        Assert.Equal("me", source!.Credentials!.Username);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ProjectSkillsConfig));
        }
    }

    [Fact]
    public void UsernameWithoutPassword_Throws() =>
        Assert.Throws<UserInputException>(() =>
            NuGetFeedConfig.AddFeed(ProjectSkillsConfig, "x", "https://x.example.com/v3/index.json", username: "me"));

    [Fact]
    public void Override_ByName_KeepsConfiguredFeed()
    {
        WriteProjectSkillsConfig("<add key=\"contoso\" value=\"https://contoso.example.com/v3/index.json\" />");

        var feeds = NuGetFeedConfig.ResolveFeeds("contoso", cwd: _project);

        Assert.Equal("https://contoso.example.com/v3/index.json", Assert.Single(feeds).Source);
    }

    [Fact]
    public void Override_UnknownUrl_IsUsedAlone() =>
        Assert.Equal("https://elsewhere.example.com/v3/index.json",
            Assert.Single(NuGetFeedConfig.ResolveFeeds("https://elsewhere.example.com/v3/index.json", cwd: _project)).Source);

    [Fact]
    public void Preferred_MovesToFront_WithoutHidingOthers()
    {
        WriteProjectSkillsConfig(
            "<clear /><add key=\"a\" value=\"https://a.example.com/v3/index.json\" /><add key=\"b\" value=\"https://b.example.com/v3/index.json\" />");

        var feeds = NuGetFeedConfig.ResolveFeeds(null, preferredSource: "https://b.example.com/v3/index.json/", cwd: _project);

        Assert.Equal(["b", "a"], feeds.Select(f => f.Name));
    }

    [Fact]
    public void FeedAdd_LocalFolder_IsStoredRelativeToTheConfigFile()
    {
        var feedDir = Path.Combine(_project, "local-feed");
        Directory.CreateDirectory(feedDir);

        var (url, name) = FeedAddCommand.Normalize(feedDir, null, ProjectSkillsConfig);

        Assert.Equal(Path.Combine("..", "local-feed"), url);
        Assert.Equal("local-feed", name);
    }

    [Fact]
    public void FeedAdd_UrlName_DefaultsToHost() =>
        Assert.Equal("pkgs.contoso.com", FeedAddCommand.Normalize("https://pkgs.contoso.com/v3/index.json", null, ProjectSkillsConfig).Name);

    [Fact]
    public void FeedAdd_NotAUrlOrFolder_Throws() =>
        Assert.Throws<UserInputException>(() => FeedAddCommand.Normalize("not-a-thing", null, ProjectSkillsConfig));
}

public sealed class NpmSkillsConfigTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "agentskills-cli-npm-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public NpmSkillsConfigTests()
    {
        _project = Path.Combine(_home, "project");
        Directory.CreateDirectory(_project);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void SkillsFiles_OverrideNormalNpmrc_ProjectWinsOverUser()
    {
        File.WriteAllText(Path.Combine(_home, ".npmrc"), "registry=https://user-npmrc.example/\n");
        File.WriteAllText(Path.Combine(_project, ".npmrc"), "@acme:registry=https://project-npmrc.example/\n");
        NpmRegistry.WriteKeys(NpmRegistry.UserSkillsPath(_home), new Dictionary<string, string?> { ["registry"] = "https://user-skills.example/" });
        NpmRegistry.WriteKeys(NpmRegistry.ProjectSkillsPath(_project), new Dictionary<string, string?> { ["@acme:registry"] = "https://project-skills.example/" });

        var npm = NpmRegistry.Load(cwd: _project, home: _home);

        Assert.Equal("https://user-skills.example/", npm.DefaultRegistryUrl);
        Assert.Equal("https://project-skills.example/", npm.ResolveRegistry("@acme/x"));
        Assert.Equal(NpmRegistry.ProjectSkillsPath(_project), npm.Origins["@acme:registry"]);
    }

    [Fact]
    public void WriteKeys_ReplacesInPlace_RemovesAndAppends_KeepsComments()
    {
        var path = Path.Combine(_home, "test.npmrc");
        File.WriteAllText(path, "# team registry\nregistry=https://old/\n@a:registry=https://a/\n@a:registry=https://dup/\nkeep=1\n");

        NpmRegistry.WriteKeys(path, new Dictionary<string, string?>
        {
            ["registry"] = "https://new/",
            ["@a:registry"] = null,
            ["@b:registry"] = "https://b/",
        });

        Assert.Equal("# team registry\nregistry=https://new/\nkeep=1\n@b:registry=https://b/\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("https://npm.contoso.com/team", "//npm.contoso.com/team/")]
    [InlineData("https://npm.contoso.com:8443/", "//npm.contoso.com:8443/")]
    [InlineData("https://registry.npmjs.org/", "//registry.npmjs.org/")]
    public void AuthKeyPrefix(string url, string expected) => Assert.Equal(expected, NpmRegistry.AuthKeyPrefix(url));
}

public class SourceUrlTrackingTests
{
    [Fact]
    public void ProjectRelative_InsideProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");
        Assert.Equal("feeds/local", InstallService.ProjectRelative(Path.Combine(root, "feeds", "local"), root));
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3/index.json")]
    [InlineData("file:///tmp/pkg.nupkg")]
    [InlineData(null)]
    public void ProjectRelative_KeepsUrls(string? url) => Assert.Equal(url, InstallService.ProjectRelative(url, Path.GetTempPath()));

    [Fact]
    public void ProjectRelative_KeepsPathsOutsideProject()
    {
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", "elsewhere"));
        Assert.Equal(outside, InstallService.ProjectRelative(outside, Path.Combine(Path.GetTempPath(), "proj")));
    }

    [Fact]
    public void PreferredFeed_OnlyForNuGet_AndNotOverridingExplicitSource()
    {
        var url = "https://feed.example.com/v3/index.json";
        Assert.Equal(url, UpdateService.WithPreferredFeed(ResolveOptions.Default, "nuget", url).PreferredNuGetSource);
        Assert.Null(UpdateService.WithPreferredFeed(ResolveOptions.Default, "npm", url).PreferredNuGetSource);
        Assert.Null(UpdateService.WithPreferredFeed(ResolveOptions.Default, "nuget", "file:///x.nupkg").PreferredNuGetSource);
        Assert.Null(UpdateService.WithPreferredFeed(new ResolveOptions(NuGetSource: "other"), "nuget", url).PreferredNuGetSource);
    }

    [Fact]
    public void PreferredFeed_RelativeFolder_ResolvedAgainstCurrentDirectory() =>
        Assert.Equal(Path.GetFullPath("feeds/local"), UpdateService.WithPreferredFeed(ResolveOptions.Default, "nuget", "feeds/local").PreferredNuGetSource);

    [Fact]
    public void RestoreSpec_UsesLocalPackageFileWhenItExists()
    {
        var file = Path.Combine(Path.GetTempPath(), $"agentskills-{Guid.NewGuid():N}.nupkg");
        File.WriteAllText(file, "x");
        try
        {
            var entry = new LocalLockEntry { Source = "Acme.Skills@1.0.0", SourceType = "nuget", SourceUrl = new Uri(file).AbsoluteUri };
            Assert.Equal(file, RestoreCommand.RestoreSpec(entry));

            File.Delete(file);
            Assert.Equal("nuget:Acme.Skills@1.0.0", RestoreCommand.RestoreSpec(entry));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
