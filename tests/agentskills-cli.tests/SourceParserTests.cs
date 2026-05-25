using AgentSkills.Sources;
using Xunit;

namespace AgentSkills.Tests;

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
    [InlineData("acme/sample-skills", "https://github.com/acme/sample-skills.git", null, null, null)]
    [InlineData("acme/sample-skills/skills/foo", "https://github.com/acme/sample-skills.git", "skills/foo", null, null)]
    [InlineData("acme/sample-skills#main", "https://github.com/acme/sample-skills.git", null, "main", null)]
    [InlineData("acme/sample-skills@example-skill", "https://github.com/acme/sample-skills.git", null, null, "example-skill")]
    [InlineData("acme/sample-skills#main@example-skill", "https://github.com/acme/sample-skills.git", null, "main", "example-skill")]
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
        var parsed = SourceParser.Parse("https://github.com/acme/sample-skills/tree/main/skills/foo");
        Assert.Equal(SourceType.GitHub, parsed.Type);
        Assert.Equal("https://github.com/acme/sample-skills.git", parsed.Url);
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
    [InlineData("nuget:Acme.SampleSkills", "Acme.SampleSkills", null)]
    [InlineData("nuget:Acme.SampleSkills@1.2.3", "Acme.SampleSkills", "1.2.3")]
    [InlineData("Acme.SampleSkills", "Acme.SampleSkills", null)]
    [InlineData("Acme.SampleSkills@1.2.3", "Acme.SampleSkills", "1.2.3")]
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
    /// Equivalent of the upstream-style invocation <c>npx skills add &lt;owner/repo&gt;</c>:
    /// the GitHub shorthand must parse to a GitHub source (canonicalized to the .git URL)
    /// with no ref/subpath/skillFilter, and, because no <c>--skill</c> flag is supplied,
    /// every skill discovered in the repo is kept for install.
    /// </summary>
    [Fact]
    public void UpstreamEquivalent_GithubShorthand_ParsesAndKeepsEveryDiscoveredSkill()
    {
        var parsed = SourceParser.Parse("acme/sample-skills");

        Assert.Equal(SourceType.GitHub, parsed.Type);
        Assert.Equal("https://github.com/acme/sample-skills.git", parsed.Url);
        Assert.Null(parsed.Ref);
        Assert.Null(parsed.Subpath);
        Assert.Null(parsed.SkillFilter);
        Assert.Equal("acme/sample-skills", SourceParser.GetOwnerRepo(parsed));

        // No --skill flag → AddCommand.FilterByName keeps every discovered skill so the
        // installer (or the interactive multi-select prompt) sees the full set.
        var discovered = new List<AgentSkills.SkillModel.Skill>
        {
            FakeSkill("alpha-skill"),
            FakeSkill("beta-skill"),
            FakeSkill("gamma-skill"),
        };
        var kept = AgentSkills.Commands.AddCommand.FilterByName(discovered, []);
        Assert.Equal(3, kept.Count);

        // --skill '*' is the explicit "all" shorthand → same result.
        Assert.Equal(3, AgentSkills.Commands.AddCommand.FilterByName(discovered, ["*"]).Count);

        // Single --skill narrows the set deterministically.
        var single = AgentSkills.Commands.AddCommand.FilterByName(discovered, ["beta-skill"]);
        Assert.Single(single);
        Assert.Equal("beta-skill", single[0].Name);
    }

    private static AgentSkills.SkillModel.Skill FakeSkill(string name) =>
        new(Name: name, Description: name, Path: "/tmp", RawContent: "", Metadata: null);
}
