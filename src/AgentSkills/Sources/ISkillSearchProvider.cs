namespace AgentSkills.Sources;

/// <summary>
/// Plug-in point for skill discovery backends. Each provider talks to one search
/// surface (skills.sh today, internal corporate registries, GitHub topic search,
/// etc.). <see cref="FindCommand"/> fans out across every enabled provider in
/// parallel and merges the results.
///
/// To add a new provider, implement this interface and register it after
/// <c>AddAgentSkillsRuntime</c>:
/// <code>services.AddSingleton&lt;ISkillSearchProvider, MyProvider&gt;();</code>
/// </summary>
public interface ISkillSearchProvider
{
    /// <summary>Stable id used for dedup, the <c>--provider</c> filter, and the result-row tag.</summary>
    string Name { get; }

    /// <summary>Skip this provider when false (env var not set, auth missing, feature flag off, etc.).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Returns up to ~10 hits for <paramref name="query"/>. Implementations should be
    /// best-effort: return an empty list rather than throwing on transient failures so
    /// that one bad provider does not break a fan-out search. Honor <paramref name="cancellationToken"/>.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken);
}
