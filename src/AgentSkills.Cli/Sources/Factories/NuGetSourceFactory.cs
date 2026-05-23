using Microsoft.Extensions.Logging;

namespace AgentSkills.Sources.Factories;

public sealed class NuGetSourceFactory : ISkillSourceFactory
{
    private readonly ILogger<NuGetSourceFactory> _logger;

    public NuGetSourceFactory(ILogger<NuGetSourceFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyCollection<SourceType> SupportedTypes { get; } = [SourceType.NuGet];

    public async Task<ISkillSource> ResolveAsync(ParsedSource parsed, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            return await NuGetSource.DownloadAsync(
                parsed,
                overrideSource: options.NuGetSource,
                log: msg => _logger.LogDebug("{Message}", msg),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new NuGetFeedException(parsed.PackageId ?? "(unknown)", ex.Message, ex);
        }
    }
}
