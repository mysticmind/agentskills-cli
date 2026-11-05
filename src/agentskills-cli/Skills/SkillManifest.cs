using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace AgentSkills.SkillModel;

public static partial class SkillManifest
{
    [GeneratedRegex(@"^---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)$", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    public sealed record Parsed(IReadOnlyDictionary<string, object?> Data, string Content);

    public static Parsed ParseFrontmatter(string raw)
    {
        var match = FrontmatterRegex().Match(raw);
        if (!match.Success)
        {
            return new Parsed(new Dictionary<string, object?>(), raw);
        }

        var yamlText = match.Groups[1].Value;
        var content = match.Groups[2].Value;

        var data = ParseYamlMapping(yamlText);
        return new Parsed(data, content);
    }

    public sealed record ParseOptions(bool IncludeInternal = false);

    public static Skill? ParseSkillMd(string skillMdPath, ParseOptions? options = null)
    {
        options ??= new ParseOptions();
        string raw;
        try
        {
            raw = File.ReadAllText(skillMdPath);
        }
        catch
        {
            return null;
        }

        var parsed = ParseFrontmatter(raw);
        if (!parsed.Data.TryGetValue("name", out var nameObj) || nameObj is not string name)
        {
            return null;
        }

        if (!parsed.Data.TryGetValue("description", out var descObj) || descObj is not string description)
        {
            return null;
        }

        IReadOnlyDictionary<string, object?>? metadata = null;
        if (parsed.Data.TryGetValue("metadata", out var metaObj) && metaObj is IReadOnlyDictionary<string, object?> m)
        {
            metadata = m;
        }

        var isInternal = metadata?.TryGetValue("internal", out var internalObj) == true && internalObj is bool b && b;
        if (isInternal && !ShouldInstallInternalSkills() && !options.IncludeInternal)
        {
            return null;
        }

        var skillDir = Path.GetDirectoryName(skillMdPath) ?? string.Empty;
        return new Skill(
            Name: Sanitize(name),
            Description: Sanitize(description),
            Path: skillDir,
            RawContent: raw,
            Metadata: metadata);
    }

    [GeneratedRegex(@"^name[ \t]*:[^\r\n]*", RegexOptions.Multiline)]
    private static partial Regex TopLevelNameLine();

    /// <summary>
    /// Returns <paramref name="raw"/> with the frontmatter's top-level <c>name:</c> set to
    /// <paramref name="newName"/>. Everything else (other keys, comments, the body, line
    /// endings) is left untouched. Used when a skill is installed under an alias: the
    /// Agent Skills spec requires <c>name</c> to match the skill's directory.
    /// </summary>
    public static string WithName(string raw, string newName)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var match = FrontmatterRegex().Match(raw);
        var newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (!match.Success)
        {
            return $"---{newline}name: {newName}{newline}---{newline}{raw}";
        }

        var yaml = match.Groups[1];
        var replaced = TopLevelNameLine().IsMatch(yaml.Value)
            ? TopLevelNameLine().Replace(yaml.Value, $"name: {newName}", 1)
            : $"name: {newName}{newline}{yaml.Value}";
        return raw[..yaml.Index] + replaced + raw[(yaml.Index + yaml.Length)..];
    }

    /// <summary>Rewrites the <c>name:</c> of the SKILL.md at <paramref name="skillMdPath"/> in place.</summary>
    public static void RewriteName(string skillMdPath, string newName)
    {
        var raw = File.ReadAllText(skillMdPath);
        var updated = WithName(raw, newName);
        if (!string.Equals(raw, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(skillMdPath, updated);
        }
    }

    public static bool ShouldInstallInternalSkills()
    {
        var value = Environment.GetEnvironmentVariable("INSTALL_INTERNAL_SKILLS");
        return value is "1" or "true";
    }

    // Mirrors src/sanitize.ts -- strip control chars used by clack prompt rendering.
    private static string Sanitize(string s) =>
        new(s.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());

    private static IReadOnlyDictionary<string, object?> ParseYamlMapping(string yamlText)
    {
        var result = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            return result;
        }

        var stream = new YamlStream();
        using var reader = new StringReader(yamlText);
        try
        {
            stream.Load(reader);
        }
        catch
        {
            return result;
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return result;
        }

        foreach (var entry in root.Children)
        {
            if (entry.Key is YamlScalarNode key)
            {
                result[key.Value ?? string.Empty] = ConvertNode(entry.Value);
            }
        }

        return result;
    }

    private static object? ConvertNode(YamlNode node) => node switch
    {
        YamlScalarNode s => ConvertScalar(s),
        YamlMappingNode m => ConvertMapping(m),
        YamlSequenceNode seq => seq.Children.Select(ConvertNode).ToList(),
        _ => null,
    };

    private static IReadOnlyDictionary<string, object?> ConvertMapping(YamlMappingNode node)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var entry in node.Children)
        {
            if (entry.Key is YamlScalarNode key)
            {
                dict[key.Value ?? string.Empty] = ConvertNode(entry.Value);
            }
        }
        return dict;
    }

    private static object? ConvertScalar(YamlScalarNode node)
    {
        var value = node.Value;
        if (value is null)
        {
            return null;
        }

        // Honor explicit string style (quoted)
        if (node.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted or YamlDotNet.Core.ScalarStyle.DoubleQuoted)
        {
            return value;
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) || value == "~")
        {
            return null;
        }
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return value;
    }
}
