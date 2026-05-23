using Microsoft.Extensions.Logging;

namespace AgentSkills.Sources;

public sealed class SourceResolver : ISourceResolver
{
    private readonly Dictionary<SourceType, ISkillSourceFactory> _byType;
    private readonly ILogger<SourceResolver> _logger;

    public SourceResolver(IEnumerable<ISkillSourceFactory> factories, ILogger<SourceResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _byType = new Dictionary<SourceType, ISkillSourceFactory>();
        foreach (var factory in factories)
        {
            foreach (var type in factory.SupportedTypes)
            {
                if (_byType.TryGetValue(type, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Source type {type} is claimed by both {existing.GetType().Name} and {factory.GetType().Name}. " +
                        "Each SourceType must be served by exactly one factory.");
                }
                _byType[type] = factory;
            }
        }
    }

    public async Task<ISkillSource> ResolveAsync(string userInput, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentNullException.ThrowIfNull(options);

        ParsedSource parsed;
        try
        {
            parsed = SourceParser.Parse(userInput);
        }
        catch (ArgumentException ex)
        {
            throw new UserInputException($"Invalid source '{userInput}': {ex.Message}", ex);
        }

        if (!_byType.TryGetValue(parsed.Type, out var factory))
        {
            throw new UserInputException($"No source factory registered for source type {parsed.Type}.");
        }

        _logger.LogDebug("Resolving {SourceType} source via {Factory}: {Input}",
            parsed.Type, factory.GetType().Name, userInput);

        return await factory.ResolveAsync(parsed, options, cancellationToken).ConfigureAwait(false);
    }
}
