using AgentSkills.Agents;
using AgentSkills.SkillModel;

namespace AgentSkills.Install;

public enum InstallMode { Symlink, Copy }

public sealed record InstallResult(
    bool Success,
    string Path,
    InstallMode Mode,
    string? CanonicalPath = null,
    bool SymlinkFailed = false,
    bool Skipped = false,
    string? Error = null);

public static class Installer
{
    private static readonly HashSet<string> ExcludeFiles = new(StringComparer.Ordinal) { "metadata.json" };
    private static readonly HashSet<string> ExcludeDirs = new(StringComparer.Ordinal) { ".git", "__pycache__", "__pypackages__" };

    public sealed record Options(bool Global = false, string? Cwd = null, InstallMode Mode = InstallMode.Symlink);

    public static InstallResult InstallForAgent(Skill skill, AgentConfig agent, Options options)
    {
        var cwd = options.Cwd ?? Directory.GetCurrentDirectory();

        if (options.Global && agent.GlobalSkillsDir is null)
        {
            return new InstallResult(false, string.Empty, options.Mode,
                Error: $"{agent.DisplayName} does not support global skill installation");
        }

        var skillName = SkillNameSanitizer.Sanitize(skill.Name);
        var canonicalBase = AgentRegistry.GetCanonicalDir(options.Global, cwd);
        var canonicalDir = Path.Combine(canonicalBase, skillName);
        var agentBase = AgentRegistry.GetAgentBaseDir(agent, options.Global, cwd);
        var agentDir = Path.Combine(agentBase, skillName);

        if (!PathSafety.IsPathInside(canonicalBase, canonicalDir) || !PathSafety.IsPathInside(agentBase, agentDir))
        {
            return new InstallResult(false, agentDir, options.Mode,
                Error: "Invalid skill name: potential path traversal detected");
        }

        try
        {
            if (options.Mode == InstallMode.Copy)
            {
                CleanAndCreate(agentDir);
                CopyDirectory(skill.Path, agentDir);
                return new InstallResult(true, agentDir, InstallMode.Copy);
            }

            // Symlink mode: stage to canonical, then symlink agentDir -> canonical
            CleanAndCreate(canonicalDir);
            CopyDirectory(skill.Path, canonicalDir);

            // Global + universal: canonical IS the agent dir, nothing more to do.
            if (options.Global && agent.IsUniversal)
            {
                return new InstallResult(true, canonicalDir, InstallMode.Symlink, CanonicalPath: canonicalDir);
            }

            // Project + non-universal: skip materializing agent-specific dir if the user
            // doesn't already use that agent in this project.
            if (!options.Global && !agent.IsUniversal)
            {
                var agentRoot = Path.Combine(cwd, agent.ProjectSkillsDir.Split('/')[0]);
                if (!Directory.Exists(agentRoot))
                {
                    return new InstallResult(true, canonicalDir, InstallMode.Symlink,
                        CanonicalPath: canonicalDir, Skipped: true);
                }
            }

            var symlinked = TryCreateSymlink(target: canonicalDir, linkPath: agentDir);
            if (!symlinked)
            {
                CleanAndCreate(agentDir);
                CopyDirectory(skill.Path, agentDir);
                return new InstallResult(true, agentDir, InstallMode.Symlink,
                    CanonicalPath: canonicalDir, SymlinkFailed: true);
            }

            return new InstallResult(true, agentDir, InstallMode.Symlink, CanonicalPath: canonicalDir);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, agentDir, options.Mode, Error: ex.Message);
        }
    }

    public static bool RemoveForAgent(string skillName, AgentConfig agent, bool global, string? cwd = null)
    {
        var sanitized = SkillNameSanitizer.Sanitize(skillName);
        var workCwd = cwd ?? Directory.GetCurrentDirectory();

        if (global && agent.GlobalSkillsDir is null) return false;
        var baseDir = AgentRegistry.GetAgentBaseDir(agent, global, workCwd);
        var target = Path.Combine(baseDir, sanitized);
        if (!PathSafety.IsPathInside(baseDir, target)) return false;
        if (!Directory.Exists(target) && !File.Exists(target)) return false;
        try
        {
            DeletePath(target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static List<InstalledSkill> List(bool? global = null, IReadOnlyList<AgentConfig>? agentFilter = null, string? cwd = null)
    {
        var workCwd = cwd ?? Directory.GetCurrentDirectory();
        var result = new Dictionary<string, InstalledSkill>(StringComparer.Ordinal);
        var detected = AgentRegistry.DetectInstalled();
        var toCheck = agentFilter is null
            ? detected
            : detected.Where(a => agentFilter.Any(f => f.Name == a.Name)).ToList();

        var scopes = global is null
            ? new[] { false, true }
            : new[] { global.Value };

        foreach (var isGlobal in scopes)
        {
            ScanCanonical(isGlobal, workCwd, toCheck, result);
            foreach (var agent in AgentRegistry.All.Values)
            {
                ScanAgentDir(agent, isGlobal, workCwd, result);
            }
        }

        return result.Values.ToList();
    }

    private static void ScanCanonical(bool global, string cwd, IReadOnlyList<AgentConfig> toCheck, Dictionary<string, InstalledSkill> map)
    {
        var dir = AgentRegistry.GetCanonicalDir(global, cwd);
        if (!Directory.Exists(dir)) return;

        var scopeKey = global ? "global" : "project";
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var skillMd = Path.Combine(sub, "SKILL.md");
            if (!File.Exists(skillMd)) continue;
            var skill = SkillManifest.ParseSkillMd(skillMd);
            if (skill is null) continue;

            var key = $"{scopeKey}:{skill.Name}";
            var agents = new List<string>();
            var sanitized = SkillNameSanitizer.Sanitize(skill.Name);
            foreach (var agent in toCheck)
            {
                var agentBase = AgentRegistry.GetAgentBaseDir(agent, global, cwd);
                var candidate = Path.Combine(agentBase, sanitized);
                if (Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "SKILL.md")))
                {
                    agents.Add(agent.Name);
                }
            }

            map[key] = new InstalledSkill(skill.Name, skill.Description, sub, sub, scopeKey, agents);
        }
    }

    private static void ScanAgentDir(AgentConfig agent, bool global, string cwd, Dictionary<string, InstalledSkill> map)
    {
        if (global && agent.GlobalSkillsDir is null) return;
        var dir = AgentRegistry.GetAgentBaseDir(agent, global, cwd);
        if (!Directory.Exists(dir)) return;

        var scopeKey = global ? "global" : "project";
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var skillMd = Path.Combine(sub, "SKILL.md");
            if (!File.Exists(skillMd)) continue;
            var skill = SkillManifest.ParseSkillMd(skillMd);
            if (skill is null) continue;

            var key = $"{scopeKey}:{skill.Name}";
            if (map.TryGetValue(key, out var existing))
            {
                if (!existing.Agents.Contains(agent.Name))
                {
                    existing.Agents.Add(agent.Name);
                }
            }
            else
            {
                map[key] = new InstalledSkill(skill.Name, skill.Description, sub, sub, scopeKey, [agent.Name]);
            }
        }
    }

    private static void CleanAndCreate(string path)
    {
        DeletePath(path);
        Directory.CreateDirectory(path);
    }

    private static void DeletePath(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.LinkTarget is not null)
            {
                info.Delete();
                return;
            }
        }
        catch
        {
            // fall through to directory delete
        }

        if (Directory.Exists(path))
        {
            // If the directory itself is a symlink, just remove the link.
            var di = new DirectoryInfo(path);
            if (di.LinkTarget is not null)
            {
                di.Delete();
                return;
            }
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var entry in Directory.EnumerateFileSystemEntries(src))
        {
            var name = Path.GetFileName(entry);
            var isDir = Directory.Exists(entry) && new DirectoryInfo(entry).LinkTarget is null;

            if (ExcludeFiles.Contains(name) || (isDir && ExcludeDirs.Contains(name))) continue;

            var target = Path.Combine(dest, name);
            if (isDir)
            {
                CopyDirectory(entry, target);
            }
            else
            {
                try
                {
                    File.Copy(entry, target, overwrite: true);
                }
                catch (FileNotFoundException)
                {
                    // broken symlink - match upstream behavior (skip with a warning at higher layer).
                }
            }
        }
    }

    private static bool TryCreateSymlink(string target, string linkPath)
    {
        try
        {
            var resolvedTarget = Path.GetFullPath(target);
            var resolvedLink = Path.GetFullPath(linkPath);

            if (string.Equals(ResolveOrSelf(resolvedTarget), ResolveOrSelf(resolvedLink), StringComparison.Ordinal))
            {
                return true;
            }

            if (Directory.Exists(linkPath))
            {
                var di = new DirectoryInfo(linkPath);
                if (di.LinkTarget is not null)
                {
                    di.Delete();
                }
                else
                {
                    Directory.Delete(linkPath, recursive: true);
                }
            }
            else if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            var parent = Path.GetDirectoryName(linkPath)!;
            Directory.CreateDirectory(parent);

            // Use relative target so the canonical directory can move with the project.
            var relative = Path.GetRelativePath(parent, target);
            Directory.CreateSymbolicLink(linkPath, relative);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveOrSelf(string p)
    {
        try
        {
            var info = new DirectoryInfo(p);
            if (info.Exists)
            {
                return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
            }
        }
        catch
        {
            // ignore
        }
        return p;
    }
}
