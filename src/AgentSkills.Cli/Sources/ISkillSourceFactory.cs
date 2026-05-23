namespace AgentSkills.Sources;

/// <summary>
/// Plug-in point for adding new source types (NuGet, npm, well-known, anything you can
/// download or read from disk). Register one with
/// <c>services.AddSingleton&lt;ISkillSourceFactory, MyFactory&gt;()</c> and
/// <see cref="ISourceResolver"/> will dispatch to it for the source types listed in
/// <see cref="SupportedTypes"/>.
/// </summary>
public interface ISkillSourceFactory
{
    /// <summary>Source types this factory handles. Each type must be claimed by exactly one factory.</summary>
    IReadOnlyCollection<SourceType> SupportedTypes { get; }

    /// <summary>
    /// Stages the source (download, extract, copy locally as needed) and returns a
    /// disposable <see cref="ISkillSource"/> ready for skill discovery. Throws a
    /// <see cref="SkillSourceException"/> subtype on failure.
    /// </summary>
    Task<ISkillSource> ResolveAsync(ParsedSource parsed, ResolveOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Per-invocation overrides for source resolution. Extend this record as new source
/// types ship; existing factories ignore fields they don't recognize.
/// </summary>
public sealed record ResolveOptions(
    string? NuGetSource = null,
    string? NpmRegistry = null)
{
    public static ResolveOptions Default { get; } = new();
}
