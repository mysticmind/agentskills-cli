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
    [JsonPropertyName("computedHash")] public string ComputedHash { get; set; } = string.Empty;
}

public sealed class LocalLockDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("skills")] public SortedDictionary<string, LocalLockEntry> Skills { get; set; } = new(StringComparer.Ordinal);
}

public static class LocalLock
{
    public const string FileName = "skills-lock.json";

    public static string GetLockPath(string? cwd = null) =>
        Path.Combine(cwd ?? Directory.GetCurrentDirectory(), FileName);

    public static LocalLockDocument Load(string? cwd = null)
    {
        var path = GetLockPath(cwd);
        if (!File.Exists(path)) return new LocalLockDocument();
        try
        {
            return JsonSerializer.Deserialize<LocalLockDocument>(File.ReadAllText(path))
                ?? new LocalLockDocument();
        }
        catch
        {
            return new LocalLockDocument();
        }
    }

    public static void Save(LocalLockDocument doc, string? cwd = null)
    {
        var path = GetLockPath(cwd);
        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
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
        var sha = SHA256.Create();
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
        return Convert.ToHexString(sha.ComputeHash(ms)).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
