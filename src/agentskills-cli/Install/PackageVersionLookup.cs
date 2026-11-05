using AgentSkills.Sources;
using NuGet.Versioning;

namespace AgentSkills.Install;

/// <summary>How far <c>update</c> may move a NuGet/npm package (the <c>--version-lock</c> option).</summary>
public enum VersionLock
{
    /// <summary>Any newer version.</summary>
    None,

    /// <summary>Stay on the current major version (1.4.0 → 1.9.2, never 2.x).</summary>
    Major,

    /// <summary>Stay on the current major.minor (1.4.0 → 1.4.7, never 1.5.x).</summary>
    Minor,
}

/// <summary>Every published version of a package, plus npm's <c>dist-tags.latest</c> when there is one.</summary>
public sealed record PackageVersions(IReadOnlyList<string> Versions, string? DistTagLatest = null);

/// <summary>
/// Lists a NuGet or npm package's versions without downloading the package.
/// Swappable in DI (tests, or a private-registry proxy).
/// </summary>
public interface IPackageVersionLookup
{
    /// <param name="sourceType"><c>nuget</c> or <c>npm</c> (the lock's <c>sourceType</c>).</param>
    Task<PackageVersions> GetVersionsAsync(
        string sourceType,
        string packageId,
        ResolveOptions options,
        CancellationToken cancellationToken);
}

public sealed class PackageVersionLookup : IPackageVersionLookup
{
    public async Task<PackageVersions> GetVersionsAsync(
        string sourceType,
        string packageId,
        ResolveOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        switch (sourceType)
        {
            case "nuget":
            {
                var versions = await NuGetSource.GetAllVersionsAsync(packageId, options.NuGetSource, cancellationToken)
                    .ConfigureAwait(false);
                return new PackageVersions(versions.Select(v => v.ToFullString()).ToList());
            }
            case "npm":
            {
                var (latest, versions) = await NpmSource.GetVersionsAsync(packageId, options.NpmRegistry, cancellationToken)
                    .ConfigureAwait(false);
                return new PackageVersions(versions, latest);
            }
            default:
                throw new ArgumentException($"Not a package source type: {sourceType}", nameof(sourceType));
        }
    }

    /// <summary>
    /// Picks the version <c>update</c> should move to.
    /// <list type="bullet">
    ///   <item>Prereleases are considered only when <paramref name="includePrerelease"/> is set.</item>
    ///   <item><paramref name="versionLock"/> keeps the result on <paramref name="current"/>'s major
    ///   (or major.minor). Ignored when <paramref name="current"/> isn't a valid version.</item>
    ///   <item>npm without a lock or prereleases: <c>dist-tags.latest</c> wins (npm semantics), so a
    ///   higher version published under another tag isn't picked up.</item>
    /// </list>
    /// Returns null when nothing qualifies.
    /// </summary>
    public static string? Pick(
        PackageVersions available,
        string? current,
        bool includePrerelease,
        VersionLock versionLock = VersionLock.None)
    {
        ArgumentNullException.ThrowIfNull(available);

        NuGetVersion? currentVersion = null;
        if (versionLock != VersionLock.None && !NuGetVersion.TryParse(current, out currentVersion))
        {
            versionLock = VersionLock.None;
        }

        if (versionLock == VersionLock.None && !includePrerelease && !string.IsNullOrEmpty(available.DistTagLatest))
        {
            return available.DistTagLatest;
        }

        var candidates = available.Versions
            .Concat(string.IsNullOrEmpty(available.DistTagLatest) ? [] : [available.DistTagLatest])
            .Select(v => NuGetVersion.TryParse(v, out var parsed) ? (Raw: v, Version: parsed) : default)
            .Where(t => t.Version is not null)
            .Where(t => includePrerelease || !t.Version!.IsPrerelease)
            .Where(t => versionLock switch
            {
                VersionLock.Major => t.Version!.Major == currentVersion!.Major,
                VersionLock.Minor => t.Version!.Major == currentVersion!.Major && t.Version.Minor == currentVersion.Minor,
                _ => true,
            })
            .ToList();

        return candidates.Count == 0 ? null : candidates.MaxBy(t => t.Version)!.Raw;
    }

    /// <summary>True when <paramref name="candidate"/> is a strictly newer SemVer/NuGet version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? current, string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (string.IsNullOrEmpty(current)) return true;
        if (NuGetVersion.TryParse(current, out var c) && NuGetVersion.TryParse(candidate, out var n))
        {
            return n > c;
        }
        return !string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the version string parses as a prerelease.</summary>
    public static bool IsPrerelease(string? version) =>
        version is not null && NuGetVersion.TryParse(version, out var v) && v.IsPrerelease;
}
