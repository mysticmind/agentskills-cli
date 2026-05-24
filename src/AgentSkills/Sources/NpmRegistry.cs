using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentSkills.Sources;

/// <summary>
/// Minimal <c>.npmrc</c> reader. Mirrors the npm rules well enough for skill installs:
/// project <c>.npmrc</c> takes precedence over <c>~/.npmrc</c>; per-scope registries
/// override the default; <c>_authToken</c> / <c>_auth</c> are honored per registry host
/// + path prefix; <c>${ENV}</c> values are expanded.
/// </summary>
public sealed class NpmRegistry
{
    private const string DefaultRegistry = "https://registry.npmjs.org/";

    private readonly Dictionary<string, string> _settings;
    private readonly string _defaultRegistry;

    public NpmRegistry(Dictionary<string, string> settings, string defaultRegistry)
    {
        _settings = settings;
        _defaultRegistry = defaultRegistry;
    }

    public string DefaultRegistryUrl => _defaultRegistry;

    public static NpmRegistry Load(string? cwd = null, string? overrideRegistry = null)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userRc = Path.Combine(home, ".npmrc");
        var projectRc = Path.Combine(cwd ?? Directory.GetCurrentDirectory(), ".npmrc");

        // npm reads from least- to most-specific so later wins; we apply in same order.
        foreach (var rc in new[] { userRc, projectRc })
        {
            if (!File.Exists(rc)) continue;
            foreach (var line in File.ReadAllLines(rc))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] is ';' or '#') continue;
                var eq = trimmed.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0) continue;
                var key = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
                settings[key] = ExpandEnv(value);
            }
        }

        var defaultRegistry = overrideRegistry
            ?? (settings.TryGetValue("registry", out var r) ? r : DefaultRegistry);
        if (!defaultRegistry.EndsWith('/')) defaultRegistry += "/";

        return new NpmRegistry(settings, defaultRegistry);
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("agentskills/0.1 (+npm)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static readonly Regex EnvRef = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static string ExpandEnv(string value) =>
        EnvRef.Replace(value, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? string.Empty);
}
