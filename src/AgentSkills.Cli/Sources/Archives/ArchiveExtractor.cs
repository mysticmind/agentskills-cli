using System.Formats.Tar;
using System.IO.Compression;

namespace AgentSkills.Sources.Archives;

/// <summary>
/// Single source of truth for safely unpacking <c>.zip</c> and <c>.tar.gz</c> payloads
/// (npm tarballs, well-known archives). Enforces shared safety limits and rejects
/// dangerous entries (symlinks, hard links, path traversal, absolute paths) before
/// writing anything to disk.
/// </summary>
public static class ArchiveExtractor
{
    public static async Task ExtractAsync(
        byte[] bytes,
        string destinationDir,
        ArchiveKind kind,
        ArchiveLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrEmpty(destinationDir);
        ArgumentNullException.ThrowIfNull(limits);

        switch (kind)
        {
            case ArchiveKind.Zip:
                ExtractZip(bytes, destinationDir, limits);
                break;
            case ArchiveKind.TarGz:
                await ExtractTarGzAsync(bytes, destinationDir, limits, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArchiveValidationException($"Unsupported archive kind: {kind}");
        }
    }

    public static ArchiveKind DetectKind(byte[] bytes, string? urlHint = null)
    {
        if (bytes is { Length: >= 2 } && bytes[0] == 0x50 && bytes[1] == 0x4b) return ArchiveKind.Zip;
        if (bytes is { Length: >= 2 } && bytes[0] == 0x1f && bytes[1] == 0x8b) return ArchiveKind.TarGz;

        if (urlHint is not null)
        {
            if (urlHint.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ArchiveKind.Zip;
            if (urlHint.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                || urlHint.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveKind.TarGz;
            }
        }
        return ArchiveKind.Unknown;
    }

    private static void ExtractZip(byte[] bytes, string destDir, ArchiveLimits limits)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        long total = 0;
        var fileCount = 0;
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory placeholder
            var path = SafeArchivePath.Normalize(entry.FullName)
                ?? throw new ArchiveValidationException($"Unsafe archive path: {entry.FullName}");

            total += entry.Length;
            fileCount++;
            limits.Enforce(total, fileCount);

            var dest = Path.Combine(destDir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static async Task ExtractTarGzAsync(byte[] bytes, string destDir, ArchiveLimits limits, CancellationToken ct)
    {
        using var ms = new MemoryStream(bytes);
        await using var gz = new GZipStream(ms, CompressionMode.Decompress);
        await using var reader = new TarReader(gz, leaveOpen: false);

        long total = 0;
        var fileCount = 0;
        while (await reader.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                throw new ArchiveValidationException("Archive links are not supported.");
            }
            if (entry.EntryType != TarEntryType.RegularFile && entry.EntryType != TarEntryType.V7RegularFile)
            {
                continue;
            }

            var path = SafeArchivePath.Normalize(entry.Name)
                ?? throw new ArchiveValidationException($"Unsafe archive path: {entry.Name}");

            total += entry.Length;
            fileCount++;
            limits.Enforce(total, fileCount);

            var dest = Path.Combine(destDir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await entry.ExtractToFileAsync(dest, overwrite: true, ct).ConfigureAwait(false);
        }
    }
}

public enum ArchiveKind { Unknown, Zip, TarGz }

public sealed record ArchiveLimits(long MaxBytes, int MaxFiles)
{
    /// <summary>Default for well-known endpoints (RFC 8615): 50 MB, 1000 files.</summary>
    public static ArchiveLimits WellKnown { get; } = new(50L * 1024 * 1024, 1000);

    /// <summary>Default for npm tarballs (larger packages are common): 100 MB, 4000 files.</summary>
    public static ArchiveLimits Npm { get; } = new(100L * 1024 * 1024, 4000);

    public void Enforce(long totalBytes, int fileCount)
    {
        if (totalBytes > MaxBytes)
        {
            throw new ArchiveValidationException(
                $"Archive exceeds maximum unpacked size of {MaxBytes / 1024 / 1024} MB.");
        }
        if (fileCount > MaxFiles)
        {
            throw new ArchiveValidationException(
                $"Archive contains more than {MaxFiles} files.");
        }
    }
}

/// <summary>
/// Validates and normalizes a path coming from inside a tar/zip entry. Rejects
/// absolute paths, drive letters, backslashes, NUL bytes, and any <c>..</c> segment.
/// </summary>
public static class SafeArchivePath
{
    public static string? Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Contains('\0', StringComparison.Ordinal)) return null;
        if (raw.StartsWith('/') || raw.StartsWith('\\')) return null;
        if (raw.Length >= 2 && char.IsLetter(raw[0]) && raw[1] == ':') return null;
        if (raw.Contains('\\', StringComparison.Ordinal)) return null;

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (parts.Any(p => p is "." or "..")) return null;
        return string.Join('/', parts);
    }
}
