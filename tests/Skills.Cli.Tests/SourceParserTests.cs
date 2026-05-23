using Skills.Sources;
using Xunit;

namespace Skills.Tests;

public class SourceParserTests
{
    [Theory]
    [InlineData("./foo")]
    [InlineData("../foo")]
    [InlineData(".")]
    [InlineData("..")]
    public void LocalRelative(string input)
    {
        var parsed = SourceParser.Parse(input);
        Assert.Equal(SourceType.Local, parsed.Type);
        Assert.NotNull(parsed.LocalPath);
    }

    [Fact]
    public void LocalAbsolute()
    {
        var path = OperatingSystem.IsWindows() ? "C:\\tmp\\skill" : "/tmp/skill";
        var parsed = SourceParser.Parse(path);
        Assert.Equal(SourceType.Local, parsed.Type);
    }

    [Theory]
    [InlineData("vercel-labs/agent-skills", "https://github.com/vercel-labs/agent-skills.git", null, null, null)]
    [InlineData("vercel-labs/agent-skills/skills/foo", "https://github.com/vercel-labs/agent-skills.git", "skills/foo", null, null)]
    [InlineData("vercel-labs/agent-skills#main", "https://github.com/vercel-labs/agent-skills.git", null, "main", null)]
    [InlineData("vercel-labs/agent-skills@web-design", "https://github.com/vercel-labs/agent-skills.git", null, null, "web-design")]
    [InlineData("vercel-labs/agent-skills#main@web-design", "https://github.com/vercel-labs/agent-skills.git", null, "main", "web-design")]
    public void GithubShorthand(string input, string expectedUrl, string? subpath, string? @ref, string? filter)
    {
        var parsed = SourceParser.Parse(input);
        Assert.Equal(SourceType.GitHub, parsed.Type);
        Assert.Equal(expectedUrl, parsed.Url);
        Assert.Equal(subpath, parsed.Subpath);
        Assert.Equal(@ref, parsed.Ref);
        Assert.Equal(filter, parsed.SkillFilter);
    }

    [Fact]
    public void GithubTreeUrlWithSubpath()
    {
        var parsed = SourceParser.Parse("https://github.com/vercel-labs/agent-skills/tree/main/skills/foo");
        Assert.Equal(SourceType.GitHub, parsed.Type);
        Assert.Equal("https://github.com/vercel-labs/agent-skills.git", parsed.Url);
        Assert.Equal("main", parsed.Ref);
        Assert.Equal("skills/foo", parsed.Subpath);
    }

    [Fact]
    public void GitlabUrlWithSubgroup()
    {
        var parsed = SourceParser.Parse("https://gitlab.com/group/subgroup/repo");
        Assert.Equal(SourceType.GitLab, parsed.Type);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", parsed.Url);
    }

    [Fact]
    public void GitlabPrefix()
    {
        var parsed = SourceParser.Parse("gitlab:group/repo");
        Assert.Equal(SourceType.GitLab, parsed.Type);
        Assert.Equal("https://gitlab.com/group/repo.git", parsed.Url);
    }

    [Fact]
    public void SshGitFallsBackToGit()
    {
        var parsed = SourceParser.Parse("git@github.com:owner/repo.git");
        Assert.Equal(SourceType.Git, parsed.Type);
    }

    [Theory]
    [InlineData("nuget:Sample.SkillPackage", "Sample.SkillPackage", null)]
    [InlineData("nuget:Sample.SkillPackage@1.2.3", "Sample.SkillPackage", "1.2.3")]
    [InlineData("Sample.SkillPackage", "Sample.SkillPackage", null)]
    [InlineData("Sample.SkillPackage@1.2.3", "Sample.SkillPackage", "1.2.3")]
    public void NuGetShorthand(string input, string expectedId, string? expectedVersion)
    {
        var parsed = SourceParser.Parse(input);
        Assert.Equal(SourceType.NuGet, parsed.Type);
        Assert.Equal(expectedId, parsed.PackageId);
        Assert.Equal(expectedVersion, parsed.PackageVersion);
    }

    [Fact]
    public void NoSlashNoDot_TreatedAsGitFallback()
    {
        var parsed = SourceParser.Parse("plainstring");
        Assert.Equal(SourceType.Git, parsed.Type);
    }

    [Fact]
    public void TraversalSubpath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            SourceParser.Parse("https://github.com/owner/repo/tree/main/../escape"));
    }

    [Fact]
    public void WellKnownUrl()
    {
        var parsed = SourceParser.Parse("https://skills.example.com/my-skills");
        Assert.Equal(SourceType.WellKnown, parsed.Type);
    }

    /// <summary>
    /// Equivalent of the upstream invocation <c>npx skills add anthropics/skills</c>:
    /// the GitHub shorthand must parse to a GitHub source (canonicalized to the .git URL)
    /// with no ref/subpath/skillFilter, and - because no <c>--skill</c> flag is supplied -
    /// every skill discovered in the repo is kept for install.
    /// </summary>
    [Fact]
    public void UpstreamEquivalent_AnthropicsSkills_ParsesAndKeepsEveryDiscoveredSkill()
    {
        var parsed = SourceParser.Parse("anthropics/skills");

        Assert.Equal(SourceType.GitHub, parsed.Type);
        Assert.Equal("https://github.com/anthropics/skills.git", parsed.Url);
        Assert.Null(parsed.Ref);
        Assert.Null(parsed.Subpath);
        Assert.Null(parsed.SkillFilter);
        Assert.Equal("anthropics/skills", SourceParser.GetOwnerRepo(parsed));

        // No --skill flag → AddCommand.FilterByName keeps every discovered skill so the
        // installer (or the interactive multi-select prompt) sees the full set.
        var discovered = new List<Skills.SkillModel.Skill>
        {
            FakeSkill("artifacts-builder"),
            FakeSkill("pdf"),
            FakeSkill("slack-app-development"),
        };
        var kept = Skills.Commands.AddCommand.FilterByName(discovered, []);
        Assert.Equal(3, kept.Count);

        // --skill '*' is the explicit "all" shorthand - same result.
        Assert.Equal(3, Skills.Commands.AddCommand.FilterByName(discovered, ["*"]).Count);

        // Single --skill narrows the set deterministically.
        var single = Skills.Commands.AddCommand.FilterByName(discovered, ["pdf"]);
        Assert.Single(single);
        Assert.Equal("pdf", single[0].Name);
    }

    private static Skills.SkillModel.Skill FakeSkill(string name) =>
        new(Name: name, Description: name, Path: "/tmp", RawContent: "", Metadata: null);
}
