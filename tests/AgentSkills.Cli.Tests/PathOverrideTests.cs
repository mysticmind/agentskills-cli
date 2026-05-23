using AgentSkills.SkillModel;
using Xunit;

namespace AgentSkills.Tests;

/// <summary>
/// Behavioral coverage for the <c>--path</c> override. The actual install pipeline is
/// covered by smoke tests; here we just lock in (a) the validation that protects against
/// path traversal in user input and (b) that <see cref="SkillDiscovery"/> happily honors
/// an arbitrary subpath inside the staged source.
/// </summary>
public class PathOverrideTests : IDisposable
{
    private readonly string _root;

    public PathOverrideTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "skills-net-pathtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Discovery_HonorsCustomSubpath()
    {
        // Layout: <root>/tools/prompts/my-skill/SKILL.md with a separate decoy at <root>/SKILL.md
        var skillDir = Path.Combine(_root, "tools", "prompts", "my-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: my-skill
            description: Lives under tools/prompts.
            ---
            body
            """);

        File.WriteAllText(Path.Combine(_root, "SKILL.md"), """
            ---
            name: decoy-root-skill
            description: Should not be returned when subpath is set.
            ---
            decoy
            """);

        var withoutSubpath = SkillDiscovery.Discover(_root);
        Assert.Contains(withoutSubpath, s => s.Name == "decoy-root-skill");

        var withSubpath = SkillDiscovery.Discover(_root, subpath: "tools/prompts");
        Assert.Single(withSubpath);
        Assert.Equal("my-skill", withSubpath[0].Name);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("ok/../escape")]
    [InlineData("a/b/../../escape")]
    public void PathSafety_RejectsTraversal(string subpath)
    {
        Assert.Throws<ArgumentException>(() => PathSafety.SanitizeSubpath(subpath));
    }

    [Theory]
    [InlineData("tools/prompts")]
    [InlineData("a/b/c/d")]
    [InlineData("just-one-segment")]
    public void PathSafety_AcceptsSafeSubpaths(string subpath)
    {
        Assert.Equal(subpath, PathSafety.SanitizeSubpath(subpath));
    }
}
