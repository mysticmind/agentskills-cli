using System.Text.RegularExpressions;
using Skills.SkillModel;

namespace Skills.Sources;

public static partial class SourceParser
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["coinbase/agentWallet"] = "coinbase/agentic-wallet-skills",
    };

    /// <summary>Extracts <c>owner/repo</c> from a parsed source's URL, or null if it can't be inferred.</summary>
    public static string? GetOwnerRepo(ParsedSource parsed)
    {
        if (parsed.Type is SourceType.Local or SourceType.NuGet or SourceType.WellKnown) return null;

        var url = parsed.Url;
        if (url.StartsWith("git@", StringComparison.Ordinal))
        {
            var colon = url.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) return null;
            var path = url[(colon + 1)..];
            if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
            return path.Contains('/', StringComparison.Ordinal) ? path : null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var pathName = uri.AbsolutePath.TrimStart('/');
        if (pathName.EndsWith(".git", StringComparison.Ordinal)) pathName = pathName[..^4];
        return pathName.Contains('/', StringComparison.Ordinal) ? pathName : null;
    }

    public static ParsedSource Parse(string rawInput)
    {
        var input = rawInput?.Trim() ?? throw new ArgumentNullException(nameof(rawInput));

        if (IsLocalPath(input))
        {
            var resolved = Path.GetFullPath(input);
            return new ParsedSource(SourceType.Local, resolved, LocalPath: resolved);
        }

        // npm explicit prefix wins over fragment parsing and over @scope detection.
        if (input.StartsWith("npm:", StringComparison.OrdinalIgnoreCase))
        {
            var tail = input["npm:".Length..];
            var (pkgId, pkgVersion) = SplitNpmPackageIdVersion(tail);
            return new ParsedSource(SourceType.Npm, $"npm:{pkgId}", PackageId: pkgId, PackageVersion: pkgVersion);
        }

        // Scoped npm shorthand: @scope/name[@version].
        if (input.StartsWith('@') && NpmScopedIdShape().IsMatch(StripVersionForShapeCheck(input)))
        {
            var (pkgId, pkgVersion) = SplitNpmPackageIdVersion(input);
            return new ParsedSource(SourceType.Npm, $"npm:{pkgId}", PackageId: pkgId, PackageVersion: pkgVersion);
        }

        // NuGet explicit prefix.
        if (input.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
        {
            var tail = input["nuget:".Length..];
            var (pkgId, pkgVersion) = SplitPackageIdVersion(tail);
            return new ParsedSource(SourceType.NuGet, $"nuget:{pkgId}", PackageId: pkgId, PackageVersion: pkgVersion);
        }

        // Bare NuGet shorthand: PackageId[@version] with a dot in the id and no slash/colon.
        if (LooksLikeNuGetId(input))
        {
            var (pkgId, pkgVersion) = SplitPackageIdVersion(input);
            return new ParsedSource(SourceType.NuGet, $"nuget:{pkgId}", PackageId: pkgId, PackageVersion: pkgVersion);
        }

        var (withoutFragment, fragmentRef, fragmentSkillFilter) = ParseFragmentRef(input);
        input = withoutFragment;

        if (Aliases.TryGetValue(input, out var aliased))
        {
            input = aliased;
        }

        var ghPrefix = GithubPrefix().Match(input);
        if (ghPrefix.Success)
        {
            return Parse(AppendFragmentRef(ghPrefix.Groups[1].Value, fragmentRef, fragmentSkillFilter));
        }

        var glPrefix = GitlabPrefix().Match(input);
        if (glPrefix.Success)
        {
            return Parse(AppendFragmentRef($"https://gitlab.com/{glPrefix.Groups[1].Value}", fragmentRef, fragmentSkillFilter));
        }

        // GitHub URL with /tree/<ref>/<path>
        var ghTreePath = GithubTreeWithPath().Match(input);
        if (ghTreePath.Success)
        {
            return new ParsedSource(
                SourceType.GitHub,
                $"https://github.com/{ghTreePath.Groups[1].Value}/{ghTreePath.Groups[2].Value}.git",
                Subpath: PathSafety.SanitizeSubpath(ghTreePath.Groups[4].Value),
                Ref: ghTreePath.Groups[3].Value);
        }

        // GitHub URL with /tree/<ref>
        var ghTree = GithubTreeOnly().Match(input);
        if (ghTree.Success)
        {
            return new ParsedSource(
                SourceType.GitHub,
                $"https://github.com/{ghTree.Groups[1].Value}/{ghTree.Groups[2].Value}.git",
                Ref: ghTree.Groups[3].Value);
        }

        // GitHub URL (bare)
        var ghRepo = GithubRepo().Match(input);
        if (ghRepo.Success)
        {
            var repo = StripGitSuffix(ghRepo.Groups[2].Value);
            return new ParsedSource(
                SourceType.GitHub,
                $"https://github.com/{ghRepo.Groups[1].Value}/{repo}.git",
                Ref: fragmentRef);
        }

        // GitLab URL with /-/tree/<ref>/<path>
        var glTreePath = GitlabTreeWithPath().Match(input);
        if (glTreePath.Success && glTreePath.Groups[2].Value != "github.com")
        {
            var repoPath = StripGitSuffix(glTreePath.Groups[3].Value);
            return new ParsedSource(
                SourceType.GitLab,
                $"{glTreePath.Groups[1].Value}://{glTreePath.Groups[2].Value}/{repoPath}.git",
                Subpath: PathSafety.SanitizeSubpath(glTreePath.Groups[5].Value),
                Ref: glTreePath.Groups[4].Value);
        }

        // GitLab URL with /-/tree/<ref>
        var glTree = GitlabTreeOnly().Match(input);
        if (glTree.Success && glTree.Groups[2].Value != "github.com")
        {
            var repoPath = StripGitSuffix(glTree.Groups[3].Value);
            return new ParsedSource(
                SourceType.GitLab,
                $"{glTree.Groups[1].Value}://{glTree.Groups[2].Value}/{repoPath}.git",
                Ref: glTree.Groups[4].Value);
        }

        // GitLab.com URL
        var glRepo = GitlabRepo().Match(input);
        if (glRepo.Success)
        {
            var repoPath = glRepo.Groups[1].Value;
            if (repoPath.Contains('/', StringComparison.Ordinal))
            {
                return new ParsedSource(SourceType.GitLab, $"https://gitlab.com/{repoPath}.git", Ref: fragmentRef);
            }
        }

        // owner/repo@skill
        var atSkill = OwnerRepoAtSkill().Match(input);
        if (atSkill.Success && !input.Contains(':', StringComparison.Ordinal) && !input.StartsWith('.') && !input.StartsWith('/'))
        {
            return new ParsedSource(
                SourceType.GitHub,
                $"https://github.com/{atSkill.Groups[1].Value}/{atSkill.Groups[2].Value}.git",
                Ref: fragmentRef,
                SkillFilter: fragmentSkillFilter ?? atSkill.Groups[3].Value);
        }

        // owner/repo[/subpath]
        var shorthand = Shorthand().Match(input);
        if (shorthand.Success && !input.Contains(':', StringComparison.Ordinal) && !input.StartsWith('.') && !input.StartsWith('/'))
        {
            var subpath = shorthand.Groups[3].Success ? PathSafety.SanitizeSubpath(shorthand.Groups[3].Value) : null;
            return new ParsedSource(
                SourceType.GitHub,
                $"https://github.com/{shorthand.Groups[1].Value}/{shorthand.Groups[2].Value}.git",
                Subpath: subpath,
                Ref: fragmentRef,
                SkillFilter: fragmentSkillFilter);
        }

        if (IsWellKnownUrl(input))
        {
            return new ParsedSource(SourceType.WellKnown, input);
        }

        return new ParsedSource(SourceType.Git, input, Ref: fragmentRef);
    }

    [GeneratedRegex(@"^github:(.+)$")] private static partial Regex GithubPrefix();
    [GeneratedRegex(@"^gitlab:(.+)$")] private static partial Regex GitlabPrefix();
    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)/tree/([^/]+)/(.+)")] private static partial Regex GithubTreeWithPath();
    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)/tree/([^/]+)$")] private static partial Regex GithubTreeOnly();
    [GeneratedRegex(@"github\.com/([^/]+)/([^/]+)")] private static partial Regex GithubRepo();
    [GeneratedRegex(@"^(https?)://([^/]+)/(.+?)/-/tree/([^/]+)/(.+)")] private static partial Regex GitlabTreeWithPath();
    [GeneratedRegex(@"^(https?)://([^/]+)/(.+?)/-/tree/([^/]+)$")] private static partial Regex GitlabTreeOnly();
    [GeneratedRegex(@"gitlab\.com/(.+?)(?:\.git)?/?$")] private static partial Regex GitlabRepo();
    [GeneratedRegex(@"^([^/]+)/([^/@]+)@(.+)$")] private static partial Regex OwnerRepoAtSkill();
    [GeneratedRegex(@"^([^/]+)/([^/]+)(?:/(.+?))?/?$")] private static partial Regex Shorthand();
    [GeneratedRegex(@"^[A-Za-z]:[/\\]")] private static partial Regex WindowsDrive();
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+(\.[A-Za-z0-9_.-]+)+(@[^\s/]+)?$")] private static partial Regex NuGetIdShape();
    [GeneratedRegex(@"^@[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*$")] private static partial Regex NpmScopedIdShape();

    private static bool IsLocalPath(string input)
    {
        if (Path.IsPathRooted(input)) return true;
        if (input is "." or "..") return true;
        if (input.StartsWith("./") || input.StartsWith("../") || input.StartsWith(".\\") || input.StartsWith("..\\")) return true;
        if (WindowsDrive().IsMatch(input)) return true;
        return false;
    }

    private static bool LooksLikeNuGetId(string input)
    {
        if (input.Contains('/', StringComparison.Ordinal) || input.Contains(':', StringComparison.Ordinal) || input.StartsWith('.') || input.StartsWith('-')) return false;
        return NuGetIdShape().IsMatch(input);
    }

    private static (string PackageId, string? Version) SplitPackageIdVersion(string input)
    {
        var at = input.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return (input, null);
        }
        return (input[..at], input[(at + 1)..]);
    }

    private static (string PackageId, string? Version) SplitNpmPackageIdVersion(string input)
    {
        int searchStart;
        if (input.StartsWith('@'))
        {
            var slash = input.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0) return (input, null);
            searchStart = slash + 1;
        }
        else
        {
            searchStart = 0;
        }
        var at = input.IndexOf('@', searchStart);
        if (at < 0) return (input, null);
        return (input[..at], input[(at + 1)..]);
    }

    /// <summary>Drops a trailing <c>@version</c> suffix so <see cref="NpmScopedIdShape"/> can validate the id alone.</summary>
    private static string StripVersionForShapeCheck(string input)
    {
        var (id, _) = SplitNpmPackageIdVersion(input);
        return id;
    }

    private static string StripGitSuffix(string input) =>
        input.EndsWith(".git", StringComparison.Ordinal) ? input[..^4] : input;

    private sealed record FragmentResult(string InputWithoutFragment, string? Ref, string? SkillFilter);

    private static FragmentResult ParseFragmentRef(string input)
    {
        var hashIndex = input.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex < 0)
        {
            return new FragmentResult(input, null, null);
        }

        var withoutFragment = input[..hashIndex];
        var fragment = input[(hashIndex + 1)..];

        if (fragment.Length == 0 || !LooksLikeGitSource(withoutFragment))
        {
            return new FragmentResult(input, null, null);
        }

        var atIndex = fragment.IndexOf('@', StringComparison.Ordinal);
        if (atIndex == -1)
        {
            return new FragmentResult(withoutFragment, Decode(fragment), null);
        }

        var refPart = fragment[..atIndex];
        var skillFilter = fragment[(atIndex + 1)..];
        return new FragmentResult(
            withoutFragment,
            refPart.Length > 0 ? Decode(refPart) : null,
            skillFilter.Length > 0 ? Decode(skillFilter) : null);
    }

    [GeneratedRegex(@"^/[^/]+/[^/]+(?:\.git)?(?:/tree/[^/]+(?:/.*)?)?/?$")] private static partial Regex GithubPathShape();
    [GeneratedRegex(@"^/.+?/[^/]+(?:\.git)?(?:/-/tree/[^/]+(?:/.*)?)?/?$")] private static partial Regex GitlabPathShape();
    [GeneratedRegex(@"^https?://.+\.git(?:$|[/?])", RegexOptions.IgnoreCase)] private static partial Regex BareGitUrl();
    [GeneratedRegex(@"^([^/]+)/([^/]+)(?:/(.+)|@(.+))?$")] private static partial Regex OwnerRepoShape();

    private static bool LooksLikeGitSource(string input)
    {
        if (input.StartsWith("github:") || input.StartsWith("gitlab:") || input.StartsWith("git@"))
        {
            return true;
        }

        if (input.StartsWith("http://") || input.StartsWith("https://"))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                if (uri.Host == "github.com")
                {
                    return GithubPathShape().IsMatch(uri.AbsolutePath);
                }
                if (uri.Host == "gitlab.com")
                {
                    return GitlabPathShape().IsMatch(uri.AbsolutePath);
                }
            }
        }

        if (BareGitUrl().IsMatch(input)) return true;

        return !input.Contains(':', StringComparison.Ordinal) &&
               !input.StartsWith('.') &&
               !input.StartsWith('/') &&
               OwnerRepoShape().IsMatch(input);
    }

    private static string AppendFragmentRef(string input, string? @ref, string? skillFilter)
    {
        if (string.IsNullOrEmpty(@ref))
        {
            return input;
        }
        return string.IsNullOrEmpty(skillFilter)
            ? $"{input}#{@ref}"
            : $"{input}#{@ref}@{skillFilter}";
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    private static bool IsWellKnownUrl(string input)
    {
        if (!input.StartsWith("http://") && !input.StartsWith("https://")) return false;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)) return false;
        if (uri.Host is "github.com" or "gitlab.com" or "raw.githubusercontent.com") return false;
        if (input.EndsWith(".git", StringComparison.Ordinal)) return false;
        return true;
    }
}
