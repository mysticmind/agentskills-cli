namespace AgentSkills.SkillModel;

public static class SkillDiscovery
{
    private static readonly string[] SkipDirs = ["node_modules", ".git", "dist", "build", "__pycache__"];

    private static readonly string[] PrioritySubdirs =
    [
        "",
        "skills",
        "skills/.curated",
        "skills/.experimental",
        "skills/.system",
        ".agents/skills",
        ".claude/skills",
        ".cline/skills",
        ".codebuddy/skills",
        ".codex/skills",
        ".commandcode/skills",
        ".continue/skills",
        ".github/skills",
        ".goose/skills",
        ".iflow/skills",
        ".junie/skills",
        ".kilocode/skills",
        ".kiro/skills",
        ".mux/skills",
        ".neovate/skills",
        ".opencode/skills",
        ".openhands/skills",
        ".pi/skills",
        ".qoder/skills",
        ".roo/skills",
        ".trae/skills",
        ".windsurf/skills",
        ".zencoder/skills",
    ];

    public sealed record DiscoverOptions(bool IncludeInternal = false, bool FullDepth = false);

    public static List<Skill> Discover(string basePath, string? subpath = null, DiscoverOptions? options = null)
    {
        options ??= new DiscoverOptions();

        if (subpath is not null)
        {
            var resolved = Path.GetFullPath(Path.Combine(basePath, subpath));
            if (!PathSafety.IsPathInside(basePath, resolved))
            {
                throw new ArgumentException(
                    $"Invalid subpath: \"{subpath}\" resolves outside the repository directory.",
                    nameof(subpath));
            }
        }

        var searchPath = subpath is null ? basePath : Path.Combine(basePath, subpath);
        var skills = new List<Skill>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parseOpts = new SkillManifest.ParseOptions(options.IncludeInternal);

        if (HasSkillMd(searchPath))
        {
            var rootSkill = SkillManifest.ParseSkillMd(Path.Combine(searchPath, "SKILL.md"), parseOpts);
            if (rootSkill is not null && seen.Add(rootSkill.Name))
            {
                skills.Add(rootSkill);
                if (!options.FullDepth)
                {
                    return skills;
                }
            }
        }

        foreach (var sub in PrioritySubdirs)
        {
            var dir = string.IsNullOrEmpty(sub) ? searchPath : Path.Combine(searchPath, sub);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateDirectories(dir))
            {
                if (HasSkillMd(entry))
                {
                    var skill = SkillManifest.ParseSkillMd(Path.Combine(entry, "SKILL.md"), parseOpts);
                    if (skill is not null && seen.Add(skill.Name))
                    {
                        skills.Add(skill);
                    }
                }
            }
        }

        if (skills.Count == 0 || options.FullDepth)
        {
            foreach (var dir in FindSkillDirs(searchPath, depth: 0, maxDepth: 5))
            {
                var skill = SkillManifest.ParseSkillMd(Path.Combine(dir, "SKILL.md"), parseOpts);
                if (skill is not null && seen.Add(skill.Name))
                {
                    skills.Add(skill);
                }
            }
        }

        return skills;
    }

    private static bool HasSkillMd(string dir) => File.Exists(Path.Combine(dir, "SKILL.md"));

    private static IEnumerable<string> FindSkillDirs(string dir, int depth, int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(dir))
        {
            yield break;
        }

        if (HasSkillMd(dir))
        {
            yield return dir;
        }

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(dir);
        }
        catch
        {
            yield break;
        }

        foreach (var sub in subdirs)
        {
            var name = Path.GetFileName(sub);
            if (SkipDirs.Contains(name))
            {
                continue;
            }
            foreach (var found in FindSkillDirs(sub, depth + 1, maxDepth))
            {
                yield return found;
            }
        }
    }
}
