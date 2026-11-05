using System.Globalization;
using System.Text.Json;

namespace AgentSkills.Install;

/// <summary>
/// Shared read/write plumbing for the global and project lock files. Centralizes the
/// three safety rules both locks follow:
/// <list type="number">
///   <item>An unreadable (corrupt) lock is backed up next to the original and a warning
///   is printed, so the next save never silently discards the user's tracking data.</item>
///   <item>A lock whose <c>version</c> is newer than this tool understands is loaded
///   read-only: saving it throws instead of downgrading another tool's file.</item>
///   <item>Saves are atomic (write a temp file, then move it over the original).</item>
/// </list>
/// </summary>
public static class LockFileIO
{
    /// <summary>
    /// Sink for lock-file warnings. Defaults to stderr so warnings never mix with
    /// machine-readable stdout (e.g. <c>--json</c>). Tests swap this to capture output.
    /// </summary>
    public static Action<string> Warn { get; set; } = message => Console.Error.WriteLine($"warning: {message}");

    /// <summary>Outcome of reading a lock file from disk.</summary>
    public enum ReadStatus { Missing, Ok, Corrupt }

    /// <summary>
    /// Reads and deserializes <paramref name="path"/>. On a parse failure the file is
    /// copied to <c>&lt;path&gt;.corrupt-&lt;timestamp&gt;</c> and a warning is emitted.
    /// </summary>
    public static (ReadStatus Status, T? Document) Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return (ReadStatus.Missing, null);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new LockFileException(path, $"Could not read lock file {path}: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new LockFileException(path, $"Could not read lock file {path}: {ex.Message}", ex);
        }

        try
        {
            var doc = JsonSerializer.Deserialize<T>(text);
            if (doc is not null) return (ReadStatus.Ok, doc);
        }
        catch (JsonException)
        {
            // handled below
        }

        BackupOnce(path, "corrupt", backup =>
            $"Lock file {path} could not be parsed. A copy was saved to {backup} and the lock is treated as empty.");
        return (ReadStatus.Corrupt, null);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> BackedUp =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Backs up <paramref name="path"/> and emits the warning only the first time a given
    /// (path, reason) pair is seen in this process. Commands load the locks several times;
    /// without this each load would create another backup and repeat the warning.
    /// </summary>
    public static string BackupOnce(string path, string reason, Func<string, string> warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        var key = $"{Path.GetFullPath(path)}|{reason}";
        var created = false;
        var backup = BackedUp.GetOrAdd(key, _ =>
        {
            created = true;
            return Backup(path, reason);
        });
        if (created) Warn(warning(backup));
        return backup;
    }

    /// <summary>Forgets which files were already backed up. Test hook.</summary>
    internal static void ResetBackupTracking() => BackedUp.Clear();

    /// <summary>
    /// Copies <paramref name="path"/> to a timestamped sibling and returns the backup path.
    /// Never overwrites an existing backup.
    /// </summary>
    public static string Backup(string path, string reason)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var backup = $"{path}.{reason}-{stamp}";
        var n = 1;
        while (File.Exists(backup))
        {
            backup = $"{path}.{reason}-{stamp}-{n++}";
        }
        try
        {
            File.Copy(path, backup);
        }
        catch (IOException ex)
        {
            Warn($"Could not back up {path}: {ex.Message}");
        }
        return backup;
    }

    /// <summary>Serializes <paramref name="document"/> and atomically replaces <paramref name="path"/>.</summary>
    public static void WriteAtomic<T>(string path, T document, JsonSerializerOptions options)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document, options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw new LockFileException(path, $"Could not write lock file {path}: {ex.Message}", ex);
        }
    }
}
