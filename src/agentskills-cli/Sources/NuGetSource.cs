using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace AgentSkills.Sources;

public sealed class NuGetSource : ISkillSource
{
    private readonly string _stagingRoot;

    public ParsedSource Parsed { get; }
    public string RootPath => _stagingRoot;
    public string? Subpath { get; }
    public string DisplaySource { get; }
    public string SourceTypeLabel => "nuget";
    public string? SourceUrl { get; }
    public string? Reference => Parsed.PackageVersion;

    private NuGetSource(ParsedSource parsed, string stagingRoot, string subpath, string sourceUrl, string display)
    {
        Parsed = parsed;
        _stagingRoot = stagingRoot;
        Subpath = subpath;
        SourceUrl = sourceUrl;
        DisplaySource = display;
    }

    public static async Task<NuGetSource> DownloadAsync(
        ParsedSource parsed,
        string? overrideSource = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        // Local .nupkg file path - skip feed resolve and extract directly. The
        // package id and version come from the .nuspec inside the archive.
        if (!string.IsNullOrEmpty(parsed.LocalPath) && File.Exists(parsed.LocalPath))
        {
            return await ExtractLocalNupkgAsync(parsed, parsed.LocalPath, log, cancellationToken)
                .ConfigureAwait(false);
        }

        var packageId = parsed.PackageId
            ?? throw new ArgumentException("Parsed source is not a NuGet source", nameof(parsed));

        var settings = Settings.LoadDefaultSettings(root: null);
        var packageSourceProvider = new PackageSourceProvider(settings);
        var sources = (overrideSource is null
                ? packageSourceProvider.LoadPackageSources()
                : new[] { new PackageSource(overrideSource) })
            .Where(s => s.IsEnabled)
            .ToList();

        if (sources.Count == 0)
        {
            throw new InvalidOperationException(
                "No enabled NuGet sources found. Add one with `dotnet nuget add source` or pass --nuget-source.");
        }

        var providers = new List<Lazy<INuGetResourceProvider>>();
        providers.AddRange(Repository.Provider.GetCoreV3());

        var logger = new SpectreNuGetLogger(log);
        using var cache = new SourceCacheContext { NoCache = false, DirectDownload = false };

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"agentskills-cli-nuget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var errors = new List<string>();
            foreach (var packageSource in sources)
            {
                try
                {
                    var repo = new SourceRepository(packageSource, providers);
                    var finder = await repo.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
                    if (finder is null)
                    {
                        continue;
                    }

                    NuGetVersion? resolved = null;
                    if (!string.IsNullOrEmpty(parsed.PackageVersion))
                    {
                        if (!NuGetVersion.TryParse(parsed.PackageVersion, out resolved))
                        {
                            throw new ArgumentException(
                                $"Invalid NuGet version: {parsed.PackageVersion}",
                                nameof(parsed));
                        }
                    }
                    else
                    {
                        var versions = await finder.GetAllVersionsAsync(packageId, cache, logger, cancellationToken).ConfigureAwait(false);
                        resolved = versions
                            .Where(v => !v.IsPrerelease)
                            .DefaultIfEmpty()
                            .Max() ?? versions.DefaultIfEmpty().Max();

                        if (resolved is null)
                        {
                            log?.Invoke($"  {packageSource.Name}: no versions for {packageId}");
                            continue;
                        }
                    }

                    log?.Invoke($"  resolving {packageId} {resolved} from {packageSource.Name}");

                    var nupkgPath = Path.Combine(stagingRoot, $"{packageId}.{resolved}.nupkg");
                    using (var nupkgStream = File.Create(nupkgPath))
                    {
                        var success = await finder.CopyNupkgToStreamAsync(
                            packageId,
                            resolved,
                            nupkgStream,
                            cache,
                            logger,
                            cancellationToken).ConfigureAwait(false);

                        if (!success)
                        {
                            File.Delete(nupkgPath);
                            log?.Invoke($"  {packageSource.Name}: failed to download {packageId} {resolved}");
                            continue;
                        }
                    }

                    var extractDir = Path.Combine(stagingRoot, "extracted");
                    Directory.CreateDirectory(extractDir);

                    using (var reader = new PackageArchiveReader(File.OpenRead(nupkgPath)))
                    {
                        foreach (var file in reader.GetFiles())
                        {
                            var dest = Path.Combine(extractDir, file.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            using var src = reader.GetStream(file);
                            using var fs = File.Create(dest);
                            await src.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    File.Delete(nupkgPath);

                    var conventional = Path.Combine(extractDir, "contentFiles", "any", "any", "skills");
                    var subpath = Directory.Exists(conventional)
                        ? Path.Combine("contentFiles", "any", "any", "skills")
                        : null;

                    var resolvedParsed = parsed with { PackageVersion = resolved.ToFullString() };
                    return new NuGetSource(
                        resolvedParsed,
                        extractDir,
                        subpath ?? string.Empty,
                        packageSource.Source,
                        $"nuget:{packageId}@{resolved}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{packageSource.Name} ({packageSource.Source}): {ex.Message}");
                    log?.Invoke($"  {packageSource.Name}: {ex.Message}");
                }
            }

            // Build a descriptive error message so the user knows *which* source(s) failed
            // and *why* without having to dig into -v / --trace verbose output.
            var sourceList = string.Join(", ", sources.Select(s => s.Name));
            var message = errors.Count == 0
                ? $"Failed to download NuGet package '{packageId}': not found on any configured source ({sourceList}). " +
                  $"Check the package id, or add the right feed with: dotnet nuget add source <URL> -n <name>"
                : $"Failed to download NuGet package '{packageId}' from any configured source:" +
                  Environment.NewLine +
                  string.Join(Environment.NewLine, errors.Select(e => $"  - {e}"));
            throw new InvalidOperationException(message);
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
    /// Extracts an existing <c>.nupkg</c> file on disk into a staging directory and
    /// returns a <see cref="NuGetSource"/> just like <see cref="DownloadAsync"/> would
    /// for a feed-resolved package. Package id + version are read from the embedded
    /// <c>.nuspec</c> so the lock entry looks identical to a feed-resolved install.
    /// </summary>
    private static async Task<NuGetSource> ExtractLocalNupkgAsync(
        ParsedSource parsed,
        string nupkgPath,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"agentskills-cli-nuget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var extractDir = Path.Combine(stagingRoot, "extracted");
            Directory.CreateDirectory(extractDir);

            string packageId;
            string packageVersion;

            await using (var nupkgStream = File.OpenRead(nupkgPath))
            using (var reader = new PackageArchiveReader(nupkgStream))
            {
                var identity = reader.GetIdentity();
                packageId = identity.Id;
                packageVersion = identity.Version.ToFullString();
                log?.Invoke($"  extracting local nupkg {packageId} {packageVersion}");

                foreach (var file in reader.GetFiles())
                {
                    var dest = Path.Combine(extractDir, file.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var src = reader.GetStream(file);
                    using var fs = File.Create(dest);
                    await src.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
                }
            }

            var conventional = Path.Combine(extractDir, "contentFiles", "any", "any", "skills");
            var subpath = Directory.Exists(conventional)
                ? Path.Combine("contentFiles", "any", "any", "skills")
                : null;

            var resolvedParsed = parsed with
            {
                PackageId = packageId,
                PackageVersion = packageVersion,
            };

            return new NuGetSource(
                resolvedParsed,
                extractDir,
                subpath ?? string.Empty,
                new Uri(nupkgPath).AbsoluteUri,
                $"nuget:{packageId}@{packageVersion}");
        }
        catch
        {
            try { Directory.Delete(stagingRoot, recursive: true); } catch { /* swallow */ }
            throw;
        }
    }

    private sealed class SpectreNuGetLogger(Action<string>? sink) : LoggerBase
    {
        public override void Log(ILogMessage message)
        {
            if (message.Level >= LogLevel.Warning)
            {
                sink?.Invoke($"  [{message.Level}] {message.Message}");
            }
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
