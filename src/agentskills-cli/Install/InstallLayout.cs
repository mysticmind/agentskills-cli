using AgentSkills.Agents;
using AgentSkills.SkillModel;

namespace AgentSkills.Install;

/// <summary>
/// Reconstructs how an installed skill is laid out on disk (which agents have it, and
/// whether it was symlinked or copied) so <c>update</c> can reinstall it the same way
/// instead of forcing copy mode onto whatever agents happen to be detected today.
/// </summary>
public static class InstallLayout
{
    public sealed record Layout(IReadOnlyList<AgentConfig> Agents, InstallMode Mode);

    /// <param name="agentFilter">When non-null, only these agents are kept (the <c>--agent</c> option).</param>
    public static Layout Detect(InstalledSkill skill, IReadOnlyList<AgentConfig>? agentFilter, string? cwd = null)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var global = skill.Scope == "global";

        var agents = skill.Agents
            .Select(n => AgentRegistry.All.TryGetValue(n, out var a) ? a : null)
            .OfType<AgentConfig>()
            .Where(a => agentFilter is null || agentFilter.Any(f => f.Name == a.Name))
            .Where(a => !global || a.GlobalSkillsDir is not null || a.IsUniversal)
            .ToList();

        if (agents.Count == 0)
        {
            agents = agentFilter is { Count: > 0 } ? agentFilter.ToList() : [AgentRegistry.Get("universal")];
        }

        return new Layout(agents, DetectMode(skill.Name, agents, global, cwd));
    }

    /// <summary>
    /// Symlink when any agent's skill directory is a symlink, or when every agent shares
    /// the canonical directory (universal agents: copy and symlink are the same layout).
    /// Copy when a non-universal agent holds a real directory.
    /// </summary>
    public static InstallMode DetectMode(string skillName, IReadOnlyList<AgentConfig> agents, bool global, string? cwd = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        var sanitized = SkillNameSanitizer.Sanitize(skillName);
        var sawRealDir = false;

        foreach (var agent in agents)
        {
            if (agent.IsUniversal) continue;
            if (global && agent.GlobalSkillsDir is null) continue;

            var dir = Path.Combine(AgentRegistry.GetAgentBaseDir(agent, global, cwd), sanitized);
            try
            {
                var info = new DirectoryInfo(dir);
                if (info.LinkTarget is not null) return InstallMode.Symlink;
                if (info.Exists) sawRealDir = true;
            }
            catch (IOException)
            {
                // unreadable entry: ignore, fall through to the default
            }
        }

        return sawRealDir ? InstallMode.Copy : InstallMode.Symlink;
    }
}
