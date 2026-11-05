using System.ComponentModel;
using System.Text.RegularExpressions;
using AgentSkills.Sources;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

// ─── registry add ─────────────────────────────────────────────────────────────

public sealed partial class RegistryAddCommand : Command<RegistryAddCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public RegistryAddCommand(IAnsiConsole console) => _console = console ?? throw new ArgumentNullException(nameof(console));

    public sealed class Settings : ConfigScopeSettings
    {
        [CommandArgument(0, "<URL>")]
        [Description("npm registry URL.")]
        public string Url { get; init; } = string.Empty;

        [CommandOption("--scope <SCOPE>")]
        [Description("Use this registry only for one package scope, e.g. @contoso. Omit to make it the default registry.")]
        public string? Scope { get; init; }

        [CommandOption("--token-env <VAR>")]
        [Description("Read the auth token from this environment variable at run time (allowed in the project file).")]
        public string? TokenEnv { get; init; }

        [CommandOption("--token <TOKEN>")]
        [Description("Store this auth token (user file only; needs -g).")]
        public string? Token { get; init; }
    }

    [GeneratedRegex("^@[a-z0-9][a-z0-9._~-]*$")]
    private static partial Regex ScopeShape();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvVarShape();

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = settings.Global ? NpmRegistry.UserSkillsPath() : NpmRegistry.ProjectSkillsPath();

        if (!Uri.TryCreate(settings.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new UserInputException($"'{settings.Url}' is not an http(s) URL.");
        }
        var url = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";

        var scope = settings.Scope?.Trim();
        if (scope is not null)
        {
            if (!scope.StartsWith('@')) scope = "@" + scope;
            if (!ScopeShape().IsMatch(scope)) throw new UserInputException($"'{settings.Scope}' is not a valid npm scope (e.g. @contoso).");
        }

        if (settings.Token is not null && settings.TokenEnv is not null)
        {
            throw new UserInputException("Use either --token or --token-env, not both.");
        }
        if (settings.Token is not null && !settings.Global)
        {
            throw new UserInputException(
                "A token can't go in the project file (.agents/.npmrc is meant to be committed). Use --token-env <VAR> " +
                "to reference an environment variable, or add -g to store the token in your user file.");
        }
        if (settings.TokenEnv is not null && !EnvVarShape().IsMatch(settings.TokenEnv))
        {
            throw new UserInputException($"'{settings.TokenEnv}' is not a valid environment variable name.");
        }

        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [scope is null ? "registry" : $"{scope}:registry"] = url,
        };
        var token = settings.Token ?? (settings.TokenEnv is null ? null : $"${{{settings.TokenEnv}}}");
        if (token is not null)
        {
            changes[$"{NpmRegistry.AuthKeyPrefix(url)}:_authToken"] = token;
        }
        NpmRegistry.WriteKeys(path, changes);
        if (settings.Token is not null) NuGetFeedConfig.RestrictToOwner(path);

        var what = scope is null ? "default npm registry" : $"npm registry for {scope}";
        _console.MarkupLine($"[green]Set {Markup.Escape(what)}[/] to {Markup.Escape(url)} in {Markup.Escape(ConfigDisplay.ShortPath(path))}");
        if (settings.TokenEnv is not null)
        {
            _console.MarkupLine($"[grey]The token is read from ${Markup.Escape(settings.TokenEnv)} when the tool runs.[/]");
        }
        return 0;
    }
}

// ─── registry list ────────────────────────────────────────────────────────────

public sealed class RegistryListCommand : Command<RegistryListCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public RegistryListCommand(IAnsiConsole console) => _console = console ?? throw new ArgumentNullException(nameof(console));

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Write the registries as JSON to stdout.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var npm = NpmRegistry.Load();

        var rows = new List<(string Scope, string Url, bool Auth, string? DefinedIn)>
        {
            ("(default)", npm.DefaultRegistryUrl, npm.GetAuthHeader(npm.DefaultRegistryUrl) is not null, npm.Origins.GetValueOrDefault("registry")),
        };
        foreach (var (key, value) in npm.Settings.Where(kv => kv.Key.StartsWith('@') && kv.Key.EndsWith(":registry", StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var scopedUrl = value.EndsWith('/') ? value : value + "/";
            rows.Add((key[..^":registry".Length], scopedUrl, npm.GetAuthHeader(scopedUrl) is not null, npm.Origins.GetValueOrDefault(key)));
        }

        if (settings.Json)
        {
            JsonOutput.Write(_console, rows.Select(r => new { scope = r.Scope, url = r.Url, hasAuth = r.Auth, definedIn = r.DefinedIn }));
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded).LeftAligned().Title("[bold]npm registries[/]");
        table.AddColumns("Scope", "Registry", "Auth", "Defined in");
        foreach (var r in rows)
        {
            table.AddRow(
                Markup.Escape(r.Scope),
                Markup.Escape(r.Url),
                r.Auth ? "yes" : "[grey]-[/]",
                $"[grey]{Markup.Escape(r.DefinedIn is null ? "built-in default" : ConfigDisplay.ShortPath(r.DefinedIn))}[/]");
        }
        _console.Write(table);
        return 0;
    }
}

// ─── registry remove ──────────────────────────────────────────────────────────

public sealed class RegistryRemoveCommand : Command<RegistryRemoveCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public RegistryRemoveCommand(IAnsiConsole console) => _console = console ?? throw new ArgumentNullException(nameof(console));

    public sealed class Settings : ConfigScopeSettings
    {
        [CommandArgument(0, "<URL_OR_SCOPE>")]
        [Description("A registry URL, or a scope such as @contoso, or 'default' for the default registry.")]
        public string Target { get; init; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = settings.Global ? NpmRegistry.UserSkillsPath() : NpmRegistry.ProjectSkillsPath();
        var entries = NpmRegistry.ReadFile(path).ToList();
        var target = settings.Target.Trim();

        var registryKeys = entries
            .Where(e => e.Key.Equals("registry", StringComparison.OrdinalIgnoreCase) || e.Key.EndsWith(":registry", StringComparison.OrdinalIgnoreCase))
            .Where(e => target.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? e.Key.Equals("registry", StringComparison.OrdinalIgnoreCase)
                : target.StartsWith('@')
                    ? e.Key.Equals($"{target}:registry", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(e.Value.TrimEnd('/'), target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (registryKeys.Count == 0)
        {
            _console.MarkupLine($"[yellow]Nothing matching '{Markup.Escape(target)}' in {Markup.Escape(ConfigDisplay.ShortPath(path))}.[/]");
            return 1;
        }

        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in registryKeys)
        {
            changes[key] = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out _)) continue;
            // Drop that registry's auth settings too, unless another remaining key still uses the registry.
            var stillUsed = entries.Any(e => !registryKeys.Contains(e) && e.Key.EndsWith("registry", StringComparison.OrdinalIgnoreCase) &&
                                             string.Equals(e.Value.TrimEnd('/'), value.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            if (stillUsed) continue;
            var prefix = NpmRegistry.AuthKeyPrefix(value);
            foreach (var auth in entries.Where(e => e.Key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)))
            {
                changes[auth.Key] = null;
            }
        }
        NpmRegistry.WriteKeys(path, changes);

        _console.MarkupLine($"[green]Removed[/] {string.Join(", ", registryKeys.Select(k => Markup.Escape(k.Key)))} from {Markup.Escape(ConfigDisplay.ShortPath(path))}");
        return 0;
    }
}
