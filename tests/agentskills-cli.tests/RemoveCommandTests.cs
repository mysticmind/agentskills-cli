using AgentSkills.Install;
using Xunit;

namespace AgentSkills.Tests;

public class PackageMatcherTests
{
    [Theory]
    [InlineData("acme/sample-skills", "acme/sample-skills")]
    [InlineData("Acme.SampleSkills@0.1.0", "Acme.SampleSkills")]
    [InlineData("Acme.SampleSkills", "Acme.SampleSkills")]
    [InlineData("@acme/sample-skills@1.0.0", "@acme/sample-skills")]
    [InlineData("@acme/sample-skills@2.0.0-beta.1", "@acme/sample-skills")]
    [InlineData("@acme/sample-skills", "@acme/sample-skills")]
    [InlineData("", "")]
    public void ExtractBaseSource(string lockSource, string expected)
    {
        Assert.Equal(expected, PackageMatcher.ExtractBaseSource(lockSource));
    }

    [Theory]
    // Explicit prefixes + version stripping
    [InlineData("npm:@acme/sample-skills", "@acme/sample-skills")]
    [InlineData("npm:sample-pkg@1.3.0", "sample-pkg")]
    [InlineData("nuget:Acme.SampleSkills@0.1.0", "Acme.SampleSkills")]
    [InlineData("NUGET:Acme.Foo", "Acme.Foo")]
    // Bare shorthand
    [InlineData("@acme/sample-skills@1.0.0", "@acme/sample-skills")]
    [InlineData("Acme.SampleSkills", "Acme.SampleSkills")]
    [InlineData("  acme/sample-skills  ", "acme/sample-skills")]
    // GitHub URLs collapse to owner/repo to match what `add` records in the lock.
    [InlineData("https://github.com/acme/sample-skills", "acme/sample-skills")]
    [InlineData("https://github.com/acme/sample-skills.git", "acme/sample-skills")]
    [InlineData("https://github.com/acme/sample-skills/tree/main/skills/example", "acme/sample-skills")]
    // GitLab URLs (incl. subgroups)
    [InlineData("https://gitlab.com/acme/sub/sample-skills", "acme/sub/sample-skills")]
    [InlineData("gitlab:acme/sample-skills", "acme/sample-skills")]
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
    [InlineData("@acme/sample-skills", "@acme/sample-skills", null)]
    [InlineData("Acme.SampleSkills", "Acme.SampleSkills", null)]
    [InlineData("npm:sample-pkg", "sample-pkg", null)]
    // Version pinned → strict match (Version preserved)
    [InlineData("@acme/sample-skills@1.5.0", "@acme/sample-skills", "1.5.0")]
    [InlineData("Acme.SampleSkills@1.2.3", "Acme.SampleSkills", "1.2.3")]
    [InlineData("npm:sample-pkg@1.3.0", "sample-pkg", "1.3.0")]
    [InlineData("nuget:Acme.Sample@2.0.0-beta.1", "Acme.Sample", "2.0.0-beta.1")]
    // GitHub refs are NOT versions: the owner/repo carries no version constraint.
    [InlineData("acme/sample-skills#main", "acme/sample-skills", null)]
    public void NormalizeToLocator_SplitsBaseAndVersion(string input, string expectedBase, string? expectedVersion)
    {
        var locator = PackageMatcher.NormalizeToLocator(input);
        Assert.Equal(expectedBase, locator.BaseId);
        Assert.Equal(expectedVersion, locator.Version);
    }

    [Theory]
    [InlineData("@acme/sample-skills@1.4.0", "1.4.0")]
    [InlineData("Acme.SampleSkills@0.1.0", "0.1.0")]
    [InlineData("acme/sample-skills", null)]
    [InlineData("", null)]
    public void ExtractVersion(string lockSource, string? expected)
    {
        Assert.Equal(expected, PackageMatcher.ExtractVersion(lockSource));
    }
}
