namespace AgentSkills.Agents;

/// <summary>
/// v1 agent registry. Paths copied verbatim from upstream src/agents.ts. "Universal" agents
/// share the canonical <c>.agents/skills</c> directory; non-universal agents get their own.
/// </summary>
public static class AgentRegistry
{
    public const string CanonicalSubdir = ".agents/skills";

    private static readonly Lazy<IReadOnlyDictionary<string, AgentConfig>> _all = new(Build);

    public static IReadOnlyDictionary<string, AgentConfig> All => _all.Value;

    public static AgentConfig Get(string name) =>
        All.TryGetValue(name, out var cfg)
            ? cfg
            : throw new ArgumentException(
                $"Unknown agent: {name}. Known: {string.Join(", ", All.Keys)}",
                nameof(name));

    public static IReadOnlyList<AgentConfig> DetectInstalled() =>
        All.Values.Where(a => a.IsInstalled()).ToList();

    public static string GetCanonicalDir(bool global, string? cwd = null) =>
        Path.Combine(global ? GetHome() : cwd ?? Directory.GetCurrentDirectory(), CanonicalSubdir);

    public static string GetAgentBaseDir(AgentConfig agent, bool global, string? cwd = null)
    {
        if (agent.IsUniversal)
        {
            return GetCanonicalDir(global, cwd);
        }
        if (global)
        {
            return agent.GlobalSkillsDir
                ?? throw new InvalidOperationException($"{agent.DisplayName} does not support global installs");
        }
        return Path.Combine(cwd ?? Directory.GetCurrentDirectory(), agent.ProjectSkillsDir);
    }

    private static IReadOnlyDictionary<string, AgentConfig> Build()
    {
        var home = GetHome();
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")?.Trim() is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(home, ".config");
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME")?.Trim() is { Length: > 0 } cx
            ? cx
            : Path.Combine(home, ".codex");
        var claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")?.Trim() is { Length: > 0 } cc
            ? cc
            : Path.Combine(home, ".claude");

        var list = new List<AgentConfig>
        {
            new(
                Name: "claude-code",
                DisplayName: "Claude Code",
                ProjectSkillsDir: ".claude/skills",
                GlobalSkillsDir: Path.Combine(claudeHome, "skills"),
                IsUniversal: false,
                IsInstalled: () => Directory.Exists(claudeHome)),

            new(
                Name: "codex",
                DisplayName: "Codex",
                ProjectSkillsDir: ".agents/skills",
                GlobalSkillsDir: Path.Combine(codexHome, "skills"),
                IsUniversal: true,
                IsInstalled: () => Directory.Exists(codexHome) || Directory.Exists("/etc/codex")),

            new(
                Name: "cursor",
                DisplayName: "Cursor",
                ProjectSkillsDir: ".agents/skills",
                GlobalSkillsDir: Path.Combine(home, ".cursor", "skills"),
                IsUniversal: true,
                IsInstalled: () => Directory.Exists(Path.Combine(home, ".cursor"))),

            new(
                Name: "opencode",
                DisplayName: "OpenCode",
                ProjectSkillsDir: ".agents/skills",
                GlobalSkillsDir: Path.Combine(configHome, "opencode", "skills"),
                IsUniversal: true,
                IsInstalled: () => Directory.Exists(Path.Combine(configHome, "opencode"))),

            new(
                Name: "universal",
                DisplayName: "Universal",
                ProjectSkillsDir: ".agents/skills",
                GlobalSkillsDir: Path.Combine(configHome, "agents", "skills"),
                IsUniversal: true,
                IsInstalled: () => false),
        };

        return list.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetHome() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
