namespace AgentSkills.SkillModel;

public sealed record Skill(
    string Name,
    string Description,
    string Path,
    string RawContent,
    IReadOnlyDictionary<string, object?>? Metadata);

public sealed record InstalledSkill(
    string Name,
    string Description,
    string Path,
    string CanonicalPath,
    string Scope,
    List<string> Agents);
