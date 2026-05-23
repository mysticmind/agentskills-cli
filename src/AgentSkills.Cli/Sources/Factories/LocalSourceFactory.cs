namespace AgentSkills.Sources.Factories;

public sealed class LocalSourceFactory : ISkillSourceFactory
{
    public IReadOnlyCollection<SourceType> SupportedTypes { get; } = [SourceType.Local];

    public Task<ISkillSource> ResolveAsync(ParsedSource parsed, ResolveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var source = new LocalSource(parsed);
        if (!Directory.Exists(source.RootPath))
        {
            source.Dispose();
            throw new SkillSourceException(source.DisplaySource, $"Local path not found: {source.RootPath}");
        }
        return Task.FromResult<ISkillSource>(source);
    }
}
