using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skills.Install;

public sealed class GlobalLockEntry
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("sourceType")] public string SourceType { get; set; } = string.Empty;
    [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; set; }
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("skillPath")] public string? SkillPath { get; set; }
    [JsonPropertyName("skillFolderHash")] public string SkillFolderHash { get; set; } = string.Empty;
    [JsonPropertyName("installedAt")] public string InstalledAt { get; set; } = string.Empty;
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    [JsonPropertyName("pluginName")] public string? PluginName { get; set; }
}

public sealed class GlobalLockDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 3;
    [JsonPropertyName("skills")] public SortedDictionary<string, GlobalLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("lastSelectedAgents")] public List<string>? LastSelectedAgents { get; set; }
}

public static class SkillLock
{
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

    public static GlobalLockDocument Load()
    {
        var path = GetLockPath();
        if (!File.Exists(path))
        {
            return new GlobalLockDocument();
        }
        try
        {
            var doc = JsonSerializer.Deserialize<GlobalLockDocument>(File.ReadAllText(path));
            if (doc is null || doc.Version != 3)
            {
                return new GlobalLockDocument();
            }
            return doc;
        }
        catch
        {
            return new GlobalLockDocument();
        }
    }

    public static void Save(GlobalLockDocument doc)
    {
        var path = GetLockPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
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
