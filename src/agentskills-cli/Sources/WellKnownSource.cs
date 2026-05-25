using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentSkills.SkillModel;
using AgentSkills.Sources.Archives;

namespace AgentSkills.Sources;

/// <summary>
/// Implements the well-known agent-skills discovery convention (RFC 8615-style):
/// fetches <c>/.well-known/agent-skills/index.json</c> with <c>/.well-known/skills/</c>
/// as legacy fallback, then materializes each skill into a staging directory so the
/// normal install pipeline can take over.
/// </summary>
public sealed class WellKnownSource : ISkillSource
{
    private const string DiscoverySchemaV2 = "https://schemas.agentskills.io/discovery/0.2.0/schema.json";
    private static readonly string[] WellKnownPaths = [".well-known/agent-skills", ".well-known/skills"];

    private readonly string _stagingRoot;

    public ParsedSource Parsed { get; }
    public string RootPath => _stagingRoot;
    public string? Subpath => null;
    public string DisplaySource { get; }
    public string SourceTypeLabel => "well-known";
    public string? SourceUrl { get; }
    public string? Reference => null;

    private WellKnownSource(ParsedSource parsed, string stagingRoot, string sourceUrl, string display)
    {
        Parsed = parsed;
        _stagingRoot = stagingRoot;
        SourceUrl = sourceUrl;
        DisplaySource = display;
    }

    public static async Task<WellKnownSource> FetchAsync(
        ParsedSource parsed,
        HttpClient? httpClient = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var owned = httpClient is null;
        httpClient ??= CreateDefaultClient();

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"agentskills-cli-wk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var (resolvedIndexUrl, entries) = await ResolveIndexAsync(parsed.Url, httpClient, log, cancellationToken)
                .ConfigureAwait(false);

            if (entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No well-known skills index found under {parsed.Url} (tried /.well-known/agent-skills/ and /.well-known/skills/).");
            }

            foreach (var entry in entries)
            {
                var dest = Path.Combine(stagingRoot, SkillNameSanitizer.Sanitize(entry.Name));
                Directory.CreateDirectory(dest);
                await MaterializeAsync(entry, dest, httpClient, log, cancellationToken).ConfigureAwait(false);
            }

            return new WellKnownSource(parsed, stagingRoot, resolvedIndexUrl, parsed.Url);
        }
        catch
        {
            try { Directory.Delete(stagingRoot, recursive: true); } catch { /* swallow */ }
            throw;
        }
        finally
        {
            if (owned) httpClient.Dispose();
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_stagingRoot, recursive: true); } catch { /* best effort */ }
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("agentskills-cli/0.1");
        return client;
    }

    // ─── Index resolution ────────────────────────────────────────────────────

    private sealed record NormalizedEntry(
        string Name,
        string Description,
        string Version,
        string? Type,
        string? ArtifactUrl,
        string? Digest,
        string[]? LegacyFiles,
        string? LegacyBaseUrl,
        string? LegacyWellKnownPath);

    private static async Task<(string IndexUrl, List<NormalizedEntry> Entries)> ResolveIndexAsync(
        string baseUrl,
        HttpClient client,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            throw new ArgumentException($"Not a valid URL: {baseUrl}", nameof(baseUrl));
        }

        var basePath = parsed.AbsolutePath.TrimEnd('/');

        foreach (var wellKnownPath in WellKnownPaths)
        {
            foreach (var indexUrl in BuildIndexCandidates(parsed, basePath, wellKnownPath))
            {
                try
                {
                    log?.Invoke($"  probing {indexUrl}");
                    using var response = await client.GetAsync(indexUrl, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) continue;

                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var entries = NormalizeIndex(json, indexUrl, wellKnownPath);
                    if (entries.Count > 0)
                    {
                        return (indexUrl, entries);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"    {ex.Message}");
                }
            }
        }

        return (baseUrl, []);
    }

    private static IEnumerable<string> BuildIndexCandidates(Uri parsed, string basePath, string wellKnownPath)
    {
        var rootBase = $"{parsed.Scheme}://{parsed.Authority}";
        yield return $"{rootBase}{basePath}/{wellKnownPath}/index.json";
        if (!string.IsNullOrEmpty(basePath))
        {
            yield return $"{rootBase}/{wellKnownPath}/index.json";
        }
    }

    private static List<NormalizedEntry> NormalizeIndex(string json, string indexUrl, string wellKnownPath)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return [];
        if (!root.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array) return [];

        var schema = root.TryGetProperty("$schema", out var schemaEl) && schemaEl.ValueKind == JsonValueKind.String
            ? schemaEl.GetString()
            : null;

        var entries = new List<NormalizedEntry>();

        if (schema == DiscoverySchemaV2)
        {
            foreach (var entry in skills.EnumerateArray())
            {
                if (!TryParseV2Entry(entry, indexUrl, out var normalized)) continue;
                entries.Add(normalized);
            }
            return entries;
        }

        // Legacy v0.1.0: must have no $schema. Unknown schemas are rejected.
        if (schema is not null) return [];

        var legacyBaseUrl = GetLegacySkillBaseUrl(indexUrl, wellKnownPath);
        foreach (var entry in skills.EnumerateArray())
        {
            if (!TryParseV1Entry(entry, legacyBaseUrl, wellKnownPath, out var normalized)) return [];
            entries.Add(normalized);
        }
        return entries;
    }

    private static bool TryParseV2Entry(JsonElement entry, string indexUrl, out NormalizedEntry result)
    {
        result = default!;
        if (entry.ValueKind != JsonValueKind.Object) return false;

        if (!TryString(entry, "name", out var name) || !IsValidSkillName(name)) return false;
        if (!TryString(entry, "description", out var description) || description!.Length > 1024) return false;
        if (!TryString(entry, "type", out var type) || (type is not "skill-md" and not "archive")) return false;
        if (!TryString(entry, "url", out var url)) return false;
        if (!TryString(entry, "digest", out var digest) ||
            !System.Text.RegularExpressions.Regex.IsMatch(digest!, "^sha256:[a-f0-9]{64}$"))
        {
            return false;
        }

        if (!Uri.TryCreate(new Uri(indexUrl), url, out var artifactUri)) return false;

        result = new NormalizedEntry(
            Name: name!,
            Description: description!,
            Version: "0.2.0",
            Type: type,
            ArtifactUrl: artifactUri.ToString(),
            Digest: digest,
            LegacyFiles: null,
            LegacyBaseUrl: null,
            LegacyWellKnownPath: null);
        return true;
    }

    private static bool TryParseV1Entry(JsonElement entry, string legacyBaseUrl, string wellKnownPath, out NormalizedEntry result)
    {
        result = default!;
        if (entry.ValueKind != JsonValueKind.Object) return false;

        if (!TryString(entry, "name", out var name) || !IsValidSkillName(name)) return false;
        if (!TryString(entry, "description", out var description)) return false;
        if (!entry.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Array) return false;

        var files = new List<string>();
        foreach (var file in filesEl.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.String) return false;
            var path = file.GetString()!;
            if (!IsSafeLegacyFilePath(path)) return false;
            files.Add(path);
        }

        if (files.Count == 0 || !files.Any(f => string.Equals(f, "SKILL.md", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        result = new NormalizedEntry(
            Name: name!,
            Description: description!,
            Version: "0.1.0",
            Type: null,
            ArtifactUrl: null,
            Digest: null,
            LegacyFiles: files.ToArray(),
            LegacyBaseUrl: legacyBaseUrl,
            LegacyWellKnownPath: wellKnownPath);
        return true;
    }

    private static string GetLegacySkillBaseUrl(string indexUrl, string wellKnownPath)
    {
        var marker = $"/{wellKnownPath}/index.json";
        return indexUrl.EndsWith(marker, StringComparison.Ordinal)
            ? indexUrl[..^marker.Length]
            : indexUrl;
    }

    // ─── Materialization ─────────────────────────────────────────────────────

    private static async Task MaterializeAsync(
        NormalizedEntry entry,
        string destDir,
        HttpClient client,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (entry.Version == "0.1.0")
        {
            await MaterializeLegacyAsync(entry, destDir, client, log, cancellationToken).ConfigureAwait(false);
            return;
        }

        log?.Invoke($"  fetching {entry.Name} ({entry.Type})");
        var bytes = await client.GetByteArrayAsync(entry.ArtifactUrl!, cancellationToken).ConfigureAwait(false);

        var actualDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualDigest, entry.Digest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Digest mismatch for {entry.Name}: expected {entry.Digest}, got {actualDigest}");
        }

        if (entry.Type == "skill-md")
        {
            await File.WriteAllBytesAsync(Path.Combine(destDir, "SKILL.md"), bytes, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // archive: zip or tar.gz - validated and extracted by the shared helper.
        var artifactUrl = entry.ArtifactUrl!;
        var kind = ArchiveExtractor.DetectKind(bytes, artifactUrl);
        if (kind == ArchiveKind.Unknown)
        {
            throw new ArchiveValidationException($"Unsupported archive format for {entry.Name} ({artifactUrl}).");
        }
        await ArchiveExtractor.ExtractAsync(bytes, destDir, kind, ArchiveLimits.WellKnown, cancellationToken)
            .ConfigureAwait(false);

        if (!File.Exists(Path.Combine(destDir, "SKILL.md")))
        {
            throw new ArchiveValidationException($"Archive for {entry.Name} did not contain a root SKILL.md.");
        }
    }

    private static async Task MaterializeLegacyAsync(
        NormalizedEntry entry,
        string destDir,
        HttpClient client,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var skillBase = $"{entry.LegacyBaseUrl!.TrimEnd('/')}/{entry.LegacyWellKnownPath}/{entry.Name}";
        log?.Invoke($"  fetching {entry.Name} (v0.1)");

        foreach (var rel in entry.LegacyFiles!)
        {
            var url = $"{skillBase}/{rel}";
            var dest = Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                using var resp = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    // SKILL.md must exist; other files are best-effort like upstream.
                    if (string.Equals(rel, "SKILL.md", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Failed to fetch required SKILL.md for {entry.Name} from {url} ({(int)resp.StatusCode})");
                    }
                    continue;
                }
                await using var src = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fs = File.Create(dest);
                await src.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }
            catch when (!string.Equals(rel, "SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                // Match upstream legacy tolerance.
            }
        }
    }

    // ─── Validation helpers ──────────────────────────────────────────────────

    private static bool TryString(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool IsValidSkillName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name.Length is >= 1 and <= 64 &&
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9-]+$") &&
        !name.StartsWith('-') && !name.EndsWith('-') && !name.Contains("--", StringComparison.Ordinal);

    private static bool IsSafeLegacyFilePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.StartsWith('/') || path.StartsWith('\\')) return false;
        if (path.Contains("..", StringComparison.Ordinal)) return false;
        if (path.Contains('\0', StringComparison.Ordinal)) return false;
        return true;
    }
}
