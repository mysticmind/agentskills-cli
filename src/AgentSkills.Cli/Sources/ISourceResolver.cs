namespace AgentSkills.Sources;

/// <summary>
/// Top-level entry point for turning a user-supplied source string into a staged
/// <see cref="ISkillSource"/>. Delegates the type-specific work to whichever
/// <see cref="ISkillSourceFactory"/> claims the parsed source's
/// <see cref="ParsedSource.Type"/>.
/// </summary>
public interface ISourceResolver
{
    /// <summary>
    /// Parses <paramref name="userInput"/>, picks the matching factory, and resolves
    /// the source. Caller disposes the returned <see cref="ISkillSource"/>.
    /// </summary>
    Task<ISkillSource> ResolveAsync(string userInput, ResolveOptions options, CancellationToken cancellationToken);
}
