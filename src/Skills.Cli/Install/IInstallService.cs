using Skills.Agents;
using Skills.SkillModel;
using Skills.Sources;

namespace Skills.Install;

/// <summary>
/// Encapsulates the full install pipeline: resolve a source, discover skills,
/// filter, install per agent, and update the lock files. Commands compose this
/// service; <see cref="FindCommand"/> and <see cref="UpdateCommand"/> reuse it
/// so the same code path serves every install vector.
/// </summary>
public interface IInstallService
{
    Task<InstallSummary> InstallAsync(InstallRequest request, CancellationToken cancellationToken);
}

public sealed record InstallRequest(
    string SourceInput,
    IReadOnlyList<string> SkillFilter,
    IReadOnlyList<AgentConfig> Agents,
    bool Global,
    InstallMode Mode,
    ResolveOptions ResolveOptions,
    bool IncludeInternal = false,
    string? SubpathOverride = null);

public sealed record InstallSummary(
    string DisplaySource,
    IReadOnlyList<SkillInstallOutcome> Outcomes,
    IReadOnlyCollection<string> InstallRoots);

public sealed record SkillInstallOutcome(
    Skill Skill,
    AgentConfig Agent,
    InstallResult Result);
