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

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"skills-net-nuget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            Exception? lastError = null;
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
                    lastError = ex;
                    log?.Invoke($"  {packageSource.Name}: {ex.Message}");
                }
            }

            throw new InvalidOperationException(
                $"Failed to download NuGet package '{packageId}' from any configured source.",
                lastError);
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
