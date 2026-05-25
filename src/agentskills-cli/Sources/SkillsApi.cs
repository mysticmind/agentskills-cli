namespace AgentSkills.Sources;

/// <summary>A single skill returned by an <see cref="ISkillSearchProvider"/>.</summary>
public sealed record SearchHit(string Slug, string Name, string Source, long Installs);

/// <summary>Display helpers shared across providers and the find UI.</summary>
public static class SearchFormatting
{
    public static string FormatInstalls(long count) => count switch
    {
        <= 0 => string.Empty,
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M installs",
        >= 1_000 => $"{count / 1_000.0:0.#}K installs",
        1 => "1 install",
        _ => $"{count} installs",
    };
}
