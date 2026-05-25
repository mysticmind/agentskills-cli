namespace AgentSkills.Sources;

/// <summary>
/// A staged source: implementations download or copy the source into <see cref="RootPath"/>
/// before the discovery + install flow runs. <see cref="Subpath"/> reflects the parsed source's
/// subpath (e.g. <c>skills/foo</c> within a repo). <c>Dispose</c> cleans temp dirs.
/// </summary>
public interface ISkillSource : IDisposable
{
    ParsedSource Parsed { get; }
    string RootPath { get; }
    string? Subpath { get; }
    string DisplaySource { get; }
    string SourceTypeLabel { get; }
    string? SourceUrl { get; }
    string? Reference { get; }
}
