namespace AgentSkills.Sources;

public enum SourceType
{
    Local,
    GitHub,
    GitLab,
    Git,
    NuGet,
    Npm,
    WellKnown,
}

public sealed record ParsedSource(
    SourceType Type,
    string Url,
    string? Subpath = null,
    string? Ref = null,
    string? SkillFilter = null,
    string? LocalPath = null,
    string? PackageId = null,
    string? PackageVersion = null);
