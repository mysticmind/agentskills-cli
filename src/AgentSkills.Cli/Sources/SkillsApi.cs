using System.Net.Http;
using System.Text.Json;

namespace AgentSkills.Sources;

public sealed record SearchHit(string Slug, string Name, string Source, long Installs);

/// <summary>
/// Thin client for the public skills.sh discovery API. Endpoint can be overridden
/// with <c>SKILLS_API_URL</c> (matches upstream).
/// </summary>
public static class SkillsApi
{
    private const string DefaultBase = "https://skills.sh";

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("SKILLS_API_URL")?.TrimEnd('/') is { Length: > 0 } envBase
            ? envBase
            : DefaultBase;

    public static async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var owned = httpClient is null;
        httpClient ??= CreateDefaultClient();
        try
        {
            var url = $"{BaseUrl}/api/search?q={Uri.EscapeDataString(query)}&limit=10";
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var hits = new List<SearchHit>();
            foreach (var entry in skills.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var slug = entry.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() ?? string.Empty
                    : string.Empty;
                var name = entry.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;
                var source = entry.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.String
                    ? srcEl.GetString() ?? string.Empty
                    : string.Empty;
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
        catch
        {
            return [];
        }
        finally
        {
            if (owned) httpClient.Dispose();
        }
    }

    public static string FormatInstalls(long count) => count switch
    {
        <= 0 => string.Empty,
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M installs",
        >= 1_000 => $"{count / 1_000.0:0.#}K installs",
        1 => "1 install",
        _ => $"{count} installs",
    };

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("skills-net/0.1");
        return client;
    }
}
