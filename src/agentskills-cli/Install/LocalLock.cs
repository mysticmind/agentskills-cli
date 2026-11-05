using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentSkills.Install;

public sealed class LocalLockEntry
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("sourceType")] public string SourceType { get; set; } = string.Empty;
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("skillPath")] public string? SkillPath { get; set; }

    /// <summary>
    /// Where the source was fetched from: the NuGet feed or npm registry for packages, the clone
    /// URL for git sources, a <c>file://</c> URI for a local package file. Lets <c>restore</c> and
    /// <c>update</c> go back to the same place (e.g. a self-hosted GitLab, or a private feed).
    /// Same meaning as the global lock's <c>sourceUrl</c>.
    /// </summary>
    [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; set; }

    /// <summary>SHA-256 content hash of the skill folder at install time (<see cref="LocalLock.ComputeFolderHash"/>).</summary>
    [JsonPropertyName("computedHash")] public string ComputedHash { get; set; } = string.Empty;

    /// <summary>The skill's name in its source when installed under an alias (<c>add --as</c>); null otherwise.</summary>
    [JsonPropertyName("originalName")] public string? OriginalName { get; set; }

    /// <summary>Fields written by other tools; kept so a rewrite never drops them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LocalLockDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = LocalLock.CurrentVersion;
    [JsonPropertyName("skills")] public SortedDictionary<string, LocalLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Set when the file was written by a newer tool; saving is refused.</summary>
    [JsonIgnore] public string? ReadOnlyReason { get; set; }
}

/// <summary>
/// The project lock (<c>./skills-lock.json</c>). Tracks <b>project-scope</b> installs only;
/// meant to be committed so <c>agentskills-cli restore</c> can rebuild a checkout.
/// Format is shared with upstream <c>vercel-labs/skills</c>.
/// </summary>
public static class LocalLock
{
    public const string FileName = "skills-lock.json";
    public const int CurrentVersion = 1;

    public static string GetLockPath(string? cwd = null) =>
        Path.Combine(cwd ?? Directory.GetCurrentDirectory(), FileName);

    public static LocalLockDocument Load(string? cwd = null)
    {
        var path = GetLockPath(cwd);
        var (status, doc) = LockFileIO.Read<LocalLockDocument>(path);
        if (status != LockFileIO.ReadStatus.Ok || doc is null)
        {
            return new LocalLockDocument();
        }

        doc.Skills ??= new SortedDictionary<string, LocalLockEntry>(StringComparer.Ordinal);
        if (doc.Version > CurrentVersion)
        {
            doc.ReadOnlyReason =
                $"{path} uses lock format v{doc.Version}, newer than this tool supports (v{CurrentVersion}). " +
                "Upgrade agentskills-cli before changing project skills.";
        }
        return doc;
    }

    public static void Save(LocalLockDocument doc, string? cwd = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var path = GetLockPath(cwd);
        if (doc.ReadOnlyReason is not null)
        {
            throw new LockFileException(path, doc.ReadOnlyReason);
        }
        LockFileIO.WriteAtomic(path, doc, JsonOptions);
    }

    public static void Remove(string skillName, string? cwd = null)
    {
        var doc = Load(cwd);
        if (doc.Skills.Remove(skillName))
        {
            Save(doc, cwd);
        }
    }

    public static string ComputeFolderHash(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        using var ms = new MemoryStream();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            ms.Write(Encoding.UTF8.GetBytes(rel + "\0"));
            ms.Write(File.ReadAllBytes(file));
            ms.WriteByte(0);
        }
        ms.Position = 0;
        return Convert.ToHexString(SHA256.HashData(ms)).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
