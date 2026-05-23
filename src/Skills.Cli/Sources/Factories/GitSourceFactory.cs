using Microsoft.Extensions.Logging;

namespace Skills.Sources.Factories;

public sealed class GitSourceFactory : ISkillSourceFactory
{
    private readonly ILogger<GitSourceFactory> _logger;

    public GitSourceFactory(ILogger<GitSourceFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyCollection<SourceType> SupportedTypes { get; } =
        [SourceType.GitHub, SourceType.GitLab, SourceType.Git];

    public Task<ISkillSource> ResolveAsync(ParsedSource parsed, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        _logger.LogDebug("Cloning {Url} (ref={Ref})", parsed.Url, parsed.Ref ?? "default");
        try
        {
            return Task.FromResult<ISkillSource>(GitSource.Clone(parsed));
        }
        catch (TimeoutException ex)
        {
            throw new GitException(parsed.Url, "git clone timed out (override with SKILLS_CLONE_TIMEOUT_MS).", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new GitException(parsed.Url, ex.Message, ex);
        }
    }
}
