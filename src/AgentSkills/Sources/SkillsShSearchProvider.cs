using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentSkills.Sources;

/// <summary>
/// Default <see cref="ISkillSearchProvider"/> backed by the public skills.sh
/// discovery API. Endpoint can be overridden with <c>SKILLS_API_URL</c>
/// (matches upstream).
/// </summary>
public sealed class SkillsShSearchProvider : ISkillSearchProvider
{
    private const string DefaultBase = "https://skills.sh";

    private readonly HttpClient _httpClient;
    private readonly ILogger<SkillsShSearchProvider> _logger;

    public SkillsShSearchProvider(HttpClient httpClient, ILogger<SkillsShSearchProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "skills.sh";

    public bool IsEnabled => true;

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("SKILLS_API_URL")?.TrimEnd('/') is { Length: > 0 } envBase
            ? envBase
            : DefaultBase;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            var url = $"{BaseUrl}/api/search?q={Uri.EscapeDataString(query)}&limit=10";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("skills.sh returned {Status} for query {Query}", (int)response.StatusCode, query);
                return [];
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return ExtractHits(doc.RootElement);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "skills.sh search failed for {Query}", query);
            return [];
        }
    }

    private static IReadOnlyList<SearchHit> ExtractHits(JsonElement root)
    {
        if (!root.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        foreach (var entry in skills.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var slug = ReadString(entry, "id");
            var name = ReadString(entry, "name");
            var source = ReadString(entry, "source");
            var installs = entry.TryGetProperty("installs", out var instEl) && instEl.ValueKind == JsonValueKind.Number
                ? instEl.GetInt64()
                : 0L;

            if (!string.IsNullOrEmpty(name))
            {
                hits.Add(new SearchHit(slug, name, source, installs));
            }
        }

        return hits.OrderByDescending(h => h.Installs).ToList();
    }

    private static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;
}
