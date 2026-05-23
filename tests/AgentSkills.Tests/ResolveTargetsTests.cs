using AgentSkills.Install;
using Xunit;

namespace AgentSkills.Tests;

public class ResolveTargetsTests
{
    private sealed record Item(string Name);

    private static readonly List<Item> Installed =
    [
        new("hello-skill"),
        new("web-design-guidelines"),
        new("caveman-commit"),
    ];

    [Fact]
    public void EmptyArgs_ReturnsEverything()
    {
        var result = PackageMatcher.ResolveTargets(Array.Empty<string>(), Installed, x => x.Name);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MatchesByExactSkillName_CaseInsensitive()
    {
        var result = PackageMatcher.ResolveTargets(["HELLO-SKILL"], Installed, x => x.Name);
        Assert.Single(result);
        Assert.Equal("hello-skill", result[0].Name);
    }

    [Fact]
    public void UnknownTarget_ReturnsEmpty()
    {
        var result = PackageMatcher.ResolveTargets(["not-installed"], Installed, x => x.Name);
        Assert.Empty(result);
    }

    [Fact]
    public void MixedSkillNamesAndUnknowns_KeepsKnownOnesOnly()
    {
        var result = PackageMatcher.ResolveTargets(
            ["hello-skill", "does-not-exist", "caveman-commit"], Installed, x => x.Name);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Name == "hello-skill");
        Assert.Contains(result, r => r.Name == "caveman-commit");
    }

    [Fact]
    public void DuplicateArgs_DeduplicatesByName()
    {
        var result = PackageMatcher.ResolveTargets(
            ["hello-skill", "hello-skill", "HELLO-SKILL"], Installed, x => x.Name);
        Assert.Single(result);
    }
}
