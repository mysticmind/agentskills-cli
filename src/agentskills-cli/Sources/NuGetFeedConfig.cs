using NuGet.Configuration;

namespace AgentSkills.Sources;

/// <summary>
/// NuGet feed settings for skills. Two skills-only config files sit on top of the normal
/// NuGet configuration, in NuGet's own <c>nuget.config</c> format so credential providers,
/// <c>packageSourceCredentials</c>, package source mapping and <c>&lt;clear /&gt;</c> all work:
/// <list type="number">
///   <item><c>&lt;project&gt;/.agents/nuget.config</c>: committed with the repo; feeds only, no secrets.</item>
///   <item><c>~/.agents/nuget.config</c>: personal feeds and credentials.</item>
///   <item>The normal NuGet chain: <c>nuget.config</c> files from the project folder upwards, then
///   the user and machine-wide files (exactly what <c>dotnet restore</c> reads).</item>
/// </list>
/// Earlier files take precedence; a <c>&lt;clear /&gt;</c> in a skills file hides the feeds below it.
/// </summary>
public static class NuGetFeedConfig
{
    public const string FileName = "nuget.config";

    public static string ProjectPath(string? cwd = null) =>
        Path.Combine(cwd ?? Directory.GetCurrentDirectory(), ".agents", FileName);

    public static string UserPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", FileName);

    /// <summary>The skills config files that exist, highest precedence first.</summary>
    public static List<string> SkillsConfigFiles(string? cwd = null) =>
        new[] { ProjectPath(cwd), UserPath() }.Where(File.Exists).ToList();

    /// <summary>Loads the merged settings described in the type summary.</summary>
    public static ISettings Load(string? cwd = null)
    {
        var root = cwd ?? Directory.GetCurrentDirectory();
        var defaults = Settings.LoadDefaultSettings(root);
        var skills = SkillsConfigFiles(root);
        if (skills.Count == 0) return defaults;

        var paths = skills.Concat(defaults.GetConfigFilePaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Settings.LoadSettingsGivenConfigPaths(paths);
    }

    /// <summary>One feed as seen by the tool, with the file that defines it.</summary>
    public sealed record FeedInfo(string Name, string Source, bool Enabled, bool HasCredentials, string? DefinedIn);

    /// <summary>Every feed in effect, in the order the tool tries them.</summary>
    public static List<FeedInfo> List(string? cwd = null)
    {
        var root = cwd ?? Directory.GetCurrentDirectory();
        var settings = Load(root);
        var sources = new PackageSourceProvider(settings).LoadPackageSources().ToList();
        var files = settings.GetConfigFilePaths().ToList();

        return sources.Select(s => new FeedInfo(
            s.Name,
            s.Source,
            s.IsEnabled,
            s.Credentials is not null,
            files.FirstOrDefault(f => DefinesSource(f, s.Name)))).ToList();
    }

    private static bool DefinesSource(string configPath, string name)
    {
        try
        {
            var single = Settings.LoadSpecificSettings(Path.GetDirectoryName(configPath)!, Path.GetFileName(configPath));
            return new PackageSourceProvider(single).GetPackageSourceByName(name) is not null;
        }
        catch (NuGetConfigurationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or updates a feed in one config file (created if missing). Credentials are only
    /// accepted for the user file: the project file is meant to be committed.
    /// </summary>
    public static void AddFeed(string configPath, string name, string url, string? username = null, string? password = null, bool storePasswordInClearText = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var provider = OpenForWrite(configPath);
        var source = new PackageSource(url, name);
        if (username is not null)
        {
            if (password is null) throw new UserInputException("--username needs --password.");
            if (!storePasswordInClearText && !OperatingSystem.IsWindows())
            {
                throw new UserInputException(
                    "Encrypted passwords are only supported on Windows. Add --store-password-in-clear-text " +
                    "to store it in the user config file, or use a credential provider instead.");
            }
            source.Credentials = PackageSourceCredential.FromUserInput(
                name, username, password, storePasswordInClearText, validAuthenticationTypesText: null);
        }

        if (provider.GetPackageSourceByName(name) is not null)
        {
            provider.UpdatePackageSource(source, updateCredentials: true, updateEnabled: true);
        }
        else
        {
            provider.AddPackageSource(source);
        }

        if (source.Credentials is not null) RestrictToOwner(configPath);
    }

    /// <summary>
    /// Makes a file that holds credentials readable only by its owner on Linux and macOS (like
    /// npm does for ~/.npmrc). On Windows the user profile's ACLs already restrict it.
    /// </summary>
    public static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Removes a feed (and its credentials) from one config file. False when it wasn't there.</summary>
    public static bool RemoveFeed(string configPath, string name)
    {
        if (!File.Exists(configPath)) return false;
        var provider = OpenForWrite(configPath);
        if (provider.GetPackageSourceByName(name) is null) return false;
        provider.RemovePackageSource(name);
        return true;
    }

    private static PackageSourceProvider OpenForWrite(string configPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        Directory.CreateDirectory(dir);
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources />\n</configuration>\n");
        }
        var settings = Settings.LoadSpecificSettings(dir, Path.GetFileName(configPath));
        return new PackageSourceProvider(settings);
    }

    /// <summary>
    /// The feeds to try for one operation. <paramref name="overrideSource"/> (<c>--nuget-source</c>)
    /// may be a feed URL, a local folder, or the <b>name</b> of a configured feed; a configured
    /// match keeps its credentials. <paramref name="preferredSource"/> (the feed a package was
    /// installed from) is moved to the front without hiding the others.
    /// </summary>
    public static List<PackageSource> ResolveFeeds(string? overrideSource, string? preferredSource = null, string? cwd = null)
    {
        var configured = new PackageSourceProvider(Load(cwd)).LoadPackageSources().ToList();

        if (!string.IsNullOrWhiteSpace(overrideSource))
        {
            var match = configured.FirstOrDefault(s =>
                string.Equals(s.Name, overrideSource, StringComparison.OrdinalIgnoreCase) || SameSource(s.Source, overrideSource));
            return [match ?? new PackageSource(overrideSource)];
        }

        var enabled = configured.Where(s => s.IsEnabled).ToList();
        if (!string.IsNullOrWhiteSpace(preferredSource))
        {
            var preferred = enabled.FirstOrDefault(s => SameSource(s.Source, preferredSource));
            if (preferred is not null)
            {
                enabled.Remove(preferred);
                enabled.Insert(0, preferred);
            }
        }
        return enabled;
    }

    internal static bool SameSource(string a, string b) =>
        string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
}
