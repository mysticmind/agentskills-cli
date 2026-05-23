using Microsoft.Extensions.Logging;

namespace Skills.Sources.Factories;

public sealed class NpmSourceFactory : ISkillSourceFactory
{
    private readonly ILogger<NpmSourceFactory> _logger;

    public NpmSourceFactory(ILogger<NpmSourceFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyCollection<SourceType> SupportedTypes { get; } = [SourceType.Npm];

    public async Task<ISkillSource> ResolveAsync(ParsedSource parsed, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            return await NpmSource.DownloadAsync(
                parsed,
                overrideRegistry: options.NpmRegistry,
                log: msg => _logger.LogDebug("{Message}", msg),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new NpmRegistryException(parsed.PackageId ?? "(unknown)", options.NpmRegistry, ex.Message, ex);
        }
    }
}
