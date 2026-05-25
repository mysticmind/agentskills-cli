using System.Net.Http;
using System.Text.Json;
using AgentSkills.Sources.Archives;

namespace AgentSkills.Sources;

/// <summary>
/// Fetches a package from any npm-compatible registry - public or private - by reading the
/// user's <c>.npmrc</c> for the default registry, per-scope registry overrides, and auth
/// tokens. Downloads the tarball, extracts it to a staging directory, then lets the regular
/// install pipeline take over.
/// </summary>
public sealed class NpmSource : ISkillSource
{
    private readonly string _stagingRoot;

    public ParsedSource Parsed { get; }
    public string RootPath => _stagingRoot;
    public string? Subpath { get; }
    public string DisplaySource { get; }
    public string SourceTypeLabel => "npm";
    public string? SourceUrl { get; }
    public string? Reference => Parsed.PackageVersion;

    private NpmSource(ParsedSource parsed, string stagingRoot, string subpath, string sourceUrl, string display)
    {
        Parsed = parsed;
        _stagingRoot = stagingRoot;
        Subpath = subpath;
        SourceUrl = sourceUrl;
        DisplaySource = display;
    }

    public static async Task<NpmSource> DownloadAsync(
        ParsedSource parsed,
        string? overrideRegistry = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        // Local .tgz / .tar.gz tarball - skip the registry fetch and extract
        // directly. Name and version come from the package.json inside.
        if (!string.IsNullOrEmpty(parsed.LocalPath) && File.Exists(parsed.LocalPath))
        {
            return await ExtractLocalTarballAsync(parsed, parsed.LocalPath, log, cancellationToken)
                .ConfigureAwait(false);
        }

        var packageId = parsed.PackageId
            ?? throw new ArgumentException("Parsed source is not an npm source", nameof(parsed));

        var npmrc = NpmRegistry.Load(overrideRegistry: overrideRegistry);
        var registry = npmrc.ResolveRegistry(packageId);
        using var http = npmrc.CreateClient();
        var auth = npmrc.GetAuthHeader(registry);

        log?.Invoke($"  using registry {registry}{(auth is null ? "" : " (auth)")}");

        var packumentUrl = $"{registry.TrimEnd('/')}/{EscapePackageId(packageId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, packumentUrl);
        if (auth is not null) request.Headers.Authorization = auth;

        using var packumentResponse = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!packumentResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to fetch packument for '{packageId}' from {registry}: HTTP {(int)packumentResponse.StatusCode}");
        }

        var json = await packumentResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var (version, tarballUrl) = ResolveVersionAndTarball(json, packageId, parsed.PackageVersion);
        log?.Invoke($"  resolving {packageId}@{version} → {tarballUrl}");

        using var tarballRequest = new HttpRequestMessage(HttpMethod.Get, tarballUrl);
        if (auth is not null) tarballRequest.Headers.Authorization = auth;

        using var tarballResponse = await http.SendAsync(tarballRequest, cancellationToken).ConfigureAwait(false);
        if (!tarballResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to download tarball {tarballUrl}: HTTP {(int)tarballResponse.StatusCode}");
        }

        var tarballBytes = await tarballResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"agentskills-cli-npm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            await ArchiveExtractor.ExtractAsync(
                tarballBytes, stagingRoot, ArchiveKind.TarGz, ArchiveLimits.Npm, cancellationToken)
                .ConfigureAwait(false);

            // npm tarballs always root at "package/".
            var packageDir = Path.Combine(stagingRoot, "package");
            var roots = new[]
            {
                Path.Combine(packageDir, "skills"),
                Path.Combine(packageDir, "contentFiles", "any", "any", "skills"),
            };
            string? subpath = null;
            foreach (var candidate in roots)
            {
                if (Directory.Exists(candidate))
                {
                    subpath = Path.GetRelativePath(stagingRoot, candidate).Replace('\\', '/');
                    break;
                }
            }
            subpath ??= Directory.Exists(packageDir) ? "package" : string.Empty;

            var resolvedParsed = parsed with { PackageVersion = version };
            return new NpmSource(
                resolvedParsed,
                stagingRoot,
                subpath,
                registry,
                $"npm:{packageId}@{version}");
        }
        catch
        {
            try { Directory.Delete(stagingRoot, recursive: true); } catch { /* swallow */ }
            throw;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_stagingRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Extracts an existing <c>.tgz</c> / <c>.tar.gz</c> file on disk into a staging
    /// directory and returns an <see cref="NpmSource"/> just like
    /// <see cref="DownloadAsync"/> would for a registry-fetched package. Name + version
    /// are read from <c>package/package.json</c> so the lock entry looks identical to a
    /// registry-resolved install.
    /// </summary>
    private static async Task<NpmSource> ExtractLocalTarballAsync(
        ParsedSource parsed,
        string tarballPath,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(tarballPath, cancellationToken).ConfigureAwait(false);

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"agentskills-cli-npm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            await ArchiveExtractor.ExtractAsync(
                bytes, stagingRoot, ArchiveKind.TarGz, ArchiveLimits.Npm, cancellationToken)
                .ConfigureAwait(false);

            var packageDir = Path.Combine(stagingRoot, "package");
            var packageJsonPath = Path.Combine(packageDir, "package.json");
            string packageId;
            string version;
            if (File.Exists(packageJsonPath))
            {
                await using var pjStream = File.OpenRead(packageJsonPath);
                using var pj = await JsonDocument.ParseAsync(pjStream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                packageId = pj.RootElement.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString()!
                    : Path.GetFileNameWithoutExtension(tarballPath);
                version = pj.RootElement.TryGetProperty("version", out var versionEl) && versionEl.ValueKind == JsonValueKind.String
                    ? versionEl.GetString()!
                    : string.Empty;
            }
            else
            {
                packageId = Path.GetFileNameWithoutExtension(tarballPath);
                version = string.Empty;
            }

            log?.Invoke($"  extracting local tarball {packageId}{(string.IsNullOrEmpty(version) ? "" : $"@{version}")}");

            var roots = new[]
            {
                Path.Combine(packageDir, "skills"),
                Path.Combine(packageDir, "contentFiles", "any", "any", "skills"),
            };
            string? subpath = null;
            foreach (var candidate in roots)
            {
                if (Directory.Exists(candidate))
                {
                    subpath = Path.GetRelativePath(stagingRoot, candidate).Replace('\\', '/');
                    break;
                }
            }
            subpath ??= Directory.Exists(packageDir) ? "package" : string.Empty;

            var resolvedParsed = parsed with
            {
                PackageId = packageId,
                PackageVersion = string.IsNullOrEmpty(version) ? null : version,
            };

            return new NpmSource(
                resolvedParsed,
                stagingRoot,
                subpath,
                new Uri(tarballPath).AbsoluteUri,
                string.IsNullOrEmpty(version) ? $"npm:{packageId}" : $"npm:{packageId}@{version}");
        }
        catch
        {
            try { Directory.Delete(stagingRoot, recursive: true); } catch { /* swallow */ }
            throw;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// npm URL-encodes the package id with the scope forward-slash preserved
    /// (e.g. <c>@my-org/foo</c> → <c>@my-org%2ffoo</c>).
    /// </summary>
    private static string EscapePackageId(string packageId)
    {
        if (!packageId.StartsWith('@')) return Uri.EscapeDataString(packageId);
        var slash = packageId.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0) return Uri.EscapeDataString(packageId);
        var scope = packageId[..slash];
        var name = packageId[(slash + 1)..];
        return $"{scope}%2f{Uri.EscapeDataString(name)}";
    }

    private static (string Version, string TarballUrl) ResolveVersionAndTarball(
        string packumentJson, string packageId, string? requestedVersion)
    {
        using var doc = JsonDocument.Parse(packumentJson);
        var root = doc.RootElement;

        string version;
        if (string.IsNullOrEmpty(requestedVersion))
        {
            if (!root.TryGetProperty("dist-tags", out var distTags) ||
                !distTags.TryGetProperty("latest", out var latest))
            {
                throw new InvalidOperationException(
                    $"Packument for {packageId} has no dist-tags.latest");
            }
            version = latest.GetString()
                ?? throw new InvalidOperationException("Invalid dist-tags.latest value");
        }
        else if (root.TryGetProperty("dist-tags", out var distTags) &&
                 distTags.TryGetProperty(requestedVersion, out var tagged) &&
                 tagged.ValueKind == JsonValueKind.String)
        {
            // Honor dist-tags like "next", "beta", etc.
            version = tagged.GetString()!;
        }
        else
        {
            version = requestedVersion;
        }

        if (!root.TryGetProperty("versions", out var versions) ||
            !versions.TryGetProperty(version, out var v))
        {
            throw new InvalidOperationException($"Version {version} not found in packument for {packageId}");
        }

        if (!v.TryGetProperty("dist", out var dist) ||
            !dist.TryGetProperty("tarball", out var tarball) ||
            tarball.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Version {version} of {packageId} has no dist.tarball");
        }

        return (version, tarball.GetString()!);
    }

}
