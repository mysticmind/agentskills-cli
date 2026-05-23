using Skills.Install;
using Xunit;

namespace Skills.Tests;

public class PackageMatcherTests
{
    [Theory]
    [InlineData("vercel-labs/agent-skills", "vercel-labs/agent-skills")]
    [InlineData("Sample.SkillPackage@0.1.0", "Sample.SkillPackage")]
    [InlineData("Sample.SkillPackage", "Sample.SkillPackage")]
    [InlineData("@jasperfx/ai-skills@1.0.0", "@jasperfx/ai-skills")]
    [InlineData("@my-org/x@2.0.0-beta.1", "@my-org/x")]
    [InlineData("@scope/name", "@scope/name")]
    [InlineData("", "")]
    public void ExtractBaseSource(string lockSource, string expected)
    {
        Assert.Equal(expected, PackageMatcher.ExtractBaseSource(lockSource));
    }

    [Theory]
    // Explicit prefixes + version stripping
    [InlineData("npm:@jasperfx/ai-skills", "@jasperfx/ai-skills")]
    [InlineData("npm:left-pad@1.3.0", "left-pad")]
    [InlineData("nuget:Sample.SkillPackage@0.1.0", "Sample.SkillPackage")]
    [InlineData("NUGET:Foo.Bar", "Foo.Bar")]
    // Bare shorthand
    [InlineData("@jasperfx/ai-skills@1.0.0", "@jasperfx/ai-skills")]
    [InlineData("Sample.SkillPackage", "Sample.SkillPackage")]
    [InlineData("  vercel-labs/agent-skills  ", "vercel-labs/agent-skills")]
    // GitHub URLs - collapse to owner/repo to match what `add` records in the lock
    [InlineData("https://github.com/anthropics/skills", "anthropics/skills")]
    [InlineData("https://github.com/anthropics/skills.git", "anthropics/skills")]
    [InlineData("https://github.com/anthropics/skills/tree/main/skills/pdf", "anthropics/skills")]
    // GitLab URLs (incl. subgroups)
    [InlineData("https://gitlab.com/group/sub/repo", "group/sub/repo")]
    [InlineData("gitlab:group/repo", "group/repo")]
    // Empty / whitespace
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeInput(string userInput, string expected)
    {
        Assert.Equal(expected, PackageMatcher.NormalizeInput(userInput));
    }

    [Fact]
    public void NormalizeInput_LocalPath_ResolvesToAbsolute()
    {
        var result = PackageMatcher.NormalizeInput("./somewhere");
        Assert.True(System.IO.Path.IsPathRooted(result),
            $"expected absolute path, got '{result}'");
        Assert.EndsWith("somewhere", result);
    }

    [Theory]
    // No version → matches anything (Version is null)
    [InlineData("@jasperfx/ai-skills", "@jasperfx/ai-skills", null)]
    [InlineData("MyOrg.AgentSkills", "MyOrg.AgentSkills", null)]
    [InlineData("npm:left-pad", "left-pad", null)]
    // Version pinned → strict match (Version preserved)
    [InlineData("@jasperfx/ai-skills@1.5.0", "@jasperfx/ai-skills", "1.5.0")]
    [InlineData("MyOrg.AgentSkills@1.2.3", "MyOrg.AgentSkills", "1.2.3")]
    [InlineData("npm:left-pad@1.3.0", "left-pad", "1.3.0")]
    [InlineData("nuget:Sample@2.0.0-beta.1", "Sample", "2.0.0-beta.1")]
    // GitHub refs are NOT versions - owner/repo carries no version constraint.
    [InlineData("anthropics/skills#main", "anthropics/skills", null)]
    public void NormalizeToLocator_SplitsBaseAndVersion(string input, string expectedBase, string? expectedVersion)
    {
        var locator = PackageMatcher.NormalizeToLocator(input);
        Assert.Equal(expectedBase, locator.BaseId);
        Assert.Equal(expectedVersion, locator.Version);
    }

    [Theory]
    [InlineData("@jasperfx/ai-skills@1.4.0", "1.4.0")]
    [InlineData("Sample.SkillPackage@0.1.0", "0.1.0")]
    [InlineData("anthropics/skills", null)]
    [InlineData("", null)]
    public void ExtractVersion(string lockSource, string? expected)
    {
        Assert.Equal(expected, PackageMatcher.ExtractVersion(lockSource));
    }
}
