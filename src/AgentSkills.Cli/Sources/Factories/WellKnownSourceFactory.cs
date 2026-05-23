using Microsoft.Extensions.Logging;

namespace AgentSkills.Sources.Factories;

public sealed class WellKnownSourceFactory : ISkillSourceFactory
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WellKnownSourceFactory> _logger;

    public WellKnownSourceFactory(HttpClient httpClient, ILogger<WellKnownSourceFactory> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyCollection<SourceType> SupportedTypes { get; } = [SourceType.WellKnown];

    public async Task<ISkillSource> ResolveAsync(ParsedSource parsed, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        try
        {
            return await WellKnownSource.FetchAsync(
                parsed,
                httpClient: _httpClient,
                log: msg => _logger.LogDebug("{Message}", msg),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new WellKnownEndpointException(parsed.Url, ex.Message, ex);
        }
    }
}
