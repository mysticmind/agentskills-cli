using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AgentSkills.Hosting;

/// <summary>Version and runtime facts printed by <c>--version</c> and <c>version</c>.</summary>
public static class VersionInfo
{
    private static readonly Assembly Assembly = typeof(VersionInfo).Assembly;

    /// <summary>The full informational version, e.g. <c>1.1.0+1c6acad0...</c> (SourceLink appends the commit).</summary>
    public static string InformationalVersion { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    /// <summary>The release version without build metadata, e.g. <c>1.1.0</c>.</summary>
    public static string Version { get; } = StripMetadata(InformationalVersion);

    /// <summary>Short commit hash from the build metadata, or null for builds without one.</summary>
    public static string? Commit { get; } = ExtractCommit(InformationalVersion);

    /// <summary>The TFM this build targets, e.g. <c>.NETCoreApp,Version=v10.0</c>.</summary>
    public static string TargetFramework { get; } =
        Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "unknown";

    /// <summary>The runtime actually executing, e.g. <c>.NET 11.0.0</c> (differs from the TFM when rolled forward).</summary>
    public static string Runtime => RuntimeInformation.FrameworkDescription;

    internal static string StripMetadata(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    internal static string? ExtractCommit(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0 || plus == version.Length - 1) return null;
        var meta = version[(plus + 1)..];
        return meta.Length > 7 ? meta[..7] : meta;
    }
}
