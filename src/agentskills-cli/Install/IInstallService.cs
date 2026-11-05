using AgentSkills.Agents;
using AgentSkills.SkillModel;
using AgentSkills.Sources;

namespace AgentSkills.Install;

/// <summary>
/// Encapsulates the full install pipeline: resolve a source, discover skills,
/// filter, install per agent, and update the lock files. Commands compose this
/// service; <see cref="Commands.FindCommand"/>, <see cref="Commands.UpdateCommand"/> and
/// <see cref="Commands.RestoreCommand"/> reuse it so the same code path serves every install vector.
/// </summary>
public interface IInstallService
{
    /// <summary>Resolves <see cref="InstallRequest.SourceInput"/>, discovers + filters skills, and installs them.</summary>
    Task<InstallSummary> InstallAsync(InstallRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Installs <paramref name="skills"/> from an already-staged <paramref name="source"/>.
    /// The caller owns (and disposes) the source. <see cref="InstallRequest.SourceInput"/>,
    /// <see cref="InstallRequest.SkillFilter"/> and <see cref="InstallRequest.ResolveOptions"/>
    /// are ignored.
    /// </summary>
    Task<InstallSummary> InstallSkillsAsync(
        ISkillSource source,
        IReadOnlyList<Skill> skills,
        InstallRequest request,
        CancellationToken cancellationToken);
}

/// <param name="Cwd">Project root for project-scope installs and the project lock. Defaults to the current directory.</param>
/// <param name="Aliases">Source skill name → name to install it under (<c>add --as</c>). Case-insensitive keys.</param>
/// <param name="Replace">
/// Overwrite skills from other sources that already use an install name. When false, such a
/// conflict throws <see cref="SkillConflictException"/> before anything is installed.
/// </param>
public sealed record InstallRequest(
    string SourceInput,
    IReadOnlyList<string> SkillFilter,
    IReadOnlyList<AgentConfig> Agents,
    bool Global,
    InstallMode Mode,
    ResolveOptions ResolveOptions,
    bool IncludeInternal = false,
    string? SubpathOverride = null,
    string? Cwd = null,
    IReadOnlyDictionary<string, string>? Aliases = null,
    bool Replace = false);

public sealed record InstallSummary(
    string DisplaySource,
    IReadOnlyList<SkillInstallOutcome> Outcomes,
    IReadOnlyCollection<string> InstallRoots);

/// <param name="Skill">The skill as found in the source (its <c>Name</c> is the source name).</param>
/// <param name="InstallName">The name it was installed under; differs from <c>Skill.Name</c> for an alias.</param>
public sealed record SkillInstallOutcome(
    Skill Skill,
    AgentConfig Agent,
    InstallResult Result,
    string? InstallName = null)
{
    public string Name => InstallName ?? Skill.Name;
}
