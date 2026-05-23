using AgentSkills.SkillModel;
using Xunit;

namespace AgentSkills.Tests;

public class SkillCoreTests
{
    [Theory]
    [InlineData("My Skill", "my-skill")]
    [InlineData("../escape", "escape")]
    [InlineData("Hello/World", "hello-world")]
    [InlineData("UPPER.case_1", "upper.case_1")]
    [InlineData("   ", "unnamed-skill")]
    [InlineData(".dotfiles.", "dotfiles")]
    public void Sanitize(string input, string expected) =>
        Assert.Equal(expected, SkillNameSanitizer.Sanitize(input));

    [Fact]
    public void Sanitize_LengthCappedAt255()
    {
        var input = new string('a', 400);
        Assert.Equal(255, SkillNameSanitizer.Sanitize(input).Length);
    }

    [Fact]
    public void PathSafety_RejectsTraversal()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "base");
        Directory.CreateDirectory(basePath);
        Assert.False(PathSafety.IsPathInside(basePath, Path.Combine(basePath, "..", "escape")));
        Assert.True(PathSafety.IsPathInside(basePath, Path.Combine(basePath, "child")));
        Assert.Throws<ArgumentException>(() => PathSafety.SanitizeSubpath("foo/../bar"));
    }

    [Fact]
    public void Manifest_ParsesFrontmatter()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "skills-net-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, """
                ---
                name: hello-skill
                description: A test skill.
                metadata:
                  internal: false
                ---
                # body
                """);
            var skill = SkillManifest.ParseSkillMd(skillMd);
            Assert.NotNull(skill);
            Assert.Equal("hello-skill", skill!.Name);
            Assert.Equal("A test skill.", skill.Description);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Discovery_FindsRootAndNested()
    {
        var root = Path.Combine(Path.GetTempPath(), "skills-net-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nested = Path.Combine(root, "skills", "nested-one");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "SKILL.md"), """
                ---
                name: nested-one
                description: Nested.
                ---
                # nested
                """);

            var found = SkillDiscovery.Discover(root);
            Assert.Single(found);
            Assert.Equal("nested-one", found[0].Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
