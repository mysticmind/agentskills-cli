using System.ComponentModel;
using AgentSkills.Sources;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

/// <summary>Shared scope option for <c>feed</c> and <c>registry</c>: project file by default, user file with -g.</summary>
public class ConfigScopeSettings : CommandSettings
{
    [CommandOption("-g|--global")]
    [Description("Use your user file (~/.agents/...) instead of the project's .agents/ file.")]
    public bool Global { get; init; }
}

internal static class ConfigDisplay
{
    /// <summary>Shortens a config path for display: project-relative, or ~ for the home folder.</summary>
    public static string ShortPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "-";
        var cwd = Directory.GetCurrentDirectory();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var full = Path.GetFullPath(path);
        if (full.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return Path.GetRelativePath(cwd, full);
        if (full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return "~" + full[home.Length..];
        return full;
    }
}

// ─── feed add ─────────────────────────────────────────────────────────────────

public sealed class FeedAddCommand : Command<FeedAddCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public FeedAddCommand(IAnsiConsole console) => _console = console ?? throw new ArgumentNullException(nameof(console));

    public sealed class Settings : ConfigScopeSettings
    {
        [CommandArgument(0, "<URL>")]
        [Description("Feed URL (https://.../v3/index.json) or a local folder.")]
        public string Url { get; init; } = string.Empty;

        [CommandOption("-n|--name <NAME>")]
        [Description("Feed name. Defaults to the host name (or folder name for a local feed).")]
        public string? Name { get; init; }

        [CommandOption("-u|--username <USER>")]
        [Description("Username for an authenticated feed (user file only; needs -g).")]
        public string? Username { get; init; }

        [CommandOption("--password <PASSWORD>")]
        [Description("Password or token for an authenticated feed (user file only; needs -g).")]
        public string? Password { get; init; }

        [CommandOption("--store-password-in-clear-text")]
        [Description("Store the password unencrypted. Required outside Windows.")]
        public bool StorePasswordInClearText { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = settings.Global ? NuGetFeedConfig.UserPath() : NuGetFeedConfig.ProjectPath();

        if (!settings.Global && (settings.Username is not null || settings.Password is not null))
        {
            throw new UserInputException(
                "Credentials can't go in the project file (.agents/nuget.config is meant to be committed). " +
                "Add the feed to the project without them, and store them in your user file with: " +
                $"agentskills-cli feed add {settings.Url} -g --username <user> --password <token>");
        }

        var (url, name) = Normalize(settings.Url, settings.Name, path);
        NuGetFeedConfig.AddFeed(path, name, url, settings.Username, settings.Password, settings.StorePasswordInClearText);

        _console.MarkupLine($"[green]Added NuGet feed[/] {Markup.Escape(name)} [grey]({Markup.Escape(url)})[/] to {Markup.Escape(ConfigDisplay.ShortPath(path))}");
        if (settings.Username is not null)
        {
            _console.MarkupLine("[grey]Credentials saved in your user file.[/]");
        }
        return 0;
    }

    /// <summary>
    /// A local folder is stored relative to the config file when that's possible, so a committed
    /// project file works on every clone. The default name is the host or folder name.
    /// </summary>
    internal static (string Url, string Name) Normalize(string input, string? name, string configPath)
    {
        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return (value, string.IsNullOrWhiteSpace(name) ? uri.Host : name.Trim());
        }

        var full = Path.GetFullPath(value);
        if (!Directory.Exists(full))
        {
            throw new UserInputException($"'{input}' is neither an http(s) URL nor an existing folder.");
        }
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var relative = Path.GetRelativePath(configDir, full);
        var stored = Path.IsPathRooted(relative) ? full : relative;
        return (stored, string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : name.Trim());
    }
}

// ─── feed list ────────────────────────────────────────────────────────────────

public sealed class FeedListCommand : Command<FeedListCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public FeedListCommand(IAnsiConsole console) => _console = console ?? throw new ArgumentNullException(nameof(console));

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Write the feeds as JSON to stdout.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var feeds = NuGetFeedConfig.List();

        if (settings.Json)
        {
            JsonOutput.Write(_console, feeds.Select(f => new
            {
                name = f.Name,
                source = f.Source,
                enabled = f.Enabled,
                hasCredentials = f.HasCredentials,
                definedIn = f.DefinedIn,
            }));
            return 0;
        }

        if (feeds.Count == 0)
        {
            _console.MarkupLine("[grey]No NuGet feeds configured. Add one with `agentskills-cli feed add <url>`.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded).LeftAligned().Title("[bold]NuGet feeds, in the order they're tried[/]");
        table.AddColumns("Name", "Source", "Enabled", "Credentials", "Defined in");
        foreach (var f in feeds)
        {
            table.AddRow(
                Markup.Escape(f.Name),
                Markup.Escape(f.Source),
                f.Enabled ? "[green]yes[/]" : "[grey]no[/]",
                f.HasCredentials ? "yes" : "[grey]-[/]",
                $"[grey]{Markup.Escape(ConfigDisplay.ShortPath(f.DefinedIn))}[/]");
        }
        _console.Write(table);
        _console.MarkupLine("[grey]Credential providers (e.g. Azure Artifacts) can supply credentials that aren't shown here.[/]");
        return 0;
    }
}

// ─── feed remove ──────────────────────────────────────────────────────────────

public sealed class FeedRemoveCommand : Command<FeedRemoveCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public FeedRemoveCommand(IAnsiConsole console) => _console = console ?? throw new ArgumentNullException(nameof(console));

    public sealed class Settings : ConfigScopeSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name of the feed to remove.")]
        public string Name { get; init; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = settings.Global ? NuGetFeedConfig.UserPath() : NuGetFeedConfig.ProjectPath();

        if (NuGetFeedConfig.RemoveFeed(path, settings.Name))
        {
            _console.MarkupLine($"[green]Removed NuGet feed[/] {Markup.Escape(settings.Name)} from {Markup.Escape(ConfigDisplay.ShortPath(path))}");
            return 0;
        }

        var elsewhere = NuGetFeedConfig.List().FirstOrDefault(f => string.Equals(f.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        _console.MarkupLine(elsewhere?.DefinedIn is { } where
            ? $"[yellow]'{Markup.Escape(settings.Name)}' isn't in {Markup.Escape(ConfigDisplay.ShortPath(path))}; it's defined in {Markup.Escape(ConfigDisplay.ShortPath(where))}.[/]"
            : $"[yellow]No NuGet feed named '{Markup.Escape(settings.Name)}' in {Markup.Escape(ConfigDisplay.ShortPath(path))}.[/]");
        return 1;
    }
}
