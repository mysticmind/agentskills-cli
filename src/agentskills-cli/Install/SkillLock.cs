using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentSkills.Install;

public sealed class GlobalLockEntry
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("sourceType")] public string SourceType { get; set; } = string.Empty;
    [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; set; }
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("skillPath")] public string? SkillPath { get; set; }

    /// <summary>
    /// Change-detection baseline. For git-backed sources (GitHub, GitLab, generic git) this
    /// is the git tree SHA of the skill folder, which matches upstream <c>vercel-labs/skills</c>.
    /// For other sources (well-known, local, npm, NuGet) it is the SHA-256 content hash of
    /// the skill folder (<see cref="LocalLock.ComputeFolderHash"/>). Empty when unknown.
    /// </summary>
    [JsonPropertyName("skillFolderHash")] public string SkillFolderHash { get; set; } = string.Empty;
    [JsonPropertyName("installedAt")] public string InstalledAt { get; set; } = string.Empty;
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    [JsonPropertyName("pluginName")] public string? PluginName { get; set; }

    /// <summary>
    /// Set when the skill was installed under an alias (<c>add --as</c>): the skill's name in
    /// its source. The entry's key is the installed (alias) name. Null when not aliased.
    /// </summary>
    [JsonPropertyName("originalName")] public string? OriginalName { get; set; }

    /// <summary>Fields written by other tools; kept so a rewrite never drops them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class GlobalLockDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = SkillLock.CurrentVersion;
    [JsonPropertyName("skills")] public SortedDictionary<string, GlobalLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("lastSelectedAgents")] public List<string>? LastSelectedAgents { get; set; }

    /// <summary>
    /// Set when the file on disk was written by a newer tool. Such a document can be read
    /// but <see cref="SkillLock.Save(GlobalLockDocument)"/> refuses to overwrite it.
    /// </summary>
    [JsonIgnore] public string? ReadOnlyReason { get; set; }
}

/// <summary>
/// The global lock (<c>~/.agents/.skill-lock.json</c>, or <c>$XDG_STATE_HOME/skills/.skill-lock.json</c>).
/// Tracks <b>globally installed</b> skills only; project installs are tracked in
/// <see cref="LocalLock"/>. Format is shared with upstream <c>vercel-labs/skills</c>.
/// </summary>
public static class SkillLock
{
    public const int CurrentVersion = 3;

    public static string GetLockPath()
    {
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgState))
        {
            return Path.Combine(xdgState, "skills", ".skill-lock.json");
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".agents", ".skill-lock.json");
    }

    public static GlobalLockDocument Load() => Load(GetLockPath());

    public static GlobalLockDocument Load(string path)
    {
        var (status, doc) = LockFileIO.Read<GlobalLockDocument>(path);
        if (status != LockFileIO.ReadStatus.Ok || doc is null)
        {
            return new GlobalLockDocument();
        }

        doc.Skills ??= new SortedDictionary<string, GlobalLockEntry>(StringComparer.Ordinal);

        if (doc.Version > CurrentVersion)
        {
            doc.ReadOnlyReason =
                $"{path} uses lock format v{doc.Version}, newer than this tool supports (v{CurrentVersion}). " +
                "Upgrade agentskills-cli before changing global skills.";
            return doc;
        }

        if (doc.Version < CurrentVersion)
        {
            // Older formats are reset (upstream does the same). Keep a copy so nothing is lost.
            var version = doc.Version;
            LockFileIO.BackupOnce(path, $"v{version}", backup =>
                $"{path} uses old lock format v{version}; starting a fresh v{CurrentVersion} lock. The old file was saved to {backup}.");
            return new GlobalLockDocument();
        }

        return doc;
    }

    public static void Save(GlobalLockDocument doc) => Save(doc, GetLockPath());

    public static void Save(GlobalLockDocument doc, string path)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.ReadOnlyReason is not null)
        {
            throw new LockFileException(path, doc.ReadOnlyReason);
        }
        LockFileIO.WriteAtomic(path, doc, JsonOptions);
    }

    public static void Remove(string skillName)
    {
        var doc = Load();
        if (doc.Skills.Remove(skillName))
        {
            Save(doc);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
