namespace AgentSkills.Sources;

public sealed class LocalSource(ParsedSource parsed) : ISkillSource
{
    public ParsedSource Parsed { get; } = parsed;
    public string RootPath { get; } = parsed.LocalPath ?? parsed.Url;
    public string? Subpath => null;
    public string DisplaySource => RootPath;
    public string SourceTypeLabel => "local";
    public string? SourceUrl => RootPath;
    public string? Reference => null;

    public void Dispose() { /* nothing to clean for local sources */ }
}
