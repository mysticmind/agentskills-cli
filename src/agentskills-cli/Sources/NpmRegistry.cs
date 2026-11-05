using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentSkills.Sources;

/// <summary>
/// Minimal <c>.npmrc</c> reader. Mirrors the npm rules well enough for skill installs:
/// per-scope registries override the default; <c>_authToken</c> / <c>_auth</c> are honored per
/// registry host + path prefix; <c>${ENV}</c> values are expanded. Files are read from least to
/// most specific, later values winning:
/// <list type="number">
///   <item><c>~/.npmrc</c> and <c>&lt;project&gt;/.npmrc</c>: your normal npm configuration.</item>
///   <item><c>~/.agents/.npmrc</c>: personal registries and tokens for skills.</item>
///   <item><c>&lt;project&gt;/.agents/.npmrc</c>: the project's registries for skills, committed
///   with the repo (tokens only as <c>${ENV_VAR}</c> references).</item>
/// </list>
/// </summary>
public sealed class NpmRegistry
{
    private const string DefaultRegistry = "https://registry.npmjs.org/";
    public const string FileName = ".npmrc";

    private readonly Dictionary<string, string> _settings;
    private readonly string _defaultRegistry;

    public NpmRegistry(Dictionary<string, string> settings, string defaultRegistry)
    {
        _settings = settings;
        _defaultRegistry = defaultRegistry;
    }

    public string DefaultRegistryUrl => _defaultRegistry;

    /// <summary>Which file each setting came from (keys as in <c>.npmrc</c>). For <c>registry list</c>.</summary>
    public IReadOnlyDictionary<string, string> Origins { get; private init; } = new Dictionary<string, string>();

    /// <summary>All settings in effect, after <c>${ENV}</c> expansion.</summary>
    public IReadOnlyDictionary<string, string> Settings => _settings;

    public static string ProjectSkillsPath(string? cwd = null) =>
        Path.Combine(cwd ?? Directory.GetCurrentDirectory(), ".agents", FileName);

    public static string UserSkillsPath(string? home = null) =>
        Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", FileName);

    /// <param name="home">
    /// Directory holding the user <c>.npmrc</c>. Defaults to the user profile. Tests pass a
    /// temp directory: on Windows the profile comes from the OS, not from HOME/USERPROFILE.
    /// </param>
    public static NpmRegistry Load(string? cwd = null, string? overrideRegistry = null, string? home = null)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = cwd ?? Directory.GetCurrentDirectory();

        // Least- to most-specific; later wins.
        foreach (var rc in new[] { Path.Combine(home, FileName), Path.Combine(root, FileName), UserSkillsPath(home), ProjectSkillsPath(root) })
        {
            foreach (var (key, value) in ReadFile(rc))
            {
                settings[key] = ExpandEnv(value);
                origins[key] = rc;
            }
        }

        var defaultRegistry = overrideRegistry
            ?? (settings.TryGetValue("registry", out var r) ? r : DefaultRegistry);
        if (!defaultRegistry.EndsWith('/')) defaultRegistry += "/";

        return new NpmRegistry(settings, defaultRegistry) { Origins = origins };
    }

    /// <summary>Raw key/value pairs of one <c>.npmrc</c> (no env expansion). Empty when missing.</summary>
    public static IEnumerable<(string Key, string Value)> ReadFile(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadAllLines(path))
        {
            if (TryParseLine(line, out var key, out var value)) yield return (key, value);
        }
    }

    internal static bool TryParseLine(string line, out string key, out string value)
    {
        key = value = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] is ';' or '#') return false;
        var eq = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0) return false;
        key = trimmed[..eq].Trim();
        value = trimmed[(eq + 1)..].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        return true;
    }

    /// <summary>
    /// Sets (or with a null value removes) keys in one <c>.npmrc</c>, keeping every other line,
    /// comment, and the file's order. Creates the file and its folder when needed.
    /// </summary>
    public static void WriteKeys(string path, IReadOnlyDictionary<string, string?> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(lines.Count + changes.Count);

        foreach (var line in lines)
        {
            if (!TryParseLine(line, out var key, out _) || !changes.TryGetValue(key, out var newValue))
            {
                result.Add(line);
            }
            else if (newValue is not null && updated.Add(key))
            {
                result.Add($"{key}={newValue}"); // first occurrence is replaced in place
            }
            // a removed key, or a later duplicate of an updated key, is dropped
        }

        result.AddRange(changes
            .Where(c => c.Value is not null && !updated.Contains(c.Key))
            .Select(c => $"{c.Key}={c.Value}"));
        lines = result;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, string.Join("\n", lines) + (lines.Count > 0 ? "\n" : string.Empty));
    }

    /// <summary><c>//host/path/</c>, the key prefix npm uses for a registry's auth settings.</summary>
    public static string AuthKeyPrefix(string registryUrl)
    {
        var uri = new Uri(registryUrl.EndsWith('/') ? registryUrl : registryUrl + "/");
        var host = uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
        return $"//{host}{uri.AbsolutePath}";
    }

    /// <summary>Resolves the registry URL to use for a given package id.</summary>
    public string ResolveRegistry(string packageId)
    {
        if (packageId.StartsWith('@'))
        {
            var slash = packageId.IndexOf('/', StringComparison.Ordinal);
            if (slash > 1)
            {
                var scope = packageId[..slash]; // e.g. "@my-org"
                if (_settings.TryGetValue($"{scope}:registry", out var scoped))
                {
                    return scoped.EndsWith('/') ? scoped : scoped + "/";
                }
            }
        }
        return _defaultRegistry;
    }

    /// <summary>
    /// Builds an Authorization header for the given registry URL, or null if no creds are configured.
    /// Matches keys of the form <c>//host/path/:_authToken</c> or <c>//host/path/:_auth</c>,
    /// preferring the longest matching path prefix.
    /// </summary>
    public AuthenticationHeaderValue? GetAuthHeader(string registryUrl)
    {
        if (!Uri.TryCreate(registryUrl, UriKind.Absolute, out var uri)) return null;

        // Normalize: //host/path/  (strip scheme, keep trailing slash semantics)
        var normalizedHost = uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
        var normalizedPath = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
        var prefixes = new List<string>();
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var accum = "/";
        prefixes.Add($"//{normalizedHost}/");
        foreach (var seg in segments)
        {
            accum += seg + "/";
            prefixes.Add($"//{normalizedHost}{accum}");
        }
        prefixes.Reverse(); // longest-first

        foreach (var prefix in prefixes)
        {
            if (_settings.TryGetValue(prefix + ":_authToken", out var token) && token.Length > 0)
            {
                return new AuthenticationHeaderValue("Bearer", token);
            }
            if (_settings.TryGetValue(prefix + ":_auth", out var basic) && basic.Length > 0)
            {
                return new AuthenticationHeaderValue("Basic", basic);
            }
            if (_settings.TryGetValue(prefix + ":_password", out var pwBase64)
                && _settings.TryGetValue(prefix + ":username", out var user))
            {
                var pw = Encoding.UTF8.GetString(Convert.FromBase64String(pwBase64));
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pw}"));
                return new AuthenticationHeaderValue("Basic", encoded);
            }
        }
        return null;
    }

    public HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"agentskills-cli/{Hosting.VersionInfo.Version} (+npm)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static readonly Regex EnvRef = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static string ExpandEnv(string value) =>
        EnvRef.Replace(value, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? string.Empty);
}
