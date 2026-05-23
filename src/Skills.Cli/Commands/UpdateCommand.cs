using System.ComponentModel;
using Skills.Agents;
using Skills.Install;
using Skills.SkillModel;
using Skills.Sources;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Skills.Commands;

public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly IInstallService _installer;
    private readonly Skills.Hosting.CommandCancellation _cancellation;

    public UpdateCommand(IInstallService installer, Skills.Hosting.CommandCancellation cancellation)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SKILLS]")]
        [Description("Only check these skill names. Omit to check everything tracked in the lock.")]
        public string[] Skills { get; init; } = [];

        [CommandOption("-g|--global")]
        [Description("Limit to globally installed skills.")]
        public bool Global { get; init; }

        [CommandOption("-p|--project")]
        [Description("Limit to project-installed skills.")]
        public bool Project { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Auto-confirm any updates instead of prompting.")]
        public bool Yes { get; init; }

        [CommandOption("--check")]
        [Description("Only report drifted skills; don't install updates.")]
        public bool CheckOnly { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var scope = ResolveScope(settings);
        AnsiConsole.MarkupLine($"[grey]Checking for updates ({scope})...[/]");

        var checks = new List<UpdateCheck>();

        if (scope is UpdateScope.Global or UpdateScope.Both)
        {
            var globalLock = SkillLock.Load();
            foreach (var (name, entry) in globalLock.Skills)
            {
                if (!Matches(settings.Skills, name)) continue;
                checks.Add(new UpdateCheck(name, "global", entry.Source, entry.SourceType, entry.SourceUrl,
                    entry.Ref, entry.SkillPath, entry.SkillFolderHash));
            }
        }

        if (scope is UpdateScope.Project or UpdateScope.Both)
        {
            var localLock = LocalLock.Load();
            foreach (var (name, entry) in localLock.Skills)
            {
                if (!Matches(settings.Skills, name)) continue;
                // Project lock has no SourceUrl; reconstruct via the source string when it's owner/repo.
                checks.Add(new UpdateCheck(name, "project", entry.Source, entry.SourceType, GuessUrl(entry.SourceType, entry.Source),
                    entry.Ref, entry.SkillPath, SkillFolderHash: null));
            }
        }

        if (checks.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing to check - no tracked skills found.[/]");
            return 0;
        }

        var checkable = new List<UpdateCheck>();
        var skipped = new List<(UpdateCheck Check, string Reason)>();
        foreach (var check in checks)
        {
            var reason = WhySkip(check);
            if (reason is null) checkable.Add(check);
            else skipped.Add((check, reason));
        }

        var available = new List<UpdateAvailable>();
        var bySource = checkable
            .Where(c => c.Scope == "global")
            .GroupBy(c => c.Source, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySource)
        {
            var tree = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"GitHub Trees API for {group.Key}...",
                    _ => GitHubApi.FetchTreeAsync(group.Key, group.First().Ref, GitHubApi.ResolveToken));

            if (tree is null)
            {
                foreach (var c in group)
                {
                    skipped.Add((c, "Could not fetch tree (rate-limited, private, or moved)"));
                }
                continue;
            }

            foreach (var check in group)
            {
                if (check.SkillPath is null || check.SkillFolderHash is null) continue;
                var latest = GitHubApi.GetFolderHashFromTree(tree, check.SkillPath);
                if (latest is null) continue;
                if (!string.Equals(latest, check.SkillFolderHash, StringComparison.Ordinal))
                {
                    available.Add(new UpdateAvailable(check, latest));
                }
            }
        }

        // For project scope we don't track folder hashes, so report which skills have a
        // re-installable source and let the user opt in.
        var projectRefreshable = checkable
            .Where(c => c.Scope == "project" && c.SourceType is "github" or "gitlab" or "nuget")
            .ToList();

        Render(available, projectRefreshable, skipped);

        if (available.Count == 0 && projectRefreshable.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All tracked skills are up to date.[/]");
            return 0;
        }

        if (settings.CheckOnly)
        {
            AnsiConsole.MarkupLine($"[yellow]{available.Count + projectRefreshable.Count} update(s) available - re-run without --check to install.[/]");
            return 0;
        }

        if (!settings.Yes && AnsiConsole.Profile.Capabilities.Interactive)
        {
            if (!AnsiConsole.Confirm("Install the updates above now?", defaultValue: true))
            {
                return 0;
            }
        }

        var failures = 0;
        foreach (var update in available)
        {
            AnsiConsole.MarkupLine($"[cyan]Updating {Markup.Escape(update.Check.Name)} (global)...[/]");
            var ok = await ReinstallAsync(update.Check, global: true);
            if (!ok) failures++;
        }

        foreach (var refresh in projectRefreshable)
        {
            AnsiConsole.MarkupLine($"[cyan]Refreshing {Markup.Escape(refresh.Name)} (project)...[/]");
            var ok = await ReinstallAsync(refresh, global: false);
            if (!ok) failures++;
        }

        return failures == 0 ? 0 : 1;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private enum UpdateScope { Project, Global, Both }

    private sealed record UpdateCheck(
        string Name,
        string Scope,
        string Source,
        string SourceType,
        string? SourceUrl,
        string? Ref,
        string? SkillPath,
        string? SkillFolderHash);

    private sealed record UpdateAvailable(UpdateCheck Check, string LatestHash);

    private static UpdateScope ResolveScope(Settings s)
    {
        if (s.Global && s.Project) return UpdateScope.Both;
        if (s.Global) return UpdateScope.Global;
        if (s.Project) return UpdateScope.Project;
        // Auto: project if a local lock or .agents/skills exists, otherwise global.
        if (File.Exists(LocalLock.GetLockPath()) || Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), ".agents", "skills")))
        {
            return UpdateScope.Both;
        }
        return UpdateScope.Global;
    }

    private static bool Matches(string[] filter, string name) =>
        filter.Length == 0 || filter.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

    private static string? WhySkip(UpdateCheck c) => c.SourceType switch
    {
        "local" => "Local path - re-install manually to refresh",
        "well-known" => "Well-known endpoint - re-install manually to refresh",
        "nuget" => "NuGet - re-install with a newer version to refresh",
        "git" => "Generic git URL - re-install manually to refresh",
        "gitlab" => "GitLab - Trees API not implemented in v1",
        "github" when c.SkillFolderHash is null or "" => "Missing folder hash (installed before update tracking)",
        "github" when c.SkillPath is null => "Missing skill path (installed before update tracking)",
        _ => null,
    };

    private static string? GuessUrl(string sourceType, string source) => sourceType switch
    {
        "github" when source.Contains('/', StringComparison.Ordinal) => $"https://github.com/{source.Split('@')[0]}.git",
        _ => null,
    };

    private static void Render(
        IReadOnlyList<UpdateAvailable> available,
        IReadOnlyList<UpdateCheck> projectRefreshable,
        IReadOnlyList<(UpdateCheck Check, string Reason)> skipped)
    {
        if (available.Count > 0)
        {
            var table = new Table().Border(TableBorder.Rounded).LeftAligned().Title("[bold]Updates available[/]");
            table.AddColumns("Skill", "Scope", "Source", "Old", "New");
            foreach (var u in available)
            {
                table.AddRow(
                    Markup.Escape(u.Check.Name),
                    u.Check.Scope,
                    Markup.Escape(u.Check.Source),
                    $"[grey]{Markup.Escape(Short(u.Check.SkillFolderHash))}[/]",
                    $"[green]{Markup.Escape(Short(u.LatestHash))}[/]");
            }
            AnsiConsole.Write(table);
        }

        if (projectRefreshable.Count > 0)
        {
            var t = new Table().Border(TableBorder.Rounded).LeftAligned().Title("[bold]Project skills to refresh[/]");
            t.AddColumns("Skill", "Source", "Ref");
            foreach (var p in projectRefreshable)
            {
                t.AddRow(Markup.Escape(p.Name), Markup.Escape(p.Source), Markup.Escape(p.Ref ?? string.Empty));
            }
            AnsiConsole.Write(t);
        }

        if (skipped.Count > 0)
        {
            var t = new Table().Border(TableBorder.Rounded).LeftAligned().Title("[bold]Skipped[/]");
            t.AddColumns("Skill", "Scope", "Reason");
            foreach (var (check, reason) in skipped)
            {
                t.AddRow(
                    Markup.Escape(check.Name),
                    check.Scope,
                    $"[grey]{Markup.Escape(reason)}[/]");
            }
            AnsiConsole.Write(t);
        }
    }

    private static string Short(string? sha) =>
        string.IsNullOrEmpty(sha) ? "-" : sha.Length > 7 ? sha[..7] : sha;

    private async Task<bool> ReinstallAsync(UpdateCheck check, bool global)
    {
        var sourceSpec = check.SourceType switch
        {
            "github" => string.IsNullOrEmpty(check.Ref) ? check.Source : $"{check.Source}#{check.Ref}",
            "nuget" => check.Source.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase)
                ? check.Source : $"nuget:{check.Source}",
            _ => check.SourceUrl ?? check.Source,
        };

        var agents = AgentRegistry.DetectInstalled();
        if (agents.Count == 0) agents = [AgentRegistry.Get("universal")];

        try
        {
            var summary = await _installer.InstallAsync(new InstallRequest(
                SourceInput: sourceSpec,
                SkillFilter: [check.Name],
                Agents: agents,
                Global: global,
                Mode: InstallMode.Copy,
                ResolveOptions: ResolveOptions.Default,
                IncludeInternal: true), _cancellation.Token);

            return summary.Outcomes.All(o => o.Result.Success);
        }
        catch (SkillsException ex)
        {
            AnsiConsole.MarkupLine($"  [red]failed:[/] {Markup.Escape(ex.Message)}");
            return false;
        }
    }
}
