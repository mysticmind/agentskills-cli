using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.Sources;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

/// <summary>Options shared by <c>update</c> and <c>outdated</c>.</summary>
public class UpdateCheckSettings : CommandSettings
{
    [CommandArgument(0, "[TARGETS]")]
    [Description("Skill names and/or sources (any shape `add` accepts: owner/repo, GitLab/git URL, " +
                 "NuGet or npm package id, local path). A package with @version moves to exactly that version. " +
                 "Omit to check everything tracked in the lock files.")]
    public string[] Targets { get; init; } = [];

    [CommandOption("-g|--global")]
    [Description("Limit to globally installed skills.")]
    public bool Global { get; init; }

    [CommandOption("-p|--project")]
    [Description("Limit to project-installed skills.")]
    public bool Project { get; init; }

    [CommandOption("-a|--agent <AGENTS>")]
    [Description("Only skills installed for these agents (and reinstall only for them).")]
    public string[] Agents { get; init; } = [];

    [CommandOption("--prerelease")]
    [Description("Consider prerelease NuGet/npm versions.")]
    public bool Prerelease { get; init; }

    [CommandOption("--version-lock <LOCK>")]
    [Description("Limit NuGet/npm updates: none (default, any newer version), major (stay on the current major), " +
                 "minor (stay on the current major.minor).")]
    public string? VersionLock { get; init; }

    [CommandOption("--json")]
    [Description("Write the result as JSON to stdout.")]
    public bool Json { get; init; }

    public override ValidationResult Validate() =>
        VersionLock is null || TryParseLock(VersionLock, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error($"Invalid --version-lock '{VersionLock}'. Valid: none, major, minor.");

    internal static bool TryParseLock(string? value, out Install.VersionLock result)
    {
        result = Install.VersionLock.None;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "none": return true;
            case "major": result = Install.VersionLock.Major; return true;
            case "minor": result = Install.VersionLock.Minor; return true;
            default: return false;
        }
    }

    [CommandOption("--exit-code")]
    [Description("When only checking: exit 1 if updates are available, 2 if any skill could not be checked.")]
    public bool ExitCode { get; init; }

    [CommandOption("--nuget-source <URL>")]
    [Description("Check NuGet packages against this feed only.")]
    public string? NuGetSource { get; init; }

    [CommandOption("--npm-registry <URL>")]
    [Description("Check npm packages against this registry.")]
    public string? NpmRegistry { get; init; }
}

public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly UpdateService _updates;
    private readonly IAnsiConsole _console;
    private readonly Hosting.CommandCancellation _cancellation;

    public UpdateCommand(UpdateService updates, IAnsiConsole console, Hosting.CommandCancellation cancellation)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
    }

    public sealed class Settings : UpdateCheckSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Install updates without prompting.")]
        public bool Yes { get; init; }

        [CommandOption("--check")]
        [Description("Only report; don't install (same as `outdated`).")]
        public bool CheckOnly { get; init; }

        [CommandOption("--force")]
        [Description("Reinstall matched skills even when no change is detected.")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Json && !settings.CheckOnly && !settings.Yes)
        {
            throw new UserInputException("--json cannot prompt for confirmation. Add --check to only report, or --yes to install.");
        }

        var runner = new UpdateRunner(_updates, _console, _cancellation);
        return await runner.RunAsync(settings, checkOnly: settings.CheckOnly, force: settings.Force, yes: settings.Yes)
            .ConfigureAwait(false);
    }
}

/// <summary><c>outdated</c>: report what <c>update</c> would change, install nothing.</summary>
public sealed class OutdatedCommand : AsyncCommand<UpdateCheckSettings>
{
    private readonly UpdateService _updates;
    private readonly IAnsiConsole _console;
    private readonly Hosting.CommandCancellation _cancellation;

    public OutdatedCommand(UpdateService updates, IAnsiConsole console, Hosting.CommandCancellation cancellation)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
    }

    public override Task<int> ExecuteAsync(CommandContext context, UpdateCheckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new UpdateRunner(_updates, _console, _cancellation)
            .RunAsync(settings, checkOnly: true, force: false, yes: false);
    }
}

internal sealed class UpdateRunner(UpdateService updates, IAnsiConsole console, Hosting.CommandCancellation cancellation)
{
    public async Task<int> RunAsync(UpdateCheckSettings settings, bool checkOnly, bool force, bool yes)
    {
        var (includeGlobal, includeProject) = ResolveScope(settings);
        var agents = settings.Agents.Length > 0 ? AgentRegistry.GetMany(settings.Agents) : null;

        var query = new UpdateQuery(
            Targets: settings.Targets,
            IncludeGlobal: includeGlobal,
            IncludeProject: includeProject,
            Agents: agents,
            IncludePrerelease: settings.Prerelease,
            Force: force,
            ResolveOptions: new ResolveOptions(NuGetSource: settings.NuGetSource, NpmRegistry: settings.NpmRegistry),
            VersionLock: UpdateCheckSettings.TryParseLock(settings.VersionLock, out var versionLock) ? versionLock : VersionLock.None);

        var scopeLabel = includeGlobal && includeProject ? "global + project" : includeGlobal ? "global" : "project";
        if (!settings.Json) console.MarkupLine($"[grey]Checking for updates ({scopeLabel})...[/]");

        using var plan = await WithStatus(settings.Json, "Checking...", progress =>
            updates.CheckAsync(query, progress, cancellation.Token)).ConfigureAwait(false);

        if (plan.Items.Count == 0)
        {
            if (settings.Json) JsonOutput.Write(console, Report(plan));
            else
            {
                console.MarkupLine(settings.Targets.Length > 0
                    ? $"[yellow]No tracked skills matched {Markup.Escape(string.Join(", ", settings.Targets))}.[/]"
                    : "[grey]Nothing to check - no tracked skills found.[/]");
            }
            return settings.Targets.Length > 0 ? 1 : 0;
        }

        var available = plan.Count(UpdateStatus.UpdateAvailable);
        if (checkOnly)
        {
            Emit(settings, plan);
            if (!settings.Json && available > 0)
            {
                console.MarkupLine("[grey]Run `agentskills-cli update` to install.[/]");
            }
            if (!settings.ExitCode) return 0;
            if (plan.Count(UpdateStatus.Failed) > 0) return 2;
            return available > 0 ? 1 : 0;
        }

        if (available == 0)
        {
            // Nothing to install, but still prune stale lock entries.
            if (plan.Items.Any(i => i.StaleLockEntry))
            {
                await updates.ApplyAsync(plan, query, progress: null, cancellation.Token).ConfigureAwait(false);
            }
            Emit(settings, plan);
            return plan.Count(UpdateStatus.Failed) > 0 ? 1 : 0;
        }

        if (!settings.Json)
        {
            UpdateReportRenderer.Render(console, plan.Items);
            if (!yes && console.Profile.Capabilities.Interactive &&
                !console.Confirm($"Install {available} update(s) now?", defaultValue: true))
            {
                console.MarkupLine("[grey]Aborted.[/]");
                return 0;
            }
        }

        await WithStatus(settings.Json, "Updating...", progress =>
            updates.ApplyAsync(plan, query, progress, cancellation.Token)).ConfigureAwait(false);

        if (settings.Json) JsonOutput.Write(console, Report(plan));
        else UpdateReportRenderer.Render(console, plan.Items, afterApply: true);

        return plan.Count(UpdateStatus.Failed) > 0 ? 1 : 0;
    }

    private void Emit(UpdateCheckSettings settings, UpdatePlan plan)
    {
        if (settings.Json) JsonOutput.Write(console, Report(plan));
        else UpdateReportRenderer.Render(console, plan.Items);
    }

    private static object Report(UpdatePlan plan) => new
    {
        items = plan.Items,
        summary = new
        {
            upToDate = plan.Count(UpdateStatus.UpToDate),
            updateAvailable = plan.Count(UpdateStatus.UpdateAvailable),
            updated = plan.Count(UpdateStatus.Updated),
            skipped = plan.Count(UpdateStatus.Skipped),
            failed = plan.Count(UpdateStatus.Failed),
        },
    };

    private async Task<T> WithStatus<T>(bool quiet, string initial, Func<IProgress<string>, Task<T>> work)
    {
        if (quiet || !console.Profile.Capabilities.Interactive)
        {
            return await work(new Progress<string>(_ => { })).ConfigureAwait(false);
        }
        return await console.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(initial, ctx => work(new SyncProgress(m => ctx.Status(Markup.Escape(m)))))
            .ConfigureAwait(false);
    }

    private async Task WithStatus(bool quiet, string initial, Func<IProgress<string>, Task> work) =>
        await WithStatus<bool>(quiet, initial, async p => { await work(p).ConfigureAwait(false); return true; })
            .ConfigureAwait(false);

    private static (bool Global, bool Project) ResolveScope(UpdateCheckSettings s)
    {
        if (s.Global || s.Project) return (s.Global, s.Project);
        // Auto: include the project when this directory has a project lock or project installs.
        var hasProject = File.Exists(LocalLock.GetLockPath()) ||
                         Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), ".agents", "skills"));
        return (true, hasProject);
    }

    /// <summary>Synchronous IProgress (System.Progress posts to the thread pool, which reorders spinner text).</summary>
    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}

internal static class UpdateReportRenderer
{
    public static void Render(IAnsiConsole console, IReadOnlyList<UpdateItem> items, bool afterApply = false)
    {
        var interesting = items.Where(i => i.Status != UpdateStatus.UpToDate).ToList();
        if (interesting.Count > 0)
        {
            var table = new Table().Border(TableBorder.Rounded).LeftAligned()
                .Title(afterApply ? "[bold]Update results[/]" : "[bold]Update check[/]");
            table.AddColumns("Skill", "Scope", "Source", "Current", "Latest", "Status");

            foreach (var item in interesting
                         .OrderBy(i => i.Status)
                         .ThenBy(i => i.Scope, StringComparer.Ordinal)
                         .ThenBy(i => i.Name, StringComparer.Ordinal))
            {
                table.AddRow(
                    Markup.Escape(item.Name),
                    item.Scope == "global" ? "[blue]global[/]" : "[green]project[/]",
                    $"{Markup.Escape(item.Source)} [grey]({Markup.Escape(item.SourceType)})[/]",
                    $"[grey]{Markup.Escape(item.Current ?? "-")}[/]",
                    Markup.Escape(item.Latest ?? "-"),
                    StatusMarkup(item));
            }
            console.Write(table);
        }

        var upToDate = items.Count(i => i.Status == UpdateStatus.UpToDate);
        var parts = new List<string>();
        if (upToDate > 0) parts.Add($"[green]{upToDate} up to date[/]");
        Add(parts, items, UpdateStatus.UpdateAvailable, "yellow", "update(s) available");
        Add(parts, items, UpdateStatus.Updated, "green", "updated");
        Add(parts, items, UpdateStatus.Skipped, "grey", "skipped");
        Add(parts, items, UpdateStatus.Failed, "red", "failed");
        console.MarkupLine(string.Join("[grey], [/]", parts));
    }

    private static void Add(List<string> parts, IReadOnlyList<UpdateItem> items, UpdateStatus status, string color, string label)
    {
        var n = items.Count(i => i.Status == status);
        if (n > 0) parts.Add($"[{color}]{n} {label}[/]");
    }

    private static string StatusMarkup(UpdateItem item)
    {
        var reason = string.IsNullOrEmpty(item.Reason) ? string.Empty : $" [grey]{Markup.Escape(item.Reason)}[/]";
        return item.Status switch
        {
            UpdateStatus.UpdateAvailable => "[yellow]update available[/]" + reason,
            UpdateStatus.Updated => "[green]updated[/]" + reason,
            UpdateStatus.Skipped => "[grey]skipped[/]" + reason,
            UpdateStatus.Failed => "[red]failed[/]" + reason,
            _ => "[green]up to date[/]",
        };
    }
}
