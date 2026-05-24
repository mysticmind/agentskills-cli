using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using Xunit;

namespace AgentSkills.Tests;

public class InstallerTests : IDisposable
{
    private readonly string _root;

    public InstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentskills-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private Skill MakeSkill(string name)
    {
        var dir = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"""
            ---
            name: {name}
            description: Test skill.
            ---
            # body
            """);
        File.WriteAllText(Path.Combine(dir, "extra.txt"), "hello");
        return SkillManifest.ParseSkillMd(Path.Combine(dir, "SKILL.md"))!;
    }

    [Fact]
    public void InstallProject_CanonicalDirGetsContents()
    {
        var cwd = Path.Combine(_root, "project");
        Directory.CreateDirectory(cwd);
        var skill = MakeSkill("foo-skill");
        var universal = AgentRegistry.Get("universal");

        var result = Installer.InstallForAgent(skill, universal,
            new Installer.Options(Global: false, Cwd: cwd, Mode: InstallMode.Copy));

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(cwd, ".agents", "skills", "foo-skill", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(cwd, ".agents", "skills", "foo-skill", "extra.txt")));
    }

    [Fact]
    public void InstallProject_NonUniversalAgent_SkipsWhenAgentDirAbsent()
    {
        var cwd = Path.Combine(_root, "project2");
        Directory.CreateDirectory(cwd);
        var skill = MakeSkill("bar-skill");
        var claude = AgentRegistry.Get("claude-code");

        var result = Installer.InstallForAgent(skill, claude,
            new Installer.Options(Global: false, Cwd: cwd, Mode: InstallMode.Symlink));

        Assert.True(result.Success);
        // Claude-specific dir should NOT have been materialized.
        Assert.False(Directory.Exists(Path.Combine(cwd, ".claude")));
        // Canonical .agents/skills should have it.
        Assert.True(File.Exists(Path.Combine(cwd, ".agents", "skills", "bar-skill", "SKILL.md")));
    }
}
