namespace Skills.Agents;

public sealed record AgentConfig(
    string Name,
    string DisplayName,
    string ProjectSkillsDir,
    string? GlobalSkillsDir,
    bool IsUniversal,
    Func<bool> IsInstalled);
